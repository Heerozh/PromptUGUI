using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class BuiltinTagsTests
    {
        [Test]
        public void BuiltinTags_Matches_BuiltinPrimitives_Registrations()
        {
            // BuiltinTags is the pure-C# lint mirror of the Unity-side registry. If they drift,
            // the CLI mis-classifies a real builtin as a Template invocation (or vice versa),
            // re-opening the PUI-TABBAR-CHILD false-positive class this guard exists to prevent.
            var reg = new ControlRegistry();
            BuiltinPrimitives.Register(reg);
            var registered = reg.All.Select(e => e.Tag).ToList();

            Assert.That(BuiltinTags.All, Is.EquivalentTo(registered),
                "BuiltinTags drifted from BuiltinPrimitives — sync Runtime/Core/Lint/BuiltinTags.cs.");
        }
    }
}
