using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.AIClient
{
    public class GenerateContentRequest
    {
        public object Contents { get; set; }
        public List<object> Tools { get; set; }
        public string SystemInstruction { get; set; }
        public string MockProviderName { get; set; }
    }

    public class GeminiClient : IAIClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        public string ModelName => _model;
        private static readonly HttpClient _httpClient = new HttpClient();

        public GeminiClient(string apiKey, string model = "gemini-2.5-flash")
        {
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> GenerateContentAsync(GenerateContentRequest request, System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                string providerName = request.MockProviderName ?? "Gemini";
                return MockDataManager.GetMockResponse(providerName);
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            object systemInstObj = null;
            if (!string.IsNullOrEmpty(request.SystemInstruction))
            {
                systemInstObj = new { parts = new[] { new { text = request.SystemInstruction } } };
            }

            var requestBody = new
            {
                contents = request.Contents,
                tools = request.Tools,
                system_instruction = systemInstObj
            };

            var json = JsonTools.Serialize(requestBody);
            int maxRetries = 3;
            int currentRetry = 0;
            int delayMs = 2000;

            HttpResponseMessage response = null;
            string responseJson = null;

            while (true)
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    response = await _httpClient.PostAsync(url, content, cancellationToken);
                    responseJson = await response.Content.ReadAsStringAsync();
                }

                if (response.IsSuccessStatusCode)
                {
                    if (MockDataManager.IsRecordingMockData)
                    {
                        string providerName = request.MockProviderName ?? "gemini";
                        MockDataManager.RecordMockResponse(providerName, responseJson);
                    }
                    break;
                }

                // 紀錄詳細錯誤資訊
                UsageLogger.LogApiError($"API Error: {response.StatusCode}", json, responseJson);

                if ((int)response.StatusCode == 429)
                {
                    if (currentRetry < maxRetries)
                    {
                        currentRetry++;
                        Console.WriteLine($"\n[GeminiClient] 收到 HTTP 429 (Too Many Requests)，等待 {delayMs / 1000.0} 秒後進行第 {currentRetry} 次重試...");
                        await Task.Delay(delayMs);
                        delayMs *= 2; // 指數退避
                        continue;
                    }
                    else
                    {
                        throw new Exception(
                            "Gemini API Quota Exceeded (429).\n" +
                            "你超出的目前的 API 使用額度，或請求過於頻繁。\n" +
                            $"已達到最大重試次數 ({maxRetries} 次)。\n" +
                            "請檢查您的 Google AI Studio 方案或稍後再試。\n" +
                            "詳細錯誤資訊可至 logs 日誌中查看。"
                        );
                    }
                }
                
                // 處理模型不支援 Function Calling 的情況 (400 Bad Request)
                if ((int)response.StatusCode == 400 && 
                    (responseJson.Contains("Model does not support function calling") || 
                     responseJson.Contains("models not supported") ||
                     responseJson.Contains("not support tools") ||
                     responseJson.Contains("Function calling is not enabled")))
                {
                    throw new Exception(
                        $"目前使用的模型 ({_model}) 不支援 Function Calling 功能 (Tool Use)。\n" +
                        "請嘗試切換至其他模型 (例如 gemini-2.0-flash 或 gemini-2.5-flash)。"
                    );
                }

                throw new Exception($"Gemini API Error: {response.StatusCode}\n{responseJson}");
            }

            return responseJson;
        }

        // 輔助方法：將單純字串轉為 API 所需的 contents 格式
        public object CreateSimpleContents(string prompt)
        {
            return new[] { BuildMessageContent("user", prompt) };
        }

        public System.Collections.ArrayList ExtractResponseParts(Dictionary<string, object> data, out Dictionary<string, object> modelContent)
        {
            modelContent = null;
            if (data == null || !data.ContainsKey("candidates")) return null;
            
            var candidates = data["candidates"] as System.Collections.ArrayList;
            if (candidates == null || candidates.Count == 0) return null;

            var firstCandidate = candidates[0] as Dictionary<string, object>;
            if (firstCandidate == null || !firstCandidate.ContainsKey("content")) return null;

            modelContent = firstCandidate["content"] as Dictionary<string, object>;
            if (modelContent == null || !modelContent.ContainsKey("parts")) return null;

            return modelContent["parts"] as System.Collections.ArrayList;
        }

        public string ExtractTextFromResponseData(Dictionary<string, object> data)
        {
            var parts = ExtractResponseParts(data, out _);
            if (parts != null && parts.Count > 0)
            {
                var dictPart = parts[0] as Dictionary<string, object>;
                return dictPart?["text"]?.ToString();
            }
            return null;
        }

        /// <summary>
        /// 輔助方法：定義多個工具
        /// </summary>
        public object[] DefineTools(params object[] functionDeclarations)
        {
            return new[]
            {
                new
                {
                    function_declarations = functionDeclarations
                }
            };
        }

        /// <summary>
        /// 建立單個 Function Declaration 物件
        /// </summary>
        public object CreateFunctionDeclaration(string name, string description, object parameters)
        {
            return new
            {
                name = name,
                description = description,
                parameters = parameters
            };
        }

        /// <summary>
        /// 獲取可用模型列表
        /// </summary>
        public async Task<string> ListModelsAsync()
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }

        public (int promptTokens, int candidateTokens, int totalTokens) ExtractTokenUsage(Dictionary<string, object> data)
        {
            int promptTokens = 0, candidateTokens = 0, totalTokens = 0;
            if (data.ContainsKey("usageMetadata") && data["usageMetadata"] is Dictionary<string, object> usage)
            {
                if (usage.ContainsKey("promptTokenCount"))
                    promptTokens = Convert.ToInt32(usage["promptTokenCount"]);
                if (usage.ContainsKey("candidatesTokenCount"))
                    candidateTokens = Convert.ToInt32(usage["candidatesTokenCount"]);
                if (usage.ContainsKey("totalTokenCount"))
                    totalTokens = Convert.ToInt32(usage["totalTokenCount"]);
            }
            return (promptTokens, candidateTokens, totalTokens);
        }

        public void AppendFixedInfoToLastUserMessage(List<object> requestContents, string additionalInfo)
        {
            if (string.IsNullOrWhiteSpace(additionalInfo) || requestContents.Count == 0) return;

            try
            {
                var lastMessage = requestContents[requestContents.Count - 1];
                string serialized = JsonTools.Serialize(lastMessage);
                var lastDict = JsonTools.Deserialize<Dictionary<string, object>>(serialized);

                if (lastDict != null && lastDict.ContainsKey("role") && lastDict["role"]?.ToString() == "user")
                {
                    var reqParts = lastDict["parts"] as System.Collections.ArrayList;
                    if (reqParts != null && reqParts.Count > 0)
                    {
                        var textPart = reqParts[0] as Dictionary<string, object>;
                        if (textPart != null && textPart.ContainsKey("text"))
                        {
                            string originalText = textPart["text"]?.ToString();
                            textPart["text"] = originalText + $"\n\n[System Fixed Info]\n{additionalInfo}";
                        }
                    }
                    requestContents[requestContents.Count - 1] = lastDict;
                }
            }
            catch (Exception)
            {
                // 忽略錯誤，避免阻斷主要執行流程
            }
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

            foreach (Dictionary<string, object> part in parts)
            {
                if (part.ContainsKey("text"))
                {
                    ui.ReportTextResponse(part["text"].ToString(), currentModelName);
                }

                if (part.ContainsKey("functionCall"))
                {
                    hasFunctionCall = true;
                    var call = part["functionCall"] as Dictionary<string, object>;
                    string funcName = call["name"].ToString();
                    var argsDict = (call.ContainsKey("args") ? call["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();

                    ui.ReportToolCall(funcName, JsonTools.Serialize(argsDict));

                    string result = await toolExecutor(funcName, argsDict);
                    UsageLogger.LogAction(funcName, result);

                    if (result == "[SKIP_FUNCTION_RESPONSE]")
                    {
                        ui.ReportToolResult("已成功將圖片注入對話歷史紀錄。");
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
                // 若為匿名型別，嘗試序列化再反序列化
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
