# SpriteSet 源资产枚举去扩展名化（多格式 + Aseprite）设计

**日期**: 2026-05-24
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. `Editor/SpriteAtlasSyncer` 当前用 `Directory.EnumerateFiles(folder, "*.png", ...)` 枚举源资产，改为 `AssetDatabase.FindAssets("t:Texture2D ∪ t:Sprite", folderAssetPath)` 联合查询（HashSet dedupe by GUID）。换掉 5 处 enumeration——其中 `Reset*` / `Apply*` 因仅作用于 TextureImporter，只查 `t:Texture2D` 即可。
2. `EnsureSpriteImporter` 加 `AsepriteImporter` 分支（仅 `PROMPTUGUI_HAS_ASEPRITE` 下编译），验证"单 sprite"契约；不修改 Aseprite 导入器设置。
3. `Runtime/Application/UI.cs:100-110` 的扩展名 strip 白名单（`.png/.jpg/.jpeg/.tga/.psd`）改为"strip 最后一个 `.` 后到结尾"——Resources 虚拟路径本来就不带扩展名，去掉白名单后任何扩展名都能正常工作。
4. `PromptUGUI.Runtime.asmdef` 和 `PromptUGUI.Editor.asmdef` 加 `com.unity.2d.aseprite ≥ 1.0` → `PROMPTUGUI_HAS_ASEPRITE` 的 `versionDefines`，照搬现有 Addressables 写法。
5. `SpriteAtlasSyncer` 的 `CountPngs / EnumeratePngs / ResetPngImportSettings` 方法 + Inspector 文案的 "PNG" 字面去掉。

**依赖**: [`2026-05-15-spriteset-rename-design.md`](2026-05-15-spriteset-rename-design.md)（`SpriteAtlasSyncer` 的当前命名）。

---

## 1. 背景

当前 SpriteSet 源资产枚举走 `Directory.EnumerateFiles(fullFolder, "*.png", SearchOption.AllDirectories)`，硬编码 PNG。导致：

- 作者用 JPG/TGA/PSD 等 Unity 原生支持的纹理格式时，源资产被 Syncer 静默忽略，最终 SpriteSet entries 缺项；XML 端 `<Image sprite="ui:foo">` 报 "resolver returned null"，但没有线索说"你的 foo.jpg 没被 Syncer 收"。
- Aseprite（`.ase` / `.aseprite`，由 `com.unity.2d.aseprite` 包的 `AsepriteImporter` 处理）完全走不通——`AsepriteImporter` 不是 `TextureImporter`，glob 也不匹配。

Runtime 端 `UI.cs:100-110` 也有一段对称的扩展名白名单（PNG/JPG/JPEG/TGA/PSD）用于从 `sprite="ui/foo.png#slice"` 之类的字面里 strip 扩展名再调 `Resources.LoadAll<Sprite>(path)`。这一段是为 `<Icon>` 之外的"裸路径 + sub-asset 引用"形态服务的——白名单不全时同样静默失效。

观察：

