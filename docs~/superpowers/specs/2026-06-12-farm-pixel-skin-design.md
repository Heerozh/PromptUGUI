# 种田风像素默认皮肤（farm-pixel-skin）设计

日期：2026-06-12
分支：`feat/common-controls-sample-expansion`（与演示扩充同一交付物）
前置：`.pxl` 像素导入管线（Editor/Pxl，PR #70 lineage，已在本分支）

## 1. 目标与动机

当前所有内置控件的兜底图素来自 `Runtime/Resources/PromptUGUI/Defaults/pugui.png`——一张
手工 .meta 切片的极小图集（圆角 9 宫格 / mask / caret / checkmark 四个 sprite），视觉上是
Unity 默认白底灰字风。本次：

1. **删除 pugui.png**，用 `.pxl` 文本像素格式重绘默认图素——清新亮丽的种田游戏像素风
   （奶油底 + 暖木描边 + 叶绿点缀），**彩色皮肤直接做库默认**（用户已确认）。
2. 控件"凹/凸/按下"形态分化：新增 inset（凹槽）、pressed（按下）、knob（旋钮）三个图素。
3. 内置 Modals / Toast / Tutorial 的硬编码配色与新皮肤协调。
4. CommonControls 演示界面美化：去掉大量 `color=` 覆盖（展示"零样式开箱即好看"），
   并新增 sample 级 SpriteSet + 装饰 `.pxl`，顺带演示 pxl→SpriteSet 管线。

## 2. 非目标

- 不做主题/换肤系统（`sprite=` / `color=` 覆盖机制原样保留）。
- 不改 `.pxl` 导入器本身。
- 不动公共 C# API 面（仅内部默认值变化——按既定豁免原则属 transparent default，
  SKILL 仅在 Btn 默认 pressedSprite 这一可见行为处小改 `reference/states.md`）。

## 3. 皮肤资产：`pugui.pxl`

### 3.1 文件与加载

- 删除 `Runtime/Resources/PromptUGUI/Defaults/pugui.png` + `.meta`。
- 新建 `Runtime/Resources/PromptUGUI/Defaults/pugui.pxl`，多 section，**纯 inline-hex 模式**
  （不写 `palette: @...`，避免消费者工程 "palette not found"）。
- Resources 路径不变（`PromptUGUI/Defaults/pugui`，Resources 按无扩展名路径寻址），
  `ProceduralBuilders.GetDefaultSprite` 的 `Resources.LoadAll<Sprite>` 不用改——
  PxlImporter 的 Sprite 子资产名 = section 名。
- **四个旧 sprite 名原样保留**（`pugui_9slice_round` / `pugui_9slice_mask` /
  `pugui_caret` / `pugui_checkmark`），Tutorial 手指、viewport mask 三态等按名引用无感。

### 3.2 Section 清单

| section | 用途 | 规格 |
|---|---|---|
| `pugui_9slice_round` | Btn/Tab/Frame 类凸面 | 12×12，border 4,4,4,4；木描边 + 顶亮底暗斜面 + 奶油面 |
| `pugui_9slice_inset` *(新)* | InputField / Slider track / ScrollList bg / Dropdown scrollbar bg 凹槽 | 12×12，border 4；斜面反转（上暗下亮），底色偏暗奶油 |
| `pugui_9slice_pressed` *(新)* | Btn 默认按下态 | 12×12，border 4；斜面反转 + 面色压暗，轮廓与 round 一致（剪影不跳） |
| `pugui_knob` *(新)* | Slider 手柄 | 11×11 奇数尺寸圆形木纽扣，无 border（Simple） |
| `pugui_9slice_mask` | viewport stencil mask | 12×12，border 4；**纯白**实心圆角（stencil 用，必须保持白色 + alpha=1） |
| `pugui_caret` | Dropdown 箭头 / Tutorial 手指 | 深木色向下小箭头，奇数宽 |
| `pugui_checkmark` | Toggle / Dropdown item 勾 | 叶绿色 ✓ |

`ppu: 100` 沿用旧图集（spritePixelsToUnits=100），避免 native-size 路径（Progress 的
`NativeOf` 等）数值突变。

### 3.3 色板（inline hex，~10 色）

| 角色 | 色值 |
|---|---|
| 深木轮廓 | `#5A3A20` |
| 木中调 | `#8B5E3C` |
| 木亮调 | `#C68B52` |
| 奶油高光 | `#FFFBEA` |
| 奶油面 | `#FFF1D2` |
| 奶油暗 | `#EFD9A8` |
| 凹槽底 | `#E8CFA0` |
| 按下面 | `#F0DBAE` |
| 叶绿 | `#58A63C` |
| 叶绿亮 | `#7CC850` |

具体逐像素绘制在实现时遵循 authoring-promptugui-pxl skill 工艺规则
（1px 最深色闭合轮廓、9-slice 边带可平铺、中心平铺色、按下态反转斜面）。

