# SpriteSet 源资产枚举去扩展名化 (多格式 + Aseprite) 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 SpriteSet 接受任何 Unity 原生纹理格式 (PNG/JPG/TGA/PSD/...) 以及 Aseprite 文件 (`.ase`/`.aseprite`)，不再硬绑 `*.png` 扩展名。

**Architecture:** Editor `SpriteAtlasSyncer` 从 `Directory.EnumerateFiles(*.png)` 切到 `AssetDatabase.FindAssets("t:Texture2D" ∪ "t:Sprite", ...)` 联合查询（HashSet by GUID 去重，覆盖 Aseprite AnimatedSprite/SpriteSheet 两种 import mode 和 PNG Default-mode 的 auto-flip 路径）。Aseprite 走 `PROMPTUGUI_HAS_ASEPRITE` versionDefines（照搬 Addressables 套路），单 sprite 契约由 Syncer 验证而非强制改写 importer 设置。Runtime `UI.cs` 扩展名 strip 去白名单，改为"strip 最后一个 `.`"。

**Tech Stack:** Unity 6+ `AssetDatabase.FindAssets` / `TextureImporter` / `AsepriteImporter` (`com.unity.2d.aseprite`)；`PromptUGUI.Tests.EditMode` (NUnit) + Unity MCP run_tests。

**Spec reference:** [`docs~/superpowers/specs/2026-05-24-spriteset-multi-format-design.md`](../specs/2026-05-24-spriteset-multi-format-design.md)

---

## File Structure

**Modify:**
- `Runtime/Application/UI.cs:100-110` — strip-extension 白名单 → "strip 最后一个 `.`"
- `Runtime/PromptUGUI.Runtime.asmdef:20-26` — 加 `PROMPTUGUI_HAS_ASEPRITE` versionDefine
- `Editor/PromptUGUI.Editor.asmdef:16-22` — 同上
- `Editor/SpriteAtlasSyncer.cs` — 5 处 `Directory.EnumerateFiles("*.png", ...)` 切到 `FindAssets` union；`EnsureSpriteImporter` 加 Aseprite 分支；方法重命名 (`CountPngs`/`EnumeratePngs`/`ResetPngImportSettings`/`FindFirstPng` → 去 PNG 化)
- `Editor/SpriteSetEditor.cs:30,59,148` — 跟随重命名 + Inspector 文案 "PNG" → "image"/"texture"
- `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` — 加 mixed-extension / Aseprite case；旧 `EnumeratePngs_*` test 名跟随方法重命名
- `Tests/EditMode/Application/ResolveSpriteTests.cs` — 加扩展名 strip case
- `.claude/skills/scripting-promptugui-csharp/SKILL.md` — 加一段 "SpriteSet accepts any Unity-recognized texture format + Aseprite"
- `.claude/skills/using-promptugui-addressables/SKILL.md` — 同上

**No new files.**

---

## Task 1: Runtime `UI.cs` 扩展名 strip 简化

**Files:**
- Modify: `Runtime/Application/UI.cs:100-110`
- Test: `Tests/EditMode/Application/ResolveSpriteTests.cs`

`UI.ResolveSprite` 当前对 `"foo.png#slice"` 形式的 `value` 用一个 PNG/JPG/JPEG/TGA/PSD 白名单去 strip 扩展名后再 `Resources.LoadAll<Sprite>(path)`。改成"任何扩展名都 strip"，对 `.aseprite`/`.bmp`/`.tiff` 等也工作。`dotIdx > slashIdx` 守门防止 `v2.0/foo` 这种"点在目录名"的情况被误 strip。

- [ ] **Step 1.1: 加 failing test (任意扩展名 strip)**

In `Tests/EditMode/Application/ResolveSpriteTests.cs`, append:

```csharp
[Test]
public void ResolveSprite_with_hash_strips_aseprite_extension()
{
    // After whitelist removal, any extension should be stripped before LoadAll.
    var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.aseprite#pugui_caret");

    Assert.IsNotNull(actual);
    Assert.AreEqual("pugui_caret", actual.name);
}

[Test]
public void ResolveSprite_with_hash_strips_unknown_extension()
{
    // Any extension after the last '.' (not in folder name) is dropped.
    var actual = UI.ResolveSprite("PromptUGUI/Defaults/pugui.xyz#pugui_caret");

    Assert.IsNotNull(actual);
    Assert.AreEqual("pugui_caret", actual.name);
}
```

- [ ] **Step 1.2: 跑 test 确认 fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ResolveSpriteTests")
```

Expected: 2 new tests fail with "Expected: not null, but was: null"（因为 LoadAll 收到带扩展名的 path，找不到资源）。

- [ ] **Step 1.3: 实现 — 替换 `UI.cs:100-110` 的白名单**

In `Runtime/Application/UI.cs:100-110`, replace:

```csharp
            var dotIdx = path.LastIndexOf('.');
            if (dotIdx > 0)
            {
                var ext = path.Substring(dotIdx);
                if (ext.Equals(".png", System.StringComparison.OrdinalIgnoreCase)
                 || ext.Equals(".jpg", System.StringComparison.OrdinalIgnoreCase)
                 || ext.Equals(".jpeg", System.StringComparison.OrdinalIgnoreCase)
                 || ext.Equals(".tga", System.StringComparison.OrdinalIgnoreCase)
                 || ext.Equals(".psd", System.StringComparison.OrdinalIgnoreCase))
                    path = path.Substring(0, dotIdx);
            }
