using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class SpriteSetTiledEntryTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void BuildLookup_registers_tiled_entries()
        {
            var tiled = Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var plain = Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

            var set = ScriptableObject.CreateInstance<SpriteSet>();
            var so = new UnityEditor.SerializedObject(set);
            so.FindProperty("setName").stringValue = "t";
            so.ApplyModifiedPropertiesWithoutUndo();
            set.SetEntriesInternal(new List<(string, Sprite, bool)>
            {
                ("vine", tiled, true),
                ("leaf", plain, false),
            });

            SpriteResolverHelpers.UseSpriteSetResolver(new[] { set });

            Assert.AreSame(tiled, UI.ResolveSprite("t:vine"));
            Assert.IsTrue(PromptUGUI.Application.Internal.SpriteRenderHints.IsTiled(tiled));
            Assert.IsFalse(PromptUGUI.Application.Internal.SpriteRenderHints.IsTiled(plain));
        }
    }
}
