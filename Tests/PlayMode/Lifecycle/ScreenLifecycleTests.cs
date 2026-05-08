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
using PromptGrid = PromptUGUI.Controls.Grid;
using PromptVStack = PromptUGUI.Controls.VStack;

namespace PromptUGUI.Tests.Lifecycle {
    public class ScreenInstantiatorTests {

        ControlRegistry _reg;
        VariantStore _store;

        [SetUp] public void SetUp() {
            UI.ResetForTests();
            BuiltinPrimitives.Register(UI.Registry);
            _reg = UI.Registry;
            _store = UI.VariantStore;
        }

        [UnityTest]
        public IEnumerator Instantiates_image_with_anchor_and_size() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='top-right' size='240x80' margin='16'/>
                </Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg, _store);
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
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='root' anchor='center' size='400x300'>
                        <Image id='a'/>
                        <Image id='b'/>
                    </VStack>
                </Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg, _store);
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
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Text>Hello</Text>
                </Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg, _store);
            var rootGo = inst.Instantiate(doc.Screens[0]).Root;

            var txt = rootGo.transform.GetChild(0).GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual("Hello", txt.text);

            Object.Destroy(rootGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Screen_creates_canvas_and_can_get_by_id() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Image id='bg' anchor='stretch'/>
                    <Text id='hello'>Hi</Text>
                </Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var inst = new ScreenInstantiator(_reg, _store);
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
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='UnknownIdTest'><Image id='only'/></Screen></PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = new PromptScreen(doc.Screens[0], new ScreenInstantiator(_reg, _store));
            screen.Open();

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get<PromptImage>("nope"));

            screen.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator UI_open_returns_screen_and_close_destroys() {
            UI.LoadDocument("test_doc", @"<PromptUGUI version='1'>
                <Screen name='UIFacade'>
                    <Image id='bg' anchor='stretch'/>
                </Screen></PromptUGUI>");

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
            UI.LoadDocument("addto_doc", @"<PromptUGUI version='1'>
                <Screen name='AddToTest'><Image id='bg'/></Screen></PromptUGUI>");
            var screen = UI.Open("AddToTest");

            var d = new TrackingDisposable();
            d.AddTo(screen);

            UI.Close("AddToTest");
            yield return null;

            Assert.IsTrue(d.Disposed);
        }

        [UnityTest]
        public IEnumerator Path_Get_walks_template_scope() {
            UI.LoadDocument("path_doc", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inside'/>
                    </Frame>
                </Template>
                <Screen name='PathTest'>
                    <Box id='outer'/>
                </Screen></PromptUGUI>");

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
            UI.LoadDocument("path_doc2", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame><Image id='inside'/></Frame>
                </Template>
                <Screen name='PathTest2'>
                    <Box id='outer'/>
                </Screen></PromptUGUI>");

            var screen = UI.Open("PathTest2");
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("outer/nope"));

            UI.Close("PathTest2");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_attribute_override_applies_when_variant_active_at_open() {
            UI.LoadDocument("v1", @"<PromptUGUI version='1'>
                <Screen name='V1'>
                    <Image id='bg' anchor='top-left' size='100x50'
                           anchor.mobile='top-right' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            UI.Variants.Set("mobile", true);
            var screen = UI.Open("V1");

            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(200, 100), rt.sizeDelta);
            // anchor=top-right → anchorMin/Max=(1,1)
            Assert.AreEqual(new Vector2(1, 1), rt.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), rt.anchorMax);

            UI.Close("V1");
            UI.Variants.Set("mobile", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Base_attribute_used_when_no_variant_active_at_open() {
            // Use a clearly-distinguishable mobile size: if variant routing accidentally
            // applied size.mobile while mobile is inactive, this would visibly fail.
            UI.LoadDocument("v2", @"<PromptUGUI version='1'>
                <Screen name='V2'>
                    <Image id='bg' anchor='top-left' size='100x50'
                           size.mobile='999x999'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("V2");

            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Close("V2");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_only_attr_without_base_falls_through_to_default_when_inactive() {
            // margin.mobile='50' is large enough that anchoredPosition would be
            // non-zero if accidentally applied while mobile is inactive.
            UI.LoadDocument("v3", @"<PromptUGUI version='1'>
                <Screen name='V3'>
                    <Image id='bg' anchor='top-left' size='100x50' margin.mobile='50'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("V3");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            // mobile inactive → no margin → anchoredPosition = (0, 0)
            Assert.AreEqual(new Vector2(0, 0), rt.anchoredPosition);
            UI.Close("V3");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_block_creates_node_when_active_at_open() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a1", @"<PromptUGUI version='1'>
                <Screen name='A1'>
                    <Frame id='root'/>
                    <Variant when='m'>
                        <Add into='#root'><Image id='joy'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A1");

            var rootGo = screen.Get<PromptUGUI.Controls.Frame>("root").GameObject;
            Assert.AreEqual(1, rootGo.transform.childCount);
            Assert.IsNotNull(screen.Get<PromptImage>("joy"));

            UI.Close("A1");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_block_skipped_when_inactive_at_open() {
            UI.LoadDocument("a2", @"<PromptUGUI version='1'>
                <Screen name='A2'>
                    <Frame id='root'/>
                    <Variant when='m'>
                        <Add into='#root'><Image id='joy'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A2");

            var rootGo = screen.Get<PromptUGUI.Controls.Frame>("root").GameObject;
            Assert.AreEqual(0, rootGo.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("joy"));

            UI.Close("A2");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_root_creates_at_screen_root() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a3", @"<PromptUGUI version='1'>
                <Screen name='A3'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A3");
            // screen root contains: base, extra
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            UI.Close("A3");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_at_index_inserts_at_position() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a4", @"<PromptUGUI version='1'>
                <Screen name='A4'>
                    <VStack id='v'>
                        <Image id='a'/>
                        <Image id='c'/>
                    </VStack>
                    <Variant when='m'>
                        <Add into='#v' at='1'><Image id='b'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A4");
            var v = screen.Get<PromptVStack>("v").GameObject.transform;
            Assert.AreEqual("a", v.GetChild(0).name);
            Assert.AreEqual("b", v.GetChild(1).name);
            Assert.AreEqual("c", v.GetChild(2).name);
            UI.Close("A4");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_at_start_inserts_first() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a5", @"<PromptUGUI version='1'>
                <Screen name='A5'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                    <Variant when='m'>
                        <Add into='#v' at='start'><Image id='first'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A5");
            var v = screen.Get<PromptVStack>("v").GameObject.transform;
            Assert.AreEqual("first", v.GetChild(0).name);
            Assert.AreEqual("a", v.GetChild(1).name);
            UI.Close("A5");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_template_instance_path_inserts_inside_template() {
            // into="#dialog/inner" → 走模板内部 ScopedIds（spec drift §5 / spec §8.4 扩展）
            UI.Variants.Set("m", true);
            UI.LoadDocument("a6", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Grid id='inner' columns='6'/>
                    </Frame>
                </Template>
                <Screen name='A6'>
                    <Box id='dialog'/>
                    <Variant when='m'>
                        <Add into='#dialog/inner'><Image id='item'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");

            var screen = UI.Open("A6");
            var inner = screen.Get<PromptGrid>("dialog/inner");
            Assert.AreEqual(1, inner.GameObject.transform.childCount);
            Assert.AreEqual("item", inner.GameObject.transform.GetChild(0).name);

            UI.Close("A6");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_unknown_path_segment_throws_at_open() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("a7", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame><Grid id='inner' columns='6'/></Frame>
                </Template>
                <Screen name='A7'>
                    <Box id='dialog'/>
                    <Variant when='m'>
                        <Add into='#dialog/missing'><Image id='item'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");

            Assert.Throws<System.InvalidOperationException>(() => UI.Open("A7"));

            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_with_multiple_children_at_start_preserves_order() {
            // 验证 SetSiblingIndex move 循环对 addedN > 1 的正确性：
            // 起点 prevCount=1（VStack 已有 'a'），加 2 个 child 'x','y' 到 'start'
            // 期望 child 顺序：x, y, a
            UI.Variants.Set("m", true);
            UI.LoadDocument("a8", @"<PromptUGUI version='1'>
                <Screen name='A8'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                    <Variant when='m'>
                        <Add into='#v' at='start'>
                            <Image id='x'/>
                            <Image id='y'/>
                        </Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("A8");
            var v = screen.Get<PromptVStack>("v").GameObject.transform;
            Assert.AreEqual("x", v.GetChild(0).name);
            Assert.AreEqual("y", v.GetChild(1).name);
            Assert.AreEqual("a", v.GetChild(2).name);
            UI.Close("A8");
            UI.Variants.Set("m", false);
            yield return null;
        }
    }
}
