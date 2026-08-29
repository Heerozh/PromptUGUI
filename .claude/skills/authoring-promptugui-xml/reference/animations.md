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
| `manual`           | Does not auto-fire; C# must call `Fire()`                                                                                                                                                                         |

**`expand` / `collapse` are not called `open` / `close`** — `on="open"` already means "the Screen opened". They exist because a `<TabMenu>`'s panel is an internal node you cannot wrap in an `<Animation>`; the panel's own entrance is the menu's `transition=` attribute, and these are for animating the **rows** inside it. They resolve upward exactly like `state-*`, and the popup they live in is switched off while collapsed — which the ancestor walk accounts for.

**`state-*` source resolution is UPWARD**: unlike `click` / `hover-enter` / `press` (which search this Trigger's **subtree downward** for a `<Btn>` / `<Image>` source), `state-*` resolves to the nearest `<Btn>` / `<Tab>` / `<Toggle>` **ancestor** (`state-...@<id>` targets a specific source control by id). A bare `state-*` with no `<Btn>` / `<Tab>` / `<Toggle>` ancestor is a runtime error (and `PUI-STATE-NO-SOURCE` in the lint CLI; `@id` forms and Template bodies are exempt). They **fire on entering** the state, so `state-normal` fires once at open and `<Animation on="state-pressed">` plays on press with `<Animation on="state-normal">` as its natural revert.

**`hover-enter` vs `state-hover`**: `hover-enter` / `press` are **raw pointer events** (`PointerEventRelay`, `IPointer*Handler`, downward source) — they fire on any pointer enter / down regardless of interactable state. `state-hover` / `state-pressed` come from the control's **Selectable state machine** (disabled-aware, drag-cancel-aware, upward source); a disabled `<Btn>` / `<Tab>` / `<Toggle>` never emits `state-hover` / `state-pressed`, only `state-disabled`.

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

- Three families are mutually exclusive: writing both `type=` and `translate=` → parse error
- `count=` and `char-color=` are mutually exclusive within the text family
- `on="click"` requires a unique `<Btn>` descendant; multiple → use `on="click@<id>"` to disambiguate; zero `<Btn>` → error

## Patterns

**Menu rows entering with the popup** — one `<Animation on="expand">` per row, reused via a Template:

```xml
<Template name="ChannelRow">
  <Param name="text"/>
  <Animation on="expand" type="slidein-left" duration="0.12s">
    <Tab id="tab" text="{{text}}"/>
  </Animation>
</Template>

<TabMenu id="channel" itemTemplate="ChannelRow" popupWidth="240" padding="8"/>
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