1. Unity 的 `AssetDatabase.FindAssets("t:Texture2D", searchFolders)` 是**跨 importer 跨扩展名**返回任何被识别为 Texture2D 主资产的资产 GUID。这覆盖了 PNG/JPG/JPEG/TGA/PSD/TIFF/BMP/EXR/HDR/GIF 以及 Aseprite **SpriteSheet 模式**（主资产 Texture2D + Sprite sub-assets）。但 **Aseprite 的默认 AnimatedSprite 模式主资产是 Sprite 而非 Texture2D**，单查 `t:Texture2D` 会漏掉它。因此实际枚举走 `t:Texture2D ∪ t:Sprite` 联合查询（HashSet by GUID 去重），既覆盖 Aseprite 两种 import mode，又保留 PNG Default 模式被 EnsureSpriteImporter auto-flip 的现有路径（Default 模式的 PNG 是 `t:Texture2D` 但还没有 Sprite sub-asset）。
2. Resources 虚拟路径本来就是去扩展名的（Unity 文档：`Resources.Load("foo/bar")` 不带扩展名），所以 Runtime 端"strip 最后一个 `.` 后到结尾"完全等价于"去掉扩展名"，不需要白名单。
3. Aseprite 本身有 multi-frame 概念；要把它纳入 SpriteSet 必须固化"一个 Aseprite 文件 = 一个 Sprite"的契约——作者负责把 Aseprite 文件导成单 sprite 输出，Syncer 端验证，违反则 LogError + skip 该文件。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| MF-D1 | Editor 枚举 API（sync 路径） | `AssetDatabase.FindAssets("t:Texture2D", ...) ∪ FindAssets("t:Sprite", ...)`，HashSet by GUID 去重 | 跨 importer 跨扩展名；Aseprite AnimatedSprite 模式（默认）的主资产是 `t:Sprite` 而非 `t:Texture2D`，单查 `t:Texture2D` 漏；Aseprite SpriteSheet 模式两个都命中（dedupe 消除）；PNG Default 模式只在 `t:Texture2D` 命中（用于触发 EnsureSpriteImporter auto-flip） |
| MF-D1b | Editor 枚举 API（Reset / Apply 路径） | 仅 `t:Texture2D` | `ResetTextureImportSettings` / `ApplyImportSettingsToFolder` 都是 TextureImporter-only 操作；Aseprite 资产 (`AsepriteImporter`) 在这两个路径里本来就要被 importer-type guard 拒掉，与其多扫一遍 `t:Sprite` 再过滤，不如只查 `t:Texture2D` |
| MF-D2 | 跳过非 TextureImporter / 非 AsepriteImporter 的 Texture2D 资产 | silent skip | SVG（vectorgraphics 包）或其他第三方 importer 当前不打算支持；未来如要支持，新增 importer 分支即可，不影响现有路径 |
| MF-D3 | Aseprite 支持的 gating 方式 | `versionDefines`: `com.unity.2d.aseprite ≥ 1.0` → `PROMPTUGUI_HAS_ASEPRITE` | 照搬 Addressables 的现有写法；未装包时 Aseprite 分支完全不参与编译；装包用户零配置启用 |
| MF-D4 | Aseprite "单 sprite" 契约的强度 | **验证而非强制**：Syncer 检查 `LoadAllAssetsAtPath(path).OfType<Sprite>().Count() == 1`，违反则 `Debug.LogError` + skip 该文件 | Aseprite 的 layer/frame 是作者意图的一部分，自动改 `AsepriteImporter` 设置（像 `EnsureSpriteImporter` 改 `TextureImporter` 那样）侵入性过强；硬契约 + loud failure 是合理的中间路线 |
| MF-D5 | EnsureSpriteImporter 对非 TextureImporter 资产的处理 | TextureImporter 走现有 flip-to-Sprite 路径；AsepriteImporter 不改设置（仅在 enumerate 时验证）；其他 importer 不动 | TextureImporter 的 default → Sprite 翻转保持现有"扔个 PNG 就能用"的便利性；Aseprite 不动，避免吞掉作者的 frame/layer 配置 |
| MF-D6 | Runtime `UI.cs:100-110` 扩展名 strip | "strip 最后一个 `/` 之后的 `.` 到结尾"，去掉 PNG/JPG/JPEG/TGA/PSD 白名单 | Resources 虚拟路径本来去扩展名；保护 `foo.v2/bar` 这种"点在目录名里"的情况靠 `dotIdx > slashIdx` 判断 |
| MF-D7 | `CountPngs` / `EnumeratePngs` / `ResetPngImportSettings` 改名 | `CountSpriteSources` / `EnumerateSpriteSources` / `ResetTextureImportSettings`；`ApplyImportSettingsToFolder` 不改名 | 不再 PNG-specific；`Reset*` 仍只对 TextureImporter 生效（不动 Aseprite），名字里留 "Texture" 是诚实的；`Apply*` 同样 TextureImporter-only，但名字已经不含 "Png"，doc-note 即可 |
| MF-D8 | Inspector 文案 | "Source PNGs" → "Source sprites"；"Apply Settings to All N PNGs in Folder" → "Apply Settings to All N TextureImporters in Folder"；"Reset All PNGs Format" → "Reset All TextureImporters Format" | 与方法名对齐，避免误以为"这个按钮也会改 Aseprite 设置" |
| MF-D9 | `FindAssets` 返回顺序的稳定性 | 调用方对 `assetPath` 字符串排序后再处理 | `FindAssets` 的返回顺序是实现细节，sync 输出（SpriteSet entries 顺序、atlas pack）应当对源资产路径稳定 |
| MF-D10 | 多 Sprite PNG（TextureImporter Multi 模式）的处理 | 维持现有行为：`LoadAssetAtPath<Sprite>` 拿第一个 sprite，silent | 现有行为，本期不改；如果作者需要 multi-sprite PNG 进 SpriteSet 那是另一个 milestone |
| MF-D11 | XML scan（`ScanXmlReferences`）的改动 | **零改动** | XML 端引用形态是 `ns:name`，与源资产扩展名无关；scan 完全不受影响 |
| MF-D12 | SKILL.md 更新 | csharp + addressables 两份；xml SKILL 不动 | csharp SKILL 在"自定义控件读取 sprite"段提一句 "SpriteSet 接受任何 Unity 原生纹理格式 + Aseprite（约定单 sprite）"；addressables SKILL 顺带提 Aseprite 走 Addressables 没问题（无新 API，纯说明）；xml SKILL 与扩展名无关，不动 |

