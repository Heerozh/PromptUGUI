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
        private const string DefaultThemeFilename = "NewTheme.ui.xml";
        private const string DefaultPxlFilename = "NewSprite.pxl";

        // Prepended to every generated .ui.xml so an AI/LLM editing the file knows
        // to load the authoring skill first. const string concat keeps it DRY.
        private const string XmlHeader =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<!--
  PromptUGUI's `.ui.xml` File — You can use `authoring-promptugui-xml` skill to learn how to edit it.
-->
";

        private const string ScreenContent = XmlHeader +
@"<PromptUGUI version=""1"">

  <Screen name=""NewScreen"">
    <SafeArea>
      <Frame anchor=""stretch"">
        <Text anchor=""stretch"" align=""center"" fontSize=""99"" color=""blue"">PromptUGUI</Text>
      </Frame>
    </SafeArea>
  </Screen>

</PromptUGUI>
";

        private const string TemplateContent = XmlHeader +
@"<PromptUGUI version=""1"">

  <Template name=""NewTemplate"">
    <Param name=""label"" default=""""/>
    <Frame>
      <Text anchor=""stretch"" align=""center"">Label: {{label}}</Text>
    </Frame>
  </Template>

</PromptUGUI>
";

        private const string ThemeContent = XmlHeader +
@"<PromptUGUI version=""1"">

  <Theme name=""light"">
    <Color name=""primary""   value=""#ff8800""/>
    <Color name=""secondary"" value=""#0080ff""/>
    <Color name=""label-fg""  value=""#222222""/>
    <Color name=""bg""        value=""#f5f5f5""/>
  </Theme>

  <Theme name=""dark"" base=""light"">
    <Color name=""primary""  value=""#cc6600""/>
    <Color name=""label-fg"" value=""#e6e6e6""/>
    <Color name=""bg""       value=""#1a1a1a""/>
  </Theme>

</PromptUGUI>
";

        // A ready-to-import starter sprite: a small two-tone heart icon. `.pxl` comments
        // start with '#'; the header points editors at the authoring skill (mirrors XmlHeader).
        // Grid rows are trimmed at parse time, so the 2-space indent is purely cosmetic.
        internal const string PxlContent =
@"# PromptUGUI .pxl pixel sprite — use the `authoring-promptugui-pxl` skill to learn how to edit it.
ppu: 100
chars:
  R: #E84A3F
  r: #F47C6A

grid:
  .RR...RR.
  RrrRRRRRR
  RRRRRRRRR
  .RRRRRRR.
  ..RRRRR..
  ...RRR...
  ....R....
";

        [MenuItem("Assets/Create/PromptUGUI/UI XML", false, 81)]
        private static void CreateUiXml() => Create(DefaultScreenFilename, ScreenContent);

        [MenuItem("Assets/Create/PromptUGUI/UI Template", false, 82)]
        private static void CreateUiTemplate() => Create(DefaultTemplateFilename, TemplateContent);

        [MenuItem("Assets/Create/PromptUGUI/UI Theme", false, 83)]
        private static void CreateUiTheme() => Create(DefaultThemeFilename, ThemeContent);

        [MenuItem("Assets/Create/PromptUGUI/Pxl Sprite", false, 84)]
        private static void CreatePxl() => Create(DefaultPxlFilename, PxlContent);

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
