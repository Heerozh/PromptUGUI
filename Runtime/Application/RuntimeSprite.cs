using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// One entry of a runtime sprite set: the sprite plus the <c>tiled</c> render hint a SpriteSet entry
    /// carries (spec 2026-10-01-runtime-sprite-sets-design §5.1). <c>default</c> — a null
    /// <see cref="Sprite"/> — means "missing": a loader returns it for a key it cannot serve.
    /// </summary>
    public readonly struct RuntimeSprite
    {
        public RuntimeSprite(Sprite sprite, bool tiled = false)
        {
            Sprite = sprite;
            Tiled = tiled;
        }

        public Sprite Sprite { get; }

        /// <summary>Same as a SpriteSet entry's <c>tiled</c>: the sprite renders as Tiled where the
        /// control derives its type from the sprite.</summary>
        public bool Tiled { get; }

        public static implicit operator RuntimeSprite(Sprite sprite) => new RuntimeSprite(sprite);
    }

    /// <summary>
    /// Placeholders of a runtime sprite set. Both belong to the caller, like the set's sprites, and are
    /// read once at registration. Null draws nothing (not uGUI's solid block).
    /// </summary>
    public sealed class RuntimeSpriteSetOptions
    {
        /// <summary>Shown while an on-demand key is loading. Ignored by sets registered with entries.</summary>
        public Sprite Loading { get; set; }

        /// <summary>Shown for a key the set does not have (or whose loader failed).</summary>
        public Sprite Missing { get; set; }
    }
}
