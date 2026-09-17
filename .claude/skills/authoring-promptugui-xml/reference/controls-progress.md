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

## The fill is the primary surface (spec 2026-09-18)

Every procedural attribute — `radius` / `borderWidth` / `borderColor` / `glow` / `glowColor` /
`innerGlow` / `innerGlowColor` / `intensity` / `haze` (+ `hazeColor` / `hazeDrift` / `hazeDensity`) /
`glass` (+ its parameters) — lands on the **filled segment**. `fill` / `fillColor` are that surface's
bitmap and colour. The track is `bg` / `bgColor` (+ `radius`, see below) and nothing more: a track
that wants a border, a glow or glass of its own is a `<Frame>` wrapped around the bar. (This is the
one place the vocabulary splits from `<Slider>`, whose `glow` lights the track.)

```xml
<!-- HUD energy bar: colour pill track, glowing gradient fill, fog along the bottom -->
<Progress value="0.6" radius="pill" bgColor="#0b1a33"
          fillColor="to right, hud-edge-cyan, #3cf" glow="6" glowColor="hud-edge-cyan/0.6"
          haze="10" hazeColor="to top, white/0.5, white/0" borderWidth="0.5" borderColor="white/0.4"/>

<!-- a dressed track: the Frame is the track, the Progress draws only its fill -->
<Frame radius="pill" glass="true" borderWidth="1" borderColor="white/0.3" height="20">
  <Progress anchor="stretch" margin="2,2,2,2" value="0.6" radius="pill" fillColor="accent" glow="4" intensity="1.4"/>
</Frame>
```

### A procedural fill is the whole bar, cut at `value` in the shader

The fill's rect stays full-size; `value` becomes a half-plane intersection inside the SDF
(`d = max(d, dot(p, n) − e)`), and everything the panel draws derives from that `d`:

- the **leading edge is straight**, the start end keeps its `radius`;
- border, inner glow and haze stop at the cut; the **outer glow wraps the cut edge** and escapes the
  track on every side — no stencil is involved, so nothing clips it (`value="0.1"` is a glowing sliver);
- a gradient `fillColor` is laid along the **whole bar** and cropped — 30 % shows the first 30 % of
  the ramp, as `Image.Filled` would;
- `direction` picks the axis and sense of the cut; `mode` has **no say** (it is a bitmap-fill knob:
  `scale` anchors the rect, `fill` uses `Image.fillAmount`);
- `value="0"` draws **nothing** — not even the glow along the start edge; `value="1"` is the uncut shape;
- a `value` tween re-emits four vertices: no layout pass, no material change (the cut rides the vertex
  channels, so bars sharing a `class=` still share one material).

`glass` on the fill is allowed: the track cannot be glass, so the two never sample the same backdrop.
A glass fill shows the **scene behind the canvas**, not the `bgColor` beneath it.

### `radius` is the bar's shape — three consumers

| the fill is… | the fill | the bg | the clip mask |
|---|---|---|---|
| **not a bitmap** (`fillColor` only, or `fill=""` / `none`) | takes `radius` as its own SDF corner (a plain colour fill goes procedural for it) | a colour bg takes it through a surface of its own; a bitmap bg keeps its baked corners | **none is built** — a stencil here would clip the glow |
| **a bitmap** (`fill="ui:bar"`) | keeps the bitmap — `radius` alone never retires it (any other procedural attribute does: `PUI-PROC-SPRITE-CONFLICT`) | as above | **auto-tracks `radius`** (`maskRadius` unset), clipping bg + fill together with the fill's leading edge square — the way `<ScrollList mask>` follows its bg sprite |

```xml
<Progress value="0.6" radius="14" bgColor="#22345a" fillColor="#ffcc33"/>   <!-- both ends round: fill and bg round themselves -->
<Progress value="0.6" radius="14" fill="ui:bar" bgColor="#22345a"/>         <!-- bitmap fill: clipped by the auto-tracked mask -->
<Progress value="0.6" radius="14" fill="ui:bar" maskRadius=""/>             <!-- opt out: the bitmap keeps its square leading corner -->
<Progress value="0.6" maskRadius="14" fill="ui:bar"/>                        <!-- clip only, no track -->
```

- The procedural mask is a **pure clipper** (`showMaskGraphic=false`); the track is `bgColor`. A
  sprite `mask=` keeps its dual role (with no bg it doubles as the track, table below).
- `mask=` and `maskRadius` are **exclusive**: one Graphic per GameObject, the sprite wins —
  `PUI-PROG-MASK-RADIUS-CONFLICT`.
- An explicit `mask=` / `maskRadius=` over a procedural fill is honoured, but it clips the glow —
  `PUI-PROG-MASK-CLIPS-GLOW` when the fill declares `glow`. Drop the mask: a procedural fill rounds itself.
- `radius` with no `bgColor` leaves the bg layer **off** — a rounded fill over nothing, not a white track.
- `fillRadius` is **retired** (`PUI-PROG-RETIRED-ATTR`): the fill is the primary surface, `radius` is its
  corner. `<Slider fillRadius>` is unaffected, so a shared skin pack that carries it is reported only on the
  `<Progress>` that wears it.
- `frameRadius` stays: the frame is still an inner layer with a corner of its own.

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
| `PUI-PROG-MASK-RADIUS-CONFLICT` | `mask=` sprite together with `maskRadius` — one Graphic per node, the sprite wins | error |
| `PUI-PROG-MASK-CLIPS-GLOW` | an explicit `mask=` / `maskRadius=` over a procedural fill that declares `glow` — the stencil eats the glow | warning |
| `PUI-PROG-RETIRED-ATTR` | `fillRadius` on a `<Progress>` (directly or via `class=`) — write `radius`; the runtime drops it silently | error |
| `PUI-PROC-SPRITE-CONFLICT` | `fill="ui:x"` together with any procedural attribute other than `radius` — the SDF wins, the bitmap stands down | error |
