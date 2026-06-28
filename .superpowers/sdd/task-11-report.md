## Task 11 Report — SKILL update + reference/navigation.md

### Files changed

| File | Change |
|---|---|
| `.claude/skills/authoring-promptugui-xml/reference/navigation.md` | NEW — 324-line deep-dive |
| `.claude/skills/authoring-promptugui-xml/SKILL.md` | +19 lines: primitives table row, 4 common-attr rows, Gamepad section, Quick Reference block |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | +55 lines: "Gamepad / Keyboard Navigation" section + Quick Reference NAV block |

### What each doc now covers

**`reference/navigation.md`**
- Navigation modes (Pointer ↔ Directional), trigger sources, touch-only behavior
- Focus visual: `InteractState.Focused` reuses hover (no `focusColor` in v1)
- `<FocusCursor>`: `side`, `offset`, child subtree = visual, first-child-only, built-in default
- Cursor animation (`<Animation on="loop">`) and templating
- `focus="true"`: document-order selection, BindItems limitation
- `nav="none"` and `navUp/navDown/navLeft/navRight`: auto-fill unspecified directions, missing-id exception
- Quick attribute table + selectable-tag list
- Modal focus trap: automatic containment + restore on close
- Lint rules: `PUI-NAV-ON-NON-SELECTABLE` (CLI + runtime) / `PUI-NAV-UNKNOWN-TARGET` (CLI only)
- Not-in-v1 section
- Two complete copy-pasteable examples: main menu + character select grid with explicit overrides

**XML SKILL.md additions**
- Primitives table: `<FocusCursor>` row
- Common attributes: `focus`, `nav`, `navUp/navDown/navLeft/navRight` with pointer to reference/navigation.md
- "Gamepad / Keyboard Navigation" summary section with pointer
- Quick Reference `GAMEPAD NAV` block

**C# SKILL.md additions**
- `## Gamepad / Keyboard Navigation` section:
  - `UI.UseGamepadNavigation()` / `UI.Navigation.Enable()` — idempotent, New Input System only, EventSystem auto-create
  - `InteractState.Focused` — reuses hover visual, subscribe via `OnState`
  - `screen.Focus(idPath)` — programmatic selection, use after BindItems
  - Modal focus trap — automatic, no markup needed
- Quick Reference `NAV` block

---

## Task 11 review-fix

### Code change

**`Runtime/Application/UI.Navigation.cs` — `IsEnabled` widened to `public`.**
`internal static bool IsEnabled { get; private set; }` → `public static bool IsEnabled { get; private set; }`. Widening change; no call-sites break. Lint clean (dotnet format --verify-no-changes --severity warn). Unity compiled with zero errors. NavEnableTests 2/2 passed.

### §2 — Numeric index path verification result

`screen.Focus("itemList/0")` is **NOT supported**. `Screen.Get(idPath)` (which `Focus` delegates to) splits by `/`, looks up segment 0 in the static `_byId` map, then resolves subsequent segments via `ScopedIds` on the found control. BindItems-generated items are never inserted into `_byId` or `ScopedIds`, so the `"0"` segment throws `KeyNotFoundException`. There is no numeric-index path form at all in the implementation. Fixed: removed the invalid example; replaced with a valid static-id call + explanatory comment that BindItems items must use the list container itself.

### §3 — Template nav forwarding verification result

`focus` and `nav*` are **NOT** in `TemplateExpander.CommonAttrs` (`anchor`, `size`, `width`, `height`, `margin`, `pivot`, `padding`, `spacing`, `hidden`, `interactable`). The expander's validation loop throws `TemplateException("<MenuBtn>: unknown attribute 'focus'")` if `focus` appears on an invocation without a matching `<Param>`. The same applies to `color="#DC2626"` on the quit invocation — also not in CommonAttrs and not a Param. Both attributes in Example 1 were broken. Fixed: added `<Param name="focus" default="false"/>` and `<Param name="color" default="#3B82F6"/>` to the MenuBtn template body, wired as `focus="{{focus}}"` and `color="{{color}}"`. Added a clarifying paragraph in both SKILL.md (Common Attributes) and navigation.md (after the quick attribute table).

### §4 — FocusCursor placement resolution

`<FocusCursor>` is NOT registered via `reg.Register<...>()` in `BuiltinPrimitives.cs` and is NOT in `BuiltinTags.cs`. The contradication was resolved by annotating the primitives-table row explicitly: "Not a registered control — not Get<T>-able; removed from the control tree before instantiation." `<FocusCursor>` stays in the primitives table (that is where authors look first) but the row now makes the structural-only status unambiguous.

### §7 — "last one wins" vs "first one wins" for multiple FocusCursor

The task description claimed "last one wins" but the parser code says otherwise:
`if (screen.FocusCursor == null) screen.FocusCursor = rootNode.Children[i];`
This is **first one wins**. Documented correctly as "first one in document order is used; later declarations are silently ignored."

### All fixes summary

| Fix | Severity | Resolution |
|---|---|---|
| `IsEnabled` public | CODE | widened to `public static bool IsEnabled { get; private set; }` |
| §1 Focused absent from OnState | CRITICAL | Added `Focused` to enum listing in body text + cheatsheet |
| §2 numeric index path | CRITICAL | Verified NOT supported; replaced invalid example |
| §3 Template nav forwarding | HIGH | Fixed Example 1 template (Params for focus+color); added clarifying note in both SKILL.md and navigation.md |
| §4 FocusCursor not-registered contradiction | HIGH | Annotated primitives-table row; navigation.md was already correct |
| §5 screen.Focus error contract | MEDIUM | Added one sentence to both C# SKILL.md and navigation.md BindItems section |
| §6 UI.Navigation not introduced as type | MEDIUM | Added note in Gamepad section + NAV cheatsheet entry |
| §7a multiple FocusCursor | LOW | Added sentence: first one wins |
| §7b child subtree ids inaccessible | LOW | Added sentence to FocusCursor section |
| §7c Pointer mode cursor hidden | LOW | Already documented at navigation.md line 36; no change needed |
| §7d type="pulse" gloss | LOW | Added inline comment "pulse preset: yoyo scale to 1.05×" in both Template example and Example 1 |

### Concerns / uncertainties for verification agent

1. **`UI.Navigation.IsEnabled` is `internal`, not `public`** — the task brief listed it as part of the C# API. The actual public API is `UI.UseGamepadNavigation()` and `UI.Navigation.Enable()`. I documented only the public surface and omitted `IsEnabled`. Verify this is correct.

2. **`UI.Navigation.DefaultCursorSrc`** — public property that takes a src key for a custom global default cursor. I mentioned it briefly in the "built-in default cursor" paragraph but did not give it a dedicated API entry (it's described as "a placeholder path for v1"). Verify the depth of documentation is appropriate.

3. **`<FocusCursor>` placement in primitive table** — `<FocusCursor>` is not a registered control, but I added it to the primitives table since that's where authors would look. The main SKILL.md section notes it is "not a registered control". Verify this is the right placement decision.

4. **Focus restored on modal close** — confirmed via `UI.Modal.cs` line 210: `es.SetSelectedGameObject(slot.PrevSelected)`. The previous selection is saved in `slot.PrevSelected` at `UI.Modal.cs` line 143. This is accurate.

5. **`<Animation on="loop">` inside `<FocusCursor>`** — the cursor's first child is instantiated via `_instantiator.InstantiateNode(cursorNode.Children[0], ...)`. Any XML markup including `<Animation>` works. Verified via `Screen.SetupFocusCursor`.
