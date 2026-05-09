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
using PromptVStack = PromptUGUI.Controls.VStack;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.E2E
{

    public sealed class TestButton : Control
    {
        private Button _btn;
        private TMP_Text _label;
        private readonly Subject<Unit> _click = new();
        public override void OnAttached()
        {
            _btn = GameObject.GetComponent<Button>();
            _label = GameObject.GetComponentInChildren<TMP_Text>();
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
        }
        [UIAttr("text")]
        public string TextValue { set => _label.text = value; }
        public Observable<Unit> OnClick => _click;
        public override void Dispose()
        {
            _click.Dispose();
            base.Dispose();
        }
    }

    public class MainMenuDemoTests
    {

        private GameObject _btnPrefab;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();

            // 构造一个 mock button prefab
            _btnPrefab = new GameObject("TestButtonPrefab", typeof(RectTransform));
            _btnPrefab.AddComponent<UnityImage>();
            _btnPrefab.AddComponent<Button>();
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(_btnPrefab.transform);
            label.AddComponent<TextMeshProUGUI>();

            UI.Registry.Register<TestButton>("TestButton", _btnPrefab, defaultTextAttr: "text");
        }

        [TearDown]
        public void TearDown()
        {
            if (_btnPrefab != null) Object.Destroy(_btnPrefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Main_menu_with_three_buttons_clicks_propagate()
        {
            UI.LoadDocument("main", @"<PromptUGUI version='1'>
                <Screen name='MainMenu'>
                    <VStack id='menuRoot' anchor='center' size='280x240' spacing='12'>
                        <TestButton id='play'>开始</TestButton>
                        <TestButton id='settings'>设置</TestButton>
                        <TestButton id='quit'>退出</TestButton>
                    </VStack>
                </Screen></PromptUGUI>");

            var screen = UI.Open("MainMenu");

            var playClicks = 0;
            screen.Get<TestButton>("play").OnClick
                  .Subscribe(_ => playClicks++)
                  .AddTo(screen);

            screen.Get<TestButton>("play").GameObject
                  .GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.AreEqual(1, playClicks);

            var playLabel = screen.Get<TestButton>("play").GameObject
                                  .GetComponentInChildren<TMP_Text>();
            Assert.AreEqual("开始", playLabel.text);

            var menuRoot = screen.Get<PromptVStack>("menuRoot").GameObject;
            Assert.AreEqual(3, menuRoot.transform.childCount);

            UI.Close("MainMenu");
            yield return null;
        }
    }
}
