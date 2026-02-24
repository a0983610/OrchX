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

        public GeminiClient(string apiKey, string model = "gemini-2.5-flash")
        {
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> GenerateContentAsync(GenerateContentRequest request)
        {
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
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // 紀錄詳細錯誤資訊
                UsageLogger.LogApiError($"API Error: {response.StatusCode}", json, responseJson);

                if ((int)response.StatusCode == 429)
                {
                    throw new Exception(
                        "Gemini API Quota Exceeded (429).\n" +
                        "你不小心超出了目前的 API 使用額度。\n" +
                        "請檢查您的 Google AI Studio 方案或稍後再試。\n" +
                        "詳細錯誤資訊可至 logs 日誌中查看。"
                    );
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
