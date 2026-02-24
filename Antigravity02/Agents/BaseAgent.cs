using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 所有 AI Agent 的基底類別，實作核心的 Function Calling 循環
    /// </summary>
    public abstract class BaseAgent
    {
        protected readonly IAIClient SmartClient;
        protected readonly IAIClient FastClient;
        private bool _useSmartModel = false; // 預設使用快速模型

        protected IAIClient Client => _useSmartModel ? SmartClient : FastClient;
        public bool IsSmartMode => _useSmartModel;
        
        public void SetModelMode(string mode)
        {
            bool wasSmart = _useSmartModel;
            if (mode?.ToLower() == "fast")
            {
                _useSmartModel = false;
            }
            else
            {
                _useSmartModel = true;
            }

            // 模式有變更時，通知子類別重新初始化工具宣告
            if (wasSmart != _useSmartModel)
            {
                OnModelModeChanged();
                _modelSwitchHappenedInThisTurn = true; // 標記發生切換
            }
        }

        /// <summary>
        /// 當模型模式切換時觸發，子類別可覆寫此方法以更新工具宣告等
        /// </summary>
        protected virtual void OnModelModeChanged() { }

        protected string SystemInstruction { get; set; }

        protected List<object> ToolDeclarations;
        protected List<object> ChatHistory; // 新增：保存完整對話紀錄
        private bool _modelSwitchHappenedInThisTurn = false; // 追蹤此輪是否觸發模型切換

        // 新增：動態開關時間戳記
        public bool EnableTimestampHeader { get; set; } = true;
        
        // 新增：歷史紀錄壓縮 Token 閾值
        public int TokenThresholdForCompression { get; set; } = 800000;

        protected BaseAgent(string apiKey, string smartModel, string fastModel)
        {
            SmartClient = new GeminiClient(apiKey, smartModel);
            FastClient = new GeminiClient(apiKey, fastModel);
            ToolDeclarations = new List<object>();
            ChatHistory = new List<object>(); // 新增：初始化對話紀錄
        }

        /// <summary>
        /// 核心執行方法：接收指令並透過 UI 回饋進度
        /// </summary>
        public async Task ExecuteAsync(string userPrompt, IAgentUI ui)
        {
            _modelSwitchHappenedInThisTurn = false;
            
            // 根據開關決定是否加上時間戳記
            string finalPrompt = userPrompt;
            if (EnableTimestampHeader)
            {
                finalPrompt = $"[Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{userPrompt}";
            }

            // 將新的使用者訊息加入歷史紀錄
            ChatHistory.Add(new { role = "user", parts = new[] { new { text = finalPrompt } } });

            bool continueLoop = true;
            int currentIteration = 0;
            const int maxIterations = 10;

            while (continueLoop && currentIteration < maxIterations)
            {
                _modelSwitchHappenedInThisTurn = false; 
                currentIteration++;
                // 每次 iteration 開始前，Client 目前的模型即為此次使用的模型
                string currentModelName = Client.ModelName;
                ui.ReportThinking(currentIteration, currentModelName);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var request = new GenerateContentRequest
                    {
                        Contents = ChatHistory,
                        Tools = ToolDeclarations,
                        SystemInstruction = SystemInstruction
                    };

                    string rawJson = await Client.GenerateContentAsync(request);
                    sw.Stop();

                    var data = JsonTools.Deserialize<Dictionary<string, object>>(rawJson);

                    // 解析 Token 使用量 (從 usageMetadata 獲取)
                    int promptTokens = 0, candidateTokens = 0, totalTokens = 0;
                    if (data.ContainsKey("usageMetadata"))
                    {
                        var usage = data["usageMetadata"] as Dictionary<string, object>;
                        if (usage != null)
                        {
                            if (usage.ContainsKey("promptTokenCount"))
                                promptTokens = Convert.ToInt32(usage["promptTokenCount"]);
                            if (usage.ContainsKey("candidatesTokenCount"))
                                candidateTokens = Convert.ToInt32(usage["candidatesTokenCount"]);
                            if (usage.ContainsKey("totalTokenCount"))
                                totalTokens = Convert.ToInt32(usage["totalTokenCount"]);
                        }
                    }

                    // 紀錄 Log
                    UsageLogger.LogApiUsage(currentModelName, sw.ElapsedMilliseconds, promptTokens, candidateTokens, totalTokens);

                    // 檢查 Token 數量，若超過閾值則觸發歷史紀錄壓縮
                    if (totalTokens >= TokenThresholdForCompression)
                    {
                        await CompressHistoryAsync(ui);
                    }

                    var candidates = data["candidates"] as System.Collections.ArrayList;
                    if (candidates == null || candidates.Count == 0) break;

                    var modelContent = (candidates[0] as Dictionary<string, object>)["content"] as Dictionary<string, object>;
                    var parts = modelContent["parts"] as System.Collections.ArrayList;

                    ChatHistory.Add(modelContent);

                    bool hasFunctionCall = false;
                    var toolResponseParts = new List<object>();
                    var pendingImageDataList = new List<Tuple<string, string>>(); // (mimeType, base64Data)

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
                            var argsDict = (call["args"] as Dictionary<string, object>) ?? new Dictionary<string, object>();

                            ui.ReportToolCall(funcName, JsonTools.Serialize(argsDict));

                            // 執行具體的工具邏輯 (由子類別實作)
                            string result = await ProcessToolCallAsync(funcName, argsDict, ui);
                            UsageLogger.LogAction(funcName, result); // 紀錄行動

                            // 判斷是否含有圖片資料，若有則拆為多個 parts
                            var resultParts = BuildToolResponseParts(funcName, result);
                            toolResponseParts.AddRange(resultParts);

                            // 收集圖片資料，稍後加入 user message
                            var imageData = ExtractImageData(result);
                            if (imageData != null)
                            {
                                pendingImageDataList.Add(imageData);
                            }

                            // UI 回報結果（顯示給使用者的不含 base64）
                            if (result.StartsWith("[IMAGE_BASE64:"))
                            {
                                // 取得描述文字部分（圖片尺寸等資訊）
                                int newlineIdx = result.IndexOf('\n');
                                string sizeInfo = newlineIdx >= 0 ? result.Substring(newlineIdx + 1) : "圖片已讀取";
                                ui.ReportToolResult($"圖片已成功讀取並傳送給 AI\n{sizeInfo}");
                            }
                            else
                            {
                                ui.ReportToolResult(result);
                            }
                        }
                    }

                    if (hasFunctionCall)
                    {
                        ChatHistory.Add(new { role = "function", parts = toolResponseParts });

                        // 若有圖片資料，額外加入 user message 包含 inlineData
                        // Gemini API 要求圖片必須在 user 角色中
                        if (pendingImageDataList.Count > 0)
                        {
                            var imageParts = new List<object>();
                            foreach (var imgData in pendingImageDataList)
                            {
                                imageParts.Add(new
                                {
                                    inline_data = new
                                    {
                                        mime_type = imgData.Item1,
                                        data = imgData.Item2
                                    }
                                });
                            }
                            imageParts.Add(new { text = "以上是透過 read_file 工具讀取的圖片，請根據圖片內容進行分析或回應。" });
                            ChatHistory.Add(new { role = "user", parts = imageParts });
                        }

                        // 如果此輪發生了模型切換，且這輪主要是為了切換模型 (只有單一工具呼叫)
                        // 我們可以將此輪從歷史紀錄中移除，讓 AI 在下一輪（新模型）重新對應原始需求
                        if (_modelSwitchHappenedInThisTurn && toolResponseParts.Count == 1)
                        {
                            // 移除剛才加入的 function response 和 model content
                            if (ChatHistory.Count >= 2)
                            {
                                ChatHistory.RemoveRange(ChatHistory.Count - 2, 2);
                            }
                            
                            // 重設標記，繼續循環會使用新模型重新請求
                            _modelSwitchHappenedInThisTurn = false;
                        }
                    }
                    else
                    {
                        continueLoop = false;
                    }
                }
                catch (Exception ex)
                {
                    UsageLogger.LogError($"Agent Error: {ex.Message}");
                    ui.ReportError(ex.Message);
                    
                    // 清理可能殘留的不完整回應 (model response 已加入但 function response 尚未加入)
                    // ChatHistory 的合法結尾應為 user 或 function，若最後一筆是 model 回應則為不完整狀態
                    if (ChatHistory.Count > 0)
                    {
                        var lastEntry = ChatHistory[ChatHistory.Count - 1] as Dictionary<string, object>;
                        if (lastEntry != null && lastEntry.ContainsKey("role") && lastEntry["role"]?.ToString() == "model")
                        {
                            ChatHistory.RemoveAt(ChatHistory.Count - 1);
                        }
                    }
                    
                    // 發生錯誤時，自動備份對話紀錄，方便使用者之後載入續行
                    string recoveryPath = "recovery_history.json";
                    if (SaveChatHistory(recoveryPath))
                    {
                        ui.ReportError($"對話紀錄已自動備份至 {recoveryPath}。您可以使用 /load {recoveryPath} 來載入並重試。");
                    }
                    else
                    {
                        ui.ReportError("無法備份對話紀錄。");
                    }
                    
                    break;
                }

                // 檢查是否達到上限並詢問是否繼續
                if (continueLoop && currentIteration >= maxIterations)
                {
                    bool shouldContinue = await ui.PromptContinueAsync($"已達到單次最大執行次數 ({maxIterations})，任務尚未完成。");
                    if (shouldContinue)
                    {
                        currentIteration = 0; // 重置計數，再給它一次循環
                    }
                    else
                    {
                        ui.ReportError("任務已被使用者中斷。");
                        
                        string recoveryPath = "interrupted_history.json";
                        SaveChatHistory(recoveryPath);
                        ui.ReportError($"目前對話已存檔至 {recoveryPath}。");
                        
                        continueLoop = false;
                    }
                }
            }
        }

        /// <summary>
        /// 子類別必須實作此方法來處理特定的工具呼叫
        /// </summary>
        protected abstract Task<string> ProcessToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui);

        /// <summary>
        /// 歷史紀錄過長時，自動進行壓縮摘要
        /// </summary>
        private async Task CompressHistoryAsync(IAgentUI ui)
        {
            if (ChatHistory.Count < 6) return;

            int targetSplitIndex = ChatHistory.Count / 2;
            int actualSplitIndex = -1;

            for (int i = targetSplitIndex; i < ChatHistory.Count; i++)
            {
                string role = "";
                if (ChatHistory[i] is Dictionary<string, object> dict && dict.ContainsKey("role"))
                {
                    role = dict["role"]?.ToString();
                }
                else
                {
                    string serialized = JsonTools.Serialize(ChatHistory[i]);
                    var tempDict = JsonTools.Deserialize<Dictionary<string, object>>(serialized);
                    if (tempDict != null && tempDict.ContainsKey("role"))
                    {
                        role = tempDict["role"]?.ToString();
                    }
                }

                if (role == "user")
                {
                    actualSplitIndex = i;
                    break;
                }
            }

            if (actualSplitIndex <= 0) return;

            ui.ReportThinking(0, "Fast Model (正在壓縮與清理對話歷史紀錄...)");

            var historyToCompress = ChatHistory.GetRange(0, actualSplitIndex);
            
            // 過濾掉 inline_data 欄位，避免將大體積圖片 base64 送給 Fast Model 做摘要，節省 Token 與網路頻寬
            string rawJsonToCompress = JsonTools.Serialize(historyToCompress);
            string cleanedJsonToCompress = System.Text.RegularExpressions.Regex.Replace(
                rawJsonToCompress,
                @"\""inline_data\""\s*:\s*\{[^}]*\}",
                "\"inline_data\": \"[IMAGE_DATA_REMOVED]\""
            );

            string compressPrompt = "請將以下歷史對話紀錄進行詳細摘要，保留重要的上下文、決策過程、變數設定與關鍵資訊：\n\n" + cleanedJsonToCompress;

            var request = new GenerateContentRequest
            {
                Contents = new List<object>
                {
                    new { role = "user", parts = new[] { new { text = compressPrompt } } }
                }
            };

            try
            {
                string rawJson = await FastClient.GenerateContentAsync(request);
                var data = JsonTools.Deserialize<Dictionary<string, object>>(rawJson);
                
                if (data != null && data.ContainsKey("candidates"))
                {
                    var candidates = data["candidates"] as System.Collections.ArrayList;
                    if (candidates != null && candidates.Count > 0)
                    {
                        var modelContent = (candidates[0] as Dictionary<string, object>)["content"] as Dictionary<string, object>;
                        var parts = modelContent["parts"] as System.Collections.ArrayList;
                        string summaryText = (parts[0] as Dictionary<string, object>)["text"]?.ToString() ?? "摘要失敗";

                        ChatHistory.RemoveRange(0, actualSplitIndex);
                        
                        ChatHistory.Insert(0, new { role = "user", parts = new[] { new { text = "以下是之前對話的歷史摘要：\n" + summaryText } } });
                        ChatHistory.Insert(1, new { role = "model", parts = new[] { new { text = "已收到歷史紀錄摘要，我會根據這些資訊上下文繼續回應並執行任務。" } } });

                        ui.ReportToolResult($"歷史紀錄 Token 過高，已自動將前半段 ({actualSplitIndex} 則對話) 壓縮為摘要，釋放 Token 空間。");
                    }
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"History Compression Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 清除對話紀錄，開始新對話
        /// </summary>
        public void ClearChatHistory()
        {
            ChatHistory.Clear();
        }

        public bool SaveChatHistory(string filePath)
        {
            try
            {
                string json = JsonTools.Serialize(ChatHistory);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"SaveChatHistory Error: {ex.Message}");
                return false;
            }
        }

        public bool LoadChatHistory(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    UsageLogger.LogError($"LoadChatHistory Error: File not found: {filePath}");
                    return false;
                }

                string json = File.ReadAllText(filePath);
                var history = JsonTools.Deserialize<List<object>>(json);
                if (history != null)
                {
                    ChatHistory.Clear();
                    ChatHistory.AddRange(history);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"LoadChatHistory Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取得目前的對話紀錄 (唯讀)，供外部顯示用
        /// </summary>
        public ReadOnlyCollection<object> GetChatHistory()
        {
            return ChatHistory.AsReadOnly();
        }

        /// <summary>
        /// 建立工具回應的 parts，圖片遞補為 functionResponse + inlineData
        /// </summary>
        private List<object> BuildToolResponseParts(string funcName, string result)
        {
            var parts = new List<object>();

            // 檢查是否包含圖片 base64 資料
            if (result.StartsWith("[IMAGE_BASE64:"))
            {
                // 解析格式: [IMAGE_BASE64:mime_type:base64data]\n[描述文字]
                int firstClose = result.IndexOf(']');
                if (firstClose > 0)
                {
                    string marker = result.Substring(1, firstClose - 1); // IMAGE_BASE64:mime:data
                    string description = firstClose + 1 < result.Length ? result.Substring(firstClose + 1).Trim() : "圖片已讀取";

                    string[] markerParts = marker.Split(new[] { ':' }, 3);
                    if (markerParts.Length == 3)
                    {
                        string mimeType = markerParts[1];
                        string base64Data = markerParts[2];

                        // 加入 functionResponse（文字描述）
                        parts.Add(new
                        {
                            functionResponse = new
                            {
                                name = funcName,
                                response = new { content = $"圖片已成功讀取。{description}。請看下方圖片內容。" }
                            }
                        });

                        // 加入 inlineData（圖片資料）作為 user part
                        // Gemini API 需要圖片放在 user 角色中
                        // 所以我們先加入 function response，再在下一步加入 user message 包含圖片
                        return parts; // 讓外層處理圖片 part
                    }
                }
            }

            // 一般文字回應
            parts.Add(new
            {
                functionResponse = new
                {
                    name = funcName,
                    response = new { content = result }
                }
            });

            return parts;
        }

        /// <summary>
        /// 從工具回傳結果中提取圖片 base64 資料，回傳 (mimeType, base64Data) 或 null
        /// </summary>
        private Tuple<string, string> ExtractImageData(string result)
        {
            if (!result.StartsWith("[IMAGE_BASE64:")) return null;

            int firstClose = result.IndexOf(']');
            if (firstClose <= 0) return null;

            string marker = result.Substring(1, firstClose - 1);
            string[] markerParts = marker.Split(new[] { ':' }, 3);
            if (markerParts.Length != 3) return null;

            return Tuple.Create(markerParts[1], markerParts[2]);
        }
    }
}
