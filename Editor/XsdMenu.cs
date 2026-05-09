using PromptUGUI.Application;
using UnityEditor;

namespace PromptUGUI.Editor
{
    internal static class XsdMenu
    {
        [MenuItem("Tools/PromptUGUI/Schema/Generate XSD")]
        private static void Run()
        {
            XsdGenerator.GenerateToFile(UI.Registry);
        }
    }
}
