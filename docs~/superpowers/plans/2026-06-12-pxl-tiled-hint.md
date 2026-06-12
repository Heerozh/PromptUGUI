# pxl-tiled-hint 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `.pxl` section 声明 `tiled: true` 后,该 sprite 在所有解析通道、所有控件上自动以 `Image.Type.Tiled` 渲染(显式 `type=` 仍优先)。

**Architecture:** parser 加指令 → importer 产 `PxlSpriteHints` 子资产(直接 Sprite 引用) → 运行时 `SpriteRenderHints` 登记表(instanceID HashSet,三个填充口:ResolveSprite Resources 分支 / SpriteSet entry / 内置皮肤自举) → 消费端统一 `ProceduralBuilders.DeriveType`。最后回收 farm-pixel-skin 轮的硬编码与 sample 的显式 `type="tiled"`。

**Tech Stack:** Unity 6 uGUI / ScriptedImporter / NUnit(UnityMCP 跑测试)。Spec: `docs~/superpowers/specs/2026-06-12-pxl-tiled-hint-design.md`。

**通用约定(每个 Task 适用):**
- 分支 `feat/pxl-tiled-hint`。提交前 `git branch --show-current` 确认不在 main。
- 测试一律走 UnityMCP:改完源码先 `refresh_unity(compile="request", mode="force", scope="all")`,`read_console(types=["error"])` 确认零编译错,再 `run_tests`(EditMode 用 `assembly_names=["PromptUGUI.Tests.EditMode"]`,EditorOnly 用 `["PromptUGUI.Tests.EditorOnly"]`;按 `group_names=["类名"]` 过滤)。`run_tests` 返回 job_id,用 `get_test_job(job_id, wait_timeout=60, include_failed_tests=true)` 取结果。
- C# 改动跑 `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`。

---

### Task 1: parser — `tiled:` 指令

**Files:**
- Modify: `Editor/Pxl/PxlParser.cs`(`PxlSection` 加字段;`Parse` 加分支,插在 `border:` 分支之后、`grid:` 分支之前)
- Test: `Tests/EditMode/Editor/Pxl/PxlParserTests.cs`

- [ ] **Step 1: 写失败测试**(追加到 `PxlParserTests` 类尾部;该文件已有同风格用例,如 `Parse_border_exceeding_size_throws`)

```csharp
[Test]
public void Parse_tiled_true_sets_section_flag()
{
    var doc = PxlParser.Parse("chars:\n  K: #000000\n\n[a]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n");
    Assert.IsTrue(doc.Sections[0].Tiled);
}

[Test]
public void Parse_tiled_defaults_false()
{
    var doc = PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ngrid:\n  KK\n  KK\n");
    Assert.IsFalse(doc.Sections[0].Tiled);
}

[Test]
public void Parse_tiled_invalid_value_reports_line()
{
    var ex = Assert.Throws<PxlParseException>(() =>
        PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ntiled: yes\ngrid:\n  KK\n  KK\n"));
    StringAssert.Contains("invalid tiled value 'yes'", ex.Message);
    StringAssert.Contains("line 5", ex.Message);
}

[Test]
public void Parse_tiled_after_grid_throws()
{
    var ex = Assert.Throws<PxlParseException>(() =>
        PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ngrid:\n  KK\n  KK\ntiled: true\n"));
    // grid 块内的非段头行按像素行校验失败(行宽/未知字符),或掉出 grid 后按
    // "tiled must come before grid" 报——两者都接受,断言只看异常类型。
}

[Test]
public void Parse_tiled_implicit_section_supported()
{
    var doc = PxlParser.Parse("chars:\n  K: #000000\n\ntiled: true\ngrid:\n  KK\n  KK\n");
    Assert.IsTrue(doc.Sections[0].Tiled);
}
```

注意 `Parse_tiled_after_grid_throws` 里 `tiled: true` 行出现在 grid 行之后**且无空行**——它会被当 grid 行校验(行宽不符报错),异常类型即可。`PxlParseException` 的 message 前缀格式以现有测试为准(先看 `Parse_unknown_grid_char_reports_line` 的断言写法,保持一致)。

- [ ] **Step 2: 跑测试确认失败**

refresh + `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], group_names=["PxlParserTests"])`。
预期:`Tiled` 字段不存在 → **编译错误**(read_console 可见 CS1061)。这一步的"红"是编译失败。

- [ ] **Step 3: 实现**

`PxlSection` 加字段(`Border` 之后):

```csharp
public bool Tiled;                   // tiled: true — 运行时按 Image.Type.Tiled 渲染的提示
```

