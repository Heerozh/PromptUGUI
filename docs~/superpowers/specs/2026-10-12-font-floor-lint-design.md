# 字号下限 lint —— `PUI-FONT-FLOOR`：按目标屏幕算出文字的实际大小（dp），低于项目下限报错

> 状态：**已实施**（§11 决策按推荐已定；分支 `feat/font-floor-lint`，实施记录见 §13）。
> 作者指示（2026-10-12）：`UI.DefaultScaleMode` 保持运行时 C# 设置、可按 Screen 混用，**不**进 settings；
> lint 对没写 `scale-mode` 的 Screen 按 `auto` 算。
> 相关：`2026-09-18-commons-settings-design.md`（settings 资产是 lint 与运行时共读的唯一声明源、`SettingsAssetReader`、
> CLI 自动发现）；`2026-08-26-theme-driven-style-design.md` §9（lint 的 raw / expanded 两遍与逐主题再推导遍）；
> XML skill「Canvas / scaler attributes on `<Screen>`」与 `scale="N"` / `"Nx"` / `"<r>r"` 三节。

## 1. 问题

作者在 XML 里写的 `fontSize` 是 **canvas 单位**，不是屏幕上的大小。屏幕上的大小还要乘上两样东西：
- **画布系数**：`reference="WxH"` 时 `Screen.cs:413` 的 Expand 公式 `min(屏宽/参考宽, 屏高/参考高)`，这个值逐设备不同；
- **祖先 `scale` 之积**：`Screen.cs:533-567` 的三种形态。

所以同一份 XML 的同一个字，在不同设备上大小不同：

| ref | 设备（逻辑尺寸） | 画布系数（dp/单位） | fontSize | 实际 |
|---|---|---|---|---|
| 1080x1920 | iPhone SE 375×667pt | 0.347 | 28 | **9.7dp** |
| 1080x1920 | iPhone 16 Pro Max 440×956pt | 0.407 | 28 | 11.4dp |
| 1920x1080 | 笔记本 1366×768 @1x | 0.711 | 16 | 11.4dp |
| 1920x1080 | 15" 1080p 笔记本 @150%（1280×720） | 0.667 | 16 | **10.7dp** |
| 1920x1080 | iPhone SE 横屏 667×375pt（没有给竖屏 / 手机另配 `reference.*`） | 0.347 | 16 | **5.6dp** |

作者在一台开发机上看着没问题，实际字太小的情况只会出现在别人的设备上。今天没有任何工具会把它指出来。
`<Toggle>` / `<Dropdown>` 的文字更是写死 14 号、没有 `fontSize` 可调（`ProceduralBuilders.AddText` 的默认值），在高分辨率的 `reference` 下几乎必然过小。

**要的是**：项目声明一个「最小可读字号」，用一个与设备无关的绝对单位；再声明一张「目标屏幕」清单。lint 对每个
`<Screen>` × 每个目标屏幕算出每段文字的实际大小，低于下限就报错，并给出修复值。

## 2. 单位：dp（视角单位）

下限用 **dp**，即 iOS pt ≈ Android dp ≈ Windows 有效像素 ≈ CSS px。它们本质上是**视角**单位：各平台按自己设备类别的典型观看距离
校准了「1 个逻辑单位 = 多少物理像素」（手机 160dpi 基准、桌面 96dpi 基准、电视 1080p 定为 2x），所以同一个 dp 数在手机、显示器、电视上
看起来差不多大。

| 设备 | 1 dp 的物理尺寸 | 观看距离 | 视角 |
|---|---|---|---|
| 手机（160dpi 基准） | ≈0.16mm | ~33cm | ≈0.027° |
| 24" 1080p 显示器 @1x | ≈0.28mm | ~60cm | ≈0.026° |
| 50" 1080p 电视 @2x（Android TV 的 960×540dp） | ≈1.15mm | ~2.75m | ≈0.024° |

平台规范给出的下限在 dp 下落在同一个区间。这是「一个数通用」的依据：

| 平台规范（Apple HIG Typography） | 原值 | 换算成本文的 dp |
|---|---|---|
| iOS / iPadOS 最小 | 11pt | 11dp |
| macOS 最小 | 10pt | ≈10dp |
| tvOS 最小 | 23pt（1080p 下 1pt = 1px） | 23px ÷ 2 = 11.5dp |

推荐值 **11dp**；电视 / 主机为主的项目可取 12dp。

否决的单位：

| 单位 | 为什么不 |
|---|---|
| canvas 单位（即 `fontSize` 本身） | 跟 `reference` 绑死，换一个 reference 下限就要重算，正是要解决的问题 |
| 物理毫米 / 印刷 pt（1/72 英寸） | 不含观看距离：手机上合适的 2mm 字在电视上看不清 |
| 叫它 `pt` | 已有三种 pt：印刷 pt、iOS pt、TMP Inspector 上标的 fontSize。字段统一叫 dp，tooltip 写清楚「= iOS pt」 |

## 3. 否决的方案

| 方案 | 为什么不 |
|---|---|
| **A. 只填一个「最小屏幕逻辑尺寸」，不要设备清单** | Expand 取 `min` 跨两轴，最坏设备取决于它和 reference 的宽高比，无法预先挑出一台；pixel 模式按物理像素取整，只有逻辑尺寸算不出来；报错也说不出是「哪台设备」 |
| **B. 读 UI Preview 的横竖屏尺寸（`UIPreviewSettings`）** | 那是每个人本机的设置，不进版本库；只有像素尺寸、没有系统缩放；一个方向只有一台设备 |
| **C. 运行时 Play 中按真实 `Screen.dpi` 告警** | 编辑器里的 dpi 是开发机显示器的，不是目标设备的；只覆盖当前这一台。列为非目标 |
| **D. 项目级缩放策略（`DefaultScaleMode` / `MinPixelScale` / `PixelScalePowerOfTwo`）搬进 settings，让 lint 看得见** | 作者否决：`DefaultScaleMode` 是运行时可混用的开关，不是项目级声明。lint 对没写 `scale-mode` 的 Screen 按 `auto` 算（§5.4） |
| **E. 规则挂进 `IRWalker`（raw 与 expanded 都跑）** | raw 遍看不到模板展开后的祖先链和 `class=` 从 import / commons 带来的 `fontSize`，会算出与展开遍不同的数，出现两条互相矛盾的 finding。只在展开遍跑（§5.6） |

