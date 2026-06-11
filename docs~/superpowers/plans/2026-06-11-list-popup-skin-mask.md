# ScrollList / Dropdown 换肤 + mask 属性 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `<ScrollList>` 新增 `frame=` / `frameColor=` / `mask=`，`<Dropdown>` 新增 `popupSprite=` / `popupColor=` / `popupMask=`，对齐 `<Progress>` 的 bg/frame/mask 词汇。

**Architecture:** mask 三态逻辑（null=默认 sprite + stencil；""=RectMask2D 直角；其他=指定 sprite + stencil）抽成 `ProceduralBuilders.ApplyViewportMask` 一份实现，两控件 OnAttached 与新 setter 共用；切换用 lazy-add + `enabled` 开关，不 Destroy。`AutoSlice` 从 Progress 提为公共。frame 层懒创建顶层 sibling，`OnAfterApply` 里 `SetAsLastSibling()` 保证压在懒建的 Scrollbar 之上。

**Tech Stack:** Unity 6 uGUI（Mask / RectMask2D / Image），EditMode NUnit via UnityMCP，dotnet format lint。

**Spec:** `docs~/superpowers/specs/2026-06-11-list-popup-skin-mask-design.md`

**前置状态：** 分支 `feat/list-popup-skin-mask` 已建（spec 已提交）。每个任务结束跑 lint：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（首次需 `dotnet restore PromptUGUI.Lint.slnx`）。

**测试命令模板**（每次源码改动后先 refresh 再跑）：

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])     # 确认无编译错误
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["<ClassName>"])
mcp__UnityMCP__get_test_job(job_id=...)                        # 轮询到完成
```

---

### Task 1: `ProceduralBuilders.AutoSlice` 公共化（纯重构）

**Files:**
- Modify: `Runtime/Controls/Internal/ProceduralBuilders.cs`（在 `ApplyDefaultSimpleSprite` 之后加方法）
- Modify: `Runtime/Controls/Progress.cs`（删私有 `AutoSlice`，5 处调用改指向公共版）

- [ ] **Step 1: ProceduralBuilders 加 AutoSlice**

```csharp
/// <summary>sprite 有 border → Sliced，否则 Simple；null sprite 不动（镜像原 Progress 私有版规则）。</summary>
public static void AutoSlice(UnityImage img)
{
    if (img == null || img.sprite == null) return;
    img.type = img.sprite.border != Vector4.zero
        ? UnityImage.Type.Sliced
        : UnityImage.Type.Simple;
}
```

- [ ] **Step 2: Progress 删私有 AutoSlice、改调用**

删除 `Progress.cs:167-173` 的 `private static void AutoSlice(UnityImage img)` 方法；文件内 5 处 `AutoSlice(...)` 调用（`Bg` setter、`Frame` setter、`Mask` setter、`OnAfterApply` 里 3 连发）全部改为 `ProceduralBuilders.AutoSlice(...)`。Progress.cs 已 `using PromptUGUI.Controls.Internal;`（确认 using 列表，没有就加）。

- [ ] **Step 3: refresh + 跑 Progress 回归**

Run: `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ProgressTests"])`
Expected: 全 PASS（纯重构零行为变化）。

- [ ] **Step 4: lint + commit**

```bash
git add Runtime/Controls/Internal/ProceduralBuilders.cs Runtime/Controls/Progress.cs
git commit -m "refactor: AutoSlice 提为 ProceduralBuilders 公共方法"
```

---

### Task 2: `ApplyViewportMask` helper + ScrollList / Dropdown OnAttached 改用（行为不变重构）

**Files:**
- Modify: `Runtime/Controls/Internal/ProceduralBuilders.cs`
- Modify: `Runtime/Controls/ScrollList.cs:48-59`（Viewport 构建段）
- Modify: `Runtime/Controls/Dropdown.cs:71-85`（popup Viewport 构建段）

- [ ] **Step 1: ProceduralBuilders 加 ApplyViewportMask**

