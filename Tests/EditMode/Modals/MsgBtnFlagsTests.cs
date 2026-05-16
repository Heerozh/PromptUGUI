using NUnit.Framework;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class MsgBtnFlagsTests
    {
        [Test]
        public void None_is_zero() => Assert.AreEqual(0, (int)MsgBtn.None);

        [Test]
        public void Flags_are_powers_of_two()
        {
            Assert.AreEqual(1, (int)MsgBtn.OK);
            Assert.AreEqual(2, (int)MsgBtn.Cancel);
            Assert.AreEqual(4, (int)MsgBtn.Yes);
            Assert.AreEqual(8, (int)MsgBtn.No);
            Assert.AreEqual(16, (int)MsgBtn.Close);
        }

        [Test]
        public void Combination_yields_or_of_values()
        {
            var combo = MsgBtn.Yes | MsgBtn.No | MsgBtn.Cancel;
            Assert.AreEqual(14, (int)combo);
            Assert.IsTrue((combo & MsgBtn.Yes) != 0);
            Assert.IsTrue((combo & MsgBtn.No) != 0);
            Assert.IsTrue((combo & MsgBtn.Cancel) != 0);
            Assert.IsTrue((combo & MsgBtn.OK) == 0);
        }
    }
}