---

## 3. 完整使用示例

作者新增一个 Aseprite icon：

```
Assets/UI/SpriteSources/ui/
├── dialog-frame.png          # 既有
├── button.jpg                # 新格式：直接放进去，Syncer 自动识别
└── bell.aseprite             # 新格式：需要装 com.unity.2d.aseprite 包
```

1. 装 `com.unity.2d.aseprite` 包，确认 `bell.aseprite` 在 Project 窗口里展开后只有 1 个 Sprite sub-asset（在 Aseprite Importer Inspector 把 Import Mode 设为 "SpriteSheet" 且只有 1 帧；或 "AnimatedSprite" 但只有 1 帧）。
2. 跑 `Tools → PromptUGUI → Sprite → Sync Atlases (All Sets)`。
3. SpriteSet entries：
   - `ui/dialog-frame` + `dialog-frame`
   - `ui/button` + `button`
   - `ui/bell` + `bell`
4. XML 端：

```xml
<Image  sprite="ui:dialog-frame" type="sliced" anchor="stretch"/>
<Btn    sprite="ui:button"       anchor="bottom-center"/>
<Icon   name="ui:bell"           size="48x48"/>
```

若 `bell.aseprite` 产出 >1 sprite，Syncer console:

```
[SpriteSync] Aseprite 'Assets/UI/SpriteSources/ui/bell.aseprite' produces 3 sprites; SpriteSet requires exactly 1 sprite per file. Skipping. Set the AsepriteImporter Import Mode to single-frame output, or use a different file per icon.
```

`ui/bell` ref 在 SpriteSet 中缺失，后续 `<Icon name="ui:bell">` 在 Runtime 显示空白 + 既有 `UI.ResolveSprite` "resolver returned null" LogError 提示去 sync。

---

## 4. 公开 API 表

| 状态 | 签名 | 说明 |
|---|---|---|
| 改名 | `public static int SpriteAtlasSyncer.CountSpriteSources(string folderAssetPath)` | 原 `CountPngs`；统计可识别的源 sprite 文件数（TextureImporter 或 AsepriteImporter）。Inspector 用 |
| 改名 | `public static List<(string pathKey, Sprite sprite)> SpriteAtlasSyncer.EnumerateSpriteSources(string folderAssetPath, string progressLabel = null)` | 原 `EnumeratePngs`；返回 `(pathKey, Sprite)` 列表，pathKey 是去扩展名的相对路径 |
| 改名 | `public static int SpriteAtlasSyncer.ResetTextureImportSettings(string folderAssetPath, bool showProgress = false)` | 原 `ResetPngImportSettings`；仅对 TextureImporter 生效（Sprite/Single/Uncompressed）；AsepriteImporter / 其他 importer 跳过 |
| 不变 | `public static int SpriteAtlasSyncer.ApplyImportSettingsToFolder(string templateAssetPath, string folderAssetPath, bool showProgress = false)` | 仅对 TextureImporter 生效；template 也必须是 TextureImporter；doc 加一行说明 |
| 新增 | `PROMPTUGUI_HAS_ASEPRITE` compile define | `Runtime/PromptUGUI.Runtime.asmdef` + `Editor/PromptUGUI.Editor.asmdef` 同时声明；条件触发：项目装了 `com.unity.2d.aseprite ≥ 1.0` |
| 不变 | `UI.SpriteResolver`、`UI.ResolveSprite`、所有 7 个内置控件的 `Sprite` setter | 行为不变；只是 strip 扩展名那段算法换了 |

