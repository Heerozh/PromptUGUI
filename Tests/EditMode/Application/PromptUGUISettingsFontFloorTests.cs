using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Application
{
    /// <summary>The Inspector-side mirror of <c>LintRun</c>'s target checks (2026-10-12 font-floor spec §5.1).</summary>
    public class PromptUGUISettingsFontFloorTests
    {
        private PromptUGUISettings _settings;

        [SetUp] public void SetUp() => _settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(_settings);

        private void Validate() =>
            typeof(PromptUGUISettings)
                .GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_settings, null);

        [Test]
        public void OnValidate_ReportsAMalformedScreen_AndAHandWrittenOrientation()
        {
            _settings.fontFloorTargets = new List<FontFloorTarget>
            {
                new FontFloorTarget { name = "SE", screen = "750*1334" },
                new FontFloorTarget { name = "PC", screen = "1920x1080", variants = "landscape" },
                new FontFloorTarget { name = "ok", screen = "1920x1080@1.5x", variants = "desktop" },
                new FontFloorTarget(),   // blank: inert, not wrong
            };
            LogAssert.Expect(LogType.Error, new Regex(@"fontFloorTargets\[0\]: font-floor target 'SE': screen=""750\*1334"""));
            LogAssert.Expect(LogType.Error, new Regex(@"fontFloorTargets\[1\]: font-floor target 'PC'.*'landscape'"));

            Validate();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnValidate_ClampsANegativeFloorToZero()
        {
            _settings.fontFloor = -3f;

            Validate();

            Assert.AreEqual(0f, _settings.fontFloor);
        }

        [Test]
        public void TheRuleIsOffByDefault()
        {
            Assert.AreEqual(0f, _settings.fontFloor);
            Assert.IsEmpty(_settings.fontFloorTargets);
        }
    }
}
