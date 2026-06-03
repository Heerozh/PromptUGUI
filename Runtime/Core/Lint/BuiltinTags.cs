using System.Collections.Generic;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// The set of built-in element tags the runtime registers (see
    /// <c>PromptUGUI.Application.BuiltinPrimitives</c>). The pure-C# lint layer can't reach
    /// the Unity-side <c>ControlRegistry</c>, so it mirrors the tag names here.
    ///
    /// Why it matters: from the UIXmlLint CLI's PRE-expansion view, any tag NOT in this set
    /// is a Template invocation whose body the CLI cannot see (<c>&lt;Import&gt;</c> is not
    /// resolved at lint time, and <c>TemplateExpander</c> never runs). Such a node MAY expand
    /// to a <c>&lt;Tab&gt;</c>, so rules like PUI-TABBAR-CHILD must not assert against it.
    ///
    /// Kept in sync with <c>BuiltinPrimitives</c> by <c>BuiltinTagsTests</c> — add new
    /// builtins here when you register them there, or the guard test fails.
    /// </summary>
    internal static class BuiltinTags
    {
        internal static readonly HashSet<string> All = new()
        {
            "Frame", "SafeArea", "Trigger", "Show", "Animation",
            "Image", "Icon", "Text", "VStack", "HStack", "Grid",
            "Btn", "Toggle", "Tab", "TabBar", "Slider", "Progress",
            "Dropdown", "ScrollList", "InputField",
        };

        internal static bool IsBuiltin(string tag) => All.Contains(tag);
    }
}
