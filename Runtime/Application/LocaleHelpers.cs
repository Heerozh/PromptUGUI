using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static class LocaleHelpers
    {
        /// <summary>
        /// RFC 4647 "lookup" 回退:在 <paramref name="configured"/> 里找
        /// <paramref name="requested"/>,找不到就逐级砍掉末尾的 <c>-子标签</c>
        /// 再找(<c>zh-Hant-TW → zh-Hant → zh</c>)。返回命中的**配置项原文**
        /// (大小写/拼写按 <paramref name="configured"/>,以便 .po 路径仍能解析),
        /// 一级都没命中返回 <c>null</c>。同长度优先精确匹配:只有当前候选在整个
        /// 列表里都没命中才会再截断,所以 <c>zh-Hans</c> 优先于 <c>zh</c>。
        /// 反向不成立:更泛的请求(<c>zh</c>)不会扩展去匹配更细的配置(<c>zh-Hans</c>)。
        /// </summary>
        public static string MatchWithFallback(string requested, IReadOnlyList<string> configured)
        {
            if (string.IsNullOrEmpty(requested) || configured == null) return null;
            var candidate = requested;
            while (true)
            {
                // Same-length exact match first, so zh-Hans beats a generic zh.
                for (var i = 0; i < configured.Count; i++)
                    if (configured[i] == candidate) return configured[i];
                var dash = candidate.LastIndexOf('-');
                if (dash < 0) return null;
                candidate = candidate.Substring(0, dash);
            }
        }

        public static string MapSystemLanguage(SystemLanguage lang) =>
            lang switch
            {
                SystemLanguage.ChineseSimplified => "zh-Hans",
                SystemLanguage.ChineseTraditional => "zh-Hant",
                SystemLanguage.Chinese => "zh-Hans",
                SystemLanguage.English => "en",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.French => "fr",
                SystemLanguage.German => "de",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Russian => "ru",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Italian => "it",
                _ => null,
            };
    }
}