```csharp
/// <summary>
/// Viewport mask 三态（spec 2026-06-11-list-popup-skin-mask §2.3）：
/// value == null → 默认 sprite + stencil Mask（OnAttached 初始形态）；
/// value == ""   → RectMask2D 直角裁剪（stencil Mask + Image 关 enabled）；
/// 其他          → 指定 sprite + stencil Mask（UI.ResolveSprite 失败路径同 sprite=）。
/// lazy-add + enabled 开关，不 Destroy —— Variant ReSolve 可在三态间任意来回切，
/// 也避免 PlayMode 下 Destroy 延迟销毁导致同帧切换读到待销毁组件。
/// </summary>
public static void ApplyViewportMask(RectTransform viewport, string value, string defaultSpriteName)
{
    var go = viewport.gameObject;
    var img = go.GetComponent<UnityImage>();
    var mask = go.GetComponent<Mask>();
    var rectMask = go.GetComponent<RectMask2D>();

    if (value != null && value.Length == 0)
    {
        if (mask != null) mask.enabled = false;
        if (img != null) img.enabled = false;
        if (rectMask == null) rectMask = go.AddComponent<RectMask2D>();
        rectMask.enabled = true;
        return;
    }

    if (rectMask != null) rectMask.enabled = false;
    if (img == null) img = go.AddComponent<UnityImage>();
    img.enabled = true;
    // alpha=1 关键：alpha<1 触发 UI/Default shader 的 alpha-discard，把 stencil 写飞 (4af322b)。
    img.color = Color.white;
    img.sprite = value == null
        ? GetDefaultSprite(defaultSpriteName)
        : PromptUGUI.Application.UI.ResolveSprite(value);
    AutoSlice(img);
    if (mask == null) mask = go.AddComponent<Mask>();
    mask.enabled = true;
    mask.showMaskGraphic = false;
}
```

注意：文件已 `using UnityEngine.UI;`（`Mask` / `RectMask2D` 直接可用）。`PromptUGUI.Application.UI` 用全名引用，避免 using 增量。

- [ ] **Step 2: ScrollList.OnAttached 改用 helper**

`ScrollList.cs` 加字段 `private RectTransform _viewport;`，`OnAttached` 中原 48-59 行（`var viewport = ...` 到 `_scroll.viewport = viewport;`）替换为：

```csharp
_viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
_viewport.pivot = new Vector2(0f, 1f);
// mask 三态 + 默认 pugui_9slice_mask 圆角，见 ApplyViewportMask 注释 / spec §2.3
ProceduralBuilders.ApplyViewportMask(_viewport, null, ProceduralBuilders.SpriteMaskRoundedRect);
_scroll.viewport = _viewport;
```

下一行 `_content = ProceduralBuilders.AddChild(viewport, "Content");` 的 `viewport` 改 `_viewport`。

- [ ] **Step 3: Dropdown.OnAttached 改用 helper**

`Dropdown.cs` 加字段：

```csharp
private UnityImage _templateBg;
private RectTransform _popupViewport;
```

64-66 行 `var templateBg = ...` 三行改为字段赋值（`_templateBg = template.gameObject.AddComponent<UnityImage>(); _templateBg.color = ProceduralBuilders.DefaultPopupBgColor; ProceduralBuilders.ApplyDefaultSlicedSprite(_templateBg);`）。

74-85 行 Viewport 段改为：

```csharp
_popupViewport = ProceduralBuilders.AddChild(template, "Viewport");
_popupViewport.anchorMin = new Vector2(0f, 0f);
_popupViewport.anchorMax = new Vector2(1f, 1f);
_popupViewport.pivot = new Vector2(0f, 1f);
_popupViewport.offsetMin = Vector2.zero;
_popupViewport.offsetMax = Vector2.zero;
_popupViewport.sizeDelta = new Vector2(-18f, 0f);  // 留 18px 给 Vertical Scrollbar
// mask 三态 + 默认 pugui_9slice_round（border 9-slice，stencil 圆角视觉上不可见），spec §2.3
ProceduralBuilders.ApplyViewportMask(_popupViewport, null, ProceduralBuilders.SpriteRoundedRect);
```

后面 `templateScroll.viewport = viewport;` 改 `_popupViewport`，`var content = ProceduralBuilders.AddChild(viewport, ...)` 改 `_popupViewport`。

- [ ] **Step 4: refresh + 回归现有套件**