`Parse` 里 `border:` 分支(`if (line.StartsWith("border:", ...))` 块)之后插入:

```csharp
if (line.StartsWith("tiled:", StringComparison.Ordinal))
{
    section = EnsureSection(doc, section, ref sawImplicitContent);
    if (section.Rows.Count > 0)
        throw new PxlParseException(lineNo, "tiled must come before grid");
    var tv = line.Substring("tiled:".Length).Trim();
    // 重复声明 last-wins,同 border:
    section.Tiled = tv switch
    {
        "true" => true,
        "false" => false,
        _ => throw new PxlParseException(lineNo,
            $"invalid tiled value '{tv}' (expected true|false)"),
    };
    continue;
}
```

- [ ] **Step 4: 跑测试确认通过**(同 Step 2 命令,预期全 PASS;顺带跑全量 EditorOnly 防回归)

- [ ] **Step 5: Commit**

```bash
git add Editor/Pxl/PxlParser.cs Tests/EditMode/Editor/Pxl/PxlParserTests.cs
git commit -m "feat(pxl): parser 支持 per-section tiled: 指令"
```

---

### Task 2: `PxlSpriteHints` 子资产 + importer 产出 + Inspector 显示

**Files:**
- Create: `Runtime/Application/PxlSpriteHints.cs`
- Modify: `Editor/Pxl/PxlImporter.cs:67-83`(section 循环)
- Modify: `Editor/Pxl/PxlImporterEditor.cs`(只读面板的 section 行)
- Test: `Tests/EditMode/Editor/Pxl/PxlImporterTests.cs`

- [ ] **Step 1: 写失败测试**(追加到 `PxlImporterTests`;沿用该文件的 `Write(fileName, content)` 辅助)

```csharp
[Test]
public void Import_tiled_section_creates_hints_subasset()
{
    var path = Write("hint.pxl",
        "chars:\n  K: #000000\n\n[a]\ntiled: true\ngrid:\n  KK\n  KK\n\n[b]\ngrid:\n  KK\n  KK\n");
    var hints = AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<PromptUGUI.Application.PxlSpriteHints>().SingleOrDefault();
    Assert.IsNotNull(hints, "tiled section -> hints sub-asset");
    var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
    var a = sprites.Single(s => s.name == "a");
    CollectionAssert.AreEquivalent(new[] { a }, hints.TiledSprites,
        "only the tiled section's sprite is referenced");
}

[Test]
public void Import_no_tiled_sections_no_hints_subasset()
{
    var path = Write("plain.pxl", "chars:\n  K: #000000\n\ngrid:\n  KK\n  KK\n");
    Assert.IsNull(AssetDatabase.LoadAllAssetsAtPath(path)
        .OfType<PromptUGUI.Application.PxlSpriteHints>().SingleOrDefault());
}
```

- [ ] **Step 2: 跑测试确认失败**(`group_names=["PxlImporterTests"]`,预期编译错:类型不存在)

- [ ] **Step 3: 实现**

新文件 `Runtime/Application/PxlSpriteHints.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>.pxl 导入产物的渲染提示子资产:`tiled: true` 的 section 对应的
    /// Sprite 引用清单。运行时由 SpriteRenderHints 各填充口登记;internal——
    /// 作者不直接触碰(transparent default,C# SKILL 免更)。</summary>
    internal sealed class PxlSpriteHints : ScriptableObject
    {
        [SerializeField] private List<Sprite> tiledSprites = new();
        public IReadOnlyList<Sprite> TiledSprites => tiledSprites;
#if UNITY_EDITOR
        internal void SetTiledSpritesInternal(List<Sprite> sprites) => tiledSprites = sprites;
#endif
    }
}
```

`PxlImporter.OnImportAsset` 的 section 循环改为收集 tiled sprite,循环后产出子资产(替换现有 67-83 行循环体):

```csharp
var basename = Path.GetFileNameWithoutExtension(ctx.assetPath);
Texture2D main = null;
List<Sprite> tiledSprites = null;
foreach (var section in doc.Sections)
{
    var name = section.Name ?? basename;
    var tex = BuildTexture(section, colors, name);
    var sprite = Sprite.Create(tex,
        new Rect(0, 0, section.Width, section.Height),
        new Vector2(0.5f, 0.5f), doc.Ppu, 0,
        SpriteMeshType.FullRect, section.Border);
    sprite.name = name;
    ctx.AddObjectToAsset($"tex:{name}", tex);
    ctx.AddObjectToAsset($"sprite:{name}", sprite);
    if (section.Tiled) (tiledSprites ??= new List<Sprite>()).Add(sprite);
    if (main == null) main = tex;
}
if (tiledSprites != null)
{
    var hints = ScriptableObject.CreateInstance<PromptUGUI.Application.PxlSpriteHints>();
    hints.name = "__pxl_hints";
    hints.SetTiledSpritesInternal(tiledSprites);
    ctx.AddObjectToAsset("__pxl_hints", hints);
}
ctx.SetMainObject(main);
```

