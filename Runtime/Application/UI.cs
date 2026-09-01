using System;
using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        private static readonly Dictionary<string, ScreenDef> _docs = new();
        private static readonly Dictionary<string, Screen> _open = new();
        private static readonly System.Collections.Generic.Dictionary<TemplateKey, IR.TemplateDef> _commonsPool = new();
        private static readonly System.Collections.Generic.Dictionary<StyleKey, IR.StyleDef> _commonsStyles = new();
        private static readonly DepGraph _depGraph = new();

        public static System.Func<string, UnityEngine.Awaitable<string>> SourceResolver { get; set; }
        public static System.Func<string, UnityEngine.Sprite> SpriteResolver { get; set; }

        // Populated by SpriteResolverHelpers.BuildLookup so resolution-failure
        // diagnostics can tell apart "set never registered" from "set registered
        // but key missing". Empty if the user installed a raw SpriteResolver
        // delegate without going through UseSpriteSetResolver — in that case the
        // diagnostic falls back to the "set not loaded" branch with an empty list.
        internal static readonly System.Collections.Generic.HashSet<string> LoadedSpriteSetNames
            = new(System.StringComparer.Ordinal);

        private static int _spriteResolverLoadCount;

        /// <summary>
        /// True while at least one async sprite-resolver loader (typically
        /// <c>SpriteResolverHelpers.UseAddressableSpriteSetResolver</c>) has called
        /// <see cref="BeginSpriteResolverLoad"/> and not yet called
        /// <see cref="EndSpriteResolverLoad"/>. Lets fire-and-forget callers start
        /// the resolver and immediately <c>UI.Open</c> Screens containing
        /// <c>&lt;Icon&gt;</c>: while the flag is set the icons stay empty silently
        /// instead of logging "SpriteResolver is not registered", and when the
        /// counter drops back to zero a Variant broadcast triggers
        /// <c>Screen.ReSolve</c> so all open icons re-render.
        /// </summary>
        public static bool IsSpriteResolverLoadInFlight => _spriteResolverLoadCount > 0;

        /// <summary>
        /// Marks the start of an async sprite-resolver install. Mirrors
        /// <see cref="EndSpriteResolverLoad"/>; the pair must be balanced (Addressable
        /// helper wraps its await in try/finally). Multiple loaders may overlap —
        /// the counter is nested, so the broadcast happens only when the outermost
        /// load finishes.
        /// </summary>
        internal static void BeginSpriteResolverLoad() => _spriteResolverLoadCount++;

        /// <summary>
        /// Marks the end of an async sprite-resolver install. When the counter drops
        /// to zero, broadcasts a Variant change so open Screens re-resolve and any
        /// <c>&lt;Icon&gt;</c> nodes that rendered empty during the load pick up the
        /// now-installed resolver.
        /// </summary>
        internal static void EndSpriteResolverLoad()
        {
            if (_spriteResolverLoadCount == 0) return;
            _spriteResolverLoadCount--;
            if (_spriteResolverLoadCount == 0)
                VariantStore.NotifyChangedInternal();
        }

        /// <summary>
        /// Dual-syntax sprite resolver entry point used by built-in controls'
        /// `sprite=` setters and recommended for custom Control subclasses.
        /// Values containing `:` are routed to <see cref="SpriteResolver"/>
        /// (SpriteSet/atlas path); bare paths fall through to
        /// <c>Resources.Load&lt;Sprite&gt;</c>. Bare paths may include
        /// <c>#sliceName</c> to pick a named sub-sprite from a multi-sprite
        /// (sliced) texture via <c>Resources.LoadAll&lt;Sprite&gt;</c>; any
        /// file extension on the path before the <c>#</c> is stripped so
        /// <c>foo.png#bar</c> and <c>foo#bar</c> both work. Null/empty input
        /// returns null.
        /// </summary>
        public static UnityEngine.Sprite ResolveSprite(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            if (value.IndexOf(':') >= 0)
            {
                if (SpriteResolver == null)
                {
                    if (IsSpriteResolverLoadInFlight) return null;
                    UnityEngine.Debug.LogError(
                        $"sprite '{value}': UI.SpriteResolver is not registered. " +
                        $"Call SpriteResolverHelpers.UseSpriteSetResolver(spriteSets) " +
                        $"before opening Screens that reference sprite='ns:name'.");
                    return null;
                }
                var sprite = SpriteResolver(value);
                if (sprite == null)
                    UnityEngine.Debug.LogError(BuildSpriteResolutionFailureMessage("sprite", value));
                return sprite;
            }

            int hashIdx = value.IndexOf('#');
            if (hashIdx < 0)
            {
                RegisterPxlHints(value);
                return UnityEngine.Resources.Load<UnityEngine.Sprite>(value);
            }

            var path = value.Substring(0, hashIdx);
            var sliceName = value.Substring(hashIdx + 1);
            // Resources virtual paths don't carry extensions; strip any trailing
            // extension on the value side so sprite="ui/dialog.png#slice" and
            // sprite="ui/dialog.aseprite#slice" both resolve via LoadAll("ui/dialog").
            // dotIdx > slashIdx guards "v2.0/dialog" where the dot is in a folder name.
            var slashIdx = path.LastIndexOf('/');
            var dotIdx = path.LastIndexOf('.');
            if (dotIdx > slashIdx && dotIdx > 0)
                path = path.Substring(0, dotIdx);
            RegisterPxlHints(path);
            var all = UnityEngine.Resources.LoadAll<UnityEngine.Sprite>(path);
            if (all == null || all.Length == 0)
                return null;
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == sliceName) return all[i];

            var names = new string[all.Length];
            for (int i = 0; i < all.Length; i++) names[i] = all[i].name;
            UnityEngine.Debug.LogError(
                $"sprite '{value}': slice '{sliceName}' not found in '{path}'. " +
                $"Available: {string.Join(", ", names)}");
            return null;
        }

        // .pxl 导入的 tiled 提示子资产与 Sprite 同 Resources 路径；LoadAll 有 Unity 资源缓存，重复调用廉价。
        private static void RegisterPxlHints(string resourcesPath)
        {
            var hints = UnityEngine.Resources.LoadAll<PxlSpriteHints>(resourcesPath);
            for (int i = 0; i < hints.Length; i++)
                Internal.SpriteRenderHints.Register(hints[i]);
        }

        // Shared between UI.ResolveSprite and Icon.Name. Splits the `set:key` form
        // and branches the hint on whether the set was ever registered, so the
        // author gets actionable advice instead of the generic "Sync Atlases" line.
        // <paramref name="prefix"/> is "sprite" for ResolveSprite, "Icon" for Icon.
        internal static string BuildSpriteResolutionFailureMessage(string prefix, string value)
        {
            int colon = value.IndexOf(':');
            if (colon <= 0)
                return $"{prefix} '{value}': resolver returned null. " +
                       $"Check the name spelling.";

            var setName = value.Substring(0, colon);
            if (!LoadedSpriteSetNames.Contains(setName))
            {
                var loaded = LoadedSpriteSetNames.Count == 0
                    ? "none"
                    : string.Join(", ", LoadedSpriteSetNames);
                return $"{prefix} '{value}': resolver returned null because SpriteSet '{setName}' is not loaded. " +
                       $"Currently loaded sets: [{loaded}]. " +
                       $"If you registered via UseAddressableSpriteSetResolver, ensure the '{setName}' SpriteSet asset has the matching Addressables label. " +
                       $"If via UseSpriteSetResolver(resourcesSubpath), ensure the asset lives under that Resources subfolder.";
            }

            var key = value.Substring(colon + 1);
            return $"{prefix} '{value}': resolver returned null. SpriteSet '{setName}' is loaded but doesn't contain '{key}'. " +
                   $"Add the sprite to the '{setName}' SpriteSet's source folder, then run " +
                   $"Tools → PromptUGUI → Sprite → Sync Atlases (Selected Set).";
        }

        // Optional override for locale → translation entries. Default (null) loads
        // .po TextAssets from `Resources/PromptUGUI/i18n/{locale}/` and
        // `Resources/PromptUGUI/i18n-custom/{locale}/`. Set to an in-memory resolver
        // (e.g. `_ => System.Linq.Enumerable.Empty<PoEntry>()`) to isolate tests from
        // the host project's PO assets.
        public static System.Func<string, UnityEngine.Awaitable<IEnumerable<I18n.PoEntry>>> PoResolver { get; set; }

        // Invoked from Screen.Open() right after the Canvas + CanvasScaler + GraphicRaycaster
        // are added and renderMode is set to ScreenSpaceOverlay. Use to switch renderMode,
        // assign worldCamera, set sortingOrder, etc. Per-Screen behavior keys off the second arg.
        public static System.Action<UnityEngine.Canvas, string> CanvasConfigurator { get; set; }

        // Project-level default for <Screen> scale-mode. Per-Screen XML override
        // (scale-mode="auto|pixel") wins when present. See ScaleMode.cs for semantics.
        public static ScaleMode DefaultScaleMode { get; set; } = ScaleMode.Auto;

        // Hard floor for the Pixel-mode scaleFactor. Default 0 = no floor (algorithm
        // can fall through 0.5 / 0.25 / 0.125 / ... toward zero on small screens).
        // Set to 0.5 / 1 / etc. to clamp the lower fallback — useful when small
        // screens shouldn't shrink content below a readable threshold (content will
        // overflow instead, and your anchor=stretch elements absorb the slack).
        // Off-ladder values like 0.7 are honored verbatim but defeat integer pixel
        // alignment — stick to {0.5, 1, 2, ...}. No effect on Auto mode.
        public static float MinPixelScale { get; set; } = 0f;

        // When true, constrains the Pixel-mode scaleFactor to a power-of-two ladder
        // (...0.25, 0.5, 1, 2, 4, 8...): the magnify segment snaps DOWN to the largest
        // power of two <= the fit-inside ratio (so a 3x-capable screen renders at 2x,
        // 5x at 4x), keeping content fit-inside. The sub-1x fallback is already 1/2^n,
        // so it is unchanged. Default false = the full integer ladder (1, 2, 3, 4, ...).
        // Applied before MinPixelScale (which still floors the result). No effect on
        // Auto mode. Pair with a power-of-two MinPixelScale to stay on-ladder.
        public static bool PixelScalePowerOfTwo { get; set; } = false;

        // Test seam: when non-null, Screen.ApplyCanvasScaler (Pixel branch) reads canvas
        // size from this override instead of the Canvas RectTransform. Mirrors the pattern
        // used by Internal.OrientationTracker.ScreenSizeOverride.
        internal static System.Func<UnityEngine.Vector2> CanvasSizeOverride { get; set; }

        public static ControlRegistry Registry { get; private set; } = CreateRegistryWithBuiltins();

        internal static VariantStore VariantStore { get; } = new();

        public static class Variants
        {
            public static void Set(string name, bool active) =>
                VariantStore.Set(name, active);
            public static bool IsActive(string name) =>
                VariantStore.IsActive(name);
        }

        /// <summary>
        /// 自动管理 reserved variant <c>portrait</c> / <c>landscape</c>。包内
        /// <see cref="Internal.OrientationTracker"/> 全局单例每帧根据
        /// <c>Screen.width/height</c> 切换这对互斥 variant。用户也可显式调
        /// <see cref="Set"/> 手动覆盖；想完全自管时把 <see cref="AutoTrack"/>
        /// 置 false。等宽高视为 landscape，与 CanvasScaler `match` 自动推断
        /// 的 W&gt;=H 锁宽逻辑保持一致。
        /// </summary>
        public static class Orientation
        {
            public static bool AutoTrack { get; set; } = true;
            public static bool IsPortrait => VariantStore.IsActive("portrait");

            public static void Set(bool isPortrait)
            {
                VariantStore.Set("portrait", isPortrait);
                VariantStore.Set("landscape", !isPortrait);
            }

            internal static void ResetForTestsInternal()
            {
                AutoTrack = true;
                Internal.OrientationTracker.ScreenSizeOverride = null;
            }
        }

        public static partial class Locale
        {
            public static string Current { get; private set; }
            public static event System.Action Changed;

            public static void Set(string locale)
            {
                if (Current == locale) return;
                if (Current != null)
                {
                    VariantStore.Set(Current, false);
                    TranslationStore.Instance.UnloadLocale(Current);
                }
                Current = locale;
                if (locale != null)
                {
                    _ = LoadPoFilesAndApplyAsyncLogged(locale);
                }
                else
                {
                    VariantStore.NotifyChangedInternal();
                    Changed?.Invoke();
                }
            }

            public static async UnityEngine.Awaitable SetAsync(string locale)
            {
                if (Current == locale) return;
                if (Current != null)
                {
                    VariantStore.Set(Current, false);
                    TranslationStore.Instance.UnloadLocale(Current);
                }
                Current = locale;
                if (locale != null)
                {
                    await LoadPoFilesAndApplyAsync(locale);
                }
                else
                {
                    VariantStore.NotifyChangedInternal();
                    Changed?.Invoke();
                }
            }

            public static void SetToSystemDefault(string fallback = null) =>
                SetToSystemDefaultCore(
                    UnityEngine.Application.systemLanguage, Configured, fallback);

            public static UnityEngine.Awaitable SetToSystemDefaultAsync(string fallback = null) =>
                SetToSystemDefaultAsyncCore(
                    UnityEngine.Application.systemLanguage, Configured, fallback);

            internal static void SetToSystemDefaultCore(
                UnityEngine.SystemLanguage systemLanguage,
                System.Collections.Generic.IReadOnlyList<string> configured,
                string fallback) =>
                Set(ResolveSystemDefault(systemLanguage, configured, fallback));

            internal static UnityEngine.Awaitable SetToSystemDefaultAsyncCore(
                UnityEngine.SystemLanguage systemLanguage,
                System.Collections.Generic.IReadOnlyList<string> configured,
                string fallback) =>
                SetAsync(ResolveSystemDefault(systemLanguage, configured, fallback));

            internal static string ResolveSystemDefault(
                UnityEngine.SystemLanguage systemLanguage,
                System.Collections.Generic.IReadOnlyList<string> configured,
                string fallback)
            {
                var sysBcp47 = LocaleHelpers.MapSystemLanguage(systemLanguage);
                var matched = LocaleHelpers.MatchWithFallback(sysBcp47, configured);
                if (matched != null) return matched;
                return fallback ?? sysBcp47;
            }

            public static void InitializeIfNeeded() =>
                InitializeIfNeededCore(UnityEngine.Application.systemLanguage, Configured);

            internal static void InitializeIfNeededCore(
                UnityEngine.SystemLanguage systemLanguage,
                System.Collections.Generic.IReadOnlyList<string> configured)
            {
                if (Current != null) return;
                if (configured == null || configured.Count == 0) return;
                var sysBcp47 = LocaleHelpers.MapSystemLanguage(systemLanguage);
                var matched = LocaleHelpers.MatchWithFallback(sysBcp47, configured);
                if (matched != null)
                {
                    Set(matched);
                    return;
                }
                var displayName = sysBcp47 ?? systemLanguage.ToString();
                UnityEngine.Debug.LogWarning(
                    $"[PromptUGUI] 丢失 '{displayName}', falling back to '{configured[0]}'");
                Set(configured[0]);
            }

            public static System.Collections.Generic.IReadOnlyList<string> Configured
            {
                get
                {
                    var s = PromptUGUISettings.Instance;
                    if (s == null) return System.Array.Empty<string>();
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var lc in s.locales) if (!string.IsNullOrEmpty(lc.locale)) list.Add(lc.locale);
                    return list;
                }
            }

            public static void ReloadCurrent()
            {
                if (Current == null) return;
                _ = ReloadCurrentAsyncLogged();
            }

            public static async UnityEngine.Awaitable ReloadCurrentAsync()
            {
                if (Current == null) return;
                await ReloadCurrentAsyncInternal();
            }

            internal static async UnityEngine.Awaitable LoadPoFilesAndApplyAsync(string locale)
            {
                await LoadPoFilesAsync(locale);
                if (Current != locale) return;              // race guard: don't flip variant for stale
                VariantStore.Set(locale, true);
                Changed?.Invoke();
            }

            private static async UnityEngine.Awaitable LoadPoFilesAndApplyAsyncLogged(string locale)
            {
                try { await LoadPoFilesAndApplyAsync(locale); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] locale load failed for '{locale}': {e}");
                }
            }

            internal static async UnityEngine.Awaitable ReloadCurrentAsyncInternal()
            {
                if (Current == null) return;
                TranslationStore.Instance.UnloadLocale(Current);
                await LoadPoFilesAsync(Current);
                VariantStore.NotifyChangedInternal();
            }

            private static async UnityEngine.Awaitable ReloadCurrentAsyncLogged()
            {
                try { await ReloadCurrentAsyncInternal(); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] locale reload failed for '{Current}': {e}");
                }
            }

            internal static void ResetForTestsInternal()
            {
                if (Current != null) VariantStore.Set(Current, false);
                Current = null;
                Changed = null;
            }
        }

        public static partial class Theme
        {
            public static string Current { get; private set; }
            public static IReadOnlyCollection<string> Available => ThemeStore.Instance.Available;

            public static event System.Action<string> Changed;

            /// <summary>
            /// Set the active theme. Order-independent: accepts any name, including
            /// one not yet registered (e.g. the user fires Set before the async
            /// LoadCommonLibraryAsync completes). If the name later registers,
            /// Theme.Changed re-fires automatically so open Screens repaint.
            /// While pending, color attribute resolution falls back to
            /// <see cref="UnityEngine.Color.white"/> for token values (literal
            /// hex/named values still resolve normally). See <see cref="Resolve"/>.
            /// </summary>
            public static void Set(string name)
            {
                if (name == null) throw new System.ArgumentNullException(nameof(name));
                if (Current == name) return;
                Current = name;
                Changed?.Invoke(name);
            }

            /// <summary>
            /// Looks up a color token in the current theme.
            /// For gradient tokens, returns the Top stop (the public surface always returns
            /// a single <see cref="UnityEngine.Color"/>). Internal callers that need a
            /// <see cref="ColorSpec"/> with both stops should use
            /// <c>UI.Theme.ResolveSpec</c>.
            /// </summary>
            public static UnityEngine.Color? Lookup(string token)
            {
                if (Current == null) return null;
                var spec = ThemeStore.Instance.LookupChained(Current, token);
                return spec.HasValue ? spec.Value.Top : (UnityEngine.Color?)null;
            }

            /// <summary>
            /// Resolve a colour value that may be a two-stop vertical gradient ("top,bottom").
            /// Each segment independently supports theme tokens, hex/named literals and the
            /// /alpha suffix. A whole-value token may itself BE a gradient token; "/alpha" on
            /// it replaces BOTH stops' alpha. A gradient token used as ONE segment of another
            /// gradient is an error (no nested gradients). Each segment may also carry a stop
            /// position ("#fff 70%,#000"), which moves where the transition happens; a gradient
            /// token brings its own along.
            /// </summary>
            internal static ColorSpec ResolveSpec(string value)
            {
                if (string.IsNullOrEmpty(value))
                    throw new System.Exception("empty color value");

                if (!Parser.ColorParser.TrySplitGradient(value, out var parts, out var gErr))
                    throw new System.Exception(gErr);

                if (parts.Bottom == null)
                    return ResolveSingle(parts.Top, allowGradientToken: true);

                var top = ResolveSingle(parts.Top, allowGradientToken: false);
                var bottom = ResolveSingle(parts.Bottom, allowGradientToken: false);
                return ColorSpec.Gradient(top.Top, bottom.Top,
                                          parts.EffectiveTopStop, parts.EffectiveBottomStop,
                                          parts.CurveExponent);
            }

            /// <summary>Resolve one segment: token / literal + optional /alpha. Returns a gradient only
            /// when the resolved token is itself a gradient AND <paramref name="allowGradientToken"/> is
            /// true; if it's a gradient token but nesting isn't allowed, throws.</summary>
            private static ColorSpec ResolveSingle(string value, bool allowGradientToken)
            {
                if (!Parser.ColorParser.TrySplitAlpha(value, out var baseValue, out var alpha, out var err))
                    throw new System.Exception(err);

                var spec = ResolveBaseSpec(baseValue);
                if (spec.IsGradient && !allowGradientToken)
                    throw new System.Exception(
                        $"color segment \"{value}\": token resolves to a gradient — gradients cannot nest inside a gradient");
                if (alpha.HasValue)
                {
                    var t = spec.Top; t.a = alpha.Value;
                    var b = spec.Bottom; b.a = alpha.Value;
                    spec = spec.IsGradient
                        ? ColorSpec.Gradient(t, b, spec.TopStop, spec.BottomStop, spec.Curve)
                        : ColorSpec.Solid(t);
                }
                return spec;
            }

            public static UnityEngine.Color Resolve(string value)
            {
                var spec = ResolveSpec(value);
                if (spec.IsGradient)
                    throw new System.Exception(
                        $"color \"{value}\": this attribute does not support gradient colors");
                return spec.Top;
            }

            private static ColorSpec ResolveBaseSpec(string value)
            {
                if (Current != null)
                {
                    var hit = ThemeStore.Instance.LookupChained(Current, value);
                    if (hit.HasValue) return hit.Value;
                }
                if (UnityEngine.ColorUtility.TryParseHtmlString(value, out var c))
                    return ColorSpec.Solid(c);
                // Soft-fail for the in-flight load case: Current was Set but its
                // named theme isn't registered yet (e.g. Theme.Set("dark") fired
                // before LoadCommonLibraryAsync completed, or the user pre-Set a
                // theme that will register from a subsequent load). Return white
                // as a placeholder; ReSolve will recompute once the registering
                // pass calls RaiseChangedIfCurrent and fires Theme.Changed.
                if (Current != null
                    && !System.Linq.Enumerable.Contains(ThemeStore.Instance.Available, Current))
                    return ColorSpec.Solid(UnityEngine.Color.white);
                throw new System.Exception(
                    $"unknown color token \"{value}\" (no entry in theme " +
                    $"'{Current ?? "(none)"}', not a valid hex/named literal)");
            }

            internal static void ResetForTestsInternal()
            {
                Current = null;
                Changed = null;
                ThemeStore.Instance.Clear();
            }

            /// <summary>Called by DocumentLoader after loading commons; if only one
            /// theme is registered and Current is unset, auto-select it (single-theme
            /// projects work without explicit Set).</summary>
            internal static void AutoSetIfSingleAvailable()
            {
                if (Current != null) return;
                var available = ThemeStore.Instance.Available;
                if (available.Count != 1) return;
                var only = System.Linq.Enumerable.First(available);
                Current = only;
                Changed?.Invoke(only);
            }

            /// <summary>Fire Theme.Changed for the current theme. Used by hot reload
            /// after a theme block was replaced — if the affected theme is currently
            /// active, re-emit Changed so open Screens ReSolve with new token values.
            /// No-op when Current is null or doesn't match.</summary>
            internal static void RaiseChangedIfCurrent(string themeName)
            {
                if (Current != null && Current == themeName)
                    Changed?.Invoke(Current);
            }
        }

        public static string Tr(string msgid, string ctx = null) =>
            TrResolver.Resolve(msgid, null, ctx);

        private static async UnityEngine.Awaitable LoadPoFilesAsync(string locale)
        {
            if (PoResolver != null)
            {
                var entries = await PoResolver(locale);
                if (Locale.Current != locale) return;          // race guard: stale load
                if (entries != null)
                    TranslationStore.Instance.Load(locale, entries);
                return;
            }
            LoadPoFromResourcesPath($"PromptUGUI/i18n/{locale}", locale);
            LoadPoFromResourcesPath($"PromptUGUI/i18n-custom/{locale}", locale);
        }

        private static void LoadPoFromResourcesPath(string resourcesPath, string locale)
        {
            var assets = UnityEngine.Resources.LoadAll<UnityEngine.TextAsset>(resourcesPath);
            foreach (var asset in assets)
            {
                try
                {
                    var entries = new System.Collections.Generic.List<I18n.PoEntry>(
                        I18n.PoParser.Parse(asset.text));
                    TranslationStore.Instance.Load(locale, entries);
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] failed to parse .po asset '{asset.name}': {e.Message}");
                }
            }
        }

        // 重复 load 已存在的 screen 是有意拒绝(显式生命周期管理,不静默替换旧定义)。报错带上正确操作,
        // 否则作者(尤其在 reconnect / 每次场景加载都 load 的流程里)只看到 "already loaded" 无从下手。
        private static string AlreadyLoadedMessage(string screenName) =>
            $"Screen '{screenName}' already loaded. You likely loaded it on a previous scene without unloading. " +
            $"Call UI.UnloadDocument(\"{screenName}\") (or UI.UnloadAll()) " +
            "before LoadDocument(Async). Also, best to use UI.Router instead of UI.LoadDocument. ";

        public static void LoadDocument(string label, string xml)
        {
            var raw = UIDocumentParser.Parse(xml, label);
            var doc = PromptUGUI.Template.TemplateExpander.Expand(raw);
            foreach (var s in doc.Screens)
            {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(AlreadyLoadedMessage(s.Name));
                _docs[s.Name] = s;
            }
        }

        /// <summary>
        /// 用已取到的 xml 文本装一份文档，但走和 <see cref="LoadDocumentAsync"/> 同一条合并路径：
        /// 沿 <c>&lt;Import&gt;</c> 链（经 <see cref="SourceResolver"/>）取依赖、合并 commons 池
        /// （全局 Template / Style）、注册 Theme 块，再展开模板。
        /// 给模态框 / 加载遮罩（<c>ModalDocCache</c>）用——它们的源文由 <c>ModalSourceLoader</c>
        /// 先取到手（内置皮走 Resources，不经 SourceResolver），所以不能直接按 src 走
        /// <see cref="LoadDocumentAsync"/>；而原先的同步 <see cref="LoadDocument(string, string)"/>
        /// 只做 Parse + Expand，模态 XML 里既用不了 Import 也用不了 commons 里的共享模板。
        /// <para>不入 depGraph：模态不参与 <see cref="ReloadAsync"/>，编辑器里由
        /// <c>ModalDocCache.Invalidate</c> 重载。没有 Import 的文档全程不碰 SourceResolver，
        /// 内置皮在 EditMode（无 PlayerLoop）下照样同步完成。</para>
        /// </summary>
        internal static async UnityEngine.Awaitable LoadDocumentWithCommonsAsync(string label, string xml)
        {
            // 入口文档已在手上：resolver 只替 Import 链取源。没有 Import 时永远不会走到 SourceResolver。
            Func<string, UnityEngine.Awaitable<string>> resolver = s =>
            {
                if (s == label) return AwaitableHelpers.Completed(xml);
                if (SourceResolver == null)
                    throw new System.InvalidOperationException(
                        $"UI.SourceResolver must be set to resolve <Import src=\"{s}\"> of '{label}'");
                return SourceResolver(s);
            };

            var loaded = await DocumentLoader.LoadAndMergeAsync(label, resolver, _commonsPool, _commonsStyles);
            RegisterThemesAndAutoSet(loaded);
            var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);
            foreach (var s in expanded.Screens)
            {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(AlreadyLoadedMessage(s.Name));
                _docs[s.Name] = s;
            }
        }

        internal static void UnloadDocument(string screenName)
        {
            _docs.Remove(screenName);
        }

        public static async UnityEngine.Awaitable<IReadOnlyList<string>> LoadDocumentAsync(string src)
        {
            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before LoadDocumentAsync");

            var loaded = await DocumentLoader.LoadAndMergeAsync(src, SourceResolver, _commonsPool, _commonsStyles);
            RegisterThemesAndAutoSet(loaded);
            var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

            var added = new List<string>();
            foreach (var s in expanded.Screens)
            {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(AlreadyLoadedMessage(s.Name));
                _docs[s.Name] = s;
                added.Add(s.Name);
                _depGraph.ScreenDeps[s.Name] = new DepGraph.ScreenDep
                {
                    EntrySrc = src,
                    AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
                };
            }
            _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
            return added;
        }

        public static async UnityEngine.Awaitable ReloadAsync(string screenName)
        {
            if (!_depGraph.ScreenDeps.TryGetValue(screenName, out var dep))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' was not loaded by src; cannot reload " +
                    $"(use LoadDocumentAsync instead of LoadDocument(label, xml))");

            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before ReloadAsync");

            var loaded = await DocumentLoader.LoadAndMergeAsync(dep.EntrySrc, SourceResolver, _commonsPool, _commonsStyles);
            // Re-register Theme blocks on reload. Mirror LoadDocumentAsync's
            // RegisterThemesAndAutoSet call but route through ReplaceFromSrc so
            // edited color values overwrite the previous (name, src) entries
            // (Register would idempotent-no-op on the same key) and fire
            // Theme.Changed if the current theme was among the replaced ones.
            ReplaceThemesAndNotify(loaded);
            var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

            PromptUGUI.IR.ScreenDef newDef = null;
            foreach (var s in expanded.Screens)
            {
                if (s.Name == screenName) { newDef = s; break; }
            }

            var wasOpen = _open.ContainsKey(screenName);
            if (wasOpen) Close(screenName);

            _docs.Remove(screenName);
            _depGraph.ScreenDeps.Remove(screenName);

            _docs[screenName] = newDef ?? throw new System.InvalidOperationException(
                    $"Screen '{screenName}' no longer present in src='{dep.EntrySrc}' after reload");
            _depGraph.ScreenDeps[screenName] = new DepGraph.ScreenDep
            {
                EntrySrc = dep.EntrySrc,
                AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
            };
            _depGraph.SrcToDeps[dep.EntrySrc] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);

            if (wasOpen) Open(screenName);
        }

        public static UnityEngine.Awaitable LoadCommonLibraryAsync(string src, string @as = null) =>
            LoadCommonLibraryAsyncInternal(src, @as, isReload: false);

        private static async UnityEngine.Awaitable LoadCommonLibraryAsyncInternal(
            string src, string @as, bool isReload)
        {
            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before LoadCommonLibraryAsync");

            var loaded = await DocumentLoader.LoadAsync(src, SourceResolver, allowScreens: false);

            var staged = new System.Collections.Generic.List<(TemplateKey Key, IR.TemplateDef Def)>();
            foreach (var kv in loaded.Templates)
            {
                var rebasedKey = @as == null
                    ? kv.Key
                    : new TemplateKey(@as, kv.Key.Name);
                if (_commonsPool.ContainsKey(rebasedKey))
                    throw new PromptUGUI.Template.TemplateException(
                        $"common library conflict: '{rebasedKey}' already in commons pool");
                staged.Add((rebasedKey, kv.Value));
            }

            var stagedStyles = new System.Collections.Generic.List<(StyleKey Key, IR.StyleDef Def)>();
            foreach (var kv in loaded.Styles)
            {
                var rebasedKey = @as == null
                    ? kv.Key
                    : new StyleKey(@as, kv.Key.Name);
                if (_commonsStyles.ContainsKey(rebasedKey))
                    throw new PromptUGUI.Template.TemplateException(
                        $"common library conflict: style '{rebasedKey}' already in commons pool");
                stagedStyles.Add((rebasedKey, kv.Value));
            }

            foreach (var (key, def) in staged)
            {
                def.OriginSrc = src;
                _commonsPool[key] = def;
            }

            foreach (var (key, def) in stagedStyles)
            {
                def.OriginSrc = src;
                _commonsStyles[key] = def;
            }

            // Register or replace <Theme> blocks. On first load, Register is used
            // (idempotent on (name, src), throws on cross-src duplicate). On reload,
            // the old (name, src) entries are dropped first via ReplaceFromSrc so
            // edited color values actually take effect — Register's idempotent
            // no-op would otherwise silently swallow the new values.
            if (isReload)
                ReplaceThemesAndNotify(loaded);
            else
                RegisterThemesAndAutoSet(loaded);
            WarnIfPendingThemeUnloaded();

            _depGraph.CommonsSources.Add(src);
            _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
        }

        public static async UnityEngine.Awaitable ReloadCommonLibraryAsync(string src)
        {
            if (!_depGraph.CommonsSources.Contains(src))
                throw new System.InvalidOperationException(
                    $"src='{src}' is not a registered common library");

            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before ReloadCommonLibraryAsync");

            // M4 v1 limitation: original `as=` namespace is not preserved across reload.
            var stashed = new System.Collections.Generic.List<
                System.Collections.Generic.KeyValuePair<TemplateKey, IR.TemplateDef>>();
            foreach (var kv in _commonsPool)
                if (kv.Value.OriginSrc == src) stashed.Add(kv);
            foreach (var kv in stashed) _commonsPool.Remove(kv.Key);

            var stashedStyles = new System.Collections.Generic.List<
                System.Collections.Generic.KeyValuePair<StyleKey, IR.StyleDef>>();
            foreach (var kv in _commonsStyles)
                if (kv.Value.OriginSrc == src) stashedStyles.Add(kv);
            foreach (var kv in stashedStyles) _commonsStyles.Remove(kv.Key);

            var prevDeps = _depGraph.SrcToDeps.TryGetValue(src, out var d)
                ? new System.Collections.Generic.HashSet<string>(d) : null;
            _depGraph.CommonsSources.Remove(src);
            _depGraph.SrcToDeps.Remove(src);

            try
            {
                await LoadCommonLibraryAsyncInternal(src, @as: null, isReload: true);
            }
            catch
            {
                foreach (var kv in stashed) _commonsPool[kv.Key] = kv.Value;
                foreach (var kv in stashedStyles) _commonsStyles[kv.Key] = kv.Value;
                _depGraph.CommonsSources.Add(src);
                if (prevDeps != null) _depGraph.SrcToDeps[src] = prevDeps;
                throw;
            }

            var names = new System.Collections.Generic.List<string>(_depGraph.ScreenDeps.Keys);
            foreach (var name in names) await ReloadAsync(name);
        }

        public static Screen Open(string screenName)
        {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(Registry, VariantStore);
            var screen = new Screen(def, inst, Registry, VariantStore);
            // root 被外部销毁(场景重载,未走 Close)时把本 Screen 从 _open 注销,避免 _open 残留
            // GO 已死的 Screen(下次 Open 同名会返回死 Screen)以及静态事件对它的强引用泄漏。
            screen.OnDetachedExternally = s => RemoveFromOpenIfSame(screenName, s);
            // 在 Open() 之前登记到 _open，让 controls 在 OnAttached / setter 阶段
            // 通过 UI.OwnerScreenOf 反查到本 Screen（例如 Toggle.Group 的 Group 解析）。
            _open[screenName] = screen;
            try
            {
                screen.Open();
            }
            catch
            {
                _open.Remove(screenName);
                throw;
            }
            return screen;
        }

        public static void Close(string screenName)
        {
            if (_open.TryGetValue(screenName, out var s))
            {
                s.Close();
                _open.Remove(screenName);
            }
        }

        // Screen 的 root 被外部销毁(未走 Close)时由哨兵回调,把它从 _open 注销——仅当当前登记的
        // 仍是它本身,避免误删一个已用同名 key 重新 Open 的新 Screen。
        private static void RemoveFromOpenIfSame(string key, Screen screen)
        {
            if (_open.TryGetValue(key, out var current) && ReferenceEquals(current, screen))
                _open.Remove(key);
        }

        private static int _modalInstanceSeq;

        /// <summary>
        /// modal / overlay 专用:从已加载的 _docs 实例化一份 Screen,登记进 _open
        /// 用唯一 key(`{docName}#m{n}`),使同一份 XML 可叠多份实例。
        /// 普通 Screen 仍走 Open(name)。
        /// </summary>
        internal static (Screen screen, string key) OpenModalScreen(string docName)
        {
            if (!_docs.TryGetValue(docName, out var def))
                throw new System.InvalidOperationException(
                    $"Modal screen '{docName}' not loaded; call LoadDocument first");
            var key = docName + "#m" + (++_modalInstanceSeq);
            var inst = new ScreenInstantiator(Registry, VariantStore);
            var screen = new Screen(def, inst, Registry, VariantStore);
            screen.OnDetachedExternally = s => RemoveFromOpenIfSame(key, s);
            _open[key] = screen;                 // Open() 前登记,让 OwnerScreenOf 反查得到
            try { screen.Open(); }
            catch { _open.Remove(key); throw; }
            return (screen, key);
        }

        internal static void CloseModalScreen(string key)
        {
            if (_open.TryGetValue(key, out var s))
            {
                s.Close();
                _open.Remove(key);
            }
        }

        public static Screen Get(string screenName) =>
            _open.TryGetValue(screenName, out var s) ? s : null;

        /// <summary>
        /// 解析 "&lt;screenName&gt;/&lt;idPath&gt;" → 控件 RectTransform。screen 名按 _open 实际注册键
        /// 做"最长前缀匹配"（screen 名本身可含斜杠，故不能数斜杠），其后 / 起为 id-path。
        /// idPath 为空 → 该 Screen root。任一步未命中 → false（Toast 控件定位据此退回默认位）。
        /// </summary>
        internal static bool TryResolvePath(string path, out UnityEngine.RectTransform rect)
            => TryResolvePath(path, out _, out rect);

        /// <summary>同 TryResolvePath，但回传 IControl（path==screen 名时 control=null、rect=root）。</summary>
        internal static bool TryResolvePath(string path, out PromptUGUI.Controls.IControl control,
            out UnityEngine.RectTransform rect)
        {
            control = null; rect = null;
            if (string.IsNullOrEmpty(path)) return false;

            string bestKey = null;
            foreach (var key in _open.Keys)
            {
                bool match = path == key
                    || path.StartsWith(key + "/", System.StringComparison.Ordinal);
                if (match && (bestKey == null || key.Length > bestKey.Length))
                    bestKey = key;
            }
            if (bestKey == null) return false;

            var screen = _open[bestKey];
            if (path.Length == bestKey.Length)   // path == screen 名，无 id-path → root
            {
                rect = screen.RootGameObject.GetComponent<UnityEngine.RectTransform>();
                return rect != null;
            }

            string idPath = path.Substring(bestKey.Length + 1);
            try
            {
                control = screen.Get(idPath);
                rect = control?.RectTransform;
                return rect != null;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { return false; }
        }

        /// <summary>
        /// Clears all commons-pool entries and dep-graph commons sources.
        /// Loaded Screens, depGraph.ScreenDeps, SourceResolver, Registry are preserved.
        /// Use when re-bootstrapping commons (e.g., to swap as= namespace).
        /// </summary>
        public static void UnloadAllCommonLibraries()
        {
            _commonsPool.Clear();
            _commonsStyles.Clear();
            _depGraph.CommonsSources.Clear();
            // Remove commons srcs from _srcToDeps; leave screen-related entries intact.
            var commonsSrcs = new System.Collections.Generic.List<string>();
            foreach (var src in _depGraph.SrcToDeps.Keys)
            {
                var stillUsedByScreen = false;
                foreach (var sd in _depGraph.ScreenDeps.Values)
                {
                    if (sd.AllDeps.Contains(src)) { stillUsedByScreen = true; break; }
                }
                if (!stillUsedByScreen) commonsSrcs.Add(src);
            }
            foreach (var s in commonsSrcs) _depGraph.SrcToDeps.Remove(s);
        }

        /// <summary>
        /// Clears all loaded state — commons + Screens + open + dep graph.
        /// Preserves SourceResolver, HotReload.AssetPathToSrc (Editor), and Registry.
        /// </summary>
        public static void UnloadAll()
        {
            Router.CancelAllForTeardown();
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
            Tutorial.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _commonsPool.Clear();
            _commonsStyles.Clear();
            _depGraph.Clear();
        }

        internal static void NotifyVariantChangedForReSolve() =>
            VariantStore.NotifyChangedInternal();

        /// <summary>测试与 ScrollList 用：取已加载的 ScreenDef（含 Templates）。</summary>
        internal static IR.ScreenDef GetScreenDef(string screenName) =>
            _docs.TryGetValue(screenName, out var d) ? d : null;

        /// <summary>测试与 ScrollList 用：拿到一个共享的 ScreenInstantiator（按需 new 一个新的）。</summary>
        internal static ScreenInstantiator GetInstantiator() =>
            new(Registry, VariantStore);

        /// <summary>
        /// 通过 Control 的 GameObject transform 沿树向上找到所属 Screen。
        /// 用于 Toggle / ScrollList 等需要在 attribute setter 里触达 Screen 级 state 的控件。
        /// </summary>
        internal static Screen OwnerScreenOf(Controls.IControl control)
        {
            if (control?.GameObject == null) return null;
            var t = control.GameObject.transform;
            while (t != null)
            {
                var go = t.gameObject;
                foreach (var s in _open.Values)
                {
                    if (s.RootGameObject == go) return s;
                }
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// Parses a raw color string (hex, named, or "top,bottom" gradient) from a
        /// <c>&lt;Color value="…"/&gt;</c> attribute into a <see cref="ColorSpec"/>.
        /// Called from both <see cref="RegisterThemesAndAutoSet"/> and
        /// <see cref="ReplaceThemesAndNotify"/> so the two theme-registration paths
        /// share identical parsing logic and can't silently diverge.
        /// </summary>
        private static ColorSpec ParseThemeColor(string raw)
        {
            Parser.ColorParser.TrySplitGradient(raw, out var parts, out _);
            UnityEngine.ColorUtility.TryParseHtmlString(parts.Top, out var top);
            if (parts.Bottom == null) return ColorSpec.Solid(top);
            UnityEngine.ColorUtility.TryParseHtmlString(parts.Bottom, out var bottom);
            return ColorSpec.Gradient(top, bottom, parts.EffectiveTopStop, parts.EffectiveBottomStop,
                                      parts.CurveExponent);
        }

        /// <summary>
        /// Shared helper: parse colors from <paramref name="loaded"/>.Themes,
        /// register each into <see cref="ThemeStore"/>, resolve base-chains, and
        /// auto-select when exactly one theme is available. Called from both
        /// <see cref="LoadCommonLibraryAsync"/> and <see cref="LoadDocumentAsync"/>
        /// so that &lt;Theme&gt; blocks work regardless of which file they live in.
        /// </summary>
        private static void RegisterThemesAndAutoSet(LoadedDoc loaded)
        {
            foreach (var (theme, themeSrc) in loaded.Themes)
            {
                var colors = new System.Collections.Generic.Dictionary<string, ColorSpec>(
                    theme.Colors.Count);
                foreach (var ce in theme.Colors)
                    colors[ce.Name] = ParseThemeColor(ce.Value);
                ThemeStore.Instance.Register(theme.Name, theme.BaseName, colors, theme.Styles, themeSrc);
            }
            ThemeStore.Instance.ResolveBases();

            // Two paths to drive Theme.Changed after this load:
            //   1. Single-theme auto-select (Current was null, exactly one theme
            //      registered → AutoSet picks it and fires Changed).
            //   2. Order-independent pre-Set: user called Theme.Set("dark")
            //      before this load completed. preExisting captures their intent;
            //      if AutoSet was a no-op (Current preserved), we fire Changed so
            //      open Screens repaint via the soft-fail → real-color transition.
            //      RaiseChangedIfCurrent fires regardless of whether the named
            //      theme is now actually registered — the soft-fail in Resolve
            //      still hits (returns white) for the typo case until the user
            //      Sets a valid name.
            var preExistingCurrent = Theme.Current;
            Theme.AutoSetIfSingleAvailable();
            if (preExistingCurrent != null && Theme.Current == preExistingCurrent)
                Theme.RaiseChangedIfCurrent(preExistingCurrent);
        }

        /// <summary>
        /// Called from the boot loader paths after register/replace finishes.
        /// If <see cref="Theme.Current"/> points at a theme name nobody actually
        /// loaded, log a one-line warning so authors don't get stuck wondering
        /// why their pre-<see cref="Theme.Set"/> never produced colors.
        /// </summary>
        private static void WarnIfPendingThemeUnloaded()
        {
            var current = Theme.Current;
            if (current == null) return;
            if (System.Linq.Enumerable.Contains(ThemeStore.Instance.Available, current))
                return;
            UnityEngine.Debug.LogWarning(
                $"UI.Theme.Current is '{current}' but that theme is not registered " +
                $"(no <Theme name=\"{current}\"> was loaded by the time " +
                "LoadCommonLibraryAsync completed). Typo, missing source file, " +
                "or load not yet finished?");
        }

        /// <summary>
        /// Hot-reload sibling of <see cref="RegisterThemesAndAutoSet"/>: groups
        /// themes by their originating src and routes each group through
        /// <see cref="ThemeStore.ReplaceFromSrc"/> so edited color values overwrite
        /// the previous (name, src) entries rather than no-oping through Register's
        /// idempotent path. After replacement, fires <see cref="Theme.Changed"/>
        /// for the current theme if it was among the replaced ones so all open
        /// Screens re-color via <see cref="Screen.ReSolve"/>. Theme.Current is
        /// preserved (no AutoSetIfSingleAvailable on reload).
        /// </summary>
        private static void ReplaceThemesAndNotify(LoadedDoc loaded)
        {
            // Group themes by their originating src so ReplaceFromSrc gets a
            // per-src complete replacement (any theme deleted from the new XML
            // for that src will correctly disappear from the store too).
            var bySrc = new System.Collections.Generic.Dictionary<
                string,
                System.Collections.Generic.List<(string name, string baseName,
                    System.Collections.Generic.IReadOnlyDictionary<string, ColorSpec> colors,
                    System.Collections.Generic.IReadOnlyDictionary<string, IR.StyleDef> styles)>>();
            foreach (var (theme, themeSrc) in loaded.Themes)
            {
                var colors = new System.Collections.Generic.Dictionary<string, ColorSpec>(
                    theme.Colors.Count);
                foreach (var ce in theme.Colors)
                    colors[ce.Name] = ParseThemeColor(ce.Value);
                if (!bySrc.TryGetValue(themeSrc, out var list))
                    bySrc[themeSrc] = list = new();
                list.Add((theme.Name, theme.BaseName, colors, theme.Styles));
            }
            foreach (var kv in bySrc)
                ThemeStore.Instance.ReplaceFromSrc(kv.Key, kv.Value);

            // Re-emit Changed for the current theme if any of the replaced themes
            // are it. Open Screens are subscribed to Theme.Changed and will ReSolve.
            if (Theme.Current != null)
            {
                foreach (var list in bySrc.Values)
                {
                    foreach (var (themeName, _, _, _) in list)
                    {
                        if (themeName == Theme.Current)
                        {
                            Theme.RaiseChangedIfCurrent(Theme.Current);
                            return;
                        }
                    }
                }
            }
        }

        // ResetForTests 末尾触发；let helpers (e.g. AddressableSpriteResolverHelper)
        // 释放 Addressables 句柄等外部资源。订阅者必须在 ResetForTests 自身把状态
        // 清空之后再跑，所以 Invoke 放在方法尾部。
        public static event System.Action OnReset;

        // 仅测试使用
        internal static void ResetForTests()
        {
            Locale.ResetForTestsInternal();
            Orientation.ResetForTestsInternal();
            Theme.ResetForTestsInternal();
            Markdown.ResetForTestsInternal();
            Internal.SpriteRenderHints.Clear();
            Controls.Internal.ProceduralBuilders.ResetDefaultSpriteCacheForTests();
            TranslationStore.Instance.UnloadAll();
            Router.ResetForTestsInternal();
            Tutorial.ResetForTestsInternal();
            Navigation.ResetForTestsInternal();
            Controls.TabMenu.ResetForTestsInternal();
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _modalInstanceSeq = 0;
            _docs.Clear();
            VariantStore.Reset();
            Registry = CreateRegistryWithBuiltins();
            _commonsPool.Clear();
            _commonsStyles.Clear();
            // After the _open loop above: closing Screens destroys their panels, which is what
            // decrements the glass panel count — resetting before that would leave it negative.
            GlassRuntime.ResetForTestsInternal();
            Controls.Internal.ProceduralMaterialCache.ResetForTests();
            Controls.Internal.FxMaterialCache.ResetForTests();
            _depGraph.Clear();
            SourceResolver = null;
            SpriteResolver = null;
            LoadedSpriteSetNames.Clear();
            _spriteResolverLoadCount = 0;
            PoResolver = null;
            CanvasConfigurator = null;
            DefaultScaleMode = ScaleMode.Auto;
            MinPixelScale = 0f;
            PixelScalePowerOfTwo = false;
            CanvasSizeOverride = null;
#if UNITY_EDITOR
            HotReload.AssetPathToSrc = null;
            HotReload.SpriteResolverRebuilder = null;
            HotReload.Enabled = true;
#endif
            OnReset?.Invoke();
        }

        private static ControlRegistry CreateRegistryWithBuiltins()
        {
            var r = new ControlRegistry();
            BuiltinPrimitives.Register(r);
            return r;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitializeLocale() => Locale.InitializeIfNeeded();

#if UNITY_6000_5_OR_NEWER
        // Clears stale Screens/docs/commons/dep-graph that survive Play→Stop→Play
        // when "Reload Domain" is disabled in Enter Play Mode Options. SourceResolver,
        // SpriteResolver and Registry (with built-ins) are intentionally preserved.
        [UnityEngine.OnEnteringPlayMode]
        private static void OnEnteringPlayMode() => UnloadAll();

        // Symmetric cleanup on play exit. Without this, Screens whose GameObjects
        // Unity tears down still sit in _open; later Editor work (e.g. icon sync's
        // ReSolve broadcast) walks them and hits destroyed RectTransforms.
        [UnityEngine.OnExitingPlayMode]
        private static void OnExitingPlayMode() => UnloadAll();

        // Test seam for the [OnEnteringPlayMode] handler above.
        internal static void OnEnteringPlayModeForTests() => OnEnteringPlayMode();
#endif

#if UNITY_EDITOR
        public static class HotReload
        {
            public static System.Func<string, string> AssetPathToSrc { get; set; }
            public static bool Enabled { get; set; } = true;

            public static void NotifyAssetChanged(string assetPath)
            {
                if (!Enabled || AssetPathToSrc == null) return;
                var src = AssetPathToSrc(assetPath);
                if (string.IsNullOrEmpty(src)) return;

                if (_depGraph.IsCommons(src))
                {
                    _ = ReloadCommonLibraryAsyncLogged(src);
                    return;
                }

                var affected = new System.Collections.Generic.List<string>();
                foreach (var name in _depGraph.ScreensDependingOn(src))
                    affected.Add(name);
                foreach (var name in affected) _ = ReloadAsyncLogged(name);
            }

            private static async UnityEngine.Awaitable ReloadAsyncLogged(string screenName)
            {
                try { await ReloadAsync(screenName); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload failed for screen '{screenName}': {e}");
                }
            }

            private static async UnityEngine.Awaitable ReloadCommonLibraryAsyncLogged(string src)
            {
                try { await ReloadCommonLibraryAsync(src); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload commons failed for src '{src}': {e}");
                }
            }

            public static System.Action SpriteResolverRebuilder { get; set; }

            public static void NotifySpriteAssetsChanged()
            {
                if (!Enabled) return;
                SpriteResolverRebuilder?.Invoke();
                foreach (var s in _open.Values) s.ReSolve();
            }
        }
#endif
    }
}
