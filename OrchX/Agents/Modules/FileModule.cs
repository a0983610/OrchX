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
            yield return client.CreateFunctionDeclaration(
                "list_files",
                "【檔案系統：列出目錄與檔案】以樹狀結構列出指定路徑的內容。預設為根目錄(限制於 AI_Workspace 內，最多往下掃描 3 層子目錄)。",
                new { type = "object", properties = new { 
                    path = new { type = "string", description = "相對於 AI_Workspace 的資料夾路徑 (例如 / 或 notes)，留空代表根目錄" },
                    sortByTime = new { type = "boolean", description = "是否依據修改時間(由新到舊)排序 (預設為 false，依檔名排序)" },
                    filePattern = new { type = "string", description = "限制檔案類型過濾，例如 *.cs 或是 text*.txt" }
                } }
            );

            yield return client.CreateFunctionDeclaration(
                "read_file",
                "【檔案系統：讀取檔案或圖片】讀取文字檔或圖片。注意：若為圖片(isImage=true)，必須單獨呼叫本工具。讀取文字預設回傳完整內容。" + (_hasFastModel ? "若帶有 summaryQuery 將由快速模型先進行摘要。" : ""),
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
                "【檔案系統：寫入檔案】建立、覆寫或附加內容至檔案。預設 append=true 附加於檔尾，若要完全覆寫請設為 false。自動遞迴建立不存在的資料夾(限 AI_Workspace)。",
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
                "update_file_line",
                "【檔案系統：單行更新】精準修改文字檔中的特定一行(1-based)。適合微調單一設定或小區塊，避免傳輸完整檔案。",
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
                "【檔案系統：內容全局搜尋】在文字檔中檢索特定字串或正則表達式(isRegex)。支援過濾路徑與副檔名，並可提供 contextLines 查看上下文。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "要搜尋的精確關鍵字或正則表達式字串" },
                        path = new { type = "string", description = "搜尋範圍的子目錄，相對於 AI_Workspace (預設為空字串，代表全局搜索)" },
                        filePattern = new { type = "string", description = "限制檔案類型，例如 *.cs 或 *.log" },
                        contextLines = new { type = "integer", description = "除了找到的那行外，額外回傳它的上下行數量 (預設為 0)" },
                        isRegex = new { type = "boolean", description = "是否將 query 視為正則表達式 (預設為 false)" }
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