Run: `run_tests(..., group_names=["ScrollListTests", "ScrollListContentSizingTests", "DropdownTests", "DropdownContentSizingTests"])`
Expected: 全 PASS——`Viewport_HasStencilMaskAndMaskSpriteWithAlphaOne`（断言 pugui_9slice_mask + Sliced + alpha=1）和 `Viewport_HasNoRectMask2D` 是关键回归，证明 helper 与原构建等价。

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Controls/Internal/ProceduralBuilders.cs Runtime/Controls/ScrollList.cs Runtime/Controls/Dropdown.cs
git commit -m "refactor: viewport mask 构建抽 ApplyViewportMask 共享 helper"
```

---

### Task 3: ScrollList `mask=` 属性

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListTests.cs`

- [ ] **Step 1: 写红测试（加到 ScrollListTests.cs）**

```csharp
private ScrollList OpenList(string attrs = "")
{
    string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Slot'><Frame/></Template>
  <Screen name='S'><ScrollList id='sl' itemTemplate='Slot' " + attrs + @"/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    return UI.Open("S").Get<ScrollList>("sl");
}

private static UnityEngine.RectTransform ViewportOf(ScrollList sl) =>
    (UnityEngine.RectTransform)sl.GameObject.transform.Find("Viewport");

[Test]
public void Mask_empty_swaps_stencil_for_RectMask2D()
{
    var sl = OpenList(@"mask=''");
    var vp = ViewportOf(sl).gameObject;
    var rectMask = vp.GetComponent<UnityEngine.UI.RectMask2D>();
    Assert.IsNotNull(rectMask);
    Assert.IsTrue(rectMask.enabled);
    var mask = vp.GetComponent<UnityEngine.UI.Mask>();
    Assert.IsTrue(mask == null || !mask.enabled, "stencil Mask must be off");
    var img = vp.GetComponent<UnityEngine.UI.Image>();
    Assert.IsTrue(img == null || !img.enabled, "viewport Image must be off (RectMask2D has no showMaskGraphic)");
}

[Test]
public void Mask_custom_sprite_replaces_default_mask_sprite()
{
    var sl = OpenList(@"mask='PromptUGUI/Defaults/pugui#pugui_9slice_round'");
    var vp = ViewportOf(sl).gameObject;
    var mask = vp.GetComponent<UnityEngine.UI.Mask>();
    Assert.IsNotNull(mask);
    Assert.IsTrue(mask.enabled);
    Assert.IsFalse(mask.showMaskGraphic);
    var img = vp.GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_round", img.sprite.name);
    Assert.AreEqual(1f, img.color.a, "alpha=1 critical (4af322b)");
    Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type, "AutoSlice: border 非零 → Sliced");
    Assert.IsNull(vp.GetComponent<UnityEngine.UI.RectMask2D>());
}

[Test]
public void Mask_toggles_between_states_without_leftover_components()
{
    var sl = OpenList();
    var vp = ViewportOf(sl).gameObject;
    sl.Mask = "";                                              // 圆角 → 直角
    sl.Mask = "PromptUGUI/Defaults/pugui#pugui_9slice_round";  // 直角 → 自定义
    sl.Mask = "";                                              // 自定义 → 直角

    Assert.AreEqual(1, vp.GetComponents<UnityEngine.UI.RectMask2D>().Length, "no duplicates");
    Assert.AreEqual(1, vp.GetComponents<UnityEngine.UI.Mask>().Length, "lazy-add keeps single instance");
    Assert.IsTrue(vp.GetComponent<UnityEngine.UI.RectMask2D>().enabled);
    Assert.IsFalse(vp.GetComponent<UnityEngine.UI.Mask>().enabled);
    Assert.IsFalse(vp.GetComponent<UnityEngine.UI.Image>().enabled);
}
```

（默认态回归已有 `Viewport_HasStencilMaskAndMaskSpriteWithAlphaOne` / `Viewport_HasNoRectMask2D` 覆盖，不重写。）

- [ ] **Step 2: refresh + 跑，确认编译失败/红**

Expected: 编译错误 `'ScrollList' does not contain a definition for 'Mask'`（属性不存在）。

- [ ] **Step 3: ScrollList 加 Mask 属性（放 `Sprite` 属性之后）**

