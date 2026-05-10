# M5.1 默认控件对齐设计：把 7 个控件 + 新 InputField 对齐 Unity 6 默认 prefab

**日期**：2026-05-09
**状态**：设计阶段（待 review，未进入实施）
**作用域**：`Runtime/Controls/` 程序化构造的视觉 & 几何，使其与 Unity 6 GameObject → UI → … 菜单生成的默认控件一致；新增 `<InputField>` 内置原语。Sprite 资产不动，靠现有 `pugui.png` atlas + tint 覆盖。
**依赖**：M5 spec [`2026-05-09-m5-common-controls-design.md`](2026-05-09-m5-common-controls-design.md)（控件已就位）；M5 plan [`2026-05-09-m5-common-controls.md`](../plans/2026-05-09-m5-common-controls.md)
**参考产物**：`docs~/default control/*.prefab` ── Unity 6 自带创建菜单产出的 10 个默认 prefab（用户提供）

---

## 1. 背景与目标

`Runtime/Controls/` 当前 7 个控件（Frame / Image / Text / Btn / Toggle / Slider / Dropdown / ScrollList）的程序化构造默认值是 M3-M5 阶段定的"深色中性兜底"——Btn 蓝、控件底深灰、容器近黑。这套兜底初衷是"无 sprite/无 color 时仍可视"，但跟 Unity 自带 `GameObject → UI → …` 菜单创建出的标准控件**视觉风格完全不同**（后者是浅色：白底 sliced + #323232 深字 + 标准 ColorBlock hover/press 反馈）。

用户在 `docs~/default control/` 下放了 10 个未改一下创建出来的 Unity 默认 prefab。需求是：

> "我的一些预定制控件结构应该和 Unity 6 里面的默认值一致……sprite 用我们自己的，其他如颜色，大小什么的都可以按这些默认控件来。"

**目标**：

1. 把现有 7 个控件的颜色 / 字号 / 子节点几何对齐默认 prefab
2. 新增 `<InputField>` 作为第 13 个内置原语（含 Frame/Image/Text/VStack/HStack/Grid/Btn/Icon/Toggle/Slider/Dropdown/ScrollList）
3. Sprite 继续用 `pugui.png` atlas 的 3 张 sprite（`pugui_9slice_round` / `pugui_caret` / `pugui_checkmark`）；不画新图
4. 公开 API 不破坏：`[UIAttr]` 属性集合、R3 事件流、`Get<T>` 路径都保持原状
5. SKILL.md 同步更新（按 CLAUDE.md "公共 C# API surface 变化必须同步 SKILL" 触发条件）

**显式不做**：

- ❌ 加 `<Panel>` 独立 primitive（用 `<Image type="sliced" sprite="..." color="#FFFFFF64" anchor="stretch"/>` 表达即可）
- ❌ 加独立 `<Scrollbar>` primitive（仅作为 ScrollList / Dropdown 内部组件出现）
- ❌ 改 sprite atlas 内容（`pugui.png` 三张 sprite 复用为 sliced bg / glyph）
- ❌ 强制设置外层控件 size（保持 XML 主导，避免破坏 layout group）
- ❌ ScrollList 双轴 scrollbar（M5 spec 锁定单轴；按 `direction=` 加单一 scrollbar）
- ❌ M5 spec 已落的 ColorBlock / R3 / itemTemplate / TrResolver 接入逻辑

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| M5.1-D1 | 主题方向 | 由"深色蓝按钮"切到 Unity 浅色（白底 + #323232 字） | 用户明确"颜色按默认来" |
| M5.1-D2 | sprite 资产 | 复用 `pugui.png` 三张 sprite，全部用 tint 区分明暗 | 用户明确"sprite 用我们自己的"；不画新图 |
| M5.1-D3 | 默认外层 size | 不强制（保持 RectTransform 由 XML 决定） | 默认 size 会破坏 layout group 计算；XML 主导更可控 |
| M5.1-D4 | 子节点几何 | 严格按 prefab 数值复刻（anchor / pivot / sizeDelta / anchoredPosition） | "大小按默认来" 指内部子节点几何，外层仍 XML 决定 |
| M5.1-D5 | ColorBlock | 不显式设置（uGUI Selectable 默认 ColorBlock 已等于 prefab 值） | 默认值 `1 / 0.961 / 0.784 / 0.961 / 0.784α0.502 / fade=0.1` 一致；写一遍是噪音 |
| M5.1-D6 | InputField 几何 | 完全复刻默认 prefab：TextArea 内缩 padding `(-8,-5,-8,-5)`、Placeholder italic α=0.5 | 默认行为对齐；偏离时作者通过 child override |
| M5.1-D7 | InputField sprite | 仍用 `pugui_9slice_round` 作 sliced bg | 与 Btn/Toggle/Dropdown 一致 |
| M5.1-D8 | Slider handle | simple type + `preserveAspect=false`（与默认 Knob 一致），用 `pugui_9slice_round` 作 simple sprite | atlas 里没有专门 knob；圆角矩形当 knob 视觉合理 |
| M5.1-D9 | Toggle 几何破坏面 | 在新 feature branch（如 `feature/m5.1-default-controls-alignment`）开发；测试同步更新；PR review 一次合入 | M5 已 squash-merge 至 main（PR #6），破坏面已扩散；新 branch 让 review 聚焦本 spec 改动 |
| M5.1-D10 | Glyph tint | Checkmark / Caret 在白底上必须深色，DefaultGlyphColor 由白改 #323232 | 假设 atlas 里两张 glyph sprite 是白色 mask（runtime 验证） |
| M5.1-D11 | InputField content type 映射 | string → enum: `standard / autocorrected / integer-number / decimal-number / alphanumeric / name / email / password / pin / custom` | 与 TMP_InputField.ContentType 一一对应；默认 `standard`；hyphen-form 跟 §10.2 / §11.1 SKILL.md 表一致 |
| M5.1-D12 | InputField 事件 | `OnValueChanged` (typing) / `OnEndEdit` (失焦或回车) / `OnSubmit` (回车) ── 全 Observable<string> | 与 TMP_InputField 三个 UnityEvent 对齐；语义一致 |
| M5.1-D13 | SKILL.md 更新 | 增 `<InputField>` 行 + 默认主题简注 | CLAUDE.md 触发条件命中（新增 tag + 行为变化） |
| M5.1-D14 | 测试策略 | EditMode：每个改动控件 ＋几何 / 颜色断言；新增 `InputFieldTests` | 跟 M5 已落地的 ToggleTests / SliderTests / DropdownTests 形状对齐 |
| M5.1-D15 | Viewport masking 组件 | Dropdown / ScrollList viewport **切回 stencil `Mask`** + alpha=1 sliced Image + `showMaskGraphic=false` | 默认 prefab 用 Mask；4af322b 的 alpha-discard bug 仅在 alpha=0.01 触发，alpha=1 安全；Mask 走 sprite 形状能给 popup/scroll 圆角裁剪 |
| M5.1-D16 | Dropdown popup 加 Scrollbar | Template 内增 `Scrollbar Vertical` 子节点；TMP_Dropdown.template/scroll 引用 wired up | 默认 prefab 有；选项数 > 容纳数时滚动 UI 体验对齐 |
| M5.1-D17 | ScrollList 加 Scrollbar | 按 `direction=` 加 `Scrollbar Vertical`（vertical）或 `Scrollbar Horizontal`（horizontal）；scrollbarVisibility=AutoHideAndExpandViewport | 默认 Scroll View 有；保持单轴 (M5 v1) → 单轴 scrollbar |
| M5.1-D18 | InputField TextArea masking | 用 `RectMask2D` + 负 padding `(-8,-5,-8,-5)` | 默认 prefab 即如此（不同于 Dropdown/ScrollList 用的 stencil Mask）；TMP_InputField 与 RectMask2D 配合是 Unity 标准方案 |
| M5.1-D19 | Toggle Background 锚点 | left-middle `(0,0.5)/(0,0.5)` pos `(10,0)` 而非默认 prefab 的 top-left `(0,1)/(0,1)` pos `(10,-10)` | 默认 prefab 假设 Toggle 固定 20 高，top-left + 20x20 = 全 fill；PromptUGUI 里 Toggle 经常被 VStack/Grid 拉高，top-left 锚会让 checkmark 卡顶部 + label 居中 → 视觉成"上下垂直布局"。Left-middle 让 checkmark 始终跟 label 同行 |