`PxlImporterEditor` 只读面板:在列出 section 尺寸/border 的那行(先读该文件找到拼接处)追加 tiled 标记——找到形如 `"{name}  {w}x{h}  border L,B,R,T"` 的展示字符串,当该 section 的 sprite 在 hints.TiledSprites 中时 append `"  tiled"`。Editor 侧可 `AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<PxlSpriteHints>()` 获取。纯展示,不写测试。

- [ ] **Step 4: 跑测试确认通过**(`PxlImporterTests` 全绿 + 全量 EditorOnly 防回归)

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/PxlSpriteHints.cs Runtime/Application/PxlSpriteHints.cs.meta \
        Editor/Pxl/PxlImporter.cs Editor/Pxl/PxlImporterEditor.cs \
        Tests/EditMode/Editor/Pxl/PxlImporterTests.cs
git commit -m "feat(pxl): tiled section -> PxlSpriteHints 子资产 + Inspector 标记"
```

(新 .cs 在 Unity refresh 后会生成 .meta,**必须一并提交**。)

---

### Task 3: 运行时登记表 `SpriteRenderHints`

**Files:**
- Create: `Runtime/Application/Internal/SpriteRenderHints.cs`
- Modify: `Runtime/Application/UI.cs`(`ResetForTests` 主体,grep `public static void ResetForTests` 定位)
- Test: 新建 `Tests/EditMode/Application/SpriteRenderHintsTests.cs`(若无 `Tests/EditMode/Application/` 目录则放 `Tests/EditMode/` 根,随邻居)

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    public class SpriteRenderHintsTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Sprite MakeSprite() =>
            Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        [Test]
        public void Register_then_IsTiled_true_idempotent()
        {
            var s = MakeSprite();
            Assert.IsFalse(SpriteRenderHints.IsTiled(s));
            SpriteRenderHints.Register(s);
            SpriteRenderHints.Register(s); // 幂等
            Assert.IsTrue(SpriteRenderHints.IsTiled(s));
        }

        [Test]
        public void Null_safe()
        {
            SpriteRenderHints.Register(null);
            Assert.IsFalse(SpriteRenderHints.IsTiled(null));
        }

        [Test]
        public void ResetForTests_clears()
        {
            var s = MakeSprite();
            SpriteRenderHints.Register(s);
            UI.ResetForTests();
            Assert.IsFalse(SpriteRenderHints.IsTiled(s));
        }
    }
}
```

(namespace `PromptUGUI.Application.Internal` 如与既有 Internal 目录文件的 namespace 惯例不符——先看 `Runtime/Application/Internal/` 里任一文件的 namespace,跟随它,测试 using 同步调整。)

- [ ] **Step 2: 跑测试确认失败**(`group_names=["SpriteRenderHintsTests"]`,预期编译错)

- [ ] **Step 3: 实现**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Internal   // 跟随 Internal 目录既有 namespace
{
    /// <summary>sprite → 渲染提示(目前仅 tiled)的运行时登记表。按 instanceID 存,
    /// 不 pin 资产;重导入后旧 ID 残留无害。填充口:UI.ResolveSprite 的 Resources
    /// 分支、SpriteResolverHelpers.BuildLookup、ProceduralBuilders.GetDefaultSprite。</summary>
    internal static class SpriteRenderHints
    {
        private static readonly HashSet<int> _tiledIds = new();

        public static void Register(Sprite s)
        {
            if (s != null) _tiledIds.Add(s.GetInstanceID());
        }

        public static void Register(PxlSpriteHints hints)
        {
            if (hints == null) return;
            for (var i = 0; i < hints.TiledSprites.Count; i++)
                Register(hints.TiledSprites[i]);
        }

        public static bool IsTiled(Sprite s) =>
            s != null && _tiledIds.Contains(s.GetInstanceID());

        public static void Clear() => _tiledIds.Clear();
    }
}
```

`UI.ResetForTests` 主体内(与其它 `ResetForTestsInternal` 调用并列)加:

```csharp
Internal.SpriteRenderHints.Clear();
```

(`PxlSpriteHints` 在 `PromptUGUI.Application`、registry 在 `...Internal`,跨 namespace 引用按需补 using。)

- [ ] **Step 4: 跑测试确认通过**

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/Internal/SpriteRenderHints.cs Runtime/Application/Internal/SpriteRenderHints.cs.meta \
        Runtime/Application/UI.cs Tests/EditMode/Application/SpriteRenderHintsTests.cs Tests/EditMode/Application/SpriteRenderHintsTests.cs.meta
git commit -m "feat(runtime): SpriteRenderHints tiled 登记表 + ResetForTests 接线"
```

