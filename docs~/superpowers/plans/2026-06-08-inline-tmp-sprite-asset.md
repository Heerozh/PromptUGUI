# Inline TMP Sprite Asset (图文混排) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let authors flag a `SpriteSet` so its sprites are baked into a single global `TMP_SpriteAsset`, enabling native TMP `<sprite name="coin">` 图文混排 inside any `<Text>`/`<Btn>` label and dynamic chat-emoji that wrap with the text.

**Architecture:** A new `[SerializeField] bool generateTmpSpriteAsset` opt-in on `SpriteSet` (Runtime, behaviorally inert). A new Editor-only `InlineSpriteAssetBuilder` collects sprites from *flagged* sets (via the existing `EnumerateSpriteSources` + `BuildLookup` bare-name promotion), merges them into one `TMP_SpriteAsset` (collision across sets = hard error), packs a dedicated point-filtered RGBA32 texture (independent of the `.spriteatlas`), and assigns it as `TMP_Settings` default sprite asset. The existing `Tools → PromptUGUI → Sprite → Sync ...` actions call it after the atlas sync; no flagged sets → nothing generated, TMP settings untouched. No XML/public-runtime API change — authoring uses native TMP rich-text.

**Tech Stack:** Unity 6, TextMeshPro (`TMPro`, `UnityEngine.TextCore` GlyphRect/GlyphMetrics), `UnityEditor.AssetDatabase`, NUnit EditorOnly tests run via Unity MCP.

---

## Pre-flight

We are on `main`, and **committing to main is forbidden** (CLAUDE.md). Create the feature branch before Task 1:

```bash
git checkout -b feat/inline-tmp-sprite
```

Plan filenames/paths in this repo live under `docs~/` (Unity ignores `~` folders) — already used for this plan. The lint + test loop for every code task:

```bash
# after C# edits — compile + console check + run the affected EditorOnly tests via Unity MCP:
#   mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
#   mcp__UnityMCP__read_console(action="get", types=["error"])
#   mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="InlineSpriteAssetBuilder")
# lint:
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

---

## File Structure

- **Modify** `Runtime/Application/SpriteSet.cs` — add the inert `generateTmpSpriteAsset` opt-in + getter.
- **Create** `Editor/InlineSpriteAssetBuilder.cs` — all generation logic (collect → merge/collision → pack → create asset → wire TMP default).
- **Modify** `Editor/SpriteAtlasMenu.cs` — call `InlineSpriteAssetBuilder.RegenerateFromProject()` after each sync action.
- **Modify** `Editor/SpriteSetEditor.cs` — "Sync This Set" button also regenerates the inline asset (one line); the checkbox itself renders automatically via `DrawDefaultInspector`.
- **Create** `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs` — pure-merge + collision + integration tests.
- **Modify** `.claude/skills/authoring-promptugui-xml/reference/icons.md` (+ a pointer line in the main XML SKILL.md) — document the new authoring capability.

---

### Task 1: `SpriteSet.generateTmpSpriteAsset` opt-in (Runtime, inert)

**Files:**
- Modify: `Runtime/Application/SpriteSet.cs`
- Test: `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs` (new file — first test goes here)

- [ ] **Step 1: Write the failing test**

Create `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs`:

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class InlineSpriteAssetBuilderTests
    {
        [Test]
        public void GenerateTmpSpriteAsset_defaults_false_and_reflects_serialized_value()
        {
            var set = ScriptableObject.CreateInstance<SpriteSet>();
            try
            {
                Assert.IsFalse(set.GenerateTmpSpriteAsset, "default must be false");

                var so = new SerializedObject(set);
                so.FindProperty("generateTmpSpriteAsset").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(set.GenerateTmpSpriteAsset);
            }
            finally { Object.DestroyImmediate(set); }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run via MCP: `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], filter="InlineSpriteAssetBuilder")`
Expected: COMPILE ERROR — `SpriteSet` has no member `GenerateTmpSpriteAsset` and no `generateTmpSpriteAsset` property.

- [ ] **Step 3: Add the field + getter**

In `Runtime/Application/SpriteSet.cs`, after the `alwaysInclude` field declaration (`[SerializeField] private List<string> alwaysInclude = new();`), add:

```csharp
        // Editor-only opt-in: when true, this set's sprites are baked into the global
        // inline TMP_SpriteAsset by SpriteAtlasSyncer / InlineSpriteAssetBuilder so they
        // can be used as <sprite name="..."> inside <Text>/<Btn>. Runtime ignores this.
        [SerializeField] private bool generateTmpSpriteAsset;
