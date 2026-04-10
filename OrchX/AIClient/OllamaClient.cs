using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using OrchX.Tools;
using OrchX.UI;
using OrchX.AIClient.Converters;
using OrchX.AIClient.Models;

namespace OrchX.AIClient
{
    /// <summary>
    /// 用於連接本地端 Ollama 以執行模型 (例如 gemma4) 的 AI Client。
    /// 實作 IAIClient 介面。
    /// </summary>
    public class OllamaClient : IAIClient
    {
        private readonly string _endpoint;
        private readonly string _model;
        public string ModelName => _model;
        public string ProviderName => "ollama";
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public OllamaClient(string endpoint = "http://localhost:11434", string model = "gemma4")
        {
            _endpoint = endpoint.TrimEnd('/');
            _model = model;
        }

        public async Task<string> GenerateContentAsync(GenerateContentRequest request, System.Threading.CancellationToken cancellationToken = default)
        {
            var url = $"{_endpoint}/api/chat";

            var unifiedRequest = AIRequestConverter.ToUnifiedRequest(request, _model);

            var requestBody = new Dictionary<string, object>
            {
                { "model", unifiedRequest.Model },
                { "messages", unifiedRequest.Messages },
                { "stream", unifiedRequest.Stream ?? false }
            };

            if (unifiedRequest.Tools != null && unifiedRequest.Tools.Count > 0)
            {
                requestBody["tools"] = unifiedRequest.Tools;
            }

            var json = JsonTools.Serialize(requestBody);
            TestRecordManager.RecordRequest(json);
            
            int maxRetries = 3;
            int currentRetry = 0;
            int delayMs = 2000;

            while (true)
            {
                System.Net.HttpStatusCode statusCode = 0;
                string responseJson = null;
                Exception requestEx = null;

                try
                {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = await _httpClient.PostAsync(url, content, cancellationToken))
                    {
                        statusCode = response.StatusCode;
                        responseJson = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            TestRecordManager.RecordResponse(responseJson);

                            var dict = JsonTools.Deserialize<Dictionary<string, object>>(responseJson);
                            if (dict == null || !dict.ContainsKey("message"))
                            {
                                throw new FormatException("Ollama 成功回應但格式異常，找不到 message 欄位。");
                            }
                            return responseJson;
                        }
                    }
                }
                catch (TaskCanceledException) { throw; }
                catch (FormatException) { throw; }
                catch (Exception ex) { requestEx = ex; }

                if (requestEx == null)
                {
                    UsageLogger.LogApiError($"Ollama API Error: {statusCode}", json, responseJson);
                }

                if (currentRetry < maxRetries)
                {
                    currentRetry++;
                    string errMsg = requestEx != null ? requestEx.Message : $"HTTP {statusCode}";
                    Console.WriteLine($"\n[OllamaClient] API 失敗 ({errMsg})，等待 {delayMs / 1000.0} 秒後重試 ({currentRetry}/{maxRetries})...");
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                    continue;
                }

