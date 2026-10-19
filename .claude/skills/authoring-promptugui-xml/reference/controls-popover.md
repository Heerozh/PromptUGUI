# `<Popover>` — a floating panel anchored to any control

An emoji picker next to a chat field, a "…" menu on a list row, a right-click menu, a "where does this number come from" breakdown: a panel that floats above the page, beside some element, and goes away on its own. Modelled on HTML's `popover="auto"`: a press outside or Escape closes it (light dismiss), opening an unrelated popup closes it, and a popover opened from inside another one is its child.

```xml
<!-- Emoji: the button toggles it, it floats above the button, right-aligned; a cell inserts at the caret -->
<HStack height="44" spacing="4">
  <InputField id="chat" width="stretch" height="stretch" placeholder="Say something…"/>
  <Btn id="emojiBtn" width="44" height="stretch">
    <Icon name="ui/smile" anchor="center" size="24x24"/>
    <Show on="expand@emojiPop"><Frame anchor="stretch" color="accent/0.25"/></Show>   <!-- lit while open -->
  </Btn>
</HStack>

<Popover id="emojiPop" on="click@emojiBtn" placement="top-end" padding="6" radius="8">
  <ScrollList id="emojiGrid" itemTemplate="EmojiCell" columns="8" cellSize="36x36" spacing="2" size="304x152"/>
</Popover>

<!-- Row menu: manual, opened from C# per row; a fixed width, rows fill it -->
<Popover id="rowMenu" width="160" padding="4" spacing="2">
  <Btn id="menuRename" class="menu-row" width="stretch" height="36">Rename</Btn>
  <Btn id="menuDelete" class="menu-row" width="stretch" height="36">Delete</Btn>
</Popover>
```

```csharp
var chat = screen.Get<InputField>("chat");
screen.Get<ScrollList>("emojiGrid").BindItems(Observable.Return<IReadOnlyList<string>>(Emoji.Common),
    (Btn cell, string e) =>
    {
        cell.Text = e;
        cell.OnClick.Subscribe(_ => chat.Insert(e)).AddTo(cell);   // at the caret the edit left
    }).AddTo(screen);

// One menu serves every row
var menu = screen.Get<Popover>("rowMenu");
Item target = null;
list.BindItems(items, (IControl row, Item it) =>
{
    var more = row.Get<Btn>("more");
    more.OnClick.Subscribe(_ => { target = it; menu.Toggle(more); }).AddTo(row);
}, key: it => it.Id).AddTo(screen);
screen.Get<Btn>("menuDelete").OnClick.Subscribe(_ => { Delete(target); menu.Close(); }).AddTo(screen);

// Right-click menu: from your own pointer callback
menu.OpenAt(eventData.position);
```

## Where it goes

