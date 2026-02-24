using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Antigravity02.Tools
{
    /// <summary>
    /// 底層 HTTP 工具類別，負責處理實際的網路通訊
    /// </summary>
    public class HttpTools
    {
        private static readonly HttpClient _httpClient = new HttpClient();


        public async Task<string> GetAsync(string url, string headersJson = null)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeaders(request, headersJson);
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return $"Status: {response.StatusCode}\nContent: {content}";
                    }
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"HttpTools(Get) Error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> PostAsync(string url, string body, string contentType = "application/json", string headersJson = null)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    AddHeaders(request, headersJson);
                    request.Content = new StringContent(body, Encoding.UTF8, contentType);

                    using (var response = await _httpClient.SendAsync(request))
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return $"Status: {response.StatusCode}\nContent: {content}";
                    }
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"HttpTools(Post) Error: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private void AddHeaders(HttpRequestMessage request, string headersJson)
        {
            if (string.IsNullOrEmpty(headersJson)) return;

            try
            {
                var headerDict = JsonTools.Deserialize<Dictionary<string, string>>(headersJson);
                if (headerDict != null)
                {
                    foreach (var kvp in headerDict)
                    {
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                UsageLogger.LogError($"HttpTools Header Parse Error: {ex.Message}");
            }
        }
    }
}