---

### Task 4: `DeriveType` 统一推导 + `ProceduralBuilders` 自身改写

**Files:**
- Modify: `Runtime/Controls/Internal/ProceduralBuilders.cs`(`AutoSlice` / `ApplyDefaultSlicedSprite` / `ApplyDefaultInsetSprite` / `GetDefaultSprite`)
- Test: `Tests/EditMode/Controls/DefaultSkinTests.cs`

- [ ] **Step 1: 写失败测试**(追加到 `DefaultSkinTests`)

```csharp
[Test]
public void DeriveType_four_branches()
{
    Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, ProceduralBuilders.DeriveType(null));

    var plain = Sprite.Create(new Texture2D(4, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
    Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, ProceduralBuilders.DeriveType(plain));

    var bordered = Sprite.Create(new Texture2D(8, 8), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f),
        100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
    Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, ProceduralBuilders.DeriveType(bordered));

    PromptUGUI.Application.Internal.SpriteRenderHints.Register(bordered);
    Assert.AreEqual(UnityEngine.UI.Image.Type.Tiled, ProceduralBuilders.DeriveType(bordered),
        "hint 优先于 border 推导");

    PromptUGUI.Application.Internal.SpriteRenderHints.Register(plain);
    Assert.AreEqual(UnityEngine.UI.Image.Type.Tiled, ProceduralBuilders.DeriveType(plain),
        "无 border 也可平铺(整图无缝纹理)");
}
```

- [ ] **Step 2: 跑测试确认失败**(`group_names=["DefaultSkinTests"]`,预期编译错:DeriveType 不存在)

- [ ] **Step 3: 实现**(`ProceduralBuilders` 内)

```csharp
/// <summary>唯一的 Image.Type 推导点(spec pxl-tiled-hint §6):
/// hint 标 tiled → Tiled;有 border → Sliced;否则 Simple。</summary>
public static UnityImage.Type DeriveType(Sprite s) =>
    s == null                                                  ? UnityImage.Type.Simple :
    PromptUGUI.Application.Internal.SpriteRenderHints.IsTiled(s) ? UnityImage.Type.Tiled :
    s.border != Vector4.zero                                   ? UnityImage.Type.Sliced :
                                                                 UnityImage.Type.Simple;
```

三处改写:

```csharp
// AutoSlice:null sprite 不动的契约保留
public static void AutoSlice(UnityImage img)
{
    if (img == null || img.sprite == null) return;
    img.type = DeriveType(img.sprite);
}

// ApplyDefaultSlicedSprite:删掉硬编码 Tiled(若只做这一步,默认皮肤暂回 Sliced、
// 两个既有 Tiled 断言会红——所以本 Task 的 Step 4 立即给 pugui.pxl 标 tiled 恢复)
img.sprite = s;
img.type = DeriveType(s);

// ApplyDefaultInsetSprite 同样:
img.sprite = s;
img.type = DeriveType(s);
```

`GetDefaultSprite` 的首次加载块(`_defaultSprites == null` 分支内,LoadAll<Sprite> 之后)加自举登记:

```csharp
var hintAssets = Resources.LoadAll<PromptUGUI.Application.PxlSpriteHints>(DefaultSpritesPath);
for (var i = 0; i < hintAssets.Length; i++)
    PromptUGUI.Application.Internal.SpriteRenderHints.Register(hintAssets[i]);
```

**注意排序陷阱**:`UI.ResetForTests` 会 `SpriteRenderHints.Clear()` 但 `_defaultSprites` 缓存也会被 `ResetDefaultSpriteCacheForTests` 置空(确认该方法已挂在 ResetForTests;若没挂,本 Task 把它挂上)——两者同生命周期,下次 GetDefaultSprite 重新登记。

- [ ] **Step 4: 处理预期内的暂时红**

`pugui.pxl` 还没标 `tiled: true`(Task 8),所以本 Task 后
`ApplyDefaultSlicedSprite_SetsRoundTiled` 与 `Tab_DefaultSkin_StaysTiled_AcrossSelection`
会红(默认皮肤暂回 Sliced)。**不要改这两个测试**——直接在本 Task 顺手给
`Runtime/Resources/PromptUGUI/Defaults/pugui.pxl` 的 `[pugui_9slice_round]` 与
`[pugui_9slice_pressed]` 两节 `border: 5,5,5,5` 行后各加一行:

