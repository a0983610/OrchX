using System.Collections.Generic;

namespace OrchX.AIClient
{
    /// <summary>
    /// Gemini 及預設 Agent 所使用的請求格式
    /// </summary>
    public class GenerateContentRequest
    {
        public object Contents { get; set; }
        public List<object> Tools { get; set; }
        public string SystemInstruction { get; set; }
        public string MockProviderName { get; set; }
    }

    /// <summary>
    /// 通用型的 AI 請求資料結構，用於定義與各類語言模型 (如 Ollama / Gemma 等) 通訊的基礎欄位
    /// </summary>
    public class UnifiedAIRequest
    {
        public string Model { get; set; }
        public object Messages { get; set; }
        public List<object> Tools { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public bool? Stream { get; set; }
    }
}
