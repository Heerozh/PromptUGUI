using UnityEditor;

namespace PromptUGUI.Editor
{
    internal static class CreateUiXmlMenu
    {
        private const string DefaultFilename = "NewUI.ui.xml";

        private const string Template =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<PromptUGUI version=""1"">

  <Screen name=""NewScreen"">
    <Frame anchor=""stretch""/>
  </Screen>

</PromptUGUI>
";

        [MenuItem("Assets/Create/PromptUGUI/UI XML", false, 81)]
        private static void CreateUiXml()
        {
            ProjectWindowUtil.CreateAssetWithTextContent(DefaultFilename, Template);
        }
    }
}