```

with:

```csharp
            // Resources virtual paths don't carry extensions; strip any trailing
            // extension on the value side so sprite="ui/dialog.png#slice" and
            // sprite="ui/dialog.aseprite#slice" both resolve via LoadAll("ui/dialog").
            // dotIdx > slashIdx guards "v2.0/dialog" where the dot is in a folder name.
            var slashIdx = path.LastIndexOf('/');
            var dotIdx = path.LastIndexOf('.');
            if (dotIdx > slashIdx && dotIdx > 0)
                path = path.Substring(0, dotIdx);
```

- [ ] **Step 1.4: 跑 test 确认 pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ResolveSpriteTests")
```

Expected: all `ResolveSpriteTests.*` pass (含 2 个新 case + 既有 case 不破)。

- [ ] **Step 1.5: dotnet format 验证**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: 无 diff，无报错。

- [ ] **Step 1.6: Commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Application/ResolveSpriteTests.cs
git commit -m "$(cat <<'EOF'
feat: UI.ResolveSprite — strip any extension (was PNG/JPG/JPEG/TGA/PSD whitelist)

The previous whitelist silently fell through on .aseprite / .bmp / .tiff /
unknown extensions, leaving the dot in the path passed to Resources.LoadAll
and returning null. Strip the trailing extension unconditionally, guarded
by dotIdx > slashIdx so "v2.0/foo" isn't misread.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Editor enumeration 切到 `AssetDatabase.FindAssets` union

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs` (5 处 enumeration + 私有 helper)
- Test: `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`

把 `Directory.EnumerateFiles(fullFolder, "*.png", SearchOption.AllDirectories)` 全部换成 `AssetDatabase.FindAssets`。两条 enumeration 路径（MF-D1/D1b）：

- **Sync 路径** (`EnumeratePngs` / `CountPngs`): union `t:Texture2D ∪ t:Sprite`，覆盖 Aseprite AnimatedSprite (主资产 = Sprite) + Aseprite SpriteSheet (主资产 = Texture2D) + PNG-as-Sprite + PNG-Default-mode (待 auto-flip)。
- **TextureImporter-only 路径** (`ResetPngImportSettings` / `ApplyImportSettingsToFolder` / `FindFirstPng`): 单查 `t:Texture2D` —— AsepriteImporter 在这些路径里本来就被 importer-type guard 拒掉，没必要 union。

本任务**只动 enumeration**，**不改方法名**（rename 在 Task 3）。

- [ ] **Step 2.1: 加 failing test — JPG 在同文件夹应被枚举**

In `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`, append (after the existing `EnumeratePngs_*` block):

```csharp
[Test]
public void EnumeratePngs_picks_up_jpg_alongside_png()
{
    // Multi-format coverage: glob-based *.png enumeration would miss JPG.
    // After switching to AssetDatabase.FindAssets("t:Texture2D" ∪ "t:Sprite"),
    // any importer-recognized image file in the folder is included.
    var folder = $"{TestRoot}/icons_mixed";
    AssetDatabase.CreateFolder(TestRoot, "icons_mixed");
    var pngPath = $"{folder}/p.png";
    var jpgPath = $"{folder}/j.jpg";
    File.WriteAllBytes(pngPath, MakeBlankPng());
    File.WriteAllBytes(jpgPath, MakeBlankJpg());
    ImportAsSprite(pngPath);
    ImportAsSprite(jpgPath);

    var entries = SpriteAtlasSyncer.EnumeratePngs(folder);
    var keys = new HashSet<string>();
    foreach (var (k, _) in entries) keys.Add(k);
    Assert.That(keys, Does.Contain("p"));
    Assert.That(keys, Does.Contain("j"));
}
```

Then add a `MakeBlankJpg` helper next to `MakeBlankPng` (around line 910):

```csharp
private byte[] MakeBlankJpg()
{
    var t = new Texture2D(1, 1);
    t.SetPixel(0, 0, Color.white);
    t.Apply();
    var bytes = t.EncodeToJPG();
    UnityEngine.Object.DestroyImmediate(t);
    return bytes;
}
```

- [ ] **Step 2.2: 加 failing test — stable order**

Append:

```csharp
[Test]
public void EnumeratePngs_returns_entries_in_stable_path_order()
{
    // FindAssets returns an implementation-defined order; the syncer must
    // sort by asset path so atlas packing and SpriteSet entries are stable
    // across runs.
    var folder = $"{TestRoot}/icons_order";
    AssetDatabase.CreateFolder(TestRoot, "icons_order");
    var c = $"{folder}/c.png";
    var a = $"{folder}/a.png";
    var b = $"{folder}/b.png";
    File.WriteAllBytes(c, MakeBlankPng()); ImportAsSprite(c);
    File.WriteAllBytes(a, MakeBlankPng()); ImportAsSprite(a);
    File.WriteAllBytes(b, MakeBlankPng()); ImportAsSprite(b);

    var entries = SpriteAtlasSyncer.EnumeratePngs(folder);
    var keys = new List<string>();
    foreach (var (k, _) in entries) keys.Add(k);
    Assert.AreEqual(new[] { "a", "b", "c" }, keys.ToArray());
}
```

- [ ] **Step 2.3: 跑 test 确认 fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SpriteAtlasSyncerTests")
```

Expected: `EnumeratePngs_picks_up_jpg_alongside_png` 缺 "j"（glob 漏 JPG）；`EnumeratePngs_returns_entries_in_stable_path_order` 顺序可能不稳定（依赖 FS）。两个 fail。

