using NUnit.Framework;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class BtnFlagsTests
    {
        [Test]
        public void None_is_zero() => Assert.AreEqual(0, (int)Btn.None);

        [Test]
        public void Flags_are_powers_of_two()
        {
            Assert.AreEqual(1, (int)Btn.OK);
            Assert.AreEqual(2, (int)Btn.Cancel);
            Assert.AreEqual(4, (int)Btn.Yes);
            Assert.AreEqual(8, (int)Btn.No);
            Assert.AreEqual(16, (int)Btn.Close);
        }

        [Test]
        public void Combination_yields_or_of_values()
        {
            var combo = Btn.Yes | Btn.No | Btn.Cancel;
            Assert.AreEqual(14, (int)combo);
            Assert.IsTrue((combo & Btn.Yes) != 0);
            Assert.IsTrue((combo & Btn.No) != 0);
            Assert.IsTrue((combo & Btn.Cancel) != 0);
            Assert.IsTrue((combo & Btn.OK) == 0);
        }
    }
}