---

## 2.5 组件级结构对齐矩阵

每个控件的"组件 × 子节点 × 关键属性"都做精确比对。所有数据均来自 `docs~/default control/*.prefab`。**sliced sprite 一律映射为 `pugui_9slice_round`**（atlas 仅此一张 9-slice），**glyph sprite 映射为 `pugui_caret` / `pugui_checkmark`**。`UISprite` / `Background` / `UIMask` / `Knob` 等 Unity 内建 sprite 名仅作为"原型形状参考"——我们用 atlas 同形态对齐。

### Btn ↔ Button.prefab

| 节点 | 组件 | 默认 prefab 关键属性 | 我们的实现 | 偏离 |
|---|---|---|---|---|
| `Button` (root) | RectTransform / CanvasRenderer / Image / Button | sliced UISprite white, ColorBlock=default | RT + Image + Button | ✓ 用 `pugui_9slice_round` 替代 UISprite；ColorBlock=Selectable 默认（一致）|
| └ `Label` | RT / CR / TMP_Text | text="Button", **fontSize=24**, color=#323232, align=Center+Middle, wrap=Normal, raycastTarget=true | 同结构，但 fontSize 由 AddText 默认（14） | **改 fontSize=24，color=#323232（由 DefaultLabelColor 提供）** |

> 默认 prefab 子节点名是 "Text (TMP)"，我们用 "Label" 的可读性更好；M5.1-D9 决定**保留我们的命名**。命名跟"结构一致"无关——CanvasRenderer 等组件类型才算结构。

### Toggle ↔ Toggle.prefab

| 节点 | 组件 | 默认 prefab 关键属性 | 我们的实现（旧） | 偏离 / 修正 |
|---|---|---|---|---|
| `Toggle` (root) | RT / Toggle | **无 Image**；ColorBlock=default | RT + **Image** + Toggle | **去掉 root Image**（移到子 Background）|
| └ `Background` | RT / CR / Image | prefab: anchor=(0,1)/(0,1), pos=(10,-10), size=20x20；**我们用 (0,0.5)/(0,0.5) pos=(10,0)** sliced UISprite white | _不存在（Image 在 root）_ | **新增子节点；锚 top-left → left-middle，让 Toggle 被 VStack/Grid 拉高时 checkmark 仍跟 label 视觉同行 (D19)** |
| │  └ `Checkmark` | RT / CR / Image | anchor=center, size=20x20, simple sprite=Checkmark white preserveAspect=false | 父级是 root，size 全 stretch | **改父级为 Background；改 size=20x20；preserveAspect=false** |
| └ `Label` | RT / CR / **legacy UI.Text** | prefab: anchor=stretch offsetMin≈(23,1) offsetMax≈(-5,-2) font=Arial **fontSize=14** color=#323232 align=Left+Middle **raycastTarget=true**；**我们用 offsetMin=(23,0) offsetMax=(-5,0)** | TMP_Text，align=Center, raycastTarget=false | **TMP_Text + offsetMin=(23,0)/offsetMax=(-5,0) (Y 全 stretch 配合 Background 垂直居中)；align=Left+Middle, raycastTarget=true** |

### Slider ↔ Slider.prefab

| 节点 | 组件 | 默认 prefab 关键属性 | 我们的实现（旧） | 偏离 / 修正 |
|---|---|---|---|---|
| `Slider` (root) | RT / Slider | **无 Image** | RT + **Image** + Slider | **去掉 root Image** |
| └ `Background` | RT / CR / Image | anchor=(0,0.25)/(1,0.75), sizeDelta=0, sliced sprite=Background white | _Image 在 root，全 stretch_ | **新增子节点**，Y 内缩到 25%-75% |
| └ `Fill Area` | RT only | anchor=(0,0.25)/(1,0.75), pos=(−5,0), sizeDelta=(−20,0) | 名 `FillArea`，全 stretch | **改名 `Fill Area`；Y 内缩；X 两侧各 −10 抵消 handle 宽度** |
| │  └ `Fill` | RT / CR / Image | anchor=(0,0)/(0,0), sizeDelta=(10,0), sliced UISprite white | sliced 同 sprite，全 stretch | **改 anchor=(0,0)/(0,0)，sizeDelta=(10,0)** |
| └ `Handle Slide Area` | RT only | anchor=stretch, sizeDelta=(−20,0) | 名 `HandleArea`，全 stretch | **改名 `Handle Slide Area`；sizeDelta=(−20,0)** |
| │  └ `Handle` | RT / CR / Image | anchor=(0,0)/(0,0), sizeDelta=(20,0), **simple** sprite=Knob white preserveAspect=false | sliced sprite，全 stretch | **改 simple type、preserveAspect=false、anchor=(0,0)/(0,0)、sizeDelta=(20,0)**；用 `pugui_9slice_round` 当 simple knob |

### Dropdown ↔ Dropdown.prefab