```csharp
[UIAttr(IsSprite = true), Preserve]
public string Mask
{
    set => ProceduralBuilders.ApplyViewportMask(
        _viewport, value, ProceduralBuilders.SpriteMaskRoundedRect);
}
```

- [ ] **Step 4: refresh + 跑 ScrollListTests**

Run: `run_tests(..., group_names=["ScrollListTests"])`
Expected: 全 PASS（含原有用例）。

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Controls/ScrollList.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(scrolllist): mask= 三态属性（默认圆角 / 自定义 sprite / ''=RectMask2D 直角）"
```

---

### Task 4: ScrollList `frame=` / `frameColor=`

**Files:**
- Modify: `Runtime/Controls/ScrollList.cs`
- Test: `Tests/EditMode/Controls/ScrollListTests.cs`

- [ ] **Step 1: 写红测试**

```csharp
[Test]
public void Frame_creates_topmost_nonraycast_layer()
{
    var sl = OpenList(@"frame='PromptUGUI/Defaults/pugui#pugui_9slice_round'");
    var root = sl.GameObject.transform;
    var frame = root.Find("Frame");
    Assert.IsNotNull(frame, "frame= should lazily create the Frame layer");
    Assert.AreEqual(root.childCount - 1, frame.GetSiblingIndex(), "frame must be the last sibling (above Viewport & Scrollbar)");
    var img = frame.GetComponent<UnityEngine.UI.Image>();
    Assert.IsFalse(img.raycastTarget);
    Assert.AreEqual("pugui_9slice_round", img.sprite.name);
    Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
    var rt = (UnityEngine.RectTransform)frame;
    Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
    Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
    Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
    Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);
}

[Test]
public void FrameColor_alone_activates_frame_layer()
{
    var sl = OpenList(@"frameColor='#FF0000'");
    var frame = sl.GameObject.transform.Find("Frame");
    Assert.IsNotNull(frame);
    var img = frame.GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual(1f, img.color.r);
    Assert.AreEqual(0f, img.color.g);
}

[Test]
public void No_frame_attr_means_no_frame_node()
{
    var sl = OpenList();
    Assert.IsNull(sl.GameObject.transform.Find("Frame"), "frame layer is lazy");
}
```

- [ ] **Step 2: refresh + 跑，确认编译失败/红**

Expected: 编译错误（`Frame` / `FrameColor` 属性不存在）。

- [ ] **Step 3: 实现**

`ScrollList.cs` 加字段 `private UnityImage _frame;`，加（放 `Mask` 属性之后）：

```csharp
private UnityImage EnsureFrame()
{
    // 边框层：内容/滚动条之上、不被 mask（spec §2.1）。懒创建；层序由 OnAfterApply 钉住。
    _frame ??= ProceduralBuilders.AddImage(RectTransform, "Frame", raycast: false);
    return _frame;
}

[UIAttr(IsSprite = true), Preserve]
public string Frame
{
    set
    {
        var img = EnsureFrame();
        img.sprite = UI.ResolveSprite(value);
        ProceduralBuilders.AutoSlice(img);
    }
}

[UIAttr(IsColor = true), Preserve]
public string FrameColor
{
    set => EnsureFrame().color = UI.Theme.Resolve(value);
}

internal override void OnAfterApply()
{
    base.OnAfterApply();
    // Scrollbar 由 Direction setter 懒建，可能晚于 frame 入树 —— 每轮 apply 后把 frame 钉回最顶。
    if (_frame != null) _frame.transform.SetAsLastSibling();
}
```

- [ ] **Step 4: refresh + 跑 ScrollListTests**

Expected: 全 PASS。`Frame_creates_topmost_nonraycast_layer` 验证 OnAfterApply 的 SetAsLastSibling 生效（XML 属性顺序里 frame 在 itemTemplate 前也不怕）。

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Controls/ScrollList.cs Tests/EditMode/Controls/ScrollListTests.cs
git commit -m "feat(scrolllist): frame=/frameColor= 边框层（mask 外、内容之上）"
```

---

### Task 5: Dropdown `popupSprite=` / `popupColor=` / `popupMask=`

