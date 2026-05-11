using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.Application
{
    public class HotReloadTests
    {
        private Dictionary<string, string> _files;

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>();
            UI.SourceResolver = src => AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
        }
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Reload_replaces_screen_def()
        {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("S");

            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.ReloadAsync("S").GetAwaiter().GetResult();

            var s = UI.Get("S");
            Assert.IsNotNull(s.Get<Frame>("b"));
            Assert.Throws<KeyNotFoundException>(() => s.Get<Frame>("a"));
        }

        [Test]
        public void Reload_failed_parse_preserves_old_state()
        {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            var oldScreen = UI.Open("S");

            _files["main"] = "<<<not xml>>>";
            Assert.Catch<Exception>(() => UI.ReloadAsync("S").GetAwaiter().GetResult());

            // Old screen still usable
            Assert.IsNotNull(oldScreen.Get<Frame>("a"));
            Assert.AreSame(oldScreen, UI.Get("S"));
        }

        [Test]
        public void Reload_raw_loaded_doc_throws()
        {
            UI.LoadDocument("inline",
                @"<?xml version='1.0'?><PromptUGUI version='1'>
                    <Screen name='S'><Frame/></Screen>
                  </PromptUGUI>");
            UI.Open("S");
            Assert.Throws<InvalidOperationException>(() => UI.ReloadAsync("S").GetAwaiter().GetResult());
        }

        [Test]
        public void Reload_preserves_VariantStore_state()
        {
            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Variants.Set("mobile", true);
            UI.Open("S");

            _files["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.ReloadAsync("S").GetAwaiter().GetResult();

            Assert.IsTrue(UI.Variants.IsActive("mobile"));
        }

        [Test]
        public void ReloadCommonLibraryAsync_picks_up_template_changes()
        {
            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='V'><Frame><Image id='inner_v1'/></Frame></Template>
              </PromptUGUI>";
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><V id='holder'/></Screen>
              </PromptUGUI>";
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            UI.Open("S");

            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='V'><Frame><Image id='inner_v2'/></Frame></Template>
              </PromptUGUI>";
            UI.ReloadCommonLibraryAsync("c").GetAwaiter().GetResult();

            var s = UI.Get("S");
            Assert.IsNotNull(s.Get<Image>("holder/inner_v2"));
        }

        [Test]
        public void ReloadCommonLibraryAsync_failed_parse_rolls_back()
        {
            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='V'><Frame/></Template>
              </PromptUGUI>";
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();

            _files["c"] = "<<<bad>>>";
            Assert.Catch<Exception>(() => UI.ReloadCommonLibraryAsync("c").GetAwaiter().GetResult());

            // Old commons still in pool — proven by being able to reload again with valid xml
            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='V'><Frame/></Template>
              </PromptUGUI>";
            Assert.DoesNotThrow(() => UI.ReloadCommonLibraryAsync("c").GetAwaiter().GetResult());
        }

        [Test]
        public void NotifyAssetChanged_for_screen_src_reloads()
        {
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            UI.Open("S");

            UI.HotReload.AssetPathToSrc = path => path == "fakepath/m.ui.xml" ? "m" : null;
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.HotReload.NotifyAssetChanged("fakepath/m.ui.xml");

            Assert.IsNotNull(UI.Get("S").Get<Frame>("b"));
        }

        [Test]
        public void NotifyAssetChanged_for_commons_src_reloads_screens()
        {
            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='T'><Frame><Image id='inner_v1'/></Frame></Template>
              </PromptUGUI>";
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><T id='outer'/></Screen>
              </PromptUGUI>";
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            UI.Open("S");

            UI.HotReload.AssetPathToSrc = path => path == "p/c.ui.xml" ? "c" : null;
            _files["c"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Template name='T'><Frame><Image id='inner_v2'/></Frame></Template>
              </PromptUGUI>";
            UI.HotReload.NotifyAssetChanged("p/c.ui.xml");

            Assert.IsNotNull(UI.Get("S").Get<Image>("outer/inner_v2"));
        }

        [Test]
        public void NotifyAssetChanged_unknown_path_silently_ignored()
        {
            UI.HotReload.AssetPathToSrc = _ => null;
            Assert.DoesNotThrow(() => UI.HotReload.NotifyAssetChanged("foo"));
        }

        [Test]
        public void NotifyAssetChanged_when_disabled_noops()
        {
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='a'/></Screen>
              </PromptUGUI>";
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            UI.Open("S");

            UI.HotReload.AssetPathToSrc = _ => "m";
            UI.HotReload.Enabled = false;
            _files["m"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='S'><Frame id='b'/></Screen>
              </PromptUGUI>";
            UI.HotReload.NotifyAssetChanged("p");

            Assert.IsNotNull(UI.Get("S").Get<Frame>("a"));   // not reloaded
        }

        [Test]
        public void ReloadAsync_unknown_screen_throws()
        {
            Assert.Throws<InvalidOperationException>(() => UI.ReloadAsync("Nonexistent").GetAwaiter().GetResult());
        }

        [Test]
        public void ReloadCommonLibraryAsync_unknown_src_throws()
        {
            Assert.Throws<InvalidOperationException>(() => UI.ReloadCommonLibraryAsync("not-a-commons").GetAwaiter().GetResult());
        }
    }
}
