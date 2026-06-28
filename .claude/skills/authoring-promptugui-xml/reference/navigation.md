# Gamepad / Keyboard Navigation

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). Read this before using `<FocusCursor>`, `focus=`, `nav=`, or `navUp/navDown/navLeft/navRight` on any control. For the C# setup call and `screen.Focus(...)`, see the **scripting-promptugui-csharp** skill.

PromptUGUI ships an optional directional navigation model for gamepad controllers and keyboards. The feature is dormant until you call `UI.UseGamepadNavigation()` once at startup; without it no cursor appears and no navigation attributes are processed.

**Requires Unity's New Input System** (`com.unity.inputsystem`). Without it, `UI.UseGamepadNavigation()` logs one `Debug.LogWarning` and returns — no runtime error, no navigation.

---

## Navigation modes

The library switches between two modes automatically based on the last device used:

| Mode | Triggered by |
| --- | --- |
| **Pointer** | Mouse move, mouse click, or touchscreen tap |
| **Directional** | Gamepad left-stick / d-pad / South (confirm) / East (back); keyboard arrow keys, Tab, Enter |

In **Pointer** mode the focus cursor is hidden and no selected control shows a highlight — the mouse pointer is the sole feedback. In **Directional** mode the cursor appears and the selected control shows its hover visual.

Touch-only devices (phones, tablets with no mouse attached) remain in Pointer mode at all times; the cursor and the directional highlight never appear on them.

---

## Focus visual

When a control is selected in Directional mode it enters `InteractState.Focused`. In v1 `Focused` **reuses the control's hover visual** (`hoverColor` / `hoverModulate`) — there is no separate `focusColor` attribute. If the control has no hover colour authored, the control appearance does not change on selection; the moving `<FocusCursor>` is the sole visual feedback.

The hover tint appears only in Directional mode. Switching back to a mouse removes it instantly (the mode signal triggers `RefreshState` on the previously-selected control).

---

## `<FocusCursor>` — the selection cursor

`<FocusCursor>` is a **`<Screen>`-level child element** (a direct child of `<Screen>`, like `<SafeArea>`). Its child subtree defines the cursor visual. The library creates a floating overlay from it and slides it to the edge of the focused control each frame (using a 0.12s `OutCubic` tween). In Directional mode the overlay is visible; in Pointer mode it is hidden (`CanvasGroup.alpha = 0`).

```xml
<Screen name="MainMenu" reference="1920x1080">
  <!-- cursor visual — direct child of <Screen>, not inside content -->
  <FocusCursor side="left" offset="-4,0">
    <Image anchor="center" size="24x24" sprite="ui:cursor-arrow"/>
  </FocusCursor>

  <!-- screen content below -->
  <VStack anchor="center" spacing="12">
    <Btn id="play"     focus="true">Play</Btn>
    <Btn id="settings">Settings</Btn>
    <Btn id="quit">Quit</Btn>
  </VStack>
</Screen>
```

### `<FocusCursor>` attributes

| Attribute | Values | Default | Description |
| --------- | ------ | ------- | ----------- |
| `side` | `left` / `right` / `top` / `bottom` | `left` | Which edge of the focused control the cursor anchors to. |
| `offset` | `x,y` (design units) | `0,0` | Additional shift from the computed edge point. Negative x moves the cursor further left when `side="left"`. |

The cursor child subtree accepts the full XML feature set — `<Image>`, `<Icon>`, `<Animation>` for an idle bob or pulse, Variants, etc. The `<FocusCursor>` element itself does NOT accept `anchor`, `size`, or `margin`; those are managed by the runtime overlay.

The library renders **one cursor overlay per Screen**. Only the **first child** of `<FocusCursor>` is used; additional children are silently ignored in v1. If more than one `<FocusCursor>` appears in a Screen, the **last** one in document order is used; earlier declarations are silently ignored.

`<FocusCursor>` is parsed as a structural Screen annotation (removed from the control tree). It is NOT a registered control and does NOT appear in `screen.Get<T>(id)`. You cannot place `id=` on it. Ids placed on elements inside the `<FocusCursor>` child subtree are also not accessible via `screen.Get<T>()` — the entire overlay subtree is hoisted outside the control tree.