---

## 5. 落地细节

### 5.1 `SpriteAtlasSyncer.EnumerateSpriteSources` 重写

`Editor/SpriteAtlasSyncer.cs`:

```csharp
public static List<(string pathKey, Sprite sprite)> EnumerateSpriteSources(
    string folderAssetPath, string progressLabel = null)
{
    var result = new List<(string, Sprite)>();
    if (string.IsNullOrEmpty(folderAssetPath)) return result;
    if (!AssetDatabase.IsValidFolder(folderAssetPath))
    {
        Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
        return result;
    }

    // MF-D1: union t:Texture2D (covers PNG/JPG/.../Aseprite-SpriteSheet) with t:Sprite
    // (covers Aseprite-AnimatedSprite where main asset is Sprite, not Texture2D).
    var guidSet = new HashSet<string>(StringComparer.Ordinal);
    foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath })) guidSet.Add(g);
    foreach (var g in AssetDatabase.FindAssets("t:Sprite",    new[] { folderAssetPath })) guidSet.Add(g);
    var paths = new string[guidSet.Count];
    var idx = 0;
    foreach (var g in guidSet) paths[idx++] = AssetDatabase.GUIDToAssetPath(g);
    Array.Sort(paths, StringComparer.Ordinal); // MF-D9: stable output

    var folderPrefix = folderAssetPath.EndsWith("/") ? folderAssetPath : folderAssetPath + "/";
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
        if (!EnsureSpriteImporter(assetPath, out var importerOk)) continue;
        if (!importerOk) continue; // MF-D5: unsupported importer type

        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sp == null) continue;

        // assetPath always starts with folderPrefix (FindAssets searchFolder contract)
        var rel = assetPath.Substring(folderPrefix.Length);
        var ext = Path.GetExtension(rel);
        var pathKey = rel.Substring(0, rel.Length - ext.Length);
        result.Add((pathKey, sp));
    }
    return result;
}
```

`EnsureSpriteImporter` 改成返回 bool 让调用方区分 "importer 识别且 OK" / "importer 不识别" / "Aseprite 但多 sprite"：

```csharp
// out importerOk: true = 该资产可作为 sprite 源；false = 跳过（既包括"识别但不合规"如 Aseprite 多 sprite，也包括"importer 不在白名单"）
// 返回值: true = 继续处理；false = 调用方应 continue（与 importerOk 等价，保留两参方便未来扩展不同跳过原因）
private static bool EnsureSpriteImporter(string assetPath, out bool importerOk)
{
    importerOk = false;
    var importer = AssetImporter.GetAtPath(assetPath);
    if (importer is TextureImporter ti)
    {
        if (ti.textureType != TextureImporterType.Sprite)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        importerOk = true;
        return true;
    }
#if PROMPTUGUI_HAS_ASEPRITE
    if (importer is UnityEditor.U2D.Aseprite.AsepriteImporter)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count();
        if (sprites != 1)
        {
            Debug.LogError(
                $"[SpriteSync] Aseprite '{assetPath}' produces {sprites} sprites; " +
                $"SpriteSet requires exactly 1 sprite per file. Skipping. " +
                $"Set the AsepriteImporter Import Mode to single-frame output, " +
                $"or use a different file per icon.");
            return true; // 别 abort 整个 enumerate；importerOk = false 让调用方 skip 这一个
        }
        importerOk = true;
        return true;
    }
#endif
    // Other importers (eg. SVG via com.unity.vectorgraphics) - silent skip
    return true;
}
```

