using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class DepGraphTests
    {
        [Test]
        public void IsCommons_reflects_set()
        {
            var g = new DepGraph();
            g.CommonsSources.Add("c");
            Assert.IsTrue(g.IsCommons("c"));
            Assert.IsFalse(g.IsCommons("d"));
        }

        [Test]
        public void ScreensDependingOn_returns_matches()
        {
            var g = new DepGraph();
            g.ScreenDeps["A"] = new DepGraph.ScreenDep
            {
                EntrySrc = "a",
                AllDeps = new() { "a", "x" },
            };
            g.ScreenDeps["B"] = new DepGraph.ScreenDep
            {
                EntrySrc = "b",
                AllDeps = new() { "b", "y" },
            };
            CollectionAssert.AreEquivalent(new[] { "A" },
                System.Linq.Enumerable.ToArray(g.ScreensDependingOn("x")));
        }

        [Test]
        public void Clear_resets()
        {
            var g = new DepGraph();
            g.CommonsSources.Add("c");
            g.ScreenDeps["A"] = new DepGraph.ScreenDep();
            g.SrcToDeps["c"] = new();
            g.Clear();
            Assert.AreEqual(0, g.CommonsSources.Count);
            Assert.AreEqual(0, g.ScreenDeps.Count);
            Assert.AreEqual(0, g.SrcToDeps.Count);
        }
    }
}
