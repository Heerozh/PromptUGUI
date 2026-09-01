# `<Collapsible>` — the inline fold

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md).
> Read this before writing a `<Collapsible>` or a `<Header>`. For the popup-shaped tab group see
> [`controls-tabs.md`](controls-tabs.md) → `<TabMenu>`; for the `expand` / `collapse` triggers and
> `reverse-on`, [`animations.md`](animations.md); for the header bar's per-state visuals,
> [`states.md`](states.md).

A header bar that stays put and a body that opens and closes under it. Folding it **re-flows the
page** — whatever follows moves up and back down — which is the whole difference from a popup.

```xml
<Collapsible id="tasks" text="任务" anchor="top-right" width="150" margin="90,20,_,_"
             sprite="none" color="surface/0.55" radius="10" headerHeight="24" transition="0.2s">
  <ScrollList id="list" itemTemplate="TaskRow" width="stretch" height="clamp(_, hug, 200)"
              sprite="none" scrollbar=""/>
</Collapsible>
```

## `<TabMenu>` or `<Collapsible>`?

| | `<TabMenu>` | `<Collapsible>` |
|---|---|---|
| Opens as | a popup on its own canvas, over the page | an inline panel, inside the page |
| The page around it | covered by a full-screen click catcher | untouched, still clickable |
| How many open | one anywhere (opening a second closes the first) | as many as you like — unless a `group` says otherwise |
| Closes on | choosing a row, Escape, clicking away | only its own header (or code) |
| Its content | a column of `<Tab>` rows, mutually exclusive | any subtree |
| Layout effect | none — it floats | pushes what follows down |

A channel switcher is a `<TabMenu>`. A settings section, a HUD tracker, a details disclosure is a
`<Collapsible>`.

## Structure

```
Collapsible            Image(面板底) + VerticalLayoutGroup + [ProceduralPanel] + ExpandableMarker
├─ Header              Image(headerColor) + PuiButton + LayoutElement(preferredHeight = headerHeight)
│   ├─ Icon / Label    built-in caption (lazy — only what you write)
│   ├─ Host            <Header>'s children land here (created only when there is a <Header>)
│   └─ Arrow           Image + RotateFlipEffect (0° open, 180° closed)
└─ Body                RectMask2D + CanvasGroup + HugElement + LayoutLink [+ ScrollRect if maxHeight]
    └─ Content         VerticalLayoutGroup + ContentSizeFitter   ← the author's body children
```

Two of those are worth knowing about:

- **`HugElement` on the body**, not a `LayoutElement`. The body republishes the content's height on
  every layout pass, so an open panel follows its rows live — hide one, push new ones through
  `BindItems`, switch language, and the panel re-flows with nothing to notify. A fold in progress
  overrides that value with the tween's.
- **`LayoutLink` on the body** — a layout controller that controls nothing. uGUI's dirty-walk goes UP
  only through layout groups and its control pass skips a subtree whose root has no controller, so a
  node that sits between a group and its content (exactly what the body is) has to be a link or both
  walks stop there.

## Height is not yours to give

`height=` / `size=` on a `<Collapsible>` is a parse error (`PUI-COLLAPSIBLE-HEIGHT`), CLI **and**
runtime. The panel is exactly `headerHeight + body`, and folding it is what changes that.

- Too tall? Cap the **body**: `maxHeight="200"` — past the cap it scrolls (drag / wheel).
- Want a shorter bar? `headerHeight="24"`.
- Width is ordinary: a number, `stretch` inside a stack, `N%` / `clamp(...)` free-positioned. Omit it
  and the panel hugs the wider of its caption and its rows.
- A vertical `margin` only **positions** the panel (`margin="46,6,_,_"` = 46 below the parent's top
  edge, growing downward from there). It never eats into the height: the panel is `headerHeight +
  body` at every fold state, collapsed included.
- Anchor-stretching the vertical axis (`anchor="stretch"`) hands the height back to the layout — the
  fold then happens inside whatever height it was given. Rarely what you want.

## `<Header>` — bringing your own bar

```xml
<Collapsible id="tasks" width="150">
  <Header>
    <HStack anchor="stretch" padding="0,8" spacing="6">
      <Icon name="ui:quest" size="14x14"/>
      <Text width="stretch">任务</Text>
      <Text id="count" tr="false">3</Text>
    </HStack>
  </Header>
  <TaskRow .../>
  <TaskRow .../>
</Collapsible>
```

- **First child, at most one, no attributes of its own** (`PUI-COLLAPSIBLE-HEADER-FIRST` /
  `-MULTI`; an attribute is a parse error naming `headerHeight=`). Everything after it is the body.
- Its children are **free-positioned** against the bar minus the caret's zone — `anchor` / `margin`
  are legal there, unlike in the body.
- The **caret is still drawn by the library** and still turns: `arrow=` / `arrowColor=` /
  `arrowSize=` keep working. `arrow=""` hides it and gives that width back.
- Mixing it with the built-in caption attributes (`text` / `icon` / `iconColor` / `font` /
  `fontSize` / `textColor`) is `PUI-COLLAPSIBLE-HEADER-CONFLICT` — they would never show.