```
tiled: true
```

(Task 8 的其余清理仍留在 Task 8。)refresh 后跑全量 EditMode,预期全绿。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Internal/ProceduralBuilders.cs \
        Runtime/Resources/PromptUGUI/Defaults/pugui.pxl \
        Tests/EditMode/Controls/DefaultSkinTests.cs
git commit -m "feat(runtime): ProceduralBuilders.DeriveType 统一推导 + 默认皮肤经 hints 走 Tiled"
```

---

### Task 5: `UI.ResolveSprite` Resources 分支登记

**Files:**
- Modify: `Runtime/Application/UI.cs:98-124`(ResolveSprite 的裸路径与 `#` 分支)
- Test: `Tests/EditMode/Editor/Pxl/PxlImporterTests.cs`(需要真实导入的 .pxl + Resources 路径,放 EditorOnly)

- [ ] **Step 1: 写失败测试**(追加到 `PxlImporterTests`;注意要用 **Resources** 子目录)

```csharp
[Test]
public void ResolveSprite_resources_path_registers_tiled_hint()
{
    UI.ResetForTests();
    try
    {
        if (!AssetDatabase.IsValidFolder($"{TmpDir}/Resources"))
            AssetDatabase.CreateFolder(TmpDir, "Resources");
        var abs = Path.Combine(UnityEngine.Application.dataPath, "__test_pxl__/Resources", "rt.pxl");
        File.WriteAllText(abs,
            "chars:\n  K: #000000\n\n[a]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n");
        AssetDatabase.ImportAsset($"{TmpDir}/Resources/rt.pxl",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var sprite = UI.ResolveSprite("rt#a");
        Assert.IsNotNull(sprite);
        Assert.IsTrue(PromptUGUI.Application.Internal.SpriteRenderHints.IsTiled(sprite));
    }
    finally { UI.ResetForTests(); }
}
```

- [ ] **Step 2: 跑测试确认失败**(`group_names=["PxlImporterTests"]`,预期 `IsTiled` 为 false → FAIL)

- [ ] **Step 3: 实现**(`UI.ResolveSprite`)

裸路径分支(原 `return Resources.Load<Sprite>(value);`)改:

```csharp
if (hashIdx < 0)
{
    RegisterPxlHints(value);
    return UnityEngine.Resources.Load<UnityEngine.Sprite>(value);
}
```

`#` 分支在 `var all = Resources.LoadAll<Sprite>(path);` 之前(或之后,效果相同)加:

```csharp
RegisterPxlHints(path);
```

同类(`UI`)内加私有方法:

```csharp
// .pxl 导入的 tiled 提示子资产与 Sprite 同路径;LoadAll 有 Unity 资源缓存,重复调用廉价。
private static void RegisterPxlHints(string resourcesPath)
{
    var hints = UnityEngine.Resources.LoadAll<PxlSpriteHints>(resourcesPath);
    for (int i = 0; i < hints.Length; i++)
        Internal.SpriteRenderHints.Register(hints[i]);
}
```