- [ ] **Step 2.4: 实现 — `EnumeratePngs` / `CountPngs` 切到 union**

In `Editor/SpriteAtlasSyncer.cs`, replace `CountPngs` (around line 282):

```csharp
public static int CountPngs(string folderAssetPath)
{
    if (string.IsNullOrEmpty(folderAssetPath)) return 0;
    if (!AssetDatabase.IsValidFolder(folderAssetPath)) return 0;
    return EnumerateSpriteSourceGuids(folderAssetPath).Length;
}
```

Replace `EnumeratePngs` body (around line 302–340):

```csharp
public static List<(string pathKey, Sprite sprite)> EnumeratePngs(
    string folderAssetPath, string progressLabel = null)
{
    var result = new List<(string, Sprite)>();
    if (string.IsNullOrEmpty(folderAssetPath)) return result;
    if (!AssetDatabase.IsValidFolder(folderAssetPath))
    {
        Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
        return result;
    }

    var paths = EnumerateSpriteSourceGuids(folderAssetPath);
    var folderPrefix = folderAssetPath.EndsWith("/")
        ? folderAssetPath
        : folderAssetPath + "/";
    for (var i = 0; i < paths.Length; i++)
    {
        var assetPath = paths[i];
        if (progressLabel != null &&
            EditorUtility.DisplayCancelableProgressBar(
                ProgressTitle,
                $"{progressLabel}: {Path.GetFileName(assetPath)} ({i + 1}/{paths.Length})",
                (float)i / Mathf.Max(1, paths.Length)))
        {
            throw new OperationCanceledException();
        }
        EnsureSpriteImporter(assetPath);
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sp == null) continue;
        var rel = assetPath.Substring(folderPrefix.Length);
        var ext = Path.GetExtension(rel);
        var pathKey = rel.Substring(0, rel.Length - ext.Length);
        result.Add((pathKey, sp));
    }
    return result;
}

// Returns asset paths sorted ordinally for stable downstream output.
// MF-D1: union t:Texture2D (covers PNG/JPG/.../Aseprite-SpriteSheet + PNG-Default-mode
// for EnsureSpriteImporter auto-flip) with t:Sprite (covers Aseprite-AnimatedSprite
// where the main asset is Sprite rather than Texture2D). HashSet by GUID dedupes
// the overlap (eg. PNG-as-Sprite hits both filters).
private static string[] EnumerateSpriteSourceGuids(string folderAssetPath)
{
    var folders = new[] { folderAssetPath };
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var g in AssetDatabase.FindAssets("t:Texture2D", folders)) seen.Add(g);
    foreach (var g in AssetDatabase.FindAssets("t:Sprite",    folders)) seen.Add(g);
    var paths = new string[seen.Count];
    var idx = 0;
    foreach (var g in seen) paths[idx++] = AssetDatabase.GUIDToAssetPath(g);
    Array.Sort(paths, StringComparer.Ordinal);
    return paths;
}
```

- [ ] **Step 2.5: 实现 — `ResetPngImportSettings` 切到 `t:Texture2D` 单查 (MF-D1b)**

In `Editor/SpriteAtlasSyncer.cs:397-443`, replace the `Directory.EnumerateFiles` block with `FindAssets`:

```csharp
public static int ResetPngImportSettings(string folderAssetPath,
                                         bool showProgress = false)
{
    if (string.IsNullOrEmpty(folderAssetPath)) return 0;
    if (!AssetDatabase.IsValidFolder(folderAssetPath))
    {
        Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
        return 0;
    }
    // MF-D1b: TextureImporter-only operation; AsepriteImporter resources would be
    // rejected by the importer-type guard anyway, so single t:Texture2D filter
    // is sufficient (and avoids enumerating Aseprite AnimatedSprite which is t:Sprite).
    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
    var paths = new string[guids.Length];
    for (var i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
    Array.Sort(paths, StringComparer.Ordinal);

    var count = 0;
    try
    {
        AssetDatabase.StartAssetEditing();
        for (var i = 0; i < paths.Length; i++)
        {
            var assetPath = paths[i];
            if (showProgress &&
                EditorUtility.DisplayCancelableProgressBar(
                    ProgressTitle,
                    $"Resetting import format: {Path.GetFileName(assetPath)} " +
                    $"({i + 1}/{paths.Length})",
                    (float)i / Mathf.Max(1, paths.Length)))
            {
                throw new OperationCanceledException();
            }
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp)
                continue;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            count++;
        }
    }
    finally
    {
        AssetDatabase.StopAssetEditing();
        if (showProgress) EditorUtility.ClearProgressBar();
    }
    return count;
}
```

- [ ] **Step 2.6: 实现 — `ApplyImportSettingsToFolder` 切到 `t:Texture2D` 单查**

In `Editor/SpriteAtlasSyncer.cs:454-510`, replace the `Directory.EnumerateFiles` enumeration. The body shape is identical to Step 2.5; replace:

```csharp
            var fullFolder = Path.GetFullPath(folderAssetPath);
            var templateFullPath = Path.GetFullPath(templatePngAssetPath);
            var files = new List<string>(Directory.EnumerateFiles(
                fullFolder, "*.png", SearchOption.AllDirectories));
```

with:

```csharp
            // MF-D1b: TextureImporter-only, same reasoning as ResetPngImportSettings.
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
            var paths = new string[guids.Length];
            for (var i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.Ordinal);
```

Then in the loop, replace `var fullPath = files[i];` + the `if (string.Equals(fullPath, templateFullPath, ...))` skip + `var assetPath = "Assets" + fullPath.Substring(...)` reconstruction with:

