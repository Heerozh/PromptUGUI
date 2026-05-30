# `<Tab>` 容器化设计（接受子节点 + Btn 对齐）

**日期**: 2026-05-30
**状态**: 设计阶段（待 review，未进入实施）
**分支**: `feat/tab-container`

**作用域**:
1. `Runtime/Core/Lint/TabRules.cs` —— 移除 `PUI-TAB-CHILDREN`（leaf 守卫）：删 `CheckTab` 方法 + `TabChildrenCode` 常量
2. `Runtime/Controls/Tab.cs` —— label 改懒创建（Btn 对齐）；icon setter 去掉对 label 的硬依赖；新增 `color` 属性
3. `Runtime/Core/Lint/IRWalker.cs`（删 `CheckTab` 调用，L51-53）+ `Runtime/Application/ScreenInstantiator.cs`（删 `CheckTab` 调用，L196）+ `Tests/EditMode/Lint/TabRulesTests.cs`（删 children 断言）
4. `.claude/skills/authoring-promptugui-xml/SKILL.md` —— Tab 接子文档化、`color` 属性、删 `PUI-TAB-CHILDREN` lint 行、点击穿透规则
5. 主 spec `2026-05-07-promptugui-description-language-design.md` §5 Tab 行属性补 `color`
6. EditMode 测试 —— Tab 接子 / 懒 label / `color` / icon+text 顺序无关
7. XSD 重新生成（新增 `color`）

**依赖**: 无（复用引擎已有的"任意控件子节点实例化"路径、`Btn` 的懒 label 模式、`UI.Theme.Resolve` 颜色解析）

**非目标**: 不动 Toggle / Slider / Dropdown / Progress 等其它"leaf"控件的容器化（更广的"任意可点控件 = 容器"愿景记录在 §8，本次不做）；不给 Tab 加 `tint`（spec 既定 Tab tint out-of-scope）。

---

## 1. 背景

`<Tab>` 当前是"叶子"控件：`TabRules.CheckTab` 在 `Children.Count > 0` 时报 `PUI-TAB-CHILDREN`（CLI error / runtime warn）。作者要做"更丰富的 tab"（纯图标、图文混排、两行文字夹图标、透明只点图标）时，只能把额外视觉做成 Tab 的**兄弟节点**（包一层 Template + Frame），靠 raycast 穿透落到 Tab 的 bg 上。

但这与 `<Btn>` 不一致：`Btn` **没有** leaf 守卫，引擎本来就会实例化任意控件的子节点并挂到 `control.ChildHostTransform`（`ScreenInstantiator.InstantiateRecursive` L243-245 对所有控件一视同仁），所以 `<Btn><Icon/><VStack>…</VStack></Btn>` 现在就能跑：子节点挂在 Btn 的 GameObject 下、按各自 anchor/margin 摆位（Frame 式叠放），点击穿透到 Btn 自带的全幅 bg（`targetGraphic` + raycast on）自动激活整个 Btn。

结论：**"叶子 vs 容器"在引擎层不存在，只是一条执行不一致的 lint 约定。** Btn 早就是容器，Tab 被 lint 硬挡。本设计把 Tab 对齐 Btn，让"可点击容器"成为 Tab 的正式语义。

把作者的目标用例对一下改造后形态：

