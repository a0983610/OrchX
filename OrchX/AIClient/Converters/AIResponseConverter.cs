using System;
using System.Collections.Generic;
using OrchX.Tools;
using OrchX.AIClient.Models;

namespace OrchX.AIClient.Converters
{
    /// <summary>
    /// 提供 AI 模型回應字串至統一模型結構的轉換處理
    /// </summary>
    public static class AIResponseConverter
    {
        /// <summary>
        /// 將原始的回應 JSON 字串轉換為 UnifiedAIResponse
        /// </summary>
        /// <param name="jsonResponse">原始 JSON 字串</param>
        /// <param name="provider">AI 服務提供者，如 "gemini" 或 "ollama"</param>
        /// <returns>轉換後的 UnifiedAIResponse，如果解析失敗則回傳 null</returns>
        public static UnifiedAIResponse ToUnifiedResponse(string jsonResponse, string provider)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse))
                return null;

            Dictionary<string, object> rawDict;
            try
            {
                rawDict = JsonTools.Deserialize<Dictionary<string, object>>(jsonResponse);
            }
            catch
            {
                return null;
            }

            if (rawDict == null)
                return null;

            if (provider?.ToLower() == "ollama")
            {
                return ParseOllamaResponse(rawDict);
            }
            else
            {
                // 預設當作 gemini 解析
                return ParseGeminiResponse(rawDict);
            }
        }

        private static UnifiedAIResponse ParseGeminiResponse(Dictionary<string, object> data)
        {
            var response = new UnifiedAIResponse();

            // 解析 Token Usage
            response.Usage = ExtractGeminiTokenUsage(data);

            if (!data.ContainsKey("candidates") || !(data["candidates"] is System.Collections.ArrayList candidates) || candidates.Count == 0)
                return response;

            var firstCandidate = candidates[0] as Dictionary<string, object>;

            if (firstCandidate == null || !firstCandidate.ContainsKey("content"))
                return response;

            var modelContent = firstCandidate["content"] as Dictionary<string, object>;
            if (modelContent == null || !modelContent.ContainsKey("parts"))
                return response;

            if (modelContent["parts"] is System.Collections.IEnumerable parts)
            {
                foreach (var partObj in parts)
                {
                    if (partObj is Dictionary<string, object> part)
                    {
                        // 處理純文字
                        if (part.ContainsKey("text"))
                        {
                            string text = part["text"]?.ToString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                response.Content = string.IsNullOrEmpty(response.Content) ? text : response.Content + text;
                            }
                        }

                        // 處理 Function Call
                        if (part.ContainsKey("functionCall"))
                        {
                            var call = part["functionCall"] as Dictionary<string, object>;
                            if (call != null && call.ContainsKey("name"))
                            {
                                string funcName = call["name"]?.ToString() ?? string.Empty;
                                var argsDict = (call.ContainsKey("args") ? call["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();

                                response.ToolCalls.Add(new UnifiedToolCall
                                {
                                    FunctionName = funcName,
                                    Arguments = argsDict
                                });
                            }
                        }
                    }
                }
            }

            return response;
        }

        private static UnifiedAIResponse ParseOllamaResponse(Dictionary<string, object> data)
        {
            var response = new UnifiedAIResponse();

            // 解析 Token Usage
            response.Usage = ExtractOllamaTokenUsage(data);

            if (!data.ContainsKey("message"))
                return response;

            var msgObj = data["message"];
            Dictionary<string, object> msg = null;

            if (msgObj is Dictionary<string, object> d)
            {
                msg = d;
            }
            else
            {
                string s = JsonTools.Serialize(msgObj);
                msg = JsonTools.Deserialize<Dictionary<string, object>>(s);
            }

            if (msg == null)
                return response;

            // 處理純文字
            if (msg.ContainsKey("content") && msg["content"] != null)
            {
                response.Content = msg["content"].ToString();
            }

            // 處理 Function Call
            if (msg.ContainsKey("tool_calls") && msg["tool_calls"] is System.Collections.IEnumerable toolCalls)
            {
                foreach (var tcObj in toolCalls)
                {
                    string tcString = JsonTools.Serialize(tcObj);
                    var tc = JsonTools.Deserialize<Dictionary<string, object>>(tcString);
                    if (tc != null && tc.ContainsKey("function") && tc["function"] is Dictionary<string, object> func)
                    {
                        var rawArgs = func.ContainsKey("arguments") ? func["arguments"] : null;
                        var parsedArgs = new Dictionary<string, object>();
                        if (rawArgs is Dictionary<string, object> dictArgs)
                        {
                            parsedArgs = dictArgs;
                        }
                        else if (rawArgs is string strArgs && !string.IsNullOrWhiteSpace(strArgs))
                        {
                            try { parsedArgs = JsonTools.Deserialize<Dictionary<string, object>>(strArgs) ?? new Dictionary<string, object>(); } catch { }
                        }

                        response.ToolCalls.Add(new UnifiedToolCall
                        {
                            FunctionName = func.ContainsKey("name") ? func["name"]?.ToString() ?? string.Empty : string.Empty,
                            Arguments = parsedArgs
                        });
                    }
                }
            }

            return response;
        }

        private static TokenUsage ExtractGeminiTokenUsage(Dictionary<string, object> data)
        {
            var usage = new TokenUsage();
            if (data.ContainsKey("usageMetadata") && data["usageMetadata"] is Dictionary<string, object> meta)
            {
                if (meta.ContainsKey("promptTokenCount"))
                    usage.PromptTokens = Convert.ToInt32(meta["promptTokenCount"]);
                if (meta.ContainsKey("candidatesTokenCount"))
                    usage.CandidateTokens = Convert.ToInt32(meta["candidatesTokenCount"]);
                if (meta.ContainsKey("totalTokenCount"))
                    usage.TotalTokens = Convert.ToInt32(meta["totalTokenCount"]);
            }
            return usage;
        }

        private static TokenUsage ExtractOllamaTokenUsage(Dictionary<string, object> data)
        {
            var usage = new TokenUsage();
            if (data.ContainsKey("prompt_eval_count"))
                usage.PromptTokens = Convert.ToInt32(data["prompt_eval_count"]);
            if (data.ContainsKey("eval_count"))
                usage.CandidateTokens = Convert.ToInt32(data["eval_count"]);
            
            usage.TotalTokens = usage.PromptTokens + usage.CandidateTokens;
            return usage;
        }
    }
}
