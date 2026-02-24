using System;
using Newtonsoft.Json;

namespace Antigravity02.Tools
{
    /// <summary>
    /// 集中處理 JSON 序列化與反序列化的工具類別。
    /// 透過這個 Wrapper 可以方便未來抽換底層的 JSON 函式庫 (如 System.Text.Json)。
    /// </summary>
    public static class JsonTools
    {
        private static readonly JsonSerializerSettings _defaultSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// 將物件序列化為 JSON 字串
        /// </summary>
        public static string Serialize(object obj, bool ignoreNulls = true)
        {
            if (obj == null) return null;
            return ignoreNulls 
                ? JsonConvert.SerializeObject(obj, _defaultSettings)
                : JsonConvert.SerializeObject(obj);
        }

        /// <summary>
        /// 將 JSON 字串反序列化為指定型別的物件
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
