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