设计权衡：

- `EnsureSpriteImporter` 返回 `(bool, out bool)` 看着冗余——其实第一个返回值现在永远是 true。保留它是为了未来"枚举级 abort"（比如 user cancel）的扩展位；如果不要这个口子，简化为 `static bool EnsureSpriteImporter(string path)` 直接返回 importerOk 也可以。`writing-plans` 阶段再定。

### 5.2 其他 4 处 enumeration 的统一改造

`ResetTextureImportSettings`（原 `ResetPngImportSettings`）、`ApplyImportSettingsToFolder`、`CountSpriteSources`（原 `CountPngs`）、`SpriteAtlasSyncer` 内部 `743:` 行附近"读首个 PNG 的 FilterMode 作 atlas 默认"逻辑——按 MF-D1 / MF-D1b 分两条 enumeration 路径：

- **`CountSpriteSources`**：与 `EnumerateSpriteSources` 同形，走 union（统计与枚举对齐，避免 Inspector 显示数字与 sync 后 entries 数对不上）。实现上抽出私有 `EnumerateSpriteSourceGuids(folder)` 助手，两个 public 方法共享。
- **`ResetTextureImportSettings` / `ApplyImportSettingsToFolder`**：只查 `t:Texture2D`（MF-D1b）。内部仍保持 `if (AssetImporter.GetAtPath(...) is not TextureImporter) continue;` —— 多余的 AsepriteImporter / 其他 importer 在此被 guard 拒掉。`Apply*` 额外要求 template 是 TextureImporter（已有逻辑保持）。
- **"首个资产决定 atlas FilterMode"**：改成"枚举结果（union）排序后取第一个 TextureImporter 资产的 `filterMode`"，AsepriteImporter 跳过（没有跨 importer 等价的 `filterMode` 字段；若 union 结果里只有 Aseprite 资产，FilterMode 用 Unity SpriteAtlas 默认值即可）。

### 5.3 Runtime `UI.cs` 扩展名 strip 简化

`Runtime/Application/UI.cs:100-110`，替换为：

```csharp
// Strip the trailing extension if any. Resources virtual paths don't carry
// extensions; this lets sprite="ui/dialog.png#slice" resolve like "ui/dialog#slice".
// dotIdx > slashIdx guards against "foo.v2/bar" where the dot is in a folder name.
var slashIdx = path.LastIndexOf('/');
var dotIdx = path.LastIndexOf('.');
if (dotIdx > slashIdx && dotIdx > 0) path = path.Substring(0, dotIdx);
```

无白名单 → 任何扩展名都被 strip → `Resources.LoadAll<Sprite>(path)` 拿到的依旧是同一个 Texture2D 资产。

### 5.4 asmdef 改动

`Runtime/PromptUGUI.Runtime.asmdef`:

```diff
   "versionDefines": [
     {
       "name": "com.unity.addressables",
       "expression": "1.0.0",
       "define": "PROMPTUGUI_HAS_ADDRESSABLES"
+    },
+    {
+      "name": "com.unity.2d.aseprite",
+      "expression": "1.0.0",
+      "define": "PROMPTUGUI_HAS_ASEPRITE"
     }
   ]
```

`Editor/PromptUGUI.Editor.asmdef` 同样追加。`AsepriteImporter` 是 Editor-only 类（`UnityEditor.U2D.Aseprite` 命名空间），因此只有 Editor asmdef 真正需要这个 define，但 Runtime asmdef 也声明它是为了未来 Runtime 路径（例如 Sample 里的 demo 控制器）能 `#if PROMPTUGUI_HAS_ASEPRITE` 走条件分支。

asmdef `references` **不**添加 `Unity.2D.Aseprite.Editor`——所有 `AsepriteImporter` 引用都包在 `#if PROMPTUGUI_HAS_ASEPRITE` 内，define 未设时类型引用根本不会被编译器看到，不需要 reference 解析。

### 5.5 命名同步

