using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Icon name&gt;</c> / <c>&lt;Image sprite&gt;</c> showing on-demand runtime sprite sets: the slot refreshes
    /// its own control when the sprite arrives, without a ReSolve (spec 2026-10-01-runtime-sprite-sets-design §7).
    /// </summary>
    public class AsyncSpriteSlotTests
    {
        private readonly Dictionary<string, AwaitableCompletionSource<RuntimeSprite>> _pending = new();
        private readonly Dictionary<string, Sprite> _static = new();
        private int _calls;
        private Sprite _loading;
        private Sprite _missing;
        private Icon _reentrantIcon;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _pending.Clear();
            _static.Clear();
            _calls = 0;
            _reentrantIcon = null;
            _loading = NewSprite();
            _missing = NewSprite();
            _static["ui:x"] = NewSprite();
            UI.SpriteResolver = key => _static.TryGetValue(key, out var s) ? s : null;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Sprite NewSprite() =>
            Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);

        private Awaitable<RuntimeSprite> PendingLoad(string key)
        {
            _calls++;
            var src = new AwaitableCompletionSource<RuntimeSprite>();
            _pending[key] = src;
            return src.Awaitable;
        }

        private void RegisterUgc(bool placeholders = true) =>
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad,
                placeholders ? new RuntimeSpriteSetOptions { Loading = _loading, Missing = _missing } : null);

        private static PromptUGUI.Application.Screen Open(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + "<Frame id='box' anchor='top-left' width='400' height='400'>" + body + "</Frame>"
                      + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S");
        }

        private static UnityImage Img(PromptUGUI.Application.Screen s, string id) =>
            s.Get(id).GameObject.GetComponent<UnityImage>();

        private static int VertexCount(UnityImage img)
        {
            var vh = new VertexHelper();
            ((FxImage)img).BuildMeshForTests(vh);
            return vh.currentVertCount;
        }

        // ── the slot core, through <Icon> ─────────────────────────────────────────────────────

        [Test]
        public void Pending_shows_Loading_then_result()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            Assert.AreSame(_loading, Img(s, "i").sprite);

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreSame(a, Img(s, "i").sprite);
        }

        [Test]
        public void Sync_completed_provider_never_shows_Loading()
        {
            var a = NewSprite();
            UI.RegisterRuntimeSpriteSet("ugc", _ => AwaitableHelpers.Completed(new RuntimeSprite(a)),
                new RuntimeSpriteSetOptions { Loading = _loading });

            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");

            Assert.AreSame(a, Img(s, "i").sprite);
        }

        [Test]
        public void Same_key_requested_once_across_icons()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/><Icon id='j' name='ugc:a' size='32x32'/>" +
                         "<Icon id='k' name='ugc:a' size='32x32'/>");

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreEqual(1, _calls);
            Assert.AreSame(a, Img(s, "i").sprite);
            Assert.AreSame(a, Img(s, "j").sprite);
            Assert.AreSame(a, Img(s, "k").sprite);
        }

        [Test]
        public void Code_written_icon_name_refreshes_on_arrival()
        {
            // The core case: a code-written name is runtime-owned, so a ReSolve skips it — only the slot
            // can bring the late sprite in.
            RegisterUgc();
            var s = Open("<Icon id='i' name='ui:x' size='32x32'/>");
            s.Get<Icon>("i").Name = "ugc:a";
            s.ReSolve();
            Assert.AreSame(_loading, Img(s, "i").sprite);

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreSame(a, Img(s, "i").sprite);
        }

        [Test]
        public void Stale_arrival_after_key_change_is_ignored()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            s.Get<Icon>("i").Name = "ugc:b";

            _pending["a"].SetResult(NewSprite());
            Assert.AreSame(_loading, Img(s, "i").sprite, "a's sprite arrives after the icon moved on to b");

            var b = NewSprite();
            _pending["b"].SetResult(b);
            Assert.AreSame(b, Img(s, "i").sprite);
        }

        [Test]
        public void Arrival_after_destroy_is_silent()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            Object.DestroyImmediate(s.RootGameObject);

            _pending["a"].SetResult(NewSprite());

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReSolve_while_pending_does_not_rerequest_or_flash()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");

            s.ReSolve();
            s.ReSolve();

            Assert.AreEqual(1, _calls);
            Assert.AreSame(_loading, Img(s, "i").sprite);
        }

        [Test]
        public void Provider_null_shows_Missing_and_warns_once()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/><Icon id='j' name='ugc:a' size='32x32'/>");

            LogAssert.Expect(LogType.Warning, new Regex("Icon 'ugc:a'"));
            _pending["a"].SetResult(default);

            Assert.AreSame(_missing, Img(s, "i").sprite);
            Assert.AreSame(_missing, Img(s, "j").sprite);
            s.ReSolve();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Provider_exception_shows_Missing_and_errors_once()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/><Icon id='j' name='ugc:a' size='32x32'/>");

            LogAssert.Expect(LogType.Error, new Regex("'ugc:a'.*boom"));
            _pending["a"].SetException(new System.IO.IOException("boom"));

            Assert.AreSame(_missing, Img(s, "i").sprite);
            Assert.AreSame(_missing, Img(s, "j").sprite);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Variant_override_switches_ondemand_key()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' name.alt='ugc:b' size='32x32'/>");

            UI.Variants.Set("alt", true);
            var b = NewSprite();
            _pending["b"].SetResult(b);

            Assert.AreEqual(2, _calls);
            Assert.AreSame(b, Img(s, "i").sprite);
        }

        [Test]
        public void Pending_with_null_Loading_draws_nothing()
        {
            RegisterUgc(placeholders: false);
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");

            Assert.IsNull(Img(s, "i").sprite);
            Assert.AreEqual(0, VertexCount(Img(s, "i")), "not uGUI's solid block");
        }

        [Test]
        public void Missing_with_null_placeholder_draws_nothing()
        {
            RegisterUgc(placeholders: false);
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");

            LogAssert.Expect(LogType.Warning, new Regex("Icon 'ugc:a'"));
            _pending["a"].SetResult(default);

            Assert.AreEqual(0, VertexCount(Img(s, "i")));
        }

        [Test]
        public void Reentrant_set_from_provider_leaves_slot_consistent()
        {
            UI.RegisterRuntimeSpriteSet("ugc", key =>
            {
                _calls++;
                if (key == "a") _reentrantIcon.Name = "ugc:b";   // the loader re-targets the very icon asking
                var src = new AwaitableCompletionSource<RuntimeSprite>();
                _pending[key] = src;
                return src.Awaitable;
            }, new RuntimeSpriteSetOptions { Loading = _loading });
            var s = Open("<Icon id='i' name='ui:x' size='32x32'/>");
            _reentrantIcon = s.Get<Icon>("i");

            _reentrantIcon.Name = "ugc:a";
            _pending["a"].SetResult(NewSprite());
            Assert.AreSame(_loading, Img(s, "i").sprite, "the outer Set was superseded by the reentrant one");

            var b = NewSprite();
            _pending["b"].SetResult(b);
            Assert.AreSame(b, Img(s, "i").sprite);
        }

        // ── register / unregister / self-heal ─────────────────────────────────────────────────

        [Test]
        public void Unregister_clears_then_reregister_refreshes()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            _pending["a"].SetResult(NewSprite());

            UI.UnregisterRuntimeSpriteSet("ugc");
            Assert.IsNull(Img(s, "i").sprite, "the library lets go of the set's sprites");

            RegisterUgc();
            Assert.AreEqual(2, _calls, "the icon asks the new registration for its key");
            var b = NewSprite();
            _pending["a"].SetResult(b);
            Assert.AreSame(b, Img(s, "i").sprite);
        }

        [Test]
        public void Detached_icon_draws_nothing()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            _pending["a"].SetResult(NewSprite());

            UI.UnregisterRuntimeSpriteSet("ugc");

            Assert.AreEqual(0, VertexCount(Img(s, "i")));
        }

        [Test]
        public void Unknown_set_errors_then_heals_on_register()
        {
            LogAssert.Expect(LogType.Error, new Regex("Icon 'pack:x'"));
            var s = Open("<Icon id='i' name='pack:x' size='32x32'/><Icon id='j' name='ui:x' size='32x32'/>");
            LogAssert.Expect(LogType.Error, new Regex("Icon 'pack:y'"));
            s.Get<Icon>("j").Name = "pack:y";

            var x = NewSprite();
            var y = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", new Dictionary<string, RuntimeSprite> { ["x"] = x, ["y"] = y });

            Assert.AreSame(x, Img(s, "i").sprite, "an XML name heals");
            Assert.AreSame(y, Img(s, "j").sprite, "a code-written (runtime-owned) name heals too");
        }

        [Test]
        public void Destroyed_slots_are_pruned_from_registry_tables()
        {
            RegisterUgc();
            var dead = new List<GameObject>();
            for (var i = 0; i < 100; i++)
            {
                var go = new GameObject("icon");
                var icon = new Icon();
                icon.AttachTo(go);
                icon.Name = "ugc:a";
                dead.Add(go);
            }
            Assume.That(RuntimeSpriteSets.BoundCountForTests("ugc"), Is.EqualTo(100));
            foreach (var go in dead) Object.DestroyImmediate(go);

            // The sweep is amortised: it runs when the set reaches twice its last live size (128 here), so the
            // dead hundred go as soon as growth crosses that line — the table tracks the live population.
            var live = new List<GameObject>();
            try
            {
                for (var i = 0; i < 40; i++)
                {
                    var go = new GameObject("live");
                    var icon = new Icon();
                    icon.AttachTo(go);
                    icon.Name = "ugc:a";
                    live.Add(go);
                }

                Assert.AreEqual(40, RuntimeSpriteSets.BoundCountForTests("ugc"),
                    "the destroyed slots were shed; only the live ones remain");
            }
            finally
            {
                foreach (var go in live) Object.DestroyImmediate(go);
            }
        }

        private sealed class CallbackListener : IRuntimeSpriteListener
        {
            public CallbackListener(Control owner) => Owner = owner;
            public Control Owner { get; }
            public System.Action Settled;
            public void OnKeySettled(RuntimeSpriteSets.Registration reg, string key, RuntimeSpriteSets.KeyEntry entry) => Settled();
            public void OnSetRegistered(string setName) { }
            public void OnSetUnregistered(RuntimeSpriteSets.Registration reg) { }
            public void OnStaticResolverInstalled() { }
        }

        [Test]
        public void Register_from_inside_arrival_callback_is_safe()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ugc:a' size='32x32'/><Icon id='j' name='ugc:a' size='32x32'/>");
            Assume.That(RuntimeSpriteSets.TryGet("ugc", out var reg));
            var listener = new CallbackListener(s.Get<Icon>("i"))
            {
                Settled = () =>
                {
                    UI.RegisterRuntimeSpriteSet("pack", new Dictionary<string, RuntimeSprite>());
                    UI.UnregisterRuntimeSpriteSet("ugc");
                },
            };
            RuntimeSpriteSets.AddWaiter(RuntimeSpriteSets.Request(reg, "a"), listener);

            Assert.DoesNotThrow(() => _pending["a"].SetResult(NewSprite()));

            Assert.IsTrue(RuntimeSpriteSets.IsRegistered("pack"));
            Assert.IsFalse(RuntimeSpriteSets.IsRegistered("ugc"));
            LogAssert.NoUnexpectedReceived();
        }

        // ── <Image sprite> ────────────────────────────────────────────────────────────────────

        private static Sprite Bordered() =>
            Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));

        private static PromptUGUI.Controls.Image ImageCtl(PromptUGUI.Application.Screen s, string id) =>
            s.Get<PromptUGUI.Controls.Image>(id);

        [Test]
        public void Image_pending_shows_Loading_then_result()
        {
            RegisterUgc();
            var s = Open("<Image id='i' sprite='ugc:a' size='32x32'/>");
            Assert.AreSame(_loading, Img(s, "i").sprite);

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreSame(a, Img(s, "i").sprite);
        }

        [Test]
        public void Late_arrival_rederives_image_state()
        {
            RegisterUgc();
            var s = Open("<Image id='c' sprite='ugc:wide' type='contain'/><Image id='b' sprite='ugc:frame' size='64x64'/>");

            _pending["wide"].SetResult(Sprite.Create(new Texture2D(200, 100), new Rect(0, 0, 200, 100), Vector2.zero));
            _pending["frame"].SetResult(Bordered());

            Assert.AreEqual(2f, ImageCtl(s, "c").GameObject.GetComponent<AspectRatioFitter>().aspectRatio, 0.001f,
                "contain re-reads the aspect of the sprite that arrived");
            Assert.AreEqual(UnityImage.Type.Sliced, Img(s, "b").type, "a bordered sprite turns the image Sliced");
        }

        [Test]
        public void Sync_hit_and_late_arrival_derive_same_image_type()
        {
            UI.RegisterRuntimeSpriteSet("ugc", key =>
            {
                if (key == "now") return AwaitableHelpers.Completed(new RuntimeSprite(Bordered()));
                return PendingLoad(key);
            }, new RuntimeSpriteSetOptions { Loading = _loading });
            var s = Open("<Image id='i' sprite='ui:x' size='64x64'/><Image id='j' sprite='ui:x' size='64x64'/>");

            ImageCtl(s, "i").Sprite = "ugc:now";
            ImageCtl(s, "j").Sprite = "ugc:later";
            _pending["later"].SetResult(Bordered());

            Assert.AreEqual(UnityImage.Type.Sliced, Img(s, "i").type, "cached: derived on the spot");
            Assert.AreEqual(UnityImage.Type.Sliced, Img(s, "j").type, "late: derived on arrival — same answer");
        }

        [Test]
        public void Explicit_type_is_kept_on_late_arrival()
        {
            RegisterUgc();
            var s = Open("<Image id='i' sprite='ugc:a' type='simple' size='64x64'/>");

            _pending["a"].SetResult(Bordered());

            Assert.AreEqual(UnityImage.Type.Simple, Img(s, "i").type);
        }

        [Test]
        public void Code_written_static_image_sprite_keeps_previous_type()
        {
            // Static values keep today's behaviour exactly: a code write outside a pass derives nothing.
            _static["ui:frame"] = Bordered();
            var s = Open("<Image id='i' sprite='ui:x' size='64x64'/>");
            Assume.That(Img(s, "i").type, Is.EqualTo(UnityImage.Type.Simple));

            ImageCtl(s, "i").Sprite = "ui:frame";

            Assert.AreEqual(UnityImage.Type.Simple, Img(s, "i").type);
        }

        // ── size: an async sprite cannot size its element (spec §7.5) ─────────────────────────

        private static readonly Regex SizeWarning = new("its size comes from the sprite");

        [Test]
        public void Native_sized_icon_on_ondemand_set_warns_once()
        {
            RegisterUgc();
            LogAssert.Expect(LogType.Warning, SizeWarning);
            var s = Open("<Icon id='i' name='ugc:a'/>");

            s.ReSolve();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Explicit_size_does_not_warn()
        {
            RegisterUgc();
            Open("<Icon id='i' name='ugc:a' size='32x32'/>");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Eager_set_native_size_does_not_warn()
        {
            UI.RegisterRuntimeSpriteSet("pack", new Dictionary<string, RuntimeSprite> { ["x"] = NewSprite() });
            Open("<Icon id='i' name='pack:x'/>");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Image_cover_without_size_does_not_warn()
        {
            RegisterUgc();
            Open("<Image id='i' sprite='ugc:a' type='cover'/>");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Grid_cell_icon_without_size_does_not_warn()
        {
            RegisterUgc();
            Open("<Grid columns='2' cellSize='40x40' width='200' height='200'><Icon id='i' name='ugc:a'/></Grid>");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Code_written_ondemand_name_on_native_icon_warns()
        {
            RegisterUgc();
            var s = Open("<Icon id='i' name='ui:x'/>");

            LogAssert.Expect(LogType.Warning, SizeWarning);
            s.Get<Icon>("i").Name = "ugc:a";
        }

        [Test]
        public void Bound_rows_warn_once_per_template_node()
        {
            RegisterUgc();
            const string row = "<Template name='Row'><Frame height='30'><Icon id='icon' name='ui:x'/></Frame></Template>";
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + row
                      + "<Screen name='S'><Frame anchor='top-left' width='400' height='600'>"
                      + "<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/></Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var s = UI.Open("S");

            LogAssert.Expect(LogType.Warning, SizeWarning);
            s.Get<ScrollList>("sl").BindItems(
                R3.Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c", "d", "e" }),
                (IControl slot, string key) => slot.Get<Icon>("icon").Name = "ugc:" + key);

            LogAssert.NoUnexpectedReceived();
        }

        // ── reused rows ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Reused_row_same_key_still_receives_arrival()
        {
            // A reused row's Track bag is released before its next bind; the slot's wait must not live there,
            // or binding the same key again (a no-op) would leave the icon waiting forever (spec §7.4).
            RegisterUgc();
            const string row = "<Template name='Row'><Frame height='30'><Icon id='icon' name='ui:x' size='24x24'/></Frame></Template>";
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + row
                      + "<Screen name='S'><Frame anchor='top-left' width='400' height='600'>"
                      + "<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/></Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            var rows = new List<IControl>();
            void Push()
            {
                rows.Clear();
                list.BindItems(R3.Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                    (IControl slot, string key) =>
                    {
                        rows.Add(slot);
                        slot.Get<Icon>("icon").Name = "ugc:" + key;
                    });
                Canvas.ForceUpdateCanvases();
            }

            Push();
            var first = rows[0];
            Push();
            Assume.That(rows[0], Is.SameAs(first), "the row is reused");

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreSame(a, first.Get<Icon>("icon").GameObject.GetComponent<UnityImage>().sprite);
        }

        // ── §1.3: a code-written name while UI.SpriteResolver is still loading ────────────────

        [Test]
        public void Code_written_icon_name_during_static_load_refreshes_on_End()
        {
            UI.SpriteResolver = null;
            UI.BeginSpriteResolverLoad();
            var s = Open("<Icon id='i' name='ui:x' size='32x32'/>");
            s.Get<Icon>("i").Name = "ui:y";   // runtime-owned from here: the End broadcast skips it

            var y = NewSprite();
            _static["ui:y"] = y;
            UI.SpriteResolver = key => _static.TryGetValue(key, out var sp) ? sp : null;
            UI.EndSpriteResolverLoad();

            Assert.AreSame(y, Img(s, "i").sprite);
        }

        [Test]
        public void Code_written_native_icon_gets_native_size_after_End()
        {
            UI.SpriteResolver = null;
            UI.BeginSpriteResolverLoad();
            var s = Open("<Icon id='i' name='ui:x'/>");
            s.Get<Icon>("i").Name = "ui:big";

            _static["ui:big"] = Sprite.Create(new Texture2D(16, 16), new Rect(0, 0, 16, 16), Vector2.zero);
            UI.SpriteResolver = key => _static.TryGetValue(key, out var sp) ? sp : null;
            UI.EndSpriteResolverLoad();

            Assert.AreEqual(new Vector2(16, 16), s.Get("i").RectTransform.sizeDelta,
                "the End broadcast re-runs ApplyCommon, which now reads the sprite's native size");
        }

        [Test]
        public void End_without_installed_resolver_stays_silent()
        {
            UI.SpriteResolver = null;
            var go = new GameObject("icon-host");
            try
            {
                var icon = new Icon();
                icon.AttachTo(go);
                UI.BeginSpriteResolverLoad();
                icon.Name = "ui:any";
                UI.EndSpriteResolverLoad();   // the load failed: no resolver after all

                Assert.IsNull(go.GetComponent<UnityImage>().sprite);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Xml_declared_static_miss_after_load_logs_once()
        {
            UI.SpriteResolver = null;
            UI.BeginSpriteResolverLoad();
            Open("<Icon id='i' name='ui:missing' size='32x32'/>");
            UI.SpriteResolver = key => _static.TryGetValue(key, out var sp) ? sp : null;

            LogAssert.Expect(LogType.Error, new Regex("Icon 'ui:missing'"));
            UI.EndSpriteResolverLoad();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Waiting_slot_heals_when_runtime_set_registers_during_static_load()
        {
            UI.SpriteResolver = null;
            UI.BeginSpriteResolverLoad();
            var s = Open("<Icon id='i' name='pack:x' size='32x32'/>");

            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", new Dictionary<string, RuntimeSprite> { ["x"] = x });
            Assert.AreSame(x, Img(s, "i").sprite, "no need to wait for the static load");

            UI.EndSpriteResolverLoad();
            Assert.AreSame(x, Img(s, "i").sprite);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Icon_static_value_reresolves_on_every_set()
        {
            var s = Open("<Icon id='i' name='ui:x' size='32x32'/>");
            var x2 = NewSprite();
            _static["ui:x"] = x2;   // what a sprite hot reload does to the resolver's map

            s.ReSolve();

            Assert.AreSame(x2, Img(s, "i").sprite);
        }
    }
}
