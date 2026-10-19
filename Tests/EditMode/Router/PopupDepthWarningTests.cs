using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    /// <summary>
    /// Spec 2026-10-19 §4.9: a popup chain k deep sorts its top panel 2 + 2k above its page. On a routed
    /// page that has to stay below the next page depth (<c>PageSortingOrderStep</c>); outgrowing it warns
    /// once, and the popup still opens.
    /// </summary>
    public class PopupDepthWarningTests
    {
        private const string HomeXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='home'>
  <Frame id='h0' size='10x10'><Frame id='h1' size='10x10'><Frame id='h2' size='10x10'><Frame id='h3' size='10x10'><Frame id='h4' size='10x10'/></Frame></Frame></Frame></Frame>
</Screen></PromptUGUI>";

        private readonly List<string> _warnings = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _warnings.Clear();   // NUnit reuses the fixture instance across tests
            var files = new Dictionary<string, string> { ["home"] = HomeXml };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UnityEngine.Application.logMessageReceived += Capture;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Application.logMessageReceived -= Capture;
            UI.ResetForTests();
        }

        private void Capture(string message, string _, LogType type)
        {
            if (type == LogType.Warning && message.Contains("PageSortingOrderStep")) _warnings.Add(message);
        }

        // A chain of popups each hosted in the previous one's panel: depth = index.
        private static List<PopupCore> Nest(int count)
        {
            var home = UI.Get("home");
            var cores = new List<PopupCore>();
            for (var i = 0; i < count; i++)
            {
                var host = home.Get<Frame>("h" + i);
                var panel = home.Get<Frame>("h" + (i + 1)).RectTransform;
                cores.Add(new PopupCore(host, panel, PopupDismiss.Owner, cageFocus: false, cancelButtonCloses: true)
                {
                    Measure = () => new PopupLayout(new Vector2(10f, 10f), 0f),
                    TransitionSeconds = 0f,
                });
            }
            return cores;
        }

        [Test]
        public void A_routed_page_warns_once_when_the_chain_outgrows_the_page_step()
        {
            UI.Router.PageSortingOrderStep = 4;
            UI.Router.Open("home").GetAwaiter().GetResult();
            var cores = Nest(3);

            cores[0].Open();
            Assert.IsEmpty(_warnings, "depth 0 sorts at +2, below the next page at +4");
            cores[1].Open();
            Assert.AreEqual(1, _warnings.Count, "depth 1 sorts at +4: level with the next page");
            Assert.IsTrue(cores[1].IsOpen, "it still opens");
            cores[2].Open();
            Assert.AreEqual(1, _warnings.Count, "once");
        }

        [Test]
        public void The_default_step_holds_three_levels()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            var cores = Nest(4);
            foreach (var c in cores) c.Open();
            Assert.AreEqual(4, PopupChain.Count);
            Assert.IsEmpty(_warnings, "+8 is below the next page at +10");
        }
    }
}
