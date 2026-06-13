# PromptUGUI 描述语言设计

**日期**：2026-05-07
**状态**：设计阶段（待 review，未进入实施）
**作用域**：仅 v1 描述语言与 C# API 设计；不含实现代码、不含 M1 之外的实施排期细节

---

## 1. 背景与目标

PromptUGUI 是一个 Unity 6+ 开源库，把一个紧凑的 XML 描述文件转译为运行时的 uGUI。
目标场景：像素风 SLG，需要同时跑 PC 宽屏与手机竖屏。

**为什么不用现成方案**

- **UI Toolkit**：像素游戏社区反馈少，预期踩坑成本高
- **uGUI 原生工作流**：高度可视化但重；对版本控制不友好（场景/Prefab 二进制 diff），LLM 难以直接生成

**这一层的核心目标**

1. 描述文件**作者面统一**：人写、LLM 写、未来工具生成都走同一份语法
2. 描述文件**精简到一页 skill 能教完**（约 40 行速查）
3. **位置全部基于锚点**，避免绝对坐标膨胀
4. **控件可由用户扩展**，但暴露给描述语言的接口保持极简
5. **数据/事件全部代码侧推送**，描述文件仅产生句柄
6. 未来与 HeTu 服务器（订阅式数据 + RPC）接入零摩擦——通过 R3 `Observable<T>` 统一抽象实现

**不做的事**（详见 §10）：动画、主题 token 系统、本地化、运行时 DOM 编辑 API、绑定表达式、可视化编辑器。

---

## 2. 设计决策一览

下表是设计阶段做过的关键二元选择，便于后续 review 与争议时回溯。

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| D1 | 多平台布局策略 | 同一控件树 + 锚点/尺寸变体 | 避免维护两份；同构利于 diff |
| D2 | 主要作者 | 人 + LLM 双优先 | 排除纯 binary / 纯 fluent C# 路线 |
| D3 | 自定义控件模型 | 描述文件可组合模板 + 代码侧 Prefab 注册 | 兼顾轻量复用与重量级控件 |
| D4 | 样式机制 | 样式与控件类型绑定（PrimaryButton / DangerButton） | 移除 class/token 抽象层；语法最简 |
| D5 | 数据绑定 | 描述文件只标记 id；代码侧主动推送 | 描述语言不引入表达式；R3 自然衔接 |
| D6 | 文件粒度 | Screen + Template 双概念，支持 Import | 大场景与可复用片段并存 |
| D7 | 锚点抽象 | 4×4 预设 + margin/size 二字段，统一向锚点内为正 | 与 uGUI 心智同构，但用户面只剩两个字段 |
| D8 | 文件格式 | XML | 通用工具链（高亮/折叠/Schema/解析器）零成本 |
| D9 | Fragment vs Template | 合并为单一 `<Template>` | KISS |
| D10 | 模板逻辑 | 仅允许 `if="{{p}}"`；无 `For`、无表达式 | 强制把逻辑推到代码侧 |
| D11 | margin 语义 | 始终"从锚点向内为正" | 用户不需根据锚点切换正负号 |
| D12 | 拉伸轴禁出现 size | 严格报错 | 避免歧义 |
| D13 | Variant 切换时机 | 运行时可切换，触发已开 Screen 重解算 | 支持桌面端窗口缩放 |
| D14 | Variant 优先级 | 声明顺序 last-active-wins | 简单，可控 |
| D15 | Variant 块形式动作 | 仅 `<Add>` | 可见性靠 `hidden.var`，覆盖靠内联 attr.var |
| D16 | 自定义控件接入 | `[UIAttr]` + `[Bind]` 反射，缓存 setter | 注册期一次反射，运行零开销 |
| D17 | `Get` 失败 | 抛异常 | UI 错误应在测试期 fail loud |
| D18 | 事件/数据 API 类型 | 统一 `Observable<T>`，禁用 `event`/Action | 第一版即对齐 HeTu 抽象 |

---

## 3. 一个完整可读的例子

读完这段就能掌握 80%。

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Import src="common/Buttons.ui.xml"/>

  <!-- 可复用的标题面板 -->
  <Template name="TitledPanel">
    <Param name="title"/>
    <Param name="closable" default="true"/>

    <VStack padding="16" spacing="8">
      <HStack height="32" spacing="8">
        <Text style="h2">{{title}}</Text>
        <Frame width="0"/>
        <CloseButton if="{{closable}}" id="close"/>
      </HStack>
      <Slot/>
    </VStack>
  </Template>

  <!-- 主菜单 -->
  <Screen name="MainMenu">
    <Image anchor="stretch" sprite="bg/main"/>

    <VStack id="menuRoot" spacing="12"
            anchor.pc="center" width.pc="480" height.pc="320"
            anchor.mobile-portrait="bottom-stretch"
            height.mobile-portrait="400"
            margin.mobile-portrait="_,16,80,16">
      <PrimaryButton id="playBtn"     size="240x64">开始游戏</PrimaryButton>
      <PrimaryButton id="settingsBtn" size="240x64">设置</PrimaryButton>
      <DangerButton  id="quitBtn"     size="240x64">退出</DangerButton>
    </VStack>

    <Variant when="mobile-portrait">
      <Add into="@root">
        <VirtualJoystick id="vjs" anchor="bottom-left"
                         size="160x160" margin="_,_,40,40"/>
      </Add>
    </Variant>
  </Screen>