## 4. 方案总览

```
PromptUGUI_Settings.asset
  fontFloor: 11                         ← dp；0 = 规则关闭
  fontFloorTargets:                     ← 目标屏幕：物理像素 @ 系统缩放（+ 可选的额外变体）
  - { name: iPhone SE,  screen: 750x1334@2x }
  - { name: TV 1080p,   screen: 1920x1080@2x, variants: tv }
        │
        ├─ Editor  Tools › PromptUGUI › Lint All UI XML     读 Instance + TMP_Settings.defaultFontSize
        └─ CLI     UIXmlLint                                 SettingsAssetReader（同一个资产）/ --font-floor / --target
                │
                ▼
        LintRun ──► DocumentLinter.Walk(..., fontFloor)
                        展开遍 + 逐主题遍 ──► FontFloorRules（Core/Lint，纯 C#）
                              每个 <Screen> × 每个目标：
                                激活变体 = {portrait|landscape（由宽高判定）} ∪ 目标的 variants
                                画布系数 f ← reference / scale-mode（CanvasScaleMath，与运行时共用）
                                逐节点 m = f × Π localScale（ScaleSyntax，与运行时共用）
                                文字节点：dp = fontSize × m ÷ 系统缩放
                              每个节点聚合各目标 → 最坏的那台 + 修复值 → PUI-FONT-FLOOR
```

谁管什么：

| 事 | 谁 |
|---|---|
| 下限与目标屏幕 | settings 资产，唯一声明源；CLI 参数可临时覆盖 |
| 画布系数、`scale` 三种形态的公式 | Core 纯 C#（`CanvasScaleMath` / `ScaleSyntax`），运行时 `Screen` 与 lint 共用一份 |
| 变体解析（last-active-wins） | Core 纯 C#（`VariantLookup`），运行时 `VariantResolver` 委托给它 |
| 每个控件的字号属性与默认值 | `FontFloorRules` 里的一张表（§5.5），与控件源码一一对应，有测试锁住 |
| `<Text>` 不写 `fontSize` 时的默认值 | 前端提供：Editor 读 `TMP_Settings.defaultFontSize`；CLI 读 `TMP Settings.asset`，找不到按 TMP 出厂值 36 |

## 5. 语义细节

### 5.1 settings 字段

```csharp
// Runtime/Core/IR/FontFloorTarget.cs —— 纯 C#（System.SerializableAttribute），CLI 编译集内
[Serializable]
public sealed class FontFloorTarget
{
    public string name;      // finding 里的名字："iPhone SE"；空 → 用 screen 原文
    public string screen;    // "750x1334@2x"：画布的物理像素 @ 系统缩放（px / dp）
    public string variants;  // 可选：额外激活的变体，空格或逗号分隔（"mobile tv"）
    public bool IsBlank => string.IsNullOrWhiteSpace(screen);
}

// Runtime/Application/PromptUGUISettings.cs
[Tooltip("Smallest readable text, in dp (= iOS pt ≈ Android dp ≈ CSS px ≈ Windows effective px). " +
         "Lint-only. 0 turns PUI-FONT-FLOOR off. 11 matches Apple's iOS minimum.")]
public float fontFloor = 0f;

[Tooltip("The screens PUI-FONT-FLOOR checks every <Screen> against: physical pixels @ OS scale, e.g. " +
         "750x1334@2x for an iPhone SE in portrait. Orientation follows the aspect (taller = portrait).")]
public List<FontFloorTarget> fontFloorTargets = new();
```

- **默认关闭**（`fontFloor = 0`），已有项目升级后不会突然多出一批报错。
- `fontFloor > 0` 但没有有效目标时规则不生效。Inspector 显示 HelpBox；`LintRun` 每次运行出一条 Note（§5.7）。
- `OnValidate`（沿用 commons 的查错风格）：`screen` 语法错 → `LogError`（指出第几行）；`variants` 里写了 `portrait` / `landscape` →
  `LogError`（方向由宽高决定，不能手写）；`fontFloor < 0` → 归零。空白行保留在 Inspector 里，lint 忽略。

### 5.2 `screen` 语法：`WxH[@D[x]]`

- `W` / `H`：画布的**物理像素**，正数；按游戏实际运行的方向写（竖屏游戏写 `750x1334`，同一台手机横屏另写一行 `1334x750`）。
- `D`：系统缩放，即每 dp 多少物理像素，正数，缺省 1。末尾的 `x` 可写可不写：`@2x` = `@2`，`@1.5x` = `@1.5`。
  - iOS：`UIScreen.scale`（@2x / @3x）；Android：`density`（xhdpi = 2、xxhdpi = 3、2.625 …）；
    Windows / macOS：显示缩放（125% = 1.25、Retina = 2）；电视：1080p 取 2、4K 取 4（Android TV 惯例）。