### Built-in default cursor

If a Screen (or a built-in modal) does not declare `<FocusCursor>`, the library falls back to a built-in caret sprite from `Resources/PromptUGUI/Navigation/FocusCursor.ui.xml`. You do not need to add `<FocusCursor>` to every Screen — declare it only when you want a custom cursor.

### Cursor animation

Use `<Animation on="loop">` inside the cursor child for an idle motion:

```xml
<FocusCursor side="left" offset="-6,0">
  <!-- arrow that bobs left-right continuously -->
  <Animation translate="-4,0:0,0" duration="0.5s" easing="out-back" on="loop">
    <Image anchor="center" size="24x24" sprite="ui:cursor-arrow"/>
  </Animation>
</FocusCursor>
```

See [`animations.md`](animations.md) for the full `on="loop"` and `<Animation>` syntax.

### Templating the cursor

`<FocusCursor>` can invoke a `<Template>` defined in the same file:

```xml
<Template name="PulseCursor">
  <!-- pulse preset: yoyo scale to 1.05× (slight throb) -->
  <Animation type="pulse" duration="0.8s" on="loop">
    <Image anchor="center" size="20x20" sprite="ui:cursor-gem"/>
  </Animation>
</Template>

<Screen name="MainMenu">
  <FocusCursor side="left" offset="-4,0">
    <PulseCursor/>
  </FocusCursor>
  ...
</Screen>
```

---

## Initial focus — `focus="true"`

Add `focus="true"` to the selectable control that should receive focus when the Screen (or modal) opens in Directional mode:

```xml
<VStack anchor="center" spacing="12">
  <Btn id="play"     focus="true">Play</Btn>   <!-- selected on open -->
  <Btn id="settings">Settings</Btn>
  <Btn id="quit">Quit</Btn>
</VStack>
```

If no control carries `focus="true"`, the **first focusable control in document (depth-first pre-order)** is selected automatically. In Pointer mode `focus="true"` is still parsed but has no visual effect until the player picks up a gamepad or presses a keyboard key.

### Limitation: BindItems-generated controls

`focus="true"` only works on controls in the **static node map** — controls declared directly in the XML. It does NOT apply to controls created dynamically via `BindItems` (items inside `<ScrollList>`, `<Carousel>`, or `<TabBar>`), because those controls are not in the parsed node tree at Screen-open time. To focus a named static control from C# call `screen.Focus("id")` after binding; `screen.Focus` throws `KeyNotFoundException` if the id is not in the static node map (BindItems-generated items are never there). Called before `UI.UseGamepadNavigation()`, `Focus` still sets the EventSystem selection but no cursor overlay appears. To hand directional-navigation scroll control to a dynamic list, focus the list container itself (e.g. `screen.Focus("itemList")`).

---

## Navigation overrides

### `nav="none"` — skip a control

Mark a selectable control with `nav="none"` to remove it from the navigation graph entirely. Directional keys skip over it as if it did not exist. The control is still interactive via pointer (click / touch).

```xml
<!-- decorative btn that should never receive gamepad focus -->
<Btn nav="none" color="ui-bg">Decoration</Btn>
```

Controls with `interactable="false"` are excluded from the graph by uGUI automatically; `nav="none"` handles enabled controls you still want to exclude.

### `navUp` / `navDown` / `navLeft` / `navRight` — explicit targets

Pin specific directional inputs to a target control by id:

```xml
<Btn id="play"     navDown="settings">Play</Btn>
<Btn id="settings" navUp="play" navDown="quit">Settings</Btn>
<Btn id="quit"     navUp="settings">Quit</Btn>
```

**Unspecified directions auto-fill.** Writing only `navDown="settings"` on `play` does **not** dead-end up, left, or right — those three still resolve via uGUI's geometric neighbor algorithm. Only the directions you explicitly write are pinned.

