# Procedural Style Demo

Common Controls Demo 的同内容改版：**同一批控件，视觉全部来自 `<Style>` + `<Frame>` 的
程序化绘制**（圆角矩形 SDF：填充 / 渐变 / 圆角 / 内描边 / 外发光 / 磨砂玻璃），一张图素、
一个 9-slice 都没有。

现在带两套皮肤，**默认跑玻璃**：

| 皮肤 | 长什么样 | 需要什么 |
|---|---|---|
| `Skin-Glass.ui`（默认） | 磨砂玻璃：面板透出后面的壁纸 | URP ≥ 17 + 场景里一台相机 |
| `Skin-Flat.ui` | 扁平实心 + 整屏渐变底 | 什么都不需要 |

## 跑起来

场景里建一个空 GameObject，挂上 `ProceduralStyleRunner`，按 Play。
不需要 SpriteSet，不需要字体资源（界面文案用英文，因为 Unity 内置 TMP 默认字体没有中日韩字形）。

玻璃皮肤额外需要 URP（≥ 17）当激活管线，以及一台 `MainCamera`（没有的话 runner 会自己建一台）。
条件不满足时玻璃自动退化成半透明面板，不会报错也不会白屏 —— 见下面「玻璃退化了怎么办」。

## 文件

| 文件 | 作用 |
|---|---|
| `Resources/UI/Skin-Glass.ui.xml` | **玻璃皮肤（默认）**。两套 `<Theme>` 配色 token + 12 个 `<Style>` 形体 |
| `Resources/UI/Skin-Flat.ui.xml` | 扁平皮肤。同样那 12 个名字的另一种实现 |
| `Resources/UI/ProceduralStyle.ui.xml` | 版面。一个颜色值、一个圆角数字都没写，只写 `class="…"` |
| `Resources/UI/Backdrop.ui.xml` | 玻璃背后那张壁纸（`canvas="camera"`，见下节） |
| `ProceduralStyleRunner.cs` | C# 绑定 + 壁纸下载 |

## 换皮肤

改主文档顶部那一行就够，版面、控件、C# 绑定一行都不用动：

```xml
<Import src="Skin-Glass.ui"/>   →   <Import src="Skin-Flat.ui"/>
```

写第三套皮肤同理 —— 实现同样这 12 个 `<Style>` 名字即可：

```
app-bg  app-bar  panel  card  well  chip
btn-primary  btn-ghost  btn-danger  divider  tab-track  hero
```

`<Theme>` 也放在 skin 文件里，所以"配色"和"材质"是一起换的。

## 玻璃看得见什么（这是唯一容易写错的地方）

玻璃采样的是**相机渲完那一刻的画面**：游戏世界 + 挂在那台相机上的所有
Screen Space-Camera canvas。**Overlay canvas 不在里面** —— uGUI 没有 grab pass，
同一个 Overlay canvas 里的玻璃永远看不见自己的兄弟节点。

所以本 demo 的结构是：

```
Backdrop.ui        canvas="camera"   ← 壁纸放这儿，会被玻璃模糊
ProceduralStyle.ui （默认 Overlay）  ← 玻璃放这儿
```

反过来写（玻璃所在的 canvas 由采集相机渲染）会形成反馈回环：玻璃采到含自己上一帧像素的画面，
几帧之后糊成一团。运行时会 warn 一次。

`canvas="camera"` 的 Screen 需要一个相机引用，而 XML 里写不了引用 —— 由 runner 的
`UI.CanvasConfigurator` 补上，用的就是 `UI.Glass.Camera`（默认 `Camera.main`）那一台：

```csharp
UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = camera;   // 无条件
```

**别写成 `if (canvas.renderMode == RenderMode.ScreenSpaceCamera) { … }`。** Unity 的
`renderMode` **getter** 在 `worldCamera` 为空时会谎报成 `ScreenSpaceOverlay`（模式其实记在内部，
一赋相机就恢复），所以那个判断在配置器里**永远不命中**，Backdrop 会静默留在 Overlay ——
画面照样显示，只是不再被相机渲染，于是玻璃采集不到它，看起来就像「玻璃对背景图不起作用」。
给真·Overlay 的 canvas 赋 `worldCamera` 是无害的（Unity 会忽略），所以无条件赋值即可。
真漏了的话 `Screen.Open` 会 warn 一条。

## 壁纸从哪来

runner 运行时去拉 **Bing 每日图**，拉不到就用内置的程序化暮色渐变兜底（断网、平台不给联网、
接口改版都走这条路，界面不会因此变成一片黑）。