- **方向**：`H > W` → `portrait`，否则 `landscape`（与 `OrientationTracker.Apply` 同一判定，正方形算 landscape）。
- 语法错的文案：`fontFloorTargets[2] screen="750*1334": expected WxH[@D] (e.g. "750x1334@2x")`。
- 解析器 `FontFloorTargetSyntax.Parse(string) → (float W, float H, float D)?`，放在 `Core/Lint`，settings 的 `OnValidate`、
  `LintRun`、CLI 共用。

一行就是一种被检查的配置，finding 用这一行的名字。需要横竖两向都查的设备写两行，不提供「自动旋转」开关（§11-4）。

### 5.3 Core 抽取（纯重构，运行时与 lint 共用一份实现）

| 新位置（CLI 编译集内） | 内容 | 运行时改为 |
|---|---|---|
| `Core/IR/VariantLookup.cs` | `Resolve(ElementNode, attr, Func<string,bool> isActive)` → 值；重载另外返回命中的变体名（null = 基础值）。last-active-wins，原样照搬 `VariantResolver.cs` | `VariantResolver.ResolveAttribute` 委托：`VariantLookup.Resolve(node, attr, store.IsActive)` |
| `Core/Parser/ScaleSyntax.cs` | 解析 `scale` 值：`Nx`（正整数）/ `<r>r`（正浮点）/ 普通正浮点 / 其它（含 `<Animation scale="1:0.5">`）→ 单位 1；`LocalScale(spec, f)`：`N/f`、`max(1, floor(f·r+0.5))/f`、`v`、`1` | `Screen.TryParseDeviceScale` / `TryParseRelativeScale` 和 `ApplyScaleToNode` 里的三段公式（`Screen.cs:533-567`、`:638-658`）改为调用它 |
| `Core/Parser/CanvasScaleMath.cs` | `ExpandFactor(sw, sh, rw, rh)` = `min(sw/rw, sh/rh)`；`PixelFactor(sw, sh, rw, rh, powerOfTwo, minPixelScale)` = `PixelScaleSolver.Solve` 的阶梯 + `MinPixelScale` 下限 | `Screen.ApplyAuto` 里的 `_canvasFactor`（`:413`）与 `ApplyPixel`（`:436-439`）改为调用它；`PixelScaleSolver` 删除，测试改指向新类 |

放 `Core/Parser` 的理由：它已经存放运行时与 CLI 共用的值语义（`ReferenceSyntax`、`RadiusParser`、`GlassAttrParser`），不需要
扩大 CLI 编译集，AGENTS.md「四个目录」的约束不变。`Mathf` 换成 `System.Math`，`Vector2` 换成两个 float。

### 5.4 计算：每个 `<Screen>` × 每个目标

对展开后文档的每个 `ScreenDef`：

1. `CanvasMode.World` → 整个 Screen 跳过（世界空间画布没有「屏幕上的大小」）。`overlay` / `camera` 照算。
2. 对每个有效目标 `t = (W, H, D, 方向, 额外变体)`：
   - **激活集** = `{方向}` ∪ `t.variants`。代码里按条件激活、但没写进 `t.variants` 的变体，lint 视为未激活。
   - **`scale-mode`** = 在激活集下解析根节点的 `scale-mode`；没写 → **`auto`**（作者指示）。运行时 C# 的 `UI.DefaultScaleMode` lint 看不见。
     这是有意的：混用的项目应在需要 pixel 的 Screen 上显式写 `scale-mode="pixel"`。
   - **`reference`** = 在激活集下解析根节点的 `reference`（`ReferenceSyntax`）。
   - **画布系数 f**（每 canvas 单位对应的物理像素）：

     | 条件 | f | 依据 |
     |---|---|---|
     | 没有 reference（auto 或 pixel） | 1 | `ApplyAuto` 的 ConstantPixelSize；`ApplyPixel` 的 LogError 退化 |
     | auto + reference | `ExpandFactor(W, H, rw, rh)` | `Screen.cs:405-413` |
     | pixel + reference | `PixelFactor(W, H, rw, rh, powerOfTwo: false, minPixelScale: 0)` | `Screen.cs:433-439`。`UI.PixelScalePowerOfTwo` / `UI.MinPixelScale` 是运行时 C#，lint 取默认值（§9） |

   - **逐节点倍率**：从根往下走，`m(节点) = f × Π localScale(祖先及自身)`，其中 `localScale` = 在激活集下解析出的 `scale`，再经
     `ScaleSyntax.LocalScale(spec, f)` 换算。与运行时 `ApplyScaleToNode` 同一个公式，所以 `scale="2x"` 得到的 dp
     与 reference 无关，`"<r>r"` 的取整也一致。
   - **文字节点**（§5.5）：`dp = fontSize × m ÷ D`。
3. 遍历范围是运行时在该激活集下**真正会建出来**的那棵树：
   - 展开树本身。`if="false"` 已被展开器剔除；`hidden` 节点、`<Pages>` 的非当前页**照查**，它们随时会显示。
   - **激活变体的 `<Add>`**：对 `<Variant when>` 在激活集内的块，把 `Add.Children` 挂到 `IntoPath` 指向的节点下面，继承该节点的倍率。
     `IntoPath` 的解析是 `ScreenInstantiator.ResolveAddTarget`（`:146-175`）的 IR 版：`@root` 指根；`#a/b/c` 的首段在 Screen 作用域找 id
     （不进入模板实例内部，实例根自己的 id 属于外层），后续各段在上一段所指实例的作用域内找。找不到 → 这块 Add 跳过（运行时那里会抛，由别处报）。
   - **`itemTemplate` 行**：`<ScrollList>` / `<TabBar>` / `<Carousel>` 在激活集下解析出 `itemTemplate` 时，取 `ScreenDef.Templates[名字]`
     （`TemplateExpander.BuildRuntimeTemplates` 预展开的那份，`TemplateExpander.cs:118`）。`BodyExpanded` 时把行体当作该节点的子树来走，
     继承宿主的倍率；有必填 `<Param>` 而没展开的模板跳过。列表行恰恰是小字最多的地方。
   - 没有被任何 Screen 调用的模板体不查：没有 Screen 就没有画布系数。

