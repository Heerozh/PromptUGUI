# UI sound cues — `sfx=`

PromptUGUI never plays audio. When the user **really acts** — clicks, flips, picks, expands, drags — the control raises a
*sound cue* on `UI.Sfx.OnCue` (C# side: see the **scripting-promptugui-csharp** skill, *UI sound cues*). The game maps the
cue's **key** to a clip. Every built-in control has a **default key**, `kind.action` (`btn.click`, `tab.select`,
`drag.land`…), so a game gets a full set of sounds without writing any XML. `sfx=` is only for overrides and mutes.

What never raises a cue: code (`IsOn = true`, `Expand()`, `GoTo()`, `onClick.Invoke()`), attribute passes, a Variant /
Theme / locale ReSolve, the state a Screen opens with, autoplay, a list re-bind, teardown. A cue is the user's action.

## Syntax

`sfx` is legal on every element and on `<Screen>`, with `.variant` forms.

```xml
<Btn sfx="confirm">…</Btn>                              <!-- every action of this element → confirm (deny excepted) -->
<Btn sfx="none">…</Btn>                                 <!-- every action muted (deny excepted) -->
<Toggle sfx="checked:switch_on unchecked:switch_off"/>  <!-- per action -->
<Collapsible sfx="drawer collapse:none">…</Collapsible> <!-- drawer for all, collapse muted -->
<Btn sfx="deny:locked">…</Btn>                          <!-- only the refusal sound -->
<Btn sfx="none deny:none">…</Btn>                       <!-- completely silent -->
<Btn sfx="{{clickSfx}}">…</Btn>                         <!-- a template argument left empty = not written = default -->
```

- Items separated by spaces or commas; an item is `key` or `action:key`.
- **Key**: any text without spaces, `,` or `:`, case kept — `ui/confirm`, `Confirm`, `pages.flip` are all fine. The game
  defines keys; lint never checks them. `none` is reserved: it mutes.
- **Action**: lower case. The built-in ones are in the table below.
- At most one bare key; each action at most once.
- **A bare key and `none` never cover `deny`.** `deny` is the library's "you can't do that" feedback, not an action of the
  control: a buy button that plays its own sound from C# writes `sfx="none"`, and still answers a click while disabled
  with the refusal sound. Only `deny:key` / `deny:none` change it.
- An empty value is "not written".
- **Whole-value replacement** between layers, like every attribute: an inline `sfx` replaces a style's or a template
  body's `sfx` entirely — the action items are not merged one by one.

## Actions and default keys

