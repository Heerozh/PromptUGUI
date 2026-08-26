# Progress (`<Progress>`)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Progress>` attribute table lives in the main doc's built-in primitives catalog; read this for the multi-layer recipes, mask×bg combinations, and lint rules.

`<Progress>` 是显示型线性进度条，把 frame / mask / bg / fill / mode / direction / value 打包进一行 XML。**只读** — C# 侧直接 setter，无 `OnValueChanged` Observable。

Radial fill（冷却环）不在 `<Progress>` 范围；以后用单独的 `<Cooldown>` 控件。

## 六个典型用例

```xml
<!-- 1. 最简：纯色 bg + 单色 fill；scale 横向 -->
<Progress value="0.6" bgColor="#222" fillColor="#3cf"/>

<!-- 2. 单 sprite 填充；scale 横向 -->
<Progress value="0.6" fill="ui:bar_red"/>

<!-- 3. 圆角胶囊：mask sprite 兼当底 (PB-D9) -->
<Progress value="0.4" mask="ui:pill" fill="ui:bar_blue"/>

<!-- 4. 全套装饰：frame + mask + bg + fill；frameColor 给金边换色 -->
<Progress value="0.6" frame="ui:gold_border" frameColor="#ffd56b" mask="ui:pill" bg="ui:track" fill="ui:bar_red"/>

<!-- 5. Unity Image.Type.Filled, 反向纵向（液体从顶部往下空） -->
<Progress value="0.3" fill="ui:liquid" mode="fill" direction="reverse-vertical"/>

<!-- 6. 在 Variant 中切换 value / colors (frame / bg / fill sprite 允许；mask 完全禁止 — PUI-PROG-MASK-VARIANT) -->
<Progress id="hp"
          value="1.0" value.low="0.2"
          fill="ui:bar" fillColor.low="#f44"
          bgColor="#000"/>
```

## bg / frame 图层的显隐

`bg` / `frame` 图层在**声明了 sprite 或颜色**（`bg` / `bgColor`，`frame` / `frameColor`）时显示，两者都没有时隐藏。这是从当前声明**算出来**的，不是一次性打开：`bg=""` / `bg="none"` 会把图层关掉，所以 Variant 切换、主题换肤、运行时赋值都能把它收回去，不会留着上一次的图素。

```xml
<Progress bg="ui:track" bg.mobile=""/>   <!-- 手机上不要底图，直接关掉该图层 -->
```

## 程序化圆角：`radius` / `fillRadius` / `frameRadius` / `maskRadius`

`radius=` 走的是主表面 = **bg 层**。但 fill 是压在 bg 之上的另一张方角 Image，所以**单靠 `radius`
进度条只有尾端是圆的**。

因此 **`maskRadius` 不写时自动跟随 `radius`** —— 同 `<ScrollList mask>` 跟随 bg sprite、
`<Dropdown popupMask>` 跟随 `popupSprite` 的既有规约。mask 挂在 `MaskWrapper` 上，bg 和 fill 同为
它的子级，于是两层一起被裁成同一形状，而 fill 的推进边保持方的 —— 那正是进度条该有的观感。

```xml
<Progress value="0.6" radius="14" bgColor="#22345a" fillColor="#ffcc33"/>   <!-- 两端都圆 -->
<Progress value="0.6" maskRadius="14" fillColor="#ffcc33"/>                 <!-- 只裁不画底 -->
<Progress value="0.6" radius="14" maskRadius="" bgColor="#22345a"/>         <!-- 退出跟随：尾端圆 -->
```

- 程序化 mask 是**纯裁剪器**（`showMaskGraphic=false`），底由 `bgColor` 负责。sprite 版 `mask=`
  保留它原有的双重身份（没写 bg 时 mask sprite 兼当底，见下表）。
- 与 `mask=` **互斥**：一个 GameObject 上只能有一个 Graphic，sprite 赢，radius 被静默丢弃 ——
  `PUI-PROG-MASK-RADIUS-CONFLICT`。
- `fillRadius` 与 `mode="fill"` **不能共存**（`PUI-PROG-FILL-RADIUS-MODE`）：那个模式靠
  `Image.fillAmount` 画填充，SDF 面没有对应物。默认 `mode="scale"` 改的是锚点，没问题。
- 内层只给 `<layer>Radius`，不给玻璃 —— backdrop 采集不含 UI 自身，玻璃 fill 压在玻璃 bg 上会采到
  同一张图、两层长得一样。

## mask × bg 四种组合

| 条件                   | MaskWrapper.UnityImage | MaskWrapper.Mask | MaskWrapper.showMaskGraphic | Bg.SetActive | Frame.SetActive |
| ---------------------- | ---------------------- | ---------------- | --------------------------- | ------------ | --------------- |
| 无 mask、无 bg/bgColor | 不挂                   | 不挂             | —                           | false        | (按 frame)      |
| 无 mask、有 bg/bgColor | 不挂                   | 不挂             | —                           | true         | (按 frame)      |
| 有 mask、无 bg/bgColor | 挂（sprite=mask）      | 挂               | true                        | false        | (按 frame)      |
| 有 mask、有 bg/bgColor | 挂（sprite=mask）      | 挂               | false                       | true         | (按 frame)      |

`有 mask、无 bg/bgColor` 时 `showMaskGraphic=true` — mask sprite 兼任可见底，一个 sprite 干两件事（圆角胶囊最常见路径）。

## Lint 规则

| Code                    | 触发条件                                                                      | 级别    |
| ----------------------- | ----------------------------------------------------------------------------- | ------- |
| `PUI-PROG-VALUE-RANGE`  | `value` 字面量超出 `[0..1]`                                                   | warning |
| `PUI-PROG-MODE`         | `mode` 不在 `scale\|fill`                                                     | error   |
| `PUI-PROG-DIRECTION`    | `direction` 不在 `horizontal\|vertical\|reverse-horizontal\|reverse-vertical` | error   |
| `PUI-PROG-CHILDREN`     | `<Progress>` 包含子元素                                                       | error   |
| `PUI-PROG-MASK-VARIANT` | `mask` 出现在 Variant 覆盖里                                                  | error   |
| `PUI-PROG-NO-FILL`      | `value` 有值但 `fill`/`fillColor` 均未设                                      | warning |
