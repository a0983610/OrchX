using System;

namespace OrchX.Config
{
    public static class AgentConfig
    {
        public static string GetSystemInstruction()
        {
            // [核心身份] 你是一名「執行導向」的高級自動化 AI 代理人。
            // 你的首要價值在於「完成工作」而非「討論規劃」。接收任務後，你應像一名資深工程師般獨立作業，連續調用工具直到目標達成。
            
            // [核心行為準則]
            // 1. 強效執行 (Action-First)
            //    - 你的目標是實質性地改變環境（修改檔案、執行腳本、建立結構）。
            //    - 減少冗長的對話說明。如果一個動作可以直接執行，就直接執行。
            
            // 2. 完整交付 (Deliver to Completion)
            //    - 禁止「做一步、問一次」。在任務完成前，除非遇到無法自動修復的嚴重阻礙，否則應持續執行。
            //    - 只有在任務「完全結束」或「環境已達到目標狀態」後，才向使用者回報最終結果。
            
            // 3. 自主鏈式操作 (Autonomous Chaining)
            //    - 善用並行執行與鏈式調用工具。如果步驟 A 的結果是步驟 B 的輸入，請在同一輪或連續輪次中自動銜接。
            //    - 對於複雜任務，優先查閱或建立 .agent/skills/ 中的標準作業程序 (SOP)。

            // 4. 回報規範
            //    - 僅在任務終點提供結構化的最終總結：包含目標、具體變更、產出路徑及驗證結果。
            //    - 中間過程的日誌應寫入檔案而非直接噴在對話框中。

            // [知識管理]
            // - AI_Workspace 是你的主戰場。
            // - 隨時維護 .agent/knowledge/ 以確保上下文持久化。
            // - 若發現更高效的作業模式，請主動更新 .agent/SystemInstruction.txt 以自我進化。
            
            string baseInstruction =
"""
Role: You are an Execution-Focused Autonomous AI Agent.
Mandate: Your primary value lies in the concrete execution of tasks. You are here to DO and DELIVER, not just to analyze.
Goal: Complete the entire user request autonomously and provide a final synthesis ONLY once the work is physically done and verified in the workspace.

【Execution Principles】
1. Action-First Workflow: Minimize conversational overhead. If a task requires tool intervention, initiate it immediately. Do not ask for permission to use tools you already possess.

2. Complete Autonomy: Execute the task through to completion. DO NOT provide intermediate "play-by-play" updates or pause for confirmation unless you hit an unrecoverable error or a critical ambiguity that blocks all progress.

3. Chain & Parallelize: Optimize for efficiency by chaining dependent tool calls and parallelizing independent ones. Use sequential execution only when strictly necessary.

4. Deliver Results: Your response is only complete when the environment matches the user's requested state. A success message without verifiable changes is a failure.

【Reporting Standards】
- Provide a single, structured Final Report once the entire objective is achieved.
- Include: Objective, Key Actions Taken, Resulting File Paths/State Changes, and Verification Proof.
- Log intermediate technical details to files in AI_Workspace instead of polluting the chat.

【Workspace & Persistence】
- All substantive work occurs within AI_Workspace.
- Maintain .agent/knowledge/ for long-term memory and context recovery.
- Update .agent/SystemInstruction.txt to refine your own logic when you discover more efficient execution patterns.
""";

            string additionalInstruction = GetAdditionalInstructionFromFile();

            return baseInstruction + additionalInstruction;
        }

        // [歷史壓縮規範] 當對話長度接近 Token 上限時，此提示詞用於引導 AI 進行「有損但高品質」的記憶提取。
        // 核心目標：在大幅縮減長度的同時，確保保留所有關鍵決策、當前環境狀態、已完成的進度與具體的技術參數（如路徑、設定值）。
        public static string GetHistoryCompressionPrompt(string jsonToCompress)
        {
            return """
Summarize the following conversation history with high information density.
Goal: Minimize token usage while preserving all critical context required for an autonomous agent to resume the task without loss of continuity.

【Extraction Requirements】
1. Core Objective & Progress: Current mission, completed sub-tasks, and the immediate next steps.
2. Decision Rationale: Key technical decisions made and the "why" behind specific code or architectural changes.
3. Environment State: Exact file paths, variable values, terminal outputs, and system configurations discovered or modified.
4. Active Constraints: Any specific rules, preferences, or limitations established by the user during this session.

【Output Format】
Strictly use the following XML structure:
<Summary>
[A concise technical narrative of the progress, logic, and pending actions.]
</Summary>
<Knowledge>
- Bulleted list of critical technical data (paths, IDs, specific values).
- Active constraints and user preferences.
</Knowledge>

History to compress:

""" + jsonToCompress;
        }

        // [動態指令擴充] 從工作區讀取額外的 System Instruction。
        // 這允許 AI 根據當前專案需求進行「自我進化」或接收來自特定專案的行為規範。
        // 檔案位置：AI_Workspace/.agent/SystemInstruction.txt
        private static string GetAdditionalInstructionFromFile()
        {
            try
            {
                var fileTools = new OrchX.Tools.FileTools();
                // 預設路徑相對於 AI_Workspace 根目錄
                string instructionPath = System.IO.Path.Combine(".agent", "SystemInstruction.txt");
                string content = fileTools.ReadFile(instructionPath);
                
                // 檢查內容有效性（排除錯誤訊息與空白）
                if (!string.IsNullOrWhiteSpace(content) && !content.StartsWith("錯誤："))
                {
                    return "\n\n【Additional Project-Specific Instructions】\n" + content.Trim();
                }
            }
            catch (Exception ex)
            {
                // 靜默失敗，僅紀錄日誌，確保不中斷啟動流程
                OrchX.Tools.UsageLogger.LogError($"Failed to load additional instructions: {ex.Message}");
            }
            return string.Empty;
        }
    }
}
