# Tabs (`<TabBar>` / `<TabMenu>` / `<Tab>`)

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<TabBar>` / `<Tab>` attribute tables live in the main doc's built-in primitives catalog; read this for layout patterns, the `<TabMenu>` attribute table, and lint rules. State colours: see [`states.md`](states.md).

**Two containers, one set of rows.** `<TabBar>` lays its `<Tab>`s out in a bar; `<TabMenu>` folds the same tabs into a popup and shows the selected one as its handle. Everything a `<Tab>` does — `bind=`, `isOn`, state colours, `selectedSprite`, procedural surfaces, `<Show on="state-*">`, `BindItems` — is identical in both.

`<TabBar>` 是 Tab 的容器；私有 `ToggleGroup`（`allowSwitchOff=false`）保证互斥，私有 `HorizontalLayoutGroup` / `VerticalLayoutGroup`（看 `direction=`）排布。**TabBar 本身没视觉**，只是布局容器；每个 `<Tab>` 自带 `sprite`（常态底）；`selectedSprite` 在选中时把 bg 自己的 `overrideSprite` 换成它、取消选中再换回 `sprite`（单图，无独立 overlay 节点；由 `isOn` 驱动，hover/按下选中 tab 不影响它；设了会把 `transition` 切到 None）。不写 `selectedSprite` 时 Tab 退化成纯色按钮视觉，互斥仍在。`selectedSprite=""` / `none` = 无 swap（no-op）。

```xml
<TabBar id="topbar" anchor="top-stretch" height="40">
  <Tab text="Edit"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="editor_panel" isOn="true"/>
  <Tab text="Help"     sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="help_panel"/>
  <Tab text="Settings" sprite="ui:tab_normal" selectedSprite="ui:tab_selected" bind="settings_panel"/>
</TabBar>

<Frame id="editor_panel"   margin="40,0,0,0">...</Frame>
<Frame id="help_panel"     margin="40,0,0,0">...</Frame>
<Frame id="settings_panel" margin="40,0,0,0">...</Frame>
```

要把所有 Tab 的视觉抽出来共享,定义一个 `<Template>` 让 Tab 在里面声明 sprite,然后当作 TabBar 子元素或者 `itemTemplate=` 使用（见下面 "Custom Tab layout via Template"）。

要给 TabBar 本身加一条背景条,直接把 TabBar 套进一个 `<Image>` —— `<Image>` 接受子节点,一层搞定。注意 Image / TabBar 的默认 anchor 是 top-left,需要显式写 `anchor=` 把它们撑开:

```xml
<Image sprite="ui:tabbar_bg" anchor="top-stretch" height="40">
  <TabBar id="topbar" anchor="stretch">...</TabBar>
</Image>
```

`bind="frame_id"` 让 Tab 选中时显示、未选时隐藏命名 Frame。lookup 是 lazy 的 —— 首次切换才解析并缓存。Tab `isOn="true"` 在 XML 里指定初始选中；都没写时 TabBar 自动选第一个。`bind=` 省略时只 fire `OnSelected`（C# 端自己处理）。`isOn` 是**运行时独占状态**：声明值是初始选中，但一旦用户/代码运行期改过它，ReSolve（窗口 resize / Variant / Theme 切换）**不会**把它打回声明默认值 —— 用户选中的 Tab 和 `bind` 的页面都保持不变。`isOn.variant`（如 `isOn.portrait`）仍然有效：只要运行期没动过，切到该 Variant 会正常重应用覆盖值；动过之后用户的选择优先。`<Toggle isOn>` 同理。（同款「运行期改过就不打回、没动过 Variant 照常覆盖」也适用于 `<Slider value>` / `<Dropdown value>` / `<Progress value>`。）

用自定义 `itemTemplate` 时（`<TabBar itemTemplate="MyTabTemplate"/>`），Template body 必须在树里某处包含恰好一个 `<Tab>`（通过 `ScopedIds` 或递归 `Control.Children` walk 在 `BindItems` 时定位）。

Tab 是 TabBar 的 layout group child —— 不能写 `anchor=` / `margin=`（`HorizontalLayoutGroup` 接管排布；TabBar 在 `selfIsLayoutGroup` 名单里）。

## Custom Tab layout

### Via Template (for shared styling across instances / dynamic BindItems)

When several tabs share one rich face, or tabs are generated at runtime from data, make the `<Tab>` the
Template root and put its content **inside** the Tab (children overlay the bg, Frame-style). `sprite` /
`selectedSprite` live on the `<Tab>` so every instance shares them without restating:

```xml
<Template name="FileTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="bind"/>
  <Param name="isOn" default="false"/>
  <Tab id="tab" width="80" height="96"
       sprite="ui:cell_normal" selectedSprite="ui:cell_selected"
       isOn="{{isOn}}" bind="{{bind}}">
    <Icon id="icon" name="ui:icon_{{icon}}"
          anchor="top-center" width="48" height="48"
          margin="8,0,0,0"/>
    <Text id="name" anchor="top-stretch" margin="60,4,0,4"
          fontSize="12" align="center" raycastTarget="false">{{text}}</Text>
  </Tab>