</PromptUGUI>
```

代码侧：

```csharp
var screen = UI.Open("MainMenu");
screen.Get<PrimaryButton>("playBtn").OnClick
      .Subscribe(_ => Game.Start()).AddTo(screen);
screen.Get<DangerButton>("quitBtn").OnClick
      .Subscribe(_ => Application.Quit()).AddTo(screen);
```

---

## 4. 文件骨架

### 4.1 后缀与编码

- 文件后缀：`.ui.xml`（双后缀，让 IDE 自动 XML 高亮，业务工具按 `.ui` 过滤）
- 编码：UTF-8
- 根元素：`<PromptUGUI version="1">`，`version` 强制必填，预留语言演进

### 4.2 顶层允许元素

| 元素 | 用途 |
|---|---|
| `<Import src="..." [as="ns"]/>` | 引入其他 .ui.xml 中的 Screen/Template |
| `<Screen name="...">` | 完整场景，运行时由 `UI.Open(name)` 打开 |
| `<Template name="...">` | 可复用子树定义，编译期展开 |

跨文件 `Screen` 同名 → 报错。
Template 同名（含 commons 与各 Import 的任意组合）→ 报错；`as="ns"` 是唯一显式消歧手段（`<ns.TitledPanel/>`）。
注释直接用 XML 标准 `<!-- -->`。

---

## 5. 内置控件原语

刻意保持极少：14 个原语，覆盖布局、最基础视觉、点击交互与五个常用控件（含 Tab 容器），其他全部通过自定义控件或 `<Template>` 扩展。

| 标签 | 作用 | 对应 uGUI |
|---|---|---|
| `<Frame>` | 纯定位容器，无视觉；可选 `mask="rect"` 启用 RectMask2D | RectTransform（+ 可选 RectMask2D） |
| `<Image>` | 图像 / 9-slice / 纯色块 | Image |
| `<RawImage>` | 运行时动态 `Texture`（头像 / 下载图 / RenderTexture）；图源仅 C# `Texture` 属性，非 sprite；`type=contain\|cover` 等比适配 + mask（详见 [`2026-06-06-rawimage-control-design.md`](2026-06-06-rawimage-control-design.md)） | RawImage |
| `<Text>` | 文本 | TMP_Text |
| `<VStack>` | 纵向自动排布 | RectTransform + VerticalLayoutGroup |
| `<HStack>` | 横向自动排布 | RectTransform + HorizontalLayoutGroup |
| `<Grid>` | 网格排布 | RectTransform + GridLayoutGroup |
| `<Btn>` | 通用按钮（背景图 + R3 OnClick 流） | Image + Button (uGUI) |
| `<Icon>` | 项目级 IconSet 中的图标（按名查找，打包期剪枝） | Image |
| `<Theme name=... base=...?>` / `<Color name=... value=...>` | 顶层主题 / 颜色 token 块；运行时可通过 `UI.Theme.Set` 切换 | — |
| `<Toggle>` | 复选 / 单选（OnValueChanged: bool；group= 字符串键互斥） | Image + Toggle (uGUI) + 内置 label |
| `<Slider>` | 数值滑块（OnValueChanged: float） | Image + Slider (uGUI) |
| `<Progress>` | 线性进度条 (scale / Image.Type.Filled, horizontal / vertical, +可选 frame / mask / bg / fill 装饰) | RectTransform（+ 内部 4 个图层；详见 [`2026-05-27-progress-control-design.md`](2026-05-27-progress-control-design.md)） |
| `<Dropdown>` | 下拉选择（OnSelected: int；BindOptions 推送选项） | TMP_Dropdown |
| `<ScrollList>` | 滚动列表（BindItems 推送数据；itemTemplate 引用 Template/Control 类） | ScrollRect + Mask |
| `<TabBar>` | Tab 容器；私有 ToggleGroup + Horizontal/VerticalLayoutGroup；纯布局,无自身视觉；支持 `itemTemplate` + `BindItems` 动态构建 | RectTransform + ToggleGroup + LayoutGroup（详见 [`2026-05-27-tabbar-design.md`](2026-05-27-tabbar-design.md)） |
| `<Tab>` | `<TabBar>` 子节点；可点击容器（接子，Frame 式叠放）；uGUI Toggle + 懒建 label + 可选 icon；`color` / `sprite` / `selectedSprite` 自管视觉（共享样式用 Template）；`bind="frame_id"` 声明式切换 Frame 可见性 | RectTransform + UnityImage + UnityToggle |
| `<Carousel>` | 水平翻页轮播卡容器；自动播放 + 拖动 + 无限循环 + 状态化指示点；itemTemplate + BindItems 动态卡片；当前页是运行期独占状态（resize 不重置） | RectTransform + RectMask2D + 自管卡条（详见 [`2026-06-04-carousel-design.md`](2026-06-04-carousel-design.md)） |
| `<Markdown>` | Markdown document → scrollable subtree of existing primitives; dynamic `text`, resize-safe; soft-depends Markdig (`PROMPTUGUI_HAS_MARKDIG`) | see [`2026-06-09-markdown-control-design.md`](2026-06-09-markdown-control-design.md) |

`<Btn>` 提供"按钮"这一通用交互原语：可作为 Template 根，配合 `<Image>` / `<Text>` 子节点组合出 PrimaryButton / DangerButton / IconButton 等业务变体而无需额外 prefab。`Btn` 内部用 R3 `Subject<Unit>` 暴露 `OnClick`（与 §9.4 的"事件统一为 `Observable<T>`"约束一致）。

`<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<TabBar>` 默认开启的参考实现（详见 [`2026-05-09-m5-common-controls-design.md`](2026-05-09-m5-common-controls-design.md)）。视觉风格用 `sprite` / `color` 等属性表达；需要项目级强差异化样式（像素描边、按下震动等）时作者继承相应类重写 `OnAttached`。

### 5.1 通用属性（任何标签可用）

| 属性 | 作用 |
|---|---|
| `id` | 在所属 Screen / Template 内唯一的句柄 |
| `anchor` | 9 预设之一（详见 §6） |
| `size` / `width` / `height` | 尺寸（详见 §6） |
| `margin` | 距锚点的内向距离（详见 §6） |
| `pivot` | 透传 RectTransform pivot；缺省随 anchor 自动推导 |
| `padding` | 容器内边距（仅 VStack/HStack/Grid/Frame） |
| `spacing` | 子项间距（仅 VStack/HStack/Grid） |
| `hidden` | 初始隐藏（GameObject SetActive false） |
| `interactable` | 初始不可交互（CanvasGroup.interactable false） |

### 5.2 文本内容简写

```xml
<Text>金币: 1234</Text>          <!-- 等价 <Text text="金币: 1234"/> -->
<PrimaryButton>开始游戏</PrimaryButton>
```

控件想吃这种简写，需在注册时声明 `defaultTextAttr`（默认值 `"text"`）。Frame/VStack 等容器不支持。

### 5.3 控件特有属性

由各标签自行声明：
- `<Image sprite="bg/main" color="#FFFFFFAA" type="sliced|simple|filled|tiled|contain|cover"/>`（`contain`/`cover` 等比适配，相对父级框，裁切作者用父级 `mask="rect"` 负责）
  - `color` 值解析：主题 token（经 `UI.Theme.Current` 与 base 链查表）→ 字面 `ColorUtility.TryParseHtmlString`；详见 [`2026-05-28-color-tokens-design.md`](2026-05-28-color-tokens-design.md)
- `<Image mask="rect|self" showMask="true|false" maskPadding="T,R,B,L"/>` — 见 [`2026-05-16-frame-image-mask-design.md`](2026-05-16-frame-image-mask-design.md)
- `<Image ... tint="multiply|linear"/>` — Image 混合材质：`multiply`（默认，UI/Default）或 `linear`（Linear Light，128 灰中性）。这些控件通用：Image / Icon / Btn / Toggle / Slider / Dropdown / ScrollList / InputField / Progress；`<Text>`（TMP 独立 shader）与 `<Tab>`（v1 暂不支持）除外。见 [`2026-05-28-image-tint-blend-mode-design.md`](2026-05-28-image-tint-blend-mode-design.md)
- `<Frame mask="rect" maskPadding="T,R,B,L"/>` — 同上
- `<Text font="..." fontSize="32" color="..." align="left|center|right" wrap="true"/>` — 注意 Text 的字号属性是 `fontSize`，**不是** `size`；`size` 是通用 WxH 布局尺寸（§6.2），写 `<Text size="32"/>` 会被布局解析器拒收

完整属性表见各控件 README（不在本 spec 范围）。

### 5.4 `<Icon>`（项目级图标系统）

引用项目级 IconSet 中的图标。完整设计见独立 spec
[`2026-05-08-icon-assets-design.md`](2026-05-08-icon-assets-design.md)。

简表：
- `name="ns:icon"` 必填，冒号分隔，两侧字符 `[\w\-]+`
- `color` multiply tint，默认 `#ffffff`
- `size` 默认 `native`，Icon 独占该值
- 完整 attrs 见 §5.3

