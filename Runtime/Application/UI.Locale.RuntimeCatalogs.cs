using System;
using System.Collections.Generic;
using PromptUGUI.I18n;
using UnityEngine;

namespace PromptUGUI.Application
{
    // Runtime .po catalogs (spec 2026-10-01-runtime-sprite-sets-design §8): translations registered from code —
    // a downloaded UGC pack, a live-ops patch — layered over the built-in .po in TranslationStore, so one can
    // be removed again without disturbing the entries it overrode.
    public static partial class UI
    {
        public static partial class Locale
        {
            private sealed class RuntimeCatalog
            {
                public readonly string Name;
                public readonly Func<string, Awaitable<IEnumerable<PoEntry>>> Load;
                public readonly TranslationStore.Layer Layer;

                public RuntimeCatalog(string name, Func<string, Awaitable<IEnumerable<PoEntry>>> load,
                                      TranslationStore.Layer layer)
                {
                    Name = name;
                    Load = load;
                    Layer = layer;
                }
            }

            // Registration order = layer order: a later catalog overrides an earlier one, all override built-in.
            private static readonly List<RuntimeCatalog> s_catalogs = new();

            /// <summary>
            /// Registers a .po catalog. <paramref name="load"/> returns the catalog's entries for a locale (empty
            /// when it has none); it is called for the current locale now, and again for each later
            /// <see cref="Set"/> / <see cref="ReloadCurrent"/>. Its entries override the built-in .po and earlier
            /// catalogs. Fire-and-forget: a loader failure is logged. The loader must time out on its own — a
            /// locale switch waits for every catalog before it completes.
            /// </summary>
            /// <exception cref="InvalidOperationException">A catalog with this name is registered.</exception>
            public static void RegisterRuntimeCatalog(string name, Func<string, Awaitable<IEnumerable<PoEntry>>> load)
            {
                var catalog = AddCatalog(name, load);
                _ = LoadCatalogForCurrentLogged(catalog);
            }

            /// <summary>
            /// <see cref="RegisterRuntimeCatalog"/>, awaitable: completes once the current locale's entries are in,
            /// and rethrows a loader failure. The registration stays either way (the next switch retries it).
            /// Validation errors (duplicate name, null loader) throw synchronously.
            /// </summary>
            public static Awaitable RegisterRuntimeCatalogAsync(string name,
                Func<string, Awaitable<IEnumerable<PoEntry>>> load)
            {
                var catalog = AddCatalog(name, load);
                return LoadCatalogForCurrentAsync(catalog);
            }

            /// <summary>Removes a runtime catalog and its entries. False when no such catalog is registered.</summary>
            public static bool UnregisterRuntimeCatalog(string name)
            {
                var index = s_catalogs.FindIndex(c => c.Name == name);
                if (index < 0) return false;
                var catalog = s_catalogs[index];
                s_catalogs.RemoveAt(index);
                TranslationStore.Instance.RemoveLayer(catalog.Layer);
                VariantStore.NotifyChangedInternal();
                return true;
            }

            private static RuntimeCatalog AddCatalog(string name, Func<string, Awaitable<IEnumerable<PoEntry>>> load)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentException("runtime catalog name must be non-empty", nameof(name));
                if (load == null) throw new ArgumentNullException(nameof(load));
                foreach (var c in s_catalogs)
                    if (c.Name == name)
                        throw new InvalidOperationException($"Duplicate runtime catalog name '{name}'");

                var catalog = new RuntimeCatalog(name, load, TranslationStore.Instance.AddLayer());
                s_catalogs.Add(catalog);
                return catalog;
            }

            private static async Awaitable LoadCatalogForCurrentAsync(RuntimeCatalog catalog)
            {
                var locale = Current;
                if (locale == null) return;   // loaded by the next Set
                var pending = catalog.Load(locale);
                var entries = pending == null ? null : await pending;
                // Stale: the locale moved on, or the catalog was unregistered meanwhile.
                if (Current != locale || !s_catalogs.Contains(catalog)) return;
                TranslationStore.Instance.LoadLayer(catalog.Layer, locale, entries ?? Array.Empty<PoEntry>());
                VariantStore.NotifyChangedInternal();
            }

            private static async Awaitable LoadCatalogForCurrentLogged(RuntimeCatalog catalog)
            {
                try { await LoadCatalogForCurrentAsync(catalog); }
                catch (Exception e) { LogCatalogFailure(catalog, Current, e); }
            }

            /// <summary>
            /// A locale switch / reload, after the built-in .po: every catalog's load starts at once, then each is
            /// awaited exactly once (Awaitable is pooled). One failing catalog is logged and skipped — it does not
            /// hold the switch back. Completes synchronously when there are no catalogs or all are synchronous.
            /// </summary>
            internal static async Awaitable LoadRuntimeCatalogsAsync(string locale)
            {
                if (s_catalogs.Count == 0) return;
                var catalogs = s_catalogs.ToArray();
                var pending = new Awaitable<IEnumerable<PoEntry>>[catalogs.Length];
                for (var i = 0; i < catalogs.Length; i++)
                {
                    try { pending[i] = catalogs[i].Load(locale); }
                    catch (Exception e) { LogCatalogFailure(catalogs[i], locale, e); }
                }
                for (var i = 0; i < catalogs.Length; i++)
                {
                    if (pending[i] == null) continue;
                    IEnumerable<PoEntry> entries;
                    try { entries = await pending[i]; }
                    catch (Exception e)
                    {
                        LogCatalogFailure(catalogs[i], locale, e);
                        continue;
                    }
                    if (Current != locale || !s_catalogs.Contains(catalogs[i])) continue;
                    TranslationStore.Instance.LoadLayer(catalogs[i].Layer, locale, entries ?? Array.Empty<PoEntry>());
                }
            }

            private static void LogCatalogFailure(RuntimeCatalog catalog, string locale, Exception e) =>
                Debug.LogError($"[PromptUGUI] runtime catalog '{catalog.Name}' failed to load for locale '{locale}': {e}");

            /// <summary>Part of <c>UI.ClearRuntimeRegistrations</c>: drops every catalog and its layer.</summary>
            internal static void ClearRuntimeCatalogs()
            {
                s_catalogs.Clear();
                TranslationStore.Instance.ClearLayers();
            }
        }
    }
}
