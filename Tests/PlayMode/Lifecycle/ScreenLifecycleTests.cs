using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.TestTools;
using PromptImage = PromptUGUI.Controls.Image;
using PromptText = PromptUGUI.Controls.Text;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.Lifecycle {
    public class ScreenInstantiatorTests {

        ControlRegistry _reg;

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
            _reg = UI.Registry;
        }

        [UnityTest]
        public IEnumerator Instantiates_image_with_anchor_and_size() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='top-right' size='240x80' margin='16'/>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]).Root;

            Assert.IsNotNull(rootGo);
            var imgGo = rootGo.transform.GetChild(0).gameObject;
            Assert.AreEqual("bg", imgGo.name);
            var rt = imgGo.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(240, 80), rt.sizeDelta);
            Assert.AreEqual(new Vector2(-16, -16), rt.anchoredPosition);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Recursive_children_are_parented_correctly() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <VStack id='root' anchor='center' size='400x300'>
                        <Image id='a'/>
                        <Image id='b'/>
                    </VStack>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]).Root;

            var vstackGo = rootGo.transform.GetChild(0).gameObject;
            Assert.AreEqual("root", vstackGo.name);
            Assert.AreEqual(2, vstackGo.transform.childCount);
            Assert.AreEqual("a", vstackGo.transform.GetChild(0).name);
            Assert.AreEqual("b", vstackGo.transform.GetChild(1).name);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Text_content_shorthand_applies_to_default_text_attr() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Text>Hello</Text>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var rootGo = inst.Instantiate(doc.Screens[0]).Root;

            var txt = rootGo.transform.GetChild(0).GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual("Hello", txt.text);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Screen_creates_canvas_and_can_get_by_id() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='stretch'/>
                    <Text id='hello'>Hi</Text>
                </Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg);
            var screen = new PromptScreen(doc.Screens[0], inst);

            screen.Open();

            Assert.IsNotNull(screen.RootGameObject.GetComponent<Canvas>());

            var bg = screen.Get<PromptImage>("bg");
            Assert.IsNotNull(bg);
            var hello = screen.Get<PromptText>("hello");
            Assert.IsNotNull(hello);

            screen.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Screen_get_unknown_id_throws() {
            const string xml = @"<UI version='1'>
                <Screen name='UnknownIdTest'><Image id='only'/></Screen></UI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = new PromptScreen(doc.Screens[0], new ScreenInstantiator(_reg));
            screen.Open();

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get<PromptImage>("nope"));

            screen.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator UI_open_returns_screen_and_close_destroys() {
            UI.LoadDocument("test_doc", @"<UI version='1'>
                <Screen name='UIFacade'>
                    <Image id='bg' anchor='stretch'/>
                </Screen></UI>");

            var screen = UI.Open("UIFacade");
            Assert.IsNotNull(screen);
            Assert.IsNotNull(screen.RootGameObject);

            UI.Close("UIFacade");
            yield return null;
            Assert.IsTrue(UI.Get("UIFacade") == null);
        }

        sealed class TrackingDisposable : System.IDisposable {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        [UnityTest]
        public IEnumerator AddTo_screen_disposes_on_close() {
            UI.LoadDocument("addto_doc", @"<UI version='1'>
                <Screen name='AddToTest'><Image id='bg'/></Screen></UI>");
            var screen = UI.Open("AddToTest");

            var d = new TrackingDisposable();
            d.AddTo(screen);

            UI.Close("AddToTest");
            yield return null;

            Assert.IsTrue(d.Disposed);
        }

        [UnityTest]
        public IEnumerator Path_Get_walks_template_scope() {
            UI.LoadDocument("path_doc", @"<UI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inside'/>
                    </Frame>
                </Template>
                <Screen name='PathTest'>
                    <Box id='outer'/>
                </Screen></UI>");

            var screen = UI.Open("PathTest");

            // 顶层 id 仍工作（模板根透传后是 Frame）
            var outer = screen.Get<PromptUGUI.Controls.Frame>("outer");
            Assert.IsNotNull(outer);

            // 模板内 id 通过路径访问
            var inside = screen.Get<PromptUGUI.Controls.Image>("outer/inside");
            Assert.IsNotNull(inside);

            UI.Close("PathTest");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Path_Get_throws_on_unknown_segment() {
            UI.LoadDocument("path_doc2", @"<UI version='1'>
                <Template name='Box'>
                    <Frame><Image id='inside'/></Frame>
                </Template>
                <Screen name='PathTest2'>
                    <Box id='outer'/>
                </Screen></UI>");

            var screen = UI.Open("PathTest2");
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("outer/nope"));

            UI.Close("PathTest2");
            yield return null;
        }
    }
}
