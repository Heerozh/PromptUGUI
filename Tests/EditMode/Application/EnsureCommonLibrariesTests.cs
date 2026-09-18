using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using UnityEngine;
using UnityEngine.TestTools;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <c>UI.EnsureCommonLibrariesAsync</c>: the common libraries a project declares in
    /// <c>PromptUGUISettings.commonLibraries</c> are loaded idempotently, in order, once — and every
    /// path that merges the commons pool (<c>LoadDocumentAsync</c>, the modal loader,
    /// <c>ReloadAsync</c>) ensures them first, so a host writes no boot code for them and a
    /// <c>UnloadAll</c> heals on the next load. 2026-09-18 commons-settings spec §4.2–§4.5.
    /// </summary>
    public class EnsureCommonLibrariesTests
    {
        private const string Prolog = "<?xml version='1.0'?><PromptUGUI version='1'>";

        private Dictionary<string, string> _files;
        private Dictionary<string, int> _fetches;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>
            {
                ["a"] = Prolog + "<Template name='A'><Frame/></Template><Style name='chip' color='#fff'/></PromptUGUI>",
                ["b"] = Prolog + "<Template name='B'><Frame/></Template><Style name='card' color='#000'/></PromptUGUI>",
                ["m"] = Prolog + "<Screen name='M'><A id='a'/><ui.B id='b'/><Frame id='f' class='chip ui:card'/></Screen></PromptUGUI>",
                ["bare"] = Prolog + "<Screen name='Bare'><A id='a'/><B id='b'/></Screen></PromptUGUI>",
            };
            _fetches = new Dictionary<string, int>();
            UI.SourceResolver = Counting(src => _files.TryGetValue(src, out var v) ? v : null);
            UI.CommonLibrariesForTests = () => Entries(("a", null), ("b", "ui"));
        }

        [TearDown]
        public void TearDown()
        {
            PromptUGUISettings.FinderForTests = null;
            UI.ResetForTests();
        }

        private Func<string, Awaitable<string>> Counting(Func<string, string> read) => src =>
        {
            _fetches[src] = Fetches(src) + 1;
            return AwaitableHelpers.Completed(read(src));
        };

        private int Fetches(string src) => _fetches.TryGetValue(src, out var n) ? n : 0;

        private static List<CommonLibraryEntry> Entries(params (string Src, string As)[] rows)
        {
            var list = new List<CommonLibraryEntry>();
            foreach (var (src, ns) in rows) list.Add(new CommonLibraryEntry { src = src, @as = ns });
            return list;
        }

        private static void Ensure() => UI.EnsureCommonLibrariesAsync().GetAwaiter().GetResult();

        private static Screen OpenM()
        {
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            return UI.Open("M");
        }

        // ── loading ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Ensure_LoadsEveryEntry_InOrder_UnderItsNamespace()
        {
            Ensure();

            var s = OpenM();
            Assert.IsNotNull(s.Get<Frame>("a"), "bare <A/> from the first library");
            Assert.IsNotNull(s.Get<Frame>("b"), "<ui.B/> from the second, loaded as='ui'");
            Assert.IsNotNull(s.Get<Frame>("f"), "class='chip ui:card' resolved against both");
        }

        [Test]
        public void Ensure_Twice_DoesNotTouchTheResolverAgain()
        {
            Ensure();
            var before = (Fetches("a"), Fetches("b"));

            Ensure();

            Assert.AreEqual(before, (Fetches("a"), Fetches("b")), "every entry is already in the pool");
        }

        [Test]
        public void Ensure_AfterUnloadAll_ReadsTheLibrariesAgain()
        {
            Ensure();
            UI.UnloadAll();

            Ensure();

            Assert.AreEqual(2, Fetches("a"), "UnloadAll drops the pool AND the source cache; the next Ensure re-reads");
            Assert.IsNotNull(OpenM().Get<Frame>("b"));
        }

        [Test]
        public void Ensure_SkipsEntriesAlreadyLoaded_ByOtherMeans()
        {
            UI.LoadCommonLibraryAsync("a").GetAwaiter().GetResult();

            Assert.DoesNotThrow(Ensure, "re-loading 'a' would be a commons conflict; Ensure must skip it");
            Assert.AreEqual(1, Fetches("a"));
            Assert.AreEqual(1, Fetches("b"));
        }

        [Test]
        public void Ensure_Concurrent_SharesOneLoad()
        {
            var pending = new Dictionary<string, AwaitableCompletionSource<string>>();
            UI.SourceResolver = src =>
            {
                _fetches[src] = Fetches(src) + 1;
                var acs = new AwaitableCompletionSource<string>();
                pending[src] = acs;
                return acs.Awaitable;
            };

            var first = UI.EnsureCommonLibrariesAsync();
            var second = UI.EnsureCommonLibrariesAsync();
            Assert.AreEqual(1, Fetches("a"), "one load in flight, the second caller waits on it");
            Assert.IsFalse(pending.ContainsKey("b"), "entries load in order: 'b' waits for 'a'");

            pending["a"].SetResult(_files["a"]);
            pending["b"].SetResult(_files["b"]);
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();

            Assert.AreEqual((1, 1), (Fetches("a"), Fetches("b")));
            // Back to a completing resolver: a fetch left pending here would sit in
            // DocumentCache's in-flight table past ResetForTests and stall every later test for 'm'.
            UI.SourceResolver = Counting(src => _files.TryGetValue(src, out var v) ? v : null);
            Assert.IsNotNull(OpenM().Get<Frame>("b"));
        }

        // ── resolver ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void Ensure_WithNoEntries_IsANoop_EvenWithoutAResolver()
        {
            UI.CommonLibrariesForTests = () => Entries();
            UI.SourceResolver = null;

            Assert.DoesNotThrow(Ensure);
        }

        [Test]
        public void Ensure_WithEntries_ButNoResolver_Throws()
        {
            UI.SourceResolver = null;

            Assert.Throws<InvalidOperationException>(Ensure);
        }

        // ── failure ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Ensure_FailingEntry_Throws_KeepsEarlierOnes_AndResumesThereNextTime()
        {
            _files.Remove("b");

            Assert.Throws<System.IO.IOException>(Ensure, "'b' cannot be read");
            Assert.AreEqual(1, Fetches("a"), "'a' loaded before the failure");

            _files["b"] = Prolog + "<Template name='B'><Frame/></Template><Style name='card' color='#000'/></PromptUGUI>";
            Assert.DoesNotThrow(Ensure, "'a' is skipped (it is in the pool), only 'b' is loaded now");
            Assert.AreEqual(1, Fetches("a"));
            Assert.IsNotNull(OpenM().Get<Frame>("b"));
        }

        // ── automatic ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void LoadDocumentAsync_EnsuresFirst()
        {
            var s = OpenM();

            Assert.IsNotNull(s.Get<Frame>("b"), "no explicit Ensure anywhere");
        }

        [Test]
        public void ModalLoad_EnsuresFirst()
        {
            var modal = Prolog + "<Screen name='Modal'><A id='a'/><ui.B id='b'/></Screen></PromptUGUI>";

            UI.LoadDocumentWithCommonsAsync("Modal", modal).GetAwaiter().GetResult();

            Assert.IsNotNull(UI.Open("Modal").Get<Frame>("b"));
        }

        [Test]
        public void ReloadAsync_EnsuresFirst()
        {
            UI.CommonLibrariesForTests = () => Entries();
            UI.LoadDocumentAsync("bare").GetAwaiter().GetResult();
            // The libraries appear in settings only now (edited during Play): a reload still sees them.
            UI.CommonLibrariesForTests = () => Entries(("a", null), ("b", null));

            UI.ReloadAsync("Bare").GetAwaiter().GetResult();

            Assert.IsNotNull(UI.Open("Bare").Get<Frame>("b"));
        }

        // ── reload keeps the namespace (M4-R8) ─────────────────────────────────────────────────

        [Test]
        public void ReloadCommonLibrary_KeepsTheNamespaceFromSettings()
        {
            Ensure();

            UI.ReloadCommonLibraryAsync("b").GetAwaiter().GetResult();

            Assert.IsNotNull(OpenM().Get<Frame>("b"), "<ui.B/> must still resolve — the reload used to drop as='ui'");
        }

        [Test]
        public void ReloadCommonLibrary_FailureRollsBack_WithTheNamespace()
        {
            Ensure();
            _files.Remove("b");

            Assert.Throws<System.IO.IOException>(() => UI.ReloadCommonLibraryAsync("b").GetAwaiter().GetResult());

            Assert.IsNotNull(OpenM().Get<Frame>("b"), "the pre-reload pool is back, ui namespace included");
        }

        // ── where the entries come from ────────────────────────────────────────────────────────

        [Test]
        public void WithoutTheTestSeam_EntriesComeFromTheSettingsInstance()
        {
            var settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
            try
            {
                settings.commonLibraries = Entries(("a", null), ("   ", null), ("b", "  "));
                PromptUGUISettings.FinderForTests = () => new[] { settings };
                PromptUGUISettings.ResetInstanceCache();
                UI.CommonLibrariesForTests = null;

                Ensure();

                UI.LoadDocumentAsync("bare").GetAwaiter().GetResult();
                var s = UI.Open("Bare");
                Assert.IsNotNull(s.Get<Frame>("a"));
                Assert.IsNotNull(s.Get<Frame>("b"), "a blank as= is no namespace; a blank src row is skipped");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ResetForTests_HidesTheProjectsRealSettings()
        {
            UI.ResetForTests();

            Assert.IsNotNull(UI.CommonLibrariesForTests, "tests never see the host project's commons");
            Assert.IsEmpty(UI.CommonLibrariesForTests());
        }

        // ── settings validation ────────────────────────────────────────────────────────────────

        [Test]
        public void OnValidate_ReportsDuplicateSrc_AndADottedNamespace()
        {
            var settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
            try
            {
                settings.commonLibraries = Entries(("a", null), ("a", "x"), ("b", "ui.v2"), ("", null));
                LogAssert.Expect(LogType.Error, new Regex("Duplicate common library src 'a'"));
                LogAssert.Expect(LogType.Error, new Regex("as='ui\\.v2'.*must not contain '\\.'"));

                typeof(PromptUGUISettings)
                    .GetMethod("OnValidate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(settings, null);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
