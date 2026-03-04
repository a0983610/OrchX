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
                "以樹狀結構列出 AI_Workspace 底下指定資料夾路徑下（最多 3 層）的所有檔案與子資料夾。預設可不填代表根目錄。",
                new { type = "object", properties = new { path = new { type = "string", description = "相對於 AI_Workspace 的資料夾路徑 (例如 / 或 notes)" } } }
            );

            yield return client.CreateFunctionDeclaration(
                "read_file",
                "讀取 AI_Workspace 下特定檔案的內容。支援一般文字格式 (如 .txt, .md, .csv, .json, .docx, .cs)，也可透過 isImage 參數讀取並解析圖片供視覺分析使用 (圖片模式無法與其他工具同時呼叫)。" + (_hasFastModel ? "若文字檔過大，可指定 summaryQuery 來擷取重點。" : ""),
                _hasFastModel
                    ? (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑" },
                            summaryQuery = new { type = "string", description = "僅讀取符合此查詢的重點 (選填，使用快速模型處理)" },
                            isImage = new { type = "boolean", description = "是否將此檔案作為圖片讀取與視覺解析 (若為 true，請單獨呼叫此工具)" }
                        },
                        required = new[] { "filePath" }
                    }
                    : (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑" },
                            isImage = new { type = "boolean", description = "是否將此檔案作為圖片讀取與視覺解析 (若為 true，請單獨呼叫此工具)" }
                        },
                        required = new[] { "filePath" }
                    }
            );

            yield return client.CreateFunctionDeclaration(
                "write_file",
                "將資訊儲存為文字檔至 AI_Workspace。支援各種文字格式 (如 .txt, .md, .json, .cs 等)。預設會將內容附加到檔案末尾，若需覆蓋請設 append=false。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" },
                        content = new { type = "string", description = "內容" },
                        append = new { type = "boolean", description = "true=附加內容到最後 (預設); false=覆蓋所有內容" }
                    },
                    required = new[] { "filePath", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "delete_file",
                "刪除 AI_Workspace 下指定的檔案。請謹慎使用。",
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
                "在 AI_Workspace 內部搬移檔案或重新命名檔案。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        sourcePath = new { type = "string", description = "相對於 AI_Workspace 的來源檔案路徑 (例如 old_folder/notes.txt)" },
                        destinationPath = new { type = "string", description = "相對於 AI_Workspace 的目標檔案路徑 (例如 new_folder/notes.txt)" }
                    },
                    required = new[] { "sourcePath", "destinationPath" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "update_file_line",
                "修改 AI_Workspace 下文字檔中的特定行內容。行號從 1 開始。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" },
                        lineNumber = new { type = "integer", description = "要修改的行號 (1-based)" },
                        newContent = new { type = "string", description = "該行的新內容" }
                    },
                    required = new[] { "filePath", "lineNumber", "newContent" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "read_skills",
                "讀取 AI_Workspace/.agent/skills 路徑下所有子資料夾內的 SKILL.md，擷取 name 與 description，並以 JSON 結構回傳。",
                new
                {
                    type = "object",
                    properties = new { }, // 不再需要外部傳入 subPath
                    required = new string[] { }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_skill",
                "建立一個新的技能，或者覆蓋已存在的同名技能。會自動在 AI_Workspace/.agent/skills 底下建立以 skillName 為名的資料夾，並寫入/覆蓋 SKILL.md 檔案（包含標準格式）。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        skillName = new { type = "string", description = "技能的資料夾名稱 (例如 text-analyser)" },
                        name = new { type = "string", description = "技能的顯示名稱 (YAML frontmatter 中的 name)" },
                        description = new { type = "string", description = "技能的功能描述 (YAML frontmatter 中的 description)" },
                        content = new { type = "string", description = "技能的詳細 Markdown 內容說明" }
                    },
                    required = new[] { "skillName", "name", "description", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "write_note",
                "當 AI 判斷有值得長期保留的知識時呼叫。可自由規劃子目錄 (如 category/note.md) 建立樹狀結構。強制存入 AI_Workspace/.agent/knowledge/，並自動維護 00_INDEX.md 索引。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "筆記的路徑與檔名 (例如 AI_Rules.md 或 frontend/React_Hooks.md)" },
                        description = new { type = "string", description = "筆記的摘要或關鍵字" },
                        content = new { type = "string", description = "詳細筆記內容" }
                    },
                    required = new[] { "title", "description", "content" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "search_knowledge_index",
                "直接讀取並回傳 AI_Workspace/.agent/knowledge/00_INDEX.md 的內容，以檢索現有的知識筆記索引。",
                new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "search_content",
                "在 AI_Workspace 內全局搜尋包含指定關鍵字的檔案。回傳檔案路徑、行號及該行內容摘要。適用於定位配置、尋找錯誤日誌或檢索特定知識。注意：為確保效能，大於 10MB 的檔案及常見的二進位檔案格式 (如 .exe, .dll, .png, .zip 等) 將會被自動忽略。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "要搜尋的關鍵字或字串" },
                        path = new { type = "string", description = "指定搜尋的子目錄，預設為根目錄 (相對於 AI_Workspace)" },
                        filePattern = new { type = "string", description = "限制搜尋的檔案類型（例如 *.log 或 *.cs）" },
                        contextLines = new { type = "integer", description = "額外回傳目標行前後的行數（預設為 0）" }
                    },
                    required = new[] { "query" }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            switch (funcName)
            {
                case "list_files":
                    string subPath = args.ContainsKey("path") ? args["path"].ToString() : "";
                    return _fileTools.ListFiles(subPath);
                case "read_file":
                    string errRead = CheckRequiredArgs(funcName, args);
                    if (errRead != null) return errRead;

                    bool isImage = args.ContainsKey("isImage") && Convert.ToBoolean(args["isImage"]);
                    if (isImage)
                    {
                        string imgPath = args["filePath"].ToString();
                        string aiWorkspacePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI_Workspace"));
                        
                        string cleanedPath = imgPath;
                        string prefixSlash = "AI_Workspace/";
                        string prefixBackslash = "AI_Workspace\\";
                        if (cleanedPath.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                            cleanedPath = cleanedPath.Substring(prefixSlash.Length);
                        else if (cleanedPath.StartsWith(prefixBackslash, StringComparison.OrdinalIgnoreCase))
                            cleanedPath = cleanedPath.Substring(prefixBackslash.Length);

                        if (!File.Exists(cleanedPath))
                        {
                            string workspaceImgPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, cleanedPath.TrimStart('/', '\\')));
                            if (File.Exists(workspaceImgPath))
                            {
                                cleanedPath = workspaceImgPath;
                            }
                            else
                            {
                                return $"[Error] 找不到圖片檔案: {imgPath} (已嘗試絕對路徑與 AI_Workspace 相對路徑)";
                            }
                        }
                        imgPath = cleanedPath;

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
                case "write_file":
                    string errWrite = CheckRequiredArgs(funcName, args);
                    if (errWrite != null) return errWrite;

                    bool append = args.ContainsKey("append") ? Convert.ToBoolean(args["append"]) : true;
                    return _fileTools.WriteFile(
                        args["filePath"].ToString(),
                        args["content"].ToString(),
                        append);
                case "delete_file":
                    string errDel = CheckRequiredArgs(funcName, args);
                    if (errDel != null) return errDel;

                    return _fileTools.DeleteFile(args["filePath"].ToString());
                case "move_file":
                    string errMove = CheckRequiredArgs(funcName, args);
                    if (errMove != null) return errMove;

                    return _fileTools.MoveFile(args["sourcePath"].ToString(), args["destinationPath"].ToString());
                case "update_file_line":
                    string errUpd = CheckRequiredArgs(funcName, args);
                    if (errUpd != null) return errUpd;

                    int lineNum = Convert.ToInt32(args["lineNumber"]);
                    string newContent = args["newContent"].ToString();
                    return _fileTools.UpdateFileLine(args["filePath"].ToString(), lineNum, newContent);
                case "read_skills":
                    // 固定讀取 AI_Workspace/.agent/skills 目錄
                    return _fileTools.ReadSkills(_fileTools.SkillsPath);
                case "write_skill":
                    string errWriteSk = CheckRequiredArgs(funcName, args);
                    if (errWriteSk != null) return errWriteSk;

                    string sName = args["skillName"].ToString();
                    string name = args["name"].ToString();
                    string desc = args["description"].ToString();
                    string content = args["content"].ToString();
                    return _fileTools.WriteSkill(sName, name, desc, content);

                case "write_note":
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

                case "search_knowledge_index":
                    string indexPath = Path.Combine(".agent", "knowledge", "00_INDEX.md").Replace("\\", "/");
                    string indexContent = _fileTools.ReadFile(indexPath);
                    if (indexContent.StartsWith("錯誤：找不到檔案"))
                    {
                        return "目前尚無知識索引 (00_INDEX.md)。";
                    }
                    return indexContent;

                case "search_content":
                    string errSearchCon = CheckRequiredArgs(funcName, args);
                    if (errSearchCon != null) return errSearchCon;

                    string sq = args["query"].ToString();
                    string spath = args.ContainsKey("path") ? args["path"].ToString() : "";
                    string sfPattern = args.ContainsKey("filePattern") ? args["filePattern"].ToString() : "";
                    int ctxLines = args.ContainsKey("contextLines") ? Convert.ToInt32(args["contextLines"]) : 0;
                    return _fileTools.SearchContent(sq, spath, sfPattern, ctxLines);

                default:
                    return null;
            }
        }

        private string UpdateKnowledgeIndex(string title, string description)
        {
            try
            {
                string aiWorkspacePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI_Workspace"));
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

                // 簡單解析回應 (假設回傳結構標準)
                var data = JsonTools.Deserialize<Dictionary<string, object>>(responseJson);
                var candidates = data["candidates"] as System.Collections.ArrayList;
                if (candidates != null && candidates.Count > 0)
                {
                    var modelContent = (candidates[0] as Dictionary<string, object>)["content"] as Dictionary<string, object>;
                    var parts = modelContent["parts"] as System.Collections.ArrayList;
                    if (parts != null && parts.Count > 0)
                    {
                        var textPart = parts[0] as Dictionary<string, object>;
                        if (textPart.ContainsKey("text"))
                        {
                            return $"[Fast AI Summary]: {textPart["text"]}";
                        }
                    }
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
