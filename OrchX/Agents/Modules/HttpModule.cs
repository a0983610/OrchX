using System.Collections.Generic;
using System.Threading.Tasks;
using OrchX.AIClient;
using OrchX.Tools;
using OrchX.UI;

namespace OrchX.Agents
{
    /// <summary>
    /// 提供 HTTP 請求功能 (GET / POST) 的模組
    /// </summary>
    public class HttpModule : BaseAgentModule
    {
        private readonly HttpTools _httpTools = new HttpTools();

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            // 【網路：HTTP GET】
            // 發送 GET 請求獲取遠端資料。支援選填的 JSON Header。
            yield return client.CreateFunctionDeclaration(
                "http_get",
                "Send an HTTP GET request to fetch data from a URL.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "The target URL" },
                        headers = new { type = "string", description = "Optional. JSON string representing request headers (e.g., '{\"Authorization\": \"Bearer ...\"}')" }
                    },
                    required = new[] { "url" }
                }
            );

            // 【網路：HTTP POST】
            // 發送 POST 請求傳送內容。預設 Content-Type 為 application/json。
            yield return client.CreateFunctionDeclaration(
                "http_post",
                "Send an HTTP POST request to submit data to a URL.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "The target URL" },
                        body = new { type = "string", description = "The content to send (typically a JSON string)" },
                        contentType = new { type = "string", description = "Optional. Default: 'application/json'" },
                        headers = new { type = "string", description = "Optional. JSON string representing request headers" }
                    },
                    required = new[] { "url", "body" }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui, System.Threading.CancellationToken cancellationToken = default)
        {
            switch (funcName)
            {
                case "http_get":
                    return await HandleHttpGetAsync(funcName, args);
                case "http_post":
                    return await HandleHttpPostAsync(funcName, args);
                default:
                    return null;
            }
        }

        private async Task<string> HandleHttpGetAsync(string funcName, Dictionary<string, object> args)
        {
            string errGet = CheckRequiredArgs(funcName, args);
            if (errGet != null) return errGet;

            string getUrl = args["url"].ToString();
            string getHeaders = args.ContainsKey("headers") ? args["headers"].ToString() : null;
            return await _httpTools.GetAsync(getUrl, getHeaders);
        }

        private async Task<string> HandleHttpPostAsync(string funcName, Dictionary<string, object> args)
        {
            string errPost = CheckRequiredArgs(funcName, args);
            if (errPost != null) return errPost;

            string postUrl = args["url"].ToString();
            string body = args["body"].ToString();
            string contentType = args.ContainsKey("contentType") ? args["contentType"].ToString() : "application/json";
            string postHeaders = args.ContainsKey("headers") ? args["headers"].ToString() : null;
            return await _httpTools.PostAsync(postUrl, body, contentType, postHeaders);
        }
    }
}
