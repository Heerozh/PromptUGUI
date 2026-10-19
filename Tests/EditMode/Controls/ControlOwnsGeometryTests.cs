using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>Control.OwnsGeometry</c> (spec 2026-10-19 §5.8): a control that places and sizes itself — a
    /// <c>&lt;Popover&gt;</c> — gets its size parsed and handed over, keeps <c>interactable</c>, and
    /// nothing else of the common geometry is written: no anchor / pivot / margin (not even parsed),
    /// no size, no <c>hidden</c>, no <c>scale</c>, no hug machinery; inside a stack it only opts out.
    /// </summary>
    public class ControlOwnsGeometryTests
    {
        private sealed class OwnProbe : Control
        {
            public SizeSpec? Got;
            public int HiddenWrites;

            internal override bool OwnsGeometry => true;

            internal override void ApplyOwnSize(SizeSpec spec) => Got = spec;

            public override bool Hidden
            {
                get => false;
                set => HiddenWrites++;
            }
        }

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.Registry.Register<OwnProbe>("OwnProbe", null);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static OwnProbe Open(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return (OwnProbe)UI.Open("S").Get("p");
        }

        [Test]
        public void ApplyCommon_writes_no_geometry_for_a_control_that_owns_it()
        {
            var p = Open("<Frame anchor='stretch'><OwnProbe id='p' anchor='top-left' width='120' height='40' margin='5,5,_,_' pivot='0,1'/></Frame>");
            var rt = p.RectTransform;

            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.anchorMin, "anchor= is the placement's, not ApplyCommon's");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.pivot, "pivot= is not written either");
            Assert.AreEqual(Vector2.zero, rt.anchoredPosition, "no margin offset");
            Assert.AreNotEqual(new Vector2(120f, 40f), rt.sizeDelta, "the size is the control's to write");
        }

        [Test]
        public void Ignored_anchor_pivot_and_margin_are_not_even_parsed()
        {
            Assert.DoesNotThrow(() => Open("<OwnProbe id='p' anchor='nonsense' pivot='x' margin='a,b'/>"),
                "an ignored attribute must not become a ParseException");
        }

        [Test]
        public void The_parsed_size_reaches_ApplyOwnSize()
        {
            var p = Open("<OwnProbe id='p' width='120' height='clamp(10, hug, 200)'/>");

            Assert.IsTrue(p.Got.HasValue, "the size was handed over");
            var s = p.Got.Value;
            Assert.IsTrue(s.HasWidth);
            Assert.AreEqual(120f, s.Width);
            Assert.IsTrue(s.IsHugHeight);
            Assert.IsTrue(s.IsClampedHeight);
            Assert.AreEqual(10f, s.MinHeight);
            Assert.AreEqual(200f, s.MaxHeight);
        }

        [Test]
        public void Inside_a_stack_it_only_ignores_layout()
        {
            var p = Open("<VStack anchor='stretch'><OwnProbe id='p' width='hug' height='clamp(_, hug, 300)'/><Frame height='20'/></VStack>");
            var go = p.GameObject;

            var le = go.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "the stack has to be told to leave it out");
            Assert.IsTrue(le.ignoreLayout);
            Assert.IsNull(go.GetComponent<HugElement>(), "no hug machinery");
            Assert.IsNull(go.GetComponent<ClampFitter>(), "no clamp machinery");
        }

        [Test]
        public void interactable_is_still_applied_and_hidden_is_not()
        {
            var p = Open("<OwnProbe id='p' interactable='false' hidden='true'/>");

            Assert.IsFalse(p.PeekInteractable, "interactable= is written as for any control");
            Assert.AreEqual(0, p.HiddenWrites, "hidden= is not the control's visibility to set");
            Assert.IsTrue(p.GameObject.activeSelf);
        }

        [Test]
        public void scale_is_not_applied()
        {
            var p = Open("<OwnProbe id='p' width='100' height='40' scale='2'/>");
            Assert.AreEqual(Vector3.one, p.RectTransform.localScale);
        }

        [Test]
        public void Hidden_is_overridable()
        {
            var p = Open("<OwnProbe id='p'/>");
            ((IControl)p).Hidden = true;
            Assert.AreEqual(1, p.HiddenWrites, "IControl.Hidden reaches the override");
        }
    }
}
