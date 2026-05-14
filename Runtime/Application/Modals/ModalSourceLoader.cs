using System;
using UnityEngine;

namespace PromptUGUI.Application.Modals
{
    internal static class ModalSourceLoader
    {
        public const string BuiltinPrefix = "PromptUGUI/";

        public static Awaitable<string> LoadAsync(string src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            if (src.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            {
                // 内置 modal XML 体积小，用同步 Resources.Load 即可——也避开了
                // EditMode 测试里 `await ResourceRequest` 在无 PlayerLoop 时不推进的坑。
                var ta = Resources.Load<TextAsset>(src);
                if (ta == null)
                    throw new InvalidOperationException(
                        $"Builtin modal XML missing at Resources/{src}.ui.xml");
                return AwaitableHelpers.Completed(ta.text);
            }

            if (UI.SourceResolver == null)
                throw new InvalidOperationException(
                    $"UI.SourceResolver must be set to load non-builtin modal '{src}'");
            return UI.SourceResolver(src);
        }
    }
}
