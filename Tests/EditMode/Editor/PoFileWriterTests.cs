using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;
using PromptUGUI.I18n;

namespace PromptUGUI.Tests.Editor
{
    public class PoFileWriterTests
    {
        [Test]
        public void Merge_NewMsgid_AddedWithEmptyMsgstr()
        {
            var existing = "";   // no prior file
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "hello" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("hello", entries[0].Msgid);
            Assert.AreEqual("", entries[0].Msgstr);
        }

        [Test]
        public void Merge_ExistingNonEmptyMsgstr_PreservedAcrossExtract()
        {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "hello", Msgstr = "你好" },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "hello" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual("你好", entries[0].Msgstr);
        }

        [Test]
        public void Merge_ExtractedDoesNotIncludeOldMsgid_RemovesIt()
        {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "old", Msgstr = "obsolete-tr" },
                new PoEntry { Msgid = "kept", Msgstr = "tr" },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "kept" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("kept", entries[0].Msgid);
        }

        [Test]
        public void Merge_NewExtractionRefreshesComments()
        {
            var existing = PoParser.Serialize(new[] {
                new PoEntry { Msgid = "x", Msgstr = "y",
                              TranslatorComments = new() { "old comment" } },
            });
            var result = PoFileWriter.Merge(existing, new[] {
                new ExtractedString { Msgid = "x", ExtractedComments = { "fresh hint" } },
            });
            var entries = PoParser.Parse(result).ToList();
            // existing user msgstr preserved; comments come from current extraction
            Assert.AreEqual("y", entries[0].Msgstr);
            Assert.IsTrue(entries[0].TranslatorComments.Any(c => c.Contains("fresh hint")));
            Assert.IsFalse(entries[0].TranslatorComments.Any(c => c.Contains("old comment")));
        }

        [Test]
        public void Merge_SameMsgidDifferentCtx_TwoEntries()
        {
            var result = PoFileWriter.Merge("", new[] {
                new ExtractedString { Msgid = "Open" },
                new ExtractedString { Msgid = "Open", Msgctxt = "door" },
            });
            var entries = PoParser.Parse(result).ToList();
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.Any(e => e.Msgctxt == null));
            Assert.IsTrue(entries.Any(e => e.Msgctxt == "door"));
        }
    }
}
