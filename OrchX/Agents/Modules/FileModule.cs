using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
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
            // 【檔案系統：列出目錄與檔案】
            // 以樹狀結構列舉 AI_Workspace 內的路徑內容。
            yield return client.CreateFunctionDeclaration(
                "list_files",
                "File System: List contents of a directory. Returns entries in a tree structure. Default is the root directory (restricted to AI_Workspace, scans up to 3 levels deep).",
                new { type = "object", properties = new { 
                    path = new { type = "string", description = "Folder path relative to AI_Workspace (e.g., '/' or 'notes'). Empty string means root." },
                    sortByTime = new { type = "boolean", description = "Whether to sort by modified time descending (Default: false, sorts by name)" },
                    filePattern = new { type = "string", description = "Filter for file types, e.g., '*.cs' or 'text*.txt'" }
                } }
            );

            // 【檔案系統：讀取檔案或圖片】
            // 支援讀取文字內容或進行圖片視覺解析 (isImage=true)。
            // 對於大型檔案可帶入 summaryQuery 使用快速模型進行摘要。
            yield return client.CreateFunctionDeclaration(
                "read_file",
                "File System: Read a text file or an image. Note: For images (isImage=true), this tool must be called alone. For text, it returns full content by default. Use summaryQuery for AI-assisted focus.",
                _hasFastModel
                    ? (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "Path relative to AI_Workspace" },
                            summaryQuery = new { type = "string", description = "Only read focal points matching this query (triggers fast AI model processing)" },
                            isImage = new { type = "boolean", description = "Whether to parse as an image (Must be called alone if true)" }
                        },
                        required = new[] { "filePath" }
                    }
                    : (object)new
                    {
                        type = "object",
                        properties = new
                        {
                            filePath = new { type = "string", description = "Path relative to AI_Workspace" },
                            isImage = new { type = "boolean", description = "Whether to parse as an image (Must be called alone if true)" }
                        },
                        required = new[] { "filePath" }
                    }
            );

            // 【檔案系統：寫入檔案】
            // 建立、覆寫或附加內容。自動建立必要的父資料夾。
            yield return client.CreateFunctionDeclaration(
                "write_file",
                "File System: Write to a file (Create, overwrite, or append). Default append=true. Automatically creates missing parent directories (within AI_Workspace).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Path relative to AI_Workspace (e.g., 'notes.txt')" },
                        content = new { type = "string", description = "The string content to write" },
                        append = new { type = "boolean", description = "true (default): append to end; false: overwrite entire file" }
                    },
                    required = new[] { "filePath", "content" }
                }
            );

            // 【檔案系統：單行更新】
            // 精準修改指定行號 (1-based)。適合小型設定檔之微調。
            yield return client.CreateFunctionDeclaration(
                "update_file_line",
                "File System: Single Line Update. Precisely modify a specific line (1-based index). Efficient for fine-tuning configuration or small blocks without sending the full file.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Path relative to AI_Workspace (e.g., 'settings.json')" },
                        lineNumber = new { type = "integer", description = "The 1-based absolute line number to modify" },
                        newContent = new { type = "string", description = "The replacement content for the line" }
                    },
                    required = new[] { "filePath", "lineNumber", "newContent" }
                }
            );

            // 【檔案系統：內容全局搜尋】
            // 全域或特定目錄下的字串搜尋，支援正則表達式 (isRegex) 與上下行預覽。
            yield return client.CreateFunctionDeclaration(
                "search_content",
                "File System: Global Content Search. Search for strings or Regex in text files. Supports path filtering and context preview.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "The exact keyword or regular expression to search for" },
                        path = new { type = "string", description = "Subdirectory to search (Default: global workspace search)" },
                        filePattern = new { type = "string", description = "Limit to file types, e.g., '*.cs' or '*.log'" },
                        contextLines = new { type = "integer", description = "Number of lines of context to include before and after the match (Default: 0)" },
                        isRegex = new { type = "boolean", description = "Whether to treat query as a regular expression (Default: false)" }
                    },
                    required = new[] { "query" }
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
                case "update_file_line":
                    return HandleUpdateFileLine(funcName, args);
                case "search_content":
                    return HandleSearchContent(funcName, args);
                default:
                    return null;
            }
        }

        private string HandleListFiles(Dictionary<string, object> args)
        {
            string subPath = args.ContainsKey("path") ? args["path"].ToString() : "";
            bool sortByTime = args.ContainsKey("sortByTime") && Convert.ToBoolean(args["sortByTime"]);
            string filePattern = args.ContainsKey("filePattern") ? args["filePattern"].ToString() : "";
            return _fileTools.ListFiles(subPath, sortByTime, filePattern);
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
            if (!_fileTools.IsPathAllowed(resolvedPath, aiWorkspacePath))
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


        private string HandleUpdateFileLine(string funcName, Dictionary<string, object> args)
        {
            string errUpd = CheckRequiredArgs(funcName, args);
            if (errUpd != null) return errUpd;

            if (!int.TryParse(args["lineNumber"]?.ToString(), out int lineNum))
            {
                return "[Error] 無效的行號參數，'lineNumber' 必須為整數。";
            }

            string newContent = args["newContent"]?.ToString() ?? string.Empty;
            return _fileTools.UpdateFileLine(args["filePath"].ToString(), lineNum, newContent);
        }

        private string HandleSearchContent(string funcName, Dictionary<string, object> args)
        {
            string errSearchCon = CheckRequiredArgs(funcName, args);
            if (errSearchCon != null) return errSearchCon;

            string sq = args["query"].ToString();
            string spath = args.ContainsKey("path") ? args["path"].ToString() : "";
            string sfPattern = args.ContainsKey("filePattern") ? args["filePattern"].ToString() : "";
            int ctxLines = args.ContainsKey("contextLines") ? Convert.ToInt32(args["contextLines"]) : 0;
            bool isRegex = args.ContainsKey("isRegex") && Convert.ToBoolean(args["isRegex"]);
            return _fileTools.SearchContent(sq, spath, sfPattern, ctxLines, isRegex);
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
