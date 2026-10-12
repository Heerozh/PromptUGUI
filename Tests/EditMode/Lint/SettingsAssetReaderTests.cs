using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// The UIXmlLint CLI runs outside Unity, so it reads <c>commonLibraries</c> straight out of the
    /// text-serialized <c>PromptUGUISettings</c> asset (2026-09-18 commons-settings spec §4.9). This
    /// pins the subset of Unity's YAML the reader understands; the EditorOnly round-trip test pins
    /// that a real asset serializes into exactly that subset.
    /// </summary>
    public class SettingsAssetReaderTests
    {
        private const string Head = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ef72af4be0229a24cb2ab979147bdc01, type: 3}
  m_Name: PromptUGUI_Settings
  m_EditorClassIdentifier:
  fontTypes:
  - default
  locales: []
  externalPoRoots: []
";

        [Test]
        public void IsPromptUGUISettings_RecognisesTheScriptGuid()
        {
            Assert.IsTrue(SettingsAssetReader.IsPromptUGUISettings(Head));
            Assert.IsFalse(SettingsAssetReader.IsPromptUGUISettings(
                Head.Replace(SettingsAssetReader.ScriptGuid, "0123456789abcdef0123456789abcdef")));
            Assert.IsFalse(SettingsAssetReader.IsPromptUGUISettings(null));
        }

        [Test]
        public void TwoEntries_PlainAndQuoted_BlankAsIsNull()
        {
            var yaml = Head + @"  commonLibraries:
  - src: UI/Templates/DefaultTheme.ui.xml
    as:
  - src: 'third:party/lib.ui'
    as: ml
";

            var entries = SettingsAssetReader.ReadCommonLibraries(yaml);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("UI/Templates/DefaultTheme.ui.xml", entries[0].src);
            Assert.IsNull(entries[0].ToImportRef().Namespace, "an empty as: is no namespace");
            Assert.AreEqual("third:party/lib.ui", entries[1].src, "single quotes are Unity's, not the value's");
            Assert.AreEqual("ml", entries[1].ToImportRef().Namespace);
        }

        [Test]
        public void EmptyList_AndMissingKey_ReadAsNoEntries()
        {
            Assert.IsEmpty(SettingsAssetReader.ReadCommonLibraries(Head + "  commonLibraries: []\n"));
            Assert.IsEmpty(SettingsAssetReader.ReadCommonLibraries(Head), "an asset saved before the field existed");
        }

        [Test]
        public void ListEnds_WhereTheNextFieldStarts()
        {
            var yaml = Head + @"  commonLibraries:
  - src: a.ui
    as:
  somethingElse:
  - src: not-a-library.ui
    as:
";

            var entries = SettingsAssetReader.ReadCommonLibraries(yaml);

            Assert.AreEqual(new[] { "a.ui" }, entries.Select(e => e.src).ToArray());
        }

        [Test]
        public void QuotedStrings_UnescapeUnitysDoubledQuote_AndDoubleQuotes()
        {
            var yaml = Head + @"  commonLibraries:
  - src: 'it''s.ui'
    as: ""dq""
";

            var entries = SettingsAssetReader.ReadCommonLibraries(yaml);

            Assert.AreEqual("it's.ui", entries.Single().src);
            Assert.AreEqual("dq", entries.Single().@as);
        }

        [Test]
        public void CrlfInput_IsReadTheSame()
        {
            var yaml = (Head + "  commonLibraries:\n  - src: a.ui\n    as: ns\n").Replace("\n", "\r\n");

            var entry = SettingsAssetReader.ReadCommonLibraries(yaml).Single();

            Assert.AreEqual("a.ui", entry.src);
            Assert.AreEqual("ns", entry.@as);
        }

        // ── font floor (2026-10-12 font-floor spec §5.7) ───────────────────────────────────────

        [Test]
        public void FontFloor_ReadsTheFloorAndItsTargets()
        {
            var yaml = Head + @"  commonLibraries: []
  fontFloor: 11.5
  fontFloorTargets:
  - name: iPhone SE
    screen: 750x1334@2x
    variants:
  - name: 'TV: living room'
    screen: 1920x1080@2x
    variants: tv mobile
";

            Assert.AreEqual(11.5f, SettingsAssetReader.ReadFontFloor(yaml));
            var targets = SettingsAssetReader.ReadFontFloorTargets(yaml);
            Assert.AreEqual(2, targets.Count);
            Assert.AreEqual("iPhone SE", targets[0].name);
            Assert.AreEqual("750x1334@2x", targets[0].screen);
            Assert.IsEmpty(targets[0].variants);
            Assert.AreEqual("TV: living room", targets[1].name, "Unity's quoting is undone");
            Assert.AreEqual("1920x1080@2x", targets[1].screen);
            Assert.AreEqual("tv mobile", targets[1].variants);
        }

        [Test]
        public void FontFloor_Absent_ReadsTheFieldDefault_AsUnityWould()
        {
            // An asset saved before the field existed: Unity deserializes the missing field at its
            // initializer, so the CLI reading the same file must agree.
            Assert.AreEqual(FontFloorPolicy.DefaultFloor, SettingsAssetReader.ReadFontFloor(Head));
            Assert.IsEmpty(SettingsAssetReader.ReadFontFloorTargets(Head));

            var emptyList = Head + "  fontFloorTargets: []\n";
            Assert.IsEmpty(SettingsAssetReader.ReadFontFloorTargets(emptyList));
            Assert.AreEqual(FontFloorPolicy.DefaultFloor, SettingsAssetReader.ReadFontFloor(emptyList),
                "fontFloorTargets is not fontFloor");
            Assert.AreEqual(0f, SettingsAssetReader.ReadFontFloor(Head + "  fontFloor: 0\n"), "an explicit 0 is off");
        }

        [Test]
        public void TmpDefaultFontSize_IsReadFromTheTmpSettingsAsset()
        {
            const string tmp = "MonoBehaviour:\n  m_Name: TMP Settings\n  m_defaultFontSize: 28\n  m_defaultAutoSizeMinRatio: 0.5\n";

            Assert.AreEqual(28f, SettingsAssetReader.ReadTmpDefaultFontSize(tmp));
            Assert.IsNull(SettingsAssetReader.ReadTmpDefaultFontSize(Head));
        }
    }
}