**Files:**
- Modify: `Runtime/Controls/Dropdown.cs`
- Test: `Tests/EditMode/Controls/DropdownTests.cs`

- [ ] **Step 1: 写红测试（加到 DropdownTests.cs，沿用该文件现有 SetUp/Open 模式；若已有等价 helper 直接复用）**

```csharp
private Dropdown OpenDropdown(string attrs = "")
{
    string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Dropdown id='dd' " + attrs + @"/></Screen>
</PromptUGUI>";
    UI.LoadDocument("test", xml);
    return UI.Open("S").Get<Dropdown>("dd");
}

private static UnityEngine.Transform TemplateOf(Dropdown dd) =>
    dd.GameObject.transform.Find("Template");

[Test]
public void PopupSprite_and_color_apply_to_template_bg()
{
    var dd = OpenDropdown(@"popupSprite='PromptUGUI/Defaults/pugui#pugui_9slice_mask' popupColor='#00FF00'");
    var bg = TemplateOf(dd).GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_mask", bg.sprite.name);
    Assert.AreEqual(0f, bg.color.r);
    Assert.AreEqual(1f, bg.color.g);
}

[Test]
public void Popup_defaults_unchanged_without_popup_attrs()
{
    var dd = OpenDropdown();
    var bg = TemplateOf(dd).GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual(UnityEngine.Color.white, bg.color);  // DefaultPopupBgColor
    Assert.AreEqual("pugui_9slice_round", bg.sprite.name);
    var vp = TemplateOf(dd).Find("Viewport").gameObject;
    Assert.IsTrue(vp.GetComponent<UnityEngine.UI.Mask>().enabled);
    Assert.IsNull(vp.GetComponent<UnityEngine.UI.RectMask2D>());
}

[Test]
public void PopupMask_empty_swaps_stencil_for_RectMask2D()
{
    var dd = OpenDropdown(@"popupMask=''");
    var vp = TemplateOf(dd).Find("Viewport").gameObject;
    Assert.IsTrue(vp.GetComponent<UnityEngine.UI.RectMask2D>().enabled);
    var mask = vp.GetComponent<UnityEngine.UI.Mask>();
    Assert.IsTrue(mask == null || !mask.enabled);
    var img = vp.GetComponent<UnityEngine.UI.Image>();
    Assert.IsTrue(img == null || !img.enabled);
}

[Test]
public void PopupMask_custom_sprite_applies_to_viewport()
{
    var dd = OpenDropdown(@"popupMask='PromptUGUI/Defaults/pugui#pugui_9slice_mask'");
    var vp = TemplateOf(dd).Find("Viewport").gameObject;
    var img = vp.GetComponent<UnityEngine.UI.Image>();
    Assert.AreEqual("pugui_9slice_mask", img.sprite.name);
    Assert.IsTrue(vp.GetComponent<UnityEngine.UI.Mask>().enabled);
    Assert.IsFalse(vp.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic);
}
```

- [ ] **Step 2: refresh + 跑，确认红**

Expected: 4 个新用例 FAIL——XML 未知属性当前路径下要么被忽略要么报错；以实际输出为准（若是 ParseException 同样算红）。

- [ ] **Step 3: 实现（放 `Sprite` 属性之后）**

```csharp
[UIAttr(IsSprite = true), Preserve]
public string PopupSprite
{
    set
    {
        _templateBg.sprite = UI.ResolveSprite(value);
        ProceduralBuilders.AutoSlice(_templateBg);
    }
}

[UIAttr(IsColor = true), Preserve]
public string PopupColor
{
    set => _templateBg.color = UI.Theme.Resolve(value);
}

[UIAttr(IsSprite = true), Preserve]
public string PopupMask
{
    set => ProceduralBuilders.ApplyViewportMask(
        _popupViewport, value, ProceduralBuilders.SpriteRoundedRect);
}
```

- [ ] **Step 4: refresh + 跑 DropdownTests**

