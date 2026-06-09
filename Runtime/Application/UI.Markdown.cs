using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

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

            private static readonly Dictionary<string, Texture2D> WebCache = new Dictionary<string, Texture2D>();

            public static void UseWebImageResolver()
            {
                ImageResolver = LoadWebTextureAsync;
            }

            private static async Awaitable<Texture2D> LoadWebTextureAsync(string url)
            {
                if (string.IsNullOrEmpty(url)) return null;
                if (WebCache.TryGetValue(url, out var cached) && cached != null) return cached;

                using var req = UnityWebRequestTexture.GetTexture(url);
                var op = req.SendWebRequest();
                var acs = new AwaitableCompletionSource<bool>();
                op.completed += _ => acs.SetResult(true);
                if (!op.isDone) await acs.Awaitable;

                if (req.result != UnityWebRequest.Result.Success) return null;
                var tex = DownloadHandlerTexture.GetContent(req);
                WebCache[url] = tex;
                return tex;
            }

            internal static void ResetForTestsInternal()
            {
                Renderer = null;            // re-injected by the gated asmdef via UI.OnReset (end of reset)
                DefaultStyle = MarkdownStyle.CreateDefault();
                ImageResolver = null;
                foreach (var t in WebCache.Values)
                    if (t != null)
                    {
                        if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(t);
                        else UnityEngine.Object.DestroyImmediate(t);
                    }
                WebCache.Clear();
            }
        }
    }
}