- Ids inside a `<Header>` are in the ordinary scope: `screen.Get<Text>("count")`.
- It survives template expansion like any other node, so `{{param}}`, `if=` and `<Slot/>` work
  inside and around it.
- A `<Btn>` you put in the header takes its own clicks (uGUI hits the top-most), so a
  "settings" button on the bar does not fold the panel.

## Dynamic rows

There is no `itemTemplate` on `<Collapsible>` in v1. Use a `<ScrollList>` as the body child — it
already has one, and `height="clamp(_, hug, N)"` gives you exactly "as tall as the rows, up to N":

```xml
<Collapsible id="tasks" text="任务" width="150" headerHeight="24">
  <ScrollList id="list" itemTemplate="TaskRow" width="stretch" height="clamp(_, hug, 200)"
              sprite="none" scrollbar=""/>
</Collapsible>
```

```csharp
screen.Get<ScrollList>("tasks/list").BindItems(quests, (IControl row, Quest q) => { … });
```

## The accordion — `group=`

```xml
<VStack anchor="stretch-left" width="240" margin="16,_,16,16" spacing="4">
  <Collapsible text="画面" group="settings" width="stretch">…</Collapsible>
  <Collapsible text="音频" group="settings" width="stretch" expanded="false">…</Collapsible>
  <Collapsible text="操作" group="settings" width="stretch" expanded="false">…</Collapsible>
</VStack>
```

- Screen-scoped, name-keyed. Opening one closes the others in its group.
- **All closed is legal** (unlike a `<TabBar>`): folding the open one away is a thing readers do.
- `expanded` defaults to **true**, so in a group you write `expanded="false"` on all but the first.
  Several authored open → the first in document order wins, the rest open closed, and you get
  `PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED` (CLI) / a console warning (runtime).
- A Variant that opens another member (`expanded.portrait="true"`) closes the current one.

## The fold itself

Three channels, one duration (`transition`, default `0.2s`), `Ease.OutCubic`:

| | from → to (opening) |
|---|---|
| body height | `0 → min(content, maxHeight)` |
| body alpha | `0 → 1` |
| caret | `180° → 0°` (mesh-level, so it turns in place) |

- **Interrupting reverses from where it is** — folding back mid-transition never snaps to an end.
- Only a collapse that *finishes* switches the rows off (`SetActive(false)`), so a cancelled close
  cannot deactivate a body the user just re-opened. A closed body neither renders, nor takes clicks,
  nor appears in the navigation graph.
- `transition="0"`, outside play mode, or on a hidden panel: the end state is written directly.
- Opening / closing **at Screen open** is not an `expand` / `collapse` — those mean "it just
  changed", and they fire with the rows already active so a row's entrance animation can measure
  itself.

```xml
<Template name="TaskRow">
  <Animation on="expand" reverse-on="collapse" translate="-12,0:0,0" fade="0:1" duration="0.12s">
    <Btn id="row" width="stretch" height="32" sprite="none" color="#0000">…</Btn>
  </Animation>
</Template>
```

## `expanded` is runtime-owned

Like `isOn` / `value` / `current`: the XML value is the *initial* state, and once the user (or code)
has folded the panel, a ReSolve — theme, locale, resize, an unrelated variant — does not push it
back. A variant override of `expanded` itself still applies, because that is a declared change and
not the user's own. A theme's `<Style>` may not set it at all.

## C#

```csharp
var panel = screen.Get<Collapsible>("tasks");
panel.IsExpanded;                       // read-only
panel.Expand(); panel.Collapse();       // programmatic — these work even while not interactable
panel.Toggle();                         // the gesture — blocked when interactable="false"
panel.OnExpanded / panel.OnCollapsed;   // Observable<Unit>, same source <Animation on="expand"> uses
panel.OnToggled;                        // Observable<bool> — the new state
panel.OnState;                          // Observable<InteractState> of the header bar
panel.Text = "任务"; panel.Icon = "ui:quest";
screen.Get<Text>("tasks/count");        // a node from inside <Header>
```

## Lint

| Code | Fires when | Where |
|---|---|---|
| `PUI-COLLAPSIBLE-HEIGHT` | `height=` / `size=` (base, any variant, or through `class=`) | CLI error + **runtime throw** |
| `PUI-COLLAPSIBLE-HEADER-FIRST` | `<Header>` is not the first child | CLI + runtime warning |
| `PUI-COLLAPSIBLE-HEADER-MULTI` | more than one `<Header>` | CLI + runtime warning |
| `PUI-COLLAPSIBLE-HEADER-CONFLICT` | `<Header>` together with `text` / `icon` / `iconColor` / `font` / `fontSize` / `textColor` | CLI + runtime warning |
| `PUI-HEADER-OUTSIDE` | `<Header>` anywhere but directly inside a `<Collapsible>` | CLI + runtime warning |
| `PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED` | several members of one `group` authored open | CLI + runtime warning |
| `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` | a **body** child anchors itself (a `<Header>` child may) | CLI + runtime warning |

## Not in v1

Horizontal folds (a side drawer), `itemTemplate` / `BindItems` of its own, a scrollbar skin for the
capped body, a header at the bottom. The nesting case needs nothing special: a `<Collapsible>` inside
another one re-flows its parent on its own, because heights are content-driven all the way up.