Expected: 全 PASS。

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Controls/Dropdown.cs Tests/EditMode/Controls/DropdownTests.cs
git commit -m "feat(dropdown): popupSprite/popupColor/popupMask 弹出列表换肤"
```

---

### Task 6: XSD 断言

**Files:**
- Test: `Tests/EditMode/Editor/XsdGeneratorTests.cs`

- [ ] **Step 1: 加测试（反射路径自动收新属性，此测试是防回归锚点）**

```csharp
[Test]
public void ScrollList_and_Dropdown_skin_attrs_in_schema()
{
    var r = new ControlRegistry();
    r.Register<PromptUGUI.Controls.ScrollList>("ScrollList", null);
    r.Register<PromptUGUI.Controls.Dropdown>("Dropdown", null);
    var xsd = XsdGenerator.Generate(r);
    StringAssert.Contains("\"frame\"", xsd);
    StringAssert.Contains("\"frameColor\"", xsd);
    StringAssert.Contains("\"mask\"", xsd);
    StringAssert.Contains("\"popupSprite\"", xsd);
    StringAssert.Contains("\"popupColor\"", xsd);
    StringAssert.Contains("\"popupMask\"", xsd);
}
```

注意：`Register` 调用形式与文件内现有用例一致（`r.Register<T>("Tag", null)`）；断言带引号匹配 `<xs:attribute name="frame"` 的 name 值，避免误匹配元素名。若文件内现有断言风格是裸子串（不带引号），跟随现有风格。

- [ ] **Step 2: refresh + 跑 XsdGeneratorTests（EditorOnly 程序集）**

Run: `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"], group_names=["XsdGeneratorTests"])`
Expected: PASS（反射自动生成，无需改 XsdGenerator；若 FAIL 则说明 `[UIAttr]` 没挂对，回查 Task 3-5）。

- [ ] **Step 3: commit**

```bash
git add Tests/EditMode/Editor/XsdGeneratorTests.cs
git commit -m "test(xsd): ScrollList/Dropdown 换肤属性进 schema 断言"
```

---

### Task 7: SKILL 更新

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`（ScrollList 表 ~L254、Dropdown 表 ~L242）

- [ ] **Step 1: ScrollList 属性表追加 3 行（`tint` 行之后）**

```markdown
| `frame` | sprite key | — | Border layer drawn above content & scrollbar, outside the mask — scrolling content never overlaps it. Lazily created. |
| `frameColor` | hex / CSS / token | — | Tints the frame layer; setting it alone also activates the layer. |
| `mask` | sprite key | built-in rounded | Viewport clip shape. `mask="custom#slice"` = stencil mask with that sprite (auto-sliced); `mask=""` = square `RectMask2D` clipping (cheaper; stops a transparent list from having its corners clipped round). Unset keeps the built-in rounded mask. |
```

- [ ] **Step 2: Dropdown 属性表追加 3 行（`tint` 行之后）**

```markdown
| `popupSprite` | sprite key | — | Skins the popup list background (the closed button keeps using `sprite`/`color`). |
| `popupColor` | hex / CSS / token | — | Tints the popup list background. |
| `popupMask` | sprite key | built-in rounded | Popup viewport clip shape; same three-state semantics as `<ScrollList mask>` (`""` = square RectMask2D). |
```

- [ ] **Step 3: 跑 UIXmlLint 自检内置 modal XML 未受影响（无新规则，纯确认）**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/
```

Expected: exit 0。

- [ ] **Step 4: commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs(skill): ScrollList frame/mask 与 Dropdown popup* 属性"
```

---

### Task 8: 全量回归 + 收尾

- [ ] **Step 1: 全量 EditMode + EditorOnly + PlayMode**

```
run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
```

Expected: 全 PASS（EditMode 基线 1522 + 新增 ~10）。

- [ ] **Step 2: lint 终验**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

Expected: exit 0。

- [ ] **Step 3: 检查 .meta 文件**

`git status` 确认无新建文件缺 .meta（本计划只改既有文件 + 测试文件已存在，理论上无新 .meta；若 Unity 生成了改动要一并提交）。

- [ ] **Step 4: 推分支开 PR（不合 main；视觉 QA 留给用户）**

```bash
git push -u origin feat/list-popup-skin-mask
gh pr create --title "feat: ScrollList frame/mask 与 Dropdown popup 换肤属性" --body "..."
```

PR body 概述三态 mask 语义 + spec 链接 + 测试数，结尾带 🤖 Generated with [Claude Code](https://claude.com/claude-code)。
