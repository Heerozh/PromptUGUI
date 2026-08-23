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

## 已知缺口（在库里修好之前，`ProceduralStyleRunner.ApplyLibraryWorkarounds` 绕过）

1. **`<Tab>` 的 `width` / `height` 不生效。** `TabBar` 建的 Layout Group 用 Unity 默认的
   `childControlWidth/Height = false`，这种模式下只摆位置、不改尺寸，所以每个 Tab 的实际
   RectTransform 一直是默认 100×100。runner 里把这两个开关打开后 XML 写的值才落到 rect 上。
   （这个问题同样影响现有的 Common Controls Demo。）

2. **部分内置控件的内部图层仍带默认像素皮。** Slider 的滑轨/滑块、Dropdown 的箭头、
   Toggle 的对勾由控件 `OnAttached` 写死，没有 XML 属性可改 —— XML 的 `sprite=` 只作用于
   控件最外层那张背景 `Image`。skill 建议"subclass 并 override `OnAttached`"，但这些控件
   全是 `sealed` 的，那条路走不通；runner 里按 GameObject 名字遍历改。

程序化视觉目前只有 `<Frame>` 有，所以带交互的控件都是"透明控件 + 一层 `class="…"` 的 Frame
当外壳"的写法 —— 这个组合也顺带演示了 `hoverModulate` / `pressedModulate` 会扩散到子树里的
Frame 上，按钮按下时整块外壳一起变暗。
