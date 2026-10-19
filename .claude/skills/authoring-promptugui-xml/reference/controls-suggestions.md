# `<Suggestions>` — a suggestion list under an `<InputField>`

A search box, a command palette, an address field: a list of suggestions that floats under (or above) the field while the user types. The field **keeps editing** the whole time — ↑ / ↓ move a highlight, Enter or a click chooses — and the list is filled from C# (`BindItems`), usually in answer to `InputField.OnInput`.

```xml
<Template name="CropRow">
  <Frame height="30">
    <Show on="state-hover"><Image anchor="stretch" color="row-hover" raycastTarget="false"/></Show>
    <Show on="state-selected"><Image anchor="stretch" color="row-pick" raycastTarget="false"/></Show>
    <HStack anchor="stretch" spacing="8" padding="0,8,0,8" childAlign="middle-left">
      <Image id="icon" size="20x20" raycastTarget="false"/>
      <Text id="name" fontSize="16"/>
      <Frame width="0"/>
    </HStack>
  </Frame>
</Template>

<InputField id="search" placeholder="Search crops" width="240" height="30"
            submitIcon="UI/icons#search" submitIconSize="18">
  <Suggestions id="hits" itemTemplate="CropRow" maxHeight="200" padding="6">
    <Scrollbar thickness="8" overlay="true"/>
  </Suggestions>
</InputField>
```

```csharp
var search = screen.Get<InputField>("search");
search.Suggestions.BindItems(search.OnInput.Select(Filter),
    (IControl row, Crop c) => { row.Get<Image>("icon").Sprite = c.Sprite; row.Get<Text>("name").TextValue = c.Name; },
    key: c => c.Id).AddTo(screen);
search.Suggestions.OnSubmit.Subscribe(s => Debug.Log(s.IsChosen ? $"chose #{s.ChosenIndex}" : $"typed '{s.Text}'"));
```

## Where it goes

- **Only as a direct child of `<InputField>`**, at most one per field. Anywhere else it stays closed (`PUI-SUGGEST-OUTSIDE`); a second one is parked inactive (`PUI-SUGGEST-DUPLICATE`).
- Its own children: `<Decor>` and `<Scrollbar>` only (`PUI-SUGGEST-CHILD`). Rows come from `itemTemplate`.
- The node **is the panel**: it hangs under the field's GameObject (that is what keeps the field focused — below), and a nested Canvas lifts it above the page, out of any ancestor mask. It sorts like every popup (`<TabMenu>`'s panel): above its page, below the modal band.

## Attributes

| Attribute | Value | Default | Meaning |
|---|---|---|---|
| `itemTemplate` | Template name / registered tag | — (**required**) | The row, resolved like `<ScrollList itemTemplate>`. `BindItems` throws without it (`PUI-SUGGEST-TEMPLATE`) |
| `maxHeight` | px ≥ 0 | `320` | Height cap; taller content scrolls inside the panel. `0` = only the room available limits it |
| `gap` | px ≥ 0 | `4` | Space between the field and the panel |
| `placement` | `below` \| `above`, optionally `-start` \| `-end` | `below` (= `below-start`) | The preferred side — it flips on open when that side lacks room and the other has more — and, for a panel wider than the field, which edges line up: `start` the left ones, `end` the right ones (it grows left). Anything else: `PUI-SUGGEST-ENUM`; the runtime throws |
| `popupWidth` | px ≥ 0 | `0` | The panel's width; `0` = the field's. Narrower than the field is allowed |
| `padding` | as `<TabMenu padding>` | `0` | Inside the panel. Use it to keep rows off a rounded panel's corners — the viewport clips to a rectangle |
| `spacing` | px | `0` | Between rows |
| `transition` | `0.1s` / `100ms` / seconds | `0.1s` | Open / close fade + slide; `0` snaps |
| `color` · `sprite` · `tint` | as the `<TabMenu>` panel | default skin | The panel's face |
| procedural attributes (`radius` / `border*` / `glow*` / `glass` …) + `<Decor>` | as `<Frame>` | — | All on the panel — it is the primary surface |

Negative or non-numeric `maxHeight` / `gap` / `popupWidth` warn and fall back to the default (`PUI-SUGGEST-VALUE`).

**Layout attributes are ignored** (`PUI-SUGGEST-LAYOUT-ATTR`): `anchor` / `size` / `width` / `height` / `margin` / `pivot` / `flow` / `scale` / `hidden`. The field places the panel — **as wide as the field** unless `popupWidth` says otherwise, as tall as `min(rows, maxHeight, room)` — and shows it only while open; `hidden="false"` cannot open it.