                if (requestEx != null)
                {
                    throw new Exception($"與 Ollama 連線失敗，已達最大重試次數 ({maxRetries})：{requestEx.Message}", requestEx);
                }
                throw new Exception($"Ollama API Error: {statusCode}\n{responseJson}");
            }
        }

        public object CreateSimpleContents(string prompt)
        {
            return new[] { BuildMessageContent("user", prompt) };
        }

        public System.Collections.ArrayList ExtractResponseParts(Dictionary<string, object> data, out Dictionary<string, object> modelContent)
        {
            modelContent = null;
            if (data == null) return null;

            string jsonResponse = JsonTools.Serialize(data);
            var unifiedResponse = AIResponseConverter.ToUnifiedResponse(jsonResponse, "ollama");
            if (unifiedResponse == null) return null;

            modelContent = new Dictionary<string, object>
            {
                { "role", "model" }
            };

            var parts = new System.Collections.ArrayList();

            if (!string.IsNullOrEmpty(unifiedResponse.Content))
            {
                parts.Add(new Dictionary<string, object> { { "text", unifiedResponse.Content } });
            }

            if (unifiedResponse.ToolCalls != null)
            {
                foreach (var tc in unifiedResponse.ToolCalls)
                {
                    var functionCall = new Dictionary<string, object>
                    {
                        { "functionCall", new Dictionary<string, object>
                            {
                                { "name", tc.FunctionName },
                                { "args", tc.Arguments ?? new Dictionary<string, object>() }
                            }
                        }
                    };
                    parts.Add(functionCall);
                }
            }

            modelContent["parts"] = parts;
            return parts;
        }

        public string ExtractTextFromResponseData(Dictionary<string, object> data)
        {
            if (data == null) return null;
            string jsonResponse = JsonTools.Serialize(data);
            var unifiedResponse = AIResponseConverter.ToUnifiedResponse(jsonResponse, "ollama");
            return unifiedResponse?.Content;
        }

        public object[] DefineTools(params object[] functionDeclarations)
        {
            var tools = new List<object>();
            foreach (var fDecl in functionDeclarations)
            {
                tools.Add(new
                {
                    type = "function",
                    function = fDecl
                });
            }
            return tools.ToArray();
        }

        public object CreateFunctionDeclaration(string name, string description, object parameters)
        {
            return new { name, description, parameters };
        }

        /// <summary>
        /// 獲取可用模型列表 (對應 Ollama API)
        /// </summary>
        public async Task<string> ListModelsAsync()
        {
            var url = $"{_endpoint}/api/tags";
            using var response = await _httpClient.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        public (int promptTokens, int candidateTokens, int totalTokens) ExtractTokenUsage(Dictionary<string, object> data)
        {
            if (data == null) return (0, 0, 0);
            string jsonResponse = JsonTools.Serialize(data);
            var unifiedResponse = AIResponseConverter.ToUnifiedResponse(jsonResponse, "ollama");
            if (unifiedResponse?.Usage != null)
            {
                return (unifiedResponse.Usage.PromptTokens, unifiedResponse.Usage.CandidateTokens, unifiedResponse.Usage.TotalTokens);
            }
            return (0, 0, 0);
        }

        public async Task<(bool hasFunctionCall, List<object> toolResponseParts)> ProcessModelPartsAsync(
            System.Collections.ArrayList parts, 
            IAgentUI ui, 
            string currentModelName,
            Func<string, Dictionary<string, object>, Task<string>> toolExecutor,
            System.Threading.CancellationToken cancellationToken = default)
        {
            bool hasFunctionCall = false;
            var toolResponseParts = new List<object>();

            foreach (var rawPart in parts)
            {
                if (rawPart is not Dictionary<string, object> part) continue;

                if (part.ContainsKey("text"))
                {
                    string text = part["text"]?.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        ui.ReportTextResponse(text, currentModelName);
                    }
                }

                if (part.ContainsKey("functionCall"))
                {
                    hasFunctionCall = true;
                    var call = part["functionCall"] as Dictionary<string, object>;
                    if (call == null) continue;
                    string funcName = call["name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(funcName))
                    {
                        UsageLogger.LogError("ProcessModelPartsAsync: functionCall 缺少有效的 name，已跳過。");
                        continue;
                    }
                    var argsDict = (call.ContainsKey("args") ? call["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();

                    ui.ReportToolCall(funcName, JsonTools.Serialize(argsDict));

                    string result = await toolExecutor(funcName, argsDict);
                    UsageLogger.LogAction(funcName, result);

                    if (result == "[SKIP_FUNCTION_RESPONSE]")
                    {
                        ui.ReportToolResult("已成功處理工具調用 (無返回值)。");
                        continue;
                    }

                    var resultParts = BuildToolResponseParts(funcName, result);
                    toolResponseParts.AddRange(resultParts);

                    ui.ReportToolResult(result);
                }
            }

            return (hasFunctionCall, toolResponseParts);
        }

        public bool TryGetTextFromPart(object partObj, out string text)
        {
            text = null;
            if (partObj is Dictionary<string, object> part && part.ContainsKey("text"))
            {
                text = part["text"]?.ToString();
                return true;
            }
            return false;
        }

        public bool TryGetFunctionCallFromPart(object partObj, out string functionName, out Dictionary<string, object> args)
        {
            functionName = null;
            args = null;
            if (partObj is Dictionary<string, object> part && part.ContainsKey("functionCall"))
            {
                if (part["functionCall"] is Dictionary<string, object> call && call.ContainsKey("name"))
                {
                    functionName = call["name"]?.ToString();
                    args = (call.ContainsKey("args") ? call["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
                    return true;
                }
            }
            return false;
        }

        public bool TryGetFunctionResponseFromPart(object partObj, out string functionName, out string responseContent)
        {
            functionName = null;
            responseContent = null;
            if (partObj is Dictionary<string, object> part && part.ContainsKey("functionResponse"))
            {
                if (part["functionResponse"] is Dictionary<string, object> resp && resp.ContainsKey("name"))
                {
                    functionName = resp["name"]?.ToString();
                    if (resp.ContainsKey("response") && resp["response"] is Dictionary<string, object> respBody && respBody.ContainsKey("content"))
                    {
                        responseContent = respBody["content"]?.ToString();
                    }
                    return true;
                }
            }
            return false;
        }

        public bool TryGetRoleAndPartsFromMessage(object messageObj, out string role, out IEnumerable<object> parts)
        {
            role = null;
            parts = null;
            
            Dictionary<string, object> dict = null;

            if (messageObj is Dictionary<string, object> d)
            {
                dict = d;
            }
            else
            {
                string serialized = JsonTools.Serialize(messageObj);
                dict = JsonTools.Deserialize<Dictionary<string, object>>(serialized);
            }

            if (dict != null)
            {
                if (dict.ContainsKey("role"))
                {
                    role = dict["role"]?.ToString();
                }
                if (dict.ContainsKey("parts") && dict["parts"] is System.Collections.IEnumerable enumerableParts)
                {
                    var list = new List<object>();
                    foreach (var p in enumerableParts)
                    {
                        list.Add(p);
                    }
                    parts = list;
                }
                
                return role != null && parts != null;
            }

            return false;
        }

        public object BuildToolResponsePart(string funcName, string result)
        {
            return new
            {
                functionResponse = new
                {
                    name = funcName,
                    response = new { content = result }
                }
            };
        }

        public object BuildFunctionMessageContent(List<object> toolResponseParts)
        {
            return new { role = "function", parts = toolResponseParts };
        }

        public object BuildMessageContent(string role, string text)
        {
            return new { role = role, parts = new[] { new { text = text } } };
        }

        public object BuildImageMessageContent(string role, string text, string mimeType, string base64Data)
        {
            return new
            {
                role = role,
                parts = new object[]
                {
                    new { text = text },
                    new
                    {
                        inlineData = new
                        {
                            mimeType = mimeType,
                            data = base64Data
                        }
                    }
                }
            };
        }

        private List<object> BuildToolResponseParts(string funcName, string result)
        {
            var parts = new List<object>();
            parts.Add(BuildToolResponsePart(funcName, result));
            return parts;
        }
    }
}
