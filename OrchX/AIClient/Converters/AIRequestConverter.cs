using System.Collections.Generic;
using System.Text;
using OrchX.Tools;
using OrchX.AIClient.Models;

namespace OrchX.AIClient.Converters
{
    /// <summary>
    /// 提供 AI 請求格式之間的轉換處理
    /// </summary>
    public static class AIRequestConverter
    {
        /// <summary>
        /// 將 Gemini 專用的 GenerateContentRequest 轉換為通用的 UnifiedAIRequest
        /// </summary>
        public static UnifiedAIRequest ToUnifiedRequest(GenerateContentRequest request, string modelName)
        {
            var messages = new List<object>();

            // 加入 System Instruction 到 Messages 內
            if (!string.IsNullOrEmpty(request.SystemInstruction))
            {
                messages.Add(new { role = "system", content = request.SystemInstruction });
            }

            // 轉換請求內容為統一的格式
            if (request.Contents is System.Collections.IEnumerable enumerableContents)
            {
                foreach (var item in enumerableContents)
                {
                    if (TryGetRoleAndPartsFromMessage(item, out string role, out IEnumerable<object> parts))
                    {
                        var contentBuilder = new StringBuilder();
                        var toolCalls = new List<object>();
                        var images = new List<string>();

                        foreach(var part in parts)
                        {
                            if (TryGetTextFromPart(part, out string text))
                            {
                                contentBuilder.Append(text);
                            }
                            
                            if (TryGetFunctionCallFromPart(part, out string funcName, out Dictionary<string, object> args))
                            {
                                toolCalls.Add(new
                                {
                                    type = "function",
                                    function = new
                                    {
                                        name = funcName,
                                        arguments = args
                                    }
                                });
                            }
                            
                            if (TryGetFunctionResponseFromPart(part, out string fnName, out string fnContent))
                            {
                                messages.Add(new { role = "tool", content = fnContent });
                            }

                            if (part is Dictionary<string, object> dictPart)
                            {
                                if (dictPart.ContainsKey("inlineData") && dictPart["inlineData"] is Dictionary<string, object> inlineData && inlineData.ContainsKey("data"))
                                {
                                    images.Add(inlineData["data"].ToString());
                                }
                            }
                            else if (part != null)
                            {
                                string partJson = JsonTools.Serialize(part);
                                var partDict = JsonTools.Deserialize<Dictionary<string, object>>(partJson);
                                if (partDict != null && partDict.ContainsKey("inlineData") && partDict["inlineData"] is Dictionary<string, object> inln && inln.ContainsKey("data"))
                                {
                                    images.Add(inln["data"].ToString());
                                }
                            }
                        }

                        if (role == "user" || role == "model")
                        {
                            string unifiedRole = role == "model" ? "assistant" : "user";
                            var msg = new Dictionary<string, object>();
                            msg["role"] = unifiedRole;
                            msg["content"] = contentBuilder.ToString();

                            if (toolCalls.Count > 0)
                            {
                                msg["tool_calls"] = toolCalls;
                            }
                            if (images.Count > 0)
                            {
                                msg["images"] = images;
                            }
                            messages.Add(msg);
                        }
                    }
                }
            }

            return new UnifiedAIRequest
            {
                Model = modelName,
                Messages = messages,
                Tools = request.Tools,
                Stream = false
            };
        }

