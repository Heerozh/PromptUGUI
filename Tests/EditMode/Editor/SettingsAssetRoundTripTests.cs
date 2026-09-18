using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using UnityEditorInternal;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// <see cref="SettingsAssetReader"/> understands one fixed YAML shape and keys off field names
    /// (<c>commonLibraries</c> / <c>src</c> / <c>as</c>). Only a real serialization can prove that a
    /// <see cref="PromptUGUISettings"/> still writes that shape — a renamed field would otherwise
    /// make the CLI silently read no commons. 2026-09-18 commons-settings spec §7-26.
    ///
    /// <para>Written with <c>SaveToSerializedFileAndForget</c> (the text form
    /// <c>AssetDatabase.CreateAsset</c> produces, minus the import) so the project's own settings
    /// asset is not joined by a second one — <c>PromptUGUISettingsAutoMaintainer</c> rightly
    /// complains about that.</para>
    /// </summary>
    public class SettingsAssetRoundTripTests
    {
        private string _path;
        private PromptUGUISettings _settings;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "PromptUGUI-settings-" + Guid.NewGuid().ToString("N") + ".asset");
            _settings = ScriptableObject.CreateInstance<PromptUGUISettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
            if (File.Exists(_path)) File.Delete(_path);
        }

        private string Serialize()
        {
            InternalEditorUtility.SaveToSerializedFileAndForget(
                new UnityEngine.Object[] { _settings }, _path, allowTextSerialization: true);
            return File.ReadAllText(_path);
        }

        [Test]
        public void ASerializedSettingsAsset_ReadsBackItsCommonLibraries()
        {
            _settings.commonLibraries = new List<CommonLibraryEntry>
            {
                new CommonLibraryEntry { src = "UI/Templates/DefaultTheme.ui.xml" },
                new CommonLibraryEntry { src = "it's: odd/lib.ui", @as = "ml" },
            };

            var text = Serialize();

            Assert.IsTrue(SettingsAssetReader.IsPromptUGUISettings(text), "the script guid is how the CLI finds the asset");
            var read = SettingsAssetReader.ReadCommonLibraries(text);
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual("UI/Templates/DefaultTheme.ui.xml", read[0].src);
            Assert.IsNull(read[0].ToImportRef().Namespace);
            Assert.AreEqual("it's: odd/lib.ui", read[1].src, "Unity's quoting is undone");
            Assert.AreEqual("ml", read[1].ToImportRef().Namespace);
        }

        [Test]
        public void AnEmptyList_SerializesToTheInlineForm_TheReaderAccepts()
        {
            var text = Serialize();

            StringAssert.Contains("commonLibraries: []", text);
            Assert.IsEmpty(SettingsAssetReader.ReadCommonLibraries(text));
        }
    }
}