### 5.5 哪些节点有字、字号从哪来

| 标签 | 字号来源 | 没写时的默认值（源码位置） | 什么时候算「有字」 |
|---|---|---|---|
| `<Text>` | `fontSize` | TMP 默认字号，由前端提供（`TmpPrimer.cs:49`） | 总是（内容可能由 C# 填） |
| `<Btn>` | `fontSize` | 24（`Btn.cs:159`） | 声明了 `text`（含文本内容写法）或 `fontSize` |
| `<Tab>` | `fontSize` | 24（`Tab.cs:104`） | 声明了 `text` 或 `fontSize` |
| `<Toggle>` | `fontSize`（**本案新增**，§5.8） | 14（`ProceduralBuilders.AddText`） | 声明了 `text` 或 `fontSize` |
| `<InputField>` | `fontSize`（经 `pointSize`，输入文字与 placeholder 同号） | 14 | 总是 |
| `<Collapsible>` | `fontSize`（标题） | 14（`Collapsible.cs:41`） | 声明了 `text` 或 `fontSize` |
| `<TabMenu>` | `fontSize`（把手标题） | 24（`TabMenu.cs:39`） | 总是（标题显示当前选中项） |
| `<Dropdown>` | 无属性，标题与选项**固定 14** | 14（`Dropdown.cs:59`、`:146`） | 总是 |
| `<Markdown>` | `fontSize`（正文；标题经 `scale` 放大，永远不小于正文） | 16（`MarkdownStyle.cs:11`） | 总是 |

- 字号按激活集解析（`fontSize.portrait` 等变体覆盖照常生效）；值无法解析成正数（未替换的 `{{…}}`、空串）→ 该节点跳过，由别的规则报。
- 已知看不到的：C# 里动态设置的字号、TMP 富文本 `<size=…>`、Markdown 内容本身（运行时才有）、Btn / Tab 只由 C# 设置文字的情况。
- 这张表在 `FontFloorRules` 里是一张静态表。§8-9 用真控件逐个比对默认值，控件改了默认字号而表没跟上，测试会红。

### 5.6 报告：聚合、文案、修复值、主题遍

- **只在展开遍和逐主题遍跑**，由 `DocumentLinter.Walk` 在 `IRWalker.Walk(expanded)` 之后直接调用 `FontFloorRules`（§3-E）。
  展开遍被跳过的文档（import 解析不到）不出这条 finding：`LintRun` 已经出过「skipping expanded pass」的 Note。
- **每个节点一条**：对所有目标聚合，取 dp 最小的那台作主报告，其余低于下限的目标附在后面。dp 与 fontSize 成正比、倍率与 fontSize 无关，
  所以最坏的那台给出的修复值对所有目标都够用。
- 比较：`dp < fontFloor - 1e-4`。显示保留 1 位小数。修复值 = `ceil(fontFloor ÷ (m/D) - 1e-4)`，取整数。
- 文案（英文，与其它规则一致）：

  ```
  <Text id='hint'>: fontSize 28 renders at 9.7dp on 'iPhone SE' (750x1334@2x portrait; canvas ×0.694 from reference 1080x1920)
  — below the 11dp floor. Needs fontSize ≥ 32. Also below on 'iPhone 16' (10.2dp).
  ```

  （iPhone SE：0.694 ÷ 2 = 0.347dp/单位 → 9.7dp，11 ÷ 0.347 = 31.7 → 32；iPhone 16 `1179x2556@3x`：min(1.092, 1.331) ÷ 3 = 0.364 → 10.2dp。）

  - 字号来源会标出来：变体覆盖写成 `fontSize.portrait 20`，来自样式包写成 `fontSize 20 (class="caption")`（依据 `ElementNode.StyleAttrNames`），
    用的是默认值写成 `default fontSize 24`。作者因此知道该去哪里改。
  - 祖先链里有 `scale` 时，括号里加一段 `scale ×0.5`。
  - `<Dropdown>` 没有可改的字号，修复值换成 scale：`Dropdown text is a fixed 14 (no fontSize attribute); scale this control: needs scale ≥ 1.6.`
    算法是在本节点当前 `scale` 的基础上乘上缺口倍数。
- **模板**：模板体内的 finding 按 `OriginSrc` + 行号报到模板文件，并带 `(via 调用处)`（`IRWalker` 已有的约定）。多个实例数字相同时，
  `DocumentLinter.KeyOf` 去重成一条；某个实例处在不同的 `scale` 下、数字不同，就单独一条，via 能区分它们。
- **主题遍**：`FontFloorRules.Evaluate(expanded, policy)` 返回「节点 → 结论」。基础遍的结论全部报告；逐主题遍只报结论**与基础遍不同**的节点，
  文案尾部加 ` (under <Theme name='dark'>)`。主题没改字号的项目不会多出任何一条；主题把某个样式包的字号改小了，作者能看到是哪个主题造成的。
- 错误码 `PUI-FONT-FLOOR`，`LintRun.Kind.Issue`，计入 CLI 的非零退出码。**不**接进 `ScreenInstantiator` 的 warning 路径：运行时只知道当前这一台设备。

### 5.7 前端

**`Core/Lint/FontFloorPolicy.cs`**（纯 C#）：

```csharp
public sealed class FontFloorPolicy
{
    public float Floor;                                // dp
    public IReadOnlyList<FontFloorTarget> Targets;     // 原始行；由 LintRun 校验、解析
    public float TextDefaultSize = 36f;                // <Text> 不写 fontSize 时
}
```

