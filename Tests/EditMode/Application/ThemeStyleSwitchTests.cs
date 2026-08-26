using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using PromptScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// End-to-end theme-driven styling (2026-08-26 spec §5): switching <c>UI.Theme.Current</c> must
    /// re-derive every <c>class=</c> node's attributes and let the existing ReSolve replay them —
    /// without rebuilding a single GameObject.
    ///
    /// <para>Themes register through the async load path (<c>RegisterThemesAndAutoSet</c>); the sync
    /// <c>LoadDocument(label, xml)</c> overload deliberately bypasses it, so these use the fake-files
    /// resolver pattern.</para>
    /// </summary>
    public class ThemeStyleSwitchTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void UseFiles(Dictionary<string, string> files) =>
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);

        private static PromptScreen Load(string body, string screenName = "S")
        {
            UseFiles(new Dictionary<string, string>
            {
                ["main"] = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>",
            });
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            return UI.Open(screenName);
        }

        private static string ColorOf(PromptScreen screen, string id)
        {
            var go = ((Control)(object)screen.Get<Control>(id)).GameObject;
            return ColorUtility.ToHtmlStringRGB(go.GetComponent<UnityImage>().color);
        }

        private const string TwoThemes = @"
            <Style name='card' color='#112233' type='sliced'/>
            <Theme name='modern'><Color name='ink' value='#000'/></Theme>
            <Theme name='pixel'>
              <Color name='ink' value='#111'/>
              <Style name='card' color='#445566'/>
            </Theme>
            <Screen name='S'><Image id='c' class='card'/></Screen>";

        [Test]
        public void SwitchingTheme_ReDerivesTheClassPack()
        {
            var screen = Load(TwoThemes);
            UI.Theme.Set("modern");
            Assume.That(ColorOf(screen, "c"), Is.EqualTo("112233"),
                "guard: 'modern' declares no card pack, so the global one stands");

            UI.Theme.Set("pixel");

            Assert.AreEqual("445566", ColorOf(screen, "c"));
        }

        // §4.2: the global <Style> is the implicit root of every theme chain, so an attribute the
        // theme does not mention keeps the global value instead of going unresolved.
        [Test]
        public void AttributeTheThemeOmits_FallsBackToTheGlobalPack()
        {
            var screen = Load(TwoThemes);
            UI.Theme.Set("pixel");

            var img = ((Control)(object)screen.Get<Control>("c")).GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(UnityImage.Type.Sliced, img.type,
                "'type' comes from the global pack; the pixel theme only overrode 'color'");
        }

        // §6.1's positive case: with the global pack supplying a baseline, a round trip returns to
        // where it started rather than leaving the previous theme's value stuck.
        [Test]
        public void ThemeRoundTrip_ReturnsToTheGlobalValue()
        {
            var screen = Load(TwoThemes);
            UI.Theme.Set("modern");
            var before = ColorOf(screen, "c");

            UI.Theme.Set("pixel");
            Assume.That(ColorOf(screen, "c"), Is.Not.EqualTo(before), "guard: the switch did something");

            UI.Theme.Set("modern");

            Assert.AreEqual(before, ColorOf(screen, "c"));
        }

        [Test]
        public void SwitchingTheme_DoesNotRebuildGameObjects()
        {
            var screen = Load(TwoThemes);
            UI.Theme.Set("modern");
            var go = ((Control)(object)screen.Get<Control>("c")).GameObject;

            UI.Theme.Set("pixel");

            Assert.AreSame(go, ((Control)(object)screen.Get<Control>("c")).GameObject,
                "references and R3 subscriptions must survive a re-skin — this is attribute replay, "
                + "not a rebuild");
        }

        [Test]
        public void InlineAttribute_StillBeatsTheThemePack()
        {
            var screen = Load(@"
                <Style name='card' color='#112233'/>
                <Theme name='pixel'><Style name='card' color='#445566'/></Theme>
                <Screen name='S'><Image id='c' class='card' color='#aabbcc'/></Screen>");

            UI.Theme.Set("pixel");

            Assert.AreEqual("AABBCC", ColorOf(screen, "c"),
                "what the author spelled out on the node outranks any pack, themed or not");
        }

        [Test]
        public void ThemeStyle_AppliesOnFirstOpen_NotOnlyOnSwitch()
        {
            UseFiles(new Dictionary<string, string>
            {
                ["main"] = @"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>
                    <Style name='card' color='#112233'/>
                    <Theme name='pixel'><Style name='card' color='#445566'/></Theme>
                    <Screen name='S'><Image id='c' class='card'/></Screen></PromptUGUI>",
            });
            UI.Theme.Set("pixel");                 // active BEFORE the document is loaded
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();

            Assert.AreEqual("445566", ColorOf(UI.Open("S"), "c"),
                "Open re-derives too; a Screen must not have to be switched away and back to get "
                + "the active skin");
        }

        // A theme's pack must compose with Variant resolution, not replace it.
        [Test]
        public void ThemePack_VariantEntries_ResolveThroughTheNormalMachinery()
        {
            var screen = Load(@"
                <Style name='card' color='#112233'/>
                <Theme name='pixel'><Style name='card' color='#445566' color.mobile='#778899'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            UI.Theme.Set("pixel");
            Assert.AreEqual("445566", ColorOf(screen, "c"), "base value of the themed pack");

            UI.Variants.Set("mobile", true);
            Assert.AreEqual("778899", ColorOf(screen, "c"), "the pack's .variant entry wins when active");

            UI.Variants.Set("mobile", false);
            Assert.AreEqual("445566", ColorOf(screen, "c"), "and reverts to the pack's base");
        }

        // Regression: the theme re-merge walks itemTemplate bodies so rows bound after a switch get
        // the current skin. A body kept RAW — because a required <Param> left nothing to substitute —
        // still reads class="{{skin}}", and looking that up as a style name threw
        // "unknown style '{{skin}}'", taking down UI.Open and the lint CLI alike. This is the shape
        // of the shipped ProceduralStyle sample, which is how it was caught.
        [Test]
        public void RawItemTemplateBody_WithAPlaceholderClass_SurvivesTheThemeReMerge()
        {
            Assert.DoesNotThrow(() => Load(@"
                <Style name='card' color='#112233'/>
                <Theme name='pixel'><Style name='card' color='#445566'/></Theme>
                <Template name='Skinned'><Param name='skin'/><Frame id='b' class='{{skin}}'/></Template>
                <Screen name='S'><Image id='c' class='card'/></Screen>"));

            UI.Theme.Set("pixel");
            Assert.AreEqual("445566", ColorOf(UI.Open("S"), "c"),
                "and the rest of the Screen still re-skins normally");
        }

        // Every project written before this feature: styles but no theme styles. Nothing may change.
        [Test]
        public void ColourOnlyThemes_LeaveClassBehaviourUntouched()
        {
            var screen = Load(@"
                <Style name='card' color='#112233'/>
                <Theme name='light'><Color name='ink' value='#000'/></Theme>
                <Theme name='dark'><Color name='ink' value='#fff'/></Theme>
                <Screen name='S'><Image id='c' class='card'/></Screen>");

            UI.Theme.Set("light");
            Assert.AreEqual("112233", ColorOf(screen, "c"));
            UI.Theme.Set("dark");
            Assert.AreEqual("112233", ColorOf(screen, "c"));
        }
    }
}
