using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OrchX.Tools
{
    /// <summary>
    /// 集中管理所有 AI 模型的 Mock Data 讀取與建立邏輯。
    /// 每個 provider 各自維護獨立的計數器與訊息狀態，互不干擾。
    /// </summary>
    public class MockDataManager
    {
        // 每個 provider 各自維護獨立的計數器與「是否已顯示首次訊息」狀態
        private static readonly ConcurrentDictionary<string, int> _mockCounters = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, bool> _mockMessageShown = new ConcurrentDictionary<string, bool>();
        private static readonly object _syncLock = new object();

        /// <summary>
        /// 是否要將真實的 API 回應記錄到 MockData 資料夾中
        /// </summary>
        public static bool IsRecordingMockData { get; set; } = false;

        /// <summary>
        /// 記錄真實的 API 回應至 MockData 資料夾，流水號自動遞增
        /// </summary>
        public static void RecordMockResponse(string providerName, string rawJson)
        {
            if (!IsRecordingMockData) return;

            string normalizedName = providerName.ToLower();
            string basePath = Environment.CurrentDirectory;
            string mockDataDir = Path.Combine(basePath, "MockData");

            if (!Directory.Exists(mockDataDir))
            {
                Directory.CreateDirectory(mockDataDir);
            }

            int nextSequenceNumber = 1;
            
            // 尋找現有的檔案以決定下一個流水號
            string searchPattern = $"{normalizedName}_mock_response_*.json";
            string[] existingFiles = Directory.GetFiles(mockDataDir, searchPattern);
            
            foreach (string file in existingFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                // 預期檔案名稱格式為 {normalizedName}_mock_response_{counter:D4}
                string prefix = $"{normalizedName}_mock_response_";
                if (fileName.StartsWith(prefix) && int.TryParse(fileName.Substring(prefix.Length), out int num))
                {
                    if (num >= nextSequenceNumber)
                    {
                        nextSequenceNumber = num + 1;
                    }
                }
            }

            string targetFileName = $"{normalizedName}_mock_response_{nextSequenceNumber:D4}.json";
            string targetPath = Path.Combine(mockDataDir, targetFileName);

            try
            {
                File.WriteAllText(targetPath, rawJson, Encoding.UTF8);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[System] 已成功記錄 {providerName} API 回應至 {targetPath}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[Error] 寫入 MockData 失敗: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// 取得指定 provider 的模擬回應資料。
        /// providerName 會被正規化為小寫，以確保檔案命名一致（例如 "gemini_mock_response_0001.json"）。
        /// </summary>
        public static string GetMockResponse(string providerName = "gemini")
        {
            lock (_syncLock)
            {
                // 正規化為小寫，確保與既有檔案命名一致
                string normalizedName = providerName.ToLower();

                // 初始化該 provider 的狀態
                _mockCounters.TryAdd(normalizedName, 1);
                _mockMessageShown.TryAdd(normalizedName, false);

                int counter = _mockCounters[normalizedName];
                string basePath = Environment.CurrentDirectory;
                string mockFileName = $"{normalizedName}_mock_response_{counter:D4}.json";
                string mockFilePath = Path.Combine(basePath, "MockData", mockFileName);

                if (File.Exists(mockFilePath))
                {
                    if (!_mockMessageShown[normalizedName])
                    {
                        Console.WriteLine($"\n[{providerName}Client] 尚未設定 API KEY，讀取模擬回應資料 ({mockFilePath})...");
                        _mockMessageShown[normalizedName] = true;
                    }
                    else
                    {
                        Console.WriteLine($"\n[{providerName}Client] 讀取模擬回應資料 ({mockFilePath})...");
                    }

                    string mockFileContent = File.ReadAllText(mockFilePath);
                    _mockCounters[normalizedName] = counter + 1;
                    return mockFileContent;
                }
                else
                {
                    Console.WriteLine($"\n[System] 找不到模擬回應檔案，正在自動建立空白檔案: {mockFilePath}");
                    
                    var directory = Path.GetDirectoryName(mockFilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string defaultMockContent = GetDefaultMockContent(normalizedName);
                    File.WriteAllText(mockFilePath, defaultMockContent, Encoding.UTF8);
                    
                    throw new Exception($"尚未設定 API KEY，且找不到模擬回應檔案。\n系統已自動於路徑建立空白檔案：{mockFilePath}\n請在該檔案中的 'text' 欄位(或對應欄位)填入您想測試的回應內容後再試一次。");
                }
            }
        }

        private static string GetDefaultMockContent(string providerName)
        {
            if (providerName == "gemini")
            {
                return @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""請在這裡填寫你想測試的回應內容。\n支援多行與 Markdown 格式。\n例如：\n\n這是一個測試回應。""
          }
        ],
        ""role"": ""model""
      },
      ""finishReason"": ""STOP""
    }
  ]
}";
            }
            
            return @"{
  ""mock_response"": ""請在這裡填寫你想測試的回應內容。""
}";
        }
    }
}