        /// <summary>
        /// 將通用的 UnifiedAIRequest 轉換回 Gemini 專用的 GenerateContentRequest
        /// </summary>
        public static GenerateContentRequest ToGenerateContentRequest(UnifiedAIRequest unifiedRequest, string mockProviderName = null)
        {
            var contents = new List<object>();
            string systemInst = null;

            if (unifiedRequest.Messages is System.Collections.IEnumerable enumerableMessages)
            {
                foreach (var msgObj in enumerableMessages)
                {
                    var dictMsg = msgObj as Dictionary<string, object>;
                    if (dictMsg == null)
                    {
                        var json = JsonTools.Serialize(msgObj);
                        dictMsg = JsonTools.Deserialize<Dictionary<string, object>>(json);
                    }

                    if (dictMsg != null)
                    {
                        string role = dictMsg.ContainsKey("role") ? dictMsg["role"]?.ToString() : "user";
                        
                        // 處理獨立的 system prompt
                        if (role == "system")
                        {
                            if (dictMsg.ContainsKey("content") && dictMsg["content"] != null)
                            {
                                string contentStr = dictMsg["content"].ToString();
                                if (!string.IsNullOrEmpty(contentStr))
                                {
                                    systemInst = string.IsNullOrEmpty(systemInst) ? contentStr : systemInst + "\n" + contentStr;
                                }
                            }
                            continue; // 系統訊息不會放進 contents
                        }

                        string geminiRole = role == "assistant" ? "model" : role;
                        var parts = new List<object>();

                        // 處理純文字 (tool response 的 content 留給 functionResponse 處理)
                        if (role != "tool" && dictMsg.ContainsKey("content") && dictMsg["content"] != null)
                        {
                            string text = dictMsg["content"].ToString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                parts.Add(new { text = text });
                            }
                        }

                        // 處理函式呼叫 (tool_calls)
                        if (dictMsg.ContainsKey("tool_calls") && dictMsg["tool_calls"] is System.Collections.IEnumerable toolCalls)
                        {
                            foreach (var tcObj in toolCalls)
                            {
                                var tc = tcObj as Dictionary<string, object>;
                                if (tc == null)
                                {
                                    tc = JsonTools.Deserialize<Dictionary<string, object>>(JsonTools.Serialize(tcObj));
                                }

                                if (tc != null && tc.ContainsKey("function") && tc["function"] is Dictionary<string, object> fn)
                                {
                                    string fnName = fn.ContainsKey("name") ? fn["name"].ToString() : "";
                                    var args = fn.ContainsKey("arguments") ? fn["arguments"] : new Dictionary<string, object>();

                                    parts.Add(new
                                    {
                                        functionCall = new
                                        {
                                            name = fnName,
                                            args = args
                                        }
                                    });
                                }
                            }
                        }

                        // 處理函式回傳結果 (tool response)
                        if (role == "tool" && dictMsg.ContainsKey("content"))
                        {
                            string fnContent = dictMsg["content"].ToString();
                            string fnName = dictMsg.ContainsKey("name") ? dictMsg["name"].ToString() : "unknown_function";
                            geminiRole = "function"; // Gemini 的工具回應角色是 function

                            parts.Add(new
                            {
                                functionResponse = new
                                {
                                    name = fnName,
                                    response = new { content = fnContent }
                                }
                            });
                        }

                        // 處理圖片 (images)
                        if (dictMsg.ContainsKey("images") && dictMsg["images"] is System.Collections.IEnumerable images)
                        {
                            foreach (var img in images)
                            {
                                parts.Add(new
                                {
                                    inlineData = new
                                    {
                                        mimeType = "image/jpeg", // 預設使用 jpeg
                                        data = img.ToString()
                                    }
                                });
                            }
                        }

                        // 如果存在有效的 part 就加進對話紀錄
                        if (parts.Count > 0)
                        {
                            contents.Add(new { role = geminiRole, parts = parts });
                        }
                    }
                }
            }

            return new GenerateContentRequest
            {
                Contents = contents,
                Tools = unifiedRequest.Tools,
                SystemInstruction = systemInst,
                MockProviderName = mockProviderName
            };
        }

        private static bool TryGetTextFromPart(object partObj, out string text)
        {
            text = null;
            if (partObj is Dictionary<string, object> part && part.ContainsKey("text"))
            {
                text = part["text"]?.ToString();
                return true;
            }
            return false;
        }

        private static bool TryGetFunctionCallFromPart(object partObj, out string functionName, out Dictionary<string, object> args)
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

        private static bool TryGetFunctionResponseFromPart(object partObj, out string functionName, out string responseContent)
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

        private static bool TryGetRoleAndPartsFromMessage(object messageObj, out string role, out IEnumerable<object> parts)
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
    }
}
