# 种田风像素默认皮肤 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用 `pugui.pxl`（种田风像素图素）取代 1px 的 `pugui.png` 作为全部内置控件兜底皮肤，并美化 CommonControls 演示界面。

**Architecture:** `.pxl` 文件放在原 Resources 路径（`PromptUGUI/Defaults/pugui`），PxlImporter 的 Sprite 子资产名 = section 名，`Resources.LoadAll<Sprite>` 与 `path#slice` 两条解析路径零改动。旧 4 个 sprite 名原样保留，新增 inset / pressed / knob 三个；C# 仅切换各 builder 的默认 sprite 名 + Btn 增加默认按下态兜底。演示 sample 新增 12 作物 `.pxl` SpriteSet。

**Tech Stack:** Unity 6 uGUI / PromptUGUI `.pxl` 管线（Editor/Pxl）/ NUnit EditMode / UnityMCP / UIXmlLint CLI。

**Spec:** `docs~/superpowers/specs/2026-06-12-farm-pixel-skin-design.md`

**约定（每个任务通用）：**
- 测试通过 UnityMCP：先 `refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`，再 `read_console(action="get", types=["error"])` 确认编译干净，然后 `run_tests`（async，轮询 `get_test_job`）。工具需先 `ToolSearch(query="select:mcp__UnityMCP__run_tests,mcp__UnityMCP__get_test_job,mcp__UnityMCP__refresh_unity,mcp__UnityMCP__read_console", max_results=4)`。
- EditMode 测试类 `[SetUp]`/`[TearDown]` 都要 `UI.ResetForTests()`。
- 改完 C# 跑 `.lint` 的 dotnet format 校验；改完 `.ui.xml` 跑 UIXmlLint CLI。
- 分支 `feat/farm-pixel-skin`，每个任务一个 commit。

---

### Task 1: `pugui.pxl` 资产替换（删除 pugui.png）

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Defaults/pugui.pxl`
- Delete: `Runtime/Resources/PromptUGUI/Defaults/pugui.png` + `pugui.png.meta`
- Gate: `Tests/EditMode/Application/ResolveSpriteTests.cs`（已有，不改）

- [ ] **Step 1: 写 `pugui.pxl`**

完整内容（inline-hex 模式，无 `.gpl` 依赖；7 个 section；旧 4 名保留）：

```
# PromptUGUI 默认控件皮肤 — 种田风像素（spec 2026-06-12-farm-pixel-skin §3）
# 奶油底 + 暖木描边 + 叶绿点缀。mask 必须纯白（stencil, 4af322b）。
ppu: 100
chars:
  K: #5A3A20
  W: #8B5E3C
  w: #C68B52
  H: #FFFBEA
  F: #FFF1D2
  f: #EFD9A8
  I: #E8CFA0
  P: #F0DBAE
  G: #58A63C
  g: #7CC850
  M: #FFFFFF

[pugui_9slice_round]
border: 4,4,4,4
grid:
  ..KKKKKKKK..
  .KHHHHHHHHK.
  KHHFFFFFFFfK
  KHFFFFFFFFfK
  KHFFFFFFFFfK
  KHFFFFFFFFfK
  KHFFFFFFFFfK
  KHFFFFFFFFfK
  KHFFFFFFFFfK
  KHfFFFFFFffK
  .KffffffffK.
  ..KKKKKKKK..

[pugui_9slice_pressed]
border: 4,4,4,4
grid:
  ..KKKKKKKK..
  .KffffffffK.
  KffPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPPHK
  KfPPPPPPPHHK
  .KHHHHHHHHK.
  ..KKKKKKKK..

[pugui_9slice_inset]
border: 4,4,4,4
grid:
  ..KKKKKKKK..
  .KWWWWWWWWK.
  KWWIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIIfK
  KWIIIIIIIffK
  .KffffffffK.
  ..KKKKKKKK..

[pugui_knob]
grid:
  ...KKKKK...
  ..KwwwwwK..
  .KwHHHHwwK.
  KwHHFFFFwwK
  KwHFFFFFFwK
  KwHFFFFFFwK
  KwHFFFFFFwK
  KwwFFFFFwwK
  .KwwFFFwwK.
  ..KwwwwwK..
  ...KKKKK...

