using System;

namespace PromptUGUI.Template
{
    public sealed class TemplateException : Exception
    {
        public TemplateException(string message) : base(message) { }
        public TemplateException(string message, Exception inner) : base(message, inner) { }
    }
}
