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

        [Test]
        public void Register_replaces_existing_tag_without_throwing() {
            var r = new ControlRegistry();
            r.Register<Frame>("Btn", null);

            Assert.DoesNotThrow(() => r.Register<Btn>("Btn", null));

            Assert.AreEqual(typeof(Btn), r.Resolve("Btn").ControlType);
            Assert.AreEqual(1, r.All.Count(x => x.Tag == "Btn"));
        }
    }
}
