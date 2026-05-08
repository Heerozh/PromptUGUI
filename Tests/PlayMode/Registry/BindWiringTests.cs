using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.Registry {

    public sealed class BindSampleControl : Control {
        [Bind] public TMP_Text Label;
        [Bind("CustomChild")] public Button Btn;
    }

    public class BindWiringTests {

        GameObject _prefab;

        [SetUp] public void SetUp() {
            UI.ResetForTests();

            _prefab = new GameObject("BindSamplePrefab", typeof(RectTransform));

            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(_prefab.transform);
            label.AddComponent<TextMeshProUGUI>();

            var custom = new GameObject("CustomChild", typeof(RectTransform));
            custom.transform.SetParent(_prefab.transform);
            custom.AddComponent<UnityImage>();
            custom.AddComponent<Button>();

            UI.Registry.Register<BindSampleControl>("BindSample", _prefab);
        }

        [TearDown] public void TearDown() {
            if (_prefab != null) Object.Destroy(_prefab);
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Bind_field_wires_component_from_named_child() {
            UI.LoadDocument("d", @"<PromptUGUI version='1'>
                <Screen name='S'><BindSample id='x'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var ctl = screen.Get<BindSampleControl>("x");
            Assert.IsNotNull(ctl.Label, "Label should auto-wire from child 'Label'");
            Assert.IsNotNull(ctl.Btn,   "Btn should auto-wire from child 'CustomChild'");

            UI.Close("S");
            yield return null;
        }
    }
}
