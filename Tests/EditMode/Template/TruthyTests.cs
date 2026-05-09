using NUnit.Framework;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template
{
    public class TruthyTests
    {
        [TestCase("true", true)]
        [TestCase("True", true)]
        [TestCase("TRUE", true)]
        [TestCase("yes", true)]
        [TestCase("foo", true)]
        [TestCase("1", true)]
        [TestCase("42", true)]
        [TestCase("-1", true)]
        [TestCase("0.5", true)]
        public void Truthy_values(string s, bool expected)
        {
            Assert.AreEqual(expected, Truthy.Eval(s));
        }

        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("false", false)]
        [TestCase("False", false)]
        [TestCase("FALSE", false)]
        [TestCase("0", false)]
        [TestCase("0.0", false)]
        [TestCase("null", false)]
        [TestCase("NULL", false)]
        public void Falsy_values(string s, bool expected)
        {
            Assert.AreEqual(expected, Truthy.Eval(s));
        }
    }
}
