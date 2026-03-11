using System.Collections.Generic;
using System.Threading.Tasks;

using Antigravity02.AIClient;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 定義一個工具模組，包含工具宣告與具體的處理邏輯
    /// </summary>
    public interface IAgentModule
    {
        /// <summary>
        /// 獲取此模組提供的所有工具宣告
        /// </summary>
        IEnumerable<object> GetToolDeclarations(IAIClient client);

        /// <summary>
        /// 嘗試處理工具呼叫
        /// </summary>
        /// <param name="ui">UI 回饋介面，若模組需要向使用者報告進度可使用</param>
        /// <returns>回傳工具執行結果，若此模組無法處理則回傳 null</returns>
        Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default);
    }
}