刻意**不把图提交进仓库**：Bing 壁纸是有版权的，一个 UPM 包不该随包分发它们。图片的版权署名
显示在 `Backdrop` 右下角 —— 它在 camera canvas 上，所以你能直接看到它**被玻璃模糊掉**。

## 玻璃退化了怎么办

以下任一条件不满足，玻璃就画成半透明面板（形状 / 描边 / 发光照旧），不报错：

- 工程没装 URP ≥ 17，或 URP 不是当前激活管线
- **URP 跑在 Compatibility Mode（Render Graph 关闭）** —— 见下
- 场景里没有采集相机（runner 会兜底建一台）
- `UI.Glass.Enabled = false`（画质选项就该接这里）
- **不在 Play 模式** —— 玻璃不在编辑器里预览

想确认当前到底有没有在模糊：读 `UI.Glass.IsActive`。

**玻璃需要 Render Graph。** 采集 pass 只实现了 `RecordRenderGraph`；Compatibility Mode 下 URP 走的是
废弃的 `Execute` 路径，于是 pass 每帧照常入队、却一次都不会跑 —— 不报错，玻璃就一直停在退化态，
很容易被误读成「模糊坏了」。**Unity 6000.0–6000.3 尤其要注意**，那几个版本
*Project Settings → Graphics → Render Graph* 里的 Compatibility Mode 开关还在，而且可能是开着的。
连续约 60 帧只入队不执行之后，运行时会 warn 一条点名这个原因。

## 玻璃皮肤里的几个取舍

- **`app-bg` 不是玻璃，是压在壁纸上的暗场。** Bing 每日图什么亮度都可能，这一层是文字可读性的
  唯一保障 —— 玻璃自己的 tint 太淡，撑不住这件事。
- **tint 的 alpha 一律压得很低**（面板 8%、卡片 12%）。高了就不像玻璃，像糊了一张色纸。
  按钮是例外（55%），它们需要在照片上一眼可辨。
- **`card` 比 `panel` 薄**（`depth` 3 对 8）。叠上去时厚度差自己就把层级说清楚了，不用加描边。
- **`well` / `tab-track` 的 tint 用深色**，读起来才是"凹进去的槽"而不是浮起来的片。
- **只有 `hero` 开了 `dispersion`。** 色散要付 3 倍 backdrop 采样，而按钮那种 6px 斜面上
  根本看不出来 —— 留给全屏唯一那个展示件。
- **`divider` 不是玻璃。** 一条 1px 的线不值得为它开一块玻璃（一块玻璃 = 一次 backdrop 采样）。

## 用到的 Style 特性

- `class="card"` —— 命名属性包
- `class="card btn-primary"` —— 多 class，右边覆盖左边
- `class="card btn-primary" radius="pill"` —— inline 再覆盖全部（Styles 页最后一行有直观对比）
- `class="{{skin}}"` —— 皮肤名作模板参数（`SwatchCell` 模板）
- `<Import>` 带过来的 `<Style>` —— 和 `<Template>` 一样可以放进共享库

## 内置控件怎么跟着一起扁平化

程序化视觉（圆角 / 描边 / 辉光）目前只有 `<Frame>` 有，所以带交互的控件都是
**"透明控件 + 一层 `class="…"` 的 Frame 当外壳"**的写法。这个组合顺带演示了
`hoverModulate` / `pressedModulate` 会扩散到子树里的 Frame 上 —— 按钮按下时整块外壳一起变暗。

控件**内部**的图层（Slider 的轨道/已填充段/滑块、Toggle 的对勾、Dropdown 的箭头和弹窗、
滚动条）各有一对 `<layer>` / `<layer>Color` 属性，全在 XML 里搞定，本 demo 没有一行
C# 皮肤代码：

```xml
<Slider sprite="" color="bg-bottom/0.6" fill="" fillColor="accent"
        handle="" handleColor="ink-dim"/>
<Toggle sprite="" color="bg-bottom/0.6" checkmarkColor="accent"/>
<Dropdown arrow="" itemColor="surface-2" itemTextColor="ink"
          scrollbar="" scrollbarColor="bg-bottom/0.6"/>
```

`""` = 去掉那一层的 sprite，只留纯色 —— 扁平外观基本靠它。`arrow=""` 是隐藏箭头
（无图的 Image 会画成实心方块），真实项目应该换成自己的 chevron sprite 或图标字体。

注意 `Image.color` 是**乘法**：带颜色的 sprite（默认皮的箭头、对勾）只能被染暗、染不成
别的色相。要彻底换色就得换图。