### 5.5 `<SafeArea>`（安全区容器）

显式安全区包裹层；运行时每条边 `inset = max(designMargin_i, Screen.safeArea_i)`（max-blend），自动响应屏幕旋转 / 窗口缩放 / Device Simulator / Variant ReSolve / Dynamic Island。完整设计见 [`2026-05-26-safearea-margin-absorb-v2-design.md`](2026-05-26-safearea-margin-absorb-v2-design.md)。

简表：
- 接受 `margin`：表示"距父级边至少这么多 design px"，会被 device safe-area inset 取大吸收。`<SafeArea/>`（无 margin）= SafeArea 正好 fit safe area。
- 不接受 `anchor` / `size` / `width` / `height` / `pivot`（含 `.variant` 覆盖）—— 形状固定为 stretch；写这些属性会在 parse 期抛 `ParseException`。
- 允许 `id` / `hidden` / `interactable` / `if=` / `margin` / `margin.variant`。
- 典型用法：作为 `<Screen>` 直接子节点，UI 全放它里面；需要 bleed 到屏幕物理边缘的背景图作为 SafeArea 的兄弟节点。
- 想要"safe area + 固定 padding"叠加（e.g. 16px below the notch, never flush），在 SafeArea 内嵌套 `<Frame anchor="stretch" margin="16,_,_,_"/>`。

### 5.6 `<Screen reference="WxH">`（参考分辨率，since 2026-05-13）