</Template>

<TabBar id="files">
  <FileTab text="111.png" icon="png" isOn="true" bind="panel1"/>
  <FileTab text="222.jpg" icon="jpg" bind="panel2"/>
</TabBar>
```

TabBar collects the Tab whether it is the Template root (as here) or nested inside a wrapper; auto-select and `OnSelectionChanged` work the same either way. Lint rules `PUI-TABBAR-CHILD` and `PUI-TAB-PARENT` are suppressed for Template-instance roots. The Tab's `width`/`height` is its layout-group cell size; its children use their own `anchor` / `margin` (Tab is not a layout group). Omit them and the Tab sizes to its own label (plus padding, min 44px tap target) — it never collapses to zero, **as long as the `<Tab>` is the Template root** (see the wrapper note below). `width="stretch"` splits the bar's remaining space evenly, exactly as in `<HStack>`.

> ⚠️ **Wrapper roots must carry the size themselves.** That zero-collapse safety net is `Tab.GetNativeSize()`, and it only fires for the node TabBar actually lays out. Wrap the Tab — `<Frame><Tab .../></Frame>`, the usual way to hang a separator / badge / second layer *outside* the Tab's own bg — and TabBar's layout child is the `<Frame>`, which has no native size at all. With `childControlWidth/Height = true` and nothing written on the wrapper, its preferred size resolves to **0**: every tab collapses and piles up at the same spot. Exactly the `<HStack>` rule, and it bites the same way — **put `width` / `height` (or `width="stretch"`) on the wrapper root, not only on the inner `<Tab>`**:
>
> ```xml
> <Template name="ChannelTab">
>   <Param name="text"/>
>   <Param name="sep" default="true"/>
>   <!-- 尺寸写在 wrapper 上；漏了就塌成 0 宽，三个 tab 重叠在 x=0 -->
>   <Frame width="stretch" height="18">
>     <Tab id="tab" anchor="stretch" sprite="" selectedSprite="ui:tab_selected">
>       <Text anchor="stretch" align="center" fontSize="12" raycastTarget="false">{{text}}</Text>
>     </Tab>
>     <Image if="{{sep}}" anchor="stretch-right" width="1" raycastTarget="false"/>
>   </Frame>
> </Template>
> ```
>
> The inner `<Tab anchor="stretch">` then fills the wrapper — it is a free-positioned child of a `<Frame>`, not a layout-group child, so `anchor` / `margin` are legal on it there.

> ⚠️ **Behaviour change.** `width` / `height` on a `<Tab>` used to be silently ignored — TabBar's layout group was left at Unity's default `childControlWidth/Height = false`, which only positions children and never resizes them, so every Tab stayed at the default 100×100 (overflowing the bar and overlapping its neighbours). TabBar now configures the group like `<VStack>` / `<HStack>` do, so the values you write actually land. The other half of the same switch: `childForceExpand*` is now `false` where Unity's serialized default is `true`, so Unity no longer forces `flexible = 1` onto every child in `GetChildSizes` — tabs that used to spread out and fill the bar regardless of the markup now sit at their preferred size, packed at the start of the bar (and a sizeless wrapper root, at width 0). Existing TabBars will shift — toward what the markup always said; add `width="stretch"` to get the even split back. Keep decorative children `raycastTarget=false` (`<Icon>` already is; add it on `<Text>`) so clicks fall through to the containing Tab.

For dynamic data, use `BindItems` with `itemTemplate="FileTab"` (the same Template works for both patterns).

## `<TabMenu>` — the folded tab group

Collapsed, it is the selected tab's icon + text + a caret; expanded, a panel of `<Tab>` rows drops
below it. Reach for it when a bar would not fit — a channel switcher in a chat header, a sort order,
a folded-up navigation.

```xml
<Style name="menu-item" sprite="" height="44" radius="8"
       hoverColor="white/0.08" selectedColor="primary/0.35" fontSize="18"/>

