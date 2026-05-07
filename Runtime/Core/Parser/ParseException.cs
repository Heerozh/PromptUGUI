using System;

namespace PromptUGUI.Parser {
    public sealed class ParseException : Exception {
        public ParseException(string message) : base(message) { }
    }
}
