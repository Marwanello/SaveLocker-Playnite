using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Deliberately untyped: <see cref="JavaScriptSerializer.DeserializeObject"/> hands back plain
    /// Dictionary/List/primitive trees with no attribute-based name mapping to get wrong, so every
    /// field this plugin reads from the agent's local API is pulled out by its exact camelCase JSON
    /// key (matching System.Text.Json's default naming on the server side) rather than trusted to an
    /// automatic reflection convention. See docs/Gotchas.md for why this, not Newtonsoft/System.Text.Json.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string json)
        {
            return string.IsNullOrEmpty(json) ? null : new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
        }

        public static string Write(object value)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value);
        }

        public static IDictionary<string, object> AsObject(object node)
        {
            return node as IDictionary<string, object>;
        }

        public static IList<IDictionary<string, object>> AsObjectArray(object node)
        {
            var list = node as IList<object>;
            return list?.Select(AsObject).Where(o => o != null).ToList()
                   ?? new List<IDictionary<string, object>>();
        }

        public static string GetString(IDictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        public static bool? GetNullableBool(IDictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v != null ? (bool?)Convert.ToBoolean(v) : null;
        }

        public static bool GetBool(IDictionary<string, object> obj, string key, bool fallback = false)
        {
            return GetNullableBool(obj, key) ?? fallback;
        }

        public static Guid? GetNullableGuid(IDictionary<string, object> obj, string key)
        {
            var s = GetString(obj, key);
            return string.IsNullOrEmpty(s) ? (Guid?)null : Guid.Parse(s);
        }

        public static Guid GetGuid(IDictionary<string, object> obj, string key)
        {
            return GetNullableGuid(obj, key) ?? Guid.Empty;
        }

        public static uint? GetNullableUInt(IDictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v != null ? (uint?)Convert.ToUInt32(v, CultureInfo.InvariantCulture) : null;
        }

        public static int GetInt(IDictionary<string, object> obj, string key, int fallback = 0)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback;
        }

        public static long GetLong(IDictionary<string, object> obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
        }

        public static DateTime? GetNullableDateTime(IDictionary<string, object> obj, string key)
        {
            var s = GetString(obj, key);
            return string.IsNullOrEmpty(s)
                ? (DateTime?)null
                : DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        public static List<string> GetStringArray(IDictionary<string, object> obj, string key)
        {
            var arr = obj != null && obj.TryGetValue(key, out var v) ? v as IList<object> : null;
            return arr?.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList() ?? new List<string>();
        }
    }
}