[pugui_9slice_mask]
border: 4,4,4,4
grid:
  ..MMMMMMMM..
  .MMMMMMMMMM.
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  MMMMMMMMMMMM
  .MMMMMMMMMM.
  ..MMMMMMMM..

[pugui_caret]
grid:
  KKKKKKK
  KwwwwwK
  .KwwwK.
  ..KwK..
  ...K...

[pugui_checkmark]
grid:
  .......K.
  ......KgK
  .....KgK.
  .K..KgK..
  KgK.KgK..
  KggKgK...
  .KgggK...
  ..KgK....
  ...K.....
```

写完逐行自校验：每个 grid 行宽一致（round/pressed/inset/mask=12、knob=11、caret=7、checkmark=9）、轮廓闭合、9-slice 中段行完全相同。

- [ ] **Step 2: 删除旧 png**

```bash
git rm Runtime/Resources/PromptUGUI/Defaults/pugui.png Runtime/Resources/PromptUGUI/Defaults/pugui.png.meta
```

- [ ] **Step 3: Unity 刷新 + console 检查**

`refresh_unity(...)` 后 `read_console(types=["error"])`：无 PxlImporter 导入错误、无丢引用。

- [ ] **Step 4: 跑守门测试**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ResolveSpriteTests"])` → 全过（`pugui.png#slice` / `pugui#slice` / `.aseprite#slice` 的扩展名剥离路径全部命中 .pxl 子资产）。再跑 `group_names=["ImageMaskTests"]` 确认 mask 路径无回归。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Resources/PromptUGUI/Defaults/pugui.pxl
git commit -m "feat(skin): pugui.pxl 种田风像素默认图素替换 pugui.png（旧4名保留+新增inset/pressed/knob）"
```

---

### Task 2: ProceduralBuilders 新常量 + inset helper + label 暖棕

**Files:**
- Modify: `Runtime/Controls/Internal/ProceduralBuilders.cs`
- Test: `Tests/EditMode/Controls/DefaultSkinTests.cs`（新建）

- [ ] **Step 1: 写红测**

新建 `Tests/EditMode/Controls/DefaultSkinTests.cs`（装载/Reset 样板照抄 `Tests/EditMode/Controls/BtnStateTests.cs` 顶部的 SetUp/TearDown + Build 辅助；该文件已有 `UI.ResetForTests()` 与 XML 装载 helper，可直接搬）：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;

public class DefaultSkinTests
{
    [SetUp] public void SetUp() => UI.ResetForTests();
    [TearDown] public void TearDown() => UI.ResetForTests();

    [TestCase(ProceduralBuilders.SpriteRoundedRect)]
    [TestCase(ProceduralBuilders.SpriteMaskRoundedRect)]
    [TestCase(ProceduralBuilders.SpriteCaret)]
    [TestCase(ProceduralBuilders.SpriteCheckmark)]
    [TestCase(ProceduralBuilders.SpriteInset)]
    [TestCase(ProceduralBuilders.SpritePressed)]
    [TestCase(ProceduralBuilders.SpriteKnob)]
    public void GetDefaultSprite_ResolvesAllSkinSections(string name)
    {
        ProceduralBuilders.ResetDefaultSpriteCacheForTests();
        Assert.IsNotNull(ProceduralBuilders.GetDefaultSprite(name), name);
    }

    [Test]
    public void ApplyDefaultInsetSprite_SetsInsetSliced()
    {
        var go = new GameObject("img", typeof(RectTransform));
        var img = go.AddComponent<UnityEngine.UI.Image>();
        ProceduralBuilders.ApplyDefaultInsetSprite(img);
        Assert.AreEqual("pugui_9slice_inset", img.sprite.name);
        Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**（CS0117 `SpriteInset` 不存在 → 编译错即红）

- [ ] **Step 3: 实现**

`ProceduralBuilders.cs` 常量区追加：

```csharp
public const string SpriteInset = "pugui_9slice_inset";
public const string SpritePressed = "pugui_9slice_pressed";
public const string SpriteKnob = "pugui_knob";
```

`ApplyDefaultSlicedSprite` 下方追加：

```csharp
/// <summary>凹形容器（输入框/滑轨/列表底）的 9-slice 兜底；规则同 ApplyDefaultSlicedSprite。</summary>
public static void ApplyDefaultInsetSprite(UnityImage img)
{
    if (img == null || img.sprite != null) return;
    var s = GetDefaultSprite(SpriteInset);
    if (s == null) return;
    img.sprite = s;
    img.type = UnityImage.Type.Sliced;
}
```

`s_darkGrey` 改暖深棕（注释同步）：

```csharp
// 单一暖深棕色源（#4A3322），匹配种田风奶油皮肤；换主题色只动这一行
private static readonly Color s_darkGrey = new(0.290f, 0.200f, 0.133f, 1f);
```

- [ ] **Step 4: 跑 DefaultSkinTests 全绿；再全量 EditMode 确认 label 色变更无快照断言挂掉**

- [ ] **Step 5: Commit** `feat(skin): inset/pressed/knob 常量 + ApplyDefaultInsetSprite + label 暖棕`

---

### Task 3: 凹形容器默认 sprite 切换（InputField / Slider / ScrollList / Dropdown scrollbar）

**Files:**
- Modify: `Runtime/Controls/InputField.cs:46`、`Runtime/Controls/Slider.cs:43,74`、`Runtime/Controls/ScrollList.cs:48,317,351`、`Runtime/Controls/Dropdown.cs:139`
- Test: `Tests/EditMode/Controls/DefaultSkinTests.cs`（追加）

- [ ] **Step 1: 写红测**（追加到 DefaultSkinTests；XML 装载 helper 模式照抄 BtnStateTests 的 Build 辅助，本质是 `UI.LoadDocument` + `UI.Open` + `screen.Get<T>`）：

```csharp
[Test]
public void InputField_DefaultBg_IsInset()
{
    var screen = OpenScreen("<InputField id='f' width='200' height='40'/>");
    var bg = screen.Get<PromptUGUI.Controls.InputField>("f").GameObject
                   .GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_inset", bg.sprite.name);
}

