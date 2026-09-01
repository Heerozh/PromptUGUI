# Icon discovery & name resolution

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Icon>` tag syntax and attribute table live in the main doc's built-in primitives catalog; read this when you need to find which `setName:icon-name` combinations exist in the project, or to understand name-resolution / sync-tool behaviour.

**Discovering available icons** — to find which `setName:icon-name` combinations are valid in the current project, run from the project root:

```bash
# 1) List every SpriteSet (setName → source folder)
find . -name "*.asset" -not -path "*/Library/*" -not -path "*/Temp/*" \
  -exec grep -l "PromptUGUI.Application.SpriteSet" {} \; 2>/dev/null \
| while IFS= read -r f; do
    n=$(grep -m1 "^  setName:" "$f" | awk '{print $2}')
    g=$(grep -m1 "^  sourceFolder:" "$f" | grep -oP 'guid: \K[a-f0-9]+')
    if [ -n "$g" ]; then
      p=$(grep -rl "^guid: $g$" --include="*.meta" . 2>/dev/null | head -1)
      echo "$n -> ${p%.meta}"
    else
      echo "$n -> (sourceFolder not set)"
    fi
  done
# example: solar -> Samples~/MainMenu/Icons

# 2) Search a known SpriteSet by keyword (relative path under sourceFolder, no extension)
cd <sourceFolder> && find . -iname "*<keyword>*.png" | sed 's|^\./||; s|\.png$||'
```

Icon name in XML = PNG path **relative to the SpriteSet's sourceFolder**, with `/` as separator and no extension. So `Arrow Right.png` directly under a set with `setName: solar` is `<Icon name="solar:Arrow Right"/>`; `Combat/heart.png` is `<Icon name="ui:Combat/heart"/>`. The bare basename (`ui:heart`) is also accepted as a shortcut **as long as it is unambiguous across the source folder** — when two PNGs in different subfolders share a basename you must use the path form, and the sync tool will error pointing at the candidates if XML still references the bare name. External packs (Font Awesome, Solar Icons, etc.) drop in as a folder of PNGs; create an SpriteSet ScriptableObject (`Create → PromptUGUI → Sprite Set`) pointing at it, set `setName`, then `Tools → PromptUGUI → Sprite → Sync Atlases (All Sets)` packs only the icons referenced from `.ui.xml` (plus `SpriteSet.alwaysInclude` entries).

**Atlas packing (matters for `blur` / `glow`).** Those effects sample the atlas *around* each sprite and clamp every sample to that sprite's own rectangle, which only holds one sprite while the atlas packs **without rotation and without tight packing**. `Sync Atlases` sets both off on atlases it creates, and warns (without changing anything) when an existing atlas has either on — repacking moves every sprite, so that is the author's call: turn them off in the SpriteAtlas inspector, then **Pack Preview**. Rotation is worth fixing regardless: uGUI's `Image` draws a rotated atlas entry wrong with or without the effects.

**Variant overrides on literal `<Icon>`**: `<Icon name="ui:sun" name.dark="ui:moon"/>` — the scanner reads both `name` and every `name.<variant>` value, so each candidate sprite is packed.

**Template-Param-driven icon names**: the sync tool follows two recognized substitution shapes inside a `<Template>` body (also applies to `name.<variant>` overrides):

- Full placeholder — `<Icon name="{{iconName}}"/>`. Treats each invocation arg (`<MyIcon iconName="solar:Bell Bing"/>`) as a complete `set:icon` ref. Param `default=` also counts.
- Partial placeholder — `<Icon name="solar:{{x}}"/>`. Treats each invocation arg as the icon-name half, paired with the literal `solar` set.

Anything else inside a Template body (`{{a}}:{{b}}`, `solar:{{a}}-{{b}}`, multi-placeholder) is unanalyzable — the syncer logs a warning. Same for forwarded args (one Template's Param fed verbatim into another's). For unanalyzable cases, list final values in `SpriteSet.alwaysInclude`. Outside a `<Template>` (a literal `<Icon name="ui:{{x}}"/>` directly in a Screen) is always unanalyzable too.

## Inline sprites in text (`<sprite name="...">`, 图文混排)

`<Icon>` places a standalone image; to drop a SpriteSet icon **inside a text run** — a coin right after a button label, or chat emoji that wrap together with the words — use TextMeshPro's native inline sprite markup instead.

**Setup (once per icon group):** tick **Generate Tmp Sprite Asset** on the SpriteSet asset (Inspector — it renders right under the default fields), then run `Tools → PromptUGUI → Sprite → Sync Atlases`. The sync bakes every *flagged* set's sprites into one global `TMP_SpriteAsset` and assigns it as the project's TextMeshPro **default sprite asset**. Only flagged sets are baked — window borders, button 9-slices, and other non-icon sprites stay out of it. The *whole* flagged set is baked (not only the icons referenced from `.ui.xml`), so emoji chosen at runtime work too.

**Authoring (no new XML attribute — plain TMP rich-text in any `text=`):**

```xml
<Btn text="Confirm &lt;sprite name=&quot;coin&quot;&gt;"/>
<Text text="lol &lt;sprite name=&quot;smile&quot;&gt; nice"/>
```

(`&lt;` / `&quot;` are just the XML escapes for `<` / `"` — the runtime text is `Confirm <sprite name="coin">`.)

- The glyph name is the icon's **bare basename** (`coin`, `smile`) — the same bare name `<Icon>` accepts as a shortcut.
- Names must be **unique across all flagged sets**. A collision aborts the sync with an error (`[InlineSprite] glyph name collision ...`) — rename the offending sprite.
- The baked sheet is point-filtered and uncompressed (crisp for pixel-art).
- Independent of the per-set `.spriteatlas` (which still serves `<Image>` / `<Icon>`): a flagged set does not need its icons referenced anywhere to be baked. The generated asset lives at `Assets/PromptUGUI.Generated/InlineSprites.asset`.

## `.pxl` text sprites

A SpriteSet `sourceFolder` may also contain `.pxl` files — LLM-authored pixel-grid
text that imports directly as point-filtered Sprites (with 9-slice border / PPU
declared in-file). Multi-section files contribute `path/section` keys (unique bare
section names work as aliases). Full format and drawing guidance: the
**authoring-promptugui-pxl** skill.
