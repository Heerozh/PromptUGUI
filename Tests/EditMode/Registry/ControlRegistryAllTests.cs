using System.Linq;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Registry {
    public class ControlRegistryAllTests {
        [Test]
        public void All_lists_all_registered_tags() {
            var r = new ControlRegistry();
            r.Register<Frame>("Frame", null);
            r.Register<VStack>("VStack", null);

            var tags = r.All.Select(x => x.Tag).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Frame", "VStack" }, tags);
        }
    }
}
