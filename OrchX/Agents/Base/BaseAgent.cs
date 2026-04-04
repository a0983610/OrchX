using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
{
    /// <summary>
    /// 所有 AI Agent 的基底類別，負責實作核心的 Function Calling 循環與對話歷史管理。
    /// </summary>
    public abstract class BaseAgent
    {
        public IAIClient SmartClient { get; }
        public IAIClient FastClient { get; }
        private bool _useSmartModel = false; // 預設使用快速模型

        protected IAIClient Client => _useSmartModel ? SmartClient : FastClient;
        public bool IsSmartMode => _useSmartModel;
        
        public virtual string AgentName { get; set; } = "Manager";
        public virtual string MockProviderName => Client?.ProviderName ?? "gemini";
        
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
        /// 當模型模式切換 (Smart/Fast) 時觸發。
        /// 子類別可覆寫此方法，藉此更新特定模式下的工具宣告或狀態。
        /// </summary>
        protected virtual void OnModelModeChanged() { }

        protected string SystemInstruction { get; set; }

        protected List<object> ToolDeclarations;
        protected List<object> ChatHistory; // 保存完整對話紀錄
        private bool _modelSwitchHappenedInThisTurn = false; // 追蹤此輪是否觸發模型切換

        // 動態開關時間戳記
        public bool EnableTimestampHeader { get; set; } = true;
        
        // 歷史紀錄壓縮 Token 閾值
        public int TokenThresholdForCompression { get; set; } = 100000;

        private bool _hasWorkspaceExceededLimit = false;

        // 靜態快取：系統環境資訊不會變動，只需初始化一次
        private static readonly string _cachedSystemEnvironmentInfo = BuildSystemEnvironmentInfo();

        private static string BuildSystemEnvironmentInfo()
        {
            return $"[System Environment]\n" +
                   $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n";
        }

        protected BaseAgent(IAIClient smartClient, IAIClient fastClient)
        {
            SmartClient = smartClient;
            FastClient = fastClient;
            ToolDeclarations = new List<object>();
            ChatHistory = new List<object>(); // 初始化對話紀錄
        }

        /// <summary>
        /// 核心執行方法：接收使用者指令，並透過 UI 回饋執行進度，處理 Function Calling 迴圈。
        /// </summary>
        public async Task ExecuteAsync(string userPrompt, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            _modelSwitchHappenedInThisTurn = false;
            AppendUserPromptToHistory(userPrompt);

            bool continueLoop = true;
            int currentIteration = 0;
            const int maxIterations = 30;

            while (continueLoop && currentIteration < maxIterations)
            {
                _modelSwitchHappenedInThisTurn = false; 
                currentIteration++;
                
                string currentModelName = Client.ModelName;
                ui.ReportThinking(currentIteration, currentModelName);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var request = CreateRequest();
                    string rawJson = await Client.GenerateContentAsync(request, cancellationToken);
                    sw.Stop();

                    var data = JsonTools.Deserialize<Dictionary<string, object>>(rawJson);

                    await HandleTokenUsageAsync(data, currentModelName, sw.ElapsedMilliseconds, ui, cancellationToken);

                    var parts = Client.ExtractResponseParts(data, out var modelContent);
                    if (parts == null) break;

                    ChatHistory.Add(modelContent);

                    var (hasFunctionCall, toolResponseParts) = await Client.ProcessModelPartsAsync(
                        parts, 
                        ui, 
                        currentModelName, 
                        async (funcName, argsDict) => await ProcessToolCallAsync(funcName, argsDict, ui, cancellationToken),
                        cancellationToken
                    );

                    if (hasFunctionCall)
                    {
                        HandleFunctionCallHistoryUpdate(toolResponseParts);
                    }
                    else
                    {
                        continueLoop = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    ui.ReportInfo("已中斷執行。");
                    break;
                }
                catch (Exception ex)
                {
                    HandleExecutionError(ex, ui);
                    break;
                }

                if (continueLoop && currentIteration >= maxIterations)
                {
                    continueLoop = await CheckMaxIterationsAndPromptAsync(maxIterations, ui);
                    if (continueLoop)
                    {
                        currentIteration = 0;
                    }
                }
            }
        }

        /// <summary>
        /// 將使用者的提問加入對話歷史紀錄，可選擇是否加上時間戳記。
        /// </summary>
        private void AppendUserPromptToHistory(string userPrompt)
        {
            string finalPrompt = EnableTimestampHeader ? 
                $"[Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{userPrompt}" : 
                userPrompt;
            ChatHistory.Add(Client.BuildMessageContent("user", finalPrompt));
        }

        /// <summary>
        /// 建立提交至 AI 模型的請求物件，包含歷史對話與系統提示。
        /// </summary>
        private GenerateContentRequest CreateRequest()
        {
            var requestContents = new List<object>(ChatHistory);

            try
            {
                string additionalInfo = BuildSystemFixedInfo();
                if (!string.IsNullOrWhiteSpace(additionalInfo))
                {
                    requestContents.Add(Client.BuildMessageContent("model", "請問目前的系統狀態與環境資訊為何？"));
                    requestContents.Add(Client.BuildMessageContent("user", additionalInfo));
                }
            }
            catch (Exception)
            {
                // 忽略錯誤，避免阻斷主要執行流程
            }

            return new GenerateContentRequest
            {
                Contents = requestContents,
                Tools = ToolDeclarations,
                SystemInstruction = SystemInstruction,
                MockProviderName = MockProviderName
            };
        }

        /// <summary>
        /// 處理並記錄 API 的 Token 使用量，若超過閾值則觸發歷史紀錄壓縮。
        /// </summary>
        private async Task HandleTokenUsageAsync(Dictionary<string, object> data, string modelName, long elapsedMs, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            var (promptTokens, candidateTokens, totalTokens) = Client.ExtractTokenUsage(data);
            UsageLogger.LogApiUsage(modelName, elapsedMs, promptTokens, candidateTokens, totalTokens);

            if (totalTokens >= TokenThresholdForCompression)
            {
                await CompressHistoryAsync(ui, cancellationToken);
            }
        }

        /// <summary>
        /// 收集並建構要固定附加於系統提示之前的環境或背景資訊。
        /// 包含系統環境、可用技能 (Skills)、知識庫索引與工作區檔案清單等。
        /// 子類別可覆寫此方法以自訂或擴充附加的內容。
        /// </summary>
        protected virtual string BuildSystemFixedInfo()
        {
            var fileTools = new FileTools();
            string skillsData = fileTools.ReadSkills(fileTools.SkillsPath);
            string additionalInfo = string.Empty;
            
            additionalInfo += _cachedSystemEnvironmentInfo + $"Current Directory: {Environment.CurrentDirectory}\n\n";
            
            if (!string.IsNullOrWhiteSpace(skillsData))
            {
                additionalInfo += $"[Available Skills]\n{skillsData}\n\n";
            }

            // 讀取知識庫索引
            string indexPath = Path.Combine(".agent", "knowledge", "00_INDEX.md").Replace("\\", "/");
            string indexContent = fileTools.ReadFile(indexPath);
            if (!string.IsNullOrWhiteSpace(indexContent) && !indexContent.StartsWith("錯誤"))
            {
                var lines = indexContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 20)
                {
                    indexContent = string.Join("\n", lines.Take(20)) + "\n... (Index truncated)";
                }
                additionalInfo += $"[Long-term Memory Index]\n{indexContent}\n\n";
            }

            // 讀取當前 AI_Workspace 的檔案清單
            if (!_hasWorkspaceExceededLimit)
            {
                string workspaceFiles = fileTools.ListFiles("");
                if (!string.IsNullOrWhiteSpace(workspaceFiles) && !workspaceFiles.StartsWith("錯誤"))
                {
                    var fileLines = workspaceFiles.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fileLines.Length <= 50)
                    {
                        additionalInfo += $"[Workspace Files]\n{workspaceFiles}\n\n";
                    }
                    else
                    {
                        _hasWorkspaceExceededLimit = true;
                    }
                }
            }

            return additionalInfo.TrimEnd() + "\n";
        }



        /// <summary>
        /// 將圖片注入對話歷史紀錄中。
        /// 若當前回合 (Model Response) 有多個 Function Call，則拒絕注入並回傳 false，以免破壞歷史紀錄結構。
        /// </summary>
        public bool InjectImageHistory(string imagePath, string mimeType, string base64Data)
        {
            if (ChatHistory.Count > 0)
            {
                var lastEntry = ChatHistory[ChatHistory.Count - 1];
                if (Client.TryGetRoleAndPartsFromMessage(lastEntry, out string role, out var parts) && role == "model")
                {
                    // 檢查此輪 model 回應中是否有多個 function call
                    int functionCallCount = 0;
                    foreach (var part in parts)
                    {
                        if (Client.TryGetFunctionCallFromPart(part, out _, out _))
                        {
                            functionCallCount++;
                        }
                    }

                    if (functionCallCount > 1)
                    {
                        return false; // 多工具同時呼叫時，拒絕注入以避免歷史紀錄結構損壞
                    }

                    ChatHistory.RemoveAt(ChatHistory.Count - 1);
                }
            }

            ChatHistory.Add(Client.BuildMessageContent("model", $"可以請你提供這張圖片給我參考嗎？檔案路徑：{imagePath}"));
            ChatHistory.Add(Client.BuildImageMessageContent("user", $"這是我提供的圖片：{imagePath}", mimeType, base64Data));
            return true;
        }

        /// <summary>
        /// 處理 Function Call 完成後的歷史紀錄更新。
        /// </summary>
        private void HandleFunctionCallHistoryUpdate(List<object> toolResponseParts)
        {
            if (toolResponseParts == null || toolResponseParts.Count == 0) return;

            if (_modelSwitchHappenedInThisTurn && toolResponseParts.Count == 1)
            {
                // 模型切換時，為了讓新模型擁有乾淨的上下文，
                // 移除剛才在 ExecuteAsync 中加入的 Tool Call Request，且不加入本次的 Tool Response。
                if (ChatHistory.Count >= 1)
                {
                    ChatHistory.RemoveAt(ChatHistory.Count - 1);
                }
                _modelSwitchHappenedInThisTurn = false;
            }
            else
            {
                ChatHistory.Add(Client.BuildFunctionMessageContent(toolResponseParts));
            }
        }

        /// <summary>
        /// 處理執行過程中的錯誤，並嘗試備份對話紀錄。
        /// </summary>
        private void HandleExecutionError(Exception ex, IAgentUI ui)
        {
            UsageLogger.LogError($"Agent Error: {ex.Message}");
            ui.ReportError(ex.Message);
            
            if (ChatHistory.Count > 0)
            {
                var lastEntry = ChatHistory[ChatHistory.Count - 1];
                if (Client.TryGetRoleAndPartsFromMessage(lastEntry, out string lastRole, out _) && lastRole == "model")
                {
                    ChatHistory.RemoveAt(ChatHistory.Count - 1);
                }
            }
            
            string recoveryPath = $"{AgentName}_recovery_history.json";
            if (SaveChatHistory(recoveryPath))
            {
                ui.ReportError($"對話紀錄已自動備份至 {recoveryPath}。您可以使用 /load {recoveryPath} 來載入並重試。");
            }
            else
            {
                ui.ReportError("無法備份對話紀錄。");
            }
        }

        /// <summary>
        /// 檢查是否超過最大對話次數，並詢問使用者是否繼續。
        /// </summary>
        private async Task<bool> CheckMaxIterationsAndPromptAsync(int maxIterations, IAgentUI ui)
        {
            bool shouldContinue = await ui.PromptContinueAsync($"已達到單次最大執行次數 ({maxIterations})，任務尚未完成。");
            if (!shouldContinue)
            {
                ui.ReportError("任務已被使用者中斷。");
                string recoveryPath = $"{AgentName}_interrupted_history.json";
                SaveChatHistory(recoveryPath);
                ui.ReportError($"目前對話已存檔至 {recoveryPath}。");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 處理模型發出的特定工具呼叫。子類別必須實作此方法以提供實際的工具行為。
        /// </summary>
        protected abstract Task<string> ProcessToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// 當歷史紀錄過長（超過 Token 閾值）時，自動對前半段對話進行壓縮與摘要，以釋放 Token 空間。
        /// 並嘗試提取明確資訊存入知識庫中。
        /// </summary>
        private async Task CompressHistoryAsync(IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            if (ChatHistory.Count < 6) return;

            int actualSplitIndex = FindCompressSplitIndex();
            if (actualSplitIndex <= 0)
            {
                UsageLogger.LogError("CompressHistory: 找不到適合分割點，跳過壓縮");
                return;
            }

            ui.ReportThinking(0, "Fast Model (正在壓縮與清理對話歷史紀錄...)");

            var historyToCompress = ChatHistory.GetRange(0, actualSplitIndex);
            string jsonToCompress = JsonTools.Serialize(historyToCompress);

            string compressPrompt = "請將以下歷史對話紀錄進行詳細摘要，保留重要的上下文、決策過程、變數設定與關鍵資訊。\n" +
                                    "此外，如果有任何明確的、未來可能會用到的確切資訊（例如特定的路徑、命令、設定值、剛剛確定的規則），請將這些明確資訊獨立列出。\n" +
                                    "請嚴格使用以下 XML 標籤格式輸出：\n" +
                                    "<Summary>\n你的摘要內容\n</Summary>\n" +
                                    "<Knowledge>\n明確資訊（條列式）\n</Knowledge>\n\n" +
                                    "歷史對話紀錄如下：\n" + jsonToCompress;

            try
            {
                string resultText = await GenerateSummaryAsync(compressPrompt, cancellationToken);
                if (resultText != null)
                {
                    string summaryText = resultText;
                    string knowledgeText = "";

                    var summaryMatch = System.Text.RegularExpressions.Regex.Match(resultText, @"<Summary>\s*(.*?)\s*</Summary>", System.Text.RegularExpressions.RegexOptions.Singleline);
                    var knowledgeMatch = System.Text.RegularExpressions.Regex.Match(resultText, @"<Knowledge>\s*(.*?)\s*</Knowledge>", System.Text.RegularExpressions.RegexOptions.Singleline);

                    if (summaryMatch.Success) summaryText = summaryMatch.Groups[1].Value.Trim();
                    if (knowledgeMatch.Success) knowledgeText = knowledgeMatch.Groups[1].Value.Trim();

                    if (!string.IsNullOrWhiteSpace(knowledgeText))
                    {
                        try
                        {
                            var fileTools = new FileTools();
                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string knowledgeFileName = $".agent/knowledge/history_{timestamp}.md";
                            
                            fileTools.WriteFile(knowledgeFileName, "# 歷史紀錄自動萃取資訊\n\n" + knowledgeText, append: false);
                            
                            string indexPath = ".agent/knowledge/00_INDEX.md";
                            fileTools.WriteFile(indexPath, $"\n- [{Path.GetFileName(knowledgeFileName)}] 歷史紀錄過長壓縮時自動萃取的明確資訊", append: true);
                        }
                        catch (Exception kex)
                        {
                            UsageLogger.LogError($"Save Knowledge Error: {kex.Message}");
                        }
                    }

                    ApplyHistoryCompression(actualSplitIndex, summaryText, ui);
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"History Compression Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 尋找歷史紀錄中適合進行壓縮分割的索引位置 (從一半開始找尋第一個 user message)。
        /// </summary>
        private int FindCompressSplitIndex()
        {
            int targetSplitIndex = ChatHistory.Count / 2;
            for (int i = targetSplitIndex; i < ChatHistory.Count; i++)
            {
                if (Client.TryGetRoleAndPartsFromMessage(ChatHistory[i], out string role, out _) && role == "user")
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 呼叫 Fast 模型產生歷史紀錄的摘要。
        /// </summary>
        private async Task<string> GenerateSummaryAsync(string prompt, System.Threading.CancellationToken cancellationToken = default)
        {
            var request = new GenerateContentRequest
            {
                Contents = new List<object>
                {
                    FastClient.BuildMessageContent("user", prompt)
                },
                MockProviderName = MockProviderName
            };

            string rawJson = await FastClient.GenerateContentAsync(request, cancellationToken);
            var data = JsonTools.Deserialize<Dictionary<string, object>>(rawJson);

            return FastClient.ExtractTextFromResponseData(data) ?? "摘要失敗";
        }

        /// <summary>
        /// 將歷史紀錄的前半段替換為產生的摘要。
        /// </summary>
        private void ApplyHistoryCompression(int splitIndex, string summaryText, IAgentUI ui)
        {
            ChatHistory.RemoveRange(0, splitIndex);
            ChatHistory.Insert(0, Client.BuildMessageContent("user", "以下是之前對話的歷史摘要：\n" + summaryText));
            ChatHistory.Insert(1, Client.BuildMessageContent("model", "已收到歷史紀錄摘要，我會根據這些資訊上下文繼續回應並執行任務。"));
            ui.ReportToolResult($"歷史紀錄 Token 過高，已自動將前半段 ({splitIndex} 則對話) 壓縮為摘要，釋放 Token 空間。");
        }

        /// <summary>
        /// 清除當前的對話紀錄，以便開始全新的對話循環。
        /// </summary>
        public void ClearChatHistory()
        {
            ChatHistory.Clear();
        }

        /// <summary>
        /// 將目前的對話歷史紀錄儲存至指定的檔案。
        /// </summary>
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

        /// <summary>
        /// 從指定的檔案載入對話歷史紀錄。
        /// </summary>
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
        /// 取得目前的對話紀錄 (唯讀)，主要提供給外部 (如 UI) 顯示使用。
        /// </summary>
        public ReadOnlyCollection<object> GetChatHistory()
        {
            return ChatHistory.AsReadOnly();
        }


    }
}
