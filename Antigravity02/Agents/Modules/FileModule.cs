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
                "ask_for_image",
                "讀取並解析特定路徑的圖片。提供圖片檔案路徑後，即可獲取該圖片供視覺分析使用。注意：此工具無法與其他工具同時呼叫，請單獨使用。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "圖片檔案相對於 AI_Workspace 的路徑 (例如 test.png 或 images/test.png)" }
                    },
                    required = new[] { "filePath" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "list_files",
                "以樹狀結構列出 AI_Workspace 底下指定資料夾路徑下（最多 3 層）的所有檔案與子資料夾。預設可不填代表根目錄。",
                new { type = "object", properties = new { path = new { type = "string", description = "相對於 AI_Workspace 的資料夾路徑 (例如 / 或 notes)" } } }
            );

            yield return client.CreateFunctionDeclaration(
                "read_file",
                "讀取 AI_Workspace 下特定檔案的內容。支援 .txt, .md, .csv, .json, .docx, .cs 等文字格式。" + (_hasFastModel ? "若檔案過大，可指定 summaryQuery 來擷取重點。" : ""),
                _hasFastModel
                    ? (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" },
                            summaryQuery = new { type = "string", description = "僅讀取符合此查詢的重點 (選填，使用快速模型處理)" }
                        },
                        required = new[] { "filePath" }
                    }
                    : (object)new
                    {
                        type = "object",
                        properties = new { filePath = new { type = "string", description = "相對於 AI_Workspace 的檔案路徑 (例如 notes.txt)" } },
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
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
        {
            switch (funcName)
            {
                case "ask_for_image":
                    string errAskImg = CheckRequiredArgs(funcName, args);
                    if (errAskImg != null) return errAskImg;

                    string imgPath = args["filePath"].ToString();
                    string aiWorkspacePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI_Workspace"));
                    
                    // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                    string cleanedPath = imgPath;
                    string prefixSlash = "AI_Workspace/";
                    string prefixBackslash = "AI_Workspace\\";
                    if (cleanedPath.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                        cleanedPath = cleanedPath.Substring(prefixSlash.Length);
                    else if (cleanedPath.StartsWith(prefixBackslash, StringComparison.OrdinalIgnoreCase))
                        cleanedPath = cleanedPath.Substring(prefixBackslash.Length);

                    if (!File.Exists(cleanedPath))
                    {
                        // 嘗試以 AI_Workspace 相對路徑解析
                        string workspaceImgPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, cleanedPath.TrimStart('/', '\\')));
                        if (File.Exists(workspaceImgPath))
                        {
                            cleanedPath = workspaceImgPath;
                        }
                        else
                        {
                            return $"[Error] 找不到檔案: {imgPath} (已嘗試絕對路徑與 AI_Workspace 相對路徑)";
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
                                return "[Error] ask_for_image 無法與其他工具同時呼叫，請單獨使用此工具來讀取圖片。";
                            }
                        }
                        
                        return "[SKIP_FUNCTION_RESPONSE]";
                    }
                    catch (Exception ex)
                    {
                        return $"[Error] 讀取圖片失敗: {ex.Message}";
                    }

                case "list_files":
                    string subPath = args.ContainsKey("path") ? args["path"].ToString() : "";
                    return _fileTools.ListFiles(subPath);
                case "read_file":
                    string errRead = CheckRequiredArgs(funcName, args);
                    if (errRead != null) return errRead;

                    string fileContent = _fileTools.ReadFile(args["filePath"].ToString());
                    string fileQuery = args.ContainsKey("summaryQuery") ? args["summaryQuery"].ToString() : null;

                    if (_hasFastModel && !string.IsNullOrEmpty(fileQuery))
                    {
                        string summary = await SummarizeContentAsync(fileContent, fileQuery);
                        // 如果快速模型失敗，回傳完整內容，但加上標註
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
                default:
                    return null;
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
