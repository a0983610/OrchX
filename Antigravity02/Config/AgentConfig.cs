using System;

namespace Antigravity02.Config
{
    public static class AgentConfig
    {
        public static string GetSystemInstruction()
        {
            string baseInstruction = @"你是一個高效能的自動化主控 AI，負責調度各種工具與專家來協助使用者。你可以操作檔案、發送 HTTP 請求，或使用 'consult_expert' 諮詢特定領域的 AI 專家來獲得深度建議。請專業且準確地回應。

【Agent 基本邏輯規則】
1. AI_Workspace/.agent/skills/：此資料夾內有處理事情的規範。當你判斷有需要將某個流程寫成規範時，請自行建立並寫入。
2. AI_Workspace/.agent/knowledge/：此資料夾用於存放知識或筆記。建立時，開頭需比照 skill 規範使用 YAML 格式寫下此篇記事的摘要（description 等），然後再寫內文。
3. AI_Workspace/.agent/feature_requests/：你可以將希望未來能添加的新功能紀錄在這個資料夾內。
4. AI_Workspace/.agent/SystemInstruction.txt：附加的 SystemInstruction。如果有該檔案，會自動附加到此處。
";
            
            string additionalInstruction = GetAdditionalInstructionFromFile();

            return baseInstruction + additionalInstruction;
        }

        private static string GetAdditionalInstructionFromFile()
        {
            try
            {
                // Try to read additional instructions from the specific file
                string agentDirPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI_Workspace", ".agent");
                string instructionFilePath = System.IO.Path.Combine(agentDirPath, "SystemInstruction.txt");
                
                if (System.IO.File.Exists(instructionFilePath))
                {
                    return "\n【附加 System Instruction】\n" + System.IO.File.ReadAllText(instructionFilePath);
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