- [ ] **Step 4: 跑测试确认通过**(PxlImporterTests 全绿 + 全量 EditMode 防回归)

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/UI.cs Tests/EditMode/Editor/Pxl/PxlImporterTests.cs
git commit -m "feat(runtime): ResolveSprite Resources 分支登记 pxl tiled 提示"
```

---

### Task 6: `SpriteSet.Entry.tiled` + `BuildLookup` 登记

**Files:**
- Modify: `Runtime/Application/SpriteSet.cs`(Entry 结构、SetEntriesInternal、新 internal 访问器)
- Modify: `Runtime/Application/SpriteResolverHelpers.cs`(BuildLookup)
- Modify: `Tests/PlayMode/Controls/IconRuntimeTests.cs:158`(SetEntriesInternal 签名随动)
- Test: 新建 `Tests/EditMode/Application/SpriteSetTiledEntryTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
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
```

- [ ] **Step 2: 跑测试确认失败**(预期编译错:SetEntriesInternal 还是二元组签名)

- [ ] **Step 3: 实现**

`SpriteSet.Entry` 加字段:

```csharp
[Serializable]
internal struct Entry
{
    public string key;
    public Sprite sprite;
    public bool tiled;   // pxl `tiled: true` 提示,Sync Atlases 烙入(List 序列化向后兼容,旧资产读出 false)
}
```

`SetEntriesInternal` 签名改三元组(唯一编辑器调用方在 Task 7 随动;PlayMode 测试本 Task 随动):

```csharp
internal void SetEntriesInternal(IList<(string key, Sprite sprite, bool tiled)> es)
{
    entries.Clear();
    for (var i = 0; i < es.Count; i++)
        entries.Add(new Entry { key = es[i].key, sprite = es[i].sprite, tiled = es[i].tiled });
    EditorUtility.SetDirty(this);
}
```

公共 `Entries` 二元组 getter **保持原样**(公共面零变化);加 internal 访问器:

```csharp
internal IEnumerable<(string key, Sprite sprite, bool tiled)> EntriesWithMeta
{
    get
    {
        foreach (var e in entries) yield return (e.key, e.sprite, e.tiled);
    }
}
```

`BuildLookup` 末段循环改用 `EntriesWithMeta` 并登记:

```csharp
foreach (var (key, sprite, tiled) in set.EntriesWithMeta)
{
    if (sprite == null) continue;
    if (tiled) PromptUGUI.Application.Internal.SpriteRenderHints.Register(sprite);
    map[$"{set.SetName}:{key}"] = sprite;
}
```

`Tests/PlayMode/Controls/IconRuntimeTests.cs:158` 的 `SetEntriesInternal(entries)`:把 entries 的元素改成三元组(原值 + `false`)——先读该处上下文,把列表构造改为 `(key, sprite, false)` 形。

- [ ] **Step 4: 跑测试确认通过**(新类 + 全量 EditMode;PlayMode 跑 `group_names=["IconRuntimeTests"]`)

- [ ] **Step 5: Commit**

```bash
git add Runtime/Application/SpriteSet.cs Runtime/Application/SpriteResolverHelpers.cs \
        Tests/EditMode/Application/SpriteSetTiledEntryTests.cs Tests/EditMode/Application/SpriteSetTiledEntryTests.cs.meta \
        Tests/PlayMode/Controls/IconRuntimeTests.cs
git commit -m "feat(runtime): SpriteSet entry 携带 tiled 标记,BuildLookup 时登记"
```

(此刻 `Editor/SpriteAtlasSyncer.cs:853` 的调用点会编译错——**本 Task 内先把它最小修复**为 `iconSetEntries` 三元组 + `false` 占位,Task 7 再填真值。最小修复:`iconSetEntries` 类型改 `List<(string key, Sprite sprite, bool tiled)>`,Add 处补 `, false`。)

---

### Task 7: Syncer 烙入 tiled

**Files:**
- Modify: `Editor/SpriteAtlasSyncer.cs:368`(EnumerateSpriteSources 加可选 out 集合)、`:805-853`(调用点 + entry 组装)
- Test: `Tests/EditMode/Editor/Pxl/PxlSyncerTests.cs`

- [ ] **Step 1: 写失败测试**(追加到 `PxlSyncerTests`,沿用其 TestRoot 建 .pxl 的辅助;先读该文件 SetUp 确认根目录与写文件方式)

```csharp
[Test]
public void EnumerateSpriteSources_collects_tiled_sprites()
{
    // 在 TestRoot 写一个含 tiled 节的 .pxl(沿用本文件既有的写文件辅助)
    WritePxl("vineframe.pxl",
        "chars:\n  K: #000000\n\n[vine]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n\n[flat]\ngrid:\n  KK\n  KK\n");
    var tiled = new HashSet<Sprite>();
    var entries = SpriteAtlasSyncer.EnumerateSpriteSources(TestRoot, null, tiled);
    var vine = entries.Single(e => e.pathKey == "vineframe/vine").sprite;
    var flat = entries.Single(e => e.pathKey == "vineframe/flat").sprite;
    Assert.IsTrue(tiled.Contains(vine));
    Assert.IsFalse(tiled.Contains(flat));
}
```

(`WritePxl` 为该文件既有辅助名;若实际名不同,跟随现状。)

- [ ] **Step 2: 跑测试确认失败**(预期编译错:三参重载不存在)

- [ ] **Step 3: 实现**

`EnumerateSpriteSources` 签名加可选参:

```csharp
public static List<(string pathKey, Sprite sprite)> EnumerateSpriteSources(
    string folderAssetPath, string progressLabel = null,
    HashSet<Sprite> tiledOut = null)
