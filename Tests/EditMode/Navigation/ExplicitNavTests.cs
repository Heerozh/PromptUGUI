using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class ExplicitNavTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void NavNone_SetsModeNone()
        {
            var s = Open("<Btn id='a' nav='none'>A</Btn>");
            var sel = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.None, sel.navigation.mode);
        }

        [Test]
        public void NavUp_SetsExplicitToTarget()
        {
            var s = Open("<Btn id='a'>A</Btn><Btn id='b' navUp='a'>B</Btn>");
            var b = s.Get<Btn>("b").GameObject.GetComponent<Selectable>();
            var a = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, b.navigation.mode);
            Assert.AreSame(a, b.navigation.selectOnUp);
        }

        // Fix A: nav target in inactive variant <Add> block must not crash Open or ReSolve.
        // The direction is left to the geometric fallback while the block is inactive and
        // self-heals when the block activates (because ReSolve re-runs Resolve after ActivateAddBlock).

        [Test]
        public void NavDown_InactiveVariantAddBlock_DoesNotThrow()
        {
            // 'mobile' variant is inactive at Open → 'B' is not in _byId → must not throw.
            Assert.DoesNotThrow(() =>
            {
                Open("<Btn id='a' navDown='B'>A</Btn>" +
                     "<Variant when='mobile'><Add into='@root'><Btn id='B'>B</Btn></Add></Variant>");
            });
        }

        [Test]
        public void NavDown_InactiveVariantAddBlock_SelfHealsOnActivation()
        {
            var s = Open("<Btn id='a' navDown='B'>A</Btn>" +
                         "<Variant when='mobile'><Add into='@root'><Btn id='B'>B</Btn></Add></Variant>");
            var aSel = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            // Before activation: 'B' does not exist so Sel returns null → selectOnDown is the
            // geometric fallback (whatever FindSelectableOnDown returns in the test environment).
            var beforeActivation = aSel.navigation.selectOnDown;

            // Activate the variant → ReSolve fires → 'B' is instantiated → nav wires up.
            UI.Variants.Set("mobile", true);
            var bSel = s.Get<Btn>("B").GameObject.GetComponent<Selectable>();
            Assert.AreSame(bSel, aSel.navigation.selectOnDown, "selectOnDown must wire to B after activation");
            // B could not have equalled the pre-activation geometric value (it didn't exist then).
            Assert.AreNotSame(beforeActivation, bSel, "selectOnDown must have changed from the pre-activation value to B");
        }

        [Test]
        public void NavDown_UndeclaredId_FallsBackToGeometric()
        {
            // A completely undeclared id (typo): the fallback design leaves it to geometric
            // rather than throwing. PUI-NAV-UNKNOWN-TARGET lint is the static catch for typos.
            Assert.DoesNotThrow(() =>
            {
                var s = Open("<Btn id='a' navDown='totallyUndeclared'>A</Btn>");
                var sel = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
                // Mode is Explicit (navDown attribute was written), direction falls to geometric.
                Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, sel.navigation.mode);
            });
        }
    }
}
