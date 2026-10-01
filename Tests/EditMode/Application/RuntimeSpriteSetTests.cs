using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// Runtime sprite sets — registry + synchronous entry (spec 2026-10-01-runtime-sprite-sets-design §5, §6).
    /// </summary>
    public class RuntimeSpriteSetTests
    {
        // On-demand loader under test control: one completion source per key, calls counted.
        private readonly Dictionary<string, AwaitableCompletionSource<RuntimeSprite>> _pending = new();
        private int _calls;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _pending.Clear();
            _calls = 0;
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private Awaitable<RuntimeSprite> PendingLoad(string key)
        {
            _calls++;
            var src = new AwaitableCompletionSource<RuntimeSprite>();
            _pending[key] = src;
            return src.Awaitable;
        }

        private static RuntimeSpriteSets.Registration Reg(string name)
        {
            Assert.IsTrue(RuntimeSpriteSets.TryGet(name, out var reg), $"set '{name}' is registered");
            return reg;
        }

        private static Sprite NewSprite() =>
            Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);

        private static Dictionary<string, RuntimeSprite> Entries(params (string key, Sprite sprite)[] es)
        {
            var d = new Dictionary<string, RuntimeSprite>();
            foreach (var (key, sprite) in es) d[key] = sprite;
            return d;
        }

        private static SpriteSet MakeStaticSet(string name)
        {
            var s = ScriptableObject.CreateInstance<SpriteSet>();
            var so = new UnityEditor.SerializedObject(s);
            so.FindProperty("setName").stringValue = name;
            so.ApplyModifiedProperties();
            return s;
        }

        private static Btn BuildBtn(string attrs)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + $"<Btn id='b' {attrs}>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Btn>("b");
        }

        // ── registration ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Invalid_set_name_throws()
        {
            var e = Entries();
            Assert.Catch<ArgumentException>(() => UI.RegisterRuntimeSpriteSet(null, e));
            Assert.Throws<ArgumentException>(() => UI.RegisterRuntimeSpriteSet("", e));
            Assert.Throws<ArgumentException>(() => UI.RegisterRuntimeSpriteSet("a b", e));
            Assert.Throws<ArgumentException>(() => UI.RegisterRuntimeSpriteSet("a:b", e));
            Assert.IsFalse(UI.UnregisterRuntimeSpriteSet("a b"), "a rejected name is not registered");
        }

        [Test]
        public void Null_entries_or_loader_throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                UI.RegisterRuntimeSpriteSet("a", (IReadOnlyDictionary<string, RuntimeSprite>)null));
            Assert.Throws<ArgumentNullException>(() =>
                UI.RegisterRuntimeSpriteSet("a", (Func<string, Awaitable<RuntimeSprite>>)null));
        }

        [Test]
        public void Empty_key_in_entries_throws()
        {
            Assert.Throws<ArgumentException>(() =>
                UI.RegisterRuntimeSpriteSet("pack", Entries(("ok", NewSprite()), ("", NewSprite()))));
            Assert.IsFalse(UI.UnregisterRuntimeSpriteSet("pack"), "nothing is registered when validation fails");
        }

        [Test]
        public void Null_sprite_entries_are_skipped()
        {
            var y = NewSprite();
            var missing = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", null), ("y", y)),
                new RuntimeSpriteSetOptions { Missing = missing });

            LogAssert.Expect(LogType.Warning, new Regex("sprite 'pack:x'"));
            Assert.AreSame(missing, UI.ResolveSprite("pack:x"));
            Assert.AreSame(y, UI.ResolveSprite("pack:y"));
        }

        [Test]
        public void Entries_and_options_are_snapshotted_at_register()
        {
            var a = NewSprite();
            var b = NewSprite();
            var missing = NewSprite();
            var entries = Entries(("x", a));
            var options = new RuntimeSpriteSetOptions { Missing = missing };
            UI.RegisterRuntimeSpriteSet("pack", entries, options);

            entries["x"] = b;
            entries["y"] = b;
            options.Missing = null;

            Assert.AreSame(a, UI.ResolveSprite("pack:x"));
            LogAssert.Expect(LogType.Warning, new Regex("sprite 'pack:y'"));
            Assert.AreSame(missing, UI.ResolveSprite("pack:y"));
        }

        [Test]
        public void Duplicate_runtime_name_throws()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries());
            var ex = Assert.Throws<InvalidOperationException>(() =>
                UI.RegisterRuntimeSpriteSet("pack", _ => AwaitableHelpers.Completed<RuntimeSprite>(default)));
            StringAssert.Contains("Duplicate SpriteSet name 'pack'", ex.Message);
        }

        [Test]
        public void Name_colliding_with_static_set_throws_in_both_orders()
        {
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });
            var ex = Assert.Throws<InvalidOperationException>(() => UI.RegisterRuntimeSpriteSet("ui", Entries()));
            StringAssert.Contains("Duplicate SpriteSet name 'ui'", ex.Message);

            UI.RegisterRuntimeSpriteSet("pack", Entries());
            var ex2 = Assert.Throws<InvalidOperationException>(() =>
                SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui"), MakeStaticSet("pack") }));
            StringAssert.Contains("Duplicate SpriteSet name 'pack'", ex2.Message);
            StringAssert.Contains("runtime", ex2.Message);
        }

        [Test]
        public void Unregister_unknown_returns_false()
        {
            Assert.IsFalse(UI.UnregisterRuntimeSpriteSet("nope"));
        }

        [Test]
        public void Unregister_known_stops_resolving()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));
            Assert.IsTrue(UI.UnregisterRuntimeSpriteSet("pack"));

            LogAssert.Expect(LogType.Error, new Regex("UI.SpriteResolver is not registered"));
            Assert.IsNull(UI.ResolveSprite("pack:x"), "an unregistered set falls back to the static path");
        }

        // ── eager sets through the synchronous entry ──────────────────────────────────────────

        [Test]
        public void Eager_set_resolves_through_ResolveSprite()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x), ("UI/heart", NewSprite())));

            Assert.AreSame(x, UI.ResolveSprite("pack:x"));
            Assert.IsNotNull(UI.ResolveSprite("pack:UI/heart"), "the key is everything after the first ':'");
        }

        [Test]
        public void Eager_missing_key_returns_Missing_and_warns_once()
        {
            var missing = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())),
                new RuntimeSpriteSetOptions { Missing = missing });

            LogAssert.Expect(LogType.Warning, new Regex("sprite 'pack:nope'"));
            Assert.AreSame(missing, UI.ResolveSprite("pack:nope"));
            Assert.AreSame(missing, UI.ResolveSprite("pack:nope"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Eager_missing_key_without_Missing_option_returns_null()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries());
            LogAssert.Expect(LogType.Warning, new Regex("sprite 'pack:nope'"));
            Assert.IsNull(UI.ResolveSprite("pack:nope"));
        }

        [Test]
        public void Eager_set_in_sync_attribute_works()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));

            var btn = BuildBtn("sprite='pack:x'");

            Assert.AreSame(x, btn.GameObject.GetComponent<UnityImage>().sprite);
        }

        [Test]
        public void Works_without_any_SpriteResolver()
        {
            Assume.That(UI.SpriteResolver, Is.Null);
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));

            Assert.AreSame(x, UI.ResolveSprite("pack:x"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Tiled_entry_registers_render_hint()
        {
            var vine = NewSprite();
            var leaf = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", new Dictionary<string, RuntimeSprite>
            {
                ["vine"] = new RuntimeSprite(vine, tiled: true),
                ["leaf"] = leaf,
            });

            Assert.IsTrue(SpriteRenderHints.IsTiled(vine));
            Assert.IsFalse(SpriteRenderHints.IsTiled(leaf));
        }

        // ── on-demand sets: the key state machine (registry level) ────────────────────────────

        [Test]
        public void Request_completes_synchronously_via_raw_OnCompleted()
        {
            // Pins the mechanism the slot relies on: a raw awaiter.OnCompleted continuation runs inside
            // SetResult (EditMode), not on a later frame.
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");
            Assume.That(entry.State, Is.EqualTo(RuntimeSpriteSets.KeyState.Loading));

            var a = NewSprite();
            _pending["a"].SetResult(a);

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Ready, entry.State);
            Assert.AreSame(a, entry.Result.Sprite);
        }

        [Test]
        public void OnDemand_set_in_sync_attribute_errors_once_even_when_cached()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("ugc", key =>
            {
                _calls++;
                return AwaitableHelpers.Completed(new RuntimeSprite(x));
            });
            Assume.That(RuntimeSpriteSets.Request(Reg("ugc"), "x").State, Is.EqualTo(RuntimeSpriteSets.KeyState.Ready));

            LogAssert.Expect(LogType.Error, new Regex("sprite 'ugc:x'.*loads on demand"));
            var btn = BuildBtn("sprite='ugc:x'");
            UI.Get("S").ReSolve();

            Assert.AreNotSame(x, btn.GameObject.GetComponent<UnityImage>().sprite,
                "deterministic: a cached on-demand key is not shown in a synchronous attribute either");
            Assert.AreEqual(1, _calls, "the synchronous entry never calls the loader");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Request_invokes_provider_once_per_key()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var reg = Reg("ugc");

            var a1 = RuntimeSpriteSets.Request(reg, "a");
            var a2 = RuntimeSpriteSets.Request(reg, "a");
            RuntimeSpriteSets.Request(reg, "b");

            Assert.AreSame(a1, a2);
            Assert.AreEqual(2, _calls);
        }

        [Test]
        public void Sync_completed_request_is_ready_without_waiting()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("ugc", _ => AwaitableHelpers.Completed(new RuntimeSprite(x)));

            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "x");

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Ready, entry.State);
            Assert.AreSame(x, entry.Result.Sprite);
        }

        [Test]
        public void Pending_request_settles_on_completion()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");
            Assert.AreEqual(RuntimeSpriteSets.KeyState.Loading, entry.State);

            _pending["a"].SetResult(NewSprite());

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Ready, entry.State);
            Assert.AreSame(entry, RuntimeSpriteSets.Request(Reg("ugc"), "a"), "the settled entry is the cache");
            Assert.AreEqual(1, _calls);
        }

        [Test]
        public void Provider_default_result_marks_key_missing()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            _pending["a"].SetResult(default);

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Missing, entry.State);
            Assert.IsNull(entry.Error);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Provider_sync_throw_marks_missing_and_errors_once()
        {
            UI.RegisterRuntimeSpriteSet("ugc", key =>
            {
                _calls++;
                throw new InvalidOperationException("boom-sync");
            });
            var reg = Reg("ugc");

            LogAssert.Expect(LogType.Error, new Regex("'ugc:a'.*boom-sync"));
            var entry = RuntimeSpriteSets.Request(reg, "a");
            RuntimeSpriteSets.Request(reg, "a");

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Missing, entry.State);
            Assert.IsInstanceOf<InvalidOperationException>(entry.Error);
            Assert.AreEqual(1, _calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Provider_async_fault_marks_missing_and_errors_once()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            LogAssert.Expect(LogType.Error, new Regex("'ugc:a'.*boom-async"));
            _pending["a"].SetException(new System.IO.IOException("boom-async"));

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Missing, entry.State);
            Assert.IsInstanceOf<System.IO.IOException>(entry.Error);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Provider_returning_null_awaitable_marks_missing()
        {
            UI.RegisterRuntimeSpriteSet("ugc", _ => null);

            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Missing, entry.State);
        }

        [Test]
        public void Empty_key_is_missing_without_calling_provider()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);

            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "");

            Assert.AreEqual(RuntimeSpriteSets.KeyState.Missing, entry.State);
            Assert.AreEqual(0, _calls);
        }

        [Test]
        public void Reentrant_request_for_same_key_does_not_reinvoke_provider()
        {
            RuntimeSpriteSets.KeyEntry inner = null;
            UI.RegisterRuntimeSpriteSet("ugc", key =>
            {
                _calls++;
                inner = RuntimeSpriteSets.Request(Reg("ugc"), key);   // the loader asks for its own key again
                return new AwaitableCompletionSource<RuntimeSprite>().Awaitable;
            });

            var outer = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            Assert.AreEqual(1, _calls);
            Assert.AreSame(outer, inner);
        }

        [Test]
        public void Result_after_unregister_is_dropped()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");
            UI.UnregisterRuntimeSpriteSet("ugc");

            var a = NewSprite();
            _pending["a"].SetResult(new RuntimeSprite(a, tiled: true));

            Assert.AreNotEqual(RuntimeSpriteSets.KeyState.Ready, entry.State, "a dead registration keeps no result");
            Assert.IsFalse(SpriteRenderHints.IsTiled(a), "nothing about the late result is recorded");
        }

        [Test]
        public void Result_for_old_registration_does_not_touch_reregistered_set()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            RuntimeSpriteSets.Request(Reg("ugc"), "a");
            var oldSource = _pending["a"];
            UI.UnregisterRuntimeSpriteSet("ugc");

            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var fresh = RuntimeSpriteSets.Request(Reg("ugc"), "a");
            oldSource.SetResult(NewSprite());
            Assert.AreEqual(RuntimeSpriteSets.KeyState.Loading, fresh.State);

            var b = NewSprite();
            _pending["a"].SetResult(b);
            Assert.AreSame(b, fresh.Result.Sprite);
            Assert.AreEqual(2, _calls);
        }

        [Test]
        public void Tiled_result_registers_render_hint()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            RuntimeSpriteSets.Request(Reg("ugc"), "a");

            var a = NewSprite();
            _pending["a"].SetResult(new RuntimeSprite(a, tiled: true));

            Assert.IsTrue(SpriteRenderHints.IsTiled(a));
        }

        // ── diagnostics + living next to SpriteSet assets ─────────────────────────────────────

        [Test]
        public void Failure_message_lists_runtime_sets_and_does_not_say_not_loaded()
        {
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())));
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);

            LogAssert.Expect(LogType.Error, new Regex(
                @"(?s)(?=.*SpriteSet 'zz' is not loaded)(?=.*\bui\b)(?=.*pack \(runtime\))(?=.*ugc \(runtime, on demand\))"));
            UI.ResolveSprite("zz:x");

            LogAssert.Expect(LogType.Warning, new Regex(@"(?s)^(?!.*not loaded).*sprite 'pack:nope'"));
            UI.ResolveSprite("pack:nope");
        }

        [Test]
        public void Null_resolver_error_lists_runtime_sets()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())));

            LogAssert.Expect(LogType.Error, new Regex(@"(?s)UI\.SpriteResolver is not registered.*pack \(runtime\)"));
            Assert.IsNull(UI.ResolveSprite("zz:x"));
        }

        [Test]
        public void Runtime_set_survives_UseSpriteSetResolver_rebind()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));

            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });

            Assert.AreSame(x, UI.ResolveSprite("pack:x"));
        }

        [Test]
        public void Runtime_set_survives_sprite_hot_reload_rebuild()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });

            UI.HotReload.SpriteResolverRebuilder();

            Assert.AreSame(x, UI.ResolveSprite("pack:x"));
        }

        [Test]
        public void LoadedSpriteSetNames_lists_static_sets_only()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries());
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });

            CollectionAssert.AreEquivalent(new[] { "ui" }, UI.LoadedSpriteSetNames);
        }

        [Test]
        public void Static_collision_throws_before_touching_loaded_names()
        {
            SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("ui") });
            var before = UI.SpriteResolver;
            UI.RegisterRuntimeSpriteSet("pack", Entries());

            Assert.Throws<InvalidOperationException>(() =>
                SpriteResolverHelpers.UseSpriteSetResolver(new[] { MakeStaticSet("art"), MakeStaticSet("pack") }));

            CollectionAssert.AreEquivalent(new[] { "ui" }, UI.LoadedSpriteSetNames,
                "a rejected rebuild leaves the previous names untouched");
            Assert.AreSame(before, UI.SpriteResolver, "and the previous resolver installed");
        }

        // ── broadcast + lifecycle ─────────────────────────────────────────────────────────────

        [Test]
        public void Eager_register_after_open_refreshes_xml_declared_btn_sprite()
        {
            LogAssert.Expect(LogType.Error, new Regex("sprite 'pack:x'"));
            var btn = BuildBtn("sprite='pack:x'");

            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));

            Assert.AreSame(x, btn.GameObject.GetComponent<UnityImage>().sprite,
                "registering an eager set replays the open Screens once");
        }

        [Test]
        public void Eager_register_and_unregister_broadcast_once_each()
        {
            var ticks = 0;
            using var sub = UI.VariantStore.Changed.Subscribe(_ => ticks++);

            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())));
            Assert.AreEqual(1, ticks);
            UI.UnregisterRuntimeSpriteSet("pack");
            Assert.AreEqual(2, ticks);
        }

        [Test]
        public void OnDemand_register_and_unregister_do_not_broadcast()
        {
            var ticks = 0;
            using var sub = UI.VariantStore.Changed.Subscribe(_ => ticks++);

            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            UI.UnregisterRuntimeSpriteSet("ugc");

            Assert.AreEqual(0, ticks, "no synchronous attribute can show an on-demand set, so nothing to replay");
        }

        [Test]
        public void UnloadAll_keeps_runtime_sets()
        {
            var x = NewSprite();
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", x)));

            UI.UnloadAll();

            Assert.AreSame(x, UI.ResolveSprite("pack:x"));
        }

        [Test]
        public void ResetForTests_clears_sets_and_drops_pending_results()
        {
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            UI.ResetForTests();
            _pending["a"].SetResult(NewSprite());

            Assert.IsFalse(RuntimeSpriteSets.IsRegistered("ugc"));
            Assert.AreNotEqual(RuntimeSpriteSets.KeyState.Ready, entry.State);
        }

        [Test]
        public void ClearRuntimeRegistrations_clears_sets()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())));
            UI.RegisterRuntimeSpriteSet("ugc", PendingLoad);
            var entry = RuntimeSpriteSets.Request(Reg("ugc"), "a");

            UI.ClearRuntimeRegistrations();
            _pending["a"].SetResult(NewSprite());

            Assert.IsFalse(RuntimeSpriteSets.IsRegistered("pack"));
            Assert.IsFalse(RuntimeSpriteSets.IsRegistered("ugc"));
            Assert.AreNotEqual(RuntimeSpriteSets.KeyState.Ready, entry.State, "registrations are dead before the tables clear");
            UI.RegisterRuntimeSpriteSet("pack", Entries());   // the name is free again
        }

#if UNITY_6000_5_OR_NEWER
        [Test]
        public void Play_mode_entry_clears_runtime_sets()
        {
            UI.RegisterRuntimeSpriteSet("pack", Entries(("x", NewSprite())));

            UI.OnEnteringPlayModeForTests();

            Assert.IsFalse(RuntimeSpriteSets.IsRegistered("pack"));
        }
#endif
    }
}
