using System;

namespace PromptUGUI.Template
{
    public sealed class TemplateException : Exception
    {
        public TemplateException(string message) : base(message) { }
    }
}
