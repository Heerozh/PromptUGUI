# `<ScrollList virtualize>` — long lists, chat logs

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md) (the `<ScrollList>` section). C# side (keyed `BindItems`, `ScrollTo*`, `IsAtEnd` / `OnAtEndChanged`): scripting-promptugui-csharp → **List / option push**.

A plain `<ScrollList>` builds a row for every item and lays all of them out, so every push costs time in proportion to the number of rows — fine for tens of rows, felt at a few hundred (a chat channel of wrapping messages: ~6 ms per message at 20 rows, ~70 ms at 200, desktop editor). `virtualize="true"` builds only the rows near the viewport — about a screenful plus a row on either side — and stands in for the rest of the list with empty space of the right height. A push, a scroll and a frame then cost about the same at 100 items as at 10,000 (a few ms per message in the desktop editor, where a plain list of 1,000 rows takes about a quarter of a second with a key and several seconds without). What still grows with the item count is each push's key diff and copy — a fraction of a microsecond per item, ~15 ms per push at 100,000.

```xml
<ScrollList id="chat" itemTemplate="ChatRow" width="stretch" height="stretch"
            virtualize="true" stickToEnd="true" spacing="4" padding="8"/>

<Template name="ChatRow">
  <HStack width="stretch" spacing="4" childAlign="upper-left">
    <Text id="time" width="44" fontSize="14"/>
    <Text id="body" width="stretch" wrap="true"/>
  </HStack>
</Template>
```

```csharp
var chat = screen.Get<ScrollList>("chat");
chat.BindItems(messages,                   // Observable<IReadOnlyList<Message>> — bind ONCE, push through it
    (IControl row, Message m) =>
    {
        row.Get<Text>("time").TextValue = m.Time;
        row.Get<Text>("body").TextValue = m.Body;
    },
    key: m => m.Id)                        // rows — and the scroll anchor — follow their messages
    .AddTo(screen);
```

## When to use it

Hundreds of rows or more, or a list that keeps growing: chat, logs, feeds, leaderboards. Below a few dozen rows a plain list is simpler and just as fast.

## Attributes

