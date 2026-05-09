using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor
{
    public class CSharpStringScannerTests
    {
        [Test]
        public void Scan_PlainTrLiteral_Extracted()
        {
            var src = @"
                using PromptUGUI.Application;
                class X {
                    void Run() {
                        var s = UI.Tr(""hello"");
                    }
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("hello", found[0].Msgid);
            Assert.IsNull(found[0].Msgctxt);
        }

        [Test]
        public void Scan_TrWithCtx_PopulatesMsgctxt()
        {
            var src = @"
                class X { void Run() { var s = PromptUGUI.Application.UI.Tr(""Open"", ctx: ""door""); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("Open", found.Msgid);
            Assert.AreEqual("door", found.Msgctxt);
        }

        [Test]
        public void Scan_NonLiteralArgs_SkippedWithWarning()
        {
            var src = @"
                class X { void Run(string x) { var s = PromptUGUI.Application.UI.Tr(x); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.IsEmpty(found);
        }

        [Test]
        public void Scan_RefIncludesFileAndLine()
        {
            var src = "class X { void R() { var s = PromptUGUI.Application.UI.Tr(\"x\"); } }";
            var es = CSharpStringScanner.Scan(src, "Path/To/X.cs").Single();
            Assert.IsTrue(es.References.Any(r => r.Contains("Path/To/X.cs")));
        }

        [Test]
        public void Scan_LeadingComment_AddedToExtractedComments()
        {
            var src = @"
                class X {
                    void R() {
                        // 提示玩家当前金币
                        var s = PromptUGUI.Application.UI.Tr(""总价: {0:C}"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("提示玩家当前金币")));
        }

        [Test]
        public void Scan_MethodNameIncludedInExtractedComments()
        {
            var src = "class X { void OnGoldChanged() { var s = PromptUGUI.Application.UI.Tr(\"x\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("OnGoldChanged")));
        }
    }
}
