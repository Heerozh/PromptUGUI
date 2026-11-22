# `drag=` / `drop=` — drag and drop

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md) (**Common attributes**). The `on=` values are also listed in [`animations.md`](animations.md), the `<Show>` values in [`states.md`](states.md). C# side — `UI.Drag`, `DragData`, `CanDrop`, `OnDrop`, the data contract, waiting for a server: scripting-promptugui-csharp → **Drag and drop**.

Any control can be picked up (`drag="kind"`) and put onto another (`drop="kinds"`): across containers, across Screens, with a mouse and with a finger from the same markup. The library draws everything the user sees — the lifted thing following the pointer, the targets lighting up, the landing — and C# hears what went where.

```xml
<!-- A 3×3 formation: each cell is a target, the unit card inside it is a source -->
<ScrollList id="formation" columns="3" cellSize="160x160" spacing="12"
            itemTemplate="FormationSlot" size="504x504"/>

<Template name="FormationSlot">
  <Frame id="slot" radius="12" color="@slot" drop="unit">
    <Show on="drop-armed"><Frame anchor="stretch" radius="12" borderWidth="2" borderColor="@accent"/></Show>
    <Show on="drop-over"><Frame anchor="stretch" radius="12" color="@accent/0.25"/></Show>
    <Show on="drop-deny"><Frame anchor="stretch" radius="12" color="@danger/0.25"/></Show>
    <Show on="lift@unit"><Frame anchor="stretch" margin="8" radius="8" borderWidth="1" borderColor="@muted"/></Show>
    <Frame id="unit" anchor="stretch" margin="8" radius="8" color="@card" drag="unit">
      <Image id="portrait" anchor="stretch" margin="4"/>
      <Text id="name" anchor="bottom-stretch" height="24"/>
    </Frame>
  </Frame>
</Template>
```

## Attributes (any element)

| Attribute | Values | Default | Notes |
|---|---|---|---|
| `drag` | a kind (`[A-Za-z_][A-Za-z0-9_-]*`) \| `none` | — | This control can be picked up. The kind decides which targets take it. `none` turns it off (for a variant: `drag="none" drag.portrait="unit"`). `true` / `false` are errors (`PUI-DRAG-VALUE`): it names what is carried, it is not a switch. |
| `drop` | kinds separated by spaces or commas \| `*` \| `none` | — | This control takes drags of these kinds. `*` = any kind. |
| `dragHold` | `auto` \| a duration (`0.4s` / `400ms` / `0.4`) | `auto` | How long the pointer must stay down before the lift. `auto` = **mouse 0**; a **finger 0.4s only inside a scroller** (`<ScrollList>` / `<Carousel>`), 0 elsewhere; 0 on both with a `dragHandle`. A duration applies to both. |
| `dragHandle` | id of a node inside the source | — | Only a press on that node lifts; the rest of the source behaves as before. Geometric hit (an `<Icon>` works). Missing id: `PUI-DRAG-HANDLE-ID`; at runtime one warning and the whole source lifts. |
| `dragDuration` | a duration | `0.18s` | The landing / going-home flight, `OutCubic`. `0` = instant. |

- All are variant-switchable. A bad value warns at runtime and keeps the previous one.
- A **template invocation carries them** onto the instance root: `<EquipSlot id="weapon" drop="item"/>`.
- A control may be both a source and a target (drag a card onto another card to merge them).

## The gesture

- **Mouse: lifts on the first drag frame.** A click is still a click.
- **Touch, outside any scroller: lifts on the first drag frame too** — nothing to tell apart.
- **Touch, inside a scroller:** move along the scroll axis before 0.4s → the list scrolls as it always did; keep still for 0.4s → it lifts. In a **single-axis** scroller a finger whose first movement is **across** the axis (more than 60° off it) lifts at once — drag an item up out of a horizontal bag without waiting.
- **Clicks stay clicks.** A source that is also a `<Btn>` (tap for details, hold to move) is let go the moment it lifts — no Pressed tint while it flies, no click on release.
- **A `drag=` node catches its press** even if it is a click-through `<Frame>` or `<Image>`; clicks still reach the `<Btn>` around it. An explicit `raycastTarget="false"` on the source wins (it can never be picked up — reported once).
- **One drag at a time.** A second finger's events are ignored; pressing another source meanwhile does nothing special.
  This is about drag and drop sessions: a `<ScrollList reorder>` row can be reordered with one finger while another
  finger drags a `drag=` source — the two do not exclude each other.
- Not lifted by: a right / middle button, `interactable="false"` (on it or an ancestor), `drag="none"`.

## What moves

**The source itself** is lifted — not a copy. It stays in its parent (ids, Screen, theme, bindings all intact) and:

- leaves its layout group's flow behind a **same-size placeholder**, so nothing around it shifts (a free-positioned parent needs none);
- is raised above **every Screen** — pages, modals (1000), toasts (2000), the tutorial overlay (3000) — at `UI.Drag.SortingOrder` (4000), and is **not clipped** by a list's viewport;
- follows the pointer 1:1.

