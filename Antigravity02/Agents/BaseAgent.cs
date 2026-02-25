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
            string finalPrompt = EnableTimestampHeader ? 
                $"[Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{userPrompt}" : 
                userPrompt;

            // 將新的使用者訊息加入歷史紀錄
            ChatHistory.Add(new { role = "user", parts = new[] { new { text = finalPrompt } } });

            bool continueLoop = true;
            int currentIteration = 0;
            const int maxIterations = 10;

            while (continueLoop && currentIteration < maxIterations)
            {
                _modelSwitchHappenedInThisTurn = false; 
                currentIteration++;
                
                string currentModelName = Client.ModelName;
                ui.ReportThinking(currentIteration, currentModelName);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // 複製一份 ChatHistory 給這次 Request 使用
                    var requestContents = new List<object>(ChatHistory);

                    // 在送往 AI 前，固定附加上系統資訊 (如 read_skills)，但不存入 ChatHistory
                    try
                    {
                        string additionalInfo = BuildSystemFixedInfo();
                        if (!string.IsNullOrWhiteSpace(additionalInfo))
                        {
                            AppendFixedInfoToLastUserMessage(requestContents, additionalInfo);
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略錯誤，避免阻斷主要執行流程
                    }

                    var request = new GenerateContentRequest
                    {
                        Contents = requestContents,
                        Tools = ToolDeclarations,
                        SystemInstruction = SystemInstruction
                    };

                    string rawJson = await Client.GenerateContentAsync(request);
                    sw.Stop();

                    var data = JsonTools.Deserialize<Dictionary<string, object>>(rawJson);

                    // 解析並紀錄 Token 使用量
                    var (promptTokens, candidateTokens, totalTokens) = ExtractTokenUsage(data);
                    UsageLogger.LogApiUsage(currentModelName, sw.ElapsedMilliseconds, promptTokens, candidateTokens, totalTokens);

                    if (totalTokens >= TokenThresholdForCompression)
                    {
                        await CompressHistoryAsync(ui);
                    }

                    var candidates = data["candidates"] as System.Collections.ArrayList;
                    if (candidates == null || candidates.Count == 0) break;

                    var modelContent = (candidates[0] as Dictionary<string, object>)["content"] as Dictionary<string, object>;
                    var parts = modelContent["parts"] as System.Collections.ArrayList;

                    ChatHistory.Add(modelContent);

                    // 處理模型回傳的 parts，包含文字回應與工具呼叫
                    var (hasFunctionCall, toolResponseParts) = await ProcessModelPartsAsync(parts, ui, currentModelName);

                    if (hasFunctionCall)
                    {
                        HandleFunctionCallHistoryUpdate(toolResponseParts);
                    }
                    else
                    {
                        continueLoop = false;
                    }
                }
                catch (Exception ex)
                {
                    HandleExecutionError(ex, ui);
                    break;
                }

                if (continueLoop && currentIteration >= maxIterations)
                {
                    bool shouldContinue = await CheckMaxIterationsAndPromptAsync(maxIterations, ui);
                    if (shouldContinue)
                    {
                        currentIteration = 0;
                    }
                    else
                    {
                        continueLoop = false;
                    }
                }
            }
        }

        /// <summary>
        /// 收集所有要附加至 User 提示的系統資訊字串。
        /// 子類別可覆寫此方法以自訂或擴充附加的內容。
        /// </summary>
        protected virtual string BuildSystemFixedInfo()
        {
            var fileTools = new FileTools();
            string skillsData = fileTools.ReadSkills(fileTools.SkillsPath);
            if (!string.IsNullOrWhiteSpace(skillsData))
            {
                return $"Available Skills (from read_skills):\n{skillsData}";
            }
            return string.Empty;
        }

        /// <summary>
        /// 在送出 Request 之前，將固定的系統資訊附加到最後一筆 User Message。
        /// </summary>
        protected virtual void AppendFixedInfoToLastUserMessage(List<object> requestContents, string additionalInfo)
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

        private (int promptTokens, int candidateTokens, int totalTokens) ExtractTokenUsage(Dictionary<string, object> data)
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

        private async Task<(bool hasFunctionCall, List<object> toolResponseParts)> ProcessModelPartsAsync(System.Collections.ArrayList parts, IAgentUI ui, string currentModelName)
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
                    var argsDict = (call["args"] as Dictionary<string, object>) ?? new Dictionary<string, object>();

                    ui.ReportToolCall(funcName, JsonTools.Serialize(argsDict));

                    string result = await ProcessToolCallAsync(funcName, argsDict, ui);
                    UsageLogger.LogAction(funcName, result);

                    var resultParts = BuildToolResponseParts(funcName, result);
                    toolResponseParts.AddRange(resultParts);

                    ui.ReportToolResult(result);
                }
            }

            return (hasFunctionCall, toolResponseParts);
        }

        private void HandleFunctionCallHistoryUpdate(List<object> toolResponseParts)
        {
            ChatHistory.Add(new { role = "function", parts = toolResponseParts });

            if (_modelSwitchHappenedInThisTurn && toolResponseParts.Count == 1)
            {
                if (ChatHistory.Count >= 2)
                {
                    ChatHistory.RemoveRange(ChatHistory.Count - 2, 2);
                }
                _modelSwitchHappenedInThisTurn = false;
            }
        }

        private void HandleExecutionError(Exception ex, IAgentUI ui)
        {
            UsageLogger.LogError($"Agent Error: {ex.Message}");
            ui.ReportError(ex.Message);
            
            if (ChatHistory.Count > 0)
            {
                var lastEntry = ChatHistory[ChatHistory.Count - 1] as Dictionary<string, object>;
                if (lastEntry != null && lastEntry.ContainsKey("role") && lastEntry["role"]?.ToString() == "model")
                {
                    ChatHistory.RemoveAt(ChatHistory.Count - 1);
                }
            }
            
            string recoveryPath = "recovery_history.json";
            if (SaveChatHistory(recoveryPath))
            {
                ui.ReportError($"對話紀錄已自動備份至 {recoveryPath}。您可以使用 /load {recoveryPath} 來載入並重試。");
            }
            else
            {
                ui.ReportError("無法備份對話紀錄。");
            }
        }

        private async Task<bool> CheckMaxIterationsAndPromptAsync(int maxIterations, IAgentUI ui)
        {
            bool shouldContinue = await ui.PromptContinueAsync($"已達到單次最大執行次數 ({maxIterations})，任務尚未完成。");
            if (!shouldContinue)
            {
                ui.ReportError("任務已被使用者中斷。");
                string recoveryPath = "interrupted_history.json";
                SaveChatHistory(recoveryPath);
                ui.ReportError($"目前對話已存檔至 {recoveryPath}。");
                return false;
            }
            return true;
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
            string jsonToCompress = JsonTools.Serialize(historyToCompress);

            string compressPrompt = "請將以下歷史對話紀錄進行詳細摘要，保留重要的上下文、決策過程、變數設定與關鍵資訊：\n\n" + jsonToCompress;

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
        /// 建立工具回應的 parts
        /// </summary>
        private List<object> BuildToolResponseParts(string funcName, string result)
        {
            var parts = new List<object>();

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
    }
}