[Test]
public void Slider_DefaultTrackInset_HandleKnob()
{
    var screen = OpenScreen("<Slider id='s' width='200' height='40'/>");
    var root = screen.Get<PromptUGUI.Controls.Slider>("s").GameObject.transform;
    var track = root.Find("Background").GetComponent<UnityEngine.UI.Image>();
    var handle = root.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_inset", track.sprite.name);
    Assert.AreEqual("pugui_knob", handle.sprite.name);
    Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, handle.type);
}

[Test]
public void ScrollList_DefaultBg_IsInset()
{
    var screen = OpenScreen("<ScrollList id='l' width='200' height='100'/>");
    var bg = screen.Get<PromptUGUI.Controls.ScrollList>("l").GameObject
                   .GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_inset", bg.sprite.name);
}
```

（`OpenScreen` helper：`UI.LoadDocument("skin", "<PromptUGUI version='1'><Screen name='Skin'>" + body + "</Screen></PromptUGUI>")` 后 `UI.Open("Skin")`，对齐 BtnStateTests 现有写法；编译细节以该文件为准。）

- [ ] **Step 2: 跑测试确认失败**（当前都是 `pugui_9slice_round`）

- [ ] **Step 3: 实现** —— 逐处把 `ApplyDefaultSlicedSprite` 换 `ApplyDefaultInsetSprite`：
  - `InputField.cs:46`（`_bg`）
  - `Slider.cs:43`（`_bg` 轨道）；`Slider.cs:74` 改 `ApplyDefaultSimpleSprite(_handle, ProceduralBuilders.SpriteKnob)` 并删掉"临时复用 round 当 knob"的注释
  - `ScrollList.cs:48`（`_bg`）、`:317`、`:351`（横竖 scrollbar bg；`:325`/`:359` 的 handle 保持 round 不动）
  - `Dropdown.cs:139`（popup scrollbar bg；`_bg`/`_templateBg`/item/handle 不动）

- [ ] **Step 4: 跑 DefaultSkinTests + DropdownTests + ScrollListTests 全绿**

- [ ] **Step 5: Commit** `feat(skin): 凹形容器默认 inset 槽 + Slider knob 手柄`

---

### Task 4: Btn 默认按下态兜底（pugui_9slice_pressed）

**Files:**
- Modify: `Runtime/Controls/Btn.cs`
- Test: `Tests/EditMode/Controls/BtnStateTests.cs`（追加）

设计要点：默认兜底走 `ApplyStateSprite` 的**惰性 fallback**，不写进 `_pressedSprite` 字段 → `OnAfterApply` 的 transition=None 翻转（Btn.cs:104）只对**作者显式**的 pressedSprite 生效，默认按钮保留 uGUI ColorTint（hover 反馈不丢，按下 = sprite 换图 × tint 复合，略深可接受）。作者写过 `pressedSprite`（含 `""`/`none`）或换过 `sprite=` 后兜底自动让位。

- [ ] **Step 1: 写红测**（追加到 BtnStateTests，复用其 `BuildBtn` / `SimulateState`）：

```csharp
[Test]
public void DefaultBtn_PressedFallsBackToBuiltinPressedSprite()
{
    var btn = BuildBtn("");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    var puiBtn = btn.GameObject.GetComponent<PuiButton>();

    puiBtn.SimulateState(Pressed);
    Assert.AreEqual("pugui_9slice_pressed", bg.overrideSprite.name);
    puiBtn.SimulateState(Normal);
    Assert.AreEqual(bg.sprite, bg.overrideSprite, "release 后回落 base sprite");
}