```csharp
                    var assetPath = paths[i];
                    if (string.Equals(assetPath, templatePngAssetPath, StringComparison.OrdinalIgnoreCase))
                        continue;
```

And update `files.Count` → `paths.Length` throughout.

- [ ] **Step 2.7: 实现 — `FindFirstPng` 切到 `t:Texture2D` 单查**

In `Editor/SpriteAtlasSyncer.cs:517-528`, replace the body:

```csharp
public static string FindFirstPng(string folderAssetPath)
{
    if (string.IsNullOrEmpty(folderAssetPath)) return null;
    if (!AssetDatabase.IsValidFolder(folderAssetPath)) return null;
    // MF-D1b: used for "first TextureImporter's filterMode" template; AsepriteImporter
    // has no equivalent filterMode field, so t:Texture2D-only is correct.
    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
    if (guids.Length == 0) return null;
    var paths = new string[guids.Length];
    for (var i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
    Array.Sort(paths, StringComparer.Ordinal);
    return paths[0];
}
```

- [ ] **Step 2.8: 跑 test 确认 pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SpriteAtlasSyncerTests")
```

Expected: 2 new tests pass + 既有 `EnumeratePngs_*` / `ResetPngImportSettings_*` / `ApplyImportSettingsToFolder_*` 全部不破。

- [ ] **Step 2.9: dotnet format 验证**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 2.10: Commit**

```bash
git add Editor/SpriteAtlasSyncer.cs Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs
git commit -m "$(cat <<'EOF'
feat: SpriteAtlasSyncer — enumerate sources via AssetDatabase.FindAssets

Replace 5 Directory.EnumerateFiles("*.png") sites with FindAssets:
sync paths use t:Texture2D ∪ t:Sprite union (Aseprite AnimatedSprite
mode has Sprite as main asset, not Texture2D); TextureImporter-only
operations (Reset/Apply/FindFirst) use t:Texture2D alone.

Method names still carry "Png" — rename in next commit. Stable
ordinal sort on asset paths so atlas packing is reproducible.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: 方法重命名 + Inspector 文案

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs` (method declarations + 1 internal call site at line 661)
- Modify: `Editor/SpriteSetEditor.cs:30,59,69,85,93-95,102-106,109-122,131-149`
- Modify: `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` (test names + assertions referring to method names)

Rename map:
- `CountPngs` → `CountSpriteSources`
- `EnumeratePngs` → `EnumerateSpriteSources`
- `ResetPngImportSettings` → `ResetTextureImportSettings`
- `FindFirstPng` → `FindFirstTexture`
- `ApplyImportSettingsToFolder` — 不改名（已经不含 "Png"）；只更新 XML doc

Inspector 文案：
- "Source PNGs" → "Source sprites"
- "PNG Import Settings" → "Texture Import Settings"
- "No PNG found under '{folder}'" → "No texture found under '{folder}'"
- "Add a PNG to define import settings." → "Add a texture to define import settings."
- "...applied to every PNG in the folder..." → "...applied to every texture in the folder..."
- "Apply Settings to All N PNGs in Folder" → "Apply Settings to All N Textures in Folder"
- "(template is the only PNG)" → "(template is the only texture)"
- "Apply Import Settings" dialog body "...every PNG under..." → "...every texture under..."
- "This overrides any per-PNG manual TextureImporter tweaks." → "This overrides any per-texture manual TextureImporter tweaks."
- "Reset All PNGs Format" → "Reset All Textures Format"
- "Reset PNG Import Format" dialog title → "Reset Texture Import Format"
- "Force re-import every PNG under..." → "Force re-import every texture under..."
- "This overrides any manual TextureImporter tweaks on these PNGs." → "...on these textures."
- log strings `[SpriteSync] copied import settings to N PNG(s)` / `[SpriteSync] reset N PNG(s)` → `texture(s)`

- [ ] **Step 3.1: 重命名方法 (decl + 1 内部 callsite)**

In `Editor/SpriteAtlasSyncer.cs`:
- Line ~284 `public static int CountPngs` → `CountSpriteSources`
- Line ~302 `public static List<...> EnumeratePngs` → `EnumerateSpriteSources`
- Line ~397 `public static int ResetPngImportSettings` → `ResetTextureImportSettings`
- Line ~517 `public static string FindFirstPng` → `FindFirstTexture`
- Line ~661 internal call `EnumeratePngs(folder, label)` → `EnumerateSpriteSources(folder, label)`
- Line ~766 internal call `FindFirstPng(folderAssetPath)` → `FindFirstTexture(folderAssetPath)`
- 同名 XML doc 段里的 "PNG" 字面统一改 "image"/"texture" (e.g. line 295 注释 `// 每个 PNG 一个 entry` → `// 每个 sprite 源资产一个 entry`；line 297 注释 `// 不再 first-wins —— 同名 PNG ...` → `// 不再 first-wins —— 同名 sprite 源资产 ...`)

- [ ] **Step 3.2: 重命名 Inspector callsites + 文案**

