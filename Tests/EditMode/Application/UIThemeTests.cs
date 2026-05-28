using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class UIThemeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Seed(string name, string baseName, params (string k, string v)[] entries)
        {
            var d = new Dictionary<string, Color>();
            foreach (var (k, v) in entries) { ColorUtility.TryParseHtmlString(v, out var c); d[k] = c; }
            ThemeStore.Instance.Register(name, baseName, d, src: "test");
            ThemeStore.Instance.ResolveBases();
        }

        [Test]
        public void Set_Unknown_Does_Not_Throw_And_Records_Intent()
        {
            // Order-independent: Set accepts any name. Pre-Set before the load
            // completes is the canonical use case (e.g. firing from
            // [RuntimeInitializeOnLoadMethod] before async LoadCommonLibraryAsync
            // resolves).
            Assert.DoesNotThrow(() => UI.Theme.Set("nope"));
            Assert.AreEqual("nope", UI.Theme.Current);
        }

        [Test]
        public void Set_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => UI.Theme.Set(null));
        }

        [Test]
        public void Set_Updates_Current_And_Fires_Changed()
        {
            Seed("light", null, ("primary", "#ff8800"));
            string fired = null;
            UI.Theme.Changed += n => fired = n;
            UI.Theme.Set("light");
            Assert.AreEqual("light", UI.Theme.Current);
            Assert.AreEqual("light", fired);
        }

        [Test]
        public void Set_Then_Register_Fires_Changed_Via_Resolve_Soft_Fail()
        {
            // Boot-time ordering: Set comes first, register comes later.
            // Until register, Resolve must not throw on token names — instead it
            // returns white as a placeholder so open Screens render *something*
            // and snap to the correct color on the eventual Theme.Changed.
            UI.Theme.Set("dark");
            Assert.AreEqual("dark", UI.Theme.Current);
            // Token name + Current set + theme not yet registered → soft-fail to white.
            Assert.AreEqual(Color.white, UI.Theme.Resolve("primary"));
            // Literal still resolves normally even while pending.
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("#ff8800"));
        }

        [Test]
        public void Resolve_Soft_Fail_Does_Not_Apply_When_Current_Is_Null()
        {
            // No Theme.Set was called → token names must still throw (the
            // soft-fail is *only* the in-flight intent case).
            Assert.Throws<Exception>(() => UI.Theme.Resolve("primary"));
        }

        [Test]
        public void Resolve_With_No_Theme_Falls_Back_To_Hex()
        {
            Assert.AreEqual(new Color32(0xff, 0x00, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("#ff0000"));
        }

        [Test]
        public void Resolve_Token_Hit_Returns_Color()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void Resolve_Token_Miss_Falls_Through_To_Hex()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("#00ff00"));
        }

        [Test]
        public void Resolve_Unknown_Token_Unknown_Hex_Throws()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            var ex = Assert.Throws<Exception>(() => UI.Theme.Resolve("primaru"));
            StringAssert.Contains("primaru", ex.Message);
            StringAssert.Contains("light", ex.Message);
        }

        [Test]
        public void Resolve_Empty_Throws()
        {
            Assert.Throws<Exception>(() => UI.Theme.Resolve(""));
            Assert.Throws<Exception>(() => UI.Theme.Resolve(null));
        }

        [Test]
        public void Lookup_When_No_Current_Returns_Null()
        {
            Seed("light", null, ("primary", "#ff8800"));
            // didn't Set
            Assert.IsNull(UI.Theme.Lookup("primary"));
        }

        [Test]
        public void Lookup_Walks_Base()
        {
            Seed("light", null, ("primary", "#ff8800"), ("bg", "#ffffff"));
            Seed("dark", "light", ("primary", "#cc6600"));
            UI.Theme.Set("dark");
            Assert.IsTrue(UI.Theme.Lookup("bg").HasValue);  // from light
        }

        [Test]
        public void ResetForTests_Clears_Store_And_Current()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            UI.ResetForTests();
            Assert.IsNull(UI.Theme.Current);
            CollectionAssert.IsEmpty(UI.Theme.Available);
        }
    }
}