```

pxl 分支改为单次 LoadAll、双 OfType:

```csharp
if (AssetImporter.GetAtPath(assetPath) is PxlImporter)
{
    var relPxl = assetPath.Substring(folderPrefix.Length);
    var pxlKey = relPxl.Substring(0, relPxl.Length - Path.GetExtension(relPxl).Length);
    var pxlBase = Path.GetFileNameWithoutExtension(assetPath);
    var objs = AssetDatabase.LoadAllAssetsAtPath(assetPath);
    foreach (var s in objs.OfType<Sprite>())
        result.Add((s.name == pxlBase ? pxlKey : $"{pxlKey}/{s.name}", s));
    if (tiledOut != null)
        foreach (var h in objs.OfType<PromptUGUI.Application.PxlSpriteHints>())
            foreach (var ts in h.TiledSprites)
                if (ts != null) tiledOut.Add(ts);
    continue;
}
```

调用点(`:805` 附近)与 entry 组装(`:846-853`,替换 Task 6 的 `false` 占位):

```csharp
var tiledSprites = new HashSet<Sprite>();
var entries = EnumerateSpriteSources(folder, label, tiledSprites);
...
var iconSetEntries = new List<(string key, Sprite sprite, bool tiled)>();
foreach (var kv in lookup)
{
    if (!picked.Contains(kv.Value)) continue;
    iconSetEntries.Add((kv.Key, kv.Value, tiledSprites.Contains(kv.Value)));
}
set.SetEntriesInternal(iconSetEntries);
```

(裸名别名 entry 与路径 entry 引用同一 Sprite 对象 → `Contains` 同真,spec §5 的"含裸名别名条目"自动满足。)

- [ ] **Step 4: 跑测试确认通过**(`PxlSyncerTests` + 全量 EditorOnly + `SpriteAtlasSyncerTests`)

- [ ] **Step 5: Commit**

```bash
git add Editor/SpriteAtlasSyncer.cs Tests/EditMode/Editor/Pxl/PxlSyncerTests.cs
git commit -m "feat(editor): Sync Atlases 把 pxl tiled 提示烙入 SpriteSet entry"
```

---

### Task 8: 消费端改写 + 回收临时手段

**Files:**
- Modify: `Runtime/Controls/Image.cs:143-150`(auto-pick)
- Modify: `Runtime/Controls/Btn.cs`(`ApplyStateSprite` 内 authored 三元推导)
- Modify: `Runtime/Controls/Tab.cs`(`ApplySelectedSprite` / `ApplyBgSprite` 内三元推导)
- Modify: `Runtime/Controls/Internal/CarouselView.cs:254`(dot sub-sprite)
- Modify: `Samples~/CommonControls/Resources/UI/CommonControls.ui.xml`(删 6 处 `type="tiled"`)
- Test: `Tests/EditMode/Controls/ImageControlTests.cs`(或 Image 测试实际所在文件,grep `auto-pick`/`_typeExplicit` 相关用例定位)

- [ ] **Step 1: 写失败测试**(加到 Image 的测试类;先 grep `Type.Sliced` 找到 auto-pick 既有用例所在文件,追加)

```csharp
[Test]
public void Image_autopick_tiled_for_hinted_sprite()
{
    var bordered = Sprite.Create(new Texture2D(8, 8), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f),
        100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
    PromptUGUI.Application.Internal.SpriteRenderHints.Register(bordered);
    UI.SpriteResolver = _ => bordered;
    // 沿用该测试文件既有的"开屏 + 取 <Image sprite='ui:x'>"辅助
    var img = OpenImage("<Image id='i' sprite='ui:x' width='64' height='64'/>");
    Assert.AreEqual(UnityEngine.UI.Image.Type.Tiled,
        img.GameObject.GetComponent<UnityEngine.UI.Image>().type);
}

[Test]
public void Image_explicit_type_overrides_hint()
{
    var bordered = Sprite.Create(new Texture2D(8, 8), new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f),
        100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
    PromptUGUI.Application.Internal.SpriteRenderHints.Register(bordered);
    UI.SpriteResolver = _ => bordered;
    var img = OpenImage("<Image id='i' sprite='ui:x' type='sliced' width='64' height='64'/>");
    Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced,
        img.GameObject.GetComponent<UnityEngine.UI.Image>().type);
}
```

(`OpenImage` 代表该文件既有的开屏辅助;落地时跟随现有用例的写法。)

- [ ] **Step 2: 跑测试确认失败**(hinted 用例 FAIL:auto-pick 仍是 Sliced)

- [ ] **Step 3: 实现**

`Image.cs` auto-pick(143-150 行)改:

```csharp
if (_typeExplicit) return;
_img.type = PromptUGUI.Controls.Internal.ProceduralBuilders.DeriveType(_img.sprite);
```

`Btn.ApplyStateSprite` 的 authored 推导改:

```csharp
_bg.type = authored != null
    ? PromptUGUI.Controls.Internal.ProceduralBuilders.DeriveType(authored)
    : _baseType;
```

`Tab.ApplySelectedSprite`:

```csharp
_bg.type = showSelected
    ? ProceduralBuilders.DeriveType(_selectedSprite)
    : _baseType;