- **Anywhere** in a Screen or a Template. It **never takes part in layout**: inside a `<VStack>` / `<HStack>` / `<Grid>` it takes no slot and adds no size. Its parent's layout checks skip it.
- Best placed near its trigger, or at the Screen's top level. It must be in the **same Screen** as its trigger; when the trigger is inside a template instance, put the popover in the same instance (ids resolve lexically — below).
- **Not** directly inside a `<ScrollList>` / `<TabBar>` / `<TabMenu>` / `<Carousel>` / `<Pages>` (it would be taken for a row, a tab, a card or a page), and **not** inside a `<Btn>` / `<Tab>` / `<Toggle>` (the panel would take that control's hover, press and disabled looks) — `PUI-POPOVER-PARENT`.
- **Its children are the panel's content**, laid out top to bottom like a `<VStack>`. A `flow="false"` child is free-positioned as usual (a backdrop, a badge); `<Decor>` decorates the panel.
- **Nesting**: a popover may hold another popover, a `<TabMenu>`, a field with `<Suggestions>` (see *Nesting*).
- The node **is the panel**: a nested Canvas lifts it above the page and out of every ancestor mask. It sorts like every popup: above its page, below the modal band.

## Attributes

| Attribute | Values | Default | Meaning |
|---|---|---|---|
| `on` | `click@<id>` \| `manual` | `manual` | Who opens it. `click@<id>`: pressing that `<Btn>` toggles it, and what `<id>` names is the anchor (a template instance: its one `<Btn>` clicks, the instance is the anchor). `manual`: only C# opens it |
| `placement` | `top` / `bottom` / `left` / `right` / `over`, optionally `-start` / `-center` / `-end` | `bottom-start` | Preferred side of the anchor, and the alignment on the other axis. `over` covers the anchor instead (see *Over the anchor*) |
| `gap` | px, ≥ 0 | `4` | Distance from the anchor. Ignored by `over` (`PUI-POPOVER-GAP`) |
| `dismiss` | `pass` \| `block` | `pass` | A press outside: `pass` closes the popover **and the press goes on** to what it landed on; `block` puts a full-screen catcher behind the panel that eats the press (like `<TabMenu>`) |
| `closeOnEditEnd` | bool | `false` | The edit of a field in the panel ending closes it (reason `EditEnded`) — a search popover that is just a field. See *A field in the panel* |
| `transition` | duration (`0.15s` / `150ms` / seconds) | `0.15s` | Open / close: a fade plus an 8 px slide out from the anchor's side (`over`: the fade only). `0` = instant |
| `width` / `height` / `size` | number / `hug` / `clamp(min, hug, max)` | `hug` | Panel size (see *Size*) |
| `padding` · `spacing` · `childAlign` | as `<VStack>` | `0` · `0` · `upper-center` | Layout inside the panel |
| `color` · `sprite` · `tint` | as `<TabMenu>`'s panel | default popup skin | The panel's background |
| procedural attrs (`radius` / `border*` / `glow*` / `glass` …) + `<Decor>` | as `<Frame>` | — | All land on the panel |
| `interactable` | bool | `true` | `false`: it will not open; an open one closes |

**Ignored, with a lint warning** (`PUI-POPOVER-LAYOUT-ATTR`, also a runtime warning): `anchor` / `margin` / `pivot` / `flow` / `scale` / `hidden`. Placement owns the position, opening and closing own the visibility.

**No `raycastTarget`**: the panel always takes the pointer, so a press on its empty area does not fall through and does not count as "outside".

## Opening

- **`on="click@<id>"`**:
  - Pressing that `<Btn>` opens the popover anchored to it; when it is open and anchored to that button, the press closes it (reason `Trigger`).
  - The id resolves like `<Trigger on="click@…">`, lexically: the popover's own subtree is skipped, then each enclosing scope (a template instance's ids), then the Screen. Resolution waits until whatever is being built is complete — the trigger may be written before or after the popover, in the same template row too.
  - **A path** reaches into a template instance: `on="click@search/btn"` — the button is then the anchor.
  - **A template instance whose root is not a `<Btn>`** (a `<Frame>` with a button and a badge): `on="click@search"` listens to the **one** `<Btn>` inside it, and the popover hangs from the **instance**. Pressing that button again closes it (`Trigger`); `Open()` / `Toggle()` use the instance; on close a selection left in the panel goes to that button (the instance is not selectable). No `<Btn>` inside, or several, is an error — name one with a path.
  - An id that is missing, names something with no `<Btn>` (or several) in it, or names a control inside the popover itself throws `InvalidOperationException`.
  - A variant may rewrite `on`; the popover re-subscribes.
- **`manual`** (default): C# opens it (`Open(anchor)` / `OpenAt(point)` — scripting skill).
- **Pressing the trigger while open** closes it in both modes, along different paths: with `pass` the trigger counts as "inside", so the press does not close it and the click toggles it; with `block` the press lands on the catcher and closes it, the click is eaten.
- **It does not open** (nothing happens) when the popover is not interactable; the anchor is inactive or not interactable; the popover's parent is inactive; or its layer is covered — a dialog above its screen, or the Loading overlay showing.

## Closing

| Source | `PopoverCloseReason` | Notes |
|---|---|---|
| `Close()`; `Open(another anchor)` while open; becoming non-interactable | `Code` | |
| Pressing the `on=` trigger again | `Trigger` | |
| Escape, gamepad Start / B | `Escape` | Top of the popup chain only |
| A press outside | `OutsidePress` | `pass`: outside the panel, its anchor and any popup above it on the chain. `block`: the catcher was pressed |
| A dialog, a routed Page / Modal, Loading appears | `Covered` | |
| The anchor stops being usable | `AnchorLost` | See *Following* |
| An unrelated popup opens (another popover, a `<TabMenu>`, a suggestion list) | `Replaced` | |
| Its Screen starts closing; an ancestor is switched off; `Dispose` | `HostGone` | |
| `closeOnEditEnd="true"` and the edit in the panel ended | `EditEnded` | See *A field in the panel* |

