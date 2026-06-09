using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>Markdown rendering facade. The Markdig backend (gated asmdef) injects
        /// <see cref="Renderer"/> at domain load and re-injects on every <see cref="UI.OnReset"/>.</summary>
        public static class Markdown
        {
            public static IMarkdownRenderer Renderer { get; set; }
            public static MarkdownStyle DefaultStyle { get; set; } = MarkdownStyle.CreateDefault();
            public static Func<string, Awaitable<Texture2D>> ImageResolver { get; set; }

            internal static void ResetForTestsInternal()
            {
                Renderer = null;            // re-injected by the gated asmdef via UI.OnReset (end of reset)
                DefaultStyle = MarkdownStyle.CreateDefault();
                ImageResolver = null;
            }
        }
    }
}
