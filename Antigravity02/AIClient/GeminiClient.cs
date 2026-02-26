using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Antigravity02.Tools;

namespace Antigravity02.AIClient
{
    public class GenerateContentRequest
    {
        public object Contents { get; set; }
        public List<object> Tools { get; set; }
        public string SystemInstruction { get; set; }
    }

    public class GeminiClient : IAIClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        public string ModelName => _model;
        private static readonly HttpClient _httpClient = new HttpClient();
        private static bool _mockMessageShown = false;
        private static int _mockCounter = 1;

        public GeminiClient(string apiKey, string model = "gemini-2.5-flash")
        {
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> GenerateContentAsync(GenerateContentRequest request)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                // 決定檔案建立的路徑 (固定在程式碼執行目錄，即專案所在位置)
                string basePath = Environment.CurrentDirectory;
                string mockFileName = $"gemini_mock_response_{_mockCounter:D4}.json";
                string mockFilePath = System.IO.Path.Combine(basePath, "MockData", mockFileName);

                if (System.IO.File.Exists(mockFilePath))
                {
                    if (!_mockMessageShown)
                    {
                        Console.WriteLine($"\n[GeminiClient] 尚未設定 API KEY，讀取模擬回應資料 ({mockFilePath})...");
                        _mockMessageShown = true;
                    }
                    else
                    {
                        Console.WriteLine($"\n[GeminiClient] 讀取模擬回應資料 ({mockFilePath})...");
                    }

                    string mockFileContent = System.IO.File.ReadAllText(mockFilePath);
                    _mockCounter++;
                    return mockFileContent;
                }
                else
                {
                    // 自動建立空白檔案並填入預設結構
                    Console.WriteLine($"\n[System] 找不到模擬回應檔案，正在自動建立空白檔案: {mockFilePath}");
                    
                    var directory = System.IO.Path.GetDirectoryName(mockFilePath);
                    if (!System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    string defaultMockContent = @"{
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
                    System.IO.File.WriteAllText(mockFilePath, defaultMockContent, Encoding.UTF8);
                    
                    throw new Exception($"尚未設定 API KEY，且找不到模擬回應檔案。\n系統已自動於路徑建立空白檔案：{mockFilePath}\n請在該檔案中的 'text' 欄位填入您想測試的回應內容後再試一次。");
                }
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            object systemInstObj = null;
            if (!string.IsNullOrEmpty(request.SystemInstruction))
            {
                systemInstObj = new { parts = new[] { new { text = request.SystemInstruction } } };
            }

            var requestBody = new
            {
                contents = request.Contents,
                tools = request.Tools,
                system_instruction = systemInstObj
            };

            var json = JsonTools.Serialize(requestBody);
            int maxRetries = 3;
            int currentRetry = 0;
            int delayMs = 2000;

            HttpResponseMessage response = null;
            string responseJson = null;

            while (true)
            {
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    response = await _httpClient.PostAsync(url, content);
                    responseJson = await response.Content.ReadAsStringAsync();
                }

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                // 紀錄詳細錯誤資訊
                UsageLogger.LogApiError($"API Error: {response.StatusCode}", json, responseJson);

                if ((int)response.StatusCode == 429)
                {
                    if (currentRetry < maxRetries)
                    {
                        currentRetry++;
                        Console.WriteLine($"\n[GeminiClient] 收到 HTTP 429 (Too Many Requests)，等待 {delayMs / 1000.0} 秒後進行第 {currentRetry} 次重試...");
                        await Task.Delay(delayMs);
                        delayMs *= 2; // 指數退避
                        continue;
                    }
                    else
                    {
                        throw new Exception(
                            "Gemini API Quota Exceeded (429).\n" +
                            "你超出的目前的 API 使用額度，或請求過於頻繁。\n" +
                            $"已達到最大重試次數 ({maxRetries} 次)。\n" +
                            "請檢查您的 Google AI Studio 方案或稍後再試。\n" +
                            "詳細錯誤資訊可至 logs 日誌中查看。"
                        );
                    }
                }
                
                // 處理模型不支援 Function Calling 的情況 (400 Bad Request)
                if ((int)response.StatusCode == 400 && 
                    (responseJson.Contains("Model does not support function calling") || 
                     responseJson.Contains("models not supported") ||
                     responseJson.Contains("not support tools") ||
                     responseJson.Contains("Function calling is not enabled")))
                {
                    throw new Exception(
                        $"目前使用的模型 ({_model}) 不支援 Function Calling 功能 (Tool Use)。\n" +
                        "請嘗試切換至其他模型 (例如 gemini-2.0-flash 或 gemini-2.5-flash)。"
                    );
                }

                throw new Exception($"Gemini API Error: {response.StatusCode}\n{responseJson}");
            }

            return responseJson;
        }

        // 輔助方法：將單純字串轉為 API 所需的 contents 格式
        public object CreateSimpleContents(string prompt)
        {
            return new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            };
        }

        public System.Collections.ArrayList ExtractResponseParts(Dictionary<string, object> data, out Dictionary<string, object> modelContent)
        {
            modelContent = null;
            var candidates = data["candidates"] as System.Collections.ArrayList;
            if (candidates == null || candidates.Count == 0) return null;

            modelContent = (candidates[0] as Dictionary<string, object>)["content"] as Dictionary<string, object>;
            return modelContent["parts"] as System.Collections.ArrayList;
        }

        public string ExtractTextFromResponseData(Dictionary<string, object> data)
        {
            var parts = ExtractResponseParts(data, out _);
            if (parts != null && parts.Count > 0)
            {
                var dictPart = parts[0] as Dictionary<string, object>;
                return dictPart?["text"]?.ToString();
            }
            return null;
        }

        /// <summary>
        /// 輔助方法：定義多個工具
        /// </summary>
        public object[] DefineTools(params object[] functionDeclarations)
        {
            return new[]
            {
                new
                {
                    function_declarations = functionDeclarations
                }
            };
        }

        /// <summary>
        /// 建立單個 Function Declaration 物件
        /// </summary>
        public object CreateFunctionDeclaration(string name, string description, object parameters)
        {
            return new
            {
                name = name,
                description = description,
                parameters = parameters
            };
        }

        /// <summary>
        /// 獲取可用模型列表
        /// </summary>
        public async Task<string> ListModelsAsync()
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }
    }
}
