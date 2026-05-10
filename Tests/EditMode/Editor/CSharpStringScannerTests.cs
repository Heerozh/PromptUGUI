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

        [Test]
        public void Scan_VerbatimString_ExtractsValue()
        {
            var src = "class X { void R() { var s = UI.Tr(@\"hello\nworld\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("hello\nworld", es.Msgid);
        }

        [Test]
        public void Scan_VerbatimStringWithDoubledQuotes_Unescaped()
        {
            var src = "class X { void R() { var s = UI.Tr(@\"say \"\"hi\"\"\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("say \"hi\"", es.Msgid);
        }

        [Test]
        public void Scan_RegularStringWithEscapedQuote_Unescaped()
        {
            var src = "class X { void R() { var s = UI.Tr(\"a\\\"b\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("a\"b", es.Msgid);
        }

        [Test]
        public void Scan_RegularStringWithEscapedBackslash_Unescaped()
        {
            var src = "class X { void R() { var s = UI.Tr(\"a\\\\b\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("a\\b", es.Msgid);
        }

        [Test]
        public void Scan_RegularStringWithCommonEscapes_Unescaped()
        {
            var src = "class X { void R() { var s = UI.Tr(\"line1\\nline2\\tend\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("line1\nline2\tend", es.Msgid);
        }

        [Test]
        public void Scan_InterpolatedString_Skipped()
        {
            var src = "class X { void R(string n) { var s = UI.Tr($\"hi {n}\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.IsEmpty(found);
        }

        [Test]
        public void Scan_StringContainingDoubleSlash_NotTreatedAsComment()
        {
            var src = "class X { void R() { var fake = \"// not a comment\"; var s = UI.Tr(\"real\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("real", found[0].Msgid);
            Assert.IsFalse(found[0].ExtractedComments.Any(c => c.Contains("not a comment")));
        }

        [Test]
        public void Scan_BlockCommentNotAddedToExtractedComments()
        {
            var src = @"
                class X {
                    void R() {
                        /* block comment above */
                        var s = UI.Tr(""x"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsFalse(es.ExtractedComments.Any(c => c.Contains("block comment")));
        }

        [Test]
        public void Scan_TwoLeadingLineComments_BothCaptured()
        {
            var src = @"
                class X {
                    void R() {
                        // first line
                        // second line
                        var s = UI.Tr(""x"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("first line")));
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("second line")));
        }

        [Test]
        public void Scan_PrecedingStatementSameLine_NoCommentLeak()
        {
            var src = @"
                class X {
                    void R() {
                        // attached to Foo
                        Foo();
                        var s = UI.Tr(""x"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsFalse(es.ExtractedComments.Any(c => c.Contains("attached to Foo")));
        }

        [Test]
        public void Scan_InsideIfBlock_KeywordNotReportedAsMethodName()
        {
            var src = "class X { void OuterMethod() { if (x) { var s = UI.Tr(\"y\"); } } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("OuterMethod")));
            Assert.IsFalse(es.ExtractedComments.Any(c => c.Contains("in if(")));
        }

        [Test]
        public void Scan_TmpRichText_AddsPreserveHint()
        {
            var src = "class X { void R() { var s = UI.Tr(\"<color=red>hi</color>\"); } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("rich text")));
        }

        [Test]
        public void Scan_NonTrInvocation_NotMatched()
        {
            var src = "class X { void R() { var s = UI.SomeOther(\"x\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.IsEmpty(found);
        }

        [Test]
        public void Scan_TwoCallsInOneFile_BothExtracted()
        {
            var src = "class X { void R() { UI.Tr(\"a\"); UI.Tr(\"b\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(2, found.Count);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, found.Select(e => e.Msgid).ToList());
        }
    }
}