In `Editor/SpriteSetEditor.cs`:
- Line 11 `_templatePngPath` → `_templateTexturePath`
- Line 30 `FindFirstPng` → `FindFirstTexture`
- Line 59 `CountPngs` → `CountSpriteSources`; `pngCount` 局部变量 → `sourceCount`
- Line 60 `"Source PNGs"` → `"Source sprites"`
- Line 69 `"PNG Import Settings"` → `"Texture Import Settings"`
- Line 73 形参 `pngCount` → `sourceCount`
- Line 85-86 dialog message PNG → texture
- Line 93-95 helpbox text PNG → texture
- Line 102-106 button label PNG → texture
- Line 109-122 dialog body / log PNG → texture
- Line 131 button label "Reset All PNGs Format" → "Reset All Textures Format"
- Line 134-136 dialog "Reset PNG Import Format" → "Reset Texture Import Format"
- Line 139-145 dialog body PNG → texture
- Line 148 `ResetPngImportSettings` → `ResetTextureImportSettings`
- Line 149 log PNG → texture

- [ ] **Step 3.3: 重命名 test 名 + 内部 call**

In `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`, mass-replace (limit to occurrences inside `[Test]` decorated method names and method-call expressions; **不**改 `MakeBlankPng` helper name 因它仍只生成 PNG):

- 测试方法名 `EnumeratePngs_*` → `EnumerateSpriteSources_*` (8 处)
- 测试方法名 `ResetPngImportSettings_*` → `ResetTextureImportSettings_*` (2 处)
- 调用 `SpriteAtlasSyncer.EnumeratePngs(...)` → `EnumerateSpriteSources(...)` (8 处)
- 调用 `SpriteAtlasSyncer.ResetPngImportSettings(...)` → `ResetTextureImportSettings(...)` (2 处)
- 调用 `SpriteAtlasSyncer.CountPngs` → `CountSpriteSources` (如有)
- 调用 `SpriteAtlasSyncer.FindFirstPng` → `FindFirstTexture` (如有)
- 注释里 "PNG" 留着（fixture 真的就是 PNG 文件，描述准确）

- [ ] **Step 3.4: refresh + 跑 test 确认 pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```

Expected: 0 compile error；既有所有 SpriteAtlasSyncer / ResolveSprite 测试通过；新加的 mixed-extension / stable-order 测试通过。

- [ ] **Step 3.5: dotnet format 验证**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 3.6: Commit**

```bash
git add Editor/SpriteAtlasSyncer.cs Editor/SpriteSetEditor.cs Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs
git commit -m "$(cat <<'EOF'
refactor: SpriteAtlasSyncer — drop Png-specific method names + Inspector text

CountPngs → CountSpriteSources, EnumeratePngs → EnumerateSpriteSources,
ResetPngImportSettings → ResetTextureImportSettings, FindFirstPng →
FindFirstTexture. Inspector labels "PNG" → "image"/"texture". Behavior
unchanged; cosmetic alignment with multi-format enumeration.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `PROMPTUGUI_HAS_ASEPRITE` versionDefines

**Files:**
- Modify: `Runtime/PromptUGUI.Runtime.asmdef:20-26`
- Modify: `Editor/PromptUGUI.Editor.asmdef:16-22`

照搬 Addressables 的 `versionDefines` 写法。**不**加 `references` 条目——`AsepriteImporter` 的所有引用都在 `#if PROMPTUGUI_HAS_ASEPRITE` 内，define 不设时编译器看不到这些类型，不需要 assembly reference。

- [ ] **Step 4.1: 加 versionDefine — Runtime**

In `Runtime/PromptUGUI.Runtime.asmdef`, change `versionDefines`:

```json
  "versionDefines": [
    {
      "name": "com.unity.addressables",
      "expression": "1.0.0",
      "define": "PROMPTUGUI_HAS_ADDRESSABLES"
    },
    {
      "name": "com.unity.2d.aseprite",
      "expression": "1.0.0",
      "define": "PROMPTUGUI_HAS_ASEPRITE"
    }
  ],
```

- [ ] **Step 4.2: 加 versionDefine — Editor**

In `Editor/PromptUGUI.Editor.asmdef`, change `versionDefines`:

```json
    "versionDefines": [
        {
            "name": "com.unity.addressables",
            "expression": "1.0.0",
            "define": "PROMPTUGUI_HAS_ADDRESSABLES"
        },
        {
            "name": "com.unity.2d.aseprite",
            "expression": "1.0.0",
            "define": "PROMPTUGUI_HAS_ASEPRITE"
        }
    ],
```

- [ ] **Step 4.3: 在 host 项目装 `com.unity.2d.aseprite` 包**

```
# Manual step the implementer takes in Unity Package Manager:
# Window → Package Manager → "+ Add package by name" → "com.unity.2d.aseprite"
# Confirm version ≥ 1.0.0
```

Verification:

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error", "warning"])
```

Expected: no compile errors; new define `PROMPTUGUI_HAS_ASEPRITE` is set in both asmdefs (verify by adding a temp `#if PROMPTUGUI_HAS_ASEPRITE` in any .cs file and seeing the code highlighted as active in IDE, then removing).

- [ ] **Step 4.4: Commit**

```bash
git add Runtime/PromptUGUI.Runtime.asmdef Editor/PromptUGUI.Editor.asmdef
git commit -m "$(cat <<'EOF'
chore: asmdef — PROMPTUGUI_HAS_ASEPRITE versionDefine for com.unity.2d.aseprite

Mirrors the PROMPTUGUI_HAS_ADDRESSABLES gating pattern. No references
entry needed — Aseprite-specific code is wrapped in #if blocks so the
type isn't compiled when the package is absent.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Aseprite branch in `EnsureSpriteImporter` (validation, not coercion)

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs:378-386` (existing `EnsureSpriteImporter`)
- Test: `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs` (new Aseprite cases)
- Test fixture: `Tests/EditMode/Editor/Fixtures/aseprite/` (commit small `.aseprite` files)