`<Screen>` 上可选属性 `reference="WxH"` 把 CanvasScaler 切到 `ScaleWithScreenSize`，
`referenceResolution` 即该值。不设 = `ConstantPixelSize, scaleFactor=1`（零迁移默认）。
`matchWidthOrHeight` 按朝向自动推断：W ≥ H → 0（锁宽），H > W → 1（锁高）。
支持 `.variant` 形态（`reference.mobile="1080x1920"`），变体翻转时通过 `ReSolve`
立即重应用。完整设计见独立 spec
[`2026-05-13-screen-reference-resolution-design.md`](2026-05-13-screen-reference-resolution-design.md)。

- `scale-mode="auto|pixel"`（可选，支持 `.variant`）：`pixel` 切 CanvasScaler 到 `ConstantPixelSize` + 整数倍 `scaleFactor`（用于像素艺术）。详见 [`2026-05-25-pixel-perfect-scaling-design.md`](2026-05-25-pixel-perfect-scaling-design.md)。
- 元素级 `scale="Nx"`（N 正整数，支持 `.variant`）：设备像素密度形态，`localScale = N / canvasFactor`（每个设计单位渲染 N 个物理像素，canvas factor 变化时重算），用于 `scale-mode="pixel"` 下位图字的像素对齐。普通 `scale="N"`（正浮点）仍是 factor-independent 的 box-preserving 渲染密度乘数。详见 [`2026-05-31-scale-device-density-design.md`](2026-05-31-scale-device-density-design.md)。
- 元素级 `scale="<r>r"`（r 正浮点，小写 `r`，支持 `.variant`）：画布相对吸附形态，`localScale = max(1, round(canvasFactor × r)) / canvasFactor`——缩放跟随 factor（随窗口长大），但净物理像素/设计单位吸附到整数保持像素对齐，填补 `scale="N"`（响应但奇数 factor 糊）与 `scale="Nx"`（恒定不长大）之间。详见 [`2026-06-01-scale-canvas-relative-snap-design.md`](2026-06-01-scale-canvas-relative-snap-design.md)。
- V/HStack 直下声明了 scale 的 `<Text>` 自动桥接（实例化期 wrapper + `ILayoutElement` 报告 `TMP preferred × s`）：占位 = 视觉、按整行宽换行、行高随内容；三种形态（`N` / `Nx` / `<r>r`）一致，resize / Variant 重算。其余控件与 Grid 子节点维持 LayoutGroup-skip（footgun 文档化）。详见 [`2026-06-11-scaled-text-layout-bridge-design.md`](2026-06-11-scaled-text-layout-bridge-design.md)。

### 5.7 `<Trigger>` / `<Animation>`（事件订阅 + LitMotion 动画，since 2026-05-14）

- `<Trigger>` — 订阅宿主控件（或任意 `source=` 指定的 id）上的 R3 事件流，满足条件时播放或停止动画序列。
- `<Animation>` — 声明一段 LitMotion 驱动的属性动画（position / scale / color / alpha 等），可组合进 `<Trigger>` 的 `play=` / `stop=` 列表。

详见独立 spec
[`2026-05-14-litmotion-animations-design.md`](2026-05-14-litmotion-animations-design.md)。

---

## 6. 锚点与尺寸

### 6.1 anchor —— 4×4 网格

`anchor="<vertical>-<horizontal>"`

| | left | center | right | stretch |
|---|---|---|---|---|
| **top** | top-left | top-center | top-right | top-stretch |
| **center** | center-left | center | center-right | center-stretch |
| **bottom** | bottom-left | bottom-center | bottom-right | bottom-stretch |
| **stretch** | stretch-left | stretch-center | stretch-right | stretch |

别名：`center` = `center-center`；`stretch` = `stretch-stretch`；`fill` = `stretch`。

### 6.2 size / width / height

```xml
<Image anchor="top-right"      size="240x80"/>     <!-- 两轴点锚 -->
<Image anchor="stretch-left"   width="200"/>       <!-- 仅水平点锚 -->
<Image anchor="top-stretch"    height="64"/>       <!-- 仅竖向点锚 -->
<Image anchor="stretch"/>                          <!-- 双轴拉伸 -->
```

**严格规则**：拉伸轴上**禁止**出现 `size` / `width` / `height` 的相应分量。如 `anchor="top-stretch"` + `width="..."` 是非法的 → 编译期报错。

**`native`**：取控件 native size（仅 `<Icon>` 接受；其他控件出现 → ParseException）。常用 `<Icon name="ui:settings"/>`，默认就是 native，作者一般不写 size。

### 6.3 margin —— 统一为"从锚点向内的距离"

```
margin="16"           四边都 16
margin="16,8"         上下 16，左右 8
margin="16,8,4,12"    T=16, R=8, B=4, L=12
```

任何锚点下 margin 的方向语义恒定："离锚点/锚定边向内为正"。

举例：

```xml
<Btn anchor="top-right"      size="240x80" margin="16"/>           <!-- 距右上 16 -->
<Bar anchor="top-stretch"    height="64"  margin="0,8,_,8"/>       <!-- 顶部全宽 -->
<Side anchor="stretch-right" width="200"  margin="16,0,16,_"/>     <!-- 右侧全高 -->
<BG  anchor="stretch"        sprite="bg"/>                         <!-- 全屏 -->
```

`_` 表示该位"不参与布局"，仅可读性。允许全省。

### 6.4 pivot 自动推导