```xml
<!-- 1. 纯图标 Tab -->
<Tab><Icon name="ui:gear"/></Tab>

<!-- 2. 图文混排 / 两行文字夹图标 -->
<Tab>
  <HStack>
    <Icon name="ui:file"/>
    <VStack>
      <Text raycastTarget="false">标题</Text>
      <Text raycastTarget="false">副标题</Text>
    </VStack>
  </HStack>
</Tab>

<!-- 3. 透明 Tab：无底，只有 icon，点了切页 -->
<Tab color="#00000000" bind="panel1"><Icon name="ui:gear"/></Tab>
```

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| TC-D1 | 接受子节点 | 删 `PUI-TAB-CHILDREN`；引擎已实例化子节点，删 lint 即生效 | "叶子"只是 lint 约定；引擎层 Tab/Btn 无差别 |
| TC-D2 | 点击穿透 | 不写代码；沿用 Btn 机制——Tab bg 是 `targetGraphic`+raycast on，子节点 raycast off 即穿透激活 | 与 Btn 心智一致；零新增机制 |
| TC-D3 | label 懒创建 | 改 Btn 模式：`text`/`fontSize`/`font` setter 走 `EnsureLabel()`；不写 `text` 不建 Label GameObject | 自定义 tab 不应背一个看不见的空 label；与 Btn 完全对齐 |
| TC-D4 | icon 懒创建 | 维持现状（已懒）；解除 icon setter 对 label 的硬依赖 | 透明/纯子节点 tab 不应被迫建 icon/label |
| TC-D5 | icon+text 顺序无关 | 两个 setter 任意先后都能正确摆"icon 居左 + label 右移"的便捷布局；label 缺席时 icon 仍居左、无 label 可移 | attr apply 顺序不保证；不能 NRE |
| TC-D6 | 新增 `color` | `[UIAttr(IsColor=true)] public string Color { set => _bg.color = UI.Theme.Resolve(value); }` | Btn 对齐；`color="#00000000"` = 透明可点，无需透明 sprite |
| TC-D7 | 透明无闪烁 | 不额外写代码；`color` 设 alpha-0 后 ColorTint 从该基色相乘，normal/hover/pressed/selected 全保持 alpha-0 | 复用 UnityToggle 的 ColorTint 语义；透明 tab 自动无按下闪烁 |
| TC-D8 | 不做向后兼容 | 项目早期、遗留 `.ui.xml` 极少；行为变化（无 text 不再有空 label）仅在 spec/SKILL 记录为事实，不作为约束 | 用户明确指示 |
| TC-D9 | 不动其它 leaf 控件 | 仅 Tab；Toggle/Slider/Dropdown/Progress 维持现状 | 它们有固定内部结构，容器化收益小、回归面大；范围聚焦 |

---

## 3. 改造细节

### 3.1 退 leaf-lint（`TabRules.cs` + 两处调用点）

`CheckTab` **唯一**内容就是 children 检查（`TabRules.cs:19-26`），`TabChildrenCode` 常量也只服务它。故整体移除，而非留空壳：

- 删 `TabRules.CheckTab` 方法 + `TabChildrenCode` 常量（`TabRules.cs:16`）。
- 删调用点：`IRWalker.cs:51-53`（`else if (node.Tag == "Tab")` 分支）、`ScreenInstantiator.cs:196` 同源块。删后 `IRWalker` 的 dispatch 链顺位接 `TabBar` 分支即可。
- `CheckTabBar`（`PUI-TABBAR-CHILD` / `PUI-TABBAR-DIRECTION`）**不动**；`TabParentCode`（`PUI-TAB-PARENT`，Tab 必须在 TabBar 下，`IRWalker.cs:78-85` 的 parent-relative 检查）**不动**。
- 删旧测试：`Tests/EditMode/Lint/TabRulesTests.cs` 中断言 `TabChildrenCode` 的两个用例（L23-24、L74-76 附近），及其它依赖"Tab 有子 → 报错"的断言。

### 3.2 label 懒创建（`Tab.cs`）

仿 `Btn.EnsureLabel()`。`OnAttached` 不再无条件建 label：

```csharp
public override void OnAttached()
{
    _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
    _bg.color = ProceduralBuilders.DefaultBtnColor;
    ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

    _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
    _toggle.targetGraphic = _bg;
    _toggle.transition = Selectable.Transition.ColorTint;

    // ← 删除原 label 无条件创建块（旧 Tab.cs:37-45）

    var group = FindAncestorToggleGroup();
    if (group == null)
        Debug.LogWarning($"Tab '{Id}' has no <TabBar> ancestor; mutual exclusion disabled.");
    else
        _toggle.group = group;

    _toggle.onValueChanged.AddListener(OnIsOnChanged);
    UI.Locale.Changed += ApplyFont;
}

private TMP_Text EnsureLabel()
{
    if (_label != null) return _label;
    _label = ProceduralBuilders.AddText(RectTransform, "Label");
    _label.alignment = TextAlignmentOptions.Center;
    _label.raycastTarget = false;
    _label.fontSize = 24;
    _label.text = "";
    var lrt = _label.rectTransform;
    lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
    lrt.offsetMin = HasIcon ? new Vector2(32f, 0f) : Vector2.zero;  // 见 §3.4
    lrt.offsetMax = Vector2.zero;
    ApplyFont();
    return _label;
}
```

setter 改走 `EnsureLabel()`：

