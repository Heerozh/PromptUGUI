using System.Collections;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using UnityApplication = UnityEngine.Application;

namespace PromptUGUI.Tests.PlayMode
{
    public class MarkdownWebImageTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator WebResolver_loads_local_file_url_and_caches()
        {
            // write a tiny PNG to a temp file and load it via file:// (no network)
            var path = Path.Combine(UnityApplication.temporaryCachePath, "md_test.png");
            var src = new Texture2D(4, 4);
            File.WriteAllBytes(path, src.EncodeToPNG());
            Object.DestroyImmediate(src);
            var url = "file://" + path.Replace('\\', '/');

            UI.Markdown.UseWebImageResolver();

            var op = UI.Markdown.ImageResolver(url);
            var aw = op.GetAwaiter();
            while (!aw.IsCompleted) yield return null;
            var tex = aw.GetResult();
            Assert.IsNotNull(tex);

            // second call returns the cached instance
            var op2 = UI.Markdown.ImageResolver(url);
            var aw2 = op2.GetAwaiter();
            while (!aw2.IsCompleted) yield return null;
            Assert.AreSame(tex, aw2.GetResult());
        }
    }
}
