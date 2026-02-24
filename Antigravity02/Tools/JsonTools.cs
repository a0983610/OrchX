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
            
            // 如果要解析為 Dictionary<string, object>，手動將 JToken 轉為舊式的 Dictionary 與 ArrayList 以相容原本代碼
            if (typeof(T) == typeof(System.Collections.Generic.Dictionary<string, object>))
            {
                var jObject = Newtonsoft.Json.Linq.JObject.Parse(json);
                return (T)(object)ToPrimitive(jObject);
            }
            
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static object ToPrimitive(Newtonsoft.Json.Linq.JToken token)
        {
            if (token is Newtonsoft.Json.Linq.JObject jobj)
            {
                var dict = new System.Collections.Generic.Dictionary<string, object>();
                foreach (var prop in jobj.Properties())
                {
                    dict[prop.Name] = ToPrimitive(prop.Value);
                }
                return dict;
            }
            if (token is Newtonsoft.Json.Linq.JArray jarr)
            {
                var list = new System.Collections.ArrayList();
                foreach (var item in jarr)
                {
                    list.Add(ToPrimitive(item));
                }
                return list;
            }
            if (token is Newtonsoft.Json.Linq.JValue jval)
            {
                return jval.Value;
            }
            return null;
        }
    }
}
