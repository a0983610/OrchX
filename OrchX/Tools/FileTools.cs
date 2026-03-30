using System;
using System.Collections.Generic;

using System.IO;
using System.Text;
using System.IO.Compression;
using System.Xml;
using System.Text.RegularExpressions;

namespace OrchX.Tools
{
    public class FileTools
    {
        private readonly string _aiOutputFolder = "AI_Workspace";
        private string _baseDirectory;

        /// <summary>
        /// 取得技能檔案的固定存放路徑 (AI_Workspace/.agent/skills)
        /// </summary>
        public string SkillsPath
        {
            get { return Path.Combine(_aiOutputFolder, ".agent", "skills"); }
        }

        public FileTools(string baseDirectory = null)
        {
            // 規範化路徑並確保以分隔符結尾，防止 "C:\Path" 比對到 "C:\PathSecret"
            _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
            if (!_baseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                _baseDirectory += Path.DirectorySeparatorChar;
            }

            // 確保 AI 輸出資料夾存在
            string path = Path.Combine(_baseDirectory, _aiOutputFolder);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            // 確保 .agent 相關子資料夾存在
            string agentPath = Path.Combine(path, ".agent");
            string[] agentSubDirs = { "knowledge", "feature_requests", "skills" };
            foreach (var sub in agentSubDirs)
            {
                string subDir = Path.Combine(agentPath, sub);
                if (!Directory.Exists(subDir)) Directory.CreateDirectory(subDir);
            }
        }

        public bool IsPathAllowed(string targetPath, string allowedBasePath)
        {
            string fullTarget = Path.GetFullPath(targetPath);
            string fullAllowed = Path.GetFullPath(allowedBasePath);

            if (!fullAllowed.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullAllowed += Path.DirectorySeparatorChar;
            if (!fullTarget.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullTarget += Path.DirectorySeparatorChar;

            return fullTarget.StartsWith(fullAllowed, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 自動移除路徑開頭的 AI_Workspace/ 或 AI_Workspace\ 前綴 (若 AI 誤傳)。
        /// 僅在前綴後緊接 / 或 \ 時才移除，避免誤判 (例如 AI_WorkspaceSecret 不會被匹配)。
        /// </summary>
        private string StripOutputFolderPrefix(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // 完全等於 AI_Workspace (不帶斜線)
            if (path.Equals(_aiOutputFolder, StringComparison.OrdinalIgnoreCase))
                return "";

            // 以 AI_Workspace/ 或 AI_Workspace\ 開頭
            string prefixSlash = _aiOutputFolder + "/";
            string prefixBackslash = _aiOutputFolder + "\\";
            if (path.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                return path.Substring(prefixSlash.Length);
            if (path.StartsWith(prefixBackslash, StringComparison.OrdinalIgnoreCase))
                return path.Substring(prefixBackslash.Length);

            return path;
        }

        public string ListFiles(string subPath = "", bool sortByTime = false, string filePattern = "")
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                subPath = StripOutputFolderPrefix(subPath);

                // 安全檢查：不允許向上層目錄存取
                if (subPath.Contains("..")) return "錯誤：禁止存取上層目錄。";

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string targetPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, subPath.TrimStart('/', '\\')));
                
                // 確保目標路徑仍在 AI_Workspace 內
                if (!IsPathAllowed(targetPath, aiWorkspacePath))
                {
                    return "錯誤：超出授權存取範圍。";
                }

                if (!Directory.Exists(targetPath))
                {
                    return $"錯誤：路徑 {subPath} 不存在。";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"[Folder Tree: {subPath}]");
                BuildTree(targetPath, 0, 3, sb, sortByTime, filePattern);

                return sb.ToString();
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ListFiles) Error: {ex.Message}");
                return $"錯誤：無法讀取清單。{ex.Message}";
            }
        }