Its own slot is empty meanwhile; `<Show on="lift@unit">` in the slot draws an outline there. "The original stays dimmed and a second picture follows the finger" is not in v1.

## Targets

When something is lifted, every target that takes its kind is judged once:

- **armed** — it will take it: `drop-armed` fires, and it becomes hit-testable for the length of the drag (a click-through `<Frame>` target needs no `raycastTarget`). Outside a drag nothing about its clicks changes.
- **denied** — the kind matches but C# refused (`CanDrop`) or it is `interactable="false"`: it shows `drop-deny` while hovered, and a release there sends the source back.
- The source's **own container** (any target it sits inside) never arms — dropping it back where it was is not a drop.
- A target for **another kind** is see-through: hovering it finds the target around it.

**Hover is a raycast**, the same answer a click gets: a target under a modal backdrop, a blocker, the tutorial mask or another Screen cannot be dropped on; a target scrolled out of a list's viewport cannot either. While a drag hovers near the edge of a `<ScrollList>` that holds armed targets, the list scrolls.

## Hooks

### Source side: `lift` / `drop`

The same two values `<ScrollList reorder>` rows use, resolved **upward to the nearest drag source or list row** (the nearer wins):

| Value | Fires |
|---|---|
| `lift` | when it is picked up. `<Show on="lift">` = while lifted |
| `drop` | when it is put down — any outcome (landed, went back, cancelled) — as the flight **starts**, so `reverse-on="drop"` runs alongside it |
| `lift@<id>` · `drop@<id>` | for the source containing `<id>` (lexical scope, a path allowed) |

The trigger must be the source itself or inside it: `<Animation on="lift" reverse-on="drop" drag="unit">…</Animation>` (the Animation *is* the source), or `<Frame drag="unit"><Animation on="lift" …>`. A source inside a list row: a bare `lift` on the row root resolves to the **row**; use `lift@<source id>`. A bare `lift` / `drop` with neither around it: `PUI-LIFT-NO-SOURCE`.

### Target side

Resolved **upward to the nearest `drop=` control** (`@<id>` to name one):