**Missing id = runtime exception.** If a `navUp/navDown/navLeft/navRight` value does not match any `id` in the same Screen, `ExplicitNavigationResolver` throws `KeyNotFoundException` at Screen-open time. The CLI lint rule `PUI-NAV-UNKNOWN-TARGET` catches this statically before runtime.

**Variant-overridable.** Use `navDown.mobile="id2"` to change the target under a variant, following normal variant rules.

### Quick attribute table

| Attribute | Applies to | Value | Effect |
| --- | --- | --- | --- |
| `focus` | Selectable controls | `"true"` | Mark as initial selection on Screen open (document order wins when multiple) |
| `nav` | Selectable controls | `"none"` | Remove from navigation graph entirely |
| `navUp` | Selectable controls | element `id` | Explicit up target |
| `navDown` | Selectable controls | element `id` | Explicit down target |
| `navLeft` | Selectable controls | element `id` | Explicit left target |
| `navRight` | Selectable controls | element `id` | Explicit right target |

**Selectable controls** (the only tags that accept these attributes): `<Btn>`, `<Tab>`, `<Toggle>`, `<Slider>`, `<Dropdown>`, `<InputField>`, `<ScrollList>`.

Writing nav attributes on a non-selectable tag (`<Frame>`, `<Image>`, `<Text>`, etc.) is a no-op at runtime and triggers `PUI-NAV-ON-NON-SELECTABLE` in the lint CLI and a runtime warning.

**Template invocations:** `focus` and `nav*` are **not** in the auto-forward set for Template invocations (unlike `anchor`, `size`, `margin`, `hidden`, `interactable`). Writing them directly on an invocation throws a `TemplateException` ("unknown attribute") at expansion time. Expose them as `<Param>` values in the template body:

```xml
<Template name="MenuBtn">
  <Param name="label"/>
  <Param name="focus" default="false"/>
  <Btn focus="{{focus}}" size="280x64">{{label}}</Btn>
</Template>

<MenuBtn id="play" label="Play" focus="true"/>  <!-- valid: focus is a declared Param -->
<MenuBtn id="quit" label="Quit"/>
```

---

## Modal focus trap

While a modal is open (MessageBox, InputBox, MarkdownBox, CenteredSlideBox, or any custom modal), directional navigation is **trapped inside the modal**. Arrow keys and gamepad stick cannot reach controls behind the modal, even if they are physically adjacent on screen. This is enforced every frame by `NavigationController`.

On close, the selection is **restored** to the control that was focused when the modal opened.

No XML markup is required for any of this — the modal stack wires the trap automatically.

---

## Lint rules

| Code | Category | Description |
| --- | --- | --- |
| `PUI-NAV-ON-NON-SELECTABLE` | CLI + runtime warning | `nav*` or `focus` on a tag with no uGUI `Selectable` component at runtime. The nav attribute is a no-op (no crash), but a `Debug.LogWarning` is logged at Screen instantiation. |
| `PUI-NAV-UNKNOWN-TARGET` | CLI only | A `navUp/navDown/navLeft/navRight` value that does not match any `id` in the same Screen. The runtime throws `KeyNotFoundException` on open; the CLI catches this statically. |

---

## Not in v1

- Per-state focus colours (`focusColor`, `focusModulate`) — the hover visual is reused.
- `<Carousel>` gamepad navigation — Carousel items are not in the static Selectable graph.
- Old Input Manager (`ENABLE_LEGACY_INPUT_MANAGER`) support — the New Input System package is required.

---

## Complete examples

