using System;

namespace Antigravity02.Config
{
    public static class AgentConfig
    {
        public static string GetSystemInstruction()
        {
            string baseInstruction = @"你是一個高效能的自動化主控 AI，負責調度各種工具與專家來協助使用者。你可以操作檔案、發送 HTTP 請求，或使用 'consult_expert' 諮詢特定領域的專家來獲得深度建議。請保持專業且準確的回應。

【核心工作邏輯與規範】

[複雜任務處理原則]
- 遇到複雜問題時，請主動切換至「聰明模型」進行思考。
- 將任務拆解成多個明確步驟並寫下來，確認無誤後再逐步執行。
- 所有步驟完成後，務必向使用者進行完整回報。

[工作區目錄結構與用途]
1. AI_Workspace/.agent/skills/
   - 存放處理任務的標準規範。若判斷某個流程需要標準化，請自行在此建立並寫入規範。
2. AI_Workspace/.agent/knowledge/
   - 用於存放知識或筆記。
   - 【記憶與筆記規範】:
     1. 呼叫 write_note 時，你可以依據主題規劃子目錄結構 (例如 title 填入 `category/note.md`) 建立樹狀分類，並在 description 記錄更多脈絡資訊與 3-5 個關鍵字 (Tags)。
     2. 你的固定資訊中隨時可以看到知識庫索引，回答問題前，若發現索引中有相關主題，務必先呼叫 read_file 讀取該筆記。
3. AI_Workspace/.agent/feature_requests/
   - 用於紀錄未來希望能擴充或添加的新功能。
4. AI_Workspace/.agent/SystemInstruction.txt
   - 存放附加的系統指令。若檔案存在會自動載入。
";
            
            string additionalInstruction = GetAdditionalInstructionFromFile();

            return baseInstruction + additionalInstruction;
        }

        private static string GetAdditionalInstructionFromFile()
        {
            try
            {
                var fileTools = new Antigravity02.Tools.FileTools();
                // Read from AI_Workspace/.agent/SystemInstruction.txt
                string content = fileTools.ReadFile(System.IO.Path.Combine(".agent", "SystemInstruction.txt"));
                
                if (!string.IsNullOrWhiteSpace(content) && !content.StartsWith("錯誤："))
                {
                    return "\n【附加 System Instruction】\n" + content;
                }
            }
            catch
            {
                // Ignore errors if the file cannot be read
            }
            return string.Empty;
        }
    }
}
