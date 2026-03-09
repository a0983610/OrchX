using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 提供 AI 自我控制與調整的模組
    /// </summary>
    public class AIControlModule : BaseAgentModule
    {
        private readonly BaseAgent _agent;
        private readonly bool _hasDifferentFastModel;

        public AIControlModule(BaseAgent agent)
        {
            _agent = agent;
            _hasDifferentFastModel = agent != null && agent.SmartClient.ModelName != agent.FastClient.ModelName;
        }

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            if (_hasDifferentFastModel && _agent != null)
            {
                bool isSmart = _agent.IsSmartMode;
                string description = isSmart
                    ? "切換 AI 思考模式。當前為[聰明模式]。若任務簡單，建議切換至 'fast' (快速模式) 以節省資源。"
                    : "切換 AI 思考模式。當前為[快速模式]。若任務複雜，建議切換至 'smart' (聰明模式) 以獲得更好的推理能力。";

                yield return client.CreateFunctionDeclaration(
                    "switch_model_mode",
                    description,
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "模式名稱 (smart 或 fast)", @enum = new[] { "smart", "fast" } }
                        },
                        required = new[] { "mode" }
                    }
                );
            }

            yield return client.CreateFunctionDeclaration(
                "refine_my_behavior",
                "當 AI 發現特定任務（如除錯、檔案處理）有更好的執行策略，或需要建立防止錯誤的檢查清單時呼叫此工具。它會向使用者提議更新『附加系統指令』以優化未來的表現。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        reason = new { type = "string", description = "為什麼需要調整指令？描述觀察到的問題或改進點。" },
                        proposed_change = new { type = "string", description = "具體要新增或修改的指令內容。" },
                        action = new { type = "string", description = "是要附加在現有指令後，還是完全替換。", @enum = new[] { "append", "replace" } }
                    },
                    required = new[] { "reason", "proposed_change", "action" }
                }
            );
        }

        public override Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            if (funcName == "switch_model_mode" && _agent != null)
            {
                return HandleSwitchModelMode(funcName, args);
            }
            if (funcName == "refine_my_behavior")
            {
                return HandleRefineMyBehavior(funcName, args, ui);
            }
            return Task.FromResult<string>(null);
        }

        private Task<string> HandleSwitchModelMode(string funcName, Dictionary<string, object> args)
        {
            string error = CheckRequiredArgs(funcName, args);
            if (error != null) return Task.FromResult(error);

            string mode = args["mode"].ToString();
            _agent.SetModelMode(mode);
            return Task.FromResult($"成功：已切換至 {mode} 模式。接下來的對話將使用此模式的模型進行回應。");
        }

        private async Task<string> HandleRefineMyBehavior(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            string error = CheckRequiredArgs(funcName, args);
            if (error != null) return error;

            string reason = args.ContainsKey("reason") ? args["reason"]?.ToString() : "";
            string proposedChange = args.ContainsKey("proposed_change") ? args["proposed_change"]?.ToString() : "";
            string action = args.ContainsKey("action") ? args["action"]?.ToString() : "";

            string promptMessage = $"\n=== 系統指令調整提案 ===\n原因：{reason}\n操作：{action}\n內容：\n{proposedChange}\n==========================\n\n是否同意更新附加系統指令？ (Y/N)：";
            
            bool isApproved = await ui.PromptContinueAsync(promptMessage);
            if (!isApproved)
            {
                return "使用者已拒絕更新系統指令。";
            }

            try
            {
                var fileTools = new FileTools();
                string targetPath = System.IO.Path.Combine(".agent", "SystemInstruction.txt");
                bool append = string.Equals(action, "append", StringComparison.OrdinalIgnoreCase);
                
                string result = fileTools.WriteFile(targetPath, proposedChange, append);
                if (result.StartsWith("錯誤"))
                {
                    return result;
                }

                return "指令已更新，將於下一次任務啟動或新對話時完整生效（因 AgentConfig.cs 會在初始化時讀取此檔）。";
            }
            catch (Exception ex)
            {
                return $"更新系統指令失敗：{ex.Message}";
            }
        }
    }
}
