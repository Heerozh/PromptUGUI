using System.Collections.Generic;

namespace PromptUGUI.I18n
{
    public sealed class PoEntry
    {
        public string Msgctxt;        // null = no ctx
        public string Msgid;
        public string Msgstr;         // "" = untranslated
        public List<string> TranslatorComments = new();
    }

    public sealed class PoParseException : System.Exception
    {
        public PoParseException(string msg) : base(msg) { }
    }
}