<HStack anchor="top-stretch" height="64" padding="0,16,0,16" spacing="8">
  <TabMenu id="channel" fontSize="22" textColor="white" iconSize="24"
           popupWidth="240" padding="8" spacing="4"
           radius="12" glass="true" color="primary-dark/0.6" borderWidth="1">
    <Tab class="menu-item" icon="ui:globe" text="World"  bind="ch_world" isOn="true"/>
    <Tab class="menu-item" icon="ui:guild" text="Guild"  bind="ch_guild"/>
    <Tab class="menu-item" icon="ui:team"  text="Party"  bind="ch_party"/>
  </TabMenu>
  <Frame width="stretch"/>
  <Text fontSize="20">128</Text>
</HStack>

<Frame id="ch_world" anchor="stretch" margin="64,0,0,0">…</Frame>
<Frame id="ch_guild" anchor="stretch" margin="64,0,0,0">…</Frame>
<Frame id="ch_party" anchor="stretch" margin="64,0,0,0">…</Frame>
```

### The surface is the popup, not the handle

This is the one thing to internalise: **`color` / `sprite` / `radius` / `glass` / `borderWidth` /
`<Decor>` on a `<TabMenu>` all describe the panel that drops down.** The collapsed handle is
transparent by design and hugs its caption, so the caret sits right after the text.

Want a background behind the handle too? Wrap it — the same move that gives a `<TabBar>` a bar:

```xml
<Frame radius="20" color="surface/0.6" borderWidth="1">
  <TabMenu id="sort" anchor="stretch" margin="0,12,0,12" fontSize="16">
    <Tab text="Newest" isOn="true"/>
    <Tab text="Hottest"/>
  </TabMenu>
</Frame>
```

### Attributes

Plus every common attribute (`anchor` / `size` / `margin` / `hidden` / `interactable` / `class` /
`if` / `focus` / `nav*`) and all fifteen procedural ones — **which land on the popup panel**.

**The handle:**

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `fontSize` | float | `24` | Caption size |
| `textColor` | color | ink | Caption colour — distinct from `color`, which fills the panel |
| `font` | string | `default` | Font slot |
| `iconSize` | float | `24` | Caption icon edge; the slot takes no space when the selected tab has no `icon` |
| `arrow` · `arrowColor` | sprite / color | `pugui_caret` | `arrow=""` hides the caret (a sprite-less Image would draw a solid block). **Flips vertically** while open — mirrored, not rotated, so it never shifts sideways |
| `arrowSize` | float | `16` | Caret edge |
| `gap` | float | `6` | Space between icon, label and caret |

**The popup:**

| Attribute | Type | Default | Notes |
|---|---|---|---|
| `popupWidth` | float | auto | Panel width. Unset = the wider of the handle and the rows |
| `popupGap` | float | `4` | Space between handle and panel |
| `padding` | `t,r,b,l` / `v,h` / `all` | `0` | Inside the panel |
| `spacing` | float | `0` | Between rows |
| `color` · `sprite` · `tint` | | rounded white | The panel's fill / skin. `sprite=""` = flat colour |
| `transition` | duration | `0.15s` | Open / close animation. `0` snaps |
| `itemTemplate` | tag / Template | `Tab` | For `BindItems`, same as `<TabBar>` |

The handle is measured from the **selected** tab's text and icon at open time, so it is laid out at
the right width from the first frame. Like `<Btn>`, its layout box does not re-measure when the
caption changes at runtime — the caption contents (label, then caret) do follow the new text, but
neighbours in an `<HStack>` only shift on the next `ReSolve`. Give the handle an explicit `width` if
your channel names vary wildly in length.

### Restyling the caret

`arrow=` takes the same sprite key as any other sprite attribute — `ui:chevron` (SpriteSet) or a bare
`Resources` path — and `arrowColor` / `arrowSize` do the rest. Pack them into a `<Style>` and pull it
in with `class=` like any other attribute bag:

```xml
<Style name="menu-caret" arrow="ui:chevron_down" arrowColor="white/0.7" arrowSize="14" gap="8"/>

