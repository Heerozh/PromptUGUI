#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using PromptUGUI.I18n;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static partial class Locale
        {
            /// <summary>
            /// 把 PoResolver 设为按 label=locale 加载所有 .po TextAsset 的 Addressables 实现。
            /// Set("zh-Hans") → Addressables.LoadAssetsAsync&lt;TextAsset&gt;("zh-Hans", null)。
            /// 仅在装了 com.unity.addressables 时存在（PROMPTUGUI_HAS_ADDRESSABLES 编译定义）。
            ///
            /// 注意 fire-and-forget 模型下 Set 返回后 UI 还看到 msgid，要等下载完才切译文；
            /// 想避免闪烁用 await Locale.SetAsync(...)。
            /// </summary>
            public static void UseAddressableResolver()
            {
                PoResolver = LoadPoFromAddressablesAsync;
            }

            private static async Awaitable<IEnumerable<PoEntry>> LoadPoFromAddressablesAsync(
                string locale)
            {
                var handle = Addressables.LoadAssetsAsync<TextAsset>(locale, null);
                try
                {
                    var assets = await handle.Task;
                    var entries = new List<PoEntry>();
                    foreach (var ta in assets ?? Array.Empty<TextAsset>())
                    {
                        if (ta == null) continue;
                        try
                        {
                            foreach (var e in PoParser.Parse(ta.text)) entries.Add(e);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"[PromptUGUI] failed to parse .po asset '{ta.name}': {ex.Message}");
                        }
                    }
                    return entries;
                }
                finally
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                }
            }
        }
    }
}
#endif
