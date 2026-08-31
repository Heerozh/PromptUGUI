# Triggers and Animations

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). Read this before using `<Trigger>` or `<Animation>`. For the `state-*` values' visual usage on `<Btn>` / `<Tab>` / `<Toggle>`, see [`states.md`](states.md).

`<Trigger>` is the base — it subscribes to an event (open / loop / click / manual) and exposes an `OnFire` stream to C#. `<Animation>` extends Trigger by also playing a LitMotion animation on fire.

## `<Trigger>` — declarative event hook

```xml
<Trigger id="bonus" on="click@bonus-btn">
  <Frame><Btn id="bonus-btn">领取</Btn></Frame>
</Trigger>
```

`on=` values:

| Value              | Fires when                                                                                                                                                                                                        |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `open`             | Once when Screen opens (default if `on=` is omitted)                                                                                                                                                              |
| `loop`             | (Animation only) Fires once on open and enables looping (default yoyo)                                                                                                                                            |
| `click`            | The unique `<Btn>` inside this Trigger's subtree is clicked (uses Unity `Button.onClick`)                                                                                                                         |
| `click@<id>`       | The `<Btn>` matching `<id>` inside the subtree is clicked                                                                                                                                                         |
| `hover-enter`      | Pointer enters the unique `<Btn>` or `<Image>` in this Trigger's subtree (uGUI `IPointerEnterHandler`)                                                                                                            |
| `hover-enter@<id>` | Pointer enters the `<Btn>` or `<Image>` with `<id>` inside the subtree                                                                                                                                            |
| `hover-exit`       | Pointer leaves the unique `<Btn>` or `<Image>` (`IPointerExitHandler`)                                                                                                                                            |
| `hover-exit@<id>`  | Pointer leaves the `<Btn>` or `<Image>` with `<id>`                                                                                                                                                               |
| `press`            | Pointer pressed down on the unique `<Btn>` or `<Image>` (`IPointerDownHandler`). Instantaneous — release / long-press are v2                                                                                      |
| `press@<id>`       | Pointer pressed down on the `<Btn>` or `<Image>` with `<id>`                                                                                                                                                      |
| `state-normal`     | The nearest **ancestor** `<Btn>` / `<Tab>` / `<Toggle>` enters its Normal state (also fires once at open, since the control starts Normal)                                                                        |
| `state-hover`      | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Hover                                                                                                                                                  |
| `state-pressed`    | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Pressed                                                                                                                                                |
| `state-selected`   | The nearest ancestor `<Tab>` / `<Toggle>` is the active/`isOn` one at rest; fires on selection and once at open if already on. **Meaningful only with a `<Tab>` / `<Toggle>` source — a `<Btn>` never emits it.** |
| `state-disabled`   | The nearest ancestor `<Btn>` / `<Tab>` / `<Toggle>` enters Disabled                                                                                                                                               |
| `state-...@<id>`   | Same, but the source is the `<Btn>` / `<Tab>` / `<Toggle>` with `<id>` (any of the five `state-*` values)                                                                                                         |
| `expand`           | The nearest **ancestor** `<TabMenu>` opens. Fires on open, not on close                                                                                                                                            |
| `collapse`         | The nearest ancestor `<TabMenu>` closes                                                                                                                                                                            |
| `expand@<id>` · `collapse@<id>` | Same, but the source is the `<TabMenu>` with `<id>` in this Trigger's subtree                                                                                                                        |
| `checked`          | The nearest **ancestor** `<Toggle>` / `<Tab>` turns on (`isOn` becomes true). Persistent, not transient — see below                                                                                               |
| `unchecked`        | The nearest ancestor `<Toggle>` / `<Tab>` turns off                                                                                                                                                               |
| `checked@<id>` · `unchecked@<id>` | Same, but the source is the `<Toggle>` / `<Tab>` with `<id>`                                                                                                                                       |
| `manual`           | Does not auto-fire; C# must call `Fire()`                                                                                                                                                                         |

