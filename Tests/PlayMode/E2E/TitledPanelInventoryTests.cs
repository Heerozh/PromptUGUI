using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PromptFrame = PromptUGUI.Controls.Frame;
using PromptGrid = PromptUGUI.Controls.Grid;
using PromptText = PromptUGUI.Controls.Text;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.E2E
{

    public sealed class CloseBtn : Control
    {
        private Button _btn;
        private readonly Subject<Unit> _click = new();
        public override void OnAttached()
        {
            _btn = GameObject.GetComponent<Button>();
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
        }
        public Observable<Unit> OnClick => _click;
        public override void Dispose() { _click.Dispose(); base.Dispose(); }
    }

    public class TitledPanelInventoryTests
    {

        private GameObject _btnPrefab;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();

            _btnPrefab = new GameObject("CloseBtnPrefab", typeof(RectTransform));
            _btnPrefab.AddComponent<UnityImage>();
            _btnPrefab.AddComponent<Button>();

            UI.Registry.Register<CloseBtn>("CloseBtn", _btnPrefab);
        }

        [TearDown]
        public void TearDown()
        {
            if (_btnPrefab != null) Object.Destroy(_btnPrefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator TitledPanel_wraps_inventory_grid_with_path_access()
        {
            UI.LoadDocument("inv", @"<PromptUGUI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                        <CloseBtn if='{{closable}}' id='close'/>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='Inventory'>
                    <TitledPanel id='dialog' anchor='center' size='600x400' title='背包'>
                        <Grid id='itemGrid' columns='6'/>
                    </TitledPanel>
                </Screen></PromptUGUI>");

            var screen = UI.Open("Inventory");

            // dialog 在顶层 scope（模板根透传后是 Frame）
            var dialog = screen.Get<PromptFrame>("dialog");
            Assert.IsNotNull(dialog);

            // 通用属性透传：模板根有 anchor=center / size=600x400
            var rt = dialog.RectTransform;
            Assert.AreEqual(new Vector2(600, 400), rt.sizeDelta);

            // Slot 内容已注入：dialog 的子节点：titleLabel, close, itemGrid
            Assert.AreEqual(3, dialog.GameObject.transform.childCount);

            // 模板内 id 通过路径访问
            var title = screen.Get<PromptText>("dialog/titleLabel");
            Assert.AreEqual("背包", title.GameObject.GetComponent<TMP_Text>().text);

            var close = screen.Get<CloseBtn>("dialog/close");
            Assert.IsNotNull(close);

            var grid = screen.Get<PromptGrid>("dialog/itemGrid");
            Assert.IsNotNull(grid);

            // close 可订阅
            var clicks = 0;
            close.OnClick.Subscribe(_ => clicks++).AddTo(screen);
            close.GameObject.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(1, clicks);

            UI.Close("Inventory");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TitledPanel_with_closable_false_omits_CloseBtn()
        {
            UI.LoadDocument("inv2", @"<PromptUGUI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                        <CloseBtn if='{{closable}}' id='close'/>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='InventoryNoClose'>
                    <TitledPanel id='dialog' title='背包' closable='false'>
                        <Grid id='itemGrid' columns='6'/>
                    </TitledPanel>
                </Screen></PromptUGUI>");

            var screen = UI.Open("InventoryNoClose");
            var dialog = screen.Get<PromptFrame>("dialog");

            // 没有 close 子节点：titleLabel + itemGrid
            Assert.AreEqual(2, dialog.GameObject.transform.childCount);

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("dialog/close"));

            UI.Close("InventoryNoClose");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TitledPanel_can_be_instantiated_twice_with_independent_ids()
        {
            UI.LoadDocument("inv3", @"<PromptUGUI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Frame>
                        <Text id='titleLabel'>{{title}}</Text>
                    </Frame>
                </Template>
                <Screen name='Twin'>
                    <VStack id='root'>
                        <TitledPanel id='left'  title='Left'/>
                        <TitledPanel id='right' title='Right'/>
                    </VStack>
                </Screen></PromptUGUI>");

            var screen = UI.Open("Twin");

            var leftTitle = screen.Get<PromptText>("left/titleLabel");
            var rightTitle = screen.Get<PromptText>("right/titleLabel");

            Assert.AreEqual("Left", leftTitle.GameObject.GetComponent<TMP_Text>().text);
            Assert.AreEqual("Right", rightTitle.GameObject.GetComponent<TMP_Text>().text);

            UI.Close("Twin");
            yield return null;
        }
    }
}