        private void BuildTree(string currentPath, int currentDepth, int maxDepth, StringBuilder sb, bool sortByTime, string filePattern)
        {
            if (currentDepth >= maxDepth) return;

            try
            {
                string indent = new string(' ', currentDepth * 4);
                
                var dirInfoList = new List<DirectoryInfo>();
                foreach (var d in Directory.GetDirectories(currentPath))
                    dirInfoList.Add(new DirectoryInfo(d));

                var fileInfoList = new List<FileInfo>();
                string searchPattern = string.IsNullOrEmpty(filePattern) ? "*" : filePattern;
                foreach (var f in Directory.GetFiles(currentPath, searchPattern))
                    fileInfoList.Add(new FileInfo(f));

                if (sortByTime)
                {
                    dirInfoList.Sort((a, b) => b.CreationTime.CompareTo(a.CreationTime));
                    fileInfoList.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                }
                else
                {
                    dirInfoList.Sort((a, b) => a.Name.CompareTo(b.Name));
                    fileInfoList.Sort((a, b) => a.Name.CompareTo(b.Name));
                }

                foreach (var di in dirInfoList)
                {
                    sb.AppendLine($"{indent}[DIR]  {di.Name} (Created: {di.CreationTime:yyyy-MM-dd})");
                    BuildTree(di.FullName, currentDepth + 1, maxDepth, sb, sortByTime, filePattern);
                }

                foreach (var fi in fileInfoList)
                {
                    string sizeStr = FormatSize(fi.Length);
                    // 對齊優化：檔名靠左30字元，大小靠右8字元
                    sb.AppendLine($"{indent}[FILE] {fi.Name, -30} | {sizeStr, 8} | Mod: {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
                
                if (dirInfoList.Count == 0 && fileInfoList.Count == 0 && currentDepth == 0)
                {
                    sb.AppendLine("(此資料夾是空的或沒有符合的檔案)");
                }
            }
            catch (UnauthorizedAccessException)
            {
                sb.AppendLine(new string(' ', currentDepth * 4) + "[存取被拒絕]");
            }
            catch (Exception ex)
            {
                sb.AppendLine(new string(' ', currentDepth * 4) + $"[錯誤: {ex.Message}]");
            }
        }

        /// <summary>
        /// 2. 讀取特定檔案 (保持唯讀，且限制範圍)
        /// </summary>
        public string ReadFile(string fileName)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                fileName = StripOutputFolderPrefix(fileName);

                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string filePath = Path.GetFullPath(Path.Combine(aiWorkspacePath, fileName.TrimStart('/', '\\')));

                // 安全檢查
                if (!IsPathAllowed(filePath, aiWorkspacePath))
                    return "錯誤：超出授權存取範圍，僅可讀取 AI_Workspace 內的檔案。";

                if (!File.Exists(filePath))
                {
                    return $"錯誤：找不到檔案 {fileName}。";
                }

                string extension = Path.GetExtension(filePath).ToLower();

                if (extension == ".txt" || extension == ".md" || extension == ".json" || extension == ".cs")
                {
                    return File.ReadAllText(filePath, Encoding.UTF8);
                }
                else if (extension == ".docx")
                {
                    return ReadDocxText(filePath);
                }
                else
                {
                    return "錯誤：不支援的檔案格式或禁止存取。";
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadFile) Error: {ex.Message}");
                return $"錯誤：無法讀取檔案。{ex.Message}";
            }
        }

        /// <summary>
        /// 3. 儲存檔案/輸出至 AI_Workspace 資料夾
        /// </summary>
        public string WriteFile(string fileName, string content, bool append = true)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                fileName = StripOutputFolderPrefix(fileName);

                // 如果沒有指定副檔名，預設使用 .txt
                if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    fileName += ".txt";
                }