[Test]
public void DefaultBtn_PressedFallback_KeepsColorTintTransition()
{
    var btn = BuildBtn("");
    Assert.AreEqual(Selectable.Transition.ColorTint,
        btn.GameObject.GetComponent<PuiButton>().transition,
        "默认兜底不得触发 transition=None（hover 反馈保留）");
}

[Test]
public void AuthoredSprite_SuppressesDefaultPressedFallback()
{
    var stub = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
    UI.SpriteResolver = _ => stub;
    var btn = BuildBtn("sprite='ui:custom'");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    btn.GameObject.GetComponent<PuiButton>().SimulateState(Pressed);
    Assert.AreEqual(bg.sprite, bg.overrideSprite, "自定皮肤按钮没有内置按下图");
}

[Test]
public void PressedSpriteEmpty_SuppressesDefaultPressedFallback()
{
    var btn = BuildBtn("pressedSprite=''");
    var bg = btn.GameObject.GetComponent<UnityImage>();
    btn.GameObject.GetComponent<PuiButton>().SimulateState(Pressed);
    Assert.AreEqual(bg.sprite, bg.overrideSprite, "显式 ''/none = 关闭换图，包括默认兜底");
}
```

- [ ] **Step 2: 跑测试确认前两个失败**（当前默认按钮 Pressed 无 override）

- [ ] **Step 3: 实现**（`Btn.cs`）：

字段区加 `private bool _pressedSpriteAuthored;`；`PressedSprite` setter 第一行加 `_pressedSpriteAuthored = true;`。`ApplyStateSprite` 改为：

```csharp
// Swaps the bg's overrideSprite (never its authored `sprite`) so revert is overrideSprite=null.
// Priority Disabled > Pressed; Pressed falls back to the built-in pressed skin when the
// author customized nothing (keeps ColorTint — only AUTHORED pressed/disabled sprites
// flip transition=None in OnAfterApply).
private void ApplyStateSprite(InteractState state)
    => _bg.overrideSprite = state == InteractState.Disabled ? _disabledSprite
                          : state == InteractState.Pressed ? (_pressedSprite ?? DefaultPressedSprite())
                          : null;

