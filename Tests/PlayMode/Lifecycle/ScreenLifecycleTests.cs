using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
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
            var screen = new PromptScreen(doc.Screens[0], inst, _reg, _store);

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
            var screen = new PromptScreen(doc.Screens[0], new ScreenInstantiator(_reg, _store), _reg, _store);
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

        [UnityTest]
        public IEnumerator ReSolve_updates_size_when_variant_toggles_at_runtime() {
            UI.LoadDocument("rs1", @"<PromptUGUI version='1'>
                <Screen name='RS1'>
                    <Image id='bg' anchor='top-left' size='100x50' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS1");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(new Vector2(200, 100), rt.sizeDelta);

            UI.Variants.Set("mobile", false);
            yield return null;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Close("RS1");
        }

        [UnityTest]
        public IEnumerator ReSolve_does_not_recreate_GameObjects() {
            UI.LoadDocument("rs2", @"<PromptUGUI version='1'>
                <Screen name='RS2'>
                    <Image id='bg' size='100x50' size.mobile='200x100'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS2");
            var bg1 = screen.Get<PromptImage>("bg").GameObject;

            UI.Variants.Set("mobile", true);
            yield return null;

            var bg2 = screen.Get<PromptImage>("bg").GameObject;
            Assert.AreSame(bg1, bg2);

            UI.Close("RS2");
            UI.Variants.Set("mobile", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_updates_control_specific_attributes() {
            UI.LoadDocument("rs3", @"<PromptUGUI version='1'>
                <Screen name='RS3'>
                    <Text id='t' fontSize='24' fontSize.mobile='48'>Hello</Text>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS3");
            var tmp = screen.Get<PromptText>("t").GameObject.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual(24, tmp.fontSize);

            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(48, tmp.fontSize);

            UI.Variants.Set("mobile", false);
            UI.Close("RS3");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_with_no_variant_overrides_is_noop() {
            UI.LoadDocument("rs4", @"<PromptUGUI version='1'>
                <Screen name='RS4'>
                    <Image id='bg' anchor='top-left' size='100x50'/>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RS4");
            var rt = screen.Get<PromptImage>("bg").RectTransform;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            // 切换无关变体不应改变属性
            UI.Variants.Set("mobile", true);
            yield return null;
            Assert.AreEqual(new Vector2(100, 50), rt.sizeDelta);

            UI.Variants.Set("mobile", false);
            UI.Close("RS4");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_first_activation_instantiates_add_block() {
            UI.LoadDocument("rsa1", @"<PromptUGUI version='1'>
                <Screen name='RSA1'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA1");

            // 首次激活前：Add 子树未实例化，screen.Get 抛 KeyNotFound
            Assert.AreEqual(1, screen.RootGameObject.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("extra"));

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            var extra = screen.Get<PromptImage>("extra");
            Assert.IsNotNull(extra);
            Assert.IsTrue(extra.GameObject.activeSelf);

            UI.Close("RSA1");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_deactivation_hides_via_SetActive_keeps_instance() {
            UI.Variants.Set("m", true);
            UI.LoadDocument("rsa2", @"<PromptUGUI version='1'>
                <Screen name='RSA2'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA2");
            var extraGo = screen.Get<PromptImage>("extra").GameObject;
            Assert.IsTrue(extraGo.activeSelf);

            UI.Variants.Set("m", false);
            yield return null;

            // Strategy C：GameObject 仍在场景里、未销毁；id 仍可解析；activeSelf=false
            Assert.IsFalse(extraGo.activeSelf);
            Assert.IsTrue(extraGo != null);  // 与 Strategy A 不同：不是 Unity null
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            Assert.AreSame(extraGo, screen.Get<PromptImage>("extra").GameObject);

            UI.Close("RSA2");
        }

        [UnityTest]
        public IEnumerator ReSolve_re_activation_reuses_same_GameObject_instance() {
            UI.LoadDocument("rsa3", @"<PromptUGUI version='1'>
                <Screen name='RSA3'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Image id='extra'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA3");

            UI.Variants.Set("m", true);
            yield return null;
            var first = screen.Get<PromptImage>("extra").GameObject;
            Assert.IsTrue(first.activeSelf);

            UI.Variants.Set("m", false);
            yield return null;
            Assert.IsFalse(first.activeSelf);

            UI.Variants.Set("m", true);
            yield return null;

            var second = screen.Get<PromptImage>("extra").GameObject;
            // Strategy C 的核心保证：同一 GameObject 实例
            Assert.AreSame(first, second);
            Assert.IsTrue(second.activeSelf);

            UI.Close("RSA3");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Variant_add_into_unknown_path_throws_when_first_activated_at_runtime() {
            // Variant 在 Open 时未激活，所以不会在 Open 时实例化；首次激活时才走 ApplyAddBlock，
            // 此刻 #dialog/missing 路径解析失败 → InvalidOperationException。
            //
            // R3 exception-routing note (verified via R3ExceptionBehaviorTests):
            //   Observer<T>.OnNext catches subscriber exceptions and routes them through
            //   OnErrorResume to the handler that was captured at Subscribe() time —
            //   specifically ObservableSystem.GetUnhandledExceptionHandler() snapshotted when
            //   Screen.Open() calls _variants.Changed.Subscribe(...). The default handler is
            //   Console.WriteLine, so the exception is silently printed; it neither propagates
            //   to UI.Variants.Set() nor appears in Unity's log. The observable side-effect
            //   (item never registered) is therefore the correct assertion anchor.
            UI.LoadDocument("a9", @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame><Grid id='inner' columns='6'/></Frame>
                </Template>
                <Screen name='A9'>
                    <Box id='dialog'/>
                    <Variant when='m'>
                        <Add into='#dialog/missing'><Image id='item'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");

            // Open 不抛（m 未激活，Variant 块未尝试实例化）
            var screen = UI.Open("A9");
            Assert.IsNotNull(screen);

            // 首次激活时才解析 #dialog/missing → 路径不存在 → InvalidOperationException。
            // 异常被 R3 路由到 Subscribe 时捕获的 handler（Console.WriteLine），
            // 对调用方透明。
            UI.Variants.Set("m", true);

            // 激活失败的可观测结果：ActivateAddBlock 在写入 _addInstances 前就抛了，
            // 所以 'item' 从未注册 → screen.Get("item") 抛 KeyNotFoundException。
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("item"));

            UI.Close("A9");
            yield return null;
        }

        [UnityTest]
        public IEnumerator UI_Variants_Set_propagates_through_to_open_screens() {
            // 双 Screen 同时打开，切一个变体应该让两边都重新解算
            UI.LoadDocument("link", @"<PromptUGUI version='1'>
                <Screen name='LinkA'>
                    <Image id='a' anchor='top-left' size='10x10' size.m='20x20'/>
                </Screen>
                <Screen name='LinkB'>
                    <Image id='b' anchor='top-left' size='30x30' size.m='40x40'/>
                </Screen></PromptUGUI>");

            var sa = UI.Open("LinkA");
            var sb = UI.Open("LinkB");

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreEqual(new Vector2(20, 20),
                sa.Get<PromptImage>("a").RectTransform.sizeDelta);
            Assert.AreEqual(new Vector2(40, 40),
                sb.Get<PromptImage>("b").RectTransform.sizeDelta);

            UI.Close("LinkA");
            UI.Close("LinkB");
            UI.Variants.Set("m", false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReSolve_subscriptions_on_add_controls_survive_toggle_cycle() {
            // Strategy C 的实用价值：在 Add 块的 Btn 上订阅一次 OnClick，跨 toggle 周期仍有效
            UI.LoadDocument("rsa4", @"<PromptUGUI version='1'>
                <Screen name='RSA4'>
                    <Frame id='base'/>
                    <Variant when='m'>
                        <Add into='@root'><Btn id='extraBtn'/></Add>
                    </Variant>
                </Screen></PromptUGUI>");
            var screen = UI.Open("RSA4");

            UI.Variants.Set("m", true);
            yield return null;

            int clicks = 0;
            var btn = screen.Get<PromptUGUI.Controls.Btn>("extraBtn");
            btn.OnClick.Subscribe(_ => clicks++).AddTo(screen);

            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, clicks);

            UI.Variants.Set("m", false);
            yield return null;
            UI.Variants.Set("m", true);
            yield return null;

            // 同一 Btn 实例、同一 Subject<Unit> → 订阅仍触发
            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(2, clicks);

            UI.Close("RSA4");
            UI.Variants.Set("m", false);
            yield return null;
        }
    }
}