```csharp
[UIAttr, Preserve] public string Text     { set { if (string.IsNullOrEmpty(value) && _label == null) return; EnsureLabel().text = value ?? ""; } }
[UIAttr, Preserve] public string Font     { set { _fontType = string.IsNullOrEmpty(value) ? "default" : value; if (_label != null) ApplyFont(); } }
[UIAttr("fontSize"), Preserve] public int FontSize { set => EnsureLabel().fontSize = value; }
```

`Text` setter 沿用 Btn 写法：空值且 label 未建则 no-op（不为 `text=""` 凭空建 label）。`ApplyFont` 已 `if (_label == null) return` 守卫（旧 Tab.cs:100），保留。`PeekDefaultText` 已是 `_label != null ? ... : null`，无需改。

### 3.3 `Dispose` / locale 订阅

`UI.Locale.Changed += ApplyFont` 仍在 `OnAttached` 注册、`Dispose` 注销（`ApplyFont` 对 null label no-op），不变。

### 3.4 icon + label 顺序无关（`Tab.cs`）

旧 icon setter 直接 `_label.rectTransform.offsetMin = (32,0)`（Tab.cs:162），label 为 null 时 NRE。改为：

```csharp
private bool HasIcon => _icon != null;

[UIAttr(IsSprite = true), Preserve]
public string Icon
{
    set
    {
        if (_icon == null)
        {
            _icon = ProceduralBuilders.AddImage(RectTransform, "Icon", raycast: false);
            var rt = _icon.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = new Vector2(16f, 0f);
            if (_label != null) _label.rectTransform.offsetMin = new Vector2(32f, 0f);  // label 已在 → 右移
        }
        _icon.sprite = UI.ResolveSprite(value);
    }
}
```

`EnsureLabel()` 里 `offsetMin = HasIcon ? (32,0) : zero`（§3.2）兜住"icon 先于 text"的顺序。两个 setter 任意先后都得到"icon 居左 + label 右移 32"的便捷布局；只有 icon 没 label → icon 居左、无 label 可移；只有 label 没 icon → label 满铺居中。**注**：此便捷布局只服务 `icon`+`text` 属性式简单场景；复杂排版用子节点（§1 用例 2），不依赖这套偏移。

### 3.5 新增 `color`（`Tab.cs`）

```csharp
[UIAttr(IsColor = true), Preserve]
public string Color
{
    set => _bg.color = UI.Theme.Resolve(value);
}
```

`_bg` 在 `OnAttached` 已建（无条件），故 setter 无需 EnsureXxx。`color="#00000000"` → bg alpha-0；UnityToggle 的 ColorTint 从该基色相乘，normal/hover/pressed/selected 全程 alpha-0 → 透明 tab 无按下闪烁（TC-D7）。`selectedSprite` overlay 走 `UnityToggle.graphic` 独立图层，不受 `color` 影响，故"透明常态 + 选中冒高亮 overlay"组合成立。

---

## 4. 点击穿透契约（文档向，无代码）

Tab bg：`targetGraphic` + `raycastTarget=true`，铺满整格、垫底（子节点在其上层，因创建顺序在后）。

- 子节点要"点了激活整个 Tab" → 自身 `raycastTarget="false"`，点击穿透落到 bg。`<Icon>` 已硬编码 off；`<Text>` / `<Image>` 需显式 `raycastTarget="false"`。
- 子节点要"自己独立响应"（如 tab 上的关闭 ✕）→ 用 `<Btn>` 等保持 raycast on + 自己的 `OnClick`，它吃掉该区域点击、不传给 Tab；其余区域继续穿透。uGUI 前序命中即停，天然支持混合。

---

## 5. 程序化层级（改造后）

```
Tab (RectTransform + UnityImage[bg, raycast on, targetGraphic] + UnityToggle[ColorTint])
├── Overlay (按需；selectedSprite；UnityToggle.graphic；raycast off)        ← 不变
├── Icon    (按需；写 icon 属性才建；raycast off；居左 24×24)               ← 仍懒，解依赖
├── Label   (按需；写 text/fontSize/font 才建；raycast off；居中/右移)       ← 改为懒
└── (作者子节点：任意控件，按各自 anchor/margin 叠放在 bg 上)              ← 新增能力
```

无 text / 无 icon / 无子节点 → Tab 只有 bg + Toggle（+ 可选 overlay）。

---