```

`Tab.ApplyBgSprite`:

```csharp
_bg.sprite = sprite;
_bg.type = ProceduralBuilders.DeriveType(sprite);
_baseType = _bg.type;
```

(Btn 的 `Sprite` setter 已走 `AutoSlice` → Task 4 改写后自动 DeriveType,无需再动。)

`CarouselView.cs:254`:

```csharp
img.sprite = shown;
img.type = ProceduralBuilders.DeriveType(shown);
```

`CommonControls.ui.xml`:

```bash
sed -i 's| type="tiled"||g' Samples~/CommonControls/Resources/UI/CommonControls.ui.xml
dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/UI/CommonControls.ui.xml
```

(sample 走 `path#name` Resources 通道 → Task 5 的登记口 + 本 Task 的 auto-pick 自动 Tiled。)

- [ ] **Step 4: 跑全量**(EditMode 全量 + PlayMode 全量;重点确认既有
`ApplyDefaultSlicedSprite_SetsRoundTiled`、`Tab_DefaultSkin_StaysTiled_AcrossSelection`、
`PressedSprite_With9SliceBorder_OnTransparentNormal_RendersSliced`、
`Tab_SelectedSprite_With9SliceBorder_RendersSliced`、CarouselDots 的 Sliced 用例全部仍绿——
未标记 sprite 行为零变化)

- [ ] **Step 5: Commit**

```bash
git add Runtime/Controls/Image.cs Runtime/Controls/Btn.cs Runtime/Controls/Tab.cs \
        Runtime/Controls/Internal/CarouselView.cs Samples~/CommonControls/Resources/UI/CommonControls.ui.xml \
        Tests/EditMode/Controls/   # Image 测试实际文件
git commit -m "feat(controls): Image/Btn/Tab/Carousel 统一 DeriveType,sample 去显式 tiled"
```

---

### Task 9: SKILL 同步 + 终检

**Files:**
- Modify: `.claude/skills/authoring-promptugui-pxl/SKILL.md`
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: pxl SKILL**(英文)

"Per-section directives" 段、`border:` 条目之后加:

```markdown
- `tiled: true` — optional render hint: every consumer (`<Image>`, `<Btn>`, `<Tab>`, default skins, Carousel cards) automatically renders this sprite with `Image.Type.Tiled` (corners fixed, edge strips and center REPEAT instead of stretch). Use for edges with directional patterns — vines, moss, wood grain, chains. Works with or without `border:` (borderless = the whole sprite tiles, e.g. seamless grass fill). An explicit `type=` in XML still wins. Must appear before `grid:`; repeated declaration last-wins like `border:`.
```

craft 段(9-slice design 条目内)补一句:

```markdown
For `tiled: true` frames, design each edge strip as a repeating unit: the pattern must loop seamlessly across the strip's own width/height AND both ends must return to the plain outline + base fill so corners and the next repeat join invisibly.
```

- [ ] **Step 2: XML SKILL**(英文)

Image `type` 属性说明处(grep `tiled` 定位该行)补:

```markdown
Sprites authored in `.pxl` with `tiled: true` auto-render as Tiled on every control (no `type=` needed); writing `type=` explicitly still overrides the hint.
```

- [ ] **Step 3: lint + 全量终检**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

UnityMCP:EditMode 全量 + EditorOnly 全量 + PlayMode 全量,read_console 零 error。

- [ ] **Step 4: Commit**

```bash
git add .claude/skills/authoring-promptugui-pxl/SKILL.md .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): pxl tiled: 指令 + XML 自动 Tiled 说明"
```

- [ ] **Step 5: 收尾** — 视觉 QA 留给用户(CommonControls 删掉显式 type 后外观应与删前一致);之后走 superpowers:finishing-a-development-branch(push + PR)。

---

## Spec 覆盖自查

- §3 格式 → Task 1;§4 载体/Inspector → Task 2;§5 登记表三填充口 → Task 3(表)+ 5(Resources)+ 6(BuildLookup)+ 4(自举);§6 DeriveType 与改写清单 → Task 4 + 8;§7 回收 → Task 4(pugui 标记+硬编码还原)+ 8(sample/消费端);§8 边界 → Task 3 幂等/Clear 测试、§hints 丢失退化由 DeriveType 分支天然覆盖;§9 测试计划 → 各 Task Step 1;§10 SKILL → Task 9。
- PNG 往返保留 `tiled:`(§3 末条):PxlPngSync 只重写 grid 行,无代码改动,**不加测试**(现行 `ppu:`/`border:` 同理无测试,YAGNI)。
