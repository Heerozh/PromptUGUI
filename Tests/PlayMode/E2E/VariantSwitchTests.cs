using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using PromptBtn = PromptUGUI.Controls.Btn;
using PromptImage = PromptUGUI.Controls.Image;
using PromptVStack = PromptUGUI.Controls.VStack;

namespace PromptUGUI.Tests.E2E
{

    public class VariantSwitchTests
    {

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator MainMenu_switches_between_pc_and_mobile_portrait()
        {
            UI.LoadDocument("e2e", @"<PromptUGUI version='1'>
                <Screen name='Menu'>
                    <VStack id='menuRoot' spacing='12'
                            anchor.pc='center' width.pc='480' height.pc='320'
                            anchor.mobile-portrait='bottom-stretch'
                            height.mobile-portrait='400'
                            margin.mobile-portrait='_,16,80,16'>
                        <Btn id='play' size='240x64'/>
                    </VStack>
                    <Variant when='mobile-portrait'>
                        <Add into='@root'>
                            <Image id='joystick' anchor='bottom-left'
                                   size='160x160' margin='_,_,40,40'/>
                        </Add>
                    </Variant>
                </Screen></PromptUGUI>");

            // PC：仅 pc 激活
            UI.Variants.Set("pc", true);
            var screen = UI.Open("Menu");
            var rootRT = screen.Get<PromptVStack>("menuRoot").RectTransform;

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMax);
            Assert.AreEqual(new Vector2(480, 320), rootRT.sizeDelta);
            // mobile-portrait 还从未激活 → joystick 还未实例化（Strategy C 的首次激活语义）
            Assert.AreEqual(1, screen.RootGameObject.transform.childCount);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => screen.Get("joystick"));

            // 切到 mobile-portrait（必须先关 pc：两者同时激活会让 width.pc=480 落到
            // anchor.mobile-portrait=bottom-stretch 的 X 轴上触发 strict 校验报错）
            UI.Variants.Set("pc", false);
            UI.Variants.Set("mobile-portrait", true);
            yield return null;

            Assert.AreEqual(new Vector2(0, 0), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(1, 0), rootRT.anchorMax);
            Assert.AreEqual(400f, rootRT.sizeDelta.y, 0.001f);
            // joystick 首次激活实例化
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            var joystickGo = screen.Get<PromptImage>("joystick").GameObject;
            Assert.IsTrue(joystickGo.activeSelf);

            // 切回 pc（同样先关再开）
            UI.Variants.Set("mobile-portrait", false);
            UI.Variants.Set("pc", true);
            yield return null;
            yield return null;  // wait one extra frame for joystick destroy

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rootRT.anchorMin);
            Assert.AreEqual(new Vector2(480, 320), rootRT.sizeDelta);
            // Strategy C：joystick 未销毁，只 SetActive(false) 隐藏；引用与 _byId 表项保持
            Assert.AreEqual(2, screen.RootGameObject.transform.childCount);
            Assert.IsFalse(joystickGo.activeSelf);
            Assert.AreSame(joystickGo, screen.Get<PromptImage>("joystick").GameObject);

            UI.Close("Menu");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameObject_identity_preserved_across_variant_switch()
        {
            // base 整体替换：base size 与 size.m 都是 anchor=center，无 stretch 轴冲突
            UI.LoadDocument("id_e2e", @"<PromptUGUI version='1'>
                <Screen name='Stable'>
                    <VStack id='root' anchor='center' size='200x200' size.m='300x300'>
                        <Btn id='go'/>
                    </VStack>
                </Screen></PromptUGUI>");

            var screen = UI.Open("Stable");
            var rootGo = screen.Get<PromptVStack>("root").GameObject;
            var btnGo = screen.Get<PromptBtn>("go").GameObject;

            UI.Variants.Set("m", true);
            yield return null;

            Assert.AreSame(rootGo, screen.Get<PromptVStack>("root").GameObject);
            Assert.AreSame(btnGo, screen.Get<PromptBtn>("go").GameObject);

            UI.Variants.Set("m", false);
            yield return null;

            Assert.AreSame(rootGo, screen.Get<PromptVStack>("root").GameObject);

            UI.Close("Stable");
        }
    }
}