| 文件 | 改动 |
|---|---|
| `Editor/SpriteAtlasSyncer.cs` | `CountPngs` → `CountSpriteSources`；`EnumeratePngs` → `EnumerateSpriteSources`；`ResetPngImportSettings` → `ResetTextureImportSettings`；内部 XML doc / 注释里 "PNG" 字面普查替换 |
| `Editor/SpriteSetEditor.cs` | "Source PNGs" → "Source sprites"；"Apply Settings to All N PNGs in Folder" → "Apply Settings to All N TextureImporters in Folder"；"Reset All PNGs Format" → "Reset All TextureImporters Format"；helper 文案里的 "manual TextureImporter tweaks on these PNGs" → "...on these textures" |
| `Editor/SpriteAtlasAutoSync.cs` / `Editor/SpriteAtlasMenu.cs` / `Editor/SpriteAtlasBuildHook.cs` | 跨文件 grep 替换 `CountPngs` / `EnumeratePngs` / `ResetPngImportSettings` 引用 |

`SyncAll` 的"unknown ref" 错误消息提示作者引用了不存在的 sprite——文案不动，已经是"ref name 维度"而非"文件名维度"了。

### 5.6 SKILL.md 更新

| SKILL | 改动 |
|---|---|
| `scripting-promptugui-csharp/SKILL.md` | 在 "SpriteSet" 段补一行: "Source folder accepts any Unity-recognized texture format (PNG, JPG, TGA, PSD, TIFF, BMP, EXR, HDR, GIF) plus Aseprite (`.ase`/`.aseprite`, requires `com.unity.2d.aseprite ≥ 1.0`; one sprite per file)." |
| `using-promptugui-addressables/SKILL.md` | 同一行注释；强调 Addressables 路径对 Aseprite 透明（Addressables 装载的是 `AssetReferenceT<Sprite>`，与底层 importer 无关） |
| `authoring-promptugui-xml/SKILL.md` | **不动**——XML 端 `sprite="ns:name"` 引用形态与底层文件扩展名无关 |

---

## 6. 测试策略

### 6.1 改造 `Tests/EditMode/Editor/SpriteAtlasSyncerTests.cs`

| 测试 | 断言 |
|---|---|
| `EnumerateSpriteSources_picks_up_png` (rename from existing) | 单 PNG 文件夹 → entries 含该 sprite |
| `EnumerateSpriteSources_picks_up_mixed_extensions` (new) | 同一文件夹混 PNG + JPG + TGA 各一个，entries 全部 3 个；pathKey 都去扩展名 |
| `EnumerateSpriteSources_skips_textures_with_non_TextureImporter` (new) | 模拟一个非 TextureImporter / 非 AsepriteImporter 的 Texture2D 资产（实现上：创建一个 Texture2D scriptable asset 或留作 manual TODO 跳过该 case）→ entries 不含它 |
| `EnumerateSpriteSources_stable_order_by_path` (new) | 文件夹下 `b.png` `a.png` `c.png` → entries 顺序 a/b/c（pathKey 字典序） |
| `EnumerateSpriteSources_flips_default_TextureImporter_to_sprite` (new) | 故意把一个 PNG 设为 `textureType=Default`，调用后断言 `TextureImporter.textureType == Sprite` 且 entries 包含它 |
| `ResetTextureImportSettings_only_affects_TextureImporter` (new) | 文件夹混 PNG + 模拟 AsepriteImporter（如有条件）→ reset 后只有 PNG 被 reset，AsepriteImporter 资产的设置不动 |

Aseprite-specific（仅 `PROMPTUGUI_HAS_ASEPRITE` 编译时跑）：