private Sprite DefaultPressedSprite()
{
    if (_pressedSpriteAuthored) return null;
    var round = ProceduralBuilders.GetDefaultSprite(ProceduralBuilders.SpriteRoundedRect);
    if (round == null || _bg.sprite != round) return null;   // 作者换过 sprite= → 让位
    return ProceduralBuilders.GetDefaultSprite(ProceduralBuilders.SpritePressed);
}
```

（惰性求值 = ReSolve 幂等：每次状态变化按当下 `_bg.sprite` 重新判定，无需缓存清理。）

- [ ] **Step 4: 跑 BtnStateTests 全组绿（含既有 pressedSprite 四测）**

- [ ] **Step 5: Commit** `feat(skin): Btn 默认按下态兜底 pugui_9slice_pressed（作者覆盖让位，保留 ColorTint）`

---

### Task 5: 内置 XML 调色（Modals / Toast / Tutorial）

**Files:**
- Modify: `Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml`、`InputBox.ui.xml`、`MarkdownBox.ui.xml`、`Loading.ui.xml`、`Runtime/Resources/PromptUGUI/Tutorial/TutorialOverlay.ui.xml`
- 不改：`Toast.ui.xml`（白字泛用，换肤示例已在 C# SKILL）

- [ ] **Step 1: 逐文件修改**

统一替换规则：
1. `sprite="PromptUGUI/Defaults/pugui.png#..."` → `sprite="PromptUGUI/Defaults/pugui#..."`（去扩展名，资产已不是 png）。
2. backdrop `color="#000000FE"` → `color="#241505FE"`（暖深棕，alpha 字节保持 FE 惯例）。
3. MessageBox/InputBox：`<Btn id="ok">` 与 `<Btn id="yes">` 加 `color="#A8D88C"`（叶绿 tint）；`<Btn id="cancel">` / `<Btn id="no">` 加 `color="#E0B888"`（木棕 tint）；`close` 保持默认。
4. Loading：三个白点 `color="white"` → `color="#FFD86B"`（麦黄）；`text` 的 `color="white"` 保持（深棕 backdrop 上可读）。
5. TutorialOverlay：bubble `color="#222222EE"` → `color="#FFF8E8F0"`（奶油底），`bubbleText` `color="white"` → `color="#4A3322"`。finger 无 color（caret 自带深木色）。
6. MarkdownBox `close` 钮保持 `sprite="" color="#00000000"`，仅 `fontSize` 字色随默认 label 变暖棕，无需改。

- [ ] **Step 2: UIXmlLint**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```
Expected: exit 0。

- [ ] **Step 3: Unity 刷新后跑 EditMode `group_names=["MessageBoxTests","InputBoxTests","MarkdownBoxTests","LoadingTests","ToastTests"]`（按实际存在的组名，先 grep Tests/ 确认）+ PlayMode 全量**

- [ ] **Step 4: Commit** `feat(skin): 内置模态/Toast/Tutorial 配色对齐种田皮肤`

---

### Task 6: SKILL 更新

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/states.md`
- Modify（按 grep 结果）: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1:** `states.md` 的 `pressedSprite` 小节加一句（英文）：默认皮肤按钮（未写 `sprite=`/`pressedSprite=`）自动获得内置 pressed 9-slice 按下态；写 `pressedSprite=""`/`"none"` 可关闭；任何作者自定 sprite 都使兜底让位且不再翻转 transition 行为不变。
- [ ] **Step 2:** `grep -n "white\|Unity-style\|grey" .claude/skills/authoring-promptugui-xml/SKILL.md .claude/skills/scripting-promptugui-csharp/SKILL.md` —— 凡描述"默认白底可 tint"的措辞改为"farm-style cream/wood pixel skin, still tintable via `color=`"。
- [ ] **Step 3: Commit** `docs(skill): 默认皮肤描述 + Btn 默认 pressed 兜底`

---

### Task 7: Sample 作物图素 `crops.pxl` + SpriteSet 接线

**Files:**
- Create: `Samples~/CommonControls/Sprites/crops.pxl`
- Modify: `Samples~/CommonControls/CommonControlsRunner.cs`

- [ ] **Step 1: 写 `crops.pxl`**（12 section、11×11、inline hex；XML 以 `farm:carrot` 等裸名引用）

```
# CommonControls sample 作物图标橱窗（12 个 11x11）
ppu: 100
chars:
  K: #3A2A18
  G: #3E8A2E
  g: #7CC850
  O: #F2802E
  o: #F8A858
  R: #E84A3F
  r: #F47C6A
  Y: #E8C84B
  y: #F8E08A
  S: #C8A03A
  P: #7C4DA5
  p: #9C6DC5
  B: #8B5E3C
  b: #A87E5C
  W: #F8F0E8
  w: #E0D0C0
  D: #2A1A10