| Attribute | Type | Default | Meaning |
|---|---|---|---|
| `virtualize` | bool | `false` | Build only the rows near the viewport. **Decided once, when the list is built** (it picks Content's layout group): a variant or a theme cannot switch it. |
| `stickToEnd` | bool | `false` | While the viewport is at the end, pushes and size changes keep it there, and the first push opens at the end. Works on plain lists too; variant-switchable. |

Everything else works as on a plain list: `spacing`, `padding`, `mask`, `frame`, procedural surfaces, the `<Scrollbar>` part. Until the first `BindItems` the list is an ordinary one — static placeholder children render (in UI Preview too) and the first push destroys them, as usual.

**One vertical column only.** Not with `columns` (a grid), `direction="horizontal"`, `reorder` or `reuseItems="false"`, and not with a height that grows with the rows — `height="hug"` / `clamp(min, hug, _)` let the viewport show every row, so every row would be built (a capped `clamp(_, hug, N)` is fine). The runtime warns once and falls back; the CLI reports each as an error:

| Code | Trigger | At runtime |
|---|---|---|
| `PUI-SCROLL-VIRTUAL-LAYOUT` | with `columns` ≥ 1 or `direction="horizontal"` (base or any variant) | not virtualized — every row is built |
| `PUI-SCROLL-VIRTUAL-REORDER` | with `reorder="true"` (base or any variant) | drag-to-reorder stays off |
| `PUI-SCROLL-VIRTUAL-REUSE` | with `reuseItems="false"` | ignored — a virtual list always recycles |
| `PUI-SCROLL-VIRTUAL-HUG` | an unbounded hug height (base or any variant) | works, but builds every row |
| `PUI-SCROLL-VIRTUAL-VARIANT` | any `virtualize.<variant>=` | the value at build time stays |
| `PUI-SCROLL-VIRTUAL-TEMPLATE` | no `itemTemplate` | `BindItems` throws |

All of them read through `class=` too.

## What changes for the bind callback

The plain-list contract holds as is — write every property unconditionally, subscribe per row with `.AddTo(row)` (C# skill, **List / option push**). On top of it:

- **Only realized rows are bound — and scrolling binds too.** A row scrolling out is handed to the item scrolling in and bound again. A push rebinds every realized row, as on a plain list.
- **A row keeps nothing that `bind` does not write.** State put on a row from outside — an expanded detail block, a text set by a click handler, a running animation — leaves with the row when it is recycled for another item. Keep such state in the item and write it in `bind`.
- **Do not filter by hiding rows.** A row hidden by `bind` (`Hidden = true`, `flow="false"`) takes no space, but the list warns once: push the filtered items instead.
- **Rows may differ in height** — wrapping text is fine. A row is measured when it is built; items never built use the average so far. The scroll position does not jump while estimates get corrected (anchoring, below).
- **`SlotCount` and the rows are the realized ones**; `ItemCount` is the number of items in the last push. Rows that scrolled out wait, inactive, in a `Pool` node under the list and are still alive — a walk over the list's descendants sees them.
- **Each push is copied** into the list's own buffer (no allocation once it has grown): `bind` runs later, while scrolling, so changing your list after pushing it changes nothing on screen until the next push. Pushing the same `List<T>` instance again is fine.
- **A hidden list only stores pushes.** Of four stacked chat channels only the shown one builds and binds rows; a hidden one does that when it shows (a push to it is just the key diff and the copy). Until then its rows still show the older data.
- **Errors are logged, not thrown at the pusher.** A push travels through R3: a duplicate or null key rejects that push, and a throwing `bind` is reported after the rest of the window was bound — both through R3's unhandled-exception handler. A `bind` that throws while scrolling is logged as an error with the row's source location.

## Scroll position: sticky edges and anchoring

The list **remembers** whether it sits at an edge. Only the user moving the content — a drag, the wheel, the bar, a fling, a bounce — re-reads that from the geometry; a row changing height, the scrollbar appearing or the viewport resizing never does. That is why a chat stays at the bottom while its rows rewrap. `ScrollTo*` sets it too.

Whenever a push lands or rows are re-measured, the list keeps one thing in place:

1. **A sticky edge** — the start by default; the end with `stickToEnd="true"` (the start is then not sticky). Content shorter than the viewport is at both edges.
2. **Otherwise the first visible row**, exactly where it is on screen — found again by key after a push. If its item is gone, the next surviving item takes its place.
3. **A first push**, or a push from another `BindItems` subscription, opens at the start — at the end with `stickToEnd`.

| Situation | Result |
|---|---|
| Chat at the bottom, a message arrives (the oldest trimmed or not) | still at the bottom |
| Chat scrolled up into history, a message arrives and the oldest is trimmed | nothing moves |
| Chat at the top loading older history (prepended) | the message being read stays put |
| Leaderboard at the top, a new first place is inserted | still at the top, showing it |

While the user drags or the list coasts, the list only shifts the content by what its anchor moved — it never snaps, and an elastic bounce at either end plays out; a push lands on a valid position once the list is at rest.

A plain list (no `virtualize`) keeps its scroll offset across a push, as it always has; `stickToEnd` works on it with the same remembered edge.

## Scrolling from code

`ScrollToStart()`, `ScrollToEnd()` and `ScrollToIndex(i)` jump at once (a fling stops); `ScrollToIndex` puts item *i*'s top edge at the viewport's top edge, clamped to what can be scrolled — it counts items on a virtual list, rows (static ones included) on a plain one. On a hidden list they take effect when it shows. `IsAtEnd` reads the current layout; `OnAtEndChanged` gives the current value on subscribe and then each change — the "↓ new messages" button:

```csharp
var more = screen.Get<Btn>("more");
chat.OnAtEndChanged.Subscribe(atEnd => more.Hidden = atEnd).AddTo(screen);
more.OnClick.Subscribe(_ => chat.ScrollToEnd()).AddTo(screen);
```

## Recipe: a chat capped at 1,000 messages

```csharp
var log = new List<Message>();                          // yours — each push is copied, so reuse it
var feed = new Subject<IReadOnlyList<Message>>();       // a Subject re-emits the same instance;
chat.BindItems(feed, Bind, key: m => m.Id).AddTo(screen);   // a ReactiveProperty would dedupe it

void OnMessage(Message m)
{
    log.Add(m);
    if (log.Count > 1000) log.RemoveAt(0);              // trim the oldest
    feed.OnNext(log);                                   // key diff + copy are O(n); the rest is O(window)
}
```

With `stickToEnd="true"` the chat follows new messages while it is at the bottom, stays on the message being read while the user is scrolled up, and the trim at the top does not move it.
