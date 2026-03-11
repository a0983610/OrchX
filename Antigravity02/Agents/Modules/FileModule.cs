using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    public class FileModule : BaseAgentModule
    {
        private readonly FileTools _fileTools;
        private readonly IAIClient _fastClient;
        private readonly bool _hasFastModel;
        private readonly BaseAgent _agent;

        public FileModule(BaseAgent agent)
        {
            _agent = agent;
            _fileTools = new FileTools();
            if (agent != null)
            {
                bool hasDifferentFastModel = agent.SmartClient.ModelName != agent.FastClient.ModelName;
                _fastClient = hasDifferentFastModel ? agent.FastClient : null;
            }
            // 簡單判斷：如果有傳入快速模型 client，就視為有能力處理 summary
            _hasFastModel = _fastClient != null;
        }

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            yield return client.CreateFunctionDeclaration(
                "list_files",
                "【檔案系統：列出目錄與檔案】以樹狀結構列出指定路徑的內容。背後實作邏輯：路徑會被安全限制在 AI_Workspace 沙盒內；為確保效能，最多只會往下掃描 3 層子目錄。未提供 path 時預設為根目錄。",
                new { type = "object", properties = new { path = new { type = "string", description = "相對於 AI_Workspace 的資料夾路徑 (例如 / 或 notes)，留空代表根目錄" } } }
            );

            yield return client.CreateFunctionDeclaration(
                "read_file",
                "【檔案系統：讀取檔案或圖片】讀取文字檔內容或解析圖片。背後實作邏輯：1. 若讀取圖片 (isImage=true)，系統會在後端將檔案轉 Base64 並直接注入 AI 的視覺輸入，隨即中斷該次回應來迫使 AI 在下一回合「看」到圖片，這導致圖片讀取『不能』與其他工具同時呼叫。2. 讀取文字時回傳完整內容。" + (_hasFastModel ? "3. 若帶有 summaryQuery，系統將啟動快速模型 (Fast Model) 先行總結龐大內容，避免 Token 爆量。" : ""),
                _hasFastModel
                    ? (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑" },
                            summaryQuery = new { type = "string", description = "僅讀取符合此查詢的重點 (將觸發後端快速模型處理)" },
                            isImage = new { type = "boolean", description = "是否作為圖片視覺解析 (為 true 時必須單獨呼叫本工具，不可包含其他函數)" }
                        },
                        required = new[] { "filePath" }
                    }
                    : (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑" },
                            isImage = new { type = "boolean", description = "是否作為圖片視覺解析 (為 true 時必須單獨呼叫本工具，不可包含其他函數)" }
                        },
                        required = new[] { "filePath" }
                    }
            );

            yield return client.CreateFunctionDeclaration(
                "write_file",
                "【檔案系統：寫入檔案】建立新檔案或覆寫、附加內容至既有檔案。背後實作邏輯：預設 append=true 會將內容接在檔尾，若要完全覆蓋必須明確設為 append=false。若指定的相對路徑中包含尚不存在的子資料夾，系統會自動遞迴建立這些資料夾。所有的操作都被安全鎖定在 AI_Workspace 內。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" },
                        content = new { type = "string", description = "要寫入的字串內容" },
                        append = new { type = "boolean", description = "true=附加到檔尾(預設); false=清空並覆蓋檔案" }
                    },
                    required = new[] { "filePath", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "delete_file",
                "【檔案系統：刪除檔案】從磁碟中永久刪除單一檔案。背後實作邏輯：僅允許刪除 AI_Workspace 內的單獨檔案物件，無法刪除資料夾。刪除後無法復原，呼叫前必須確認目標明確。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" }
                    },
                    required = new[] { "filePath" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "move_file",
                "【檔案系統：移動/重新命名檔案】變更單一檔案的路徑或名稱。背後實作邏輯：本質是 File.Move，會在 AI_Workspace 內將檔案從來源路徑搬移至目標路徑，若目標資料夾不存在會自動建立，若目標檔案已存在會將其覆寫。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        sourcePath = new { type = "string", description = "來源檔案路徑，相對於 AI_Workspace (例如 old/notes.txt)" },
                        destinationPath = new { type = "string", description = "目標檔案路徑，相對於 AI_Workspace (例如 new/notes.txt)" }
                    },
                    required = new[] { "sourcePath", "destinationPath" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "update_file_line",
                "【檔案系統：單行更新】精準修改文字檔裡面的特定一行。背後實作邏輯：這是一個輕量級的操作，後端會將檔案全部讀入記憶體，找到對應行號(1-based)替換內容，然後覆寫回檔案。適合用於微調單一設定或小區塊，避免重新傳輸完整檔案的 write_file 所耗費的資源。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" },
                        lineNumber = new { type = "integer", description = "欲修改的絕對行號 (從 1 開始)" },
                        newContent = new { type = "string", description = "用來替換此行的全新內容字串" }
                    },
                    required = new[] { "filePath", "lineNumber", "newContent" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "search_content",
                "【檔案系統：內容全局搜尋】在所有文字檔案中全文檢索特定字串。背後實作邏輯：系統會掃描並列舉 path 限制下的所有檔案，為求效能過濾掉大於 10MB 的檔案以及常見的二進位檔案格式 (例如 .exe, .png, .zip)，進以快速找出匹配 query 的文本行與詳細行號；可帶入 contextLines 同時查看上下行文脈。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "要搜尋的精確關鍵字或字串" },
                        path = new { type = "string", description = "搜尋範圍的子目錄，相對於 AI_Workspace (預設為空字串，代表全局搜索)" },
                        filePattern = new { type = "string", description = "限制檔案類型，例如 *.cs 或 *.log" },
                        contextLines = new { type = "integer", description = "除了找到的那行外，額外回傳它的上下行數量 (預設為 0)" }
                    },
                    required = new[] { "query" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "read_skills",
                "【技能管理：讀取技能清單】列出當前系統安裝的所有可用技能庫。背後實作邏輯：無須外界給定路徑，後台會寫死掃描 AI_Workspace/.agent/skills/ 目錄下各個技能資料夾中的 SKILL.md，重點回傳其結構化的名稱與功能描述。供 AI 檢視有哪些技能工具。",
                new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_skill",
                "【技能管理：新增/覆寫技能】建立 AI 專用的技能工作流擴充。背後實作邏輯：它會自動處理新建技能目錄，在 .agent/skills/{skillName}/ 之下建立或覆寫 SKILL.md，並按照系統要求的 YAML frontmatter 標準將 name 與 description 封裝寫入。適合將複雜的命令流程封裝為未來的標準 SOP。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        skillName = new { type = "string", description = "技能所在的資料夾簡稱，限英數與破折號 (例如 build-tool)" },
                        name = new { type = "string", description = "技能的顯示名稱 (在 YAML frontmatter 中)" },
                        description = new { type = "string", description = "一句話簡述該技能的觸發時機或作用 (在 YAML frontmatter 中)" },
                        content = new { type = "string", description = "此技能具體的 Markdown 循序執行步驟與指令細節內容" }
                    },
                    required = new[] { "skillName", "name", "description", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_note",
                "【知識庫：寫入筆記】封存值得長期記憶的重要知識。背後實作邏輯：為了免除 AI 額外的建檔整理負擔，呼叫此工具後系統會強制把筆記存入 .agent/knowledge/ 目錄中，如果包含子路徑會自動遞迴建立資料夾；最方便的是，後端會『自動解析並增改』00_INDEX.md，這意味著只需要呼叫 write_note 就能全自動維護檢索索引庫，節省步驟。",
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
                "【知識庫：檢索索引】快速查閱長期記憶庫的「總目錄」。背後實作邏輯：無須任何參數，後端直接讀取 .agent/knowledge/00_INDEX.md。這是一份由 write_note 自動生成的 Markdown 表格，協助 AI 在開始全新任務前能最快得知此前是否有留下共用的模組或踩坑經驗。",
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
                case "list_files":
                    return HandleListFiles(args);
                case "read_file":
                    return await HandleReadFileAsync(funcName, args);
                case "write_file":
                    return HandleWriteFile(funcName, args);
                case "delete_file":
                    return HandleDeleteFile(funcName, args);
                case "move_file":
                    return HandleMoveFile(funcName, args);
                case "update_file_line":
                    return HandleUpdateFileLine(funcName, args);
                case "read_skills":
                    return HandleReadSkills();
                case "write_skill":
                    return HandleWriteSkill(funcName, args);
                case "write_note":
                    return HandleWriteNote(funcName, args);
                case "search_knowledge_index":
                    return HandleSearchKnowledgeIndex();
                case "search_content":
                    return HandleSearchContent(funcName, args);
                default:
                    return null;
            }
        }

        private string HandleListFiles(Dictionary<string, object> args)
        {
            string subPath = args.ContainsKey("path") ? args["path"].ToString() : "";
            return _fileTools.ListFiles(subPath);
        }

        private async Task<string> HandleReadFileAsync(string funcName, Dictionary<string, object> args)
        {
            string errRead = CheckRequiredArgs(funcName, args);
            if (errRead != null) return errRead;

            bool isImage = args.ContainsKey("isImage") && Convert.ToBoolean(args["isImage"]);
            if (isImage)
            {
                return HandleReadImage(args);
            }

            string fileContent = _fileTools.ReadFile(args["filePath"].ToString());
            string fileQuery = args.ContainsKey("summaryQuery") ? args["summaryQuery"].ToString() : null;

            if (_hasFastModel && !string.IsNullOrEmpty(fileQuery))
            {
                string summary = await SummarizeContentAsync(fileContent, fileQuery);
                if (summary.StartsWith("[Fast AI Error]"))
                {
                    return summary + "\n\n[Warning: Summary failed, falling back to full content] 以下是原始檔案內容：\n" + fileContent;
                }
                return summary;
            }
            return fileContent;
        }

        private string HandleReadImage(Dictionary<string, object> args)
        {
            string imgPath = args["filePath"].ToString();
            string aiWorkspacePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "AI_Workspace"));
            if (!aiWorkspacePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                aiWorkspacePath += Path.DirectorySeparatorChar;

            string cleanedPath = imgPath;
            string prefixSlash = "AI_Workspace/";
            string prefixBackslash = "AI_Workspace\\";
            if (cleanedPath.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                cleanedPath = cleanedPath.Substring(prefixSlash.Length);
            else if (cleanedPath.StartsWith(prefixBackslash, StringComparison.OrdinalIgnoreCase))
                cleanedPath = cleanedPath.Substring(prefixBackslash.Length);

            // 正規化為工作區內的絕對路徑
            string resolvedPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, cleanedPath.TrimStart('/', '\\')));

            // 沙盒驗證：確保最終路徑在 AI_Workspace 內
            if (!resolvedPath.StartsWith(aiWorkspacePath, StringComparison.OrdinalIgnoreCase))
            {
                return $"[Error] 超出授權存取範圍，圖片僅可讀取 AI_Workspace 內的檔案。";
            }

            if (!File.Exists(resolvedPath))
            {
                return $"[Error] 找不到圖片檔案: {imgPath}";
            }
            imgPath = resolvedPath;

            try
            {
                string ext = Path.GetExtension(imgPath).ToLower();
                string mime = "image/jpeg";
                if (ext == ".png") mime = "image/png";
                else if (ext == ".webp") mime = "image/webp";
                else if (ext == ".heic") mime = "image/heic";
                else if (ext == ".heif") mime = "image/heif";
                
                byte[] bytes = File.ReadAllBytes(imgPath);
                string base64 = Convert.ToBase64String(bytes);
                
                if (_agent != null)
                {
                    if (!_agent.InjectImageHistory(imgPath, mime, base64))
                    {
                        return "[Error] 讀取圖片 (isImage=true) 無法與其他工具同時呼叫，請單獨使用此工具來讀取圖片。";
                    }
                }
                
                return "[SKIP_FUNCTION_RESPONSE]";
            }
            catch (Exception ex)
            {
                return $"[Error] 讀取圖片失敗: {ex.Message}";
            }
        }

        private string HandleWriteFile(string funcName, Dictionary<string, object> args)
        {
            string errWrite = CheckRequiredArgs(funcName, args);
            if (errWrite != null) return errWrite;

            bool append = args.ContainsKey("append") ? Convert.ToBoolean(args["append"]) : true;
            return _fileTools.WriteFile(
                args["filePath"].ToString(),
                args["content"].ToString(),
                append);
        }

        private string HandleDeleteFile(string funcName, Dictionary<string, object> args)
        {
            string errDel = CheckRequiredArgs(funcName, args);
            if (errDel != null) return errDel;

            return _fileTools.DeleteFile(args["filePath"].ToString());
        }

        private string HandleMoveFile(string funcName, Dictionary<string, object> args)
        {
            string errMove = CheckRequiredArgs(funcName, args);
            if (errMove != null) return errMove;

            return _fileTools.MoveFile(args["sourcePath"].ToString(), args["destinationPath"].ToString());
        }

        private string HandleUpdateFileLine(string funcName, Dictionary<string, object> args)
        {
            string errUpd = CheckRequiredArgs(funcName, args);
            if (errUpd != null) return errUpd;

            int lineNum = Convert.ToInt32(args["lineNumber"]);
            string newContent = args["newContent"].ToString();
            return _fileTools.UpdateFileLine(args["filePath"].ToString(), lineNum, newContent);
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
            return _fileTools.WriteSkill(sName, name, desc, content);
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

        private string HandleSearchContent(string funcName, Dictionary<string, object> args)
        {
            string errSearchCon = CheckRequiredArgs(funcName, args);
            if (errSearchCon != null) return errSearchCon;

            string sq = args["query"].ToString();
            string spath = args.ContainsKey("path") ? args["path"].ToString() : "";
            string sfPattern = args.ContainsKey("filePattern") ? args["filePattern"].ToString() : "";
            int ctxLines = args.ContainsKey("contextLines") ? Convert.ToInt32(args["contextLines"]) : 0;
            return _fileTools.SearchContent(sq, spath, sfPattern, ctxLines);
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

        private async Task<string> SummarizeContentAsync(string fileContent, string query)
        {
            if (string.IsNullOrEmpty(fileContent) || fileContent.StartsWith("錯誤") || fileContent.StartsWith("找不到"))
            {
                return fileContent;
            }

            try
            {
                // 使用快速模型來節錄重點
                string prompt = $"以下是檔案內容：\n\n{fileContent}\n\n請根據使用者的需求：「{query}」，擷取相關重點或進行總結。不要回答無關的內容。";
                
                // 使用快速模型來呼叫 GenerateContentAsync (不帶 tools)
                var request = new GenerateContentRequest
                {
                    Contents = _fastClient.CreateSimpleContents(prompt)
                };
                string responseJson = await _fastClient.GenerateContentAsync(request);

                // 簡單解析回應 (防禦式存取，避免非預期格式拋出例外)
                var data = JsonTools.Deserialize<Dictionary<string, object>>(responseJson);
                if (data != null &&
                    data.TryGetValue("candidates", out var candidatesObj) &&
                    candidatesObj is System.Collections.ArrayList candidates &&
                    candidates.Count > 0 &&
                    candidates[0] is Dictionary<string, object> firstCandidate &&
                    firstCandidate.TryGetValue("content", out var contentObj) &&
                    contentObj is Dictionary<string, object> modelContent &&
                    modelContent.TryGetValue("parts", out var partsObj) &&
                    partsObj is System.Collections.ArrayList parts &&
                    parts.Count > 0 &&
                    parts[0] is Dictionary<string, object> textPart &&
                    textPart.TryGetValue("text", out var textObj) &&
                    textObj != null)
                {
                    return $"[Fast AI Summary]: {textObj}";
                }
                return "[Fast AI Error] 無法解析摘要回應。";
            }
            catch (Exception ex)
            {
                return $"[Fast AI Error] 摘要失敗: {ex.Message}\n原始內容長度: {fileContent.Length}";
            }
        }
    }
}
