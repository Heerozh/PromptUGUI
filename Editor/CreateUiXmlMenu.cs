using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal static class CreateUiXmlMenu
    {
        private const string DefaultFilename = "NewUI.ui.xml";

        private const string Template =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<PromptUGUI version=""1"">

  <Screen name=""NewScreen"">
    <SafeArea>
      <Frame anchor=""stretch"">
        <Text anchor=""stretch"" align=""center"" fontSize=""99"" color=""blue"">PromptUGUI</Text>
      </Frame>
    </SafeArea>
  </Screen>

</PromptUGUI>
";

        [MenuItem("Assets/Create/PromptUGUI/UI XML", false, 81)]
        private static void CreateUiXml()
        {
#if UNITY_6000_6_OR_NEWER
              ProjectWindowUtil.CreateAssetWithTextContent(DefaultFilename, Template);
#else
            var icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                ScriptableObject.CreateInstance<DoCreateUiXml>(),
                DefaultFilename,
                icon,
                null);
#endif
        }

#if !UNITY_6000_6_OR_NEWER
        private sealed class DoCreateUiXml : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                File.WriteAllText(pathName, Template);
                AssetDatabase.ImportAsset(pathName);
                ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath<Object>(pathName));
            }
        }
#endif
    }
}
