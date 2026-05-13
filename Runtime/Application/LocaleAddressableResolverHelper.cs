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
            /// 把 PoResolver 设为按 label=<c>Locale:&lt;locale&gt;</c> 加载所有 .po TextAsset 的
            /// Addressables 实现。<c>Set("zh-Hans")</c> →
            /// <c>Addressables.LoadAssetsAsync&lt;TextAsset&gt;("Locale:zh-Hans", null)</c>。
            /// 仅在装了 com.unity.addressables 时存在（PROMPTUGUI_HAS_ADDRESSABLES 编译定义）。
            ///
            /// AA group 里的每个 .po 必须打上 <c>Locale:&lt;locale&gt;</c> label。
            /// 用 Editor 菜单 <c>Tools → PromptUGUI → I18n → Setup Addressables for
            /// Locale PO Files</c> 一键把项目里所有 .po 加进 AA 默认组并打 label。
            ///
            /// 注意 fire-and-forget 模型下 Set 返回后 UI 还看到 msgid，要等下载完才切译文；
            /// 想避免闪烁用 await Locale.SetAsync(...)。
            /// </summary>
            public static void UseAddressableResolver()
            {
                PoResolver = LoadPoFromAddressablesAsync;
            }

            /// <summary>
            /// Builds the Addressables label string the resolver expects for a given locale.
            /// Single source of truth shared with the Editor-side auto-tagger; if you change
            /// this format, change <c>AddressablePoLabelSyncer.LabelPrefix</c> too.
            /// </summary>
            internal static string BuildLocaleLabel(string locale) => "Locale:" + locale;

            private static async Awaitable<IEnumerable<PoEntry>> LoadPoFromAddressablesAsync(
                string locale)
            {
                var handle = Addressables.LoadAssetsAsync<TextAsset>(BuildLocaleLabel(locale), null);
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
