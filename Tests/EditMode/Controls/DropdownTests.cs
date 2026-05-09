using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DropdownTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BindOptions_strings_populates_tmp_dropdown()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");

            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "Low", "High" }));
            var tmp = d.GameObject.GetComponentInChildren<TMPro.TMP_Dropdown>();
            Assert.AreEqual(2, tmp.options.Count);
            Assert.AreEqual("Low", tmp.options[0].text);
        }

        [Test]
        public void OnSelected_fires_when_value_setter_changes()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var d = screen.Get<Dropdown>("d");
            d.BindOptions(Observable.Return<IEnumerable<string>>(new[] { "A", "B", "C" }));
            int? last = null;
            d.OnSelected.Subscribe(i => last = i);
            d.Value = 2;
            Assert.AreEqual(2, last);
        }

        [Test]
        public void Geometry_ArrowSizeIsTwentyTwenty()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var d = UI.Open("S").Get<Dropdown>("d");
            var arrow = d.GameObject.transform.Find("Arrow") as RectTransform;
            Assert.IsNotNull(arrow);
            Assert.AreEqual(new Vector2(20, 20), arrow.sizeDelta);
        }

        [Test]
        public void Geometry_ArrowAnchoredPositionMinusFifteen()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var d = UI.Open("S").Get<Dropdown>("d");
            var arrow = d.GameObject.transform.Find("Arrow") as RectTransform;
            Assert.AreEqual(new Vector2(-15, 0), arrow.anchoredPosition);
        }

        [Test]
        public void Viewport_HasStencilMaskAndImageWithAlphaOne()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var d = UI.Open("S").Get<Dropdown>("d");
            var viewport = d.GameObject.GetComponentInChildren<UnityEngine.UI.Mask>(includeInactive: true);
            Assert.IsNotNull(viewport, "Viewport should use stencil Mask (default prefab parity)");
            Assert.IsFalse(viewport.showMaskGraphic, "Mask graphic must be hidden");

            var img = viewport.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(img, "Mask requires Image graphic on same GO");
            Assert.AreEqual(1f, img.color.a, "alpha=1 critical to avoid 4af322b alpha-discard regression");
        }

        [Test]
        public void Viewport_HasNoRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var d = UI.Open("S").Get<Dropdown>("d");
            var rm2d = d.GameObject.GetComponentInChildren<UnityEngine.UI.RectMask2D>(includeInactive: true);
            Assert.IsNull(rm2d, "RectMask2D should be replaced by stencil Mask");
        }

        [Test]
        public void Viewport_SizeDeltaXMinusEighteen()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Dropdown id='d'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var d = UI.Open("S").Get<Dropdown>("d");
            var viewport = d.GameObject.transform.Find("Template/Viewport") as RectTransform;
            Assert.IsNotNull(viewport);
            Assert.AreEqual(-18f, viewport.sizeDelta.x, "viewport sizeDelta.x = -18 reserves 18px for Vertical Scrollbar");
        }
    }
}