## 4. C# 改动（全部 internal）

### 4.1 `ProceduralBuilders`

- 新增常量：`SpriteInset = "pugui_9slice_inset"`、`SpritePressed = "pugui_9slice_pressed"`、
  `SpriteKnob = "pugui_knob"`。
- 新增 `ApplyDefaultInsetSprite(UnityImage)`（与 `ApplyDefaultSlicedSprite` 同形，换 sprite 名）。
- `s_darkGrey`（label/glyph/placeholder 单点基色）`#323232` → 暖深棕 `#4A3322`。
- 其余 Default*Color 维持 white（彩色 sprite 自带颜色，`color=` 仍是 tint 语义）。

### 4.2 各 builder 的默认 sprite 切换

| 控件 | 现状 | 改为 |
|---|---|---|
| InputField `_bg` | round | inset |
| Slider `_bg`（track） | round | inset |
| Slider `_handle` | round (Simple) | knob (Simple) |
| ScrollList `_bg` | round | inset |
| ScrollList / Dropdown scrollbar bg | round | inset |
| scrollbar handle | round | round（不变，凸面合理） |
| Btn / Tab / Toggle / Dropdown `_bg`、popup bg | round | round（不变） |

### 4.3 Btn 默认 pressedSprite

用户未写 `pressedSprite` 且 `_bg.sprite` 仍是默认 round sprite 时，兜底
`pugui_9slice_pressed`（走既有 overrideSprite-on-press 机制）。用户写了
`sprite=` 或 `pressedSprite=` 则完全让位。判定时机：OnAttached 的
`ApplyDefaultSlicedSprite` 之后、属性应用完成后（实现计划里定具体钩子，
需兼容 ReSolve 不重复套用）。

## 5. 内置 XML 配色协调

`Modals/MessageBox|InputBox|MarkdownBox|Loading.ui.xml`、`Toast.ui.xml`、
`Tutorial/TutorialOverlay.ui.xml`：硬编码深灰/蓝 `color=` 换暖色系——
背板奶油（默认皮肤原色，多数 `color=` 直接删）、确认钮叶绿、取消钮木棕、
遮罩半透明深棕。文案字色交给新的默认 label 色。逐文件过 UIXmlLint。

## 6. 演示界面美化（Samples~/CommonControls）

### 6.1 `CommonControls.ui.xml`

- 背景 `#202020` → 清新双层：天空浅青 + 草地绿（或奶油纸张），具体在实现时视觉调。
- 标题区横幅化（木牌质感：默认 round + 暖色 tint，或 sample sprite）。
- 删掉演示页绝大多数 `color="#3B82F6"` / `#555555` / `#333333` 等覆盖——
  控件裸写即种田风；保留少量 tint 示例（如 Toast 按钮绿色）以演示 tint 仍可用。
- ② 页 Grid 12 个纯色块 → 12 个作物/农场小图标（sample SpriteSet）。
- Carousel 卡片模板换皮肤框 + 暖色系卡片底色。

### 6.2 Sample SpriteSet + 装饰 `.pxl`

- `Samples~/CommonControls/Resources/Sprites/` 新增 SpriteSet（sourceFolder 模式），
  装饰 `.pxl` 若干：作物图标（胡萝卜/番茄/麦穗/苹果…凑 12 个 Grid 用）、太阳/星星、
  Carousel 卡片框。sample 内可配 `.gpl` 色板（sample 自带，不影响 Runtime）。
- Runner C# 绑定 id 全部不变，仅 Carousel 卡片配色数组等数据微调。

## 7. 测试与验收

- **TDD**：§4 每个 builder 默认 sprite 变化先写红测（EditMode，`UI.ResetForTests` 惯例）；
  Btn 默认 pressedSprite 的"用户覆盖让位 / ReSolve 幂等"各一测。
- grep 现有测试对 `pugui_*` / `pugui.png` 的引用并同步（sprite 名保留应使破坏面≈0）。
- `pugui.pxl` 自校验：逐行复读网格（行宽一致、轮廓闭合、9-slice 边带均匀），
  Unity Console 无导入错误，`GetDefaultSprite` 七个名字全命中。
- UIXmlLint 跑所有改动的 `.ui.xml`；UnityMCP 全量 EditMode + EditorOnly + PlayMode。
- dotnet format lint 清洁。
- 最终视觉 QA 归用户。

## 8. 风险

- **pressed 兜底与 RuntimeState/ReSolve 交互**：沿用 pressedSprite 既有机制、
  仅改"默认值来源"，并以幂等测试钉住。
- **mask sprite 必须纯白 alpha=1**：spec §3.2 已标注（4af322b 的 stencil 教训）。
- **彩色默认对存量用户是视觉 breaking**：接受——本库 pre-1.0，且 `sprite=`/`color=`
  覆盖路径不变。