- **Game code with its own back key** (leave a mode, clear a selection on Escape / Cancel) sees the same press that closed the popover: check `UI.EscapeConsumedThisFrame` first (scripting skill).
- A popover whose Screen is torn down without `UI.Close` (a scene change, leaving Play mode) leaves the chain silently — no `OnClosed`, nothing left to tell.
- Pressing something **inside** the panel never closes it — an emoji picker must take several taps. A menu closes itself from C# (`menu.Close()` in the row handlers).
- While a tutorial `Block` step holds input, neither Escape nor a press outside closes it.

## Placement

- **`placement` = side[-align].** The side is a preference: it flips only when the preferred side cannot hold the panel and the opposite side has more room. The alignment is on the cross axis — `start` lines up with the anchor's left edge (its top edge when the side is left / right), `center` centres, `end` lines up with the right (bottom) edge. Pushed back inside the bounds if it overflows, start edge first. **`start` is left / top: there is no RTL.**
- **Neither side fits**: the roomier side, pushed back inside along the main axis — the panel may then cover the anchor. Larger than the bounds: the start edge stays inside.
- **Bounds**: the safe area minus the visible soft keyboard.
- **The side is locked while open**: content growing or the anchor moving only re-clamps it — the panel never jumps from side to side. Only a new Canvas size (rotation, a resized window) chooses again; a keyboard sliding in only re-clamps on the locked side.
- **`OpenAt(screen point)`**: the anchor is that point, zero-sized. With the default `bottom-start` the panel's top-left corner sits at the pointer, like a common context menu.

### Over the anchor

`placement="over"`, optionally `-start` / `-center` / `-end` (`over` = `over-start`): the panel **covers** its anchor instead of sitting beside it — an icon that unfolds into a field in place.

- **Vertically** the panel is centred on the anchor; **horizontally** `start` lines up the left edges, `center` the centres, `end` the right edges. A panel wider than the anchor reaches out from the aligned edge (`over-end` grows to the left); a smaller one covers part of it.
- **No flip** — there is no other side. Overflowing the bounds, it is pushed back inside on both axes; larger than the bounds, its left / top edges stay in. A soft keyboard covering the anchor pushes the panel up above the keyboard.
- **Pivot**: the aligned edge and the vertical middle. Content that grows while open grows up and down equally, and sideways away from the aligned edge.
- **`gap` is ignored** (`PUI-POPOVER-GAP` when every placement value is `over*`); the transition only fades.
- It follows its anchor and is lost with it exactly like a side placement. `OpenAt` centres it vertically on the point; with `start` its left edge sits on the point.
- **The anchor still counts as "inside"** for `dismiss="pass"`. A panel that covers its trigger never meets this — a press there lands on the panel. But anchored to something larger than the panel (`Open(wholeIconRow)` with a panel covering part of it), a press on a button the panel leaves uncovered neither closes the popover nor is eaten: that button is clicked and the popover stays. **Anchor it to the trigger**, not to the row.
- **The trigger is under the panel**, so "press it again to close" does not apply: a press outside, Escape / B, or `Close()` closes it.
- **It does not start editing by itself** — see *A field in the panel*.
- A field's `<Suggestions>` inside it is its child popup: Escape closes the list first, then the popover; a press on a suggestion row is not "outside".

## A field in the panel