- `DocumentLinter.Walk(doc, entrySrc, imports, commons, FontFloorPolicy fontFloor = null)`：尾部可选参数，现有调用不改也能编译。
- `LintRun`：新增 `public FontFloorPolicy FontFloor { get; set; }`（每次运行一份）。第一次 `Lint` 时校验一次：语法错的行 → 每行一条
  `Kind.Error`（`File = null`，同 commons 非法 `as` 的处理）；`variants` 含 `portrait` / `landscape` → Error；`Floor > 0` 且无有效行 →
  一条 Note：`fontFloor is 11dp but no fontFloorTargets are configured — PUI-FONT-FLOOR is inactive`。
- **Editor 菜单**（`UIXmlLintMenu`）：`PromptUGUISettings.Instance` 的 `fontFloor` / `fontFloorTargets`。`TextDefaultSize` 取
  `TMP_Settings.defaultFontSize`；TMP Essentials 没导入、`TMP_Settings.instance == null` 时取 36。
- **CLI**（`.lint/UIXmlLint`）：
  - 默认从已发现的 settings 资产读（`SettingsAssetReader.ReadFontFloor(yaml) → (float floor, List<FontFloorTarget> targets)`，
    与 `ReadCommonLibraries` 同一种「只认 Unity 文本序列化固定形态」的读法，内部的列表读取泛化成按键名读平铺对象列表）。
  - `--font-floor <dp>`：覆盖下限；`--font-floor 0` 关闭规则。
  - `--target <WxH@D>[,<variant>…]`：可重复；只要给了一个，就**整体替换** settings 里的目标表。名字取 `screen` 原文。
  - `--text-default-size <n>`：覆盖 `<Text>` 的默认字号。不给时，在 settings 资产所在的 `Assets/` 下找 `TMP Settings.asset`，读 `m_defaultFontSize:`，
    找不到用 36。
  - usage 与 README 同步。

### 5.8 `<Toggle fontSize>`（新增属性）

`<Toggle>` 的 label 今天写死 14（`Toggle.cs:93` → `AddText`）。lint 指出它太小以后，作者在 XML 里却没有能改的属性，用 `scale` 又会把勾选框一起放大。
所以补上：

```csharp
[UIAttr("fontSize"), Preserve]
public float FontSize { set => _label.fontSize = value; }
```

- 默认值不变（14）。可用变体覆盖、可走样式包，与 `<Btn>` / `<Tab>` 相同。XSD 由 `[UIAttr]` 反射生成，自动包含。
- `<Dropdown fontSize>` **不在本案**：选项行高（`Dropdown.cs:116` 固定 20）、勾选标记、弹窗高度都要跟着字号变，是一次控件改造。
  lint 对它给出 `scale` 修复值（§5.6），改造另立案（§10）。

### 5.9 Inspector

`PromptUGUISettingsEditor` 新增一段 **Font Floor**：

- `Floor (dp)` 浮点字段；HelpBox 写明单位（「= iOS pt ≈ Android dp ≈ CSS px；11 = Apple 的 iOS 最小值」），并提示 0 = 关闭。
- `Targets` 列表。
- **Add preset ▾** 按钮：弹出菜单，手机 / 平板的预设分「Portrait / Landscape」两个子项（横屏行自动交换宽高，名字加 ` (landscape)`）：

  | 预设 | screen | 逻辑尺寸 |
  |---|---|---|
  | iPhone SE | `750x1334@2x` | 375×667pt |
  | iPhone 16 | `1179x2556@3x` | 393×852pt |
  | iPhone 16 Pro Max | `1320x2868@3x` | 440×956pt |
  | Android 360dp | `1080x2400@3x` | 360×800dp |
  | iPad mini | `1488x2266@2x` | 744×1133pt |
  | Laptop 1366×768 | `1366x768@1x` | 1366×768 |
  | Laptop 1080p @150% | `1920x1080@1.5x` | 1280×720 |
  | Desktop 1080p | `1920x1080@1x` | 1920×1080 |
  | TV 1080p | `1920x1080@2x` | 960×540dp |
  | TV 4K | `3840x2160@4x` | 960×540dp |

- `fontFloor > 0` 且表为空时，HelpBox（Warning）：`PUI-FONT-FLOOR is inactive until at least one target is listed.`

## 6. 公开 API / XML 变化

| 项 | 今天 | 之后 |
|---|---|---|
| `PromptUGUISettings.fontFloor` / `fontFloorTargets` | — | 新增（lint 专用，运行时不读） |
| `PromptUGUI.IR.FontFloorTarget` | — | 新增（Core，纯 C#） |
| `<Toggle fontSize>` | — | 新增 |
| lint 错误码 | — | `PUI-FONT-FLOOR` |
| UIXmlLint CLI | — | `--font-floor` / `--target` / `--text-default-size` |
| `UI.DefaultScaleMode` / `MinPixelScale` / `PixelScalePowerOfTwo` | 公开 C# | **不变**（作者指示） |
| `PixelScaleSolver`（internal） | `Application/` | 删除，由 `Core/Parser/CanvasScaleMath` 取代 |

## 7. 实现地图

