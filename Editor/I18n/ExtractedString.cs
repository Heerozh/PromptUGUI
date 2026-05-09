using System.Collections.Generic;

namespace PromptUGUI.Editor.I18n
{
    /// <summary>
    /// One translatable string discovered by a scanner. Multiple ExtractedStrings with the same
    /// (msgid, msgctxt) get merged at write-time — comments/refs concatenated.
    /// </summary>
    internal sealed class ExtractedString
    {
        public string Msgid;
        public string Msgctxt;       // null = no explicit ctx
        public List<string> Comments = new();   // # ...
        public List<string> ExtractedComments = new();   // #. ...
        public List<string> References = new();          // #: file:line
        public string LocalePartition;       // e.g. "screens/MainMenu" or "_code"
    }
}