- **Opening does not start editing.** To type at once, call `field.BeginEdit()` from `OnOpened` (scripting skill): it enters edit mode under the pointer and the gamepad alike, and opens the phone keyboard. (`screen.Focus("field")` only selects: under the pointer that also starts editing, but with gamepad navigation the two-level model waits for A / Enter.)
- **`closeOnEditEnd="true"`** closes the popover when the field's edit ends — a press on the panel's empty area, the phone keyboard hidden, a submit your handler neither closes on nor answers with `BeginEdit()`. It is the edit that counts, not the selection, and the popover waits before closing:
  - it closes **one frame after** the end is seen, so an edit that begins again in that frame keeps it — `BeginEdit()` from the submit handler (a failed search going back to the field), a press on another field in the panel;
  - a `BeginEdit()` not carried out yet counts as editing;
  - while the field's **suggestion list is still open** — a row was held down when the edit ended, as Android does when the keyboard hides under a tap — it waits for that press: released on the row, the suggestion is chosen; elsewhere, the list closes and so does the popover;
  - when the selection **went on to another control in the panel** (a `<Btn>`, a toggle) that end no longer counts; the next edit in the panel starts over;
  - an edit never started since it opened closes nothing.
- It closes as `EditEnded`; the selection the field held goes back to the trigger. Every other way of closing still applies.
- A submit that should keep the panel calls `field.BeginEdit()`; one that is done calls `pop.Close()` (or lets `closeOnEditEnd` do it).

## Following

- While open it follows its anchor every frame (`LateUpdate`): an animated anchor, a scrolling list, a layout change.
- **The anchor is lost** — the popover closes as `AnchorLost` — when it is destroyed, deactivated or made non-interactable; when it scrolls entirely out of its clip area (an anchor inside another popover is not clipped by a list behind it); or when the `<ScrollList>` row it sits in is retired, parked, or handed to another item (virtual scrolling, or a keyed push that gives the row to another key).
- A row that keeps showing the same item is re-bound by every push — that is **not** a loss; without a key a row's identity is its index.
- A popover opened with `OpenAt` has no anchor to lose; it re-clamps only when the Canvas size changes.

## Size

- The panel lays its children out like a `<VStack>`: size = `padding` + `spacing` + the children's preferred sizes.
- `width` / `height`: omitted or `hug` — the content; a number — fixed; `clamp(min, hug, max)` — the content, clamped. `stretch` / `N%` are errors (`PUI-POPOVER-SIZE`): the panel is in no layout and has no parent box to share.
- Content changes (a push, a wrapped line, a locale switch) are followed within the same canvas update; while open, a new size re-clamps on the locked side.
- **Children follow `<VStack>`'s rules**: with a hugging height a `height="stretch"` child collapses to 0 (`PUI-HUG-STRETCH-CHILD`); with a hugging width the panel is as wide as its sized children and `width="stretch"` children fill that width — if **every** child stretches, the panel is 0 wide (`PUI-POPOVER-HUG-WIDTH`). A menu usually fixes the panel's width and lets the rows stretch.
- **Too tall**: put a `<ScrollList height="clamp(_, hug, N)">` in the panel.

## Focus

- **On open**, with directional navigation on (`UI.UseGamepadNavigation()`) and something focusable in the panel: the focus is caged in the panel, on its first focusable — `BindItems` cells included, so the d-pad never walks out of an emoji grid. A panel with nothing focusable (an info popover) neither cages nor moves the focus.
- **On close** it only gives back what is still its own: the cage, if the panel still holds it; the selection, if it is still inside the panel — back to the anchor, or (anchor not selectable, `OpenAt`, `AnchorLost`, `HostGone`) to what was selected before it opened, else cleared. This happens whether or not it caged: a mouse click on a `<Btn>` in the panel makes it the selection.
- **Emoji next to a field**: pressing the trigger or a cell (Selectables) ends the field's edit — on a phone the keyboard goes away, as chat apps do. Insert with `InputField.Insert`, which uses the caret the edit left (scripting skill).

## Nesting

- B is A's child when B's host **or B's anchor** is inside A's panel — a popover opened from a button in another popover is its child wherever it is declared. Escape and a press inside the parent close the child first.
- Opening a popup that is no open popup's descendant closes the whole chain first (`Replaced`).
- Each level sorts two orders above the one below it. On a routed page the page step (`PageSortingOrderStep`, 10 by default) bounds how deep a chain can go without passing the next page (2 + 2k < step: depth 3 at the default); deeper, the Console warns once and it still opens.

## `expand` / `collapse` and `<Show>`

