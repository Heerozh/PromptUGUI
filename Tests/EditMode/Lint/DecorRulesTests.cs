using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// What the CLI has to say about a <c>&lt;Decor&gt;</c> before Unity ever opens it. Two of these
    /// catch an omission the runtime can only answer by drawing nothing (no <c>kind</c>, no
    /// <c>sprite</c>), one catches attributes that belong to the node's layout rather than the
    /// decoration, and one catches attributes the chosen kind has no use for.
    /// </summary>
    public class DecorRulesTests
    {
        private static UIDocument Parse(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Frame id='host'>{innerXml}</Frame></Screen></PromptUGUI>";
            return UIDocumentParser.Parse(xml);
        }

        private static string[] Codes(string innerXml)
            => IRWalker.Walk(Parse(innerXml)).Select(i => i.Code).ToArray();

        private static string Message(string innerXml, string code)
            => IRWalker.Walk(Parse(innerXml)).First(i => i.Code == code).Message;

        // ---- missing kind ----

        [Test]
        public void NoKind_IsReported()
        {
            CollectionAssert.Contains(Codes("<Decor id='d' color='red'/>"), DecorRules.KindCode);
        }

        [Test]
        public void KindNone_IsNotReported()
        {
            // "none" is the theme's way to take a decoration away — an answer, not an omission.
            CollectionAssert.DoesNotContain(Codes("<Decor id='d' kind='none'/>"), DecorRules.KindCode);
        }

        // ---- sprite kind without a sprite ----

        [Test]
        public void SpriteKind_WithoutSprite_IsReported()
        {
            CollectionAssert.Contains(Codes("<Decor id='d' kind='sprite'/>"), DecorRules.SpriteCode);
        }

        [Test]
        public void SpriteKind_WithSprite_IsClean()
        {
            CollectionAssert.DoesNotContain(
                Codes("<Decor id='d' kind='sprite' sprite='ui:corner'/>"), DecorRules.SpriteCode);
        }

        // ---- layout attributes ----

        [TestCase("anchor='center'")]
        [TestCase("width='40'")]
        [TestCase("height='40'")]
        [TestCase("size='40'")]
        [TestCase("margin='4'")]
        [TestCase("flow='false'")]
        public void LayoutAttributes_AreReported(string attr)
        {
            var codes = Codes($"<Decor id='d' kind='bracket' {attr}/>");
            CollectionAssert.Contains(codes, DecorRules.LayoutAttrCode);
        }

        [Test]
        public void LayoutAttrMessage_PointsAtTheAttributesThatDoPlaceIt()
        {
            var msg = Message("<Decor id='d' kind='bracket' anchor='center'/>",
                              DecorRules.LayoutAttrCode);
            StringAssert.Contains("at", msg);
            StringAssert.Contains("inset", msg);
        }

        [Test]
        public void SizeMessage_NamesExtent_BecauseTheNamesCollide()
        {
            // size= is a common layout attribute, so it never reaches Decor's own setter — the
            // decoration's own dimension is extent=. Being told exactly that is the whole point.
            var msg = Message("<Decor id='d' kind='bracket' size='40'/>", DecorRules.LayoutAttrCode);
            StringAssert.Contains("extent", msg);
        }

        // ---- attributes the kind has no use for ----

        [TestCase("kind='tick' thickness='2'")]
        [TestCase("kind='bracket' offset='4'")]
        [TestCase("kind='sprite' sprite='ui:x' glow='6'")]
        [TestCase("kind='sprite' sprite='ui:x' thickness='2'")]
        [TestCase("kind='bracket' sprite='ui:x'")]
        [TestCase("kind='line' mirror='false'")]
        public void InapplicableAttributes_AreReported(string attrs)
        {
            CollectionAssert.Contains(Codes($"<Decor id='d' {attrs}/>"), DecorRules.AttrCode);
        }

        [TestCase("kind='bracket' thickness='2' inset='4'")]
        [TestCase("kind='line' at='top' extent='50%' thickness='1'")]
        [TestCase("kind='tick' at='bottom' offset='12' glow='4'")]
        [TestCase("kind='sprite' sprite='ui:x' mirror='false' inset='2'")]
        public void LegitimateCombinations_AreClean(string attrs)
        {
            CollectionAssert.DoesNotContain(Codes($"<Decor id='d' {attrs}/>"), DecorRules.AttrCode);
        }

        // ---- value syntax and cross-attribute grammar ----

        [Test]
        public void UnknownKind_IsReported()
        {
            CollectionAssert.Contains(Codes("<Decor id='d' kind='sparkle'/>"), DecorRules.ValueCode);
        }

        [Test]
        public void BracketOnAnEdge_IsReported()
        {
            CollectionAssert.Contains(Codes("<Decor id='d' kind='bracket' at='bottom'/>"),
                                      DecorRules.ValueCode);
        }

        [Test]
        public void PercentOutsideLine_IsReported()
        {
            CollectionAssert.Contains(Codes("<Decor id='d' kind='tick' extent='50%'/>"),
                                      DecorRules.ValueCode);
        }

        // ---- the procedural-attribute tier keeps its hands off what Decor really supports ----

        [Test]
        public void GlowOnDecor_IsNotReportedAsSilentlyIgnored()
        {
            CollectionAssert.DoesNotContain(
                Codes("<Decor id='d' kind='bracket' glow='6' glowColor='white'/>"),
                PureContainerVisualAttrRules.VisualAttrCode);
        }

        [Test]
        public void RadiusOnDecor_IsStillReported()
        {
            // A decoration's shape comes from kind=; radius has nowhere to land here.
            CollectionAssert.Contains(
                Codes("<Decor id='d' kind='bracket' radius='8'/>"),
                PureContainerVisualAttrRules.VisualAttrCode);
        }

        [Test]
        public void InnerGlowOnDecor_IsStillReported()
        {
            // A 2px stroke has no inside to light. Only the outer glow pair carries over to a
            // decoration; the inner one belongs to a surface.
            CollectionAssert.Contains(
                Codes("<Decor id='d' kind='bracket' innerGlow='6'/>"),
                PureContainerVisualAttrRules.VisualAttrCode);
        }

        [Test]
        public void CleanDecor_HasNoIssues()
        {
            Assert.IsEmpty(IRWalker.Walk(Parse(
                "<Decor id='d' kind='bracket' at='top-left,top-right' extent='14' " +
                "thickness='2' color='white' glow='6' inset='2'/>")));
        }
    }
}
