using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal static class CreateUiXmlMenu
    {
        private const string DefaultScreenFilename = "NewUI.ui.xml";
        private const string DefaultTemplateFilename = "NewTemplate.ui.xml";

        private const string ScreenContent =
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

        private const string TemplateContent =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<PromptUGUI version=""1"">

  <Template name=""NewTemplate"">
    <Param name=""label"" default=""""/>
    <Frame>
      <Text anchor=""stretch"" align=""center"">Label: {{label}}</Text>
    </Frame>
  </Template>

</PromptUGUI>
";

        [MenuItem("Assets/Create/PromptUGUI/UI XML", false, 81)]
        private static void CreateUiXml() => Create(DefaultScreenFilename, ScreenContent);

        [MenuItem("Assets/Create/PromptUGUI/UI Template", false, 82)]
        private static void CreateUiTemplate() => Create(DefaultTemplateFilename, TemplateContent);

        private static void Create(string filename, string content)
        {
#if UNITY_6000_6_OR_NEWER
            ProjectWindowUtil.CreateAssetWithTextContent(filename, content);
#else
            var icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;
            var action = ScriptableObject.CreateInstance<DoCreateUiXml>();
            action.Content = content;
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                action,
                filename,
                icon,
                null);
#endif
        }

#if !UNITY_6000_6_OR_NEWER
        private sealed class DoCreateUiXml : EndNameEditAction
        {
            public string Content;
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                File.WriteAllText(pathName, Content);
                AssetDatabase.ImportAsset(pathName);
                ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath<Object>(pathName));
            }
        }
#endif
    }
}
