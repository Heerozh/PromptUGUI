using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Template;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// End-to-end behaviour of <c>&lt;Style&gt;</c> / <c>class=</c>: commons sharing and hot reload
    /// (mirroring Templates), Variant re-solve without rebuilding GameObjects, and the CSS-like
    /// "an attribute the control doesn't have is simply ignored" property.
    /// </summary>
    public class StyleIntegrationTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void UseFiles(Dictionary<string, string> files) =>
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);

        private static ProceduralPanel PanelOf(IControl c)
            => ((Control)c).GameObject.GetComponent<ProceduralPanel>();

        [Test]
        public void CommonsStyle_IsVisibleToEntryDocument()
        {
            UseFiles(new Dictionary<string, string>
            {
                ["lib"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' color='#112233' radius='16'/>
                            </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Screen name='S'><Frame id='f' class='card'/></Screen>
                            </PromptUGUI>",
            });
            UI.LoadCommonLibraryAsync("lib").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            var p = PanelOf(UI.Open("S").Get<Frame>("f"));
            Assert.AreEqual(16f, p.CurrentParams.CornerWidth.x);
        }

        [Test]
        public void CommonsStyle_UnderNamespace_IsReferencedWithColon()
        {
            UseFiles(new Dictionary<string, string>
            {
                ["lib"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' radius='24'/>
                            </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Screen name='S'><Frame id='f' color='#fff' class='ui:card'/></Screen>
                            </PromptUGUI>",
            });
            UI.LoadCommonLibraryAsync("lib", "ui").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            Assert.AreEqual(24f, PanelOf(UI.Open("S").Get<Frame>("f")).CurrentParams.CornerWidth.x);
        }

        [Test]
        public void CommonsStyle_ConflictingWithEntryDocument_Throws()
        {
            UseFiles(new Dictionary<string, string>
            {
                ["lib"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' radius='16'/>
                            </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' radius='4'/>
                              <Screen name='S'><Frame id='f' class='card'/></Screen>
                            </PromptUGUI>",
            });
            UI.LoadCommonLibraryAsync("lib").GetAwaiter().GetResult();
            var ex = Assert.Throws<TemplateException>(() =>
                UI.LoadDocumentAsync("main").GetAwaiter().GetResult());
            StringAssert.Contains("card", ex.Message);
        }

        [Test]
        public void ReloadCommonLibrary_RepaintsDependentScreens()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' color='#fff' radius='16'/>
                            </PromptUGUI>",
                ["main"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Screen name='S'><Frame id='f' class='card'/></Screen>
                            </PromptUGUI>",
            };
            UseFiles(files);
            UI.LoadCommonLibraryAsync("lib").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("S");

            files["lib"] = @"<?xml version='1.0'?><PromptUGUI version='1'>
                              <Style name='card' color='#fff' radius='2'/>
                            </PromptUGUI>";
            UI.ReloadCommonLibraryAsync("lib").GetAwaiter().GetResult();

            Assert.AreEqual(2f, PanelOf(UI.Open("S").Get<Frame>("f")).CurrentParams.CornerWidth.x);
        }

        [Test]
        public void VariantOverrideFromStyle_ReSolvesWithoutRebuildingGameObject()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='card' color='#fff' radius='16' radius.mobile='4'/>
  <Screen name='S'><Frame id='f' class='card'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var frame = s.Get<Frame>("f");
            var panel = PanelOf(frame);
            var go = frame.GameObject;
            Assert.AreEqual(16f, panel.CurrentParams.CornerWidth.x);

            UI.Variants.Set("mobile", true);

            Assert.AreSame(go, frame.GameObject, "a Variant flip must never rebuild GameObjects");
            Assert.AreSame(panel, PanelOf(frame), "…nor re-create the panel component");
            Assert.AreEqual(4f, panel.CurrentParams.CornerWidth.x);

            UI.Variants.Set("mobile", false);
            Assert.AreEqual(16f, panel.CurrentParams.CornerWidth.x, "…and it must revert");
        }

        /// <summary>
        /// One style, many control types — and since <Btn> grew a procedural surface
        /// (procedural-surface spec M1) that now means the SAME shape, not just the same colour.
        /// This test used to assert the opposite ("radius means nothing to Btn"); that boundary is
        /// what the milestone removed.
        /// </summary>
        [Test]
        public void Style_SkinsAFrameAndAControlAlike()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='surface' color='#123456' radius='16'/>
  <Screen name='S'>
    <Frame id='f' class='surface'/>
    <Btn id='b' class='surface'>OK</Btn>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");

            Assert.AreEqual(16f, PanelOf(s.Get<Frame>("f")).CurrentParams.CornerWidth.x);

            var btn = s.Get<Btn>("b").GameObject;
            var surface = btn.transform.Find("__Surface");
            Assert.IsNotNull(surface, "the same pack has to reach the Btn's shape too");
            var panel = surface.GetComponent<PromptUGUI.Controls.Internal.ProceduralPanel>();
            Assert.AreEqual(16f, panel.CurrentParams.CornerWidth.x);
            Assert.AreEqual(new Color32(0x12, 0x34, 0x56, 0xff), (Color32)panel.CurrentParams.FillTop);

            Assert.AreEqual(0, btn.GetComponent<UnityEngine.UI.Image>().color.a,
                "the Image stands down while the surface draws — kept, not destroyed");
        }

        [Test]
        public void Style_IgnoresAttributesTheControlDoesNotHave()
        {
            // The CSS property proper: `weld` fuses a Frame's glass children and means nothing to a
            // Btn, so it is dropped there while `color` still lands on both.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='shared' color='#123456' weld='16'/>
  <Screen name='S'>
    <Btn id='b' class='shared'>OK</Btn>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");

            var btnBg = s.Get<Btn>("b").GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(btnBg);
            Assert.AreEqual(new Color32(0x12, 0x34, 0x56, 0xff), (Color32)btnBg.color,
                "color lands; weld is silently dropped, which is what PUI-CONTAINER-VISUAL-ATTR "
                + "reports at lint time");
        }

        [Test]
        public void Style_ColorTokenResolvesThroughTheme()
        {
            // A style value is just a value — tokens and /alpha resolve exactly as they do inline.
            // (Theme seeded straight into the store: the sync LoadDocument overload bypasses
            // DocumentLoader and so never registers <Theme> blocks. Same setup as
            // ColorTokenIntegrationTests.)
            var tokens = new Dictionary<string, ColorSpec>();
            ColorUtility.TryParseHtmlString("#204060", out var surface);
            tokens["surface"] = ColorSpec.Solid(surface);
            ThemeStore.Instance.Register("dark", null, tokens, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("dark");

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='card' color='surface/0.5'/>
  <Screen name='S'><Frame id='f' class='card'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var p = PanelOf(UI.Open("S").Get<Frame>("f"));
            Assert.AreEqual(surface.r, p.CurrentParams.FillTop.r, 0.01f);
            Assert.AreEqual(surface.b, p.CurrentParams.FillTop.b, 0.01f);
            Assert.AreEqual(0.5f, p.CurrentParams.FillTop.a, 0.01f);
        }
    }
}