**`expand` / `collapse` are not called `open` / `close`** — `on="open"` already means "the Screen opened". They exist because a `<TabMenu>`'s panel is an internal node you cannot wrap in an `<Animation>`; the panel's own entrance is the menu's `transition=` attribute, and these are for animating the **rows** inside it. They resolve upward exactly like `state-*`, and the popup they live in is switched off while collapsed — which the ancestor walk accounts for.

**`checked` / `unchecked` are the PERSISTENT pair.** `state-selected` is part of uGUI's transient interaction machine — Hover and Pressed override it while they last, so a block keyed on it blinks out the moment the pointer touches the control. `checked` asks a different question ("is it on?"), which hovering does not change, and it is the one to use for a header that shows or hides a panel. A bare `checked` / `unchecked` with no `<Toggle>` / `<Tab>` ancestor is a runtime error and `PUI-CHECKED-NO-SOURCE` in the lint CLI; a `<Btn>` does **not** count — it has no checked state at all. They fire on the edge, and a control that is ALREADY in that state as the Screen opens dispatches once too — see *First-frame establishment* below.

**`state-*` source resolution is UPWARD**: unlike `click` / `hover-enter` / `press` (which search this Trigger's **subtree downward** for a `<Btn>` / `<Image>` source), `state-*` resolves to the nearest `<Btn>` / `<Tab>` / `<Toggle>` **ancestor** (`state-...@<id>` targets a specific source control by id). A bare `state-*` with no `<Btn>` / `<Tab>` / `<Toggle>` ancestor is a runtime error (and `PUI-STATE-NO-SOURCE` in the lint CLI; `@id` forms and Template bodies are exempt). They **fire on entering** the state, so `state-normal` fires once at open and `<Animation on="state-pressed">` plays on press with `<Animation on="state-normal">` as its natural revert.

**`hover-enter` vs `state-hover`**: `hover-enter` / `press` are **raw pointer events** (`PointerEventRelay`, `IPointer*Handler`, downward source) — they fire on any pointer enter / down regardless of interactable state. `state-hover` / `state-pressed` come from the control's **Selectable state machine** (disabled-aware, drag-cancel-aware, upward source); a disabled `<Btn>` / `<Tab>` / `<Toggle>` never emits `state-hover` / `state-pressed`, only `state-disabled`.

**`@id` resolution is LEXICAL, nearest scope first** (every `@id` form: `click` / `hover-*` / `press` / `state-*` / `expand` / `collapse` / `checked` / `unchecked`). The id is looked for in the trigger's own subtree, then in each enclosing scope walking outward — which is where a Template instance's shared id table lives, so two invocations of one template never see each other's ids — and finally among the Screen's top-level ids. That is what lets a trigger point at a **sibling**:

```xml
<VStack width="150">
  <Toggle id="hdr" isOn="true">任务</Toggle>
  <Show on="checked@hdr">           <!-- not inside the Toggle — a sibling of it -->
    <ScrollList itemTemplate="TaskRow" width="stretch" height="clamp(_, hug, 200)"/>
  </Show>
</VStack>
```

An id that resolves nowhere is a runtime error naming all three places it looked. `click@<id>` keeps its historic subtree-first walk (which reaches a `<Btn>` at any depth) before falling back to the lexical lookup.

**Pointer-event source range**: only `<Btn>` and `<Image>` can be `hover-enter` / `hover-exit` / `press` event sources. They both default to `raycastTarget=true`, which is what Unity's EventSystem requires for dispatching pointer events. Using `@<id>` to reference `<Icon>` (hardcoded `raycastTarget=false`), `<Text>` (default `false`), `<Frame>` (no Graphic to receive raycasts), or any other control as a pointer source → runtime error `"id 'X' is a Y, not supported as pointer event source. Use <Btn> or <Image>."`

**Caveat — `raycastTarget="false"` silently breaks pointer triggers**: if you set `<Image raycastTarget="false">` and then reference that Image via `on="hover-enter@..."`, the pointer event never reaches the GameObject — the trigger silently never fires. No error is raised. Keep `raycastTarget=true` on any Image you want to trigger pointer events from.

**`click` vs `press`**:

- `click` uses Unity's `Button.onClick` (drag-cancel / disabled-state handling). **`<Btn>` only.**
- `press` is the raw `IPointerDownHandler` event. **Works on both `<Btn>` and `<Image>`.**
- Use `click` for button activation; use `press` for instant visual feedback on press (scale 0.95 etc.).

Subscribe in C#:

```csharp
screen.Get<Trigger>("bonus").OnFire
    .Subscribe(_ => Game.AwardBonus())
    .AddTo(screen);
```

## `<Animation>` — LitMotion-driven effects

Three exclusive attribute families. Each `<Animation>` uses **exactly one** family.

### Family A — Preset (opinionated bundle)

```xml
<Animation type="fadein" duration="0.3s">
  <Text>Welcome</Text>
</Animation>
```

Valid `type=` values: `fadein` / `fadeout` / `slidein-left` / `slidein-right` / `slidein-up` / `slidein-down` / `slideout-left` / `slideout-right` / `slideout-up` / `slideout-down` / `scalein` / `scaleout` / `pulse` / `bounce` / `shake`

### Family B — Low-level transform (compose any combination)

```xml
<Animation translate="0,-50:0,0" fade="0:1" duration="0.4s" easing="out-back">
  <Frame>...</Frame>
</Animation>
```

Attributes (any combination):

| Attribute   | Format                     | Notes                                                                      |
| ----------- | -------------------------- | -------------------------------------------------------------------------- |
| `translate` | `"x1,y1:x2,y2"`            | Offset from→to in pixels. Omitting `from` (e.g. `":50,0"`) means from=zero |
| `scale`     | `"s:s"` or `"sx,sy:sx,sy"` | Scale from→to; single value applies to both x and y                        |
| `rotate`    | `"d1:d2"`                  | Z-axis rotation in degrees                                                 |
| `fade`      | `"a1:a2"`                  | Alpha from→to (0..1)                                                       |

Transform attributes always target the Animation's inner `_offsetProxy` GO — they cannot be redirected with `target=`.

### Family C — Text effect

```xml
<!-- Count-up number -->
<Animation count="0:100000" format="{0:N0}" duration="2s">
  <Text>0</Text>
</Animation>

<!-- Per-character color wave (hex or theme token) -->
<Animation char-color="#ffffff:#ff4400" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
<!-- Or with theme tokens -->
<Animation char-color="primary:secondary" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
```

| Attribute                                       | Notes                                                                                                                                                                                          |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `count="from:to"` + `format="{0:N0}"`           | Animates a number; writes formatted string into `<Text>` (LitMotion `BindToText`)                                                                                                              |
| `char-color="from:to"` + `char-stagger="0.05s"` | Per-char color wave (`BindToTMPCharColor`); `from:to` is hex literal / CSS named / theme token (e.g. `#ffffff:#ff0000` or `primary:secondary`); each char's motion is delayed by `i * stagger` |
| `target="@id"`                                  | Resolves a `<Text id="id">` in screen-global scope when the target is outside the wrapper subtree                                                                                              |

Text family default: looks for the unique `<Text>` in the subtree. Multiple `<Text>` descendants without `target=` → parse error.

### Family D — Reveal (grow and shrink a box)

```xml
<!-- an inline fold: the content below it moves as this opens and closes -->
<Animation on="expand@tasks" reverse-on="collapse@tasks" reveal="y" fade="0:1" duration="0.2s">
  <VStack width="stretch" spacing="4">…rows…</VStack>
</Animation>
```

| Attribute     | Values           | Default | Notes                                                                    |
| ------------- | ---------------- | ------- | ------------------------------------------------------------------------ |
| `reveal`      | `y` / `x`        | —       | The axis this animation owns: `y` = height, `x` = width                  |
| `reveal-from` | px / `hug`       | `0`     | Where it rests before anything fires                                     |
| `reveal-to`   | px / `hug`       | `hug`   | `hug` = the child's measured content size, re-measured on every fire      |

Unlike the transform channels — which move an invisible proxy and never touch layout — **reveal owns the host's size on that axis**, so the siblings around it move as it opens. That is the whole point: an inline fold pushes the content below it down.

- **Composes with Family B** (`fade` / `translate` / `scale` / `rotate`), which is why "grow open while fading in" is one animation and not two. It is exclusive with Family A (`type=`, a fixed bundle) and Family C (`count` / `char-color`, which drive a string).
- **The resting state is `reveal-from`, not identity.** This is the one channel that starts by hiding something: an `on="expand@…"` subtree has no business being visible before the expand. The default `on="open"` therefore plays 0 → content at Screen open.
- **Exactly one child** (`PUI-REVEAL-SINGLE-CHILD`) — it is the thing being measured and clipped. Wrap several in one `<VStack>` / `<Frame>`.
- **`hug` is measured per fire, never cached**, so new rows, a wrapped line or a locale switch are picked up by the next open. The child is measured even if it is currently inactive.
- **Clipping**: a `RectMask2D` on the host is enabled while anything is hidden and switched off at a `hug` endpoint (where the box is exactly the content, so nothing is). A numeric endpoint keeps the clip — it may or may not cover the content.
- **Do not also write the size** it owns: `height=` / `size=` alongside `reveal="y"` is `PUI-REVEAL-SIZE-CONFLICT` (the reveal overwrites it every pass); the cross axis is yours. `scale=` on the same node is `PUI-REVEAL-SCALE`; a child that stretches on the animated axis is `PUI-REVEAL-CHILD-STRETCH` (the child would follow the box while the box measures the child).

### `reverse-on` — play it backwards

```xml
<Animation on="checked@hdr" reverse-on="unchecked@hdr" rotate="0:180" duration="0.15s">
  <Icon name="ui:chevron" size="12x12"/>
</Animation>
```

`reverse-on=` takes the same event grammar as `on=` (minus `open` / `loop`, which describe a beginning rather than an event) and plays the animation **backwards from wherever it currently is** — an open interrupted half-way turns straight around instead of snapping to an end. The reversal takes the full `duration` and the same `easing`; `reverse-on="manual"` is the C# entry point (`animation.Reverse()`, plus an `OnReverse` stream).

- Declaring `reverse-on` also changes the **forward** direction: it starts from the current value too, so re-firing mid-flight continues rather than restarting. An animation without `reverse-on` keeps the historic "write `from`, then tween" restart exactly.
- Exclusive with `loop=` (`PUI-REVERSE-LOOP` — a looping motion has no resting end state) and with the text family (`PUI-REVERSE-TEXT` — a number counting backwards has no stable current value).
- Only on `<Animation>`; on a `<Trigger>` / `<Show>` there is nothing to reverse (`PUI-REVERSE-ON-TAG`). For a second event stream in C#, add another `<Trigger>`.

**First-frame establishment.** A `checked` / `unchecked` trigger whose control is *already* in that state as the Screen opens establishes the end state instead of animating into it — a header authored `isOn="true"` shows its chevron already turned rather than spinning it on frame 1. Every later flip animates normally. (The same idea as `states.md`'s first-frame rule for colours.)

### Common attributes (all families)

| Attribute  | Default     | Notes                                                                                        |
| ---------- | ----------- | -------------------------------------------------------------------------------------------- |
| `duration` | `0.3s`      | Supports `0.3s` / `300ms` / bare float (seconds)                                             |
| `delay`    | `0s`        | Delay before motion starts                                                                   |
| `easing`   | `out-cubic` | See easing table below                                                                       |
| `loop`     | (none)      | `true` (infinite restart) / `yoyo` (infinite back-and-forth) / `count:N` (N times then stop) |
| `on`       | `open`      | Same as `<Trigger>`                                                                          |

**Easing values:** `linear` / `in-cubic` / `out-cubic` / `in-out-cubic` / `in-quad` / `out-quad` / `in-out-quad` / `in-quart` / `out-quart` / `in-out-quart` / `in-quint` / `out-quint` / `in-out-quint` / `out-back` / `out-elastic` / `out-bounce`

## Rules and parse errors

- Families A (preset), B (low-level transform) and C (text) are mutually exclusive: writing both `type=` and `translate=` → parse error. **D (reveal) is the exception — it composes with B**, and is exclusive with A and C.
- `count=` and `char-color=` are mutually exclusive within the text family
- `on="click"` requires a unique `<Btn>` descendant; multiple → use `on="click@<id>"` to disambiguate; zero `<Btn>` → error
- `reveal-from` and `reveal-to` may not be the same value (nothing would move); a reveal endpoint may not be negative
- `reverse-on` may not be `open` or `loop`

## Patterns

**Menu rows entering with the popup** — one `<Animation on="expand">` per row, reused via a Template. With `reverse-on` the same row also plays out when the menu closes:

```xml
<Template name="ChannelRow">
  <Param name="text"/>
  <Animation on="expand" reverse-on="collapse" type="slidein-left" duration="0.12s">
    <Tab id="tab" text="{{text}}"/>
  </Animation>
</Template>

<TabMenu id="channel" itemTemplate="ChannelRow" popupWidth="240" padding="8"/>
```

**A toggle-driven chevron** — the icon turns as the header is checked, and turns back when it is not. Already-checked at open means already turned, with no first-frame spin:

```xml
<Toggle id="hdr" isOn="true" width="stretch" height="24">任务
  <Animation on="checked" reverse-on="unchecked" rotate="0:180" duration="0.15s">
    <Icon name="ui:chevron" anchor="center-right" size="12x12" margin="_,8,_,_"/>
  </Animation>
</Toggle>
```

**An inline fold** — content that grows open and pushes its siblings down, and closes again:

```xml
<VStack width="150" spacing="0">
  <Toggle id="hdr" isOn="true" width="stretch" height="24">任务</Toggle>
  <Animation on="checked@hdr" reverse-on="unchecked@hdr" reveal="y" fade="0:1" duration="0.2s">
    <VStack width="150" spacing="0">
      <TaskRow/><TaskRow/><TaskRow/>
    </VStack>
  </Animation>
</VStack>
```

**Menu entry stagger** (v1 has no stagger sugar — write siblings with explicit delays):

```xml
<VStack>
  <Animation type="slidein-left" delay="0.0s"><Btn>A</Btn></Animation>
  <Animation type="slidein-left" delay="0.05s"><Btn>B</Btn></Animation>
  <Animation type="slidein-left" delay="0.10s"><Btn>C</Btn></Animation>
</VStack>
```

**Score popup (count + char-color combo):** Nest animations sharing the same `<Text>`:

```xml
<Animation count="0:1000" format="{0:N0}" duration="2s">
  <Animation char-color="#ffffff:#ffcc33" char-stagger="0.05s" delay="2s" duration="0.4s">
    <Text id="score">0</Text>
  </Animation>
</Animation>
```

**Caveats:**

- `char-color` assumes Text content doesn't change during animation; concurrent `count` + `char-color` on the same `<Text>` may produce wrong-char colors as text length changes
- `<Animation>` adds a `CanvasGroup` and an inner `_offsetProxy` GameObject (transparent to layout, but visible in the Hierarchy)
- `on="open"` fires once at Screen open; Variant ReSolve does **not** re-fire