| anchor | 自动 pivot |
|---|---|
| top-left | (0, 1) |
| top-right | (1, 1) |
| center | (0.5, 0.5) |
| bottom-stretch | (0.5, 0) |
| stretch | (0.5, 0.5) |

仅当需要绕非中心点旋转/缩放时才显式 `pivot="0.5,1"` 等。

### 6.5 在 VStack/HStack/Grid 内的特殊行为

子节点的 `anchor` 与 `margin` **被布局组接管而失效**。仅 `size` / `width` / `height` 生效（被写入 LayoutElement.preferredWidth/Height）。

子节点显式写了 `anchor` → **编译期警告**（不静默丢弃，避免误导）。

容器自身仍用 `anchor` + `size`/`margin` 在父级里定位。

**例外：`flow="false"`（出流子节点，FLW）。** 子节点声明 `flow="false"` 后退出排版流：实现为 `LayoutElement.ignoreLayout=true`，布局组在定位与量算 preferred 时都跳过它；`anchor` / `margin` / `N%` 对它恢复完整自由定位语义（相对布局组自身的 rect），上述警告与 `%` 禁令一并解除。`width="stretch"` 仍非法（流外没有 flex 权重，应改用 `anchor="stretch"`）。典型用途：hug 尺寸的 Stack 内铺满的 9-slice 背景层 / 角标。在非布局组父级下 `flow` 是 inert 属性（lint `PUI-FLOW-OUTSIDE-GROUP`）。可被 Variant 覆盖（`flow.portrait="false"`），切换走标准 ReSolve 重解算。

---

## 7. Template：复用与组合

### 7.1 定义

```xml
<Template name="TitledPanel">
  <Param name="title"/>
  <Param name="closable" default="true"/>
  <Param name="icon"     default=""/>

  <VStack padding="16" spacing="8">
    <HStack height="32" spacing="8">
      <Image if="{{icon}}" sprite="{{icon}}" size="32x32"/>
      <Text style="h2">{{title}}</Text>
      <Frame width="0"/>
      <CloseButton if="{{closable}}" id="close"/>
    </HStack>
    <Slot/>
  </VStack>
</Template>
```

**约定**

- `<Param>` 必须紧跟 `<Template>` 开头；其他位置出现的 Param 视为普通自定义控件
- `default` 缺省 → 该参数必填，调用方未传则编译期报错
- `<Slot/>` 出现 0 或 1 次（v1 不支持多 slot）
- 同名 Template 跨文件冲突 → 报错；用 `Import as="ns"` 或重命名

### 7.2 调用

```xml
<TitledPanel anchor="center" size="600x400" title="背包">
  <Grid columns="6" spacing="4">
    <ItemSlot/>  ...
  </Grid>
</TitledPanel>
```

属性 = 参数；元素内容 = 注入到 `<Slot/>`。形式上与原生标签完全一致。

### 7.3 替换规则（仅 Template 内有效）

| 用法 | 例 |
|---|---|
| 属性插值 | `text="{{title}}"` |
| 属性内拼接 | `sprite="icons/{{icon}}.png"` |
| 文本节点 | `<Text>{{title}}</Text>` |
| 条件元素 | `<X if="{{closable}}"/>` |

**`if` 是唯一允许的逻辑结构**：
- 仅检查参数 truthy（非空串、非 false、非 0、非 null）
- 不支持 `!`、`==`、`&&`、`||` 等
- 不支持 `<Else>`、`<For>`

如果模板需要更多逻辑 → 它不该是模板，应改为代码侧自定义控件。

### 7.4 ID 作用域

模板内的 `id` 是**模板实例局部命名空间**：

```xml
<Template name="Dialog">
  <Frame>
    <CloseButton id="close"/>
    <Slot/>
  </Frame>
</Template>

<Screen name="Game">
  <Dialog id="confirm">
    <Text>真的吗？</Text>
  </Dialog>
</Screen>
```

代码侧：

```csharp
var dialog   = screen.Get("confirm");        // Dialog 实例
var closeBtn = dialog.Get("close");          // 模板内部
// 或：screen.Get("confirm/close")
```

同一模板可被实例化多次，id 不冲突。

### 7.5 Screen 与 Template 区别

| | Screen | Template |
|---|---|---|
| 顶层用法 | `UI.Open(name)` | 不可独立打开 |
| Canvas 归属 | 自己的根 Canvas | 嵌入父 Screen 的 Canvas |
| 生命周期 | 有 OnOpen/OnClose 钩子 | 跟随父节点 |
| 可作为标签使用 | 否 | 是 |

需要"既能独立打开又能嵌入"的场景：定义为 Template，再用一个简单 Screen 包一层。

### 7.6 Import

```xml
<Import src="common/Buttons.ui.xml"/>
<Import src="common/Panels.ui.xml" as="ui"/>

<Screen name="X">
  <PrimaryButton/>           <!-- 来自 Buttons -->
  <ui.TitledPanel/>          <!-- 来自 Panels，带前缀消歧 -->
</Screen>
```

`as=` 是唯一显式消歧手段；commons 与 Import 多源同名时必填。常态下 Template 名唯一即可省略。

---

## 8. Variant：平台与上下文变体

### 8.1 模型

- Variant 是带名字的开关，由代码侧管理：
  ```csharp
  UI.Variants.Set("mobile-portrait", true);
  UI.Variants.Set("pc", false);
  ```