```

And alongside the other public getters (near `public IReadOnlyList<string> AlwaysInclude => alwaysInclude;`):

```csharp
        public bool GenerateTmpSpriteAsset => generateTmpSpriteAsset;
```

- [ ] **Step 4: Run test to verify it passes**

Run via MCP (refresh first): `refresh_unity` → `read_console(types=["error"])` (expect none) → `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/SpriteSet.cs Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs
git commit -m "feat(spriteset): add inert generateTmpSpriteAsset opt-in flag"
```

---

### Task 2: Pure glyph-table merge + cross-set collision

**Files:**
- Create: `Editor/InlineSpriteAssetBuilder.cs`
- Test: `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs`

The merge is the one piece of logic worth isolating: given flat `(setName, name, sprite)` candidates, dedupe and reject names that appear in more than one set (mirrors how the atlas syncer treats `<Icon>` collisions — hard error, no silent overwrite).

- [ ] **Step 1: Write the failing tests**

Append to `InlineSpriteAssetBuilderTests.cs` (add `using System.Collections.Generic;` and `using UnityEngine;` at top if not present):

```csharp
        private static Sprite Dummy()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4f);
        }

        [Test]
        public void BuildInlineGlyphTable_merges_distinct_names_across_sets()
        {
            var candidates = new List<(string set, string name, Sprite sprite)>
            {
                ("ui", "coin", Dummy()),
                ("emoji", "smile", Dummy()),
            };

            var glyphs = InlineSpriteAssetBuilder.BuildInlineGlyphTable(candidates, out var collisions);

            Assert.IsEmpty(collisions);
            CollectionAssert.AreEquivalent(
                new[] { "coin", "smile" }, glyphs.ConvertAll(g => g.name));
        }

        [Test]
        public void BuildInlineGlyphTable_reports_cross_set_name_collision()
        {
            var candidates = new List<(string set, string name, Sprite sprite)>
            {
                ("ui", "heart", Dummy()),
                ("emoji", "heart", Dummy()),
            };

            var glyphs = InlineSpriteAssetBuilder.BuildInlineGlyphTable(candidates, out var collisions);

            Assert.That(collisions, Does.Contain("heart"));
            Assert.IsEmpty(glyphs, "no glyphs emitted when a collision aborts the merge");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run via MCP: `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: COMPILE ERROR — `InlineSpriteAssetBuilder` does not exist.

- [ ] **Step 3: Create the builder with the pure merge**

Create `Editor/InlineSpriteAssetBuilder.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// Editor-only: bakes the sprites of every SpriteSet flagged
    /// <c>generateTmpSpriteAsset</c> into a single global <see cref="TMPro.TMP_SpriteAsset"/>
    /// so authors can use native TMP <c>&lt;sprite name="..."&gt;</c> inline markup.
    /// The pure <see cref="BuildInlineGlyphTable"/> is unit-tested; the asset I/O lives in
    /// <see cref="Generate"/> / <see cref="RegenerateFromProject"/>.
    /// </summary>
    public static partial class InlineSpriteAssetBuilder
    {
        public struct Glyph
        {
            public string name;
            public Sprite sprite;
        }

        /// <summary>Merge flat (set, name, sprite) candidates into a unique-by-name glyph
        /// list. A name present in more than one set is a hard collision: it is added to
        /// <paramref name="collisions"/> and the merge returns an EMPTY list (caller aborts,
        /// matching the atlas syncer's no-silent-overwrite contract).</summary>
        public static List<Glyph> BuildInlineGlyphTable(
            IReadOnlyList<(string set, string name, Sprite sprite)> candidates,
            out List<string> collisions)
        {
            collisions = new List<string>();
            var owner = new Dictionary<string, string>(System.StringComparer.Ordinal);   // name -> setName
            var ordered = new List<Glyph>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var (set, name, sprite) in candidates)
            {
                if (string.IsNullOrEmpty(name) || sprite == null) continue;
                if (owner.TryGetValue(name, out var prevSet))
                {
                    if (prevSet != set && !collisions.Contains(name)) collisions.Add(name);
                    continue;
                }
                owner[name] = set;
                if (seen.Add(name)) ordered.Add(new Glyph { name = name, sprite = sprite });
            }

            if (collisions.Count > 0)
            {
                Debug.LogError(
                    "[InlineSprite] glyph name collision across flagged SpriteSets: " +
                    string.Join(", ", collisions) + ". Rename so every inline glyph is unique.");
                return new List<Glyph>();
            }
            return ordered;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

The collision test logs an expected error — guard it. Wrap the collision-test body in `LogAssert.Expect(LogType.Error, ...)` OR add at the top of `BuildInlineGlyphTable_reports_cross_set_name_collision`:

```csharp
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
```
and reset it to `false` at the end of that test (mirrors the established pattern in `SpriteAtlasSyncerTests.Scan_skips_dynamic_icon_outside_template`).

Run via MCP: `refresh_unity` → `read_console(types=["error"])` → `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Editor/InlineSpriteAssetBuilder.cs Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs
git commit -m "feat(inline-sprite): pure glyph merge + cross-set collision detection"
```

---

### Task 3: Collect bare-named candidates from flagged sets

**Files:**
- Modify: `Editor/InlineSpriteAssetBuilder.cs`
- Test: `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs`

Reuse `SpriteAtlasSyncer.EnumerateSpriteSources` + `BuildLookup`: the lookup already promotes every **unambiguous bare basename** to the sprite (keys containing `/` are path-only and excluded from inline naming). Glyph name = bare key.

- [ ] **Step 1: Write the failing test**

Append to `InlineSpriteAssetBuilderTests.cs`. This test builds a real temp folder with two PNG sprites and a flagged SpriteSet (`SourceFolder` is `DefaultAsset` — assign via `SerializedObject`):

```csharp
        private const string TestRoot = "Assets/__test_inlinesprite__";
        private readonly List<string> _cleanup = new();

        [TearDown]
        public void Teardown()
        {
            foreach (var p in _cleanup) AssetDatabase.DeleteAsset(p);
            _cleanup.Clear();
            if (AssetDatabase.IsValidFolder(TestRoot)) AssetDatabase.DeleteAsset(TestRoot);
        }

        private string WriteSpritePng(string folder, string name)
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 0, 255);
            tex.SetPixels32(px); tex.Apply();
            var path = $"{folder}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG().Length == 0 ? new byte[0] : tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
            Object.DestroyImmediate(tex);
            return path;
        }

        private SpriteSet MakeFlaggedSet(string setName, string folderPath)
        {
            var set = ScriptableObject.CreateInstance<SpriteSet>();
            var so = new SerializedObject(set);
            so.FindProperty("setName").stringValue = setName;
            so.FindProperty("generateTmpSpriteAsset").boolValue = true;
            so.FindProperty("sourceFolder").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            var setPath = $"{folderPath}/{setName}.asset";
            AssetDatabase.CreateAsset(set, setPath);
            _cleanup.Add(setPath);
            return set;
        }

        [Test]
        public void CollectCandidates_returns_bare_names_from_flagged_set()
        {
            AssetDatabase.CreateFolder("Assets", "__test_inlinesprite__");
            WriteSpritePng(TestRoot, "coin");
            WriteSpritePng(TestRoot, "smile");
            var set = MakeFlaggedSet("ui", TestRoot);

            var candidates = InlineSpriteAssetBuilder.CollectCandidates(new[] { set });
            var names = candidates.ConvertAll(c => c.name);

            CollectionAssert.AreEquivalent(new[] { "coin", "smile" }, names);
            Assert.That(candidates.TrueForAll(c => c.set == "ui"));
        }
```

> Note: `SpriteSet.SourceFolder`/`sourceFolder` is wrapped in `#if UNITY_EDITOR` and is a `DefaultAsset` — these tests are EditorOnly so that compiles.

- [ ] **Step 2: Run test to verify it fails**

Run via MCP: `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: COMPILE ERROR — `CollectCandidates` does not exist.

- [ ] **Step 3: Implement `CollectCandidates`**

Add to `InlineSpriteAssetBuilder` (same file). It depends on `SpriteAtlasSyncer.EnumerateSpriteSources` + `BuildLookup` (both already `public`/`internal` and visible inside `PromptUGUI.Editor`):

```csharp
        /// <summary>Flatten flagged sets into (set, bareName, sprite) candidates. Only
        /// unambiguous bare basenames become inline glyph names — path-only keys (those
        /// containing '/') and bare names that collide *within* a set are dropped by the
        /// reused BuildLookup promotion rule.</summary>
        public static List<(string set, string name, Sprite sprite)> CollectCandidates(
            IReadOnlyList<PromptUGUI.Application.SpriteSet> flaggedSets)
        {
            var result = new List<(string, string, Sprite)>();
            foreach (var set in flaggedSets)
            {
                if (set == null || string.IsNullOrEmpty(set.SetName)) continue;
                var entries = SpriteAtlasSyncer.EnumerateSpriteSources(set.SourceFolderPath);
                var lookup = SpriteAtlasSyncer.BuildLookup(entries, out _);
                foreach (var kv in lookup)
                {
                    if (kv.Key.IndexOf('/') >= 0) continue;        // path-only → not inline-addressable
                    result.Add((set.SetName, kv.Key, kv.Value));
                }
            }
            return result;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run via MCP: `refresh_unity` → `read_console(types=["error"])` → `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Editor/InlineSpriteAssetBuilder.cs Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs
git commit -m "feat(inline-sprite): collect bare-named candidates from flagged sets"
```

---

### Task 4: Pack texture, build the TMP_SpriteAsset, wire TMP default

**Files:**
- Modify: `Editor/InlineSpriteAssetBuilder.cs`
- Test: `Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs`

This is the Unity-heavy task. `Generate` orchestrates: collect → merge (abort on collision) → pack a dedicated point-filtered RGBA32 sheet → build glyph/character tables → save `.asset` (texture + material as sub-assets) → assign as `TMP_Settings` default sprite asset. Returns the asset, or `null` when there are no glyphs.

- [ ] **Step 1: Write the failing integration test**

Append to `InlineSpriteAssetBuilderTests.cs`:

```csharp
        [Test]
        public void Generate_creates_sprite_asset_with_expected_characters_and_sets_tmp_default()
        {
            AssetDatabase.CreateFolder("Assets", "__test_inlinesprite__");
            WriteSpritePng(TestRoot, "coin");
            WriteSpritePng(TestRoot, "smile");
            var set = MakeFlaggedSet("ui", TestRoot);
            var outPath = $"{TestRoot}/InlineSprites.asset";
            _cleanup.Add(outPath);

            var asset = InlineSpriteAssetBuilder.Generate(new[] { set }, outPath);

            Assert.IsNotNull(asset);
            var names = new List<string>();
            foreach (var ch in asset.spriteCharacterTable) names.Add(ch.name);
            CollectionAssert.AreEquivalent(new[] { "coin", "smile" }, names);
            Assert.AreEqual(asset.spriteCharacterTable.Count, asset.spriteGlyphTable.Count);
            Assert.IsNotNull(asset.spriteSheet, "must have a packed texture");
            Assert.AreSame(asset, TMPro.TMP_Settings.defaultSpriteAsset);
        }

        [Test]
        public void Generate_returns_null_when_no_glyphs()
        {
            Assert.IsNull(InlineSpriteAssetBuilder.Generate(
                new PromptUGUI.Application.SpriteSet[0], $"{TestRoot}/none.asset"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run via MCP: `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: COMPILE ERROR — `Generate` does not exist.

- [ ] **Step 3: Implement packing + asset creation**

Add to `InlineSpriteAssetBuilder.cs`. Add `using` directives at the top of the file: `using System.IO;`, `using TMPro;`, `using UnityEditor;`, `using UnityEngine.TextCore;`.

```csharp
        /// <summary>Build/overwrite the inline TMP_SpriteAsset at <paramref name="outputPath"/>
        /// from the flagged sets. Returns the asset, or null when there is nothing to bake.</summary>
        public static TMP_SpriteAsset Generate(
            IReadOnlyList<PromptUGUI.Application.SpriteSet> flaggedSets, string outputPath)
        {
            var candidates = CollectCandidates(flaggedSets);
            var glyphs = BuildInlineGlyphTable(candidates, out var collisions);
            if (collisions.Count > 0) return null;   // already logged; do not half-write
            if (glyphs.Count == 0) return null;

            // 1) Pack the source sprites into one point-filtered RGBA32 sheet. Read via a
            //    RenderTexture blit so non-readable source textures still work.
            var copies = new Texture2D[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++) copies[i] = ReadableCopy(glyphs[i].sprite);
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var uv = sheet.PackTextures(copies, 2, 4096, false);
            sheet.Apply(false, false);
            sheet.name = Path.GetFileNameWithoutExtension(outputPath) + " Atlas";
            foreach (var c in copies) Object.DestroyImmediate(c);

            var texW = sheet.width;
            var texH = sheet.height;

            // 2) Build glyph + character tables (GlyphRect origin is bottom-left, matching UV).
            var glyphTable = new List<TMP_SpriteGlyph>(glyphs.Count);
            var charTable = new List<TMP_SpriteCharacter>(glyphs.Count);
            for (var i = 0; i < glyphs.Count; i++)
            {
                var r = uv[i];
                int x = Mathf.RoundToInt(r.x * texW), y = Mathf.RoundToInt(r.y * texH);
                int w = Mathf.RoundToInt(r.width * texW), h = Mathf.RoundToInt(r.height * texH);
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(w, h, 0f, h, w),     // bearingX 0, bearingY h (baseline-sit), advance w
                    new GlyphRect(x, y, w, h),
                    1.0f, 0)
                { sprite = glyphs[i].sprite };
                glyphTable.Add(glyph);

                charTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph)
                {
                    name = glyphs[i].name,
                    glyphIndex = (uint)i,
                });
            }

            // 3) Assemble the asset (+ material). Overwrite in place: delete the old file so
            //    stale sub-assets don't accumulate, then re-point TMP default (handles GUID).
            if (AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(outputPath) != null)
                AssetDatabase.DeleteAsset(outputPath);
            EnsureFolder(Path.GetDirectoryName(outputPath).Replace('\\', '/'));

            var spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            spriteAsset.name = Path.GetFileNameWithoutExtension(outputPath);
            spriteAsset.version = "1.1.0";
            spriteAsset.spriteSheet = sheet;
            spriteAsset.spriteGlyphTable = glyphTable;
            spriteAsset.spriteCharacterTable = charTable;

            var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { name = spriteAsset.name + " Material" };
            mat.SetTexture(ShaderUtilities.ID_MainTex, sheet);
            spriteAsset.material = mat;

            AssetDatabase.CreateAsset(spriteAsset, outputPath);
            AssetDatabase.AddObjectToAsset(sheet, spriteAsset);
            AssetDatabase.AddObjectToAsset(mat, spriteAsset);
            spriteAsset.UpdateLookupTables();
            EditorUtility.SetDirty(spriteAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(outputPath);

            // 4) Wire as the global default sprite asset.
            SetDefaultSpriteAsset(spriteAsset);
            return spriteAsset;
        }

        private static Texture2D ReadableCopy(Sprite sprite)
        {
            var tex = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static void SetDefaultSpriteAsset(TMP_SpriteAsset asset)
        {
            var settings = TMP_Settings.instance;
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultSpriteAsset");
            if (prop != null) { prop.objectReferenceValue = asset; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
```

> **MCP verification of TMP API:** `TMP_SpriteGlyph(uint, GlyphMetrics, GlyphRect, float, int)`, `TMP_SpriteCharacter(uint, TMP_SpriteGlyph)` + `.name`/`.glyphIndex`, and the `m_defaultSpriteAsset` serialized name are the Unity 6 / TMP 3.x shapes. After Step 3, `refresh_unity` + `read_console(types=["error"])`. If a signature mismatches the installed TMP version, use `mcp__UnityMCP__unity_reflect` on `TMPro.TMP_SpriteGlyph` / `TMP_SpriteCharacter` to confirm the exact ctor/property and adjust before moving on.

- [ ] **Step 4: Run test to verify it passes**

Run via MCP: `refresh_unity` → `read_console(types=["error"])` → `run_tests(... filter="InlineSpriteAssetBuilder")`
Expected: PASS (all tests). If `Generate_..._sets_tmp_default` fails only on the `TMP_Settings.defaultSpriteAsset` assertion in a headless context, confirm `TMP_Settings.instance` is non-null in the host project (a TMP Settings asset must exist — it does once TMP is imported).

- [ ] **Step 5: Commit**

```bash
git add Editor/InlineSpriteAssetBuilder.cs Tests/EditMode/Editor/InlineSpriteAssetBuilderTests.cs
git commit -m "feat(inline-sprite): pack sheet + build TMP_SpriteAsset + wire TMP default"
```

---

### Task 5: `RegenerateFromProject` + hook into the sync menu/editor

**Files:**
- Modify: `Editor/InlineSpriteAssetBuilder.cs`
- Modify: `Editor/SpriteAtlasMenu.cs`
- Modify: `Editor/SpriteSetEditor.cs`

The merged asset is global, so it must always rebuild from **all** flagged sets in the project (never just the synced subset), at a fixed output path. The menu/editor sync actions call it after the atlas sync.

- [ ] **Step 1: Add `RegenerateFromProject` (no new test — covered by Generate + the menu is a thin caller)**

Add to `InlineSpriteAssetBuilder.cs`:

```csharp
        /// <summary>Fixed output location for the single global inline sprite asset, in the
        /// host project (not the package). Mirrors where generated atlases live — next to
        /// nothing in particular, so a stable dedicated folder is used.</summary>
        public const string OutputPath = "Assets/PromptUGUI.Generated/InlineSprites.asset";

        /// <summary>Rebuild the global inline sprite asset from every flagged SpriteSet in the
        /// project. No flagged sets → no asset created, TMP settings left untouched.</summary>
        public static TMP_SpriteAsset RegenerateFromProject()
        {
            var flagged = new List<PromptUGUI.Application.SpriteSet>();
            foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets())
                if (s != null && s.GenerateTmpSpriteAsset) flagged.Add(s);
            if (flagged.Count == 0) return null;
            return Generate(flagged, OutputPath);
        }
```

- [ ] **Step 2: Hook the menu**

In `Editor/SpriteAtlasMenu.cs`, in **both** `SyncAll()` and `SyncSelected()`, immediately after the existing `SpriteAtlasSyncer.SyncAll(...)` call (and before / alongside `UI.HotReload.NotifySpriteAssetsChanged()`), add:

```csharp
            InlineSpriteAssetBuilder.RegenerateFromProject();
```

For `SyncAll()` it becomes:

```csharp
            SpriteAtlasSyncer.SyncAll(sets);
            InlineSpriteAssetBuilder.RegenerateFromProject();
            UI.HotReload.NotifySpriteAssetsChanged();
            Debug.Log($"[PromptUGUI] Synced {sets.Count} SpriteSet(s)");
```

For `SyncSelected()`:

```csharp
            SpriteAtlasSyncer.SyncAll(picked);
            InlineSpriteAssetBuilder.RegenerateFromProject();
            UI.HotReload.NotifySpriteAssetsChanged();
```

- [ ] **Step 3: Hook the per-set editor button**

In `Editor/SpriteSetEditor.cs`, inside the existing `if (GUILayout.Button("Sync This Set"))` block, after `SpriteAtlasSyncer.SyncAll(new[] { set });` add:

```csharp
                InlineSpriteAssetBuilder.RegenerateFromProject();
```

(The `generateTmpSpriteAsset` checkbox already renders via the existing `DrawDefaultInspector()` call — no extra UI code needed.)

- [ ] **Step 4: Compile + regression-run the EditorOnly suite**

Run via MCP: `refresh_unity` → `read_console(types=["error"])` (expect none) → `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
Expected: full EditorOnly suite PASS (existing `SpriteAtlasSyncerTests` unaffected; new `InlineSpriteAssetBuilderTests` green).

- [ ] **Step 5: Commit**

```bash
git add Editor/InlineSpriteAssetBuilder.cs Editor/SpriteAtlasMenu.cs Editor/SpriteSetEditor.cs
git commit -m "feat(inline-sprite): regenerate global asset from all flagged sets after sync"
```

---

### Task 6: Document the authoring capability (SKILL update)

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/icons.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md` (pointer only)

Per CLAUDE.md, any functional change must be reflected in the relevant skill in the same PR (in English). This adds a new author-facing capability (inline `<sprite>` markup) + Editor workflow (the checkbox).

- [ ] **Step 1: Add an "Inline sprites (图文混排)" section to `reference/icons.md`**

Append a section covering exactly this, in English:

```markdown
## Inline sprites in text (`<sprite name="...">`)

To drop a SpriteSet icon *inside* a text run (e.g. a coin after a button label, or chat
emoji that wrap with the text), tick **Generate Tmp Sprite Asset** on the SpriteSet asset
(Inspector), then run `Tools → PromptUGUI → Sprite → Sync Atlases`. The sync bakes every
flagged set's sprites into one global `TMP_SpriteAsset` and assigns it as the TextMeshPro
default sprite asset.

Author with native TMP rich-text — no new XML attribute:

    <Btn text="Confirm &lt;sprite name=&quot;coin&quot;&gt;"/>
    <Text text="lol &lt;sprite name=&quot;smile&quot;&gt; nice"/>

The glyph name is the icon's bare basename (`coin`, `smile`) — the same bare name `<Icon>`
accepts. Names must be unique across *all* flagged sets; a collision aborts the sync with an
error (rename the offending sprite). Only flagged sets are baked — window borders, button
9-slices, and other non-icon sprites stay out of the TMP sprite asset. The whole flagged set
is baked (not only XML-referenced icons), so runtime-chosen emoji work too.
```

- [ ] **Step 2: Add a pointer in the main XML SKILL.md**

Near the `<Text>` / i18n markup area of `.claude/skills/authoring-promptugui-xml/SKILL.md`, add one line:

```markdown
- Inline icons inside text (`<sprite name="coin">`, 图文混排) → see `reference/icons.md` → "Inline sprites in text".
```

- [ ] **Step 3: Sanity-check the docs render**

No build step — re-read both edited files and confirm the `<sprite ...>` examples are HTML-escaped (`&lt;`/`&quot;`) so they survive as literal markup in the skill.

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-xml/reference/icons.md .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): inline <sprite> 图文混排 authoring + flagged SpriteSet workflow"
```

---

## Final verification (before PR)

- [ ] `refresh_unity` → `read_console(types=["error"])` clean.
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])` — all green (new + existing).
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` — regression green.
- [ ] Lint: `cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`.
- [ ] **Manual visual QA (user):** in the host project, flag a SpriteSet, Sync, then put `<Btn text="OK <sprite name=&quot;coin&quot;>"/>` in a `.ui.xml` and confirm the coin renders inline; type emoji in a multi-line `<Text>` and confirm they wrap with the text and stay crisp (Point filter).
- [ ] Open PR against `main` (do **not** commit to main directly).

---

## Self-Review notes

- **Spec coverage:** flag (Task 1) · merge+collision-error (Task 2) · whole-flagged-set, bare-name source (Task 3) · dedicated point-filtered sheet + TMP_SpriteAsset + TMP default + overwrite-in-place (Task 4) · one-button integration + global rebuild + gating "no flagged → nothing generated" (Task 5) · native-`<sprite>` authoring, no XML attr, docs (Task 6). Out-of-scope items (per-set addressing, emoji i18n) intentionally excluded.
- **Risk concentration:** Task 4 (TMP/TextCore API + packing) is the only one that can surface a version-specific API mismatch — the MCP `unity_reflect` fallback is called out inline.
- **Type consistency:** `BuildInlineGlyphTable`, `CollectCandidates`, `Generate(sets, path)`, `RegenerateFromProject()`, `OutputPath`, `Glyph{name,sprite}` are referenced consistently across Tasks 2–5.