| 文件 | 内容 |
|---|---|
| `Runtime/Core/IR/VariantLookup.cs`（新） | §5.3 |
| `Runtime/Core/Variants/VariantResolver.cs` | 委托 `VariantLookup` |
| `Runtime/Core/Parser/ScaleSyntax.cs`（新） | §5.3 |
| `Runtime/Core/Parser/CanvasScaleMath.cs`（新） | §5.3 |
| `Runtime/Application/Screen.cs` | `ApplyAuto` / `ApplyPixel` / `ApplyScaleToNode` / `DeclaresFactorScale` 改为调用 Core |
| `Runtime/Application/PixelScaleSolver.cs` | 删除 |
| `Runtime/Core/IR/FontFloorTarget.cs`（新） | §5.1 |
| `Runtime/Core/Lint/FontFloorTargetSyntax.cs`（新） | §5.2 |
| `Runtime/Core/Lint/FontFloorPolicy.cs`（新） | §5.7 |
| `Runtime/Core/Lint/FontFloorRules.cs`（新） | §5.4–5.6：遍历、Add 挂载、itemTemplate、字号表、聚合、文案 |
| `Runtime/Core/Lint/DocumentLinter.cs` | `Walk(..., fontFloor)`：展开遍后调用、主题遍取差异 |
| `Runtime/Core/Lint/LintRun.cs` | `FontFloor` 属性；目标表校验（Error / Note 各一次） |
| `Runtime/Core/Lint/SettingsAssetReader.cs` | `ReadFontFloor`；列表读取泛化 |
| `Runtime/Application/PromptUGUISettings.cs` | 两个字段 + `OnValidate` |
| `Runtime/Controls/Toggle.cs` | `fontSize`（§5.8） |
| `Editor/PromptUGUISettingsEditor.cs` | Font Floor 段 + 预设菜单 |
| `Editor/UIXmlLintMenu.cs` | 构造 `FontFloorPolicy`（settings + `TMP_Settings`） |
| `.lint/UIXmlLint/Program.cs` / `README.md` | 参数、TMP 默认字号发现、usage |
| `Tests/EditMode/Lint/FontFloorRulesTests.cs`（新） | §8-1~18 |
| `Tests/EditMode/Lint/FontFloorTargetSyntaxTests.cs`（新）、`SettingsAssetReaderTests.cs`、`LintRunTests.cs` | §8-19~22 |
| `Tests/EditMode/Application/PixelScaleSolverTests.cs` → 改名 `CanvasScaleMathTests.cs`（用例原样保留，改调新类）；`ScreenScaleModeTests.cs` 里对 `PixelScaleSolver` 的引用同改；`ScaleSyntaxTests.cs`、`VariantLookupTests.cs`（新） | §8-23~25 |
| `Tests/EditMode/Controls/ToggleTests.cs` | §8-26 |
| `Tests/EditMode/Editor/SettingsAssetRoundTripTests.cs`、`UIXmlLintMenuTests.cs` | §8-27~28 |

## 8. 测试（Red first）

`FontFloorRulesTests`（解析内存 XML → 展开 → `DocumentLinter.Walk(..., policy)`；数字取 §1 的表）：

1. auto、`reference="1080x1920"`、目标 iPhone SE `750x1334@2x`、`<Text fontSize="28">` → 一条 `PUI-FONT-FLOOR`，文案含 `9.7dp`、`Needs fontSize ≥ 32`。
2. 同上，只有 Pro Max `1320x2868@3x` → 无。
3. `reference="1920x1080" reference.portrait="1080x1920"`：竖屏目标用 portrait 那个，横屏目标用基础值。
4. `fontSize="16" fontSize.portrait="28"`：竖屏目标按 28 算，横屏按 16，文案分别写 `fontSize.portrait 28` / `fontSize 16`。
5. 祖先 `scale="0.5"` 让 dp 减半，文案含 `scale ×0.5`。`scale="2x"` 在不同 reference 下 dp 相同。`"<r>r"` 的取整与 `Screen` 一致（同一组输入断言等于 `ScaleSyntax.LocalScale`）。
6. `scale-mode="pixel"`、`reference="320x180"`、目标 `1920x1080@1x` → f = 6；目标 `1366x768@1x` → f = 4（floor 4.27）。小于 1 时阶梯取 0.5 / 0.25。
7. 没有 reference → f = 1：`fontSize="14"`、目标 `@2x` → 7dp。
8. `canvas="world"` → 无。
9. 字号表：没写 `fontSize` 的 `<Btn text="OK">` 按 24；没写 text 也没写 `fontSize` 的 `<Btn>` → 无；`<Dropdown>` 按 14，修复值是 `scale ≥ …`；
   没写 `fontSize` 的 `<Text>` 按 `policy.TextDefaultSize`；`<Markdown>` 默认 16；`<TabMenu>` 默认 24。另起一个 EditMode 用例 Open 真控件、
   读 TMP 组件的 `fontSize`，逐个与表里的默认值比对。
10. 模板体里的 `<Text>`：finding 的 `Origin` 是模板文件、`Via` 是调用处。两个数字相同的实例 → 一条；其中一个在 `scale="0.5"` 下 → 两条。
11. `class="caption"` 带来的 `fontSize` → 文案含 `(class="caption")`。
12. 主题：`<Theme name="dark">` 把 `caption` 的字号改小 → 基础遍一条、dark 遍一条，后者带 `(under <Theme name='dark'>)`；主题不动字号 → 只有一条。
13. `<Variant when="mobile"><Add into="#panel">` 里的 `<Text>`：只有 `variants: mobile` 的目标查它，倍率继承 `#panel` 的 `scale`；`into="#card/body"` 能穿过模板实例作用域；目标路径不存在 → 跳过、不抛。
14. `<ScrollList itemTemplate="Row" scale="0.8">`：行体里的 `<Text>` 按 0.8 算；`Row` 有必填 Param → 跳过。
15. 聚合：两台目标都不达标 → 一条，主报告是 dp 更小的那台，`Also below on` 列出另一台。
16. 恰好等于下限（11.0dp）→ 不报。
17. `policy == null`、`Floor == 0`、目标表为空 → 都不报。
18. 有 `<Import>` 但 `imports == null`（展开遍被跳过）→ 不出这条 finding、不抛；raw 遍不出这条 finding。

