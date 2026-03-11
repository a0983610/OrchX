using System.Threading.Tasks;

namespace Antigravity02.UI
{
    /// <summary>
    /// 定義 AI Agent 的視覺與進度回饋介面
    /// 讓後續不論是 Console 還是 Browser UI 都能對接
    /// </summary>
    public interface IAgentUI
    {
        void ReportThinking(int iteration, string modelName);
        void ReportToolCall(string toolName, string args);
        void ReportToolResult(string resultSummary);
        void ReportTextResponse(string text, string modelName);
        void ReportError(string message);
        void ReportInfo(string message);
        Task<bool> PromptContinueAsync(string message);
        Task<int> PromptSelectionAsync(string message, params string[] options);
    }
}