## 6. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| `<Tab>` 含子节点 | 合法（删 lint 后）；子节点实例化挂 Tab 下，按 anchor/margin 叠放 |
| 子节点写 `anchor` / `margin` | 合法且生效——Tab **不是** layout group（不在 `selfIsLayoutGroup` 名单），子节点走普通 anchor/margin 定位（Frame 式）|
| 只写 icon 不写 text | 建 icon，不建 label；icon 居左 |
| 只写 text 不写 icon | 建 label 满铺居中；无 icon |
| icon 与 text 都写（任意 setter 顺序）| icon 居左 + label 右移 32（§3.4 兜两序）|
| 既不写 text/icon 也无子节点 | 只有 bg + Toggle；可点但视觉空（配 `color` / `sprite` / `selectedSprite` 自定义）|
| `color="#00000000"` | bg 透明且可点；ColorTint 全程 alpha-0，无按下闪烁 |
| 子节点 raycast on 且盖住 bg | 该区域点击被子节点吃掉、不激活 Tab（作者需自行 raycast off 或改用 Btn 子做独立点击）|
| Template wrapper 内的 Tab + Tab 自身又有子节点 | 兼容——TabBar 的 `FindTabIn` 递归找到 Tab 实例；Tab 自己的子节点是 Tab 的后代，不影响 TabBar 收集 |

---

## 7. 测试 / 文档

### 7.1 EditMode 测试（`PromptUGUI.Tests.EditMode`）
- Tab 带子节点：解析无 `PUI-TAB-CHILDREN`；子节点实例化为 Tab 后代；点击子节点（raycast off）激活 Tab（断言 `IsOn` / `OnSelected` / bind frame SetActive）。
- 懒 label：不写 text → Tab 下无 Label GameObject；写 text → 有且文本正确。
- 懒 icon 解依赖：只写 icon（无 text）不 NRE；icon 在、label 不在。
- icon+text 顺序无关：两种 attr 顺序都得到 label offsetMin.x==32。
- `color`：`color="#RRGGBBAA"` 写入 `_bg.color`；`#00000000` → alpha 0。
- 删除/调整任何断言"无条件空 label"的旧测试。

### 7.2 SKILL（`authoring-promptugui-xml/SKILL.md`，同 PR）
- Built-in 表 `<Tab>` 行：属性补 `color`；去掉"No nested XML children allowed"，改为"接受子节点（Frame 式叠放）；点击穿透需子节点 raycast off"。
- "Custom Tab layout via Template" 一节：补**直接子节点**写法（现在更简单的首选路径），Template 包装仍保留为"共享样式 / 动态 BindItems"场景。
- Lint 表：删 `PUI-TAB-CHILDREN` 行。
- 三个目标用例（纯图标 / 图文混排 / 透明）各一段最小示例（§1）。

### 7.3 主 spec
`2026-05-07-promptugui-description-language-design.md` §5 控件表 Tab 行属性补 `color`，并把 Tab 从"leaf"措辞改为"可点击容器"。

### 7.4 XSD
`Tools → PromptUGUI → Schema → Generate XSD` 重新生成（新增 `color` attr）。

---

## 8. Out of Scope（记录，后续单独立项）

- **通用"任意可点控件 = 容器"机制** —— 用户更广愿景；本次仅 Tab。将来可统一 Toggle / 自定义控件的"声明可点 + 接子 + 点击穿透"语义。
- **Tab `tint`** —— spec 既定 out-of-scope。
- **Tab 内子节点的 layout-group 化**（让 Tab 直接当 HStack/VStack 排子）—— 维持 Frame 式叠放；要排版用子 `<HStack>` 等。

---

## 9. 风险与回滚

| 风险 | 缓解 |
|---|---|
| 删 `PUI-TAB-CHILDREN` 后残留引用 | 已全仓确认引用点：`TabRules.cs`、`IRWalker.cs:51-53`、`ScreenInstantiator.cs:196`、`TabRulesTests.cs`（2 处）；同 PR 全部清理 |
| label 懒创建破坏依赖"label 永远在"的代码（如 PeekDefaultText / OnSelectionChanged 链路）| `PeekDefaultText` 已 null-safe；其余路径不读 label；测试覆盖无-text Tab |
| icon setter 顺序 NRE | §3.4 两 setter 双向兜底；测试覆盖两种顺序 |
| 透明 tab 仍闪 | ColorTint 从基色相乘，alpha-0 基色全程 alpha-0；如仍闪由 selectedSprite/overlay 引起则属预期（overlay 独立图层）|
| XSD 不自动更新 | 手动重生成；CLAUDE.md 已述 |