[carrot]
grid:
  ....G.G....
  ...GgGgG...
  ....GGG....
  ...KOOOK...
  ...KOoOK...
  ...KOOOK...
  ....KOoK...
  ....KOOK...
  .....KOK...
  .....KOK...
  ......K....

[tomato]
grid:
  ....GGG....
  ..G.GGG.G..
  ..KKRRRKK..
  .KRRrRRRRK.
  KRRrrRRRRRK
  KRRrRRRRRRK
  KRRRRRRRRRK
  KRRRRRRRRRK
  .KRRRRRRRK.
  ..KKRRRKK..
  ....KKK....

[wheat]
grid:
  ....KyK....
  ...KyYyK...
  ....KYK....
  ...KyYyK...
  ....KYK....
  ...KyYyK...
  ....KYK....
  ....KSK....
  ....KSK....
  ....KSK....
  .....K.....

[apple]
grid:
  .....KG....
  .....KGG...
  ..KKRKRKK..
  .KRrRRRRRK.
  KRrrRRRRRRK
  KRrRRRRRRRK
  KRRRRRRRRRK
  KRRRRRRRRRK
  .KRRRRRRRK.
  ..KRRRRRK..
  ...KKKKK...

[corn]
grid:
  ....KYK....
  ...KYyYK...
  ..GKYyYKG..
  ..GKYYYKG..
  .GGKYyYKGG.
  .G.KYYYK.G.
  ....KYyK...
  ....KYYK...
  .....KYK...
  .....KK....
  ...........

[pumpkin]
grid:
  .....G.....
  ....GG.....
  ..KKOOOKK..
  .KOoOoOoOK.
  KOoOOoOOoOK
  KOoOOoOOoOK
  KOoOOoOOoOK
  KOoOOoOOoOK
  .KOoOoOoOK.
  ..KKOOOKK..
  ...KKKKK...

[strawberry]
grid:
  ....GGG....
  ..GGGGGGG..
  .KRRRRRRRK.
  KRyRRyRRyRK
  KRRRRRRRRRK
  KRyRRyRRyRK
  .KRRRRRRRK.
  ..KRyRRyK..
  ...KRRRK...
  ....KRK....
  .....K.....

[eggplant]
grid:
  ......GG...
  .....GG....
  ....KKK....
  ...KPpPK...
  ...KPpPK...
  ..KPpPPK...
  ..KPpPPK...
  .KPpPPPPK..
  .KPPPPPPK..
  ..KPPPPK...
  ...KKKK....

[sunflower]
grid:
  ...KKKKK...
  ..KYYYYYK..
  .KYYKKKYYK.
  KYYKBBBKYYK
  KYKBBbBBKYK
  KYKBBbbBKYK
  KYKBBBBBKYK
  KYYKBBBKYYK
  .KYYKKKYYK.
  ..KYYYYYK..
  ...KKKKK...

[grape]
grid:
  .....KG....
  ....KGG....
  ..KKKKKK...
  .KPpKPpPK..
  .KPPKPPPK..
  ..KKKKKK...
  ...KPpPK...
  ...KPPPK...
  ....KKK....
  ...........
  ...........

[watermelon]
grid:
  ...........
  .KKKKKKKKK.
  KRRDRRDRRRK
  KRRRRRRRRRK
  KRDRRDRRDRK
  .KRRRRRRRK.
  .KgRRRRRgK.
  ..KgggggK..
  ..KGGGGGK..
  ...KKKKK...
  ...........

[turnip]
grid:
  ....G.G....
  ...GgGgG...
  ....GGG....
  ...KWWWK...
  ..KWwWWWK..
  .KWWwWWWWK.
  .KWWWWWWWK.
  ..KWWWWWK..
  ...KWWWK...
  ....KWK....
  .....K.....
```

逐 section 自校验行宽（全部 11，watermelon/corn/grape 的纯 `.` 行也是 11）。

- [ ] **Step 2: Runner 加 SpriteSet 字段**（对齐 `Samples~/MainMenu/MainMenuRunner.cs` 既有模式）：

```csharp
public sealed class CommonControlsRunner : MonoBehaviour
{
    [SerializeField] SpriteSet[] spriteSets;   // 拖 FarmSpriteSet.asset（README/注释说明）

