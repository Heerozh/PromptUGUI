using System;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Application
{
    public class UIResetEventTests
    {
        [SetUp]
        public void Setup() => UI.ResetForTests();

        [TearDown]
        public void Teardown() => UI.ResetForTests();

        [Test]
        public void OnReset_event_fires_after_reset()
        {
            var fired = 0;
            Action handler = () => fired++;
            UI.OnReset += handler;
            try
            {
                UI.ResetForTests();
                Assert.AreEqual(1, fired,
                    "UI.OnReset should fire exactly once per ResetForTests call");
            }
            finally
            {
                UI.OnReset -= handler;
            }
        }
    }
}