`EnsureSpriteImporter` 当前只识别 `TextureImporter`。加 `#if PROMPTUGUI_HAS_ASEPRITE` 分支：检测 `AsepriteImporter`，验证 `LoadAllAssetsAtPath(path).OfType<Sprite>().Count() == 1`，违反 → `Debug.LogError` + `EnumerateSpriteSources` 在 LoadAssetAtPath<Sprite> 拿到第一个 sprite 后通过单 sprite 检测器跳过该项。

由于需要 fixture `.aseprite` 文件，本任务包含 fixture 准备步骤。

- [ ] **Step 5.1: 准备 fixture .aseprite 文件**

In the host Unity project (`UnityProjects~/PromptUGUIDev`), create three small Aseprite files via the Aseprite tool (or download minimal sample files from the Aseprite docs):

- 1-frame AnimatedSprite (default mode): saves as `single_animated.aseprite`
- 1-frame SpriteSheet (Import Mode set to SpriteSheet in Inspector): `single_sheet.aseprite`
- 3-frame AnimatedSprite: `multi.aseprite`

Copy them into the **package** `Tests/EditMode/Editor/Fixtures/aseprite/` directory:

```bash
mkdir -p Tests/EditMode/Editor/Fixtures/aseprite
# Then place the three .aseprite files at:
#   Tests/EditMode/Editor/Fixtures/aseprite/single_animated.aseprite
#   Tests/EditMode/Editor/Fixtures/aseprite/single_sheet.aseprite
#   Tests/EditMode/Editor/Fixtures/aseprite/multi.aseprite
```

Verify Unity recognizes them: in Project window, each `.aseprite` shows the expected Sprite sub-asset count (1, 1, 3) under the foldout.

If creating .aseprite from scratch is impractical: use NUnit `Assume.That(File.Exists(...))` to skip tests when fixtures are missing, and document fixture setup in a `Tests/EditMode/Editor/Fixtures/aseprite/README.md` (single sentence: "Drop test .aseprite files here; see plan §5.1"). Decide on a per-tester basis.

- [ ] **Step 5.2: 加 failing test — single-frame AnimatedSprite Aseprite 被收**

In `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`, append at end of class:

```csharp
#if PROMPTUGUI_HAS_ASEPRITE
[Test]
public void EnumerateSpriteSources_picks_up_single_frame_animatedsprite_aseprite()
{
    // AnimatedSprite is Aseprite's default Import Mode. Main asset is t:Sprite,
    // NOT t:Texture2D, so the union filter (MF-D1) is what makes this work.
    var folder = $"{TestRoot}/aseprite_animated";
    AssetDatabase.CreateFolder(TestRoot, "aseprite_animated");
    var srcAseprite = "Packages/com.heerozh.promptugui/Tests/EditMode/Editor/Fixtures/aseprite/single_animated.aseprite";
    Assume.That(File.Exists(srcAseprite), $"Fixture missing: {srcAseprite}");
    var destAseprite = $"{folder}/single.aseprite";
    AssetDatabase.CopyAsset(srcAseprite, destAseprite);

    var entries = SpriteAtlasSyncer.EnumerateSpriteSources(folder);
    var keys = new List<string>();
    foreach (var (k, _) in entries) keys.Add(k);
    Assert.That(keys, Does.Contain("single"));
}

[Test]
public void EnumerateSpriteSources_picks_up_single_frame_spritesheet_aseprite()
{
    var folder = $"{TestRoot}/aseprite_sheet";
    AssetDatabase.CreateFolder(TestRoot, "aseprite_sheet");
    var srcAseprite = "Packages/com.heerozh.promptugui/Tests/EditMode/Editor/Fixtures/aseprite/single_sheet.aseprite";
    Assume.That(File.Exists(srcAseprite), $"Fixture missing: {srcAseprite}");
    var destAseprite = $"{folder}/single.aseprite";
    AssetDatabase.CopyAsset(srcAseprite, destAseprite);

    var entries = SpriteAtlasSyncer.EnumerateSpriteSources(folder);
    var keys = new List<string>();
    foreach (var (k, _) in entries) keys.Add(k);
    Assert.That(keys, Does.Contain("single"),
        "SpriteSheet-mode 1-sprite Aseprite must be enumerated");
    // Dedupe assertion: SpriteSheet mode hits BOTH t:Texture2D and t:Sprite.
    // Union HashSet should fold them into one entry.
    Assert.AreEqual(1, keys.Count, "Expected exactly one entry; got: " + string.Join(",", keys));
}

[Test]
public void EnumerateSpriteSources_skips_multi_sprite_aseprite_with_log_error()
{
    var folder = $"{TestRoot}/aseprite_multi";
    AssetDatabase.CreateFolder(TestRoot, "aseprite_multi");
    var srcAseprite = "Packages/com.heerozh.promptugui/Tests/EditMode/Editor/Fixtures/aseprite/multi.aseprite";
    Assume.That(File.Exists(srcAseprite), $"Fixture missing: {srcAseprite}");
    var destAseprite = $"{folder}/multi.aseprite";
    AssetDatabase.CopyAsset(srcAseprite, destAseprite);

    LogAssert.Expect(LogType.Error,
        new System.Text.RegularExpressions.Regex("produces .* sprites; SpriteSet requires exactly 1"));

    var entries = SpriteAtlasSyncer.EnumerateSpriteSources(folder);
    var keys = new List<string>();
    foreach (var (k, _) in entries) keys.Add(k);
    Assert.That(keys, Does.Not.Contain("multi"));
}
#endif
```

