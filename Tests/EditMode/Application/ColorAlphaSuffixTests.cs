using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// Reference-site alpha suffix on color values: <c>color="black/0.5"</c>.
    /// The <c>/&lt;0..1&gt;</c> tail REPLACES the resolved colour's alpha (matches
    /// Unity <c>Color.a</c> / Flutter <c>withOpacity</c> semantics). Definition-side
    /// <c>&lt;Color value="..."&gt;</c> stays pure (covered elsewhere).
    /// </summary>
    public class ColorAlphaSuffixTests
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
        public void NamedColor_With_Alpha_Suffix()
        {
            var c = UI.Theme.Resolve("black/0.5");
            Assert.AreEqual(0f, c.r, 0.001f);
            Assert.AreEqual(0f, c.g, 0.001f);
            Assert.AreEqual(0f, c.b, 0.001f);
            Assert.AreEqual(0.5f, c.a, 0.001f);
        }

        [Test]
        public void Hex_With_Alpha_Suffix()
        {
            var c = UI.Theme.Resolve("#ff0000/0.3");
            Assert.AreEqual(1f, c.r, 0.001f);
            Assert.AreEqual(0f, c.g, 0.001f);
            Assert.AreEqual(0.3f, c.a, 0.001f);
        }

        [Test]
        public void Token_With_Alpha_Suffix()
        {
            Seed("light", null, ("primary", "#ff8800"));
            UI.Theme.Set("light");
            var c = UI.Theme.Resolve("primary/0.5");
            Assert.AreEqual(1f, c.r, 0.005f);          // 0xff
            Assert.AreEqual(0x88 / 255f, c.g, 0.005f); // 0x88
            Assert.AreEqual(0f, c.b, 0.005f);
            Assert.AreEqual(0.5f, c.a, 0.001f);
        }

        [Test]
        public void Suffix_Replaces_Token_Baked_Alpha()
        {
            // scrim is itself 50% opaque (#..80). "/1" must REPLACE → fully opaque,
            // not multiply (which would give ~0.5).
            Seed("light", null, ("scrim", "#00000080"));
            UI.Theme.Set("light");
            Assert.AreEqual(1f, UI.Theme.Resolve("scrim/1").a, 0.001f);
        }

        [Test]
        public void No_Suffix_Leaves_Baked_Alpha_Untouched()
        {
            // No '/' → behaviour identical to before this feature.
            Assert.AreEqual(0x80 / 255f, UI.Theme.Resolve("#ff000080").a, 0.005f);
        }

        [Test]
        public void Alpha_Out_Of_Range_Throws()
        {
            Assert.Throws<Exception>(() => UI.Theme.Resolve("black/1.5"));
            Assert.Throws<Exception>(() => UI.Theme.Resolve("black/-0.1"));
        }

        [Test]
        public void Alpha_Malformed_Throws()
        {
            Assert.Throws<Exception>(() => UI.Theme.Resolve("black/"));   // empty tail
            Assert.Throws<Exception>(() => UI.Theme.Resolve("black/abc")); // non-numeric
            Assert.Throws<Exception>(() => UI.Theme.Resolve("/0.5"));      // no base colour
        }

        [Test]
        public void Suffix_On_Pending_Theme_SoftFails_To_White_Keeping_Alpha()
        {
            UI.Theme.Set("dark");  // intent recorded, theme not yet registered
            var c = UI.Theme.Resolve("primary/0.5");
            Assert.AreEqual(1f, c.r, 0.001f);   // white placeholder
            Assert.AreEqual(0.5f, c.a, 0.001f); // alpha intent survives the placeholder
        }
    }
}