- A `<Popover>` is an `expand` / `collapse` source like `<TabMenu>` / `<Collapsible>`: a bare `on="expand"` inside the panel finds it (row entrance animations); elsewhere point at it with `expand@<id>` / `collapse@<id>`. `expand` fires as it starts opening — the content is active, measured and placed.
- **`<Show on="expand@pop">`** is shown while it is open, `collapse@pop` while closed — a persistent state like `checked`, right from the first frame. The same works for `<TabMenu>` and `<Collapsible>` (e.g. a bare `<Show on="expand">` in a `<Collapsible>`'s `<Header>` swaps an arrow).
- A **bare** `<Show on="expand|collapse">` inside its own source's panel is a mistake (`PUI-SHOW-EXPAND-SELF`): the panel is only seen while open, so the block is always on (expand) or never seen (collapse) — and an expand block appearing after the panel was measured leaves it the wrong size. Put it on the trigger.

## Recipes

**Emoji picker** — above, `placement="top-end"`, `pass` (the press on the field or the page goes through), cells call `InputField.Insert`, `<Show on="expand@…">` lights the button.

**Row "…" menu** — one `manual` popover for the whole list, `Toggle(more)` from each row's button, remember the row's item in C#, `Close()` from the menu rows. `dismiss="block"` when a stray press must not reach the page behind.

**Right-click menu** — `OpenAt(eventData.position)` from your pointer handler (a press handler on the area, or a right-button filter); `bottom-start` puts the menu's corner at the pointer.

**Search that unfolds from its icon** — a search button at the right end of an icon row; the popover covers the row with a field whose submit icon lands where the button was:

```xml
<HStack height="44" spacing="4" childAlign="middle-right">
  <Btn id="homeBtn" size="44x44"><Icon name="ui:home" anchor="center" size="24x24"/></Btn>
  <Btn id="searchBtn" size="44x44"><Icon name="ui:search" anchor="center" size="24x24"/></Btn>
</HStack>

<Popover id="searchPop" on="click@searchBtn" placement="over-end" width="320" radius="22" closeOnEditEnd="true">
  <InputField id="searchField" width="stretch" height="44" placeholder="Search…" submitIcon="ui:search">
    <Suggestions id="searchHits" itemTemplate="HitRow"/>
  </InputField>
</Popover>
```

```csharp
var pop = screen.Get<Popover>("searchPop");
var field = screen.Get<InputField>("searchField");
pop.OnOpened.Subscribe(_ => field.BeginEdit()).AddTo(screen);   // type at once
field.Suggestions.OnSubmit.Subscribe(s =>
{
    if (Search(s)) pop.Close();
    else field.BeginEdit();      // nothing found: back to the field, the popover stays
}).AddTo(screen);
```

Give the panel a width (its only child stretches — `PUI-POPOVER-HUG-WIDTH` otherwise) and make it about as tall as the row: it is centred on the button.

## Lint

| Code | When | Runtime |
|---|---|---|
| `PUI-POPOVER-ON` | `on=` other than `click@<id>` / `manual` | throws |
| `PUI-POPOVER-ENUM` | `placement` / `dismiss` (base or any variant) not in the table above | throws |
| `PUI-POPOVER-GAP` | `gap` written while `placement` (base and every variant) is `over*` | CLI only |
| `PUI-POPOVER-VALUE` | `gap` not a number ≥ 0; `transition` not a duration | warns, uses the default |
| `PUI-POPOVER-SIZE` | `width` / `height` / `size` that stretches or takes a percentage (also inside `clamp`) | throws |
| `PUI-POPOVER-LAYOUT-ATTR` | `anchor` / `margin` / `pivot` / `flow` / `scale` / `hidden` | ignored, warns |
| `PUI-POPOVER-PARENT` | directly in `<ScrollList>` / `<TabBar>` / `<TabMenu>` / `<Carousel>` / `<Pages>`, or inside a `<Btn>` / `<Tab>` / `<Toggle>` | CLI only |
| `PUI-POPOVER-HUG-WIDTH` | hugging width, every child `width="stretch"` | CLI only |
| `PUI-SHOW-EXPAND-SELF` | a bare `<Show on="expand|collapse">` in its nearest source's panel | CLI only |
| `PUI-HUG-STRETCH-CHILD` | a `height="stretch"` child under a hugging (or unset) height | CLI + runtime warning |
