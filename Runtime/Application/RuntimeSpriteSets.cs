using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Something that shows a key of a runtime sprite set and wants to hear when it settles.
    /// Implemented by <c>Controls.Internal.AsyncSpriteSlot</c>.
    /// </summary>
    internal interface IRuntimeSpriteListener
    {
        /// <summary>The control the listener draws into: logging context, and liveness
        /// (<c>Owner.GameObject == null</c> = dead).</summary>
        public Controls.Control Owner { get; }

        public void OnKeySettled(RuntimeSpriteSets.Registration reg, string key, RuntimeSpriteSets.KeyEntry entry);
    }

    /// <summary>
    /// The registry behind <c>UI.RegisterRuntimeSpriteSet</c> (spec 2026-10-01-runtime-sprite-sets-design).
    /// Holds data only: which sets exist, the state of every requested key of an on-demand set, who is
    /// waiting for what. Sets live apart from <c>UI.SpriteResolver</c>, so reinstalling that resolver (or a
    /// sprite hot reload) never touches them.
    /// </summary>
    internal static class RuntimeSpriteSets
    {
        internal enum KeyState { Loading, Ready, Missing }

        internal sealed class KeyEntry
        {
            public KeyState State;
            public RuntimeSprite Result;
            public Exception Error;
            internal List<IRuntimeSpriteListener> Waiters;
        }

        internal sealed class Registration
        {
            public readonly string Name;
            public readonly Dictionary<string, RuntimeSprite> Entries;      // eager: every key known up front
            public readonly Func<string, Awaitable<RuntimeSprite>> Load;    // on demand: one call per key
            public readonly Sprite Loading;
            public readonly Sprite Missing;
            public readonly Dictionary<string, KeyEntry> Keys = new(StringComparer.Ordinal);

            // Three dedupe tables (spec §6.5); they die with the registration.
            public readonly HashSet<string> WarnedMissing = new(StringComparer.Ordinal);
            public readonly HashSet<string> ErroredLoader = new(StringComparer.Ordinal);
            public readonly HashSet<string> ErroredMisuse = new(StringComparer.Ordinal);

            /// <summary>False once unregistered or cleared: results that arrive later are dropped.</summary>
            public bool Alive = true;

            public bool OnDemand => Load != null;

            public Registration(string name, Dictionary<string, RuntimeSprite> entries,
                                Func<string, Awaitable<RuntimeSprite>> load, RuntimeSpriteSetOptions options)
            {
                Name = name;
                Entries = entries;
                Load = load;
                Loading = options?.Loading;
                Missing = options?.Missing;
            }
        }

        private static readonly Dictionary<string, Registration> s_sets = new(StringComparer.Ordinal);

        internal static bool TryGet(string setName, out Registration reg) => s_sets.TryGetValue(setName, out reg);

        internal static bool IsRegistered(string setName) => s_sets.ContainsKey(setName);

        internal static int Count => s_sets.Count;

        /// <summary>The registered sets for a diagnostic: <c>pack (runtime), ugc (runtime, on demand)</c>.</summary>
        internal static IEnumerable<string> DescribeNames()
        {
            foreach (var reg in s_sets.Values)
                yield return reg.OnDemand ? $"{reg.Name} (runtime, on demand)" : $"{reg.Name} (runtime)";
        }

        /// <summary>Same rule as the set half of <c>&lt;Icon name&gt;</c>'s XSD pattern: <c>[A-Za-z0-9_-]+</c>.</summary>
        internal static void ValidateName(string setName)
        {
            if (setName == null) throw new ArgumentNullException(nameof(setName));
            var ok = setName.Length > 0;
            foreach (var c in setName)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    continue;
                ok = false;
                break;
            }
            if (!ok)
                throw new ArgumentException(
                    $"runtime SpriteSet name '{setName}' must match [A-Za-z0-9_-]+ (the set half of 'set:key')",
                    nameof(setName));
        }

        internal static void Register(Registration reg)
        {
            if (s_sets.ContainsKey(reg.Name))
                throw new InvalidOperationException(
                    $"Duplicate SpriteSet name '{reg.Name}' (already registered at runtime via UI.RegisterRuntimeSpriteSet)");
            if (UI.LoadedSpriteSetNames.Contains(reg.Name))
                throw new InvalidOperationException(
                    $"Duplicate SpriteSet name '{reg.Name}' (a SpriteSet asset with this name is loaded through " +
                    "SpriteResolverHelpers.UseSpriteSetResolver / UseAddressableSpriteSetResolver)");

            if (reg.Entries != null)
                foreach (var kv in reg.Entries)
                    if (kv.Value.Tiled) Internal.SpriteRenderHints.Register(kv.Value.Sprite);
            s_sets.Add(reg.Name, reg);

            // An eager set can sit in any sprite attribute: replay the open Screens once so references declared
            // in XML before the pack arrived resolve now. Rare by nature (a pack download), hence a full replay.
            // An on-demand set never needs it — only Icon / Image show one, and they refresh themselves.
            if (!reg.OnDemand) UI.NotifyVariantChangedForReSolve();
        }

        internal static bool Unregister(string setName)
        {
            if (string.IsNullOrEmpty(setName) || !s_sets.TryGetValue(setName, out var reg)) return false;
            s_sets.Remove(setName);
            reg.Alive = false;
            if (!reg.OnDemand) UI.NotifyVariantChangedForReSolve();
            return true;
        }

        /// <summary>
        /// The synchronous entry (<c>UI.ResolveSprite</c>). True when <paramref name="value"/> names a runtime
        /// set — the caller then uses <paramref name="sprite"/> as is; false hands the value to the static path.
        /// </summary>
        internal static bool TryResolveSync(string value, out Sprite sprite)
        {
            sprite = null;
            if (s_sets.Count == 0) return false;
            var colon = value.IndexOf(':');
            if (colon <= 0 || !s_sets.TryGetValue(value.Substring(0, colon), out var reg)) return false;

            var key = value.Substring(colon + 1);
            if (reg.OnDemand)
            {
                // Deterministic, cached or not (spec §6.3): this attribute can never wait for a late sprite.
                if (reg.ErroredMisuse.Add(key))
                    UILog.Error(
                        $"sprite '{value}': runtime SpriteSet '{reg.Name}' loads on demand, and only <Icon name> and " +
                        "<Image sprite> can wait for a sprite. Register the set with entries (every sprite up front) " +
                        "to use it in this attribute.");
                return true;
            }
            sprite = ResolveEager(reg, key, value, "sprite", null);
            return true;
        }

        /// <summary>A key of an eager set: the sprite, or the set's Missing placeholder (warned once).</summary>
        internal static Sprite ResolveEager(Registration reg, string key, string value, string prefix,
                                            Controls.Control context)
        {
            if (reg.Entries.TryGetValue(key, out var hit)) return hit.Sprite;
            WarnMissingOnce(reg, key, value, prefix, context);
            return reg.Missing;
        }

        internal static void WarnMissingOnce(Registration reg, string key, string value, string prefix,
                                             Controls.Control context)
        {
            // A key whose loader threw already has its error; a second line for the same failure is noise.
            if (reg.ErroredLoader.Contains(key) || !reg.WarnedMissing.Add(key)) return;
            var message = $"{prefix} '{value}': runtime SpriteSet '{reg.Name}' has no '{key}'"
                          + (reg.Missing != null ? "; showing the set's Missing sprite." : ".");
            if (context != null) UILog.Warn(context, message);
            else UILog.Warn(message);
        }

        /// <summary>
        /// Requests a key of an on-demand set: the loader is called at most once per key while the set stays
        /// registered. A loader that completes synchronously (a memory-cache hit) leaves the entry settled on
        /// return. <paramref name="requester"/> is the logging context for a synchronous failure.
        /// </summary>
        internal static KeyEntry Request(Registration reg, string key, Controls.Control requester = null)
        {
            if (reg.Keys.TryGetValue(key, out var entry)) return entry;

            // In the table BEFORE the loader runs: a loader that re-requests its own key synchronously
            // finds this entry and queues, instead of being called a second time (spec §5.3).
            entry = new KeyEntry { State = KeyState.Loading };
            reg.Keys[key] = entry;
            if (key.Length == 0)
            {
                Settle(reg, key, entry, default, null, requester);
                return entry;
            }

            Awaitable<RuntimeSprite> pending;
            try { pending = reg.Load(key); }
            catch (Exception e)
            {
                Settle(reg, key, entry, default, e, requester);
                return entry;
            }
            if (pending == null)
            {
                Settle(reg, key, entry, default, null, requester);
                return entry;
            }

            // One awaiter, its result taken exactly once (Awaitable is pooled). Only GetAwaiter / IsCompleted /
            // GetResult / OnCompleted: the UniTask-backed shim for Unity < 6 has the same surface.
            var awaiter = pending.GetAwaiter();
            if (awaiter.IsCompleted) SettleFrom(reg, key, entry, requester, () => awaiter.GetResult());
            else awaiter.OnCompleted(() => SettleFrom(reg, key, entry, requester, () => awaiter.GetResult()));
            return entry;
        }

        private static void SettleFrom(Registration reg, string key, KeyEntry entry, Controls.Control requester,
                                       Func<RuntimeSprite> getResult)
        {
            RuntimeSprite result;
            Exception error = null;
            try { result = getResult(); }
            catch (Exception e)
            {
                result = default;
                error = e;
            }
            Settle(reg, key, entry, result, error, requester);
        }

        private static void Settle(Registration reg, string key, KeyEntry entry, RuntimeSprite result,
                                   Exception error, Controls.Control requester)
        {
            if (entry.State != KeyState.Loading) return;
            // Unregistered (or cleared) meanwhile: nothing references the result; the loader's cache owns it.
            if (!reg.Alive) return;

            if (error == null && result.Sprite != null)
            {
                if (result.Tiled) Internal.SpriteRenderHints.Register(result.Sprite);
                entry.Result = result;
                entry.State = KeyState.Ready;
            }
            else
            {
                entry.Error = error;
                entry.State = KeyState.Missing;
                if (error != null) ReportLoaderErrorOnce(reg, key, error, LiveContext(requester, entry));
            }

            var waiters = entry.Waiters;
            entry.Waiters = null;
            if (waiters == null) return;
            foreach (var w in waiters)
            {
                // One misbehaving listener must not starve the rest.
                try { w.OnKeySettled(reg, key, entry); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void ReportLoaderErrorOnce(Registration reg, string key, Exception error,
                                                  Controls.Control context)
        {
            if (!reg.ErroredLoader.Add(key)) return;
            var message = $"sprite '{reg.Name}:{key}': the loader of runtime SpriteSet '{reg.Name}' failed: " +
                          $"{error.GetType().Name}: {error.Message}. Showing the set's Missing sprite. " +
                          "Return default from the loader for an expected failure (404, offline) instead of throwing.";
            if (context != null) UILog.Error(context, message);
            else UILog.Error(message);
        }

        // A failure that settles asynchronously has no node in hand: log against the requester, or failing that
        // the first listener still alive.
        private static Controls.Control LiveContext(Controls.Control requester, KeyEntry entry)
        {
            if (IsAlive(requester)) return requester;
            if (entry.Waiters != null)
                foreach (var w in entry.Waiters)
                    if (IsAlive(w.Owner)) return w.Owner;
            return null;
        }

        internal static bool IsAlive(Controls.Control control) => control != null && control.GameObject != null;

        /// <summary>Drops every set. Registrations are marked dead first, so results arriving later are dropped.</summary>
        internal static void Clear()
        {
            foreach (var reg in s_sets.Values) reg.Alive = false;
            s_sets.Clear();
        }
    }
}
