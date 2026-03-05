using System.Collections.Generic;
using System.Threading.Tasks;
using Antigravity02.AIClient;
using Antigravity02.Tools;
using Antigravity02.UI;

namespace Antigravity02.Agents
{
    /// <summary>
    /// 提供 HTTP 請求功能 (GET / POST) 的模組
    /// </summary>
    public class HttpModule : BaseAgentModule
    {
        private readonly HttpTools _httpTools = new HttpTools();

        protected override IEnumerable<object> BuildToolDeclarations(IAIClient client)
        {
            yield return client.CreateFunctionDeclaration(
                "http_get",
                "發送 HTTP GET 請求獲取資料。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "目標 URL" },
                        headers = new { type = "string", description = "選填，JSON 格式的 Header (例如: {\"Authorization\": \"Bearer ...\"})" }
                    },
                    required = new[] { "url" }
                }
            );

            yield return client.CreateFunctionDeclaration(
                "http_post",
                "發送 HTTP POST 請求傳送資料。",
                new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "目標 URL" },
                        body = new { type = "string", description = "POST 內容 (通常是 JSON 字串)" },
                        contentType = new { type = "string", description = "選填，預設為 application/json" },
                        headers = new { type = "string", description = "選填，JSON 格式的 Header" }
                    },
                    required = new[] { "url", "body" }
                }
            );
        }

        public override async Task<string> TryHandleToolCallAsync(string funcName, Dictionary<string, object> args, IAgentUI ui)
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
