using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Internal
{
    /// <summary>sprite → 渲染提示(目前仅 tiled)的运行时登记表。按 <see cref="EntityId"/>
    /// (值类型 id)存，不持有 Sprite 引用 —— 登记表不会把资产钉在内存里，
    /// Resources.UnloadUnusedAssets 仍可回收；重导入后旧 id 失配无害。
    /// 填充口：UI.ResolveSprite 的 Resources 分支、SpriteResolverHelpers.BuildLookup、
    /// ProceduralBuilders.GetDefaultSprite。</summary>
    internal static class SpriteRenderHints
    {
        // 存 EntityId 而非 Sprite 引用：HashSet<Sprite> 会强引用、把 tiled 资产永久钉住
        // (Clear() 仅测试调用)。EntityId 是 IEquatable 值类型，无 Unity Object 的 fake-null /
        // GetHashCode 覆盖问题。EntityId/GetEntityId() 是 6000.1 新增，6000.0 回退 int 实例 id。
#if UNITY_6000_1_OR_NEWER
        private static readonly HashSet<EntityId> _tiledIds = new();

        private static EntityId IdOf(Sprite s) => s.GetEntityId();
#else
        private static readonly HashSet<int> _tiledIds = new();

        private static int IdOf(Sprite s) => s.GetInstanceID();
#endif

        public static void Register(Sprite s)
        {
            if (s != null) _tiledIds.Add(IdOf(s));
        }

        public static void Register(PxlSpriteHints hints)
        {
            if (hints == null) return;
            for (var i = 0; i < hints.TiledSprites.Count; i++)
                Register(hints.TiledSprites[i]);
        }

        public static bool IsTiled(Sprite s) =>
            s != null && _tiledIds.Contains(IdOf(s));

        public static void Clear() => _tiledIds.Clear();
    }
}