| Value | Kind | When |
|---|---|---|
| `drop-armed` | state, paired with `drop-disarmed` | a drag this target will take is in progress |
| `drop-disarmed` | state | no such drag (also how it starts) |
| `drop-over` | state, paired with `drop-out` | that drag hovers here — releasing now drops here |
| `drop-deny` | state, ended by `drop-out` | a drag of a matching kind hovers here but is refused |
| `drop-out` | state | nothing hovers here (also how it starts). Ends `drop-over`, `drop-deny` and `drop-pending` |
| `drop-pending` | state, ended by `drop-out` | a drop here is **waiting for confirmation** (C# deferred it): the dragged thing waits over this target |
| `receive` | edge | something landed here (accepted) — as the landing starts |

- `<Show>` takes all but `receive`; blocks are independent (an armed, hovered slot shows both its `drop-armed` and `drop-over` blocks — layer them yourself). `<Show on="receive">` is an error: a moment, not a state.
- How it starts is established like `ready` / `cooling`: an `<Animation on="drop-out">` lands on its end state at open, a plain `<Trigger>` is not fired for it.
- `<Animation on="drop-over" reverse-on="drop-out" scale="1:1.06">` grows on hover and shrinks back however the hover ends (moved off, dropped, cancelled).
- A bare target value with no `drop=` around it: `PUI-DROP-NO-SOURCE`.

```xml
<Template name="EquipSlot">
  <Frame size="96x96" radius="10" color="@slot">
    <Show on="drop-armed"><Frame anchor="stretch" radius="10" borderWidth="2" borderColor="@accent"/></Show>
    <Animation on="drop-over" reverse-on="drop-out" scale="1:1.08" duration="0.1s">
      <Frame anchor="stretch" radius="10" color="@accent/0.2"/>
    </Animation>
    <Show on="drop-pending"><Icon name="ui:spinner" anchor="center" size="24x24"/></Show>
    <Frame id="equipped" anchor="stretch" drag="item">   <!-- what is equipped can be dragged out again -->
      <Image id="icon" anchor="stretch" margin="8"/>
    </Frame>
  </Frame>
</Template>

<HStack spacing="16">
  <EquipSlot id="weapon" drop="item"/>
  <EquipSlot id="armor" drop="item"/>
</HStack>
```

## Default look

- **Source:** with no `on="lift"` hook, a lift scales it to ×1.03 (0.12s) and the drop eases it back — the same default as a reorder row. Write any `on="lift"` hook (`<Animation>` / `<Trigger>` / `<Show>`) and the look is entirely yours. Relative to its own scale.
- **Target:** none. Hover feedback is always authored — every target should have at least a `<Show on="drop-over">`.

## Release

1. The structure goes back first (placeholder out, source back in the flow) — the picture stays where it was let go.
2. C# hears of it (`OnDrop`), synchronously, and updates the data.
3. The landing, over the already-updated screen:
   - **dropped:** the one active drag source of the same kind inside the target (a slot's card, which the push has just filled) flies in from where the source was let go, growing or shrinking from its size; the target fires `receive`. Zero or several such sources: nothing flies — animate `receive` instead. C# can name what lands or add flights (a swap's displaced card) — see the C# skill.
   - **released over nothing / a refusing target / rejected by C#:** the source flies home.
4. `drop` fires on the source, every target goes back to `drop-out` / `drop-disarmed`.

### Waiting for confirmation

When a server has the final word, C# can **defer** the drop: the source flies over the target and waits there (the target shows `drop-pending`), then lands when C# accepts or flies home when it rejects. Nothing new can be lifted while a drop waits (a safety timeout gives up after `UI.Drag.PendingTimeout`, 10s by default). The target it waits over stays armed and `drop-over` (only the others let go); the source's Screen starting to close ends the wait (`OnEnd(Cancelled)`), while a push or a ReSolve only puts the source back and keeps waiting. The XML side is just `drop-pending`; the API is in the C# skill.

## Across Screens

A source in one Screen and a target in another (a bag page and an equipment modal) needs nothing special: the lifted thing draws above every Screen, the hover raycast sees every Screen, and the event says both Screens. Sizes: the lifted thing keeps its own Screen's scale while dragged and the landing grows / shrinks it into the target's. **Limitation:** a source on a `canvas="camera"` Screen dragged over an Overlay Screen draws under it (Overlay always draws above camera canvases) — keep both Overlay, or both on the same camera.

## Cancellation

These end a drag at once: the source goes back without a flight, `drop` fires, C# gets no drop (`OnEnd` says `Cancelled`), and the rest of that gesture goes nowhere:

- a push / clear / `itemTemplate` change of the list the source lives in;
- **any** ReSolve — a Variant, theme, orientation or locale change;
- the source destroyed, hidden, disabled, or its `drag` turned off;
- the source's Screen closing; a modal or routed page opening above it;
- **Escape** / the gamepad's East (the press is spent on it);
- a touch the **system cancels** — a call, the notification shade, a system gesture, too many fingers, the app losing
  focus. It is never taken for a release: nothing is put into the target under the finger;
- `UI.Drag.Cancel()`.

The same signals hold while a source is only **pressed** — counting down its hold, or (mouse) waiting for its first move:
the press ends, nothing is lifted, no `OnLift` / `OnEnd`, and the rest of that gesture goes nowhere. A row re-bound under a
held finger (a push to its list) is therefore never lifted with its new item.

A target's Screen closing only takes its targets away; the drag goes on.

## Sound cues

`drag.lift` when it is picked up; `drag.land` when it goes into a target — the key looked up on the **target** first
(`<Frame drop="item" sfx="land:equip">`), then on the dragged element; `drag.return` when it goes home (let go over
nothing or a refusing target, a `Reject`, Escape). A deferred drop raises nothing when let go and speaks at
`Accept` / `Reject`; `UI.Drag.Cancel()`, a push, a ReSolve, a close, a layer above, the pending timeout and a late answer
raise nothing. Not the same moments as the `drop` hook, which fires for any ending. → [`sfx.md`](sfx.md)

## Lint

| Code | When |
|---|---|
| `PUI-DRAG-VALUE` | `drag` is not a kind (incl. `true` / `false`), `drop` lists a non-kind, `dragHold` is not `auto` / a duration, `dragDuration` is not a duration (base and variants, through `class=` too) |
| `PUI-DRAG-HANDLE-ID` | `dragHandle="x"` but the source's subtree has no `id="x"` |
| `PUI-DRAG-REORDER` | `drag=` on or inside a row of a `<ScrollList reorder="true">` (any variant) — two gestures would fight over one drag |
| `PUI-DRAG-VIRTUAL` | `drag=` inside a row of a `<ScrollList virtualize="true">` — its rows are recycled while scrolling |
| `PUI-DRAG-CAROUSEL-CARD` | `drag=` on a `<Carousel>` card root (it repositions its cards) — put it on a node inside the card |
| `PUI-DROP-NO-SOURCE` | a bare `drop-armed` / `drop-disarmed` / `drop-over` / `drop-deny` / `drop-out` / `drop-pending` / `receive` with no `drop=` around it (template bodies and `@id` forms exempt) |
| `PUI-LIFT-NO-SOURCE` | a bare `lift` / `drop` with neither a `drag=` source nor a `<ScrollList>` row around it |

## Not in v1

Gamepad / keyboard drag and drop; a copy that follows the finger while the original stays (`dragGhost`); dragging a row out of a sortable list; dragging out of a virtual list; switching tabs / pages by hovering over them; several drags at once.
