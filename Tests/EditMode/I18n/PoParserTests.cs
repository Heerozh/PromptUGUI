using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.I18n {
    public class PoParserTests {
        [Test] public void Parse_SingleEntry_ReturnsMsgidMsgstr() {
            var src = "msgid \"hello\"\nmsgstr \"你好\"\n";
            var entries = PoParser.Parse(src).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("hello", entries[0].Msgid);
            Assert.AreEqual("你好", entries[0].Msgstr);
            Assert.IsNull(entries[0].Msgctxt);
        }

        [Test] public void Parse_WithMsgctxt_PopulatesCtx() {
            var src = "msgctxt \"door\"\nmsgid \"Open\"\nmsgstr \"打开\"\n";
            var e = PoParser.Parse(src).Single();
            Assert.AreEqual("door", e.Msgctxt);
            Assert.AreEqual("Open", e.Msgid);
            Assert.AreEqual("打开", e.Msgstr);
        }

        [Test] public void Parse_MultilineString_ConcatenatesQuoted() {
            var src = "msgid \"\"\n\"line1 \"\n\"line2\"\nmsgstr \"\"\n\"out\"\n";
            var e = PoParser.Parse(src).Single();
            Assert.AreEqual("line1 line2", e.Msgid);
            Assert.AreEqual("out", e.Msgstr);
        }

        [Test] public void Parse_EscapeSequences_AreDecoded() {
            var src = "msgid \"a\\nb\\tc\\\"d\\\\e\"\nmsgstr \"\"\n";
            var e = PoParser.Parse(src).Single();
            Assert.AreEqual("a\nb\tc\"d\\e", e.Msgid);
        }

        [Test] public void Parse_TranslatorCommentsCollected() {
            var src = "# translator note\n#. extracted note\n#: file.cs:42\nmsgid \"x\"\nmsgstr \"\"\n";
            var e = PoParser.Parse(src).Single();
            CollectionAssert.Contains(e.TranslatorComments, "translator note");
        }

        [Test] public void Parse_ObsoleteEntries_AreSkipped() {
            var src = "#~ msgid \"old\"\n#~ msgstr \"old-tr\"\nmsgid \"new\"\nmsgstr \"new-tr\"\n";
            var entries = PoParser.Parse(src).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("new", entries[0].Msgid);
        }

        [Test] public void Parse_HeaderEntry_IsReturnedLikeOthers() {
            // Header is the entry with empty msgid; we still emit it.
            var src = "msgid \"\"\nmsgstr \"Content-Type: text/plain\\n\"\nmsgid \"x\"\nmsgstr \"y\"\n";
            var entries = PoParser.Parse(src).ToList();
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("", entries[0].Msgid);
        }

        [Test] public void Parse_EmptyOrWhitespace_ReturnsEmpty() {
            CollectionAssert.IsEmpty(PoParser.Parse("").ToList());
            CollectionAssert.IsEmpty(PoParser.Parse("\n\n  \n").ToList());
        }

        [Test] public void Parse_MissingMsgstr_Throws() {
            Assert.Throws<PoParseException>(() => PoParser.Parse("msgid \"x\"\n").ToList());
        }

        [Test] public void Roundtrip_BasicEntries_PreservesContent() {
            var src = new[] {
                new PoEntry { Msgid = "hello", Msgstr = "你好" },
                new PoEntry { Msgctxt = "door", Msgid = "Open", Msgstr = "打开",
                              TranslatorComments = { "doorway action" } },
            };
            var text = PoParser.Serialize(src);
            var parsed = PoParser.Parse(text).ToList();
            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("hello", parsed[0].Msgid);
            Assert.AreEqual("你好",  parsed[0].Msgstr);
            Assert.AreEqual("door",  parsed[1].Msgctxt);
            Assert.AreEqual("Open",  parsed[1].Msgid);
            Assert.AreEqual("打开",  parsed[1].Msgstr);
        }

        [Test] public void Serialize_EscapesNewlinesAndQuotes() {
            var entries = new[] {
                new PoEntry { Msgid = "line\nwith \"quotes\"", Msgstr = "" }
            };
            var text = PoParser.Serialize(entries);
            StringAssert.Contains("\\n", text);
            StringAssert.Contains("\\\"", text);
        }
    }
}