| 测试 | 断言 |
|---|---|
| `EnumerateSpriteSources_picks_up_single_frame_animatedsprite_aseprite` | 1 帧 Aseprite，AnimatedSprite mode（默认，主资产 t:Sprite） → entries 含它，pathKey 去 `.aseprite` 扩展名 |
| `EnumerateSpriteSources_picks_up_single_frame_spritesheet_aseprite` | 1 帧 Aseprite，SpriteSheet mode（主资产 t:Texture2D + 1 Sprite sub-asset） → entries 含它 |
| `EnumerateSpriteSources_skips_multi_sprite_aseprite_with_logError` | >1 帧 Aseprite（无论哪种 import mode） → entries 不含 + `LogAssert.Expect(LogType.Error, ...)` 含 "produces N sprites" |
| `EnumerateSpriteSources_dedupes_aseprite_spritesheet_under_union` | SpriteSheet mode Aseprite 同时命中 `t:Texture2D` 和 `t:Sprite` → entries 仍只有 1 条 |

> **风险**: Aseprite 测试需要 fixture `.aseprite` 文件；本仓库目前没有，Plan 阶段决定是 (a) commit fixture 二进制 (b) 在 `[OneTimeSetUp]` 里程序化生成 Aseprite 文件（如果包暴露这样的 API）。**(a) 是默认**——每个 import mode + frame count combo 一个最小 fixture。

### 6.2 Runtime `UI.cs` 扩展名 strip

新增 `Tests/EditMode/Application/ResolveSpriteTests.cs` (existing) 中的几个 case，或者放到现有 `Tests/EditMode/Application/ResolveSpriteTests.cs`：

| 测试 | 断言 |
|---|---|
| `ResolveSprite_strips_png_extension` (existing-equivalent) | `"foo.png#slice"` → 实际 LoadAll path 是 `"foo"` |
| `ResolveSprite_strips_aseprite_extension` (new) | `"foo.aseprite#slice"` → LoadAll path 是 `"foo"` |
| `ResolveSprite_strips_unknown_extension` (new) | `"foo.svg#slice"` / `"foo.xyz#slice"` → LoadAll path 是 `"foo"`；不再硬编码白名单 |
| `ResolveSprite_does_not_strip_dot_in_folder_name` (new) | `"v2.0/foo#slice"` → LoadAll path 是 `"v2.0/foo"`（dot 在 slash 前，不该 strip） |

### 6.3 不写的测试

- 7 个控件的 `Sprite` setter——已被 `ResolveSpriteTests` 覆盖。
- XML scan——本期不动 `ScanXmlReferences`，无新增 case。
- asmdef `versionDefines`——Unity 自身的契约，不在我们范围。

---

## 7. 迁移与破坏性影响

库未上线（前 spec 既有结论），不做向后兼容：

- `SpriteAtlasSyncer.CountPngs` / `EnumeratePngs` / `ResetPngImportSettings` 直接改名，不留 `[Obsolete]` 别名。三者都是 `public static`，但仅 Editor asmdef 内部 + Inspector 使用，no-op for Player.
- Runtime `UI.cs` 扩展名 strip 行为：白名单（PNG/JPG/JPEG/TGA/PSD）→ "strip 最后一个 `.`"。语义扩展，对原白名单中的扩展名结果完全一致；对原白名单**外**的（如 `.bmp .tiff .exr .gif .aseprite .ase`）从"不 strip → LoadAll 失败"变成"strip → LoadAll 命中"——这是 bugfix 方向。
- Inspector 文案 "PNG" → "image"/"texture"：无 API 影响。
- asmdef 加 `versionDefines`：装包用户得到新 define；未装包用户照旧（define 不设）。

Samples 目前没有 JPG/TGA/Aseprite 引用，无需改。验证一个最小 Sample case：把现有 Samples 里一个 PNG 改个扩展名为 JPG（重新导成 JPG），跑 sync，验证 SpriteSet 内 entry 仍存在、`<Image sprite="ui:foo">` 仍正常。

---

## 8. 非目标 / 推迟

