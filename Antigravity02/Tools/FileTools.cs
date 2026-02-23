using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.IO.Compression;
using System.Xml;

namespace Antigravity02.Tools
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
            _baseDirectory = Path.GetFullPath(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory);
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

        public string ListFiles(string subPath = "")
        {
            try
            {
                // 安全檢查：不允許向上層目錄存取
                if (subPath.Contains("..")) return "錯誤：禁止存取上層目錄。";

                string targetPath = Path.GetFullPath(Path.Combine(_baseDirectory, subPath));
                
                // 確保目標路徑仍在根目錄內
                if (!targetPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return "錯誤：超出授權存取範圍。";
                }

                if (!Directory.Exists(targetPath))
                {
                    return $"錯誤：路徑 {subPath} 不存在。";
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"[Folder Tree: {subPath}]");
                BuildTree(targetPath, 0, 3, sb);

                return sb.ToString();
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ListFiles) Error: {ex.Message}");
                return $"錯誤：無法讀取清單。{ex.Message}";
            }
        }

        private void BuildTree(string currentPath, int currentDepth, int maxDepth, StringBuilder sb)
        {
            if (currentDepth >= maxDepth) return;

            try
            {
                var entries = Directory.GetFileSystemEntries(currentPath);
                string indent = new string(' ', currentDepth * 4);

                foreach (var entry in entries)
                {
                    bool isDir = Directory.Exists(entry);
                    string name = Path.GetFileName(entry);
                    
                    if (isDir)
                    {
                        DirectoryInfo di = new DirectoryInfo(entry);
                        sb.AppendLine($"{indent}[DIR]  {name} (Created: {di.CreationTime:yyyy-MM-dd})");
                        BuildTree(entry, currentDepth + 1, maxDepth, sb);
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(entry);
                        string sizeStr = FormatSize(fi.Length);
                        // 對齊優化：檔名靠左30字元，大小靠右8字元
                        sb.AppendLine($"{indent}[FILE] {name, -30} | {sizeStr, 8} | Mod: {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                    }
                }
                
                if (entries.Length == 0 && currentDepth == 0)
                {
                    sb.AppendLine("(此資料夾是空的)");
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
                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string filePath = Path.GetFullPath(Path.Combine(_baseDirectory, fileName));

                // 安全檢查
                if (!filePath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
                    return "錯誤：超出授權範圍。";

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
                else if (IsImageExtension(extension))
                {
                    return ReadImageAsBase64(filePath, extension);
                }
                else
                {
                    return "不支援的檔案格式或禁止存取。";
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
                // 如果沒有指定副檔名，預設使用 .txt
                if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    fileName += ".txt";
                }

                // 防禦 Path Traversal
                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string filePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder, fileName));
                string aiWorkspacePath = Path.GetFullPath(Path.Combine(_baseDirectory, _aiOutputFolder));

                // 安全檢查：確保目標路徑仍在 AI_Workspace 內
                if (!filePath.StartsWith(aiWorkspacePath, StringComparison.OrdinalIgnoreCase))
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
        /// 4. 刪除檔案 (限制範圍)
        /// </summary>
        public string DeleteFile(string fileName)
        {
            try
            {
                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string filePath = Path.GetFullPath(Path.Combine(_baseDirectory, fileName));

                // 安全檢查
                if (!filePath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
                    return "錯誤：超出授權範圍。";

                if (!File.Exists(filePath))
                {
                    return $"錯誤：找不到檔案 {fileName}。";
                }

                File.Delete(filePath);
                return $"成功：已刪除檔案 {fileName}";
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(DeleteFile) Error: {ex.Message}");
                return $"錯誤：無法刪除檔案。{ex.Message}";
            }
        }

        /// <summary>
        /// 5. 修改檔案指定行數
        /// </summary>
        public string UpdateFileLine(string fileName, int lineNumber, string newContent)
        {
            try
            {
                if (fileName.Contains("..")) return "錯誤：格式不合法。";

                string filePath = Path.GetFullPath(Path.Combine(_baseDirectory, fileName));

                // 安全檢查
                if (!filePath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
                    return "錯誤：超出授權範圍。";

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
                    if (entry == null) return "無效的 .docx 檔案。";

                    using (Stream stream = entry.Open())
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(stream);

                        // 使用命名空間管理器處理 Word 的 XML 命名空間
                        XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                        nsmgr.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

                        XmlNodeList nodes = xmlDoc.SelectNodes("//w:t", nsmgr);
                        foreach (XmlNode node in nodes)
                        {
                            sb.Append(node.InnerText);
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadDocxText) Error: {ex.Message}");
                return $"讀取 .docx 時發生錯誤：{ex.Message}";
            }
        }
        /// <summary>
        /// 判斷副檔名是否為支援的圖片格式
        /// </summary>
        private bool IsImageExtension(string extension)
        {
            switch (extension)
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".webp":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 取得圖片的 MIME type
        /// </summary>
        private string GetImageMimeType(string extension)
        {
            switch (extension)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                default: return "application/octet-stream";
            }
        }

        /// <summary>
        /// 讀取圖片檔案，若太大則縮小，回傳特殊標記格式讓上層解析
        /// 格式：[IMAGE_BASE64:mime_type:base64data]
        /// </summary>
        private string ReadImageAsBase64(string filePath, string extension)
        {
            const int MaxDimension = 1024; // 最大邊長
            const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB 上限

            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length > MaxFileSizeBytes)
                {
                    return $"錯誤：圖片檔案過大 ({FormatSize(fi.Length)})，超過 10MB 上限。";
                }

                string mimeType = GetImageMimeType(extension);

                using (var originalImage = Image.FromFile(filePath))
                {
                    int origWidth = originalImage.Width;
                    int origHeight = originalImage.Height;

                    // 判斷是否需要縮小
                    if (origWidth > MaxDimension || origHeight > MaxDimension)
                    {
                        // 等比縮放
                        double ratio = Math.Min((double)MaxDimension / origWidth, (double)MaxDimension / origHeight);
                        int newWidth = (int)(origWidth * ratio);
                        int newHeight = (int)(origHeight * ratio);

                        using (var resized = new Bitmap(newWidth, newHeight))
                        {
                            using (var g = Graphics.FromImage(resized))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = SmoothingMode.HighQuality;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                            }

                            // 縮小後以 JPEG 編碼（節省空間），除非原圖是 PNG 且可能有透明
                            using (var ms = new MemoryStream())
                            {
                                ImageFormat outputFormat;
                                if (extension == ".png")
                                {
                                    outputFormat = ImageFormat.Png;
                                    // mimeType 保持 image/png
                                }
                                else
                                {
                                    outputFormat = ImageFormat.Jpeg;
                                    mimeType = "image/jpeg";
                                }
                                resized.Save(ms, outputFormat);
                                string base64 = Convert.ToBase64String(ms.ToArray());
                                return $"[IMAGE_BASE64:{mimeType}:{base64}]\n[原始大小: {origWidth}x{origHeight}, 已縮放至: {newWidth}x{newHeight}]";
                            }
                        }
                    }
                    else
                    {
                        // 不需縮放，直接讀取原始檔案 bytes
                        byte[] imageBytes = File.ReadAllBytes(filePath);
                        string base64 = Convert.ToBase64String(imageBytes);
                        return $"[IMAGE_BASE64:{mimeType}:{base64}]\n[大小: {origWidth}x{origHeight}]";
                    }
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadImageAsBase64) Error: {ex.Message}");
                return $"錯誤：無法讀取圖片。{ex.Message}";
            }
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
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

                string targetPath = Path.GetFullPath(Path.Combine(_baseDirectory, subPath));
                
                if (!targetPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
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

                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                return serializer.Serialize(skills);
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"FileTools(ReadSkills) Error: {ex.Message}");
                return $"錯誤：無法讀取技能清單。{ex.Message}";
            }
        }

        /// <summary>
        /// 寫入技能檔案，強制建立在 AI_Workspace/.agent/skills/{skillName}/SKILL.md
        /// </summary>
        public string WriteSkill(string skillName, string name, string description, string content)
        {
            try
            {
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