- 多个开关可同时为真
- 切换 Variant 触发已实例化 Screen 的**重新解算**（不重建 GameObject，只刷新被覆盖的属性值）——支持 PC 端窗口缩放等场景
- `UI.Theme.Set(name)` 也触发已开 Screen 的重新解算，平行于 `UI.Locale.Set` 与 `UI.Variants.Set` 的逻辑

### 8.2 内联属性覆盖（90% 用法）

```xml
<VStack id="menuRoot"
        anchor="center" size="480x320"
        size.mobile="320x600">
  ...
</VStack>
```

任何属性都可加 `.variantName` 后缀；多个后缀可并存。

`attr.var` 是**整体替换**——不支持按分量的部分覆盖（例如 `size.var="_,400"` 借 margin 占位语法是非法）。要单独改一根轴：用 `width.var` / `height.var`，或在 base 不放冲突轴的 size、改用显式 `pc` / `mobile-portrait` 等变体把每条尺寸分别声明（见 §3 主菜单示例：anchor 切到 stretch 那一轴必须从 base 删除 width，所以走 `anchor.pc` + `width.pc` + `height.mobile-portrait` 这种"分期"写法）。

### 8.3 解析规则

按属性的**声明顺序**扫描所有 `attr.X` 形式：取**最后一个 X 当前为真**的那个；都不真则用基础值。

```xml
<X size="100" size.mobile="200" size.tablet="150"/>
```

| 当前激活 | 结果 |
|---|---|
| 都不开 | 100 |
| `mobile` | 200 |
| `tablet` | 150 |
| `mobile`+`tablet` 都开 | 150（声明在后） |

需要不同优先级，调整声明顺序即可。

> **基础值与控件私有属性的复位边界。** 上面「都不真则用基础值」隐含一个前提：基础值存在。公共几何属性（`anchor` / `size` / `width` / `height` / `margin` / `pivot` / `interactable` / `flow`，以及 `scale`）即使**没有**基础值，变体失活时也会干净地回到控件默认——ReSolve 把它们整体重交给 `ApplyCommon` / `ApplyScales` 重算（见 scale-device-density 设计的 `Variant_reset_restores_base_geometry`）。**例外：`hidden` 不自愈**——它名义上也算「公共属性」，但 `ApplyCommon` 用 `if (hidden.HasValue)` 应用它（`Control.cs`），解算为空时是「跳过」而非「复位」，所以 `hidden` 与下面的控件私有属性同类、必须配基础值。**控件私有属性**（映射到控件自身 `[UIAttr]` setter 的，如 `<TabBar>` / `<ScrollList>` 的 `direction` / `spacing`、各控件的 `color` / `sprite` 等）在**只有 `.variant` 覆盖、没有基础值**时，变体失活**不会**回滚：ReSolve 的重应用对「解算为空」的控件属性直接 `continue` 跳过（没有针对任意 setter 的通用「回默认」信号），于是停在最后一次应用的值。因此控件私有属性（含 `hidden`）的 `.variant` 覆盖应**始终配一个基础值**（如 `direction="horizontal" direction.portrait="vertical"`）。这与 `*Color` / `pressedSprite` 的「set-only，变体清除不回滚」属同一类已知限制（见 `2026-06-01-btn-pressed-sprite-design.md`）；真正想 sticky 的运行期值另有 `RuntimeStateAttr`（`isOn` / `value` / `current`）机制。**lint CLI 会把缺失基础值静态报为 `PUI-VARIANT-NO-BASE`**（仅内置控件——CLI 看不到自定义控件的 setter；模板体根节点上可被调用方注入的 CommonAttrs 也豁免）。

### 8.4 块形式：仅 `<Add>`

```xml
<Variant when="mobile-portrait">
  <Add into="#menuRoot" at="end">
    <Frame height="40"/>
  </Add>
  <Add into="@root">
    <VirtualJoystick id="vjs" anchor="bottom-left" size="160x160" margin="_,_,40,40"/>
  </Add>
</Variant>
```

- `into` 指定目标父节点：`@root`（Screen 根）/ `#id`（Screen 顶层 id）/ `#id/path/to/inner`（按 `/` 分段下钻 ScopedIds，与 §9.2 `Screen.Get("a/b")` 同义；用于把变体专属节点插入模板实例内部，例如 `<Add into="#dialog/itemGrid">` 把项目注入 TitledPanel 内的 Grid）
- `at` 控制插入位置：`start` / `end`（默认）/ 整数索引（越界自动 clamp：负数 → 0，超过当前 child 数 → 末尾）
- 移除元素 → 用 `hidden.variant="true"`，无需 Remove
- 修改属性 → 用内联 `attr.variant`，无需 Override

### 8.5 不可覆盖

下列字段**禁止**带 `.variant` 后缀，编译期报错：

- `id`
- 标签名本身
- `<Param>` 的 `default`

理由：避免 Variant 切换造成控件身份/类型/契约漂移，使代码侧 `Get<T>` 永远稳定。

---

## 9. 代码侧 C# 接口

### 9.1 顶层 facade

```csharp
public static class UI {
    public static IScreen Open(string screenName);
    public static void   Close(string screenName);
    public static IScreen Get(string screenName);

    public static class Variants {
        public static void Set(string name, bool active);
        public static bool IsActive(string name);
    }

    public static class Registry {
        public static void Register<T>(string tag, GameObject prefab) where T : Control;
    }
}
```

