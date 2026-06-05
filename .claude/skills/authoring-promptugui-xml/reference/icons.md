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

**Variant overrides on literal `<Icon>`**: `<Icon name="ui:sun" name.dark="ui:moon"/>` — the scanner reads both `name` and every `name.<variant>` value, so each candidate sprite is packed.

**Template-Param-driven icon names**: the sync tool follows two recognized substitution shapes inside a `<Template>` body (also applies to `name.<variant>` overrides):

- Full placeholder — `<Icon name="{{iconName}}"/>`. Treats each invocation arg (`<MyIcon iconName="solar:Bell Bing"/>`) as a complete `set:icon` ref. Param `default=` also counts.
- Partial placeholder — `<Icon name="solar:{{x}}"/>`. Treats each invocation arg as the icon-name half, paired with the literal `solar` set.

Anything else inside a Template body (`{{a}}:{{b}}`, `solar:{{a}}-{{b}}`, multi-placeholder) is unanalyzable — the syncer logs a warning. Same for forwarded args (one Template's Param fed verbatim into another's). For unanalyzable cases, list final values in `SpriteSet.alwaysInclude`. Outside a `<Template>` (a literal `<Icon name="ui:{{x}}"/>` directly in a Screen) is always unanalyzable too.
