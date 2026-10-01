using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    // Runtime sprite sets (spec 2026-10-01-runtime-sprite-sets-design): sprite sets registered from code —
    // a downloaded pack, or a loader that fetches each key on demand — referenced exactly like a SpriteSet
    // asset (`set:key`). They live apart from UI.SpriteResolver, so reinstalling it never drops them.
    public static partial class UI
    {
        /// <summary>
        /// Registers a sprite set whose sprites are all known now (a downloaded and decoded pack):
        /// <c>"setName:key"</c> then resolves like a SpriteSet asset, in every sprite attribute.
        /// The entries and the options are copied; the sprites stay the caller's to destroy, after
        /// <see cref="UnregisterRuntimeSpriteSet"/>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="setName"/> is not <c>[A-Za-z0-9_-]+</c>, or a key is empty.</exception>
        /// <exception cref="InvalidOperationException">A runtime or SpriteSet-asset set with this name exists.</exception>
        public static void RegisterRuntimeSpriteSet(string setName,
            IReadOnlyDictionary<string, RuntimeSprite> entries, RuntimeSpriteSetOptions options = null)
        {
            RuntimeSpriteSets.ValidateName(setName);
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var snapshot = new Dictionary<string, RuntimeSprite>(StringComparer.Ordinal);
            foreach (var kv in entries)
            {
                if (string.IsNullOrEmpty(kv.Key))
                    throw new ArgumentException($"runtime SpriteSet '{setName}': entry with an empty key", nameof(entries));
                if (kv.Value.Sprite == null) continue;   // same as BuildLookup: a null entry is a missing key
                snapshot[kv.Key] = kv.Value;
            }
            RuntimeSpriteSets.Register(new RuntimeSpriteSets.Registration(setName, snapshot, null, options));
        }

        /// <summary>
        /// Registers a sprite set that loads each key on demand: <paramref name="load"/> is called the first
        /// time a key is shown, at most once per key while the set stays registered. Only
        /// <c>&lt;Icon name&gt;</c> and <c>&lt;Image sprite&gt;</c> can show such a set — they refresh
        /// themselves when the sprite arrives; any other sprite attribute logs an error.
        /// </summary>
        /// <param name="load">Key → sprite. Must complete on the main thread and return a fresh Awaitable per
        /// call; return <c>default</c> for a key it cannot serve (404, offline).</param>
        public static void RegisterRuntimeSpriteSet(string setName,
            Func<string, Awaitable<RuntimeSprite>> load, RuntimeSpriteSetOptions options = null)
        {
            RuntimeSpriteSets.ValidateName(setName);
            if (load == null) throw new ArgumentNullException(nameof(load));
            RuntimeSpriteSets.Register(new RuntimeSpriteSets.Registration(setName, null, load, options));
        }

        /// <summary>
        /// Removes a runtime sprite set. Icons and Images showing it are cleared, and refresh by themselves if a
        /// set of the same name is registered again. False when no such set is registered.
        /// </summary>
        public static bool UnregisterRuntimeSpriteSet(string setName) => RuntimeSpriteSets.Unregister(setName);

        /// <summary>
        /// Clears every runtime registration (sprite sets, .po catalogs). No version guard: the dev host has no
        /// Enter/Exit Play Mode hooks, and with Domain Reload off a second Play session would otherwise re-register
        /// into a table still holding the previous session's destroyed sprites (spec §6.7).
        /// </summary>
        internal static void ClearRuntimeRegistrations()
        {
            RuntimeSpriteSets.Clear();
        }

        // Before any user RuntimeInitializeOnLoadMethod of a later phase; games must not register in this phase.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearRuntimeRegistrationsOnPlayStart() => ClearRuntimeRegistrations();
    }
}
