using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor.ConsoleLog;

namespace PromptUGUI.Tests.Editor
{
    public class LlmLogFormatterTests
    {
        [Test]
        public void FormatEntry_TimestampTypeMessage_NoStack()
        {
            var e = new ConsoleLogEntry("23:39:28", "LOG", "hello world", "");
            Assert.AreEqual("[23:39:28] LOG hello world", LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void FormatEntry_EmptyTimestamp_OmitsBracket()
        {
            var e = new ConsoleLogEntry("", "LOG", "hi", "");
            Assert.AreEqual("LOG hi", LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void FormatEntry_StripsRichTextTags()
        {
            var e = new ConsoleLogEntry("23:39:28", "LOG", "[<color=#19aaff>HeTuClient</color>] <b>hi</b>", "");
            Assert.AreEqual("[23:39:28] LOG [HeTuClient] hi", LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void FormatEntry_TrimsStackToMaxFrames_AppendsMoreCount()
        {
            var cs = "f0\nf1\nf2\nf3\nf4\nf5\nf6"; // 7 frames
            var e = new ConsoleLogEntry("23:39:28", "ERROR", "boom", cs);
            var expected = "[23:39:28] ERROR boom\n  f0\n  f1\n  f2\n  f3\n  f4\n  ... (+2 more frames)";
            Assert.AreEqual(expected, LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void FormatEntry_StackWithinLimit_NoTruncationLine()
        {
            var e = new ConsoleLogEntry("23:39:28", "ERROR", "boom", "f0\nf1\nf2");
            Assert.AreEqual("[23:39:28] ERROR boom\n  f0\n  f1\n  f2", LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void FormatEntry_DropsBlankAndWhitespaceStackLines()
        {
            var e = new ConsoleLogEntry("23:39:28", "ERROR", "boom", "f0\n\n  f1  \n");
            Assert.AreEqual("[23:39:28] ERROR boom\n  f0\n  f1", LlmLogFormatter.FormatEntry(e, 5));
        }

        [Test]
        public void Format_JoinsEntriesWithBlankLine()
        {
            var e1 = new ConsoleLogEntry("23:39:28", "LOG", "a", "");
            var e2 = new ConsoleLogEntry("23:39:29", "ERROR", "b", "f0");
            var entries = new List<ConsoleLogEntry> { e1, e2 };
            var expected = LlmLogFormatter.FormatEntry(e1, 5) + "\n\n" + LlmLogFormatter.FormatEntry(e2, 5);
            Assert.AreEqual(expected, LlmLogFormatter.Format(entries, 5));
        }
    }
}