## Rows and their states

Every item gets a **row shell** first, and the template is instantiated under it. The shell is the rows' state source — a press that is **not** a `Selectable` — so the template's `<Show on="state-*">` blocks work exactly as inside a `<Btn>` / `<Toggle>`:

| State | When |
|---|---|
| `state-normal` | nothing else |
| `state-hover` | the pointer is over the row |
| `state-pressed` | the row is held down |
| `state-selected` | **the keyboard / gamepad highlight** (folded like a Toggle's `isOn`: hover / pressed override it while they last) |
| `state-disabled` | a row that cannot be chosen (below) |

The shell is a layout group, so the template root is a layout-group child (`height` applies, `anchor` / `margin` do not) — same as a `<ScrollList>` row. A row takes the pointer over its whole rect, gaps between the template's graphics included. There are no `hoverColor` / `pressOffset` style attributes on a row (it is not a `<Btn>`); give it looks with `<Show>`.

**No Selectables inside a row** (`<Btn>` / `<Toggle>` / `<InputField>` …, `PUI-SUGGEST-ROW-SELECTABLE`; the list also warns once at runtime): pressing one moves the selection off the field and ends the edit. Why the rest works:

1. the panel is under the field, so a press anywhere in it finds the field as its select handler — the selection stays put;
2. rows and the submit icon are not Selectables, and the scrollbar is **display-only** (uGUI's `Scrollbar` is a Selectable, so its track and handle take no raycasts — scroll by dragging the rows or the wheel);
3. every press inside the panel is taken (rows by their shell, blank space by the panel), so none reaches the field's text handling to move the caret.

## Rows that cannot be chosen

"No match", "Loading…", a group title: push it as an item, and in the bind set the row's `Interactable` to `false` (C#: `row.Interactable = !item.IsMessage`).

- The row **shows** — a push with only such rows still opens the list — in **`state-disabled`**: give the template a `<Show on="state-disabled">` look (dimmed text, no hover band). It has no hover / pressed reaction.
- ↑ / ↓ (d-pad) **skip** it; with nothing choosable they do nothing and the highlight stays on none, so Enter submits the typed text. `HighlightedIndex` set to it is ignored.
- A press on it **does nothing**: no submit, the list stays open, the field keeps editing. An edit that ends while it is held just closes the list.
- Rows are reused across pushes, and every row starts choosable again before its bind — the bind only has to set `false` for the items that are not. The flag is read at bind time; changing `Interactable` later takes effect at the next push.
- `Count` counts it, and `OnSubmit`'s index stays the item's index in the push.

## When it shows

**Open ⟺ the field is editing ∧ the list is armed ∧ the last push was non-empty ∧ the field is interactable, not `readOnly`, active.**

- **Armed** by the user changing the text (`OnInput`), or by `Suggestions.Open()` from code (e.g. "show recent searches on focus").
- **Disarmed (and closed)** by Escape / gamepad B, any submit, the end of the edit, a layer covering it (a dialog, a routed page / Modal, the Loading overlay — this also ends the edit), `Close()`.
- **Closed but still armed** by an empty push — the next non-empty push re-opens it. So never push an empty list as "loading…": the list would blink closed and open again.
- Entering edit mode does **not** arm it: focusing the field does not bring back last round's rows.
- A push while open rebuilds the rows in place and resets the highlight to none; the side chosen on open is kept, so the panel never jumps across the field while the user types.

## Keys, gamepad, pointer

While the list is open (and no input method is composing — below):

| Key | Does | TMP sees it |
|---|---|---|
| ↓ | highlight next: none → 0 → … → n−1 → none | no |
| ↑ | highlight previous: none → n−1 → … → 0 → none | no |
| Enter / keypad Enter, with a highlight | choose it | no |
| Enter, no highlight | TMP's own submit of the typed text, ending the edit | yes |
| Escape | the Escape chain closes the list; the field keeps editing and the text is **not** rolled back | no |
| anything else (characters, ← →, Home / End, Backspace, Ctrl+A / C / V / X, Shift+↑↓ …) | — | yes |

With the list closed, every key is TMP's as before. A multi-line field works the same (↑ / ↓ belong to the list while it is open — `PUI-SUGGEST-MULTILINE` warns). The highlight scrolls its row into view; holding ↓ steps one row at a time.

Gamepad (editing, list open): d-pad / stick up-down = ↑ / ↓; South with a highlight chooses it, without one is TMP's submit; East closes the list (the edit is not cancelled).

| Pointer | Does |
|---|---|
| enter / leave a row | `state-hover` on / off; the keyboard highlight does not move |
| press / release on a row | `state-pressed`; releasing on the same row chooses it |
| press on blank panel space | swallowed |
| drag / wheel in the panel | scrolls the panel |
| anywhere outside the field | the field loses the selection → the edit ends → the list closes |

If the edit ends while a row is held (Android may hide the keyboard on a tap), the list waits for that press: released on the row, it chooses it; released elsewhere, it closes.

## Submitting

| Entry | Highlight | `InputField.OnSubmit` | `Suggestions.OnSubmit` | Then |
|---|---|---|---|---|
| Enter / gamepad South | none | `text` | `(text, −1)` | list closes, TMP ends the edit |
| Enter / gamepad South | row i | — | `(text, i)` | list closes, edit ends |
| click row i | — | — | `(text, i)` | list closes, edit ends |
| `submitIcon` | none | `text` | `(text, −1)` | list closes; edit ends if editing |
| `submitIcon` | row i | — | `(text, i)` | same |
| soft keyboard Done / Go | — | `text` | `(text, −1)` | TMP ends the edit |
| `Submit` that enters edit (two-level navigation) | — | — | — | not a submit |

When both fire, `InputField.OnSubmit` comes first. `InputField.OnSubmit` means "the typed text was submitted"; with a list, `Suggestions.OnSubmit` alone covers every submit. Choosing a suggestion **does not rewrite the field** — write `TextValue` in your handler if you want that (a code write does not raise `OnInput`, so it does not query again).

## Placement

- **Width**: the field's, or `popupWidth`.
- **Horizontally**: `placement="…-start"` (default) lines up the left edges, `…-end` the right ones — a field near the right of the screen wants `below-end`, or the wider panel is pushed back at the screen edge and overhangs the field on the right. Either way it is pulled back inside the bounds, the left edge kept in.
- **Height**: `min(rows, maxHeight, room)`; with `maxHeight="0"`, `min(rows, room)`. More scrolls (the default bar auto-hides, as on `<Dropdown>`).
- **Side**: `placement` is a preference; decided on open, kept while open.
- **Bounds**: the root canvas shrunk into the device safe area; while the on-screen keyboard is visible its height comes off the bottom (height only — `TouchScreenKeyboard.area`'s origin differs per platform).
- Re-placed on open, on every push and on every ReSolve (Variant / theme).

## Input methods and mobile

- While an IME is composing (and in the frame composition ends) the list takes **no** keys — ↑ / ↓ / Enter belong to the IME's candidate window. Composition does not change `text`, so suggestions update when the text is committed.
- On mobile, TMP shows its native input bar by default; not changed here. A field covered by the keyboard is not handled.

## Lint

| Code | Trigger | Runtime |
|---|---|---|
| `PUI-SUGGEST-OUTSIDE` | under a built-in tag other than `<InputField>` (a Template-invocation parent is judged on the expanded pass) | warning; stays closed |
| `PUI-SUGGEST-DUPLICATE` | a second `<Suggestions>` under one field | warning; parked inactive |
| `PUI-SUGGEST-TEMPLATE` | no / empty `itemTemplate` | `BindItems` throws |
| `PUI-SUGGEST-LAYOUT-ATTR` | a layout attribute (list above) | warning; ignored |
| `PUI-SUGGEST-CHILD` | a child other than `<Decor>` / `<Scrollbar>` | warning |
| `PUI-SUGGEST-ENUM` | `placement` not `below` / `above` (optionally `-start` / `-end`) | throws |
| `PUI-SUGGEST-VALUE` | `maxHeight` / `gap` / `popupWidth` not a number ≥ 0 | warning; default used |
| `PUI-SUGGEST-MULTILINE` | the field's `lineType` is not `single` | warning |
| `PUI-SUGGEST-ROW-SELECTABLE` | the row template contains a Selectable (CLI, expanded pass) | warning once, when the first such row is built |

`<Suggestions>` is also a scrollbar host (`PUI-SCROLLBAR-OUTSIDE` does not fire under it), takes `PUI-PADDING-INT`, counts as an interactive hit layer for `PUI-RAYCAST-UNDECIDED`, and its row template's text is checked by `PUI-FONT-FLOOR`.
