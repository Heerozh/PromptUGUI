# `<ScrollList reorder>` — drag-to-reorder

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md) (the `<ScrollList>` section). The `lift` / `drop` event vocabulary is also listed in [`animations.md`](animations.md). C# side (`OnReordered`, the data contract): scripting-promptugui-csharp → **List / option push → Reorder**.

Press a row, drag it, the other rows make room as you go, release and the row settles into the gap. Works on a single column, a single row (`direction="horizontal"`), and a grid (`columns=`); on static children and on `BindItems` rows alike; with a mouse and with a finger from the same markup.

```xml
<ScrollList id="tasks" itemTemplate="TaskRow" width="stretch" height="stretch"
            reorder="true" reorderHandle="grip" reorderDuration="0.15s">
  <Scrollbar thickness="6" overlay="true"/>
</ScrollList>

<Template name="TaskRow">
  <Animation on="lift" reverse-on="drop" scale="1:1.03" duration="0.12s">
    <Frame id="row" width="stretch" height="48" radius="8" color="@panel">
      <Icon id="grip" name="ui:grip" anchor="left" margin="0,0,0,12"/>
      <Text id="title" anchor="stretch" margin="0,12,0,40">{{title}}</Text>
      <Show on="lift">
        <Frame anchor="stretch" radius="8" glow="12" glowColor="@accent"/>
      </Show>
    </Frame>
  </Animation>
</Template>
```

## Attributes

| Attribute | Type / values | Default | Notes |
|---|---|---|---|
| `reorder` | bool | `false` | Turns the gesture on. Variant-switchable (`reorder.portrait="true"`); to turn it off in a variant spell `reorder="false"` — an absent variant value is skipped, not reverted. |
| `reorderHold` | `auto` \| duration | `auto` | How long the pointer must stay down before the row lifts. `auto` = **mouse `0`, touch `0.4s`**, and `0` on both when `reorderHandle` is set. A duration (`0.4s` / `400ms` / `0.4`) applies to both pointer kinds. A bad value warns and keeps the previous one (`PUI-REORDER-VALUE` in the CLI). |
| `reorderHandle` | id inside the row | — | Only a press on that node can lift the row; the rest of the row scrolls as it always did. Looked up in the row's template scope first, then by a recursive walk. The hit is **geometric** (`RectangleContainsScreenPoint`) — an `<Icon>`, `<Image>`, `<Frame>` or `<Text>` all work, no `raycastTarget` needed. Missing id: `PUI-REORDER-HANDLE-ID` in the CLI; at runtime one warning and the whole row lifts. |
| `reorderDuration` | duration | `0.15s` | The squeeze (rows making room) and the settle (the dropped row sliding in), `OutCubic`. `0` = instant. |

## The gesture, and why the defaults are what they are

- **Mouse: lifts on the first drag frame.** Nobody scrolls a list by dragging with a mouse — the wheel and the scrollbar do that — so there is nothing to disambiguate. A plain click is still a click (the row is never lifted on press alone).
- **Touch: hold 0.4s, then lift.** On a touch screen a finger drag IS the scroll. Move before the hold elapses → the list scrolls exactly as before; keep still until it elapses → the row lifts (your `lift` hooks fire — that is the feedback), and the drag that follows moves the row. Lift-then-release without moving drops the row where it was and does **not** click the `<Btn>` under the finger.
- **A handle makes the hold unnecessary** on both: a drag that starts on the grip cannot be a scroll, so `auto` becomes `0`.
- **Everything that is not a reorder is untouched.** Pressing between rows, on padding, on the scrollbar, on a row that is `hidden` or `interactable="false"`, or with a non-left button scrolls as today. The wheel scrolls. A row containing its own drag control (`<Slider>`, `<Carousel>`, a nested `<ScrollList>`) keeps its gesture.
- **Live gap.** While dragging, a same-size placeholder holds the row's slot and moves the moment the dragged row's centre crosses a neighbour's centre; the neighbours slide (`reorderDuration`). The list's content size never changes, so a `height="hug"` list does not jump. Near the top / bottom (left / right) edge of the viewport the list autoscrolls.
- **The lifted row is not cut at the sides.** A list clips along its scroll axis only (see the `mask` row in the main doc), so the 1.03 scale, a border or a glow reaching past the list's left / right edge (top / bottom for a horizontal list) stays visible. It is still clipped along the scroll axis — a row dragged to the viewport's end disappears under the edge like any scrolled-out row — and within the rounded corners of a 9-slice mask. A border-less custom `mask=` (a hexagon, say) keeps clipping on all sides.
- **Single column / row locks the cross axis** — the row stays in its column. A grid moves freely in both axes and targets the cell under the dragged row's centre.
- **Hidden rows** (`hidden="true"` / `Hidden = true`) are never crossed and keep their place; the reported indices still count every slot.

