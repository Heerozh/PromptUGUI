using System;
using System.Collections.Generic;

namespace PromptUGUI.Application
{
    /// <summary>路由查询参数的只读包装。从 URL 的 ?k=v&... 或 Open 传入的字典构造。</summary>
    public sealed class RouteQuery
    {
        public static readonly RouteQuery Empty = new(new Dictionary<string, string>(0));

        private readonly IReadOnlyDictionary<string, string> _q;

        public RouteQuery(IReadOnlyDictionary<string, string> query)
            => _q = query ?? new Dictionary<string, string>(0);

        public bool Has(string key) => _q.ContainsKey(key);

        public string Get(string key, string fallback = null)
            => _q.TryGetValue(key, out var v) ? v : fallback;

        public int GetInt(string key, int fallback = 0)
            => _q.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

        public string this[string key] => Get(key);

        public IReadOnlyDictionary<string, string> Raw => _q;

        /// <summary>解析 "k=v&k2=v2"(无前导 '?')。空串 → Empty。k/v 各做 URL decode。</summary>
        internal static RouteQuery ParseQueryString(string qs)
        {
            if (string.IsNullOrEmpty(qs)) return Empty;
            var d = new Dictionary<string, string>();
            foreach (var pair in qs.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                var k = eq >= 0 ? pair.Substring(0, eq) : pair;
                var v = eq >= 0 ? pair.Substring(eq + 1) : "";
                if (k.Length == 0) continue;
                d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
            return new RouteQuery(d);
        }
    }
}
