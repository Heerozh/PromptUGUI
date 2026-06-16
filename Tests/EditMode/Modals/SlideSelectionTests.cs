using NUnit.Framework;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class SlideSelectionTests
    {
        private sealed class Item { public string Id; }

        [Test]
        public void Holds_Item_And_Button()
        {
            var it = new Item { Id = "x" };
            var sel = new SlideSelection<Item>(it, "play");
            Assert.AreSame(it, sel.Item);
            Assert.AreEqual("play", sel.Button);
            Assert.IsFalse(sel.Cancelled);
        }

        [Test]
        public void Default_Is_Cancelled()
        {
            SlideSelection<Item> sel = default;
            Assert.IsNull(sel.Item);
            Assert.IsNull(sel.Button);
            Assert.IsTrue(sel.Cancelled);
        }

        [Test]
        public void Deconstructs()
        {
            var it = new Item { Id = "x" };
            var (item, button) = new SlideSelection<Item>(it, "hard");
            Assert.AreSame(it, item);
            Assert.AreEqual("hard", button);
        }

        [Test]
        public void Cancelled_Tracks_Button_Null_Only()
        {
            // 单按钮路径内部会出现 (item, null)；Cancelled 以 Button==null 为准（item 设了也算）
            var sel = new SlideSelection<Item>(new Item(), null);
            Assert.IsTrue(sel.Cancelled);
        }
    }
}
