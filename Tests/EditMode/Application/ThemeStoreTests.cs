using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class ThemeStoreTests
    {
        [SetUp] public void SetUp() => ThemeStore.Instance.Clear();
        [TearDown] public void TearDown() => ThemeStore.Instance.Clear();

        private static Dictionary<string, Color> Map(params (string k, string v)[] entries)
        {
            var d = new Dictionary<string, Color>();
            foreach (var (k, v) in entries)
            {
                ColorUtility.TryParseHtmlString(v, out var c);
                d[k] = c;
            }
            return d;
        }

        [Test]
        public void Register_And_Lookup_Hits_Same_Theme()
        {
            ThemeStore.Instance.Register("light", baseName: null, Map(("primary", "#ff8800")), src: "t");
            ThemeStore.Instance.ResolveBases();
            var c = ThemeStore.Instance.LookupChained("light", "primary");
            Assert.IsTrue(c.HasValue);
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff), (Color32)c.Value);
        }

        [Test]
        public void Lookup_Walks_Base_Chain()
        {
            ThemeStore.Instance.Register("light", null, Map(("primary", "#ff8800"), ("bg", "#ffffff")), "t");
            ThemeStore.Instance.Register("dark", baseName: "light", Map(("primary", "#cc6600")), "t");
            ThemeStore.Instance.ResolveBases();
            // bg not in dark → walks to light
            var bg = ThemeStore.Instance.LookupChained("dark", "bg");
            Assert.IsTrue(bg.HasValue);
            // primary in dark → returns dark's
            var p = ThemeStore.Instance.LookupChained("dark", "primary");
            Assert.AreEqual(new Color32(0xcc, 0x66, 0x00, 0xff), (Color32)p.Value);
        }

        [Test]
        public void Lookup_Missing_Returns_Null()
        {
            ThemeStore.Instance.Register("light", null, Map(("primary", "#ff8800")), "t");
            ThemeStore.Instance.ResolveBases();
            Assert.IsNull(ThemeStore.Instance.LookupChained("light", "nope"));
        }

        [Test]
        public void ResolveBases_Throws_On_Missing_Base()
        {
            ThemeStore.Instance.Register("dark", baseName: "ghost", Map(("primary", "#000000")), "t");
            var ex = Assert.Throws<ParseException>(() => ThemeStore.Instance.ResolveBases());
            StringAssert.Contains("ghost", ex.Message);
            StringAssert.Contains("not found", ex.Message);
        }

        [Test]
        public void ResolveBases_Throws_On_Cycle()
        {
            ThemeStore.Instance.Register("a", baseName: "b", Map(), "t");
            ThemeStore.Instance.Register("b", baseName: "a", Map(), "t");
            var ex = Assert.Throws<ParseException>(() => ThemeStore.Instance.ResolveBases());
            StringAssert.Contains("cycle", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Register_Duplicate_Name_Throws_With_Both_Srcs()
        {
            ThemeStore.Instance.Register("light", null, Map(), "themes/main");
            var ex = Assert.Throws<ParseException>(() =>
                ThemeStore.Instance.Register("light", null, Map(), "themes/extra"));
            StringAssert.Contains("themes/main", ex.Message);
            StringAssert.Contains("themes/extra", ex.Message);
        }

        [Test]
        public void Register_Same_Src_Replaces_Existing_Values()
        {
            // Editor "edit XML → re-Play with Domain Reload off" scenario: the
            // static singleton persists across play sessions, so the same
            // (name, src) re-registers with new values. Must REPLACE, not no-op,
            // or the author's edit is silently dropped.
            ThemeStore.Instance.Register("light", null, Map(("primary", "#ff8800")), "themes/main");
            ThemeStore.Instance.ResolveBases();

            ThemeStore.Instance.Register("light", null, Map(("primary", "#00ff00")), "themes/main");
            ThemeStore.Instance.ResolveBases();

            var c = ThemeStore.Instance.LookupChained("light", "primary");
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff), (Color32)c.Value);
        }
    }
}