语法 / 读取 / 前端：

19. `FontFloorTargetSyntax`：`750x1334@2x`、`1920x1080`、`1920x1080@1.5`、`1920x1080@1.5x` 都能解析；`750*1334`、`0x10`、`10x10@0`、`10x10@` 报错。方向：`H > W` 为 portrait，正方形为 landscape。
20. `SettingsAssetReader.ReadFontFloor`：手写 YAML（`fontFloor: 11`、两行目标、`variants` 为空、带引号的 name、`fontFloorTargets: []`、字段不存在）都能正确读出。
21. `LintRun`：一行语法错 → 一条 Error，且整个运行只出一次（lint 两个文件）；`variants: portrait` → Error；有下限没目标 → Note 只一次。
22. CLI 参数优先级在 `Options` 层测（`--target` 整体替换、`--font-floor 0` 关闭）。CLI 没有测试程序集，把解析逻辑放在可测的位置，或者手工验证 §8-29。

Core 抽取（纯重构，先跑全绿）：

23. `CanvasScaleMathTests`：原 `PixelScaleSolver` 的全部用例 + `ExpandFactor` + `minPixelScale` 下限。
24. `ScaleSyntaxTests`：三种形态 + 非法值 → 1。
25. `VariantLookupTests`：last-active-wins、没有激活的覆盖时回退基础值、返回命中的变体名。`ScreenScaleModeTests` 等既有运行时测试原样全绿。

控件 / Editor：

26. `<Toggle text="x" fontSize="22">` → label `fontSize == 22`；`fontSize.portrait` 随变体切换；不写时仍是 14。
27. EditorOnly：`SettingsAssetRoundTripTests` 补 `fontFloor` / `fontFloorTargets` 往返。
28. EditorOnly：`UIXmlLintMenu.Lint` 带 policy：临时目录里放一个违规文档 → 报出 `PUI-FONT-FLOOR`；`TextDefaultSize` 取自 `TMP_Settings`。

手工验证（宿主工程，Unity MCP）：

29. settings 里填 11dp + iPhone SE（竖）+ Laptop 1080p @150% → `Tools › PromptUGUI › Lint All UI XML`，看 Console 的 finding 能否点击定位；CLI 跑同一个目录，结果一致。
30. 按 finding 给的修复值改完 → 再跑一遍，为零。

## 9. lint 看不见的（写进 skill）

| 看不见的 | lint 的取值 | 偏差方向 |
|---|---|---|
| `UI.DefaultScaleMode`（C# 设置） | 没写 `scale-mode` 的 Screen 按 `auto` 算 | 运行时实际走 pixel 时，pixel 阶梯 ≤ Expand 系数，所以 lint 偏乐观。用 pixel 的 Screen 请显式写 `scale-mode="pixel"` |
| `UI.MinPixelScale` | 0（不设下限） | 运行时设了下限会把系数抬高，lint 偏保守（可能多报） |
| `UI.PixelScalePowerOfTwo` | false | 运行时开了会把系数压低，lint 偏乐观 |
| `UI.CanvasConfigurator` 改了 `screenMatchMode` / `matchWidthOrHeight` | 按 Expand 算 | 视改法而定 |
| C# 按条件激活的变体 | 只认目标行 `variants` 里列出的 | — |
| `UI.Orientation.AutoTrack = false` 自管方向 | 仍按宽高判定 | — |
| C# 动态字号、富文本 `<size>`、Markdown 内容、Carousel 的 `edgeScale`、动画中的缩放 | 不计 | — |
| 安全区 / 刘海 | 不计（画布仍是整屏） | — |

## 10. 非目标

- 运行时（Play 中）按当前设备告警（§3-C）。
- `<Dropdown fontSize>`：需要一起改选项行高与弹窗尺寸，另立案；在那之前 lint 给 `scale` 修复值。
- 按 locale / 字体类型分别设下限（CJK 笔画多，可能要更高）：v1 只有一个数；需要时在 `fontFloor` 之外加按 `fontTypes` 的覆盖表。
- UI Preview 复用目标表切 Game view 尺寸：数据形态已经兼容（像素 + 缩放），另案。
- 检查「行高 / 触控目标尺寸」等其它可读性 / 可用性下限：同一套 dp 计算可以复用，另案。

## 11. 决策（按推荐已定）

| # | 项 | 定为 | 备选 |
|---|---|---|---|
| 1 | 单位 | dp（视角单位） | 毫米 / 印刷 pt / canvas 单位（§2） |
| 2 | 目标的写法 | 物理像素 @ 系统缩放（`750x1334@2x`）：auto 只用到逻辑尺寸，但 pixel 的整数阶梯要用物理像素 | 只写逻辑尺寸（pixel 模式算不了） |
| 3 | 项目缩放策略 | **作者已定**：`DefaultScaleMode` 等留在 C#，lint 对没写 `scale-mode` 的按 auto，pixel 的两个旋钮取默认值 | 搬进 settings（§3-D，已否决） |
| 4 | 横竖屏 | 一行一个方向，按宽高判定 | 每行加 `rotate` 开关，自动查两向 |
| 5 | 规则跑在哪一遍 | 只在展开遍 + 主题遍；主题遍只报差异 | raw 遍也跑（§3-E） |
| 6 | 聚合粒度 | 每个节点一条，报最坏的目标并附带其余 | 每个节点 × 每个目标一条（刷屏） |
| 7 | `<Toggle fontSize>` | 本案做 | lint 只给 `scale` 修复值 |
| 8 | `<Dropdown fontSize>` | 另案；lint 给 `scale` 修复值 | 本案一起改控件 |
| 9 | `<Text>` 默认字号 | 前端提供：Editor 读 `TMP_Settings`；CLI 读 `TMP Settings.asset`，否则 36 | 固定按 36 / 不写字号的 `<Text>` 跳过 |
| 10 | 默认是否开启 | 关（`fontFloor = 0`） | 默认 11 + 一组预设目标 |
| 11 | Inspector 预设 | 做（§5.9 的表） | 只有文档里的表 |

