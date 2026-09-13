using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using PromptScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// <c>screen.Instantiate(template, parent)</c> — the public entry to the subtree instantiation
    /// that only the <c>BindItems</c> hosts could reach before
    /// (spec 2026-09-13-runtime-template-instantiate-design).
    ///
    /// <para>An instance is a dynamic subtree like a bound row: ids live in the root's own scope,
    /// never in the Screen's; it re-solves with the Screen (Variant / theme / scale); it is destroyed
    /// with <c>root.Dispose()</c>; and the Screen's registry forgets it lazily, so instantiate /
    /// dispose churn must neither throw nor pile up entries.</para>
    /// </summary>
    public class ScreenInstantiateTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Doc = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <HStack>
      <Text id='label'>placeholder</Text>
      <Image id='bg' color='#112233' color.alt='#445566'/>
    </HStack>
  </Template>
  <Template name='Needs'><Param name='tint'/><Image id='bg' color='{{tint}}'/></Template>
  <Screen name='S'>
    <Frame id='layer' anchor='stretch'/>
    <ScrollList id='list' size='100x100'/>
  </Screen>
  <Screen name='T'><Frame id='layerT' anchor='stretch'/></Screen>
</PromptUGUI>";

        private static PromptScreen OpenS()
        {
            UI.LoadDocument("test", Doc);
            return UI.Open("S");
        }

        private static RectTransform LayerOf(PromptScreen screen) => screen.Get("layer").RectTransform;

        private static string ColorOf(IControl root, string id) =>
            ColorUtility.ToHtmlStringRGB(
                ((Control)root.Get<IControl>(id)).GameObject.GetComponent<UnityImage>().color);

        private static string TmpTextOf(IControl root, string id) =>
            ((Control)root.Get<IControl>(id)).GameObject.GetComponent<TMPro.TMP_Text>().text;

        private sealed class Flag : IDisposable
        {
            public bool Disposed;
            public void Dispose() => Disposed = true;
        }

        // ------------------------------------------------------------------ resolution & scope

        [Test]
        public void Instantiate_Template_RootGetReachesScopedIds()
        {
            var screen = OpenS();
            var layer = LayerOf(screen);

            var root = screen.Instantiate("Row", layer);

            Assert.IsNotNull(root);
            Assert.AreEqual(layer, root.GameObject.transform.parent, "lands directly under the given parent");
            Assert.IsNotNull(root.Get<Text>("label"), "ids inside the body resolve through the root's scope");
            Assert.AreEqual("112233", ColorOf(root, "bg"), "attributes were applied");
        }

        [Test]
        public void Instantiate_DoesNotPolluteScreenIds()
        {
            var screen = OpenS();
            var a = screen.Instantiate("Row", LayerOf(screen));
            var b = screen.Instantiate("Row", LayerOf(screen));

            Assert.AreNotSame(a.Get("label"), b.Get("label"), "each instance owns its own scope");
            Assert.Throws<KeyNotFoundException>(() => screen.Get("label"),
                "an instance id never reaches the Screen-level table");
        }

        [Test]
        public void Instantiate_ControlTag_GivesBareControl()
        {
            var screen = OpenS();

            var root = screen.Instantiate("Text", LayerOf(screen));

            Assert.IsInstanceOf<Text>(root, "a registered Control tag resolves like itemTemplate= does");
        }

        [Test]
        public void Instantiate_IControlParent_LandsOnChildHost()
        {
            var screen = OpenS();
            var list = screen.Get<ScrollList>("list");

            var root = screen.Instantiate("Row", list);

            var host = ((Control)list).ChildHostTransform;
            Assert.AreNotEqual(list.RectTransform, host, "guard: ScrollList hosts children in Content, not itself");
            Assert.AreEqual(host, root.GameObject.transform.parent,
                "an IControl parent means 'where an XML child of it would go'");
        }

        // ------------------------------------------------------------------ errors

        [Test]
        public void Instantiate_UnknownName_ThrowsKeyNotFound()
        {
            var screen = OpenS();

            var ex = Assert.Throws<KeyNotFoundException>(() => screen.Instantiate("Nope", LayerOf(screen)));

            StringAssert.Contains("Nope", ex.Message);
            StringAssert.Contains("'S'", ex.Message);
        }

        [Test]
        public void Instantiate_RequiredParam_ThrowsParseException_NamingTheApi()
        {
            var screen = OpenS();

            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(
                () => screen.Instantiate("Needs", LayerOf(screen)));

            StringAssert.Contains("Instantiate(\"Needs\")", ex.Message);
            StringAssert.Contains("tint", ex.Message);
            StringAssert.Contains("Needs", ex.Message);
        }

        [Test]
        public void Instantiate_ParentOutsideScreen_ThrowsArgument()
        {
            var screen = OpenS();
            var stray = new GameObject("Stray", typeof(RectTransform));
            try
            {
                Assert.Throws<ArgumentException>(
                    () => screen.Instantiate("Row", (RectTransform)stray.transform),
                    "a RectTransform that is not under this Screen's root");

                var other = UI.Open("T");
                Assert.Throws<ArgumentException>(
                    () => screen.Instantiate("Row", other.Get("layerT")),
                    "a control that belongs to another Screen");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void Instantiate_AfterClose_ThrowsInvalidOperation()
        {
            var screen = OpenS();
            var layer = LayerOf(screen);
            screen.Close();

            Assert.Throws<InvalidOperationException>(() => screen.Instantiate("Row", layer));
        }

        // ------------------------------------------------------------------ re-solve

        [Test]
        public void Instantiate_SetText_SurvivesReSolve()
        {
            var screen = OpenS();
            var root = screen.Instantiate("Row", LayerOf(screen));
            root.Get<Text>("label").TextValue = "bound";

            screen.ReSolve();

            Assert.IsTrue(root.GameObject != null, "the instance is still alive after a replay");
            Assert.AreEqual("bound", TmpTextOf(root, "label"),
                "the DefaultText lock keeps a replay from clobbering code-set content");
        }

        [Test]
        public void Instantiate_FollowsVariantFlip()
        {
            var screen = OpenS();
            var root = screen.Instantiate("Row", LayerOf(screen));
            Assume.That(ColorOf(root, "bg"), Is.EqualTo("112233"));

            UI.Variants.Set("alt", true);

            Assert.AreEqual("445566", ColorOf(root, "bg"),
                "an instance is registered as a dynamic subtree, so the state path reaches it");
        }

        [Test]
        public void Instantiate_HiddenSurvivesReSolve()
        {
            var screen = OpenS();
            var root = screen.Instantiate("Row", LayerOf(screen));

            root.Hidden = true;
            screen.ReSolve();

            Assert.IsTrue(root.Hidden, "Hidden is only replayed when the node declares hidden=; a pooled "
                                       + "instance parked with Hidden=true must stay parked");
        }

        // ------------------------------------------------------------------ lifetime

        [Test]
        public void Dispose_ThenInstantiate_NoLeakedRegistration()
        {
            var screen = OpenS();
            var first = screen.Instantiate("Row", LayerOf(screen));

            first.Dispose();
            Assume.That(first.GameObject == null, "EditMode Dispose destroys immediately");
            var second = screen.Instantiate("Row", LayerOf(screen));

            Assert.IsNotNull(second);
            Assert.AreEqual(1, screen.LiveDynamicSubtreeCount,
                "the dead entry is pruned on the next registration; only the live instance remains");
            screen.ReSolve();   // and a replay over the table does not trip over the disposed one
        }

        [Test]
        public void Dispose_ReleasesTrackedSubscriptions()
        {
            var screen = OpenS();
            var root = screen.Instantiate("Row", LayerOf(screen));
            var onRoot = new Flag().AddTo(root);
            var onChild = new Flag().AddTo(root.Get("label"));

            root.Dispose();

            Assert.IsTrue(onRoot.Disposed, ".AddTo(root) is released by root.Dispose()");
            Assert.IsTrue(onChild.Disposed, ".AddTo(child) is released recursively");
            Assert.IsTrue(root.GameObject == null, "and the GameObject is gone");
        }

        [Test]
        public void Close_DisposesLiveInstances()
        {
            var screen = OpenS();
            var root = screen.Instantiate("Row", LayerOf(screen));
            var flag = new Flag().AddTo(root);

            screen.Close();

            Assert.IsTrue(flag.Disposed,
                "an instance is nobody's child, so Close has to release its subscriptions itself — "
                + "otherwise they keep firing into destroyed controls");
        }

        [Test]
        public void Close_AfterHostDisposedRows_IsIdempotent()
        {
            UI.LoadDocument("test", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack><Text id='label'>x</Text></HStack></Template>
  <Screen name='S'><Frame id='layer' anchor='stretch'/><ScrollList id='list' itemTemplate='Row' size='100x100'/></Screen>
</PromptUGUI>");
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a" }), (IControl slot, string s) => { });
            Assume.That(list.SlotCount, Is.EqualTo(1));
            screen.Instantiate("Row", LayerOf(screen));

            // Rows are disposed by their host first, then Close sweeps the dynamic table: the second
            // Dispose on a row must be a no-op, not an error.
            Assert.DoesNotThrow(() => screen.Close());
        }
    }
}
