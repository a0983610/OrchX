using System.Collections.Generic;
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
    public class ExpertAgent : BaseAgent
    {
        public string Name { get; }
        public string Role 
        { 
            get => SystemInstruction;
            set => SystemInstruction = value;
        }

        private readonly List<IAgentModule> _modules = new List<IAgentModule>();

        public ExpertAgent(string name, string role, IAIClient client) : base(client, client)
        {
            Name = name;
            Role = role;

            RegisterModule(new FileModule(this));
            RegisterModule(new HttpModule());
            
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
    }
}