- [ ] **Step 5.3: 跑 test 确认 fail**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="aseprite")
```

Expected (assuming fixtures present): `EnumerateSpriteSources_picks_up_single_frame_animatedsprite_aseprite` 可能已 pass（union 已经覆盖）；`EnumerateSpriteSources_skips_multi_sprite_aseprite_with_log_error` FAIL（当前 multi-frame Aseprite 没被任何代码拒绝，会 silently include 它的第一个 sprite）。

- [ ] **Step 5.4: 实现 — `EnsureSpriteImporter` Aseprite 分支**

In `Editor/SpriteAtlasSyncer.cs:378-386`, replace `EnsureSpriteImporter`:

```csharp
private static void EnsureSpriteImporter(string assetPath)
{
    var importer = AssetImporter.GetAtPath(assetPath);
    if (importer is TextureImporter ti)
    {
        if (ti.textureType == TextureImporterType.Sprite) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return;
    }
#if PROMPTUGUI_HAS_ASEPRITE
    if (importer is UnityEditor.U2D.Aseprite.AsepriteImporter)
    {
        // MF-D4: validate single-sprite contract; do not coerce AsepriteImporter
        // settings — layer/frame configuration is author intent.
        var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count();
        if (sprites != 1)
        {
            Debug.LogError(
                $"[SpriteSync] Aseprite '{assetPath}' produces {sprites} sprites; " +
                $"SpriteSet requires exactly 1 sprite per file. Skipping. " +
                $"Set the AsepriteImporter Import Mode to single-frame output, " +
                $"or use a different file per icon.");
        }
        return;
    }
#endif
    // Other importer types (eg. SVG via com.unity.vectorgraphics) - silent skip.
}
```

Add `using System.Linq;` at the top of `SpriteAtlasSyncer.cs` if not already there (needed for `.OfType<Sprite>()`).

- [ ] **Step 5.5: 实现 — `EnumerateSpriteSources` skip multi-sprite Aseprite**

The current `EnumerateSpriteSources` body calls `EnsureSpriteImporter(assetPath)` then `LoadAssetAtPath<Sprite>(assetPath)`. For multi-sprite Aseprite, `EnsureSpriteImporter` logs an error but doesn't change the asset; `LoadAssetAtPath<Sprite>` still returns a (random) sprite, so the entry leaks.

In `Editor/SpriteAtlasSyncer.cs` `EnumerateSpriteSources` body, replace the `EnsureSpriteImporter(assetPath); var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);` block with:

```csharp
        EnsureSpriteImporter(assetPath);
#if PROMPTUGUI_HAS_ASEPRITE
        // MF-D4: multi-sprite Aseprite is rejected at validation time; skip it
        // here so a stray first-sprite doesn't sneak into the SpriteSet.
        if (AssetImporter.GetAtPath(assetPath) is UnityEditor.U2D.Aseprite.AsepriteImporter)
        {
            if (AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count() != 1)
                continue;
        }
#endif
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sp == null) continue;
```

> **Note**: The Aseprite count check runs twice for multi-sprite files (once in `EnsureSpriteImporter` to log, once here to skip). That's two `LoadAllAssetsAtPath` calls per Aseprite file (the existing test fixtures have at most a handful of files, so the cost is negligible). Refactoring to one call would require either threading the count through a return value or caching by path; the duplication is the simpler choice. If profiling later shows it's hot, fold into a single helper.

- [ ] **Step 5.6: 跑 test 确认 pass**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="aseprite")
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="SpriteAtlasSyncerTests")
```

Expected: all Aseprite tests pass; existing SpriteAtlasSyncerTests not broken.

- [ ] **Step 5.7: dotnet format 验证**

```bash
cd .lint && dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 5.8: Commit**

```bash
git add Editor/SpriteAtlasSyncer.cs Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs Tests/EditMode/Editor/Fixtures/aseprite/
git commit -m "$(cat <<'EOF'
feat: SpriteAtlasSyncer — Aseprite support (PROMPTUGUI_HAS_ASEPRITE)

EnsureSpriteImporter gains an AsepriteImporter branch that validates
the single-sprite contract (LoadAllAssetsAtPath... OfType<Sprite>()
.Count() == 1) without coercing AsepriteImporter settings.
EnumerateSpriteSources skips multi-sprite Aseprite files so they
don't sneak in via LoadAssetAtPath<Sprite>'s arbitrary-first behavior.

Test fixtures in Tests/EditMode/Editor/Fixtures/aseprite/ cover
both AnimatedSprite (main asset = t:Sprite) and SpriteSheet (main
asset = t:Texture2D) modes; gated by Assume.That for repos that
haven't installed the package.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: SKILL.md updates

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `.claude/skills/using-promptugui-addressables/SKILL.md`

XML SKILL **不**动（`sprite="ns:name"` 引用形态与底层文件格式无关）。

- [ ] **Step 6.1: 找到 csharp SKILL 里的 SpriteSet 段**

```bash
grep -n "SpriteSet\|SpriteResolverHelpers\|sourceFolder" .claude/skills/scripting-promptugui-csharp/SKILL.md | head -20
```

- [ ] **Step 6.2: 在 SpriteSet 段尾追加格式说明**

In `.claude/skills/scripting-promptugui-csharp/SKILL.md`, locate the SpriteSet description and append (use the exact insertion point identified in Step 6.1):

