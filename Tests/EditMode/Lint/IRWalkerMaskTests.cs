using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class IRWalkerMaskTests
    {
        [Test]
        public void Walk_DispatchesFrameMaskRulesOnRootAndDescendants()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='root' mask='self'>
      <Frame id='inner' mask='circle'/>
    </Frame>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            // root: mask=self → FRAME-SELF; inner: mask=circle → VALUE
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "root"));
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.ValueCode && i.Id == "inner"));
        }

        [Test]
        public void Walk_DispatchesImageMaskRules()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image id='bad' mask='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.SelfNoSpriteCode && i.Id == "bad"));
        }

        [Test]
        public void Walk_NonFrameNonImageTags_NoMaskIssue()
        {
            // <VStack mask="rect"> 不该触发 mask rule（VStack 是纯容器，没有 mask 属性）。
            // 注意：暴露 mask 的不止 Frame/Image —— RawImage / Progress 也有，见下面的覆盖缺口测试。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack id='v' mask='rect'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).Where(i =>
                i.Code.StartsWith("PUI-MASK-")).ToList();
            Assert.IsEmpty(issues);
        }

        /// <summary>
        /// M0 of docs~/superpowers/specs/2026-08-26-theme-driven-style-design.md §8.
        ///
        /// <para><c>&lt;RawImage&gt;</c> exposes the whole mask family (RawImage.cs:101 / :121 / :133)
        /// with the same add-only <c>AddComponent</c> implementation Frame and Image have — but it
        /// falls through BOTH guards: <c>IRWalker</c> dispatches <c>MaskAttributeRules</c> only for
        /// <c>Frame</c> and <c>Image</c>, and <c>VariantBaseRules</c> deliberately skips the mask
        /// family because <c>PUI-MASK-VARIANT</c> is supposed to own it "in ALL cases". So a
        /// per-variant mask switch on a RawImage is silently accepted and silently broken.</para>
        ///
        /// <para>Ignored red test: the fix is to dispatch the mask rules for <c>RawImage</c> too
        /// (and to decide whether <c>Progress</c>'s <c>showMask</c> / <c>maskPadding</c> need the
        /// same, since PUI-PROG-MASK-VARIANT only covers <c>mask</c>).</para>
        /// </summary>
        [Test]
        [Ignore("Red: spec 2026-08-26 §8 — RawImage mask variants reach neither PUI-MASK-VARIANT nor PUI-VARIANT-NO-BASE. Un-ignore with the fix.")]
        public void Walk_RawImageMaskInVariantOverride_VariantIssue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <RawImage id='r' mask='rect' mask.alt='self'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.VariantCode && i.Id == "r"),
                "switching a RawImage's mask mode per Variant is as unsupported as it is on Frame/Image, "
                + "but no rule currently reports it");
        }
    }
}