    async void Start()
    {
        UI.UseResourcesResolver("UI");
        if (spriteSets != null && spriteSets.Length > 0)
            SpriteResolverHelpers.UseSpriteSetResolver(spriteSets);
        ...
```

类注释的"使用步骤"同步加一行"把 FarmSpriteSet.asset 拖到 Sprite Sets 字段"。

- [ ] **Step 3: Commit** `feat(samples): 12 作物 crops.pxl + Runner SpriteSet 接线`

---

### Task 8: CommonControls.ui.xml 美化 + Runner 暖色数据

**Files:**
- Modify: `Samples~/CommonControls/Resources/UI/CommonControls.ui.xml`（整体重写视觉层）
- Modify: `Samples~/CommonControls/CommonControlsRunner.cs`（仅 BindListPage 卡片色）

- [ ] **Step 1: 重写 XML 视觉层**（id / 结构 / 绑定全部不变；要点 diff）：

1. 背景：`<Image anchor="stretch" color="#202020"/>` → 双层
   `<Image anchor="stretch" color="#BDE3EC"/>` + `<Image anchor="bottom-stretch" height="56" color="#8FCF6A" raycastTarget="false"/>`。
2. 标题行：`<Text fontSize="22">` 换成木牌 —— `<Image sprite="PromptUGUI/Defaults/pugui#pugui_9slice_round" color="#E8B86B" width="220" height="30"><Text anchor="center" fontSize="18">Common Controls Demo</Text></Image>`；`tutorialBtn` 删 `color`/`pressedModulate`（默认皮肤 + 默认 pressed 兜底）。
3. TabBar 四个 Tab：删 `color="#333333"`，`selectedColor="#3B82F6"` → `selectedColor="#CDEBA8"`。
4. 每页 `<Frame id="pageX">` 第一个子节点加奶油底板
   `<Image anchor="stretch" sprite="PromptUGUI/Defaults/pugui#pugui_9slice_round" raycastTarget="false"/>`。
5. ① 表单页：控件零 color（InputField 凹槽 / Slider 木 knob / Toggle 绿勾全靠新默认）。
6. ② 展示页：Progress `bgColor="#222222" fillColor="#3CC3F0"` → `bgColor="#E8CFA0" fillColor="#58A63C"`；`progMinus`/`progPlus`/`pulseBtn` 删全部 color/pressedModulate/hoverColor（hover 角标 `<Image color="#F59E0B">` → `<Image sprite="farm:strawberry"/>` 顺带演示 SpriteSet）。
7. ③ 列表页：Grid 12 个 `<Image color="#xxx"/>` → `<Image sprite="farm:carrot"/>`、`tomato`、`wheat`、`apple`、`corn`、`pumpkin`、`strawberry`、`eggplant`、`sunflower`、`grape`、`watermelon`、`turnip`；DemoCard 模板 `<Image id="bg" color="#444444"/>` → `<Image id="bg" sprite="PromptUGUI/Defaults/pugui#pugui_9slice_round"/>`（C# tint 上色）；Carousel `dotColor="#666666" dotSelectedColor="#FFFFFF"` → `dotColor="#C8A878" dotSelectedColor="#5A3A20"`。
8. ④ 模态页：五个 Btn 删 color/pressedModulate，仅 `toastBtn` 保留 `color="#A8D88C"` 当 tint 示例（注释注明）。

- [ ] **Step 2: Runner 卡片色改暖**（`BindListPage`）：

```csharp
("欢迎使用 PromptUGUI", "#F2B24C"),
("XML 直接生成 uGUI", "#8FCF6A"),
("轮播卡自动播放", "#F28C6A"),
```

- [ ] **Step 3: UIXmlLint**

```bash
dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/UI/CommonControls.ui.xml
```
Expected: exit 0（注意页底板 Image 是 Frame 直下不是 layout child，合法）。

- [ ] **Step 4: Commit** `feat(samples): CommonControls 演示界面种田风美化（默认皮肤裸奔 + farm 图标橱窗）`

---

### Task 9: 宿主工程落地（SpriteSet asset / Sync Atlases / 场景接线 / 回拷）

**Files:**
- Create（先在宿主生成再回拷）: `Samples~/CommonControls/Resources/FarmSpriteSet.asset`(+meta)、`Samples~/CommonControls/FarmIcons.spriteatlasv2`(+meta)、`Samples~/CommonControls/Sprites.meta`、`Samples~/CommonControls/Sprites/crops.pxl.meta`
- Modify: `Samples~/CommonControls/CommonControls.unity`（Runner 的 spriteSets 字段）

宿主 sample 副本在 `UnityProjects~/PromptUGUIDev/Assets/Samples/CommonControls/`。

- [ ] **Step 1:** 把 Task 7/8 的 sample 改动拷进宿主副本（`cp -r` Sprites/、Runner.cs、CommonControls.ui.xml），`refresh_unity` + console 确认 crops.pxl 12 section 导入无错。
- [ ] **Step 2:** 用 `mcp__UnityMCP__execute_code` 创建 SpriteSet asset（`ScriptableObject.CreateInstance<SpriteSet>()` + `SerializedObject` 填 `setName="farm"`、`sourceFolder=Assets/Samples/CommonControls/Sprites` 的 DefaultAsset，`AssetDatabase.CreateAsset` 到 `Assets/Samples/CommonControls/Resources/FarmSpriteSet.asset`），对照 `Samples~/MainMenu/SolarSpriteSet.asset` 的字段形态。
- [ ] **Step 3:** 跑 Sync Atlases（菜单在 Tools → PromptUGUI → Sprite 下，用 `unity_reflect`/菜单枚举确认精确路径后 `execute_menu_item`；**禁止** Assets/Reimport All）。确认 FarmSpriteSet entries 填了 12 key + 裸名别名、atlas 生成；若 atlas 糊（双线性），在 atlas importer 上设 filterMode=Point 后重打。
- [ ] **Step 4:** 场景接线：打开 CommonControls 场景，把 FarmSpriteSet.asset 拖给 Runner 的 spriteSets（`manage_components` 设置序列化字段），保存场景。
- [ ] **Step 5:** PlayMode 冒烟：进 Play，console 无 "resolver returned null"、无 sprite 丢失 error；四页切换正常。
- [ ] **Step 6:** 把宿主新生成的 FarmSpriteSet.asset(+meta)、FarmIcons.spriteatlasv2(+meta)、Sprites/ 下 .meta、更新后的 CommonControls.unity 回拷到 `Samples~/CommonControls/` 对应路径。
- [ ] **Step 7: Commit** `feat(samples): FarmSpriteSet + atlas + 场景接线（宿主 Sync 产物回拷）`

---

### Task 10: 全量验证 + 收尾

- [ ] **Step 1:** UnityMCP 全量：
  - `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`（基线 1641，新增约 +10）
  - `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
  - `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`（基线 136）
- [ ] **Step 2:** dotnet lint：

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

- [ ] **Step 3:** UIXmlLint 全仓兜底：`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` + sample xml。
- [ ] **Step 4:** 收尾 commit（若有散落改动），推分支，开 PR（base main），PR 描述附 spec 链接 + 截图位（视觉 QA 留用户）。

---

## Self-Review 记录

- spec §3 七 section ↔ Task 1；§4 C# ↔ Task 2-4；§5 ↔ Task 5；§6 ↔ Task 7-9；§7 测试 ↔ 各任务 + Task 10。spec §6.2 提到的 ".gpl 可选" 最终选择 inline hex（双处一致，已在 Task 7 注明理由：避免消费者工程 palette 撞名）。
- 类型一致性：`SpriteInset`/`SpritePressed`/`SpriteKnob`/`ApplyDefaultInsetSprite` 在 Task 2 定义、Task 3/4 引用，名字一致。
- 已知留白（显式声明，非占位符）：测试装载 helper 的精确签名以 `BtnStateTests.cs` 现有代码为准；Sync Atlases 菜单精确路径执行时枚举确认；像素网格允许执行时按导入结果微调（行宽规则不变）。
