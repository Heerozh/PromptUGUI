using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PromptUGUI.Application.Internal
{
    /// <summary>sprite → 渲染提示(目前仅 tiled)的运行时登记表。按引用存，
    /// 重导入后旧引用失效无害。填充口：UI.ResolveSprite 的 Resources
    /// 分支、SpriteResolverHelpers.BuildLookup、ProceduralBuilders.GetDefaultSprite。</summary>
    internal static class SpriteRenderHints
    {
        // Unity Object overrides == / GetHashCode; use CLR identity to avoid
        // UnityEngine fake-null surprises and the obsolete GetInstanceID().
        private sealed class IdentityComparer : IEqualityComparer<Sprite>
        {
            internal static readonly IdentityComparer Instance = new IdentityComparer();
            public bool Equals(Sprite x, Sprite y) => ReferenceEquals(x, y);
            public int GetHashCode(Sprite s) => RuntimeHelpers.GetHashCode(s);
        }

        private static readonly HashSet<Sprite> _tiledSprites =
            new HashSet<Sprite>(IdentityComparer.Instance);

        public static void Register(Sprite s)
        {
            if (s != null) _tiledSprites.Add(s);
        }

        public static void Register(PxlSpriteHints hints)
        {
            if (hints == null) return;
            for (var i = 0; i < hints.TiledSprites.Count; i++)
                Register(hints.TiledSprites[i]);
        }

        public static bool IsTiled(Sprite s) =>
            s != null && _tiledSprites.Contains(s);

        public static void Clear() => _tiledSprites.Clear();
    }
}