### 9.2 句柄查询

```csharp
PrimaryButton btn  = screen.Get<PrimaryButton>("playBtn");
IControl       any = screen.Get("playBtn");
IControl   nested  = screen.Get("confirmDialog/close");
IEnumerable<ItemSlot> slots = screen.GetAll<ItemSlot>();
```

- `Get` 找不到 → 抛异常（fail loud）
- `TryGet` 提供给运行时不确定存在的场景

### 9.3 自定义控件作者模式

一个自定义控件 = Prefab + 类 + 一次注册。

```csharp
public class PrimaryButton : Control {
    [UIAttr] public string Text {
        get => _label.text;
        set => _label.text = value;
    }

    public Observable<Unit> OnClick => _btn.OnClickAsObservable();

    [Bind] TMP_Text _label;
    [Bind] Button   _btn;
}

UI.Registry.Register<PrimaryButton>("PrimaryButton",
    Resources.Load<GameObject>("UI/PrimaryButton"));
```

约定：
- `[UIAttr]` 标记的属性 = 描述文件该 tag 上同名属性自动写入
- `[Bind]` 标记的字段 = Prefab 内同名子节点自动 wire
- 反射只在注册期一次，运行期使用缓存的 setter（零额外 GC）

通用属性（`anchor` / `size` / `margin` / 等）由 `Control` 基类统一处理；子类不需也不允许覆盖。

### 9.4 事件 = R3 流

所有事件统一暴露为 `Observable<T>`：

```csharp
screen.Get<PrimaryButton>("playBtn")
      .OnClick
      .Subscribe(_ => StartGame())
      .AddTo(screen);
```

`IScreen` 实现 `IDisposable` / `ICancelable`，是订阅生命周期 owner。

**严格约束**：库暴露的所有事件接口禁止使用 C# `event` 或 `Action` 回调，必须是 `Observable<T>`。这是为 HeTu 接入预留的唯一约束。

### 9.5 数据绑定 = 代码侧推送

```csharp
playerGoldRP.Subscribe(g => screen.Get<Text>("goldLabel").Text = $"金币: {g}")
            .AddTo(screen);

// 可选糖：
playerGoldRP.BindText(screen.Get<Text>("goldLabel"), g => $"金币: {g}")
            .AddTo(screen);
```

**列表也是代码侧推送**——列表控件本身是自定义控件：

```xml
<ScrollList id="inv" anchor="stretch" itemTemplate="ItemSlot"/>
```

```csharp
var list = screen.Get<ScrollList<Item>>("inv");
list.BindItems(player.Inventory, (slot, item) => {
    slot.Icon  = item.Icon;
    slot.Count = item.Count;
});
```

`itemTemplate` 是已注册 tag 名；ScrollList 内部按需要实例化。

### 9.6 Screen 生命周期（可选 ScreenView）

简单场景直接 Open + Get + Subscribe；复杂场景继承 `ScreenView`：

```csharp
public class InventoryView : ScreenView {
    protected override string ScreenName => "Inventory";

    protected override void OnOpen() {
        Get<PrimaryButton>("closeBtn").OnClick
            .Subscribe(_ => Close()).AddTo(this);
        Player.Gold.BindText(Get<Text>("gold"), g => $"{g}").AddTo(this);
    }

    protected override void OnClose() { /* save state etc. */ }
}

UI.Bind<InventoryView>();   // 注册：Screen name → 类
```

`UI.Open("Inventory")` 时若有 Bound class 则同时实例化它，调用 OnOpen。

### 9.7 HeTu 接入预留

HeTu 订阅返回 `Observable<T>`（设计上对齐 R3）。所以**当前 API 不需要为 HeTu 改任何东西**：

```csharp
// 今天：本地 ReactiveProperty
playerGoldRP.BindText(...).AddTo(screen);

// 未来：HeTu 订阅
HeTu.Sub<int>("player.gold").BindText(...).AddTo(screen);
```

预留点仅一处：库的所有事件/绑定 API 必须 `Observable<T>`，不得退化为 event / Action。这条在 §9.4 已明确为硬约束。

---

## 10. 显式非目标（v1 不做）

为防止后续讨论 scope creep：

- ❌ **动画**：用 Unity Animator / DOTween，不在描述文件管
- ❌ **主题 token / 全局样式表**：风格通过控件类型变体表达（PrimaryButton vs DangerButton），不再做 token 层
- ✅ **本地化** (M5 起)：见 `2026-05-08-i18n-fonts-design.md`
  - 零 key gettext 流（msgid = 源文本字面量）
  - .po 表 + Roslyn / XML 抽取 + LLM 翻译菜单
  - locale 切换走 Variant.Changed 通路
  - 字体 type → 每 locale TMP_FontAsset 表（Settings）
- ❌ **运行时 DOM 编辑 API**：想动态加节点请重建 Screen
- ❌ **绑定表达式 / 模板循环 `<For>`**：列表是代码侧推送的自定义控件
- ❌ **多 Slot 命名**：单匿名 Slot 够用；真有多 slot 需求重新评估
- ❌ **可视化编辑器**：但保留 ScreenView 等抽象使其将来可加
- ❌ **样式 class / inheritance**：见 D4

---

## 11. PromptUGUI 描述语言速查（一页 skill）

下面 40 行就是未来给 LLM 的 system prompt 片段，作为对"语言精简"目标的最终验证。

