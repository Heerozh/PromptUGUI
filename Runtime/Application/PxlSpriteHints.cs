using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>.pxl 导入产物的渲染提示子资产：`tiled: true` 的 section 对应的
    /// Sprite 引用清单。运行时由 SpriteRenderHints 各填充口登记；internal——
    /// 作者不直接触碰(transparent default，C# SKILL 免更)。</summary>
    internal sealed class PxlSpriteHints : ScriptableObject
    {
        [SerializeField] private List<Sprite> tiledSprites = new();
        public IReadOnlyList<Sprite> TiledSprites => tiledSprites;
#if UNITY_EDITOR
        internal void SetTiledSpritesInternal(List<Sprite> sprites) => tiledSprites = sprites;
#endif
    }
}