<TabMenu id="channel" class="menu-caret" fontSize="22">…</TabMenu>
```

`arrow=""` (or `arrow="none"`) hides it entirely — the caret is a glyph, not a panel, so a
sprite-less `Image` would render as a solid block rather than nothing.

The caret is a single `Image`, not an authorable subtree: it draws whatever sprite you hand it,
flipped vertically while the menu is open. If you need two genuinely different glyphs for the two
states, hide the built-in one (`arrow=""`) and put your own `<Show on="state-*">` pair inside a
wrapping `<Frame>` next to the menu.

### `<Tab>` inside a `<TabMenu>`

Identical to a `<Tab>` in a bar, with two differences:

- **`width` does nothing** — rows always span the panel (`PUI-TABMENU-ITEM-WIDTH` says so). Size the
  menu with `popupWidth` instead. `height` works normally; omit it and the row sizes to its label.
- `anchor` / `margin` are illegal, as in any layout group.

The selected tab's `text` and `icon` are what the handle mirrors, so a Template-wrapped row that
carries its content in child `<Text>` / `<Icon>` nodes leaves the caption empty — put `text=` on the
`<Tab>` itself.

### Opening and closing

Clicking the handle toggles. The menu closes when a row is picked (**including re-picking the one
already selected**), when the click-catcher behind the panel is clicked, and on Escape / gamepad B.
At most one menu is open anywhere at a time.

Expanded is **runtime state**: a resize, a Variant flip or a theme switch re-measures the panel but
never closes it, and there is no `expanded=` attribute — a menu always starts closed.

The panel gets its own `Canvas` with `overrideSorting` while open, which is what lifts it above the
page **and** frees it from an ancestor mask — a `<TabMenu>` inside a `<ScrollList>` still drops a
full, unclipped menu. It hangs below the handle, left-aligned; it flips above when there is not
enough room below and more above, and slides back inside the right edge if it would overflow.

### Animating the rows

The panel is an internal node, so you cannot wrap it in an `<Animation>` — that is what `transition`
is for. The rows are yours, via `on="expand"` / `on="collapse"`
(see [`animations.md`](animations.md)):

```xml
<Template name="ChannelRow">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="bind"/>
  <Animation on="expand" type="slidein-left" duration="0.12s">
    <Tab id="tab" class="menu-item" icon="ui:{{icon}}" text="{{text}}" bind="{{bind}}"/>
  </Animation>
</Template>

<TabMenu id="channel" itemTemplate="ChannelRow" popupWidth="240" padding="8"/>
```

```csharp
screen.Get<TabMenu>("channel")
      .BindItems(channels, (Tab tab, Channel c) => { tab.Text = c.Name; tab.Icon = c.IconKey; })
      .AddTo(screen);
```

### Lint rules

| Code | Fires when | Level |
|---|---|---|
| `PUI-TABMENU-CHILD` | A direct child's subtree holds no `<Tab>`, is not a Template invocation, and is not `<Decor>` | warning |
| `PUI-TABMENU-ITEM-WIDTH` | A row (or the `<Tab>` inside it) declares `width` — including a variant-only one | warning |
| `PUI-EXPAND-NO-SOURCE` | A bare `on="expand"` / `on="collapse"` with no `<TabMenu>` ancestor (`@id` forms and Template bodies exempt) | error |
| `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` | A row writes `anchor` / `margin` | error |

## Lint 规则

| Code                   | 触发条件                                                                              | 级别    |
| ---------------------- | ------------------------------------------------------------------------------------- | ------- |
| `PUI-TAB-PARENT`       | `<Tab>` 不在 `<TabBar>` / `<TabMenu>` 直接父节点下（Template-instance root 内的 Tab 已豁免） | warning |
| `PUI-TABBAR-CHILD`     | `<TabBar>` 的直接子节点不是 `<Tab>`，且子树里既无字面 `<Tab>` 也无模板调用（Template wrapper 与模板调用均已豁免——后者是非内置 tag，CLI 不展开 `<Import>`，可能含 `<Tab>`，如 `itemTemplate` 项） | warning |
| `PUI-TABBAR-DIRECTION` | `direction` 不是 `horizontal` / `vertical`                                            | error   |
