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
        public void Scan_TrInsideInterpolationHole_Extracted()
        {
            var src = @"
                class X {
                    void R(System.Exception e) {
                        var s = $""{UI.Tr(""下载星图失败:"")}\n{e.Message}"";
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("下载星图失败:", es.Msgid);
            Assert.IsTrue(es.ExtractedComments.Any(c => c.Contains("R")));
        }

        [Test]
        public void Scan_TwoTrCallsInOneInterpolatedString_BothExtracted()
        {
            var src = "class X { void R(int n) { var s = $\"{UI.Tr(\"a\")} {n} {UI.Tr(\"b\", ctx: \"c\")}\"; } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, found.Select(e => e.Msgid).ToList());
            Assert.AreEqual("c", found.Single(e => e.Msgid == "b").Msgctxt);
        }

        [Test]
        public void Scan_TrInHoleOfVerbatimInterpolatedString_Extracted()
        {
            var src = "class X { void R() { var s = $@\"[{UI.Tr(\"x\")}]\"; } }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.AreEqual("x", es.Msgid);
        }

        [Test]
        public void Scan_TrInNestedInterpolationHole_Extracted()
        {
            var src = "class X { void R(int n) { var s = $\"{(n > 0 ? $\"{UI.Tr(\"inner\")}\" : \"\")}\"; } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("inner", found[0].Msgid);
        }

        [Test]
        public void Scan_BraceInsideHoleStringLiteral_DoesNotEndHoleEarly()
        {
            var src = "class X { void R() { var s = $\"{UI.Tr(\"a}b\")}\"; var t = UI.Tr(\"after\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            CollectionAssert.AreEquivalent(new[] { "a}b", "after" }, found.Select(e => e.Msgid).ToList());
        }

        [Test]
        public void Scan_EscapedBracesInInterpolatedString_NotTreatedAsHole()
        {
            var src = "class X { void R() { var s = $\"{{ not a hole }}\"; var t = UI.Tr(\"after\"); } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("after", found[0].Msgid);
        }

        [Test]
        public void Scan_FormatSpecifierInHole_NotExtracted()
        {
            var src = "class X { void R(float v) { var s = $\"{v:F2} {UI.Tr(\"unit\")}\"; } }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("unit", found[0].Msgid);
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
        public void Scan_SwitchExpressionArms_CommentStaysWithItsArm()
        {
            var src = @"
                class X {
                    static string Row(string k) => k switch
                    {
                        // greetings
                        ""hi"" => UI.Tr(""Hello""),
                        ""bye"" => UI.Tr(""Goodbye""),
                        // a playing card, not a currency
                        ""ace"" => UI.Tr(""Ace""),
                        _ => k,
                    };
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToDictionary(e => e.Msgid);
            Assert.IsTrue(found["Hello"].ExtractedComments.Contains("greetings"));
            Assert.IsFalse(found["Goodbye"].ExtractedComments.Any(c => c.Contains("greetings")));
            Assert.IsTrue(found["Ace"].ExtractedComments.Contains("a playing card, not a currency"));
            Assert.IsFalse(found["Ace"].ExtractedComments.Any(c => c.Contains("greetings")));
        }

        [Test]
        public void Scan_CollectionInitializer_CommentStaysWithItsElement()
        {
            var src = @"
                class X {
                    void R() {
                        var names = new[] {
                            // first one
                            UI.Tr(""a""),
                            UI.Tr(""b""),
                        };
                    }
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToDictionary(e => e.Msgid);
            Assert.IsTrue(found["a"].ExtractedComments.Contains("first one"));
            Assert.IsFalse(found["b"].ExtractedComments.Any(c => c.Contains("first one")));
        }

        [Test]
        public void Scan_CommentAboveEarlierArgument_NotAttachedToLaterArgument()
        {
            var src = @"
                class X {
                    void R() {
                        // dialog
                        Show(
                            // the title
                            UI.Tr(""a""),
                            UI.Tr(""b""));
                    }
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToDictionary(e => e.Msgid);
            CollectionAssert.IsSubsetOf(new[] { "dialog", "the title" }, found["a"].ExtractedComments);
            Assert.IsTrue(found["b"].ExtractedComments.Contains("dialog"));
            Assert.IsFalse(found["b"].ExtractedComments.Any(c => c.Contains("the title")));
        }

        [Test]
        public void Scan_CommentInsideEarlierArgument_NotAttached()
        {
            var src = @"
                class X {
                    void R() {
                        Show(Pick(
                            // which one
                            1), UI.Tr(""b""));
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsFalse(es.ExtractedComments.Any(c => c.Contains("which one")));
        }

        [Test]
        public void Scan_CommentAboveStatement_ReachesTrInLaterArgument()
        {
            var src = @"
                class X {
                    void R() {
                        // delete confirmation
                        MessageBox.Open(this, UI.Tr(""Delete save?""));
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Contains("delete confirmation"));
        }

        [Test]
        public void Scan_CommaInGenericArguments_DoesNotHideStatementComment()
        {
            var src = @"
                class X {
                    void R() {
                        // fallback label
                        Func<int, string> f = n => UI.Tr(""x"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsTrue(es.ExtractedComments.Contains("fallback label"));
        }

        [Test]
        public void Scan_TrailingComment_BelongsToTrOnItsLine()
        {
            var src = @"
                class X {
                    static string Row(string k) => k switch
                    {
                        ""up"" => UI.Tr(""Up""), // arrow key
                        ""down"" => UI.Tr(""Down""),
                        _ => k,
                    };
                    void R() {
                        var a = UI.Tr(""a""); // about a
                        var b = UI.Tr(""b"");
                    }
                }";
            var found = CSharpStringScanner.Scan(src, "X.cs").ToDictionary(e => e.Msgid);
            Assert.IsTrue(found["Up"].ExtractedComments.Contains("arrow key"));
            Assert.IsFalse(found["Down"].ExtractedComments.Any(c => c.Contains("arrow key")));
            Assert.IsTrue(found["a"].ExtractedComments.Contains("about a"));
            Assert.IsFalse(found["b"].ExtractedComments.Any(c => c.Contains("about a")));
        }

        [Test]
        public void Scan_TrailingComment_OnLineWithoutTr_NotAttached()
        {
            var src = @"
                class X {
                    void R() {
                        Foo(); // about Foo
                        var s = UI.Tr(""x"");
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            Assert.IsFalse(es.ExtractedComments.Any(c => c.Contains("about Foo")));
        }

        [Test]
        public void Scan_TrailingComment_FollowsLeadingComments()
        {
            var src = @"
                class X {
                    void R() {
                        // above
                        var s = UI.Tr(
                            ""x""); // after
                    }
                }";
            var es = CSharpStringScanner.Scan(src, "X.cs").Single();
            var above = es.ExtractedComments.IndexOf("above");
            var after = es.ExtractedComments.IndexOf("after");
            Assert.That(above, Is.GreaterThanOrEqualTo(0));
            Assert.That(after, Is.GreaterThan(above));
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