- **Multi-sprite PNG（TextureImporter Multi 模式）进 SpriteSet**：现有"`LoadAssetAtPath<Sprite>` 拿第一个"行为不变；本期不引入 multi-sprite-per-file 支持。
- **SVG / Photoshop layer / 第三方 importer 支持**：MF-D2 已决，silent skip；要支持时新增 importer 分支。
- **强制改写 `AsepriteImporter` 设置以确保单 sprite 输出**：MF-D4 已决，仅验证不改写。
- **Aseprite multi-frame → 动画 Sprite 序列**：非本期范围。`<Icon>` / `<Image>` 都是静态 sprite 控件；想要 Aseprite 驱动的动画走 LitMotion 或 sprite swap，与 SpriteSet 不交互。
- **`UI.cs` 扩展名 strip 的 Span 优化**：current `Substring` 拷贝在 sprite resolve 路径里不是热路径（一次 Open 调几十次），不动。
- **`FindAssets` 用 `t:Sprite` 替代 union**：`t:Sprite` 单查会漏掉 `textureType=Default` 的 PNG（它还没有 Sprite sub-asset），破坏现有"扔一个 PNG 就能用"的便利性；不漏 Aseprite。MF-D1 选 union 是为了同时覆盖"Default 模式 PNG（auto-flip 触发）"和"Aseprite AnimatedSprite 模式（主资产 t:Sprite）"两条路径。

---

## 9. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| ~~Aseprite 主资产在某些 import 模式下不是 Texture2D~~ | ~~漏单~~ | **已确认 + 已解决**：AnimatedSprite mode (默认) 主资产是 `t:Sprite`、SpriteSheet mode 主资产是 `t:Texture2D`；MF-D1 用 union 覆盖两种 |
| `versionDefines` 不带 `references` 时，`UnityEditor.U2D.Aseprite.AsepriteImporter` 类型即使包了 `#if PROMPTUGUI_HAS_ASEPRITE` 仍解析不到 | 编译错误 | Plan 阶段实测；若发现需要 reference，加 `Unity.2D.Aseprite.Editor` 到 references。Unity 对"reference 缺失但 versionDefines 没触发"的处理是 warning 而非 error（Addressables 既有路径已验证），所以预期 OK |
| `FindAssets` 在大型项目里比 `Directory.EnumerateFiles` 慢 | Sync 时间延长 | 实际作者级 SpriteSet 文件夹通常 <500 文件，差距毫秒级；不优化 |
| 作者改了 PNG 扩展名到 JPG/TGA 但忘了重新 sync | SpriteSet entry 仍指向旧 GUID（已被 Unity 跟踪），但 atlas 没 repack | sync 流程已有；与现有 PNG 改名场景一致，不是新风险 |
| 多扩展名混在同一 setName 下产生 pathKey 冲突（`foo.png` 和 `foo.jpg` 都在 `ui/` 下）| 后到的 entry 覆盖前者，silent | 加 `EnumerateSpriteSources` 内的重复 pathKey 检测，遇到 → `Debug.LogError` + 跳过后者；plan 阶段决定具体实现 |
| Inspector 文案改完后 SpriteSet 老用户找不到按钮 | UX 摩擦 | 库未上线，n/a |

---

## 10. 实施粒度（提示）

writing-plans 阶段细化，大致 4 块：

1. **Editor enumeration 切换**
   - `EnumerateSpriteSources` 重写 + `EnsureSpriteImporter` 分支化
   - `CountSpriteSources` / `ResetTextureImportSettings` 切到 `FindAssets`
   - `ApplyImportSettingsToFolder` 切到 `FindAssets`（TextureImporter guard 保持）
   - 测试: `SpriteAtlasSyncerTests` 新 case（不含 Aseprite）
2. **Aseprite 分支 + asmdef versionDefines**
   - 两个 asmdef 加 `PROMPTUGUI_HAS_ASEPRITE`
   - `EnsureSpriteImporter` Aseprite 分支
   - 测试: 装包 + Aseprite fixture（条件编译），覆盖单 sprite / 多 sprite 两 case
3. **Runtime `UI.cs` 扩展名 strip 简化**
   - 替换白名单为"strip 最后一个 `.`"
   - 测试: `ResolveSpriteTests` 新 case（任意扩展名 / dot-in-folder-name）
4. **命名 + 文案 + SKILL.md**
   - `CountPngs` / `EnumeratePngs` / `ResetPngImportSettings` rename + 跨文件引用更新
   - `SpriteSetEditor` Inspector 文案
   - SKILL.md csharp + addressables 两份各加一段
   - dotnet format + Unity MCP run_tests 全跑过

---
