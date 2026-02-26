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
    public class AIControlModule : IAgentModule
    {
        private readonly Action<string> _onSwitchMode;
        private readonly Func<bool> _isSmartMode;
        private readonly bool _hasDifferentFastModel;

        public AIControlModule(Action<string> onSwitchMode, Func<bool> isSmartMode, bool hasDifferentFastModel = true)
        {
            _onSwitchMode = onSwitchMode;
            _isSmartMode = isSmartMode;
            _hasDifferentFastModel = hasDifferentFastModel;
        }

        public IEnumerable<object> GetToolDeclarations(IAIClient client)
        {
            if (_hasDifferentFastModel)
            {
                bool isSmart = _isSmartMode?.Invoke() ?? true;
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
        }

        public Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            if (funcName == "switch_model_mode")
            {
                string mode = args["mode"].ToString();
                _onSwitchMode?.Invoke(mode);
                return Task.FromResult($"成功：已切換至 {mode} 模式。接下來的對話將使用此模式的模型進行回應。");
            }
            return Task.FromResult<string>(null);
        }
    }
}
