using System.Collections.Generic;
using System.Threading.Tasks;
using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 萬能 Agent：整合多個功能模組，具備全方位的工具箱
    /// </summary>
    public class UniversalAgent : BaseAgent
    {
        private readonly List<IAgentModule> _modules = new List<IAgentModule>();

        public UniversalAgent(IAIClient smartClient, IAIClient fastClient, string systemInstruction = null) : base(smartClient, fastClient)
        {
            // 在此註冊所有模組
            // 只有 Smart 和 Fast 為不同模型時，才啟用摘要功能（避免浪費相同模型的 API 呼叫）
            bool hasDifferentFastModel = SmartClient.ModelName != FastClient.ModelName;
            RegisterModule(new FileModule(hasDifferentFastModel ? FastClient : null));
            RegisterModule(new HttpModule());
            RegisterModule(new AIControlModule(this.SetModelMode, () => this.IsSmartMode, hasDifferentFastModel));
            RegisterModule(new MultiAgentModule(smartClient));
            // 未來可以輕鬆加入更多模組，例如：
            // RegisterModule(new WebSearchModule());
            // RegisterModule(new DatabaseModule());
            
            SystemInstruction = systemInstruction ?? "";
            
            InitializeToolDeclarations();
        }

        public void RegisterModule(IAgentModule module)
        {
            _modules.Add(module);
        }

        private void InitializeToolDeclarations()
        {
            var allDeclarations = new List<object>();
            foreach (var module in _modules)
            {
                allDeclarations.AddRange(module.GetToolDeclarations(Client));
            }

            if (allDeclarations.Count > 0)
            {
                ToolDeclarations = new List<object>(Client.DefineTools(allDeclarations.ToArray()));
            }
        }

        /// <summary>
        /// 模型模式切換時，重新初始化工具宣告以匹配新模型
        /// </summary>
        protected override void OnModelModeChanged()
        {
            InitializeToolDeclarations();
        }

        protected override async Task<string> ProcessToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            foreach (var module in _modules)
            {
                string result = await module.TryHandleToolCallAsync(funcName, args, ui);
                if (result != null)
                {
                    return result;
                }
            }

            return "Error: Unknown tool.";
        }
    }
}
