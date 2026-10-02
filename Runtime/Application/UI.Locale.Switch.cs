using System;
using System.Collections.Generic;
using PromptUGUI.I18n;
using UnityEngine;

namespace PromptUGUI.Application
{
    // A locale switch commits once the new language has arrived (spec 2026-10-02-locale-commit-on-load-design): a
    // read-only, asynchronous gather, then one synchronous commit — table, Current, both locale variants in a single
    // Changed, the old table out, Locale.Changed. Until then the old locale stays in effect, so no re-render in
    // between ever shows a msgid.
    public static partial class UI
    {
        public static partial class Locale
        {
            /// <summary>
            /// The locale a switch is loading, not in effect yet; null when no switch is in flight.
            /// <see cref="Current"/> moves to it once its translations — built-in .po and runtime catalogs — have all
            /// arrived.
            /// </summary>
            public static string Pending => s_pending?.Locale;

            // The latest Set / SetAsync still loading. A request that is no longer s_pending has been superseded or
            // cancelled and settled already; whatever its load brings back is dropped.
            private static Request s_pending;

            private sealed class Request
            {
                public readonly string Locale;
                private readonly List<AwaitableCompletionSource> _waiters = new();

                public Request(string locale) => Locale = locale;

                // One source per SetAsync caller: an Awaitable can only be awaited once.
                public Awaitable Join()
                {
                    var waiter = new AwaitableCompletionSource();
                    _waiters.Add(waiter);
                    return waiter.Awaitable;
                }

                public void Settle(Exception failure = null)
                {
                    foreach (var waiter in _waiters)
                    {
                        if (failure == null) waiter.SetResult();
                        else waiter.SetException(failure);
                    }
                    _waiters.Clear();
                }
            }

            /// <summary>
            /// Switches to <paramref name="locale"/> once its translations have loaded; the current locale stays in
            /// effect meanwhile (see <see cref="Pending"/>). With a synchronous loader — the default Resources path —
            /// the switch is done when this returns. Fire-and-forget: a failed load is logged and switches nothing.
            /// </summary>
            public static void Set(string locale)
            {
                if (s_pending != null && s_pending.Locale == locale) return;   // already loading
                var request = Begin(locale);
                if (request != null) _ = Run(request, logFailure: true);
            }

            /// <summary>
            /// <see cref="Set"/>, awaitable: completes when the switch commits — at once when a later request
            /// supersedes or cancels it — and rethrows a load failure, after which the current locale is unchanged.
            /// </summary>
            public static async Awaitable SetAsync(string locale)
            {
                Awaitable settled;
                if (s_pending != null && s_pending.Locale == locale)
                {
                    settled = s_pending.Join();
                }
                else
                {
                    var request = Begin(locale);
                    if (request == null) return;
                    settled = request.Join();   // before Run: a synchronous load settles it right away
                    _ = Run(request, logFailure: false);
                }
                await settled;
            }

            // The spec's §3.2 rows 2–4 (row 1, the locale already loading, is each caller's own). Returns the request
            // to load, or null when the call is settled already.
            private static Request Begin(string locale)
            {
                CancelPending();
                if (locale == Current) return null;
                if (locale == null)
                {
                    CommitNull();
                    return null;
                }
                return s_pending = new Request(locale);
            }

            private static void CancelPending()
            {
                var pending = s_pending;
                if (pending == null) return;
                s_pending = null;
                pending.Settle();
            }

            // Loads one request, then commits it if it is still the latest. Settles its callers either way.
            private static async Awaitable Run(Request request, bool logFailure)
            {
                LocaleBundle bundle;
                try
                {
                    bundle = await GatherAsync(request.Locale, () => s_pending == request);
                }
                catch (Exception e)
                {
                    if (s_pending != request) return;
                    s_pending = null;   // nothing was committed: the current locale stays
                    if (logFailure) Debug.LogError($"[PromptUGUI] locale load failed for '{request.Locale}': {e}");
                    request.Settle(e);
                    return;
                }
                if (s_pending != request) return;

                try
                {
                    Commit(request.Locale, bundle);
                }
                catch (Exception e)
                {
                    // A Locale.Changed handler threw. The switch itself is in.
                    if (logFailure)
                        Debug.LogError($"[PromptUGUI] Locale.Changed handler failed after switching to '{request.Locale}': {e}");
                    request.Settle(e);
                    return;
                }
                request.Settle();
            }

            // One synchronous step, so a ReSolve sees either all of the old locale or all of the new: the table
            // first, then Current, then both locale variants in one Changed (a single ReSolve of each Screen); the
            // old table goes only after that ReSolve, and Locale.Changed (fonts, host listeners) follows it as before.
            private static void Commit(string locale, LocaleBundle bundle)
            {
                var store = TranslationStore.Instance;
                // Merged, not replaced: entries loaded before Set (tests, host code) stay.
                if (bundle.Builtin != null) store.Load(locale, bundle.Builtin);
                foreach (var (catalog, entries) in bundle.Catalogs)
                    if (s_catalogs.Contains(catalog)) store.LoadLayer(catalog.Layer, locale, entries);

                var old = Current;
                Current = locale;
                s_pending = null;
                if (old == null)
                {
                    VariantStore.Set(locale, true);
                }
                else
                {
                    VariantStore.Set(old, false, locale, true);
                    store.UnloadLocale(old);
                }
                try { Changed?.Invoke(); }
                finally { LoadLateCatalogs(bundle); }
            }

