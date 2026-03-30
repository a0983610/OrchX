using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
{
    /// <summary>
    /// 提供 AI 自我控制與調整的模組
    /// </summary>
    public class AIControlModule : BaseAgentModule
    {
        private readonly BaseAgent _agent;
        private readonly bool _hasDifferentFastModel;
        private readonly FileTools _fileTools;

        public AIControlModule(BaseAgent agent)
        {
            _agent = agent;
            _hasDifferentFastModel = agent != null && agent.SmartClient.ModelName != agent.FastClient.ModelName;
            _fileTools = new FileTools();
        }

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            if (_hasDifferentFastModel && _agent != null)
            {
                string description = "切換 AI 思考模式。可傳入 'smart' (聰明模式：適合複雜推理與程式碼撰寫) 或 'fast' (快速模式：適合簡單問答與初步整理)。完成切換後系統將會回傳最新的狀態。請根據接下來任務的複雜度，決定是否需要呼叫此工具進行切換。";

                yield return client.CreateFunctionDeclaration(
                    "switch_model_mode",
                    description,
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "模式名稱 (smart 或 fast)", @enum = new[] { "smart", "fast" } }
                        },
                        required = new[] { "mode" }
                    }
                );
            }

            yield return client.CreateFunctionDeclaration(
                "refine_my_behavior",
                "當 AI 發現特定任務（如除錯、檔案處理）有更好的執行策略，或需要建立防止錯誤的檢查清單時呼叫此工具。它會向使用者提議更新『附加系統指令』以優化未來的表現。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        reason = new { type = "string", description = "為什麼需要調整指令？描述觀察到的問題或改進點。" },
                        proposed_change = new { type = "string", description = "具體要新增或修改的指令內容。" },
                        action = new { type = "string", description = "是要附加在現有指令後，還是完全替換。", @enum = new[] { "append", "replace" } }
                    },
                    required = new[] { "reason", "proposed_change", "action" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "read_skills",
                "【技能管理：讀取技能清單】列出當前系統安裝的所有可用技能庫(位於 .agent/skills/)，回傳其結構化的名稱與功能描述。",
                new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_skill",
                "【技能管理：新增/覆寫技能】建立 AI 專用技能工作流。自動建立資料夾與 YAML frontmatter，將複雜流程封裝為標準 SOP。支援 {{變數名稱}} 格式的簡單字串替換。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        skillName = new { type = "string", description = "技能所在的資料夾簡稱，限英數與破折號 (例如 build-tool)" },
                        name = new { type = "string", description = "技能的顯示名稱 (在 YAML frontmatter 中)" },
                        description = new { type = "string", description = "一句話簡述該技能的觸發時機或作用 (在 YAML frontmatter 中)" },
                        content = new { type = "string", description = "此技能具體的 Markdown 循序執行步驟與指令細節內容，可使用 {{變數名稱}} 建立佔位符。" },
                        variables = new { type = "object", description = "選填。提供鍵值對作為變數，系統會在寫入前將 content 中的 {{key}} 替換為對應的 value。" }
                    },
                    required = new[] { "skillName", "name", "description", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_note",
                "【知識庫：寫入筆記】封存重要知識。檔案會自動存入 .agent/knowledge/ 並自動維護 00_INDEX.md 索引，無須額外手動管理。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "筆記相對檔名或路徑 (例如 React_Best_Practices.md 或 subfolder/note.md)" },
                        description = new { type = "string", description = "簡短的內容實意摘要，這會被系統自動寫入 00_INDEX.md 中" },
                        content = new { type = "string", description = "知識筆記的完整文字內容" }
                    },
                    required = new[] { "title", "description", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "search_knowledge_index",
                "【知識庫：檢索索引】快速查閱長期記憶庫的「總目錄」(00_INDEX.md)。建議在開始新任務前呼叫，確認是否有現成共用模組或曾踩過的坑。",
                new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            switch (funcName)
            {
                case "switch_model_mode":
                    if (_agent != null) return await HandleSwitchModelMode(funcName, args);
                    break;
                case "refine_my_behavior":
                    return await HandleRefineMyBehavior(funcName, args, ui);
                case "read_skills":
                    return HandleReadSkills();
                case "write_skill":
                    return HandleWriteSkill(funcName, args);
                case "write_note":
                    return HandleWriteNote(funcName, args);
                case "search_knowledge_index":
                    return HandleSearchKnowledgeIndex();
            }
            return null;
        }

        private Task<string> HandleSwitchModelMode(string funcName, Dictionary<string, object> args)
        {
            string error = CheckRequiredArgs(funcName, args);
            if (error != null) return Task.FromResult(error);

            string mode = args["mode"].ToString();
            _agent.SetModelMode(mode);
            return Task.FromResult($"成功：已切換至 {mode} 模式。接下來的對話將使用此模式的模型進行回應。");
        }

        private async Task<string> HandleRefineMyBehavior(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            string error = CheckRequiredArgs(funcName, args);
            if (error != null) return error;

            string reason = args.ContainsKey("reason") ? args["reason"]?.ToString() : "";
            string proposedChange = args.ContainsKey("proposed_change") ? args["proposed_change"]?.ToString() : "";
            string action = args.ContainsKey("action") ? args["action"]?.ToString() : "";

            string promptMessage = $"\n=== 系統指令調整提案 ===\n原因：{reason}\n操作：{action}\n內容：\n{proposedChange}\n==========================\n\n是否同意更新附加系統指令？ (Y/N)：";
            
            bool isApproved = await ui.PromptContinueAsync(promptMessage);
            if (!isApproved)
            {
                return "使用者已拒絕更新系統指令。";
            }

            try
            {
                string targetPath = Path.Combine(".agent", "SystemInstruction.txt");
                bool append = string.Equals(action, "append", StringComparison.OrdinalIgnoreCase);
                
                string result = _fileTools.WriteFile(targetPath, proposedChange, append);
                if (result.StartsWith("錯誤"))
                {
                    return result;
                }

                return "指令已更新，將於下一次任務啟動或新對話時完整生效（因 AgentConfig.cs 會在初始化時讀取此檔）。";
            }
            catch (Exception ex)
            {
                return $"更新系統指令失敗：{ex.Message}";
            }
        }

        private string HandleReadSkills()
        {
            return _fileTools.ReadSkills(_fileTools.SkillsPath);
        }

        private string HandleWriteSkill(string funcName, Dictionary<string, object> args)
        {
            string errWriteSk = CheckRequiredArgs(funcName, args);
            if (errWriteSk != null) return errWriteSk;

            string sName = args["skillName"].ToString();
            string name = args["name"].ToString();
            string desc = args["description"].ToString();
            string content = args["content"].ToString();

            Dictionary<string, string> variables = null;
            if (args.TryGetValue("variables", out object varsObj) && varsObj != null)
            {
                variables = new Dictionary<string, string>();
                if (varsObj is IDictionary<string, object> dictObj)
                {
                    foreach (var kvp in dictObj)
                    {
                        variables[kvp.Key] = kvp.Value?.ToString() ?? "";
                    }
                }
                else if (varsObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in je.EnumerateObject())
                    {
                        variables[prop.Name] = prop.Value.ToString();
                    }
                }
            }

            return _fileTools.WriteSkill(sName, name, desc, content, variables);
        }

        private string HandleWriteNote(string funcName, Dictionary<string, object> args)
        {
            string errWriteNote = CheckRequiredArgs(funcName, args);
            if (errWriteNote != null) return errWriteNote;

            string noteTitle = args["title"].ToString();
            if (!noteTitle.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !noteTitle.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                noteTitle += ".md";
            }
            string noteDesc = args["description"].ToString();
            string noteContent = args["content"].ToString();
            
            string knowledgePath = Path.Combine(".agent", "knowledge", noteTitle).Replace("\\", "/");
            string writeResult = _fileTools.WriteFile(knowledgePath, noteContent, false);
            if (writeResult.StartsWith("錯誤"))
            {
                return writeResult;
            }
            
            string updateIndexResult = UpdateKnowledgeIndex(noteTitle, noteDesc);
            return $"{writeResult}\n{updateIndexResult}";
        }

        private string HandleSearchKnowledgeIndex()
        {
            string indexPath = Path.Combine(".agent", "knowledge", "00_INDEX.md").Replace("\\", "/");
            string indexContent = _fileTools.ReadFile(indexPath);
            if (indexContent.StartsWith("錯誤：找不到檔案"))
            {
                return "目前尚無知識索引 (00_INDEX.md)。";
            }
            return indexContent;
        }

        private string UpdateKnowledgeIndex(string title, string description)
        {
            try
            {
                string aiWorkspacePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "AI_Workspace"));
                string knowledgeDir = Path.Combine(aiWorkspacePath, ".agent", "knowledge");
                if (!Directory.Exists(knowledgeDir))
                {
                    Directory.CreateDirectory(knowledgeDir);
                }

                string indexPath = Path.Combine(knowledgeDir, "00_INDEX.md");
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                
                if (!File.Exists(indexPath))
                {
                    string initialContent = $"| 檔名 | 摘要/關鍵字 | 最後更新日期 |\n|---|---|---|\n| {title} | {description} | {today} |\n";
                    File.WriteAllText(indexPath, initialContent, System.Text.Encoding.UTF8);
                    return "已成功建立並更新 00_INDEX.md。";
                }

                var lines = new List<string>(File.ReadAllLines(indexPath, System.Text.Encoding.UTF8));
                bool updated = false;
                
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith($"| {title} |", StringComparison.OrdinalIgnoreCase) || 
                        lines[i].Trim().StartsWith($"|{title}|", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"| {title} | {description} | {today} |";
                        updated = true;
                        break;
                    }
                }

                if (!updated)
                {
                    lines.Add($"| {title} | {description} | {today} |");
                }

                File.WriteAllLines(indexPath, lines, System.Text.Encoding.UTF8);
                return updated ? "已成功更新 00_INDEX.md 內現有紀錄。" : "已成功新增紀錄至 00_INDEX.md。";
            }
            catch (Exception ex)
            {
                return $"更新索引時發生例外: {ex.Message}";
            }
        }
    }
}
