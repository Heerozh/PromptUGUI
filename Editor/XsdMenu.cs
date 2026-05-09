using UnityEditor;
using PromptUGUI.Application;

namespace PromptUGUI.Editor {
    static class XsdMenu {
        [MenuItem("Tools/PromptUGUI/Schema/Generate XSD")]
        static void Run() {
            XsdGenerator.GenerateToFile(UI.Registry);
        }
    }
}