```markdown
**Source formats**: SpriteSet's source folder accepts any Unity-recognized texture
format (PNG, JPG, JPEG, TGA, PSD, TIFF, BMP, EXR, HDR, GIF) plus Aseprite
(`.ase` / `.aseprite`, requires `com.unity.2d.aseprite ≥ 1.0`). For Aseprite,
each file must produce exactly **one sprite** — set the AsepriteImporter Import
Mode to single-frame output or use one file per icon. Multi-sprite Aseprite
files are logged as errors and skipped during sync.
```

- [ ] **Step 6.3: 在 addressables SKILL 加同样说明**

```bash
grep -n "SpriteSet\|UseAddressableSpriteSetResolver" .claude/skills/using-promptugui-addressables/SKILL.md | head -10
```

In `.claude/skills/using-promptugui-addressables/SKILL.md`, append next to the SpriteSet Addressable resolver section:

```markdown
**Source formats via Addressables**: Sprite source format is transparent to the
Addressables path — `AssetReferenceT<Sprite>` resolves to a Sprite regardless of
whether the underlying file was PNG / JPG / Aseprite / etc. The same single-sprite
contract for Aseprite (see csharp SKILL) applies.
```

- [ ] **Step 6.4: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md .claude/skills/using-promptugui-addressables/SKILL.md
git commit -m "$(cat <<'EOF'
docs: SKILL.md — SpriteSet multi-format + Aseprite

csharp + addressables SKILLs note that SpriteSet accepts any Unity-recognized
texture format plus Aseprite (single-sprite contract; PROMPTUGUI_HAS_ASEPRITE
gating). XML SKILL unchanged — `sprite="ns:name"` is format-agnostic.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: 全量 verify + Sample smoke check

**Files:** none

最终验收：跨 assembly 跑 EditMode + PlayMode 全套；Sample 里手动改一个 PNG 为 JPG 跑 sync 确认 end-to-end。

- [ ] **Step 7.1: refresh + 全量 EditMode test**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
```

Expected: 0 failures, 0 compile errors. If `PROMPTUGUI_HAS_ADDRESSABLES` enabled in the host:

```
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode.Addressables"])
```

- [ ] **Step 7.2: 全量 PlayMode test**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: 0 failures.

- [ ] **Step 7.3: Sample 端 smoke**

```bash
ls Samples~/MainMenu/
```

In Samples~/MainMenu (or whichever sample has a SpriteSet referenced from `.ui.xml`):
1. Pick an existing PNG referenced by `<Image sprite="ns:name">` in a sample XML.
2. Re-import it as JPG (rename in OS, or duplicate-and-rename then delete the PNG) and adjust the SpriteSet sourceFolder to include it.
3. Run `Tools → PromptUGUI → Sprite → Sync Atlases (All Sets)`.
4. Open the Sample scene, hit Play, confirm the sprite still appears in the UI.

Document the outcome in this task's commit message; no code change expected.

- [ ] **Step 7.4: dotnet format final pass**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style PromptUGUI.Lint.slnx
dotnet format analyzers PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: no diffs introduced; verify step passes.

- [ ] **Step 7.5: UIXmlLint pass**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: 0 errors.

- [ ] **Step 7.6: (No commit unless Step 7.3 surfaces fix-ups)**

If Step 7.3 surfaced fixes (eg. SpriteSet asset references), commit them with:

```bash
git commit -m "$(cat <<'EOF'
chore: Samples — verify multi-format sprite resolves end-to-end

<one-line description of what was changed in the sample>

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

If no fixes needed: skip commit.

---

## Self-Review Checklist (filled at plan-write time)

**Spec coverage:**
- MF-D1 (union enumeration) → Task 2 Step 2.4
- MF-D1b (Reset/Apply use t:Texture2D only) → Task 2 Steps 2.5–2.7
- MF-D2 (silent skip non-TextureImporter/non-AsepriteImporter) → Task 5 Step 5.4 (else-branch implicit return)
- MF-D3 (PROMPTUGUI_HAS_ASEPRITE gating) → Task 4
- MF-D4 (Aseprite validate-not-coerce) → Task 5 Step 5.4
- MF-D5 (TextureImporter still auto-flips, Aseprite doesn't) → Task 5 Step 5.4
- MF-D6 (Runtime UI.cs strip simplification) → Task 1
- MF-D7 (rename methods去 Png) → Task 3
- MF-D8 (Inspector text "PNG" → "image"/"texture") → Task 3 Step 3.2
- MF-D9 (stable sort) → Task 2 Step 2.4 `Array.Sort`
- MF-D10 (multi-sprite PNG behavior unchanged) → no task needed; existing tests cover
- MF-D11 (XML scan unchanged) → no task needed
- MF-D12 (SKILL updates: csharp + addressables) → Task 6

**Placeholder scan:** none found — every code step has a complete code block; every command is exact; every commit message is fully written.

**Type consistency:**
- `EnumerateSpriteSourceGuids` is the only new private helper; called in Task 2 Steps 2.4. Signature `private static string[] EnumerateSpriteSourceGuids(string folderAssetPath)` used consistently.
- `EnumerateSpriteSources` (renamed in Task 3) — all Task 5 references use this name (assertions and method calls in tests).
- `EnsureSpriteImporter` signature unchanged: `private static void EnsureSpriteImporter(string assetPath)`.
- `using System.Linq;` added in Step 5.4 — required for `.OfType<Sprite>()`.
- `multi.aseprite` fixture referenced in Step 5.2 must match the file created in Step 5.1.

**Found and fixed:**
- (none after self-review)
