using System.Collections.Generic;
using System.Threading.Tasks;
using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
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
            RegisterModule(new FileModule(this));
            RegisterModule(new HttpModule());
            RegisterModule(new AIControlModule(this));
            RegisterModule(new MultiAgentModule(this));
            RegisterModule(new TerminalModule());
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

        protected override async Task<string> ProcessToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            foreach (var module in _modules)
            {
                string result = await module.TryHandleToolCallAsync(funcName, args, ui, cancellationToken);
                if (result != null)
                {
                    return result;
                }
            }

            return $"Error: Unknown tool '{funcName}'.";
        }

        protected override string BuildSystemFixedInfo()
        {
            string info = base.BuildSystemFixedInfo();

            var activeTasks = System.Linq.Enumerable.ToList(TaskOrchestrator.GetActiveTasks());
            if (activeTasks.Count > 0)
            {
                info += "[Active Async Tasks (consult_expert)]\n";
                foreach (var t in activeTasks)
                {
                    info += $"- TaskId: {t.TaskId}, Assignee: {t.Assignee}, Status: {t.Status}, Request: {t.Request}\n";
                }
                info += "\n";
            }

            return info;
        }
    }
}