| 节点 | 组件 | 默认 prefab 关键属性 | 我们的实现（51eecde 之后） | 偏离 / 修正 |
|---|---|---|---|---|
| `Dropdown` (root) | RT / CR / Image / TMP_Dropdown | sliced UISprite white | ✓ | ✓ |
| └ `Label` | RT / CR / TMP_Text | offsetMin=(10,6) offsetMax=(−25,−7), fontSize=14, color=#323232, align=Left+Middle, wrap=Normal | ✓ 几何已对齐；fontSize 由 AddText 默认 | fontSize/color 由调色板默认提供（一致）|
| └ `Arrow` | RT / CR / Image | anchor=(1,0.5)/(1,0.5), pos=(−15,0), **size=20x20**, simple DropdownArrow white preserveAspect=false | pos=(−12,0), **size=14x10** | **改 size=20x20、pos=(−15,0)、preserveAspect=false** |
| └ `Template` | RT / CR / Image / ScrollRect | anchor=(0,0)/(1,0), pivot=(0.5,1), sizeDelta=(0,150), pos=(0,2), **inactive**, sliced UISprite white | ✓ | ✓ |
| │  └ `Viewport` | RT / CR / **Image / Mask** | anchor=stretch, sizeDelta=**(−18,0)**, pivot=(0,1), sliced sprite=UIMask **alpha=1** white, **`Mask.showMaskGraphic=0`** | RT / **RectMask2D** 无 Image，sizeDelta=0 | **切回 Mask + sliced Image alpha=1 + showMaskGraphic=false；sizeDelta.x=−18 给 Scrollbar 留位** |
| │  │  └ `Content` | RT only | anchor=(0,1)/(1,1), pivot=(0.5,1), sizeDelta=(0,28) | ✓ | ✓ |
| │  │  │  └ `Item` | RT / Toggle | anchor=(0,0.5)/(1,0.5), sizeDelta=**(0,20)**, ColorBlock=default | sizeDelta=(0,**48**) | **改 height=20**（51eecde 写的是 48，对齐默认 20；24px 高的"item icon visual"靠 fontSize 撑） |
| │  │  │  │  ├ `Item Background` | RT / CR / Image | anchor=stretch, sizeDelta=0, **simple, NO sprite, color=#F5F5F5** | sliced sprite white | **改 simple type；不应用 sprite；color=#F5F5F5（即 highlighted-tinted 背景）** |
| │  │  │  │  ├ `Item Checkmark` | RT / CR / Image | anchor=(0,0.5)/(0,0.5), pos=(10,0), **size=20x20**, simple Checkmark white | pos=(12,0), size=14x14 | **改 size=20x20、pos=(10,0)** |
| │  │  │  │  └ `Item Label` | RT / CR / TMP_Text | offsetMin=(20,1.5) offsetMax=(−10,−1.5), fontSize=14, color=#323232, align=Left+Middle | offsetMin=(24,0) offsetMax=(−10,0), fontSize=14 | **改 offset 到 (20,1.5)/(−10,−1.5)** |
| │  └ `Scrollbar` | RT / CR / Image / Scrollbar | anchor=(1,0)/(1,1), pivot=(1,1), sizeDelta=(20,0), sliced sprite=Background white, direction=BottomToTop, value=0, size=0.2 | _不存在_ | **新增整个子树**（含 Sliding Area + Handle）|
| │  │  └ `Sliding Area` | RT only | anchor=stretch, sizeDelta=(−20,−20) | _不存在_ | **新增** |
| │  │  │  └ `Handle` | RT / CR / Image | anchor=(0,0)/(1,0.2), sizeDelta=(20,20), sliced UISprite white | _不存在_ | **新增** |
| └ TMP_Dropdown 引用 | — | template/captionText/itemText/**verticalScrollbar** wired | template/captionText/itemText 已 wired，**无 verticalScrollbar** | **wire `_tmp.verticalScrollbar` 到新增的 Scrollbar；ScrollRect.verticalScrollbar 同 wire** |

### ScrollList ↔ Scroll View.prefab

| 节点 | 组件 | 默认 prefab 关键属性 | 我们的实现（旧） | 偏离 / 修正 |
|---|---|---|---|---|
| `Scroll View` (root) | RT / CR / Image / ScrollRect | sliced UISprite **white α=0.392**, ScrollRect (movementType=Elastic, scrollbarVisibility=2 AutoHideAndExpandViewport, spacing=−3) | RT / Image / ScrollRect | ✓ bg α=0.392 由调色板提供；**显式设 movementType=Elastic、verticalScrollbarVisibility=AutoHideAndExpandViewport、verticalScrollbarSpacing=−3**（horizontal 同理） |
| └ `Viewport` | RT / CR / **Image / Mask** | anchor=stretch（被 ScrollRect 驱动）, sliced sprite=UIMask **alpha=1** white, **`Mask.showMaskGraphic=0`** | RT / **RectMask2D** 无 Image | **切回 Mask + sliced Image alpha=1 + showMaskGraphic=false** |
| │  └ `Content` | RT only | anchor + pivot 由 direction 决定（已正确） | ✓ | ✓ |
| └ `Scrollbar Vertical` | RT / CR / Image / Scrollbar | anchor=(1,0)/(1,0)，pivot=(1,1), sizeDelta=(20,0), sliced Background white, direction=BottomToTop | _不存在_ | **direction="vertical" 时新增**（含 Sliding Area + Handle）|
| └ `Scrollbar Horizontal` | RT / CR / Image / Scrollbar | anchor=(0,0)/(0,0), pivot=(0,0), sizeDelta=(0,20), sliced Background white, direction=LeftToRight | _不存在_ | **direction="horizontal" 时新增** |
| └ ScrollRect 引用 | — | viewport/content/horizontalScrollbar/verticalScrollbar wired | viewport/content wired，无 scrollbar | **按 direction wire 单一 scrollbar** |

> 默认 Scroll View prefab 同时含 H+V scrollbar。M5 spec 锁定 ScrollList 单轴模式（M5-D... "v1 仅 vertical / horizontal stack"），所以**只加跟 `direction` 一致的那条 Scrollbar**。两条全加是越界。

### InputField (新增) ↔ InputField (TMP).prefab

| 节点 | 组件 | 默认 prefab 关键属性 |
|---|---|---|
| `InputField` (root) | RT / CR / Image / TMP_InputField | sliced sprite=InputFieldBackground white, ColorBlock=default, GlobalPointSize=14, GlobalFontAsset=LiberationSans, CaretColor=#323232, SelectionColor=`(0.659, 0.808, 1.0, 0.753)` |
| └ `Text Area` | RT / **RectMask2D** | anchor=stretch, sizeDelta=(−20,−13), pos=(0,−0.5), `Padding=(−8,−5,−8,−5)` |
| │  ├ `Placeholder` | RT / CR / TMP_Text | anchor=stretch, sizeDelta=0, text="Enter text...", **italic (fontStyle=2)**, color=#32323280, align=Left+Top, wrap=Disabled, **LayoutElement.IgnoreLayout=true** |
| │  └ `Text` | RT / CR / TMP_Text | anchor=stretch, sizeDelta=0, text="​" (zero-width space), color=#323232, align=Left+Top, wrap=PreserveWhitespaceNoWrap (=3) |

### 不动的控件

- `Frame` — PromptUGUI 独有概念（无视觉容器），无对应默认 prefab
- `Image` — 默认 100x100、无 sprite、color=white，跟 prefab 一致
- `Text` — TMP 出厂默认 fontSize=36 + color=white，跟 prefab "Text (TMP)" 一致
- `Icon` — PromptUGUI 独有（IconSet 系统），无对应默认 prefab
- `VStack` / `HStack` / `Grid` — Unity 默认菜单不直接产出对应 prefab；属于 PromptUGUI 布局原语

---

## 3. 调色板迁移（`ProceduralBuilders.cs`）

### 3.1 常量改动

```diff
-public static readonly Color DefaultBtnColor       = new(0.231f, 0.510f, 0.965f, 1f);  // #3B82F6
-public static readonly Color DefaultControlBgColor = new(0.267f, 0.267f, 0.267f, 1f);  // #444444
-public static readonly Color DefaultTrackColor     = new(0.200f, 0.200f, 0.200f, 1f);  // #333333
-public static readonly Color DefaultFillColor      = new(0.231f, 0.510f, 0.965f, 1f);  // #3B82F6
-public static readonly Color DefaultHandleColor    = new(1.000f, 1.000f, 1.000f, 1f);
-public static readonly Color DefaultPopupBgColor   = new(0.227f, 0.227f, 0.227f, 1f);  // #3A3A3A
-public static readonly Color DefaultContainerColor = new(0.165f, 0.165f, 0.165f, 1f);  // #2A2A2A
-public static readonly Color DefaultGlyphColor     = new(1.000f, 1.000f, 1.000f, 1f);
+// 默认配色对齐 Unity 6 标准控件（菜单 GameObject → UI → … 创建出来的 prefab）
+// 全部白底 sliced + #323232 深字；sprite 由 atlas tint 表现明暗。
+public static readonly Color DefaultBtnColor         = Color.white;                    // Btn bg
+public static readonly Color DefaultControlBgColor   = Color.white;                    // Toggle/Dropdown/InputField bg
+public static readonly Color DefaultTrackColor       = Color.white;                    // Slider 轨道
+public static readonly Color DefaultFillColor        = Color.white;                    // Slider Fill
+public static readonly Color DefaultHandleColor      = Color.white;                    // Slider Handle
+public static readonly Color DefaultPopupBgColor     = Color.white;                    // Dropdown popup
+public static readonly Color DefaultContainerColor   = new(1f, 1f, 1f, 0.392f);        // ScrollList / Panel 半透明
+public static readonly Color DefaultGlyphColor       = new(0.196f, 0.196f, 0.196f, 1f); // #323232 ── Checkmark/Arrow tint
+public static readonly Color DefaultLabelColor       = new(0.196f, 0.196f, 0.196f, 1f); // Btn/Toggle/Dropdown 文字
+public static readonly Color DefaultPlaceholderColor = new(0.196f, 0.196f, 0.196f, 0.5f); // InputField placeholder
```

### 3.2 `AddText` 默认色

```diff
 public static TMP_Text AddText(RectTransform parent, string name)
 {
     var rt = AddChild(parent, name);
     var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
     tmp.alignment = TextAlignmentOptions.Center;
     tmp.raycastTarget = false;
+    tmp.color = DefaultLabelColor;
+    tmp.fontSize = 14;  // Unity 默认 button/toggle/dropdown 内嵌 label 的 fontSize
     return tmp;
 }
```

> **`<Text>` 顶层原语不受影响** ── 它走 `Text.cs.OnAttached()`，自己 GetComponent/AddComponent，没有经过 `AddText`。这跟默认 prefab 的"Text (TMP)" fontSize=36 一致（TMP 出厂默认 36）。

### 3.3 sprite 应用 simple 时不强制 preserveAspect

```diff
-public static void ApplyDefaultSimpleSprite(UnityImage img, string spriteName)
+public static void ApplyDefaultSimpleSprite(UnityImage img, string spriteName, bool preserveAspect = false)
 {
     if (img == null || img.sprite != null) return;
     var s = GetDefaultSprite(spriteName);
     if (s == null) return;
     img.sprite = s;
     img.type = UnityImage.Type.Simple;
-    img.preserveAspect = true;
+    img.preserveAspect = preserveAspect;
 }
```

> 默认 prefab 里所有 simple sprite 的 `preserveAspect=false`；当前实现强制 true 是为了避免 caret/checkmark 在非正方 RT 里被拉扁，但这违反了 prefab 默认。两个调用点（Toggle Checkmark 和 Dropdown Arrow）的 RectTransform 现在都已经是正方 (20x20)，preserveAspect 无关紧要；显式 `false` 跟默认对齐。

---

## 4. Btn 调整

`Runtime/Controls/Btn.cs`：

```diff
 _bg.color = ProceduralBuilders.DefaultBtnColor;  // 现在是 white
 PromptUGUI.Controls.Internal.ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
 _btn = GameObject.GetComponent<Button>() ?? GameObject.AddComponent<Button>();
 _btn.targetGraphic = _bg;
+// uGUI Button 的 ColorBlock.defaultColorBlock 已经等于 Unity 默认 prefab 值
+// (1 / 0.961 / 0.784 / 0.961 / 0.784α0.502, fade=0.1)，无需显式设置。
```

`EnsureLabel()` 默认 fontSize 由 `AddText` 提供（14），但**默认 prefab Btn 的 label fontSize=24**。所以 `EnsureLabel` 在 `AddText` 之后**显式覆盖 fontSize=24**：

```diff
 _autoLabel = go.AddComponent<TextMeshProUGUI>();
 _autoLabel.alignment = TextAlignmentOptions.Center;
 _autoLabel.raycastTarget = false;
+_autoLabel.fontSize = 24;  // 默认 prefab Button 的 label fontSize；其他控件 label 走 AddText 默认 14
+// color 由 AddText 默认提供（DefaultLabelColor = #323232）
 ApplyFont();
```

> **设计细节**：`AddText` 留 14 作为兜底（Toggle / Dropdown caption / item label / InputField text 都是 14），Btn 因为是 "primary action" 单独覆盖到 24。这样每个控件 label 字号都精确匹配各自的 default prefab。

---

## 5. Toggle 几何重构（破坏面）

### 5.1 默认 prefab 拆解

```
Toggle (160x20, anchor center)
├── Background (anchor=(0,1), pivot=(0.5,0.5), sizeDelta=20x20, anchoredPosition=(10,-10))
│   └── Checkmark (anchor=center, sizeDelta=20x20, simple sprite, color=white × dark sprite = dark)
└── Label (anchor=stretch, offsetMin=(9,-0.5), offsetMax=(-28,...), color=#323232, fontSize=14)
       ↑ 注意 label 的 offsetMin.x = 9 而非 28 ── 因为 Background 是 20x20 在 (10,-10)，
         右边到 x=20，label 从 x=9 开始（看起来 prefab 里有故意 1px overlap）
```

> 实际默认 prefab：`Label.offsetMin = (9, -0.5)`、`Label.offsetMax = (-28, 0)`、`Label.sizeDelta = (-28, -3)` ── 复刻这个。

### 5.2 `Toggle.OnAttached()` 改造

```diff
 public override void OnAttached()
 {
-    _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
-    _bg.color = ProceduralBuilders.DefaultControlBgColor;
-    ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
-    _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
-    _toggle.targetGraphic = _bg;
+    _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
 
-    _checkmark = ProceduralBuilders.AddImage(RectTransform, "Checkmark", raycast: false);
+    // Background：左上角 20x20
+    var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
+    bgRt.anchorMin = new Vector2(0f, 1f);
+    bgRt.anchorMax = new Vector2(0f, 1f);
+    bgRt.pivot = new Vector2(0.5f, 0.5f);
+    bgRt.sizeDelta = new Vector2(20f, 20f);
+    bgRt.anchoredPosition = new Vector2(10f, -10f);
+    _bg = bgRt.gameObject.AddComponent<UnityImage>();
+    _bg.color = ProceduralBuilders.DefaultControlBgColor;
+    ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
+    _toggle.targetGraphic = _bg;
+
+    // Checkmark：放在 bg 内部，居中 20x20
+    _checkmark = ProceduralBuilders.AddImage(bgRt, "Checkmark", raycast: false);
+    _checkmark.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
+    _checkmark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
+    _checkmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
+    _checkmark.rectTransform.anchoredPosition = Vector2.zero;
     _checkmark.color = ProceduralBuilders.DefaultGlyphColor;
     ProceduralBuilders.ApplyDefaultSimpleSprite(_checkmark, ProceduralBuilders.SpriteCheckmark);
     _toggle.graphic = _checkmark;
 
-    _label = ProceduralBuilders.AddText(RectTransform, "Label");
+    // Label：右侧水平 stretch
+    _label = ProceduralBuilders.AddText(RectTransform, "Label");
+    _label.alignment = TextAlignmentOptions.Left;
+    var labelRt = _label.rectTransform;
+    labelRt.anchorMin = new Vector2(0f, 0f);
+    labelRt.anchorMax = new Vector2(1f, 1f);
+    labelRt.pivot = new Vector2(0.5f, 0.5f);
+    labelRt.offsetMin = new Vector2(9f, 0f);
+    labelRt.offsetMax = new Vector2(-28f, 0f);
     ApplyFont();
     ...
 }
```

### 5.3 破坏面

- 旧 `<Toggle>` 实例：bg 占满 → checkmark 占满。新：bg 20x20 在左上角。
- 已有 ToggleTests 几何断言会需要更新（数量 < 5 处，已估算入测试改动）
- **M5 已经 squash-merge 进了 main**（PR #6 / 796b05c），所以本次 Toggle 几何重构会改 main 上的行为；考虑用新 feature branch（如 `feature/m5.1-default-controls-alignment`）操作，整体合并以减小 review 单元

### 5.4 Raycast 兼容点

把 Image 从 root 挪到子 Background 之后，root GO 没有 raycast target；但 Selectable 的点击需要在 RectTransform 内任意 graphic 命中才生效。Unity 默认 Toggle 之所以全区可点是因为 `Label`（legacy UI.Text）`raycastTarget=true`。

实施约束：`Toggle.OnAttached()` 在 `AddText(...)` 之后**显式设** `_label.raycastTarget = true`。（`AddText` 默认 false，不动；Btn 不受影响因为 root 还是 Image。）

---

## 6. Slider 几何重构

### 6.1 默认 prefab 拆解

```
Slider (160x20, anchor center)
├── Background (anchor=(0,0.25)/(1,0.75), sizeDelta=0,0, sliced sprite white)
├── Fill Area (anchor=(0,0.25)/(1,0.75), anchoredPos=(-5,0), sizeDelta=(-20,0))
│   └── Fill (anchor=(0,0)/(0,1)*, sizeDelta=(10,0), sliced sprite white)
└── Handle Slide Area (anchor=stretch, sizeDelta=(-20,0))
    └── Handle (anchor=(0,0)/(0,0), sizeDelta=(20,0), simple sprite, no preserveAspect)
```

> **\* `Fill.anchorMax = (0,1)` 注意点**：默认 prefab YAML 写的是 `(0,0)`，但 Unity Slider.UpdateVisuals() 在 LeftToRight 方向会强制把 `anchorMax.y` 覆写为 1。我们在程序化构造里直接预设 `(0,1)`，避免首帧前一瞬间的视觉位差；测试断言对齐 runtime 状态。

### 6.2 `Slider.OnAttached()` 改造

```diff
 public override void OnAttached()
 {
-    _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
+    // Background：竖向内缩到中间 50%（anchor 0.25-0.75）
+    var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
+    bgRt.anchorMin = new Vector2(0f, 0.25f);
+    bgRt.anchorMax = new Vector2(1f, 0.75f);
+    bgRt.offsetMin = Vector2.zero;
+    bgRt.offsetMax = Vector2.zero;
+    _bg = bgRt.gameObject.AddComponent<UnityImage>();
     _bg.color = ProceduralBuilders.DefaultTrackColor;
     ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
 
-    var fillArea = ProceduralBuilders.AddChild(RectTransform, "FillArea");
+    // Fill Area：跟 Background 同样 Y 内缩，X 两侧各留 10px
+    var fillArea = ProceduralBuilders.AddChild(RectTransform, "Fill Area");
+    fillArea.anchorMin = new Vector2(0f, 0.25f);
+    fillArea.anchorMax = new Vector2(1f, 0.75f);
+    fillArea.anchoredPosition = new Vector2(-5f, 0f);
+    fillArea.sizeDelta = new Vector2(-20f, 0f);
     _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
+    var fillRt = _fill.rectTransform;
+    fillRt.anchorMin = Vector2.zero;
+    fillRt.anchorMax = Vector2.zero;
+    fillRt.sizeDelta = new Vector2(10f, 0f);
     _fill.color = ProceduralBuilders.DefaultFillColor;
     ProceduralBuilders.ApplyDefaultSlicedSprite(_fill);
 
-    var handleArea = ProceduralBuilders.AddChild(RectTransform, "HandleArea");
+    // Handle Slide Area：水平 stretch，左右各留 10px
+    var handleArea = ProceduralBuilders.AddChild(RectTransform, "Handle Slide Area");
+    handleArea.anchorMin = Vector2.zero;
+    handleArea.anchorMax = Vector2.one;
+    handleArea.sizeDelta = new Vector2(-20f, 0f);
+    handleArea.anchoredPosition = Vector2.zero;
     _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);
+    var handleRt = _handle.rectTransform;
+    handleRt.anchorMin = Vector2.zero;
+    handleRt.anchorMax = Vector2.zero;
+    handleRt.sizeDelta = new Vector2(20f, 0f);
     _handle.color = ProceduralBuilders.DefaultHandleColor;
-    ProceduralBuilders.ApplyDefaultSlicedSprite(_handle);
+    // Handle 用 simple type，跟默认 Knob 一致；preserveAspect=false
+    ProceduralBuilders.ApplyDefaultSimpleSprite(_handle, ProceduralBuilders.SpriteRoundedRect);

     _slider = GameObject.GetComponent<UnitySlider>() ?? GameObject.AddComponent<UnitySlider>();
     _slider.targetGraphic = _handle;
     _slider.fillRect = _fill.rectTransform;
     _slider.handleRect = _handle.rectTransform;
     _slider.direction = UnitySlider.Direction.LeftToRight;
     ...
 }
```

> 决策 M5.1-D8：handle 没有 dedicated knob sprite，复用 `pugui_9slice_round` 设为 simple type ── 是个圆角矩形，做 knob 视觉合理。

### 6.3 破坏面

- `Slider.Sprite` UIAttr setter 之前给 _bg 改 sprite；保持原状（仍只换 bg）。Handle 不通过 attr 换 sprite，作者要换得 fork。
- 测试更新：FillArea 名字由 `FillArea` → `Fill Area`（两单词，跟 prefab 一致）；HandleArea → `Handle Slide Area`。如果有 GetByName 的测试要同步。

---

## 7. Dropdown 调整

51eecde 把 popup 几何拉到 TMP_Dropdown 标准外形，但**组件级**还有 4 处偏离默认：(1) Arrow size/pos；(2) Viewport masking 用 RectMask2D；(3) Item 高度+几何；(4) 缺 Scrollbar。本节按 §2.5 矩阵全数对齐。

### 7.1 Arrow 几何

```diff
 var arrow = ProceduralBuilders.AddImage(RectTransform, "Arrow", raycast: false);
 arrow.color = ProceduralBuilders.DefaultGlyphColor;
 ProceduralBuilders.ApplyDefaultSimpleSprite(arrow, ProceduralBuilders.SpriteCaret);
 arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
 arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
 arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
-arrow.rectTransform.sizeDelta = new Vector2(14f, 10f);
-arrow.rectTransform.anchoredPosition = new Vector2(-12f, 0f);
+arrow.rectTransform.sizeDelta = new Vector2(20f, 20f);
+arrow.rectTransform.anchoredPosition = new Vector2(-15f, 0f);
```

### 7.2 Viewport 切回 stencil Mask

```diff
-// Viewport (full-fills the template; clip items via RectMask2D — scissor-rect, no stencil/graphic).
+// Viewport: stencil Mask + sliced Image (alpha=1, showMaskGraphic=false) ── 跟默认 prefab 一致。
+// 注意：不能用 alpha=0 的 graphic（4af322b 之前的 alpha=0.01 触发 UI/Default shader alpha-discard 把 stencil 写飞）；
+// alpha=1 + showMaskGraphic=false 视觉等价于"看不见的 mask"，stencil 正常。
 var viewport = ProceduralBuilders.AddChild(template, "Viewport");
 viewport.anchorMin = new Vector2(0f, 0f);
 viewport.anchorMax = new Vector2(1f, 1f);
 viewport.pivot = new Vector2(0f, 1f);
 viewport.offsetMin = Vector2.zero;
-viewport.offsetMax = Vector2.zero;
-viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
+viewport.offsetMax = Vector2.zero;
+viewport.sizeDelta = new Vector2(-18f, 0f);  // 留 18px 给 Vertical Scrollbar
+var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
+viewportImg.color = Color.white;  // alpha=1 关键
+ProceduralBuilders.ApplyDefaultSlicedSprite(viewportImg);
+var viewportMask = viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
+viewportMask.showMaskGraphic = false;
```

### 7.3 Item 几何对齐

```diff
-// Item template (cloned per option; fixed height + horizontal stretch).
-const float itemHeight = 48f;
+// Item: 默认 prefab height=20，sliced bg 用 simple+#F5F5F5 表达 highlighted 状态
+const float itemHeight = 20f;
 var item = ProceduralBuilders.AddChild(content, "Item");
 item.anchorMin = new Vector2(0f, 0.5f);
 item.anchorMax = new Vector2(1f, 0.5f);
 item.pivot = new Vector2(0.5f, 0.5f);
 item.sizeDelta = new Vector2(0f, itemHeight);
 
-var itemBg = item.gameObject.AddComponent<UnityImage>();
-itemBg.color = ProceduralBuilders.DefaultControlBgColor;
-ProceduralBuilders.ApplyDefaultSlicedSprite(itemBg);
+// Item Background：default prefab 用 simple+无 sprite+#F5F5F5 ── 一块平的高亮色带。
+var itemBgRt = ProceduralBuilders.AddChild(item, "Item Background");
+var itemBg = itemBgRt.gameObject.AddComponent<UnityImage>();
+itemBg.type = UnityImage.Type.Simple;
+itemBg.sprite = null;  // 不应用 sprite，跟 default 一致
+itemBg.color = new Color(0.961f, 0.961f, 0.961f, 1f);  // #F5F5F5
 var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
 itemToggle.targetGraphic = itemBg;
 
-// Item checkmark anchored on the left side of the item.
 var itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
 itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
 ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
 itemCheckmark.rectTransform.anchorMin = new Vector2(0f, 0.5f);
 itemCheckmark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
 itemCheckmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
-itemCheckmark.rectTransform.sizeDelta = new Vector2(14f, 14f);
-itemCheckmark.rectTransform.anchoredPosition = new Vector2(12f, 0f);
+itemCheckmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
+itemCheckmark.rectTransform.anchoredPosition = new Vector2(10f, 0f);
 itemToggle.graphic = itemCheckmark;
 
-// Item label fills the rest of the item.
 var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
 itemLabel.alignment = TextAlignmentOptions.Left;
 itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
 itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
-itemLabel.rectTransform.offsetMin = new Vector2(24f, 0f);
-itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);
+itemLabel.rectTransform.offsetMin = new Vector2(20f, 1.5f);
+itemLabel.rectTransform.offsetMax = new Vector2(-10f, -1.5f);
```

### 7.4 加 Scrollbar Vertical 子树

```csharp
// 紧跟 Viewport 之后、TMP_Dropdown wiring 之前。
var scrollbarRt = ProceduralBuilders.AddChild(template, "Scrollbar");
scrollbarRt.anchorMin = new Vector2(1f, 0f);
scrollbarRt.anchorMax = new Vector2(1f, 1f);
scrollbarRt.pivot = new Vector2(1f, 1f);
scrollbarRt.sizeDelta = new Vector2(20f, 0f);
scrollbarRt.anchoredPosition = Vector2.zero;
var scrollbarBg = scrollbarRt.gameObject.AddComponent<UnityImage>();
scrollbarBg.color = ProceduralBuilders.DefaultControlBgColor; // white
ProceduralBuilders.ApplyDefaultSlicedSprite(scrollbarBg);
var scrollbar = scrollbarRt.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
scrollbar.value = 0f;
scrollbar.size = 0.2f;

var slidingArea = ProceduralBuilders.AddChild(scrollbarRt, "Sliding Area");
slidingArea.sizeDelta = new Vector2(-20f, -20f);

var sbHandle = ProceduralBuilders.AddImage(slidingArea, "Handle");
sbHandle.color = Color.white;
ProceduralBuilders.ApplyDefaultSlicedSprite(sbHandle);
sbHandle.rectTransform.anchorMin = new Vector2(0f, 0f);
sbHandle.rectTransform.anchorMax = new Vector2(1f, 0.2f);
sbHandle.rectTransform.sizeDelta = new Vector2(20f, 20f);
sbHandle.rectTransform.anchoredPosition = Vector2.zero;
scrollbar.targetGraphic = sbHandle;
scrollbar.handleRect = sbHandle.rectTransform;

// Wire ScrollRect & TMP_Dropdown
templateScroll.verticalScrollbar = scrollbar;
templateScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
templateScroll.verticalScrollbarSpacing = -3f;
```

> Item Background 因为是 `#F5F5F5` 当永久高亮色，靠 Toggle.ColorBlock 的 transition 来表达 hover/press 状态。Selectable 默认 `pressedColor=#C8C8C8`、`disabledColor=#C8C8C880` 会在 #F5F5F5 之上 multiplicative tint，效果跟 Unity 默认 dropdown 一致。

---

## 8. ScrollList 调整

bg 色由 `DefaultContainerColor`（现已是 white α=0.392）提供，root 上的 Image 不动。Viewport 切回 stencil Mask；按 `direction` 加单一 Scrollbar；ScrollRect 显式设默认参数。

### 8.1 Viewport 切 Mask

```diff
 var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
-viewport.gameObject.AddComponent<RectMask2D>();
+var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
+viewportImg.color = Color.white;  // alpha=1 关键，避免 alpha-discard 把 stencil 写飞
+ProceduralBuilders.ApplyDefaultSlicedSprite(viewportImg);
+var viewportMask = viewport.gameObject.AddComponent<Mask>();
+viewportMask.showMaskGraphic = false;
 _scroll.viewport = viewport;
```

### 8.2 ScrollRect 显式参数

```csharp
// OnAttached() 末尾、ApplyDirection 前
_scroll.movementType = ScrollRect.MovementType.Elastic;
_scroll.elasticity = 0.1f;
_scroll.inertia = true;
_scroll.decelerationRate = 0.135f;
_scroll.scrollSensitivity = 1f;
```

### 8.3 按 `direction` 加 Scrollbar

`ApplyDirection()` 已经处理 `_scroll.horizontal`/`_scroll.vertical` 切换；这次扩展为同时加/切对应 Scrollbar 子节点。

```csharp
private Scrollbar _vertScrollbar;
private Scrollbar _horizScrollbar;

private void ApplyDirection()
{
    // ... 现有 layoutGroup / fitter / content 逻辑不动 ...

    if (_direction == "horizontal")
    {
        EnsureHorizontalScrollbar();
        SetActive(_vertScrollbar, false);
    }
    else
    {
        EnsureVerticalScrollbar();
        SetActive(_horizScrollbar, false);
    }
}

private void EnsureVerticalScrollbar()
{
    if (_vertScrollbar != null) { _vertScrollbar.gameObject.SetActive(true); return; }
    var rt = ProceduralBuilders.AddChild(RectTransform, "Scrollbar Vertical");
    rt.anchorMin = new Vector2(1f, 0f);
    rt.anchorMax = new Vector2(1f, 0f);
    rt.pivot = new Vector2(1f, 1f);
    rt.sizeDelta = new Vector2(20f, 0f);
    var bg = rt.gameObject.AddComponent<UnityImage>();
    bg.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(bg);
    _vertScrollbar = rt.gameObject.AddComponent<Scrollbar>();
    _vertScrollbar.direction = Scrollbar.Direction.BottomToTop;

    var sliding = ProceduralBuilders.AddChild(rt, "Sliding Area");
    sliding.sizeDelta = new Vector2(-20f, -20f);
    var handle = ProceduralBuilders.AddImage(sliding, "Handle");
    handle.color = Color.white;
    ProceduralBuilders.ApplyDefaultSlicedSprite(handle);
    handle.rectTransform.anchorMin = Vector2.zero;
    handle.rectTransform.anchorMax = Vector2.zero;
    handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
    _vertScrollbar.targetGraphic = handle;
    _vertScrollbar.handleRect = handle.rectTransform;

    _scroll.verticalScrollbar = _vertScrollbar;
    _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
    _scroll.verticalScrollbarSpacing = -3f;
}

// EnsureHorizontalScrollbar 对称实现：anchor=(0,0)/(0,0), pivot=(0,0), sizeDelta=(0,20), direction=LeftToRight
```

### 8.4 破坏面

- ScrollList Viewport 子树多了 Image+Mask 组件——已存在的 ScrollListTests 几何断言（如 GetComponent<RectMask2D>）会失败
- `ApplyDirection` 切方向会重建 layoutGroup 但保留 scrollbar 子节点（toggling SetActive）
- 单方向 scrollbar 是新行为：之前 ScrollList 完全没滚动条，作者依赖 drag 滚动；现在 AutoHideAndExpandViewport 模式下 content > viewport 时显示 ── 是改进，不是破坏

---

## 9. Image / Text / Frame

**全部不动**：

- `Image` ── 默认无 sprite/color，跟 Unity 默认 Image prefab 一致（白色 100x100 无 sprite）
- `Text` ── 走 TMP 默认（fontSize=36，color=white）；跟 prefab 一致
- `Frame` ── 无视觉，是 PromptUGUI 独有概念（默认 prefab "Panel" 是带 sliced bg 的 Image，由 author 写 `<Image>` 表达）

---

## 10. 新增 `<InputField>` 内置原语

### 10.1 XML 用法

```xml
<!-- 简单单行输入 -->
<InputField id="username" placeholder="Enter username..." width="240" height="30"/>

<!-- 多行 -->
<InputField id="bio"
            placeholder="Tell us about yourself..."
            lineType="multi-newline"
            characterLimit="200"
            anchor="stretch" margin="8"/>

<!-- 密码 -->
<InputField id="password"
            placeholder="Password"
            contentType="password"
            width="240" height="30"/>

<!-- text 短手（同 Btn / Text） -->
<InputField id="search">默认搜索词</InputField>
```

### 10.2 属性集合

| 属性 | 类型 | 默认 | 备注 |
|---|---|---|---|
| `text` | string | `""` | 当前文本；setter 触发 OnValueChanged |
| `placeholder` | string | `""` | 占位文本（半透明 italic） |
| `contentType` | string | `standard` | `standard / autocorrected / integer-number / decimal-number / alphanumeric / name / email / password / pin / custom` |
| `lineType` | string | `single` | `single / multi-newline / multi-submit` |
| `characterLimit` | int | `0` | 0 = 无限 |
| `readOnly` | bool | `false` |  |
| `color` | string | (默认色) | bg tint |
| `sprite` | string | (默认) | bg sprite Resources path |
| `font` | string | `default` | i18n 字体类型（同 Btn/Text） |
| `tr` / `ctx` | bool / string | `true` / null | placeholder 走 i18n（与 `<Text>` 一致），text 不走（用户输入） |

### 10.3 R3 事件

```csharp
public Observable<string> OnValueChanged { get; }   // 每次按键
public Observable<string> OnEndEdit      { get; }   // 失焦或 submit
public Observable<string> OnSubmit       { get; }   // 显式提交
```

### 10.4 几何（程序化构造，复刻默认 prefab）

```
InputField (root)                                                ← components: RT + Image(sliced bg) + TMP_InputField
└── Text Area  (anchor=stretch, sizeDelta=(-20,-13), pos=(0,-0.5))  ← components: RT + RectMask2D(padding=-8,-5,-8,-5)
    ├── Placeholder  (anchor=stretch, sizeDelta=0)               ← components: RT + TMP_Text(italic, #32323280, align=Left+Top, wrap=Disabled, raycastTarget=false) + LayoutElement(IgnoreLayout=true)
    └── Text         (anchor=stretch, sizeDelta=0)               ← components: RT + TMP_Text(color=#323232, align=Left+Top, wrap=PreserveWhitespaceNoWrap, raycastTarget=false)
```

TMP_InputField 引用 wired up：

```csharp
_input.targetGraphic   = _bg;
_input.textViewport    = textAreaRt;
_input.textComponent   = _text;
_input.placeholder     = _placeholder;
_input.caretColor      = ProceduralBuilders.DefaultGlyphColor;          // #323232
_input.customCaretColor = false;
_input.selectionColor  = new Color(0.659f, 0.808f, 1f, 0.753f);
_input.fontAsset       = ...; // 由 ApplyFont 处理
```

### 10.5 注册

```csharp
// Application/BuiltinPrimitives.cs
registry.Register<InputField>("InputField");
```

跟 Toggle / Slider / Dropdown / ScrollList 一样路径，无 prefab。

---

## 11. SKILL.md 同步更新

`Runtime/.claude/skills/authoring-promptugui-xml/SKILL.md`：

### 11.1 内置原语表

> "Built-in primitives (12)" → "(13)"，新增一行：

| Tag | Notes | Tag-specific attributes |
|---|---|---|
| `<InputField>` | TMP_InputField；R3 `OnValueChanged`/`OnEndEdit`/`OnSubmit: string`。`<InputField>初始文本</InputField>` 短手设 `text=`。 | `text`, `placeholder`, `contentType` (`standard`/`integer-number`/`decimal-number`/`alphanumeric`/`name`/`email`/`password`/`pin`/`custom`/`autocorrected`), `lineType` (`single`/`multi-newline`/`multi-submit`), `characterLimit` (int), `readOnly` (bool), `color`, `sprite`, `font`, `tr` (placeholder)/`ctx` |

### 11.2 默认主题简注

在内置原语表上方加一段：

> **默认视觉主题**：白底 sliced + #323232 深字（与 Unity 6 `GameObject → UI → …` 创建出来的标准 prefab 一致）。所有控件的颜色/sprite 都能通过 `color=` / `sprite=` 属性 override；想要彻底深色主题项目级覆写 `ProceduralBuilders` 的常量，或用 Variant 方式 `color.dark="..."`。

### 11.3 quick reference cheatsheet 增 `<InputField>`

```diff
-BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
-              <Toggle> <Slider> <Dropdown> <ScrollList>
+BUILT-INS     <Frame> <Image> <Text> <VStack> <HStack> <Grid> <Btn> <Icon>
+              <Toggle> <Slider> <Dropdown> <ScrollList> <InputField>
+TEXT SHORT    ... 也支持 <InputField>初始文本</InputField>
```

---

## 12. 测试

### 12.1 EditMode（修订）

| 文件 | 改动 |
|---|---|
| `ToggleTests.cs` | 新增 `Geometry_BackgroundIsTwentyByTwentyTopLeft` / `Geometry_CheckmarkIsChildOfBackground` / `Geometry_LabelStretchesRightOfBackground` / `Visual_LabelRaycastTargetTrue`；旧"全 stretch"几何断言删除；root **无 Image** 断言（`GameObject.GetComponent<UnityImage>()` is null on root） |
| `SliderTests.cs` | `Geometry_BackgroundYInsetMatchesPrefab`（anchor=(0,0.25)/(1,0.75)）/ `Geometry_FillAreaInset`（pos=(−5,0), sizeDelta=(−20,0)）/ `Geometry_FillSizeDelta`（(10,0)）/ `Geometry_HandleSimpleNotPreserveAspect`/ root **无 Image** 断言 |
| `DropdownTests.cs` | `Geometry_ArrowSizeTwentyTwenty` / `Geometry_ArrowAnchoredPositionMinusFifteen` / `Geometry_ItemHeightTwenty` / `Geometry_ItemBackgroundIsSimpleHighlightF5`（color≈#F5F5F5）/ `Geometry_ItemCheckmarkSizeTwenty` / `Geometry_ItemCheckmarkPosTen` / `Geometry_ItemLabelOffset` / `Visual_ViewportMaskGraphicAlphaIsOne` / `Has_ScrollbarChildWithCorrectGeometry` / `Wired_VerticalScrollbarReference` |
| `BtnTests.cs` | `Visual_BgColorWhite` / `Visual_LabelColorDarkGrey` / `Visual_LabelFontSizeTwentyFour`（§4 锁定 24） |
| `ScrollListTests.cs` | `Visual_BgColorTranslucentWhite`（α≈0.392）/ `Visual_ViewportMaskGraphicAlphaIsOne` / `Has_VerticalScrollbarWhenDirectionVertical` / `Has_HorizontalScrollbarWhenDirectionHorizontal` / `Geometry_ScrollbarVerticalAnchorRight` / `Wired_ScrollRectVerticalScrollbar` |

### 12.2 EditMode（新增）

`Tests/EditMode/InputFieldTests.cs`：

```csharp
[TestFixture]
public class InputFieldTests
{
    [SetUp/TearDown] // 走 UI.ResetForTests

    [Test] Build_CreatesTextAreaPlaceholderText
    [Test] Geometry_TextAreaInsetMatchesPrefab     // sizeDelta=(-20,-13)
    [Test] Geometry_RectMask2DPadding              // (-8,-5,-8,-5)
    [Test] Visual_PlaceholderItalicHalfAlpha
    [Test] Apply_TextAttribute
    [Test] Apply_PlaceholderAttribute
    [Test] Apply_ContentType_Password
    [Test] Apply_LineType_MultiNewline
    [Test] Apply_CharacterLimit
    [Test] Apply_ReadOnly
    [Test] Event_OnValueChanged_FiresOnTextSet
    [Test] Event_OnEndEdit_FiresOnEndEditCallback   // simulate via reflection
    [Test] TextShorthand_BodyTextSetsText
    [Test] Trans_PlaceholderRuntime                 // i18n 走通
}
```

### 12.3 PlayMode

`Tests/PlayMode/InputFieldRuntimeTests.cs`：1 个端到端用例（Open Screen + 模拟键入 + assert OnValueChanged 触发）。

### 12.4 Lint

每改一个 .cs 后跑 `dotnet format whitespace + style`（CLAUDE.md 流程；不跑 `analyzers --severity info`）。

---

## 13. Sample 更新

`Samples~/MainMenu/`（若已存在）或新增 `Samples~/DefaultControls/`：

```xml
<Screen name="DefaultControls" anchor="stretch">
  <VStack anchor="center" size="300x500" spacing="12" padding="24">
    <Btn>Click Me</Btn>
    <Toggle>Enable Audio</Toggle>
    <Slider min="0" max="100" value="50"/>
    <Dropdown id="quality"/>
    <InputField placeholder="Type here..."/>
    <ScrollList itemTemplate="..." height="120"/>
  </VStack>
</Screen>
```

视觉应跟 Unity 默认创建的同名控件**视觉差不多一致**，仅 sprite 风格不同。

---

## 14. 风险与回滚

**风险**：

1. **Toggle 几何破坏面**：M5 spec 主体 + plan 都还在 feature 分支，但 Sample/Test 已经写过 ── 这次改动要同步。**估算成本**：测试 5-10 处断言重写。
2. **glyph tint 假设**：`pugui_checkmark` / `pugui_caret` 必须是白/灰 mono-mask；如果是带颜色的 PNG，tint 到 #323232 会出现彩色压暗。**runtime 验证**：第一次 `refresh_unity` 之后看 Game 视图。
3. **Mask alpha-discard 复发**：4af322b 的 bug 根因是 alpha=0.01 触发 UI/Default shader 的 alpha-test 丢弃。本次切回 Mask **必须坚持 alpha=1**（白色 + sliced sprite）。
   - **实施红线**：`ApplyDefaultSlicedSprite(viewportImg)` 之后 `viewportImg.color = Color.white`；不能写 `new Color(1,1,1,0.01f)`。
   - **回归测试**：EditMode 增 `ScrollList_ViewportMaskGraphicAlphaIsOne` / `Dropdown_ViewportMaskGraphicAlphaIsOne` 断言 `viewportImg.color.a == 1f`。
   - **PlayMode 验证**：`ScrollList_ChildrenStillVisibleUnderMask` 渲一帧检查内容可见。
4. **Dropdown popup Scrollbar 视觉**：原型 prefab 的 Scrollbar 用 `Background` sprite（一种灰色 sliced rect），我们替换为 `pugui_9slice_round`，可能跟 popup bg 难以区分。可接受；通过 ColorBlock 默认 hover/press tint 仍能区分交互态。
5. **Mask shader 兼容性**：URP/Built-in 渲染管线下 Mask + sliced sprite 行为一致；若项目使用自定义 UI shader，sliced sprite 的 alpha-corner 区域可能不写 stencil（这是 Unity 原 prefab 也会有的同样行为，非新增问题）。

**回滚**：

| 改动 | 回滚成本 |
|---|---|
| `ProceduralBuilders` 常量调色板 | 单文件 diff，1 个 commit revert 即可 |
| Toggle / Slider 几何 | 各自 commit；测试同期更新；最坏全 revert 到 4af322b 状态 |
| Dropdown Mask + Scrollbar | 单 commit；revert 即回到 RectMask2D + 无 Scrollbar |
| ScrollList Mask + Scrollbar | 单 commit |
| `<InputField>` | 新增 1 文件 + ControlRegistry 一行；删除即彻底回滚 |

---

## 15. 与主 spec / SKILL.md 的关系

- 主 spec [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5（内置原语表）：增 `<InputField>` 行，主题简注（合并 SKILL.md 同款表述）
- 主 spec §10（"自定义控件"哲学段）：不动
- M5 spec [`2026-05-09-m5-common-controls-design.md`](2026-05-09-m5-common-controls-design.md)：**不修订**，本 spec 是它的视觉 + 数量增量
- SKILL.md：见 §11

---

## 16. 后续项

- ScrollList 双轴模式（同时 H+V scrollbar），跟默认 Scroll View prefab 完全对齐；本 spec 锁单轴
- Knob 专用 sprite（atlas 加 `pugui_knob`，圆形 simple sprite，让 Slider handle 视觉更接近 Unity 默认 Knob）
- ColorBlock 开放为 `[UIAttr]` 让作者 XML override hover/press tint（normalColor / highlightedColor / pressedColor / selectedColor / disabledColor / fadeDuration）
- 项目级"主题包"概念：通过单一 ScriptableObject 覆写 ProceduralBuilders 全套常量
- Item Background 改用 sliced sprite + 透明色 + ColorBlock multi-state，目前是 simple+#F5F5F5 的扁平方案（跟默认对齐但限制了视觉差异化空间）