### Example 1: Main menu with a custom animated cursor

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <!--
    Reusable menu button template.
    focus and color are NOT auto-forwarded from invocations — declare them as <Param>.
  -->
  <Template name="MenuBtn">
    <Param name="label"/>
    <Param name="focus" default="false"/>
    <Param name="color" default="#3B82F6"/>
    <Btn color="{{color}}" size="280x64" focus="{{focus}}"
         hoverModulate="#dddddd" pressedModulate="#aaaaaa">
      <Text anchor="center" fontSize="22" color="#FFFFFF">{{label}}</Text>
    </Btn>
  </Template>

  <Screen name="MainMenu" reference="1920x1080">

    <!-- Custom cursor: a gem that pulses while idle (pulse preset: yoyo scale to 1.05×) -->
    <FocusCursor side="left" offset="-8,0">
      <Animation type="pulse" duration="0.7s" on="loop">
        <Image anchor="center" size="24x24" sprite="ui:cursor-gem"/>
      </Animation>
    </FocusCursor>

    <!-- Full-screen background -->
    <Image anchor="stretch" sprite="bg/main"/>

    <!-- Menu column — arrow keys / gamepad navigate naturally top-to-bottom -->
    <VStack id="menu" anchor="center" size="300x220" spacing="16">
      <MenuBtn id="play"     label="Play"   focus="true"/>
      <MenuBtn id="settings" label="Settings"/>
      <MenuBtn id="quit"     label="Quit"   color="#DC2626"/>
    </VStack>

  </Screen>

</PromptUGUI>
```

C# startup:

```csharp
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using R3;

async void Start()
{
    // Enable gamepad/keyboard navigation (idempotent; auto-creates EventSystem if missing)
    UI.UseGamepadNavigation();

    UI.UseResourcesResolver("UI");
    await UI.LoadDocumentAsync("screens/MainMenu");
    var screen = UI.Open("MainMenu");

    screen.Get<Btn>("play").OnClick
          .Subscribe(_ => StartGame()).AddTo(screen);

    screen.Get<Btn>("settings").OnClick
          .Subscribe(_ => OpenSettings()).AddTo(screen);

    screen.Get<Btn>("quit").OnClick
          .Subscribe(_ => Application.Quit()).AddTo(screen);
}

async void OpenSettings()
{
    // MessageBox is a built-in modal: navigation is automatically
    // trapped inside it and the cursor returns to "settings" on close.
    await MessageBox.Open("Settings coming soon!", MsgBtn.OK);
}

// Programmatic focus from C# — valid only for controls in the static node map:
void SetInitialFocus(IScreen screen)
{
    screen.Focus("play");  // moves EventSystem selection to the named static control
    // BindItems-generated items are NOT in the static node map and cannot be focused
    // by id or numeric index — there is no such index path form. Focus the list
    // container itself ("itemList") to hand scroll control to the ScrollList widget.
}
```

---

### Example 2: Character select grid with explicit navigation overrides

When controls are laid out in a 2D grid, uGUI's geometric neighbor resolution may not produce the intended wrapping. Pin the horizontal neighbors explicitly and let uGUI auto-fill the vertical:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <Screen name="CharacterSelect" reference="1920x1080">

    <!-- Right-hand-side cursor for this screen -->
    <FocusCursor side="right" offset="8,0">
      <Image anchor="center" size="32x32" sprite="ui:cursor-arrow-right"/>
    </FocusCursor>

    <Image anchor="stretch" sprite="bg/select"/>

    <!-- 3-column character grid -->
    <Grid id="grid" columns="3" cellSize="200x200" anchor="center">
      <!--
        Horizontal neighbors pinned explicitly so wrap-around is clean.
        Vertical navigation (navUp / navDown) omitted — uGUI fills these
        geometrically (c0→c3, c3→c0, etc.).
      -->
      <Btn id="c0" navRight="c1"            focus="true">Warrior</Btn>
      <Btn id="c1" navLeft="c0" navRight="c2"            >Mage</Btn>
      <Btn id="c2" navLeft="c1"                          >Rogue</Btn>
      <Btn id="c3" navRight="c4"                         >Paladin</Btn>
      <Btn id="c4" navLeft="c3" navRight="c5"            >Druid</Btn>
      <Btn id="c5" navLeft="c4"                          >Ranger</Btn>
    </Grid>

    <!-- Confirm button is reachable from the bottom row via navDown auto-fill -->
    <Btn id="confirm" anchor="bottom-center" size="200x60" margin="_,_,40,_"
         color="#22C55E">
      Select
    </Btn>

  </Screen>

</PromptUGUI>
```
