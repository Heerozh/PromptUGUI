using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal static class CreateUiXmlMenu
    {
        private const string DefaultFilename = "NewUI.ui.xml";
        private const string XsdAssetPath = "Assets/PromptUGUI.gen.xsd";

        [MenuItem("Assets/Create/PromptUGUI/UI XML", false, 81)]
        private static void CreateUiXml()
        {
            var folder = ResolveSelectedFolder();
            var schemaRel = ComputeRelativePath(folder, XsdAssetPath);
            var content = BuildTemplate(schemaRel);
            ProjectWindowUtil.CreateAssetWithTextContent(DefaultFilename, content);
        }

        private static string ResolveSelectedFolder()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) return "Assets";
            return AssetDatabase.IsValidFolder(path)
                ? path
                : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        private static string ComputeRelativePath(string fromDir, string toFile)
        {
            var from = new Uri(Path.GetFullPath(fromDir) + Path.DirectorySeparatorChar);
            var to = new Uri(Path.GetFullPath(toFile));
            return Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString())
                     .Replace('\\', '/');
        }

        private static string BuildTemplate(string schemaRel) =>
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<PromptUGUI version=""1""
            xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
            xsi:noNamespaceSchemaLocation=""{schemaRel}"">

  <Screen name=""NewScreen"">
    <Frame anchor=""stretch""/>
  </Screen>

</PromptUGUI>
";
    }
}