## 12. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| M0 | Core 抽取：`VariantLookup` / `ScaleSyntax` / `CanvasScaleMath`，运行时改为委托，删除 `PixelScaleSolver`（§8-23~25） | EditMode / PlayMode 全绿；`dotnet build .lint/UIXmlLint` 通过 |
| M1 | 规则：`FontFloorTarget` + 语法 + `FontFloorPolicy` + `FontFloorRules` + `DocumentLinter` 接入（§8-1~19） | EditMode 全绿 |
| M2 | 前端：settings 字段 + `OnValidate` + Inspector 段与预设 + `SettingsAssetReader` + `LintRun` 校验 + Editor 菜单 + CLI 参数与 TMP 默认字号（§8-20~22、27、28） | EditorOnly 全绿；§8-29、30 |
| M3 | `<Toggle fontSize>`（§8-26）+ 文档（下） | lint / format 通过，PR |

文档（同一 PR，英文）：

- `authoring-promptugui-xml/SKILL.md`：在「Canvas / scaler attributes on `<Screen>`」之后新增 **Font-size floor (`PUI-FONT-FLOOR`)** 一节
  （dp 是什么、公式、目标行写法、§9 的盲区表、修复思路：调大 `fontSize` / 加竖屏 `reference.portrait` / 用 `scale="Nx"`）；
  `<Toggle>` 属性行补 `fontSize`；错误码 / lint 列表补 `PUI-FONT-FLOOR`。
- `scripting-promptugui-csharp/SKILL.md`：Tooling 段补一句「settings 的 Font Floor 段喂 lint」；`DefaultScaleMode` 那段补一句
  「lint 看不到这个开关，用 pixel 的 Screen 请显式写 `scale-mode`」。
- `.lint/UIXmlLint/README.md`：Font floor 一节（参数、TMP 默认字号发现、与 settings 的优先级）。
- `AGENTS.md`：UIXmlLint 段补一句 commons 之外 settings 还提供 font floor。

## 13. 实施记录（2026-10-12，分支 `feat/font-floor-lint`）

M0（`2656256`）、M1（`214718e`）、M2（`5584db8`）、`<Toggle fontSize>`（`7381ea0`）与文档已落地。与上文的出入：

- **`SettingsAssetReader` 拆成三个方法**：`ReadFontFloor(yaml) → float`、`ReadFontFloorTargets(yaml) → List<FontFloorTarget>`、
  `ReadTmpDefaultFontSize(yaml) → float?`（§5.7 写的是一个返回元组的 `ReadFontFloor`）。列表读取泛化成按键名读平铺对象列表，
  `ReadCommonLibraries` 改走同一个实现，行为不变。
- **CLI `--target` 多认一种写法 `[name=]WxH@D[,variant…]`**：不给名字时仍以屏幕串作名。名字与屏幕串相同时，finding 里只写一次屏幕串
  （`on '750x1334@2x' (portrait; …)`），不再重复。
- **settings 资产在给了 `--commons` 时也会读**（字号下限要用）；以前 `--commons` 时完全不碰资产。显式 `--settings` 读不了仍是 exit 2，
  自动发现的读不了仍视为「没有 settings」。
- **「没有可用目标」的 Note 文案**改为 `no usable fontFloorTargets are configured`：行都写坏了时，原文案「没有配置」会误导。
- **Editor 读 TMP 默认字号时先 `Resources.Load<TMP_Settings>("TMP Settings")`**：`TMP_Settings.instance` 在 essentials 没导入时会弹
  TMP 的导入窗口，lint 不该弹窗；取不到按 36。
- **主题遍的比较键**是 `Origin:Line|Via|Code|Tag|Id|Message`（`DocumentLinter.PlacedKeyOf`），即「同一处、同一结论」；§5.6 说的「节点 →
  结论」字典没有用上：消息里已经带着全部数字，按位置比消息就够了，也自然覆盖了同一模板体被多次实例化的情形。
- **规则内部按「节点 + itemTemplate 宿主链」聚合各目标的读数**（`VisitKey`）：同一份行模板挂在两个缩放不同的列表下时，是两次独立的访问。
- 验证（ssw_re_client，Unity 6000.7.0b3，Unity MCP）：EditMode 5020 / EditorOnly 466 / PlayMode 265 全绿；
  `dotnet build .lint/UIXmlLint`、`dotnet format --verify-no-changes`（whitespace / style / analyzers）通过。
  CLI 实跑：包自带的 `Runtime/Resources/` 在 `--font-floor 11 --target 750x1334@2x --target 1920x1080@1.5x` 下报 18 条 ——
  **内置 modal（MessageBox / InputBox / MarkdownBox / CenteredSlideBox）与 TutorialOverlay 在 iPhone SE 竖屏上全部低于 11dp**
  （它们的竖屏 reference 是 1080x1920，正文 18、标题 24、按钮默认 24），属于库自身的默认皮肤问题，另案处理；
  ssw_re_client 在 `Laptop 150%=1920x1080@1.5x` + `Laptop=1366x768` 下报 18 条（`Round.ui.xml` 的 6 号字，reference 640x360 → ×3 → 9.5dp）。
  §8-29、30 的 Unity 菜单手工验证、Inspector 外观未做（Inspector 只确认了绘制无异常）。