| Element | kind | Action | When (the user's own act) | Looked up on |
|---|---|---|---|---|
| `<Btn>` | `btn` | `click` | left click / gamepad·keyboard Submit | the Btn |
| | | `deny` | a click / Submit on a disabled Btn (itself or through a disabled ancestor) | the Btn |
| `<Toggle>` | `toggle` | `checked` · `unchecked` | the click turned it on · off | the Toggle |
| | | `deny` | | the Toggle |
| `<Tab>` | `tab` | `select` | the user picked a tab that was **not** selected | the Tab → its `<TabBar>` / `<TabMenu>` |
| | | `deselect` | `allowSwitchOff="true"`: the selected tab clicked off | same |
| | | `deny` | | same |
| `<TabMenu>` | `tabmenu` | `expand` · `collapse` | the handle opens it; the handle again, an outside press, Escape or re-picking the current row closes it | the TabMenu |
| | | `deny` | a disabled handle | the TabMenu |
| `<Dropdown>` | `dropdown` | `expand` · `collapse` | opened; closed by the catcher, Escape / B, or re-picking the current option | the Dropdown |
| | | `select` | another option picked — the list closes as a consequence, no `collapse` | the Dropdown |
| | | `deny` | | the Dropdown |
| `<Popover>` | `popover` | `expand` · `collapse` | its `on="click@…"` button opens / closes it; outside press, Escape | the Popover |
| `<Collapsible>` | `collapsible` | `expand` · `collapse` | the header clicked | the Collapsible |
| | | `deny` | a disabled header | the Collapsible |
| `<Carousel>` | `carousel` | `flip` | a swipe that turned the page, a dot, a slide box's side card (a disabled carousel takes neither) | the Carousel |
| `<Screen>` (page / plain) | `screen` | `open` · `close` | every normal open · start of a normal close | the Screen |
| `<Screen>` (as a dialog) | `modal` | `open` · `close` | same | the Screen |
| a `drag=` source | `drag` | `lift` | picked up | the source |
| | | `land` | went into a target (as its landing starts; a deferred drop: at `Accept`) | **the target → the source** |
| | | `return` | went home: let go over nothing / a refusing target, a `Reject` (also after `Defer`), Escape | the source |
| a `<ScrollList reorder>` row | `reorder` | `lift` · `land` · `return` | row picked up · put in a new place · put back in its own | the row's root → the list |

`<TabBar>` raises nothing itself but is looked up for its tabs; `<TabMenu>` for its tabs too; a `<ScrollList reorder>` for
its rows; a `drop=` target for what lands in it.

**Lookup:** for each element on the chain, nearest first — its `action:key` wins; else (not for `deny`) its bare key;
else the next element. Nothing decided → the default key `kind.action`.

```xml
<TabBar sfx="select:page_turn">
  <Tab>Bag</Tab>                       <!-- picking it: page_turn -->
  <Tab sfx="special">Shop</Tab>        <!-- picking it: special (its own bare key comes first) -->
  <Tab interactable="false">Forge</Tab><!-- clicking it: tab.deny (select: does not reach deny) -->
</TabBar>
```

**An element with several roles writes `action:`, not a bare key.** A clickable reorder row `<Btn sfx="select">` also
re-keys its `lift` / `land` / `return`; a clickable drop slot `<Btn drop="item" sfx="slot">` re-keys every `land` into
it (the target is looked up before the source). Write `sfx="click:select"` / `sfx="click:slot"`.

## Only the user's own action

- **A state that did not change raises nothing** — re-clicking the selected tab, re-picking the current option, a short
  swipe that springs back. `deny` is the exception: it reports exactly that nothing happened.
- **Consequences raise nothing**: the toggle a radio group turns off, the tab a bar turns off, accordion siblings, the
  list a pick closes, child popovers closed with their parent, a popover replaced by another opening.
- **One gesture can raise several cues** on purpose — a button click plus the dialog it opens, a popover trigger's
  `btn.click` plus `popover.expand`, a pass-through outside press closing a popover (on the press) plus the click on the
  button below (on the release). The game picks per frame (C# skill).
- A Screen that has started closing raises no control cues (a captured reorder that finishes on the ghost included).

## Pages: no cue — put the key on the button

`<Pages>` has no user gesture: a page flips only when C# sets `Selected` (or a Variant does). The button whose `OnClick`
flips the page already raised its click. To make it sound like a page turn, key the button:

```xml
<Btn id="toDetail" sfx="pages.flip">Details</Btn>   <!-- OnClick: pages.Selected = "detail" -->
```

`pages.flip` is just a key name the game maps; the library never raises it by itself. `sfx=` on `<Pages>` is
`PUI-SFX-INERT`. The same recipe for arrows wired to `<Carousel>` in C#: `<Btn sfx="carousel.flip">`.

## Screens and dialogs

```xml
<Screen name="Shop" sfx="open:shop_open close:none"> … </Screen>
<Screen name="Hud"  sfx="none"> … </Screen>                  <!-- always-on screens opened at boot -->
```

- `screen` or `modal` is decided by **how it was opened**: `UI.Open` / a Router page → `screen`; `UI.Modal.*` dialogs
  (MessageBox, InputBox, MarkdownBox, CenteredSlideBox, custom requests) and `RoutePresent.Modal` → `modal`.
- Silent by themselves: the Loading overlay, toasts, the tutorial overlay, `PromptUGUIDocumentHost`.
- A close is cued when the Screen starts closing normally (`UI.Close`, the Router, a dialog button, Escape). Teardown
  (hot reload, `UnloadAll`, `Dispose`) raises nothing; hot reload reopens silently.
- The built-in dialogs' buttons are ordinary `<Btn>`s (`btn.click`); a CenteredSlideBox with one button sounds the same
  when its centred card is clicked.

## Templates, styles, themes, variants

```xml
<Style name="btn-primary" sfx="confirm"/>
<Style name="btn-cancel"  sfx="cancel"/>
<Btn class="btn-primary">OK</Btn>

<Template name="DangerButton"><Btn sfx="danger">…</Btn></Template>
<DangerButton sfx="none">Delete draft</DangerButton>     <!-- the call site wins on the instance root -->
```

- A template invocation carries `sfx` onto the instance **root** (like `hidden` / `drag`), over the body root's value —
  also when the body root got it from a style.
- If the root raises no cue (a card whose root is a `<VStack>` with the button inside), the call site's `sfx` does
  nothing (`PUI-SFX-INERT`). Expose a Param **with another name**:

  ```xml
  <Template name="ShopCard">
    <Param name="buySfx" default=""/>                     <!-- empty default = the default sound -->
    <VStack> … <Btn id="buy" sfx="{{buySfx}}">Buy</Btn> </VStack>
  </Template>
  <ShopCard buySfx="purchase"/>
  ```

  `<Param name="sfx">` is a parse error.
- Theme `<Style>`s may carry `sfx` (a retro theme with retro keys). Switching themes re-reads it.
- `sfx.mobile="none"` needs no base: `sfx` is resolved on every pass, so a cleared variant or theme falls back to the
  default.

## Lint

| Code | When |
|---|---|
| `PUI-SFX-VALUE` | Malformed value: `click:` (no key), `:x` (no action), two bare keys, an action twice — inline, in `<Style>`s and theme `<Style>`s, on `<Screen>`. The runtime warns once per node and plays the defaults. |
| `PUI-SFX-ACTION` | An action written inline on a built-in element that it never does or is looked up for (`<Btn sfx="checked:x">`), or misspelt (`clik:`). `lift` / `land` / `return` pass anywhere (drag and drop or reorder can be turned on by the call site, the list or C#); styles and template body roots are not judged. |
| `PUI-SFX-INERT` | `sfx` on `<Pages>`; inline `sfx` on a built-in element that neither raises nor is looked up for a cue (`<Frame>`, `<Text>`, `<Image>`, `<VStack>`…, a `<ScrollList>` without `reorder`) and has no `drag=` / `drop=`; a call site's `sfx` on a template whose root is such an element. Not reported: styles, template body roots, a static child of a `reorder` list, a value with `lift:` / `land:` / `return:` (you will turn drag on from C#). |
