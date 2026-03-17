using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
{
    /// <summary>
    /// 多重 AI 代理模組：允許主 Agent 創建並諮詢其他特定角色的 AI 專家
    /// 支援多輪對話，每位專家擁有獨立的記憶與上下文
    /// </summary>
    public class MultiAgentModule : BaseAgentModule
    {
        private readonly IAIClient _smartClient;
        private readonly IAIClient _fastClient;

        /// <summary>
        /// 以 expert_name 為 Key 管理多個專家代理，支援多執行緒安全存取
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExpertAgent> _agents = new System.Collections.Concurrent.ConcurrentDictionary<string, ExpertAgent>(StringComparer.OrdinalIgnoreCase);

        public MultiAgentModule(BaseAgent agent)
        {
            _smartClient = agent?.SmartClient;
            _fastClient = agent?.FastClient;
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
                "read_task_result",
                "讀取非同步指派給專家的任務狀態與結果。當收到任務完成的通知時，必須呼叫此工具來獲取並核銷完整結果。",
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

            yield return client.CreateFunctionDeclaration(
                "wait_all_tasks",
                "當有非同步專家任務仍在執行，且目前沒有其他事情可以先做時，呼叫此工具等待所有專家任務完成。",
                new
                {
                    type = "object",
                    properties = new { }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            switch (funcName)
            {
                case "consult_expert":
                    return await HandleConsultExpertAsync(funcName, args, ui, cancellationToken);

                case "read_task_result":
                    return HandleReadTaskResult(funcName, args);

                case "list_experts":
                    return ListExperts();

                case "dismiss_expert":
                    return HandleDismissExpert(funcName, args);

                case "wait_all_tasks":
                    return await HandleWaitAllTasksAsync(funcName, ui, cancellationToken);

                default:
                    return null;
            }
        }

        private async Task<string> HandleConsultExpertAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
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
                return await ConsultExpertAsync(expertName, question, role, ui, cancellationToken);
            }
            else
            {
                var taskItem = TaskOrchestrator.AddTask(expertName, question);
                var safeUi = new SafeAgentUI(ui);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, OrchX.Tools.TaskStatus.Running, null);
                        string result = await ConsultExpertAsync(expertName, question, role, safeUi, System.Threading.CancellationToken.None);
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, OrchX.Tools.TaskStatus.Completed, result);
                    }
                    catch (Exception ex)
                    {
                        TaskOrchestrator.UpdateTask(taskItem.TaskId, OrchX.Tools.TaskStatus.Failed, $"Exception: {ex.Message}");
                    }
                });
                return $"[System]: 任務已非同步指派給專家 {expertName}，任務編號為 {taskItem.TaskId}。您可以稍後呼叫 read_task_result 查詢進度與結果。";
            }
        }

        private string HandleReadTaskResult(string funcName, Dictionary<string, object> args)
        {
            string errCTS = CheckRequiredArgs(funcName, args);
            if (errCTS != null) return errCTS;

            string tid = args.ContainsKey("taskId") ? args["taskId"].ToString() : "";
            var t = TaskOrchestrator.GetTask(tid);
            if (t == null)
            {
                return $"[System]: 找不到任務編號 {tid}";
            }

            if (t.Status == OrchX.Tools.TaskStatus.Completed)
            {
                TaskOrchestrator.MarkAsDelivered(tid);
                return t.Result;
            }
            else if (t.Status == OrchX.Tools.TaskStatus.Failed)
            {
                TaskOrchestrator.MarkAsDelivered(tid);
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

        private async Task<string> HandleWaitAllTasksAsync(string funcName, IAgentUI ui, System.Threading.CancellationToken cancellationToken)
        {
            var activeTasks = TaskOrchestrator.GetActiveTasks().ToList();
            if (activeTasks.Count == 0)
            {
                return "[System]: 目前沒有執行中的非同步專家任務。";
            }

            ui?.ReportInfo($"[System]: 等待 {activeTasks.Count} 個專家任務完成...");

            while (TaskOrchestrator.GetActiveTasks().Any())
            {
                await Task.Delay(1000, cancellationToken);
            }

            return "[System]: 所有非同步專家任務已完成。請呼叫 read_task_result 獲取結果。";
        }

        private async Task<string> ConsultExpertAsync(string expertName, string question, string role, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            ExpertAgent agent = null;
            bool isNewAgent = false;

            if (_agents.TryGetValue(expertName, out agent))
            {
                if (!string.IsNullOrEmpty(role))
                {
                    agent.Role = role;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(role))
                {
                    return $"[System Error]: 建立新專家 '{expertName}' 時必須提供 'role' 設定（專業背景與指導原則）。";
                }
                agent = new ExpertAgent(expertName, role, _smartClient, _fastClient);
                isNewAgent = true;

                agent = _agents.GetOrAdd(expertName, agent);
                if (agent.Role != role)
                {
                     isNewAgent = false; 
                     if (!string.IsNullOrEmpty(role))
                     {
                         agent.Role = role;
                     }
                }
            }

            return await ConsultExpertInternalAsync(agent, question, isNewAgent, ui, cancellationToken);
        }

        private async Task<string> ConsultExpertInternalAsync(ExpertAgent agent, string question, bool isNewAgent, IAgentUI ui, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                int currentTurn = (agent.GetChatHistory().Count / 2) + 1;
                
                var prefixedUi = new ExpertPrefixUI(ui, agent.Name);

                if (isNewAgent)
                {
                    prefixedUi.ReportInfo($"\n建立新專家 Session");
                    prefixedUi.ReportInfo($"角色: {Truncate(agent.Role, 80)}");
                }
                else
                {
                    prefixedUi.ReportInfo($"\n第 {currentTurn} 輪對話");
                }
                prefixedUi.ReportInfo($"提問: {Truncate(question, 120)}");
                prefixedUi.ReportInfo($"等待回應中...");

                int historySnapshot = agent.GetChatHistory().Count;

                try
                {
                    await agent.ExecuteAsync(question, prefixedUi, cancellationToken);
                }
                catch (Exception ex)
                {
                    UsageLogger.LogError($"ConsultExpert({agent.Name}) Error: {ex.Message}");
                    return $"[System Error] 諮詢專家 {agent.Name} 時發生錯誤: {ex.Message}";
                }

                // 取得最後一段 model 的回應
                var history = agent.GetChatHistory();
                string finalResponseText = string.Empty;
                
                if (history.Count > historySnapshot)
                {
                    for (int i = history.Count - 1; i >= historySnapshot; i--)
                    {
                        if (agent.SmartClient.TryGetRoleAndPartsFromMessage(history[i], out string role, out var parts) && role == "model")
                        {
                            foreach (var part in parts)
                            {
                                if (agent.SmartClient.TryGetTextFromPart(part, out string text))
                                {
                                    finalResponseText += text + "\n";
                                }
                            }
                            break; // 找到最近一次 model 輸出即可
                        }
                    }
                }

                finalResponseText = finalResponseText.TrimEnd();

                if (!string.IsNullOrEmpty(finalResponseText))
                {
                    int turnCount = agent.GetChatHistory().Count / 2;
                    string sessionInfo = isNewAgent
                        ? $" (新建專家 Session，角色: {Truncate(agent.Role, 50)})"
                        : $" (第 {turnCount} 輪對話)";

                    return $"[專家 {agent.Name} 回應]{sessionInfo}：\n{finalResponseText}";
                }

                return $"[System]: 專家 {agent.Name} 沒有回應或超出反覆迭代次數。";
            }
            catch (Exception ex)
            {
                return $"[System Error] 諮詢專家 {agent.Name} 時發生錯誤: {ex.Message}";
            }
        }

        private string ListExperts()
        {
            if (_agents.Count == 0)
            {
                return "目前沒有活躍的專家。";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"目前有 {_agents.Count} 位活躍的專家：");
            sb.AppendLine();

            foreach (var kvp in _agents)
            {
                int turns = kvp.Value.GetChatHistory().Count / 2;
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

            if (_agents.TryRemove(expertName, out var agentToRemove))
            {
                int turns = agentToRemove.GetChatHistory().Count / 2;
                return $"已結束專家 {expertName} （共進行了 {turns} 輪對話）。";
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
            
            public Task<int> PromptSelectionAsync(string message, params string[] options)
            {
                try { return _inner != null ? _inner.PromptSelectionAsync(message, options) : Task.FromResult(0); }
                catch { return Task.FromResult(0); }
            }
        }

        private class ExpertPrefixUI : IAgentUI
        {
            private readonly IAgentUI _inner;
            private readonly string _prefix;

            public ExpertPrefixUI(IAgentUI inner, string expertName)
            {
                _inner = inner;
                _prefix = $"[Expert: {expertName}]";
            }

            public void ReportThinking(int iteration, string modelName) => _inner?.ReportThinking(iteration, modelName);
            public void ReportToolCall(string toolName, string args) => _inner?.ReportToolCall($"{_prefix} {toolName}", args);
            public void ReportToolResult(string resultSummary) => _inner?.ReportToolResult($"{_prefix} {resultSummary}");
            public void ReportTextResponse(string text, string modelName) => _inner?.ReportTextResponse($"{_prefix} {text}", modelName);
            public void ReportError(string message) => _inner?.ReportError($"{_prefix} {message}");
            public void ReportInfo(string message) => _inner?.ReportInfo($"{_prefix} {message}");
            
            public Task<bool> PromptContinueAsync(string message) => _inner != null ? _inner.PromptContinueAsync($"{_prefix} {message}") : Task.FromResult(true);
            public Task<int> PromptSelectionAsync(string message, params string[] options) => _inner != null ? _inner.PromptSelectionAsync($"{_prefix} {message}", options) : Task.FromResult(0);
        }
    }
}
