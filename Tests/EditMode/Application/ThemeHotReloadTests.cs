using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class ThemeHotReloadTests
    {
        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>();
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void ReplaceFromSrc_Updates_Token_Value_And_Notifies()
        {
            // Seed: 'light' theme with primary=#ff8800 registered against src='themes/main'.
            var v1 = new Dictionary<string, ColorSpec>();
            ColorUtility.TryParseHtmlString("#ff8800", out var c1); v1["primary"] = ColorSpec.Solid(c1);
            ThemeStore.Instance.Register("light", null, v1, "themes/main");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");

            // Simulate hot reload: same src, new value for primary.
            var v2 = new Dictionary<string, ColorSpec>();
            ColorUtility.TryParseHtmlString("#00ff00", out var c2); v2["primary"] = ColorSpec.Solid(c2);

            string fired = null;
            UI.Theme.Changed += n => fired = n;

            ThemeStore.Instance.ReplaceFromSrc("themes/main",
                new List<(string, string, IReadOnlyDictionary<string, ColorSpec>,
                          IReadOnlyDictionary<string, PromptUGUI.IR.StyleDef>)>
                {
                    ("light", null, v2, null)
                });
            UI.Theme.RaiseChangedIfCurrent("light");

            Assert.AreEqual("light", fired);
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void RaiseChangedIfCurrent_NonCurrent_Does_Not_Fire()
        {
            // Seed two themes; Current = 'light'.
            var v = new Dictionary<string, ColorSpec>();
            ColorUtility.TryParseHtmlString("#ff8800", out var c); v["primary"] = ColorSpec.Solid(c);
            ThemeStore.Instance.Register("light", null, v, "themes/main");
            ThemeStore.Instance.Register("dark", null, v, "themes/main");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");

            string fired = null;
            UI.Theme.Changed += n => fired = n;

            // Reload affects 'dark' only; 'light' is unaffected so no event.
            UI.Theme.RaiseChangedIfCurrent("dark");
            Assert.IsNull(fired);
        }

        [Test]
        public void RaiseChangedIfCurrent_NoCurrent_Does_Not_Fire()
        {
            // Edge: Theme.Current is null (no Set yet). Should not throw and not fire.
            string fired = null;
            UI.Theme.Changed += n => fired = n;
            UI.Theme.RaiseChangedIfCurrent("light");
            Assert.IsNull(fired);
        }

        [Test]
        public void NotifyAssetChanged_For_Theme_Commons_Replaces_And_Fires_Changed()
        {
            // Commons-only doc containing a Theme block, loaded as common library.
            _files["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
              </PromptUGUI>";
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual("light", UI.Theme.Current);   // auto-set single
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));

            UI.HotReload.AssetPathToSrc =
                path => path == "p/themes/main.ui.xml" ? "themes/main" : null;

            string fired = null;
            UI.Theme.Changed += n => fired = n;

            _files["themes/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Theme name='light'><Color name='primary' value='#00ff00'/></Theme>
              </PromptUGUI>";
            UI.HotReload.NotifyAssetChanged("p/themes/main.ui.xml");

            Assert.AreEqual("light", UI.Theme.Current);
            Assert.AreEqual("light", fired);
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void ReloadAsync_For_Screen_Doc_Re_Registers_Theme()
        {
            // Screen-doc that also declares a Theme block: ReloadAsync should
            // ReplaceFromSrc (not silently no-op via the idempotent Register
            // path) so edited color values take effect.
            _files["s/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Theme name='light'><Color name='primary' value='#ff8800'/></Theme>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("s/main").GetAwaiter().GetResult();
            UI.Open("S");
            Assert.AreEqual(new Color32(0xff, 0x88, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));

            string fired = null;
            UI.Theme.Changed += n => fired = n;

            _files["s/main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Theme name='light'><Color name='primary' value='#00ff00'/></Theme>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.ReloadAsync("S").GetAwaiter().GetResult();

            Assert.AreEqual("light", fired);
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff),
                            (Color32)UI.Theme.Resolve("primary"));
        }
    }
}
