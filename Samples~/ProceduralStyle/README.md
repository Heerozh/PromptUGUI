# Procedural Style Demo

Common Controls Demo 的同内容改版：**同一批控件，视觉全部来自 `<Style>` + `<Frame>` 的
程序化绘制**（圆角矩形 SDF：填充 / 渐变 / 圆角 / 内描边 / 外发光），一张图素、一个 9-slice
都没有。

## 跑起来

场景里建一个空 GameObject，挂上 `ProceduralStyleRunner`，按 Play。
不需要 SpriteSet，不需要字体资源（界面文案用英文，因为 Unity 内置 TMP 默认字体没有中日韩字形）。

## 文件

| 文件 | 作用 |
|---|---|
| `Resources/UI/Skin-Flat.ui.xml` | **皮肤层**。整套 demo 唯一决定"长什么样"的地方：两套 `<Theme>` 配色 token + 12 个 `<Style>` 形体 |
| `Resources/UI/ProceduralStyle.ui.xml` | 版面。一个颜色值、一个圆角数字都没写，只写 `class="…"` |
| `ProceduralStyleRunner.cs` | C# 绑定，和 CommonControls 版基本一样 |

## 换皮肤

写一份新的 skin 文件，实现同样这 12 个 `<Style>` 名字：

```
app-bg  app-bar  panel  card  well  chip
btn-primary  btn-ghost  btn-danger  divider  tab-track  hero
```

然后改主文档顶部那一行：

```xml
<Import src="UI/Skin-Flat"/>   →   <Import src="UI/Skin-Glass"/>
```

版面、控件、C# 绑定一行都不用动。`<Theme>` 也放在 skin 文件里，所以"配色"和"材质"是一起换的。

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