            // Set(null): nothing to load, so it is in at once.
            private static void CommitNull()
            {
                var old = Current;
                Current = null;
                VariantStore.Set(old, false);
                TranslationStore.Instance.UnloadLocale(old);
                Changed?.Invoke();
            }

            /// <summary>
            /// Loads the current locale again — built-in .po and runtime catalogs — and swaps the new entries in at
            /// once; the old ones stay in effect until then. Fire-and-forget: a failure is logged and keeps them.
            /// </summary>
            public static void ReloadCurrent()
            {
                if (Current == null) return;
                _ = ReloadLogged();
            }

            /// <summary><see cref="ReloadCurrent"/>, awaitable: rethrows a load failure (the old entries stay).</summary>
            public static async Awaitable ReloadCurrentAsync()
            {
                if (Current == null) return;
                await ReloadAsync();
            }

            // Every gathered table is replaced on its own rather than the locale unloaded wholesale: a catalog
            // registered while this was loading has already loaded for the locale and is not in the bundle. A switch
            // that commits meanwhile makes the result stale.
            private static async Awaitable ReloadAsync()
            {
                var locale = Current;
                var bundle = await GatherAsync(locale, () => Current == locale);
                if (bundle == null || Current != locale) return;
                var store = TranslationStore.Instance;
                store.ReplaceLocale(locale, bundle.Builtin ?? Array.Empty<PoEntry>());
                foreach (var (catalog, entries) in bundle.Catalogs)
                    store.ReplaceLayer(catalog.Layer, locale, entries);
                VariantStore.NotifyChangedInternal();
            }

            private static async Awaitable ReloadLogged()
            {
                var locale = Current;
                try { await ReloadAsync(); }
                catch (Exception e) { Debug.LogError($"[PromptUGUI] locale reload failed for '{locale}': {e}"); }
            }

            // What a switch or a reload loads before it changes anything.
            private sealed class LocaleBundle
            {
                public IEnumerable<PoEntry> Builtin;
                public readonly List<(RuntimeCatalog Catalog, IEnumerable<PoEntry> Entries)> Catalogs = new();
                // The catalogs registered when the gather started theirs; a later one is not in Catalogs.
                public RuntimeCatalog[] Snapshot = Array.Empty<RuntimeCatalog>();
            }

            // A catalog registered after the switch took its snapshot loaded for the old locale only (its
            // registration loads for Current), so it loads again now that the new one is current.
            private static void LoadLateCatalogs(LocaleBundle bundle)
            {
                foreach (var catalog in s_catalogs.ToArray())
                    if (Array.IndexOf(bundle.Snapshot, catalog) < 0) _ = LoadCatalogForCurrentLogged(catalog);
            }

            // A built-in loader failure throws. A catalog failure is logged and skipped — it does not hold the switch
            // back. Null when `live` turns false before the catalogs start: a newer request took over, or the
            // locale a reload was for is no longer current.
            private static async Awaitable<LocaleBundle> GatherAsync(string locale, Func<bool> live)
            {
                var bundle = new LocaleBundle { Builtin = await GatherBuiltinAsync(locale) };
                if (!live()) return null;
                if (s_catalogs.Count == 0) return bundle;

                // Every catalog's load starts at once, then each is awaited exactly once (Awaitable is pooled).
                var catalogs = s_catalogs.ToArray();
                bundle.Snapshot = catalogs;
                var pending = new Awaitable<IEnumerable<PoEntry>>[catalogs.Length];
                for (var i = 0; i < catalogs.Length; i++)
                {
                    try { pending[i] = catalogs[i].Load(locale); }
                    catch (Exception e) { LogCatalogFailure(catalogs[i], locale, e); }
                }
                for (var i = 0; i < catalogs.Length; i++)
                {
                    if (pending[i] == null) continue;
                    try { bundle.Catalogs.Add((catalogs[i], await pending[i] ?? Array.Empty<PoEntry>())); }
                    catch (Exception e) { LogCatalogFailure(catalogs[i], locale, e); }
                }
                return bundle;
            }

            private static async Awaitable<IEnumerable<PoEntry>> GatherBuiltinAsync(string locale)
            {
                if (PoResolver != null) return await PoResolver(locale);
                var entries = new List<PoEntry>();
                ParseResources($"PromptUGUI/i18n/{locale}", entries);
                ParseResources($"PromptUGUI/i18n-custom/{locale}", entries);
                return entries;
            }

            private static void ParseResources(string resourcesPath, List<PoEntry> into)
            {
                foreach (var asset in UnityEngine.Resources.LoadAll<TextAsset>(resourcesPath))
                {
                    try
                    {
                        // Whole file or nothing: parse it out before adding any of it.
                        into.AddRange(new List<PoEntry>(PoParser.Parse(asset.text)));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[PromptUGUI] failed to parse .po asset '{asset.name}': {e.Message}");
                    }
                }
            }
        }
    }
}
