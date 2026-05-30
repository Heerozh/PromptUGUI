using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateBroadcasterTests
    {
        // Selectable.SelectionState ordinals.
        private const int Normal = 0, Highlighted = 1, Pressed = 2, NavSelected = 3, Disabled = 4;

        [Test]
        public void MapTransient_FoldsNavSelectedToNormal()
        {
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(Normal));
            Assert.AreEqual(InteractState.Hover, StateBroadcaster.MapTransient(Highlighted));
            Assert.AreEqual(InteractState.Pressed, StateBroadcaster.MapTransient(Pressed));
            Assert.AreEqual(InteractState.Normal, StateBroadcaster.MapTransient(NavSelected));
            Assert.AreEqual(InteractState.Disabled, StateBroadcaster.MapTransient(Disabled));
        }

        [Test]
        public void Composite_SelectedIsRestingBaselineOfActiveControl()
        {
            var b = new StateBroadcaster();
            Assert.AreEqual(InteractState.Normal, b.Current);

            b.SetOn(true);                                  // active at rest -> Selected
            Assert.AreEqual(InteractState.Selected, b.Current);

            b.SetTransient(InteractState.Hover);            // transient overrides Selected
            Assert.AreEqual(InteractState.Hover, b.Current);

            b.SetTransient(InteractState.Pressed);
            Assert.AreEqual(InteractState.Pressed, b.Current);

            b.SetTransient(InteractState.Normal);           // release -> back to Selected
            Assert.AreEqual(InteractState.Selected, b.Current);

            b.SetOn(false);                                 // deactivate -> Normal
            Assert.AreEqual(InteractState.Normal, b.Current);
        }

        [Test]
        public void Composite_DisabledWinsOverIsOn()
        {
            var b = new StateBroadcaster();
            b.SetOn(true);
            b.SetTransient(InteractState.Disabled);
            Assert.AreEqual(InteractState.Disabled, b.Current);
        }
    }
}
