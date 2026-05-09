using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class UIBuiltinsAutoRegisterTests
    {
        private static readonly string[] Builtins = {
            "Frame", "Image", "Icon", "Text", "VStack", "HStack", "Grid", "Btn",
        };

        [Test]
        public void Registry_has_builtins_without_manual_registration()
        {
            UI.ResetForTests();

            foreach (var tag in Builtins)
                Assert.IsTrue(UI.Registry.Has(tag),
                    $"expected builtin tag '{tag}' to be auto-registered after ResetForTests");
        }
    }
}
