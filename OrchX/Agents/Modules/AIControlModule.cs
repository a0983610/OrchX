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
    /// 提供 AI 自我控制與調整的模組 (AI Self-Control and Refinement Module)
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
                // 【工具：切換思考模式】
                // 允許 AI 在 'smart' (高推理) 與 'fast' (高效能) 之間切換。
                string description = "Switch the AI thinking mode. Pass 'smart' for complex reasoning and coding tasks, or 'fast' for simple Q&A and basic formatting. The system will return the updated status after switching. Use this tool if the upcoming task requires a different level of intelligence or speed.";

                yield return client.CreateFunctionDeclaration(
                    "switch_model_mode",
                    description,
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            mode = new { type = "string", description = "Target mode: 'smart' (Reasoning) or 'fast' (Efficiency)", @enum = new[] { "smart", "fast" } }
                        },
                        required = new[] { "mode" }
                    }
                );
            }

            // 【工具：自我調整行為】
            // AI 發現更好的策略時，提議更新『附加系統指令』(SystemInstruction.txt)。
            yield return client.CreateFunctionDeclaration(
                "refine_my_behavior",
                "Update personal system behavior instructions. Call this when you identify a better execution strategy or want to establish error-prevention checklists for specific tasks (like debugging or file handling). This proposes updates to the 'Additional System Instructions'.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        reason = new { type = "string", description = "Reason for the adjustment. Describe the observed problem or potential improvement." },
                        proposed_change = new { type = "string", description = "Specific instruction content to be added or modified." },
                        action = new { type = "string", description = "Whether to append to the existing instructions or replace them.", @enum = new[] { "append", "replace" } }
                    },
                    required = new[] { "reason", "proposed_change", "action" }
                }
            );

            // 【技能管理：讀取技能清單】
            // 列出存放在 .agent/skills/ 下的所有可用 AI 技能工作流。
            yield return client.CreateFunctionDeclaration(
                "read_skills",
                "Skill Management: List all available skill libraries (workflows) installed in the system (located in .agent/skills/). Returns their structured names and functional descriptions.",
                new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            );

            // 【技能管理：新增/覆寫技能】
            // 將複雜流程封裝為標準 SOP，支援變數替換，存入 .agent/skills/。
            yield return client.CreateFunctionDeclaration(
                "write_skill",
                "Skill Management: Create or overwrite an AI skill workflow. Automatically creates folders and YAML frontmatter to encapsulate complex processes into standard SOPs. Supports {{variable_name}} for simple string replacement.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        skillName = new { type = "string", description = "Short folder name for the skill (alphanumeric and dashes only, e.g., 'build-tool')" },
                        name = new { type = "string", description = "Display name of the skill (in YAML frontmatter)" },
                        description = new { type = "string", description = "One-sentence summary of when to use this skill (in YAML frontmatter)" },
                        content = new { type = "string", description = "The detailed Markdown execution steps and instructions. Use {{variable}} for placeholders." },
                        variables = new { type = "object", description = "Optional. Key-value pairs to replace {{key}} in the content during writing." }
                    },
                    required = new[] { "skillName", "name", "description", "content" }
                }
            );

            // 【知識庫：寫入筆記】
            // 封存重要知識至 .agent/knowledge/，並自動更新 00_INDEX.md 索引。
            yield return client.CreateFunctionDeclaration(
                "write_note",
                "Knowledge Base: Write a permanent note. Files are automatically stored in .agent/knowledge/ and the master index (00_INDEX.md) is updated automatically.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Relative filename or path for the note (e.g., 'React_Best_Practices.md' or 'subfolder/note.md')" },
                        description = new { type = "string", description = "Short summary of the content to be recorded in 00_INDEX.md" },
                        content = new { type = "string", description = "Full text content of the knowledge note." }
                    },
                    required = new[] { "title", "description", "content" }
                }
            );

            // 【知識庫：檢索索引】
            // 查閱長期記憶庫的總目錄 (00_INDEX.md)，確認是否有相關模組或經驗。
            yield return client.CreateFunctionDeclaration(
                "search_knowledge_index",
                "Knowledge Base: Search the master index (00_INDEX.md) of the long-term memory. Recommended at the start of new tasks to identify existing modules or lessons learned.",
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
