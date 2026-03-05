using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 專家 Session：維護單一專家的角色設定與對話歷史
    /// </summary>
    internal class ExpertSession
    {
        public string Role { get; set; }
        public List<object> History { get; set; } = new List<object>();
        
        /// <summary>
        /// 用於保護 History 集合的多執行緒存取
        /// </summary>
        public readonly object HistoryLock = new object();
    }

    /// <summary>
    /// 多重 AI 代理模組：允許主 Agent 創建並諮詢其他特定角色的 AI 專家
    /// 支援多輪對話，每位專家擁有獨立的記憶與上下文
    /// </summary>
    public class MultiAgentModule : BaseAgentModule
    {
        private readonly IAIClient _client;

        private readonly List<object> _expertToolDeclarations = new List<object>();
        private readonly FileModule _fileModule;
        private readonly HttpModule _httpModule;

        /// <summary>
        /// 以 expert_name 為 Key 管理多個專家 Session，支援多執行緒安全存取
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExpertSession> _sessions = new System.Collections.Concurrent.ConcurrentDictionary<string, ExpertSession>(StringComparer.OrdinalIgnoreCase);

        public MultiAgentModule(BaseAgent agent)
        {
            _client = agent?.SmartClient;
            
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

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            yield return client.CreateFunctionDeclaration(
                "consult_expert",
                "諮詢一個特定領域的 AI 專家，支援多輪對話。使用相同的 expert_name 可延續先前的對話。\n" +
                "首次建立專家時必須提供 role，之後追問只需 expert_name 和 question。\n" +
                "目前活躍的專家列表會在回應中附帶提示。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        expert_name = new { type = "string", description = "專家的識別名稱 (例如 'security_expert', 'arch_expert')，用於多輪對話時識別同一位專家" },
                        question = new { type = "string", description = "要問專家的具體問題或任務內容" },
                        role = new { type = "string", description = "專家的角色設定與專業背景 (System Instruction)。首次建立專家時必填，後續追問可省略" },
                        is_async = new { type = "boolean", description = "是否使用非同步背景執行。若任務可能耗時較長請設為 true，系統會立即回傳 TaskId；若需要立即知道答案請設為 false (預設)。" }
                    },
                    required = new[] { "expert_name", "question" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "check_task_status",
                "查詢非同步指派給專家的任務狀態與結果。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        taskId = new { type = "string", description = "任務編號" }
                    },
                    required = new[] { "taskId" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "list_experts",
                "列出目前所有活躍中的專家 Session，包含名稱、角色設定、對話輪數。",
                new
                {
                    type = "object",
                    properties = new { }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "dismiss_expert",
                "結束某位專家的 Session，釋放其對話歷史。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        expert_name = new { type = "string", description = "要結束的專家識別名稱" }
                    },
                    required = new[] { "expert_name" }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            switch (funcName)
            {
                case "consult_expert":
                    return await HandleConsultExpertAsync(funcName, args, ui);

                case "check_task_status":
                    return HandleCheckTaskStatus(funcName, args);

                case "list_experts":
                    return ListExperts();

                case "dismiss_expert":
                    return HandleDismissExpert(funcName, args);

                default:
                    return null;
            }
        }

        private async Task<string> HandleConsultExpertAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            string errCE = CheckRequiredArgs(funcName, args);
            if (errCE != null) return errCE;

            string expertName = args.ContainsKey("expert_name") ? args["expert_name"].ToString() : "default";
            string question = args.ContainsKey("question") ? args["question"].ToString() : "";
            string role = args.ContainsKey("role") ? args["role"].ToString() : null;
            
            bool isAsync = false;
            if (args.TryGetValue("is_async", out object valAsync) && valAsync != null)
            {
                if (valAsync is bool bVal) isAsync = bVal;
                else bool.TryParse(valAsync.ToString(), out isAsync);
            }

            if (!isAsync)
            {
                return await ConsultExpertAsync(expertName, question, role, ui);
            }
            else
            {
                var taskItem = TaskOrchestrator.AddTask(expertName, question);
                var safeUi = new SafeAgentUI(ui);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, Antigravity02.Tools.TaskStatus.Running, null);
                        string result = await ConsultExpertAsync(expertName, question, role, safeUi);
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, Antigravity02.Tools.TaskStatus.Completed, result);
                    }
                    catch (Exception ex)
                    {
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, Antigravity02.Tools.TaskStatus.Failed, $"Exception: {ex.Message}");
                    }
                });
                return $"[System]: 任務已非同步指派給專家 {expertName}，任務編號為 {taskItem.TaskId}。您可以稍後呼叫 check_task_status 查詢進度與結果。";
            }
        }

        private string HandleCheckTaskStatus(string funcName, Dictionary<string, object> args)
        {
            string errCTS = CheckRequiredArgs(funcName, args);
            if (errCTS != null) return errCTS;

            string tid = args.ContainsKey("taskId") ? args["taskId"].ToString() : "";
            var t = TaskOrchestrator.GetTask(tid);
            if (t == null)
            {
                return $"[System]: 找不到任務編號 {tid}";
            }

            if (t.Status == Antigravity02.Tools.TaskStatus.Completed)
            {
                return t.Result;
            }
            else if (t.Status == Antigravity02.Tools.TaskStatus.Failed)
            {
                return $"[System Error]: 任務 {tid} 執行失敗: {t.Result}";
            }
            else
            {
                return $"[System]: 任務 {tid} 仍在處理中，請稍候再查。";
            }
        }

        private string HandleDismissExpert(string funcName, Dictionary<string, object> args)
        {
            string errDE = CheckRequiredArgs(funcName, args);
            if (errDE != null) return errDE;

            string dismissName = args.ContainsKey("expert_name") ? args["expert_name"].ToString() : "";
            return DismissExpert(dismissName);
        }

        private async Task<string> ConsultExpertAsync(string expertName, string question, string role, IAgentUI ui)
        {
            ExpertSession session = null;
            bool isNewSession = false;
            int historySnapshot = 0;

            try
            {
                // 取得或建立 Session

                if (_sessions.TryGetValue(expertName, out session))
                {
                    // 如果有提供新的 role，更新它 (允許動態調整專家角色)
                    if (!string.IsNullOrEmpty(role))
                    {
                        session.Role = role;
                    }
                }
                else
                {
                    // 建立新 Session
                    if (string.IsNullOrEmpty(role))
                    {
                        return $"[System Error]: 建立新專家 '{expertName}' 時必須提供 'role' 設定（專業背景與指導原則）。";
                    }
                    session = new ExpertSession { Role = role };
                    isNewSession = true;
                    
                    // 如果被其他執行緒搶先建立，則使用已經建立的版本
                    session = _sessions.GetOrAdd(expertName, session);
                    if (session.Role != role)
                    {
                         isNewSession = false; // 代表不是我們剛才建立的
                         // 如果有提供新的 role，更新它 (允許動態調整專家角色)
                         if (!string.IsNullOrEmpty(role))
                         {
                             session.Role = role;
                         }
                    }
                }

                int currentTurn = 0;
                List<object> currentHistorySnapshot;
                lock (session.HistoryLock)
                {
                    // 記錄快照點，用於異常時回滾
                    historySnapshot = session.History.Count;
                    currentTurn = (session.History.Count / 2) + 1;
                    
                    // 將使用者問題加入此專家的對話歷史
                    session.History.Add(_client.BuildMessageContent("user", question));
                    
                    // 複製一份歷史供這輪對話使用
                    currentHistorySnapshot = new List<object>(session.History);
                }

                // --- UI: 顯示諮詢開始 ---
                if (isNewSession)
                {
                    ui.ReportInfo($"\n[Expert: {expertName}] 建立新專家 Session");
                    ui.ReportInfo($"[Expert: {expertName}] 角色: {Truncate(session.Role, 80)}");
                }
                else
                {
                    ui.ReportInfo($"\n[Expert: {expertName}] 第 {currentTurn} 輪對話");
                }
                ui.ReportInfo($"[Expert: {expertName}] 提問: {Truncate(question, 120)}");
                ui.ReportInfo($"[Expert: {expertName}] 等待回應中...");

                int maxIterations = 20;
                int iterations = 0;
                string finalResponseText = null;

                while (iterations < maxIterations)
                {
                    iterations++;

                    // 建立 API 請求 (使用完整的對話歷史，並給予工具)
                    var request = new GenerateContentRequest
                    {
                        SystemInstruction = session.Role,
                        Contents = currentHistorySnapshot,
                        Tools = _expertToolDeclarations.Count > 0 ? _expertToolDeclarations : null,
                        MockProviderName = $"gemini_expert_{expertName}"
                    };

                    string responseJson = await _client.GenerateContentAsync(request);

                    // 解析回應
                    var data = JsonTools.Deserialize<Dictionary<string, object>>(responseJson);
                    var parts = _client.ExtractResponseParts(data, out var modelContent);

                    if (parts == null || parts.Count == 0) break;

                    // 將模型回應加入對話歷史 (保持多輪對話)
                    if (modelContent != null)
                    {
                        lock (session.HistoryLock)
                        {
                            session.History.Add(modelContent);
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
                                ui.ReportInfo($"[Expert: {expertName}] {partText}");
                            }
                        }

                        if (_client.TryGetFunctionCallFromPart(part, out string funcName, out var argsDict))
                        {
                            hasFunctionCall = true;

                            ui.ReportInfo($"[Expert: {expertName}] 呼叫工具 {funcName}...");

                            // 執行工具呼叫
                            string result = await _fileModule.TryHandleToolCallAsync(funcName, argsDict, ui);
                            if (result == null) result = await _httpModule.TryHandleToolCallAsync(funcName, argsDict, ui);
                            if (result == null) result = "Error: Unknown tool.";

                            toolResponseParts.Add(_client.BuildToolResponsePart(funcName, result));
                            ui.ReportInfo($"[Expert: {expertName}] 工具返回結果長度: {result.Length}");
                        }
                    }

                    if (hasFunctionCall)
                    {
                        // 加入 function 回應，繼續下一輪
                        var funcMessage = _client.BuildFunctionMessageContent(toolResponseParts);
                        lock (session.HistoryLock)
                        {
                            session.History.Add(funcMessage);
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
                    lock (session.HistoryLock)
                    {
                         turnCount = session.History.Count / 2;
                    }
                    string sessionInfo = isNewSession
                        ? $" (新建專家 Session，角色: {Truncate(session.Role, 50)})"
                        : $" (第 {turnCount} 輪對話)";

                    return $"[專家 {expertName} 回應]{sessionInfo}：\n{finalResponseText}";
                }

                // 回應失敗時，回滾到快照點（清除本次所有殘留記錄）
                lock (session.HistoryLock)
                {
                    if (session.History.Count > historySnapshot)
                    {
                        session.History.RemoveRange(historySnapshot, session.History.Count - historySnapshot);
                    }
                }
                return $"[System]: 專家 {expertName} 沒有回應或超出反覆迭代次數。";
            }
            catch (Exception ex)
            {
                // 異常時回滾：回到快照點，清除迴圈中所有殘留記錄
                if (session != null)
                {
                    lock (session.HistoryLock)
                    {
                        if (session.History.Count > historySnapshot)
                        {
                            session.History.RemoveRange(historySnapshot, session.History.Count - historySnapshot);
                        }
                    }
                }
                UsageLogger.LogError($"ConsultExpert({expertName}) Error: {ex.Message}");
                return $"[System Error] 諮詢專家 {expertName} 時發生錯誤: {ex.Message}";
            }
        }

        private string ListExperts()
        {
            if (_sessions.Count == 0)
            {
                return "目前沒有活躍的專家 Session。";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"目前有 {_sessions.Count} 位活躍的專家：");
            sb.AppendLine();

            foreach (var kvp in _sessions)
            {
                int turns;
                lock (kvp.Value.HistoryLock)
                {
                     turns = kvp.Value.History.Count / 2;
                }
                string rolePreview = Truncate(kvp.Value.Role, 60);
                sb.AppendLine($"  [{kvp.Key}] 對話輪數: {turns} | 角色: {rolePreview}");
            }

            return sb.ToString().TrimEnd();
        }

        private string DismissExpert(string expertName)
        {
            if (string.IsNullOrEmpty(expertName))
            {
                return "[System]: 請指定要結束的專家名稱。";
            }

            if (_sessions.TryRemove(expertName, out var sessionToRemove))
            {
                int turns;
                lock (sessionToRemove.HistoryLock)
                {
                     turns = sessionToRemove.History.Count / 2;
                }
                return $"已結束專家 {expertName} 的 Session（共進行了 {turns} 輪對話）。";
            }

            return $"[System]: 找不到名為 {expertName} 的專家。";
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
        }

        private class SafeAgentUI : IAgentUI
        {
            private readonly IAgentUI _inner;
            public SafeAgentUI(IAgentUI inner) { _inner = inner; }
            public void ReportThinking(int iteration, string modelName) { try { _inner?.ReportThinking(iteration, modelName); } catch { } }
            public void ReportToolCall(string toolName, string args) { try { _inner?.ReportToolCall(toolName, args); } catch { } }
            public void ReportToolResult(string resultSummary) { try { _inner?.ReportToolResult(resultSummary); } catch { } }
            public void ReportTextResponse(string text, string modelName) { try { _inner?.ReportTextResponse(text, modelName); } catch { } }
            public void ReportError(string message) { try { _inner?.ReportError(message); } catch { } }
            public void ReportInfo(string message) { try { _inner?.ReportInfo(message); } catch { } }
            public Task<bool> PromptContinueAsync(string message)
            {
                try { return _inner != null ? _inner.PromptContinueAsync(message) : Task.FromResult(true); }
                catch { return Task.FromResult(true); }
            }
        }
    }
}
