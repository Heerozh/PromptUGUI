using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouteQueryTests
    {
        [Test]
        public void Empty_HasNothing()
        {
            Assert.IsFalse(RouteQuery.Empty.Has("x"));
            Assert.IsNull(RouteQuery.Empty["x"]);
            Assert.AreEqual("fb", RouteQuery.Empty.Get("x", "fb"));
            Assert.AreEqual(7, RouteQuery.Empty.GetInt("x", 7));
        }

        [Test]
        public void Parse_SplitsPairs_AndUrlDecodes()
        {
            var q = RouteQuery.ParseQueryString("uid=123&name=a%20b&flag=");
            Assert.AreEqual("123", q["uid"]);
            Assert.AreEqual(123, q.GetInt("uid"));
            Assert.AreEqual("a b", q["name"]);
            Assert.IsTrue(q.Has("flag"));
            Assert.AreEqual("", q["flag"]);
        }

        [Test]
        public void Parse_Empty_ReturnsEmptyQuery()
        {
            var q = RouteQuery.ParseQueryString("");
            Assert.IsFalse(q.Has("x"));
        }

        [Test]
        public void GetInt_NonNumeric_ReturnsFallback()
        {
            var q = RouteQuery.ParseQueryString("n=abc");
            Assert.AreEqual(-1, q.GetInt("n", -1));
        }
    }
}
