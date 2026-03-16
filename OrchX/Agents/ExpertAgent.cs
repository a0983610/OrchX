using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
{
    /// <summary>
    /// 特定領域的 AI 專家代理
    /// 包含獨立的角色設定、對話歷史與工具
    /// </summary>
    public class ExpertAgent
    {
        public string Name { get; }
        public string Role { get; set; }
        
        public List<object> History { get; } = new List<object>();
        public readonly object HistoryLock = new object();

        private readonly IAIClient _client;
        private readonly FileModule _fileModule;
        private readonly HttpModule _httpModule;
        private readonly List<object> _expertToolDeclarations = new List<object>();

        public ExpertAgent(string name, string role, IAIClient client)
        {
            Name = name;
            Role = role;
            _client = client;

            // 初始化專家可用工具模組
            _fileModule = new FileModule((BaseAgent)null); // 專家暫不支援快速模型摘要
            _httpModule = new HttpModule();

            // 收集工具宣告
            var allDeclarations = new List<object>();
            allDeclarations.AddRange(_fileModule.GetToolDeclarations(_client));
            allDeclarations.AddRange(_httpModule.GetToolDeclarations(_client));
            
            if (allDeclarations.Count > 0)
            {
                _expertToolDeclarations = new List<object>(_client.DefineTools(allDeclarations.ToArray()));
            }
        }

        public async Task<string> ConsultAsync(string question, bool isNewSession, IAgentUI ui, CancellationToken cancellationToken = default)
        {
            int historySnapshot = 0;
            int currentTurn = 0;
            List<object> currentHistorySnapshot;

            try
            {
                lock (HistoryLock)
                {
                    // 記錄快照點，用於異常時回滾
                    historySnapshot = History.Count;
                    currentTurn = (History.Count / 2) + 1;
                    
                    // 將使用者問題加入此專家的對話歷史
                    History.Add(_client.BuildMessageContent("user", question));
                    
                    // 複製一份歷史供這輪對話使用
                    currentHistorySnapshot = new List<object>(History);
                }

                // --- UI: 顯示諮詢開始 ---
                if (isNewSession)
                {
                    ui.ReportInfo($"\n[Expert: {Name}] 建立新專家 Session");
                    ui.ReportInfo($"[Expert: {Name}] 角色: {Truncate(Role, 80)}");
                }
                else
                {
                    ui.ReportInfo($"\n[Expert: {Name}] 第 {currentTurn} 輪對話");
                }
                ui.ReportInfo($"[Expert: {Name}] 提問: {Truncate(question, 120)}");
                ui.ReportInfo($"[Expert: {Name}] 等待回應中...");

                int maxIterations = 20;
                int iterations = 0;
                string finalResponseText = null;

                while (iterations < maxIterations)
                {
                    iterations++;

                    // 建立 API 請求 (使用完整的對話歷史，並給予工具)
                    var request = new GenerateContentRequest
                    {
                        SystemInstruction = Role,
                        Contents = currentHistorySnapshot,
                        Tools = _expertToolDeclarations.Count > 0 ? _expertToolDeclarations : null,
                        MockProviderName = $"gemini_expert_{Name}"
                    };

                    string responseJson = await _client.GenerateContentAsync(request, cancellationToken);

                    // 解析回應
                    var data = JsonTools.Deserialize<Dictionary<string, object>>(responseJson);
                    var parts = _client.ExtractResponseParts(data, out var modelContent);

                    if (parts == null || parts.Count == 0) break;

                    // 將模型回應加入對話歷史 (保持多輪對話)
                    if (modelContent != null)
                    {
                        lock (HistoryLock)
                        {
                            History.Add(modelContent);
                            currentHistorySnapshot.Add(modelContent);
                        }
                    }

                    bool hasFunctionCall = false;
                    var toolResponseParts = new List<object>();
                    var textParts = new System.Text.StringBuilder();

                    foreach (object part in parts)
                    {
                        if (_client.TryGetTextFromPart(part, out string partText))
                        {
                            textParts.AppendLine(partText);
                            // --- UI: 即時顯示文字思考/片段 ---
                            if (!string.IsNullOrWhiteSpace(partText))
                            {
                                ui.ReportInfo($"[Expert: {Name}] {partText}");
                            }
                        }

                        if (_client.TryGetFunctionCallFromPart(part, out string funcName, out var argsDict))
                        {
                            hasFunctionCall = true;

                            ui.ReportInfo($"[Expert: {Name}] 呼叫工具 {funcName}...");

                            // 執行工具呼叫
                            string result = await _fileModule.TryHandleToolCallAsync(funcName, argsDict, ui, cancellationToken);
                            if (result == null) result = await _httpModule.TryHandleToolCallAsync(funcName, argsDict, ui, cancellationToken);
                            if (result == null) result = $"Error: Unknown tool '{funcName}'.";

                            toolResponseParts.Add(_client.BuildToolResponsePart(funcName, result));
                            ui.ReportInfo($"[Expert: {Name}] 工具返回結果長度: {result.Length}");
                        }
                    }

                    if (hasFunctionCall)
                    {
                        // 加入 function 回應，繼續下一輪
                        var funcMessage = _client.BuildFunctionMessageContent(toolResponseParts);
                        lock (HistoryLock)
                        {
                            History.Add(funcMessage);
                            currentHistorySnapshot.Add(funcMessage);
                        }
                    }
                    else
                    {
                        // 沒有呼叫工具，為最終文字回應
                        if (textParts.Length > 0)
                        {
                            finalResponseText = textParts.ToString().TrimEnd();
                        }
                        break;
                    }
                } // end while

                if (finalResponseText != null)
                {
                    int turnCount;
                    lock (HistoryLock)
                    {
                         turnCount = History.Count / 2;
                    }
                    string sessionInfo = isNewSession
                        ? $" (新建專家 Session，角色: {Truncate(Role, 50)})"
                        : $" (第 {turnCount} 輪對話)";

                    return $"[專家 {Name} 回應]{sessionInfo}：\n{finalResponseText}";
                }

                // 回應失敗時，回滾到快照點
                lock (HistoryLock)
                {
                    if (History.Count > historySnapshot)
                    {
                        History.RemoveRange(historySnapshot, History.Count - historySnapshot);
                    }
                }
                return $"[System]: 專家 {Name} 沒有回應或超出反覆迭代次數。";
            }
            catch (Exception ex)
            {
                // 異常時回滾
                lock (HistoryLock)
                {
                    if (History.Count > historySnapshot)
                    {
                        History.RemoveRange(historySnapshot, History.Count - historySnapshot);
                    }
                }
                UsageLogger.LogError($"ConsultExpert({Name}) Error: {ex.Message}");
                return $"[System Error] 諮詢專家 {Name} 時發生錯誤: {ex.Message}";
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
        }
    }
}
