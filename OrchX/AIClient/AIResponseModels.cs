using System.Collections.Generic;

namespace OrchX.AIClient
{
    /// <summary>
    /// 定義統一的模型回應資料結構
    /// </summary>
    public class UnifiedAIResponse
    {
        public string Role { get; set; } = "assistant";
        
        /// <summary>
        /// 模型回傳的純文字內容
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// 模型請求呼叫的函式與參數列表
        /// </summary>
        public List<UnifiedToolCall> ToolCalls { get; set; } = new List<UnifiedToolCall>();
        
        /// <summary>
        /// 本次請求的 Token 使用量
        /// </summary>
        public TokenUsage Usage { get; set; }
    }

    /// <summary>
    /// 定義統一的函式呼叫結構
    /// </summary>
    public class UnifiedToolCall
    {
        /// <summary>
        /// 函式名稱
        /// </summary>
        public string FunctionName { get; set; }
        
        /// <summary>
        /// 傳入函式的參數
        /// </summary>
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 定義統一的 Token 使用量統計
    /// </summary>
    public class TokenUsage
    {
        public int PromptTokens { get; set; }
        public int CandidateTokens { get; set; }
        public int TotalTokens { get; set; }
    }
}