## Release, and what the hooks see

On release the **structure commits immediately**: the placeholder goes, the row is inserted at its new sibling index, the list's slot order is permuted the same way, and only then does the row slide from under the finger to its final place. Because the structure is already final, `OnReordered` fires in that same moment — a host that pushes the permuted data synchronously rebinds with zero visual change (see the C# skill). Dropping a row back where it came from fires `drop` but not `OnReordered`.

`lift` / `drop` are `on=` values on `<Trigger>` / `<Animation>` / `<Show>` that resolve **upward** to the row the trigger lives in (like `expand` / `collapse` resolve to a `<Collapsible>`):

| Value | Fires |
|---|---|
| `lift` | when this row is picked up |
| `drop` | when this row is released — as the settle tween **starts**, so `reverse-on="drop"` plays alongside it. Also on a cancelled session (below). |
| `lift@<id>` · `drop@<id>` | for the row containing `<id>` (lexical scope; an enclosing element's own id counts, so `lift@row` written on the row root works) |

- Hooks are legal in **every** `<ScrollList>` row. A list without `reorder` simply never fires them, so one row template serves sortable and plain lists, and `reorder.portrait` needs no rebinding.
- **Default lifted look**: a row with **no** `on="lift"` hook is brought to the front and scaled to 1.03 (0.12s), undone on drop. Write any `on="lift"` hook (`<Animation>`, `<Trigger>`, `<Show>`) and the look is entirely yours — nothing is layered on top, so your `scale=` never fights a built-in one. A `drop`-only hook keeps the default look. Bring-to-front always happens.
- `<Show on="lift">` shows its subtree from lift to drop. `<Show on="drop">` is an error — drop is a moment, not a state.
- A bare `lift` / `drop` outside any `<ScrollList>` is a runtime error and `PUI-LIFT-NO-SOURCE` in the CLI (template bodies and `@id` forms exempt).

## Cancellation

A structural change during a drag ends the session and puts the row **back where it started**, fires `drop`, and fires **no** `OnReordered`: a `BindItems` push arriving, a ReSolve reaching the list (Variant / theme / orientation change), `itemTemplate` changing, the list closing, `reorder` turning off. The rest of that pointer gesture goes nowhere (it is not handed to the scroll half-way). A push that arrives *after* release, while the settle tween still runs, is unaffected — the structure was already committed.

## Lint

| Code | When |
|---|---|
| `PUI-REORDER-HANDLE-ID` | `reorderHandle="x"` but the `itemTemplate`'s body has no node with `id="x"` (or, without an `itemTemplate`, a static row lacks it). A template this document does not declare is not judged. Checked even without `reorder` — a variant may turn it on. |
| `PUI-REORDER-VALUE` | `reorderHold` is neither `auto` nor a duration; `reorderDuration` is not a duration (base and variants, through `class=` too). |
| `PUI-LIFT-NO-SOURCE` | a bare `on="lift"` / `on="drop"` with no `<ScrollList>` ancestor. |

## Not in v1

Dragging a row out of its list or between lists; a programmatic animated `Move(from, to)`; keyed `BindItems` diffs that animate server-side reorders; a visible placeholder (the gap is empty).