```
# PromptUGUI 描述语言 (.ui.xml) 速查

## 文件骨架
<PromptUGUI version="1">
  <Import src="path.ui.xml" [as="ns"]/>
  <Screen   name="...">  body  </Screen>
  <Template name="..."> [<Param name="p" [default=""]/>...] body </Template>
</PromptUGUI>

## 内置原语 (8)
<Frame>            纯定位容器
<Image sprite="" color=""/>
<Text>文本</Text>     或 <Text text="..."/>
<VStack spacing="" padding="">
<HStack spacing="" padding="">
<Grid columns="" spacing="" padding="">
<Btn color="" sprite="">点击</Btn>   通用按钮（OnClick 流）
<Icon name="ns:icon" color="" size="native"/>   项目级图标

## 自定义控件
注册后写法等同 <PascalCase .../>。

## 通用属性
id anchor size|width|height margin pivot padding spacing hidden interactable

## anchor
"<v>-<h>"  v ∈ {top,center,bottom,stretch}  h ∈ {left,center,right,stretch}
别名: center, stretch, fill

## 尺寸
size="WxH"  /  width="W"  /  height="H"     拉伸轴禁出现

## margin (向锚点内为正)
"X" | "V,H" | "T,R,B,L"   "_" = 占位

## 文本内容
<Btn>开始</Btn> 等价 <Btn text="开始"/>

## 模板插值 (仅 Template 内)
{{p}}            在属性值/文本中替换
if="{{p}}"       仅 truthy 时保留该元素
<Slot/>          注入子节点

## ID 路径
<D id="d"><B id="b"/></D>  →  screen.Get("d/b")

## Variant
内联:  attr.var="..."     (last-active-wins; 多个 .var 可并存; 整体替换, 不支持 _ 部分覆盖)
块:    <Variant when="var">
         <Add into="#id[/path/...]|@root" at="end|start|N">...</Add>
       </Variant>
不可带 .var: id, 标签名, <Param default>
```

---

## 12. 实施分期建议

每个 M 是一个独立 plan + PR 节奏。本 spec 仅交付到设计；实施计划由 writing-plans 流程产出。

| 阶段 | 内容 | 验收 |
|---|---|---|
| **M1 核心** | XML parser → Tree IR；6 原语；Screen Open/Close；`Get<T>`；自定义控件注册（`[UIAttr]` + `[Bind]`）；anchor/size/margin 系统 | 跑通"主菜单 + 三按钮 + 点击事件"完整闭环 |
| **M2 模板** | `<Template>` + `<Param>` + `<Slot>` + `{{}}` 替换 + `if=` | 用 TitledPanel 包背包 Grid |
| **M3 变体** | 内联 `attr.var`；块 `<Variant>/<Add>`；运行时切换重解算 | 同一 Screen 在 mobile-portrait 与 pc 间切换 |
| **M4.1** | `<Import>` parser + 循环检测 + 跨文件 Template 合并 | 单 Screen + 一层 Import 跑通 |
| **M4.2** | `LoadCommonLibrary` + 全局 commons 池 + `LoadDocumentFromSrc` + 依赖图 | bootstrap 后 Screen 文件能用 commons 模板 |
| **M4.3** | `as="ns"` 命名空间（commons / Import 一致语法） | conflict + namespace 用例覆盖 |
| **M4.4** | `UI.Reload` + `UI.ReloadCommonLibrary` + `HotReload.NotifyAssetChanged` | EditMode 测试模拟改文件触发 reload |
| **M4.5** | `PromptUGUI.Editor` asmdef + AssetPostprocessor + `UseResourcesResolver` + Sample 迁移 | 真 Editor 内改 .ui.xml 自动 reload |
| **M4.6** | XsdGenerator + 菜单 + snapshot 测试 | IDE 内自动补全可工作 |
| **M5 生态** | 内置 ScrollList / Toggle / Slider / Dropdown 自定义控件参考实现 | 用户零代码即可用上常用控件 |

---

## 13. 风险与开放问题

| # | 风险 / 问题 | 应对 |
|---|---|---|
| R1 | XML 解析性能（启动时大量描述文件） | 缓存 IR，预制实例池；M1 末做 profiling |
| R2 | Variant 重解算开销 | 仅刷被覆盖属性，不重建 GameObject；M3 末 profiling |
| R3 | LayoutGroup 与 Variant 同时驱动可能冲突 | 子节点在 Stack 内 `anchor.var` 同样视作非法（与 §6.5 一致），编译期警告 |
| R4 | 自定义控件 Prefab 与 description 字段对齐错误 | M1 提供注册期一次性校验：检查 `[UIAttr]` 标记的属性与 prefab 是否匹配 |
| R5 | ID 路径冲突（用户在 Screen 与 Template 内用同名 id） | 路径访问天然消歧；GetAll 返回所有匹配 |
| R6 | 跨文件 Template 同名冲突且双方都未 alias | 编译期硬报错，要求其一改名或 alias |
| R7 | Icon SpriteAtlas 4096 上限 | 同步工具 LogWarning；后续可加 split 策略 |
| R8 | `<Icon name="ui:{{x}}"/>` 动态名漏打包 | IconSet.alwaysInclude 兜底 |

---

_Spec 结束。下一步：用 writing-plans skill 把 M1 拆成可执行实施计划。_
