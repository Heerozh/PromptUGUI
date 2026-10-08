# 状态乘色（`*Modulate`）挪到 CanvasRenderer 通道

日期：2026-10-08 · 分支：`fix/state-modulate-channel`

## 问题

按钮 / 页签 / 开关写了 `*Modulate` 后，子节点的颜色只在控件创建时被记一次，之后每次悬停、按下、选中都写回这份记录：

- C# 运行时写的 `Text.Color`、`btn.TextColor`、`toggle.CheckmarkColor` 在下一次状态变化时丢失；
- 子节点 `color=` 上的 Variant / 切主题，最晚到下一次悬停就变回最初那套颜色；
- 控件自己的 `btn.Color` / `tab.Color` / `toggle.Color` / `collapsible.HeaderColor` 从 C# 写入后，下一次状态变化回到 XML 声明的底色；
- **没写 `*Modulate` 也会中**：默认禁用灰度对 TMP 也是 capture 一次 `tmp.color`、启用时写回，C# 写的字色禁用再启用一次就没了，禁用期间写的颜色直接亮着显示。

## 根因

两个写入方共用一个通道：`StateTintReactor` 把 `底色 × 乘子` 写进 `Graphic.color`，而 `Graphic.color` 本来属于设置它的那一方（`color=`、Variant、主题、代码）。扇出到子节点的 reactor 拿不到子节点的声明，只能在安装时 Peek 一次当底色，之后它就是过期副本。灰度控制器对 TMP 是同一个形状。

## 决策

**两层颜色，各归其主。** uGUI 本来就把颜色分成 `Graphic.color`（作者）× `CanvasRenderer` 颜色（Selectable 的 ColorTint 驱动的那层），合批时相乘进顶点色——正是 `底色 × 乘子`。

| 层 | 内容 | 谁写 |
|---|---|---|
| 填充 | `absolute ?? selectedBase ?? 底色` | 只有 targetGraphic：Image 上是 `Graphic.color`，程序化面板上是材质填充 |
| 乘子 | `modulate ?? white` | 每个 reactor（targetGraphic 与所有扇出子节点）：`CanvasRenderer` 颜色 |

- 扇出 reactor **不再持有底色、不再写 `Graphic.color`**——子节点的颜色无论谁写都不会被覆盖，也不需要任何"通知 reactor"的入口。
- targetGraphic 的底色仍来自控件的 `color=` 声明；代码写 `Color` 时（`InApplyPass` 之外）由 setter 调 `StateTintReactor.SetBase` 推给 reactor，悬停中写入时继续显示 hoverColor 直到松开。
- 兜底 Peek 改为**第一次画填充时**才取：一个图形可能先作为扇出子节点、后被提升为 target（程序化面板接管），只有那时它的填充才是控件的。
- TMP 子网格（回落字体 / 内联 sprite，中文常见）有自己的 CanvasRenderer。乘子写入时一并写给子网格（`CanvasTint.Set`）；之后新建的子网格由 TMP 在重新生成网格时同步父级颜色。
- 淡变改用 `MotionScheduler.UpdateIgnoreTimeScale`：`Time.timeScale = 0` 的暂停菜单里悬停也会淡变（uGUI 的 ColorTint 同样不受 timeScale 影响）。
- 一个通道已在目标值时不再起 tween（只写 `hoverModulate` 的按钮悬停时，填充层就是底色）。

**CanvasRenderer 颜色不是完全空闲的**，有两个 uGUI 自己的写入方，分别处理：

1. **uGUI `Toggle.graphic`**（对勾）用 CanvasRenderer alpha 做 isOn 淡入淡出。与乘子共层时，悬停会让未勾选的对勾显形；uGUI 的 alpha 补间每帧还会把起始 RGB 写回去，与点击同时到达的按下乘子会丢。→ `<Toggle>` 不再设置 `Toggle.graphic`，对勾节点挂 `CanvasGroup`，由控件按 `isOn` 驱动（0.1s 淡变，开屏 / 出生帧 / Edit Mode 立即）；每次 `OnAfterApply` 再对一次账，兜住不触发回调的 `isOn` 变化。
2. **嵌套的非状态源 Selectable**（`<Slider>` 的 handle、`<InputField>` 的底等 ColorTint 目标）以及子树里其它 uGUI Toggle 的 `graphic`：扇出时跳过（`StateTintInstaller.ForeignTinted`），留给它们自己的 Selectable。这与"嵌套 `<Btn>` 是扇出边界"同一原则。

**默认禁用灰度对 TMP**：不再写 `tmp.color`，改在 `TMP_Text.OnPreRenderText`（网格上传前）把全部 `meshInfo[*].colors32` 去饱和；切换灰 / 不灰时标脏让 TMP 在下一次画布重建时重新生成。富文本 `<color>` 与顶点渐变一起变灰。非 TMP 图形仍是材质替换，逻辑不变。

## 不做

- **颜色的"运行时接管锁"**：ReSolve（切主题 / Variant / locale / resize）仍会把 XML **声明**的 `color=` 重新应用到代码写过的颜色上，与 `grayscale` 的现状一致；在 C# skill 的 *Colours set from code* 里写明。是否像 `isOn` / `Hidden` 那样给颜色属性加锁，另议。
- 被 `stateReact="false"` 的 Variant 翻转"变成剪枝"的旧 reactor 不会被摘掉（剪枝集合里也有嵌套状态源自己的 reactor，不能一并 Detach）。原样保留。

## 测试

- EditMode `StateModulateChannelTests`（19 个）：报告的场景、Variant / 主题下的子节点颜色、`TextColor` / `CheckmarkColor`、四种控件的运行时 `Color`、通道本身、TMP 子网格、嵌套 Slider、Toggle 对勾显隐、默认灰的顶点与写入。改前 17 个失败（另 2 个是对勾的护栏）。
- PlayMode `StateModulateChannelPlayTests`（4 个）：淡变中途写色、`timeScale = 0` 下的淡变、对勾的淡入淡出、默认灰到达实际上传的网格。
- 旧测试里断言 `Graphic.color = 底色 × 乘子` 的 17 处改为断言屏幕上的结果（`Graphic.color × CanvasRenderer 颜色`），并在能加强的地方补了"子节点自己的颜色不变"。