                // 防禦 Path Traversal
                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string filePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder, fileName.TrimStart('/', '\\')));
                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));

                // 安全檢查：確保目標路徑仍在 AI_Workspace 內
                if (!IsPathAllowed(filePath, aiWorkspacePath))
                    return "錯誤：超出授權寫入範圍，僅可寫入 AI_Workspace 內。";

                // 確保目標資料夾存在
                string targetDir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                
                if (append)
                {
                    File.AppendAllText(filePath, content + Environment.NewLine, Encoding.UTF8);
                }
                else
                {
                    File.WriteAllText(filePath, content, Encoding.UTF8);
                }

                string relativePath = filePath.Substring(aiWorkspacePath.Length).TrimStart(Path.DirectorySeparatorChar).Replace("\\", "/");
                string actionType = append ? "附加內容" : "儲存檔案(覆蓋)";
                return $"成功：{actionType}已完成至 {_aiOutputFolder}/{relativePath}";
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(WriteFile) Error: {ex.Message}");
                return $"錯誤：無法儲存檔案。{ex.Message}";
            }
        }

        /// <summary>
        /// 4. 刪除檔案或資料夾 (限制範圍)
        /// </summary>
        public string DeleteFile(string path, bool recursive = false)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                path = StripOutputFolderPrefix(path);

                if (path.Contains("..")) return "錯誤：格式不合法。";

                // 防止刪除根目錄 (空路徑或只剩 / \ 的情況)
                if (string.IsNullOrWhiteSpace(path) || path == "/" || path == "\\")
                {
                    return "錯誤：禁止刪除根目錄 AI_Workspace。";
                }

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string targetPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, path.TrimStart('/', '\\')));

                // 再次確保不是根目錄 (比對解析後是否等同於 AI Workspace 根目錄)
                if (targetPath.Equals(aiWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    return "錯誤：禁止刪除根目錄 AI_Workspace。";
                }

                // 安全檢查
                if (!IsPathAllowed(targetPath, aiWorkspacePath))
                    return "錯誤：超出授權範圍，僅可刪除 AI_Workspace 內的路徑。";

                if (Directory.Exists(targetPath))
                {
                    if (!recursive && Directory.GetFileSystemEntries(targetPath).Length > 0)
                    {
                        return $"錯誤：資料夾 {path} 內部包含檔案或子資料夾，請使用 recursive=true 參數來強制刪除。";
                    }
                    Directory.Delete(targetPath, recursive);
                    return $"成功：已刪除資料夾 {path}";
                }
                else if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                    return $"成功：已刪除檔案 {path}";
                }
                else
                {
                    return $"錯誤：找不到檔案或資料夾 {path}。";
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(DeleteFile) Error: {ex.Message}");
                return $"錯誤：無法刪除路徑。{ex.Message}";
            }
        }

        /// <summary>
        /// 5. 修改檔案指定行數
        /// </summary>
        public string UpdateFileLine(string fileName, int lineNumber, string newContent)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                fileName = StripOutputFolderPrefix(fileName);

                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string filePath = Path.GetFullPath(Path.Combine(aiWorkspacePath, fileName.TrimStart('/', '\\')));

                // 安全檢查
                if (!IsPathAllowed(filePath, aiWorkspacePath))
                    return "錯誤：超出授權範圍，僅可修改 AI_Workspace 內的檔案。";

                if (!File.Exists(filePath))
                {
                    return $"錯誤：找不到檔案 {fileName}。";
                }

                List<string> lines = new List<string>(File.ReadAllLines(filePath, Encoding.UTF8));

                if (lineNumber < 1 || lineNumber > lines.Count)
                {
                    return $"錯誤：行號 {lineNumber} 超出範圍 (總行數: {lines.Count})。";
                }

                // 修改指定行 (index = lineNumber - 1)
                lines[lineNumber - 1] = newContent;

                File.WriteAllLines(filePath, lines, Encoding.UTF8);
                return $"成功：已修改 {fileName} 第 {lineNumber} 行。";
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(UpdateFileLine) Error: {ex.Message}");
                return $"錯誤：無法修改檔案。{ex.Message}";
            }
        }

        /// <summary>
        /// 6. 搬移檔案 (限制範圍)
        /// </summary>
        public string MoveFile(string sourceFileName, string destinationFileName)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                sourceFileName = StripOutputFolderPrefix(sourceFileName);
                destinationFileName = StripOutputFolderPrefix(destinationFileName);

                if (sourceFileName.Contains("..") || destinationFileName.Contains(".."))
                    return "錯誤：格式不合法。";

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string sourcePath = Path.GetFullPath(Path.Combine(aiWorkspacePath, sourceFileName.TrimStart('/', '\\')));
                string destinationPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, destinationFileName.TrimStart('/', '\\')));

                // 安全檢查
                if (!IsPathAllowed(sourcePath, aiWorkspacePath) || !IsPathAllowed(destinationPath, aiWorkspacePath))
                    return "錯誤：超出授權範圍，僅可搬移 AI_Workspace 內的檔案。";

                if (!File.Exists(sourcePath))
                {
                    return $"錯誤：找不到來源檔案 {sourceFileName}。";
                }

                // 確保目標資料夾存在
                string targetDir = Path.GetDirectoryName(destinationPath);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                try
                {
                    File.Move(sourcePath, destinationPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    return $"錯誤：目標檔案 {destinationFileName} 已存在。";
                }

                return $"成功：已將檔案 {sourceFileName} 搬移至 {destinationFileName}";
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(MoveFile) Error: {ex.Message}");
                return $"錯誤：無法搬移檔案。{ex.Message}";
            }
        }

        /// <summary>
        /// 簡易的 .docx 文字提取 (透過讀取 zip 內的 word/document.xml)
        /// </summary>
        private string ReadDocxText(string filePath)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(filePath))
                {
                    var entry = archive.GetEntry("word/document.xml");
                    if (entry == null) return "錯誤：無效的 .docx 檔案。";

                    using (Stream stream = entry.Open())
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(stream);

                        // 使用命名空間管理器處理 Word 的 XML 命名空間
                        XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                        nsmgr.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

                        // 依據段落 <w:p> 遍歷，並在段落間加入換行符號
                        XmlNodeList paragraphs = xmlDoc.SelectNodes("//w:p", nsmgr);
                        if (paragraphs != null)
                        {
                            foreach (XmlNode pNode in paragraphs)
                            {
                                sb.AppendLine(pNode.InnerText);
                            }
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadDocxText) Error: {ex.Message}");
                return $"錯誤：讀取 .docx 時發生錯誤：{ex.Message}";
            }
        }


        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1}{1}", number, suffixes[counter]);
        }

        /// <summary>
        /// 讀取指定路徑下所有資料夾內的 SKILL.md，擷取其中的 name 與 description，並回傳結構化的 JSON 字串
        /// </summary>
        public string ReadSkills(string subPath)
        {
            try
            {
                if (subPath.Contains("..")) return "錯誤：禁止存取上層目錄。";

                string targetPath = Path.GetFullPath(Path.Combine(_baseDirectory, subPath.TrimStart('/', '\\')));
                
                if (!IsPathAllowed(targetPath, _baseDirectory))
                    return "錯誤：超出授權存取範圍。";

                if (!Directory.Exists(targetPath))
                    return "[]"; // 目錄尚未建立，回傳空清單

                var skillFiles = Directory.GetFiles(targetPath, "SKILL.md", SearchOption.AllDirectories);
                var skills = new List<Dictionary<string, string>>();

                foreach (var file in skillFiles)
                {
                    try
                    {
                        var lines = File.ReadAllLines(file, Encoding.UTF8);
                        bool inFrontmatter = false;
                        string name = null;
                        string description = null;

                        foreach (var line in lines)
                        {
                            if (line.Trim() == "---")
                            {
                                if (!inFrontmatter)
                                {
                                    inFrontmatter = true;
                                    continue;
                                }
                                else
                                {
                                    break; // 結束 frontmatter
                                }
                            }

                            if (inFrontmatter)
                            {
                                if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = line.Substring(5).Trim();
                                }
                                else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                                {
                                    description = line.Substring(12).Trim();
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(name))
                        {
                            string relativePath = file.Substring(_baseDirectory.Length).Replace("\\", "/");
                            skills.Add(new Dictionary<string, string>
                            {
                                { "name", name },
                                { "description", description ?? "" },
                                { "filePath", relativePath }
                            });
                        }
                    }
                    catch
                    {
                        // 忽略單一檔案讀取錯誤
                    }
                }

                return JsonTools.Serialize(skills);
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadSkills) Error: {ex.Message}");
                return $"錯誤：無法讀取技能清單。{ex.Message}";
            }
        }

        /// <summary>
        /// 全局搜尋包含特定關鍵字的檔案
        /// </summary>
        public string SearchContent(string query, string subPath = "", string filePattern = "", int contextLines = 0, bool isRegex = false)
        {
            try
            {
                // 自動移除 AI_Workspace 前綴 (若 AI 誤傳)
                subPath = StripOutputFolderPrefix(subPath);

                if (subPath.Contains("..")) return "錯誤：禁止存取上層目錄。";

                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));
                string targetPath = Path.GetFullPath(Path.Combine(aiWorkspacePath, subPath.TrimStart('/', '\\')));

                // 安全檢查：確保目標路徑仍在 AI_Workspace 內
                if (!IsPathAllowed(targetPath, aiWorkspacePath))
                {
                    return "錯誤：超出授權存取範圍。";
                }

                if (!Directory.Exists(targetPath))
                {
                    return $"錯誤：目錄 {subPath} 不存在。";
                }

                if (string.IsNullOrEmpty(filePattern)) filePattern = "*.*";

                var files = Directory.EnumerateFiles(targetPath, filePattern, SearchOption.AllDirectories);
                
                string[] ignoredExtensions = { ".exe", ".dll", ".png", ".jpg", ".jpeg", ".gif", ".pdf", ".zip", ".tar", ".gz", ".pdb", ".bin", ".obj" };
                
                // Collect valid files and their LastWriteTime
                var validFiles = new List<Tuple<string, DateTime>>();
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length > 10 * 1024 * 1024) continue; // 超過 10MB 跳過
                        
                        string ext = fi.Extension.ToLower();
                        if (Array.IndexOf(ignoredExtensions, ext) >= 0) continue;

                        validFiles.Add(new Tuple<string, DateTime>(file, fi.LastWriteTime));
                    }
                    catch { } // 忽略權限或讀取錯誤
                }

                // 優先考慮最近修改的檔案 (Descending order)
                validFiles.Sort((a, b) => b.Item2.CompareTo(a.Item2));

                Regex regex = null;
                if (isRegex)
                {
                    try
                    {
                        regex = new Regex(query, RegexOptions.IgnoreCase);
                    }
                    catch (Exception ex)
                    {
                        return $"錯誤：無效的正則表達式 '{query}'。{ex.Message}";
                    }
                }

                StringBuilder sb = new StringBuilder();
                int matchCount = 0;
                bool limitReached = false;

                foreach (var fileTuple in validFiles)
                {
                    if (limitReached) break;
                    
                    try
                    {
                        string filePath = fileTuple.Item1;
                        string relativePath = filePath.Substring(aiWorkspacePath.Length).TrimStart(Path.DirectorySeparatorChar).Replace("\\", "/");
                        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

                        for (int i = 0; i < lines.Length; i++)
                        {
                            bool isMatch = isRegex ? regex.IsMatch(lines[i]) : lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isMatch)
                            {
                                if (matchCount == 0) sb.AppendLine($"搜尋結果 (關鍵字: {query})：\n");

                                sb.AppendLine($"[{relativePath}][Line {i + 1}] {lines[i].Trim()}");
                                
                                // Handle context lines
                                if (contextLines > 0)
                                {
                                    int start = Math.Max(0, i - contextLines);
                                    int end = Math.Min(lines.Length - 1, i + contextLines);
                                    for (int j = start; j <= end; j++)
                                    {
                                        if (j != i)
                                        {
                                            sb.AppendLine($"  {j + 1}: {lines[j].Trim()}");
                                        }
                                    }
                                    sb.AppendLine("---");
                                }

                                matchCount++;
                                if (matchCount >= 50)
                                {
                                    limitReached = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { } // 讀取失敗則跳過
                }

                if (matchCount == 0) return "找不到符合的內容。(註：超過 10MB 的檔案以及常見的二進位格式將會被自動忽略)";
                if (limitReached) sb.AppendLine("\n結果過多，請縮小關鍵字範圍。");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(SearchContent) Error: {ex.Message}");
                return $"錯誤：搜尋失敗。{ex.Message}";
            }
        }

        /// <summary>
        /// 寫入技能檔案，強制建立在 AI_Workspace/.agent/skills/{skillName}/SKILL.md
        /// 支援基礎的 {{key}} 替換
        /// </summary>
        public string WriteSkill(string skillName, string name, string description, string content, Dictionary<string, string> variables = null)
        {
            try
            {
                if (variables != null)
                {
                    foreach (var kvp in variables)
                    {
                        content = content.Replace("{{" + kvp.Key + "}}", kvp.Value ?? "");
                    }
                }

                // 過濾資料夾名稱，防止 Path Traversal
                string safeSkillName = Path.GetFileName(skillName);
                if (string.IsNullOrWhiteSpace(safeSkillName))
                {
                    return "錯誤：技能名稱無效。";
                }

                string folderPath = Path.Combine(_baseDirectory, _aiOutputFolder, ".agent", "skills", safeSkillName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string filePath = Path.Combine(folderPath, "SKILL.md");

                bool fileExists = File.Exists(filePath);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("---");
                sb.AppendLine($"name: {name}");
                sb.AppendLine($"description: {description}");
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine(content);

                // File.WriteAllText 本身已經會直接覆寫存在的檔案
                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                string action = fileExists ? "覆蓋" : "建立";
                return $"成功：已{action}技能檔案至 {_aiOutputFolder}/.agent/skills/{safeSkillName}/SKILL.md";
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(WriteSkill) Error: {ex.Message}");
                return $"錯誤：無法建立技能檔案。{ex.Message}";
            }
        }
    }
}
