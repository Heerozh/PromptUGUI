using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Lifecycle {
    public class ScreenInstantiatorTests {

        ControlRegistry _reg;

        [SetUp] public void SetUp() {
            _reg = new ControlRegistry();
            BuiltinPrimitives.Register(_reg);
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
    }
}
