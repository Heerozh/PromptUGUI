# PromptUGUI

这是一个适合大模型驱动开发的 uGUI 框架，提供极其精简的 UI 描述语言 `.ui.xml` 和一个运行时解析器，翻译成uGUI结构。

如果你需要uGUI的自由，但又需要大模型快速理解和拼接界面，这个包可能适合你。

- 极简的XML描述语言，符合大模型习惯
  - 支持热重载，改完立刻反馈
  - 支持响应式UI
  - 自动XSD Schema语法检查
  - 高扩展性，允许创建模板做自定义控件，也可以直接把Prefab当成模板
- 自动icon引用
  - 把目录添加到IconSet，xml里用`<Icon name="solar:Forward" />`引用，并且只会打包用到的
  - 建议使用web开发习惯的图标库，适合大模型选用
- 已内置全自动多国语言系统：
  - 自动提取界面文本，以及代码中Tr()包裹的字符串
  - 自动携带上下文交给OpenAI兼容的模型自动翻译。对于翻译不准确的内容，用户可在文字上添加注释，会携带到上下文
- Addressable随需下载/热更支持
  - 图标集和多国语言可按Label方式随需下载 ("Iconset:banner" AND "Locale:zh-Hans")
  - UI XML可以作为Addressable Reference直接拖入

## 安装/升级方法

### Claude Code

（可选：打开Unity和Unity MCP），在项目目录执行以下提示词：

```
我希望安装 PromptUGUI 到本Unity项目，请curl获取后遵循指导： https://github.com/Heerozh/PromptUGUI/raw/refs/heads/main/install_for_claude.md
```

升级同样。

### 手动安装：

0. Prerequisite:

Install NuGetForUnity: https://github.com/GlitchEnzo/NuGetForUnity
Install R3: github.com/Cysharp/R3
Install LitMotion: https://github.com/annulusgames/LitMotion.git

1. UPM
Window > Package Manager > "+" > "Add package from git URL" > Enter:

```
https://github.com/heerozh/PromptUGUI.git
```

2. Skills
把包内的 `.claude/skills/` 目录下的三个 skill 目录 copy 到你项目对应agent的skills目录下
Claude Code: `<project root>/.claude/skills/`
Codex: `<project root>/.agents/skills/`

> Skill 文件遵循开放 [Agent Skills](https://agentskills.io) 规范，兼容平台（Codex / Gemini CLI 等）也可复用，只需放到对应平台的 skill 目录。

3. AGENT.md / CLAUDE.md
把以下内容写到项目全局提示词：
```
Use `Tr("...")` to wrap all player-facing text for i18n.
```

## 使用方法

### 1. 创建IconSet

Project 右键 → Create → PromptUGUI → IconSet，拖一个PNG图标集目录（比如Font Awesome）到Project，并设为IconSet Folder，此后Skill会自动发现你所拥有的所有图标。

使用公开图集，或图标名保持近似，让大模型认识。

**推荐**使用Addressable，把多个IconSet和对应的SpriteAtlas打成一个Label，图标带文字的打上语言标签，然后用`await IconResolverHelpers.UseAddressableSpriteAtlasIconResolver({label, "Locale:zh-Hans"}, MergeMode.Intersection);`
就可以实现按需下载和热更对应语言的图标集。

### 2. 设置字体和多国语言 (可选)

Project 右键 → Create → PromptUGUI → Settings，设置字体Type (以后font="..."使用type名) 和多国语言以及字体。

设置好即可，以后一键翻译会自动提取界面文本和代码中Tr()包裹的字符串。

**建议**使用Addressable，点击菜单的`Setup Addressable for Locale ...`后，i18n目录即可移出Resources目录，放到其他目录。通过`UI.Locale.UseAddressableResolver();`后，`UI.Locale.SetToSystemDefault("en");`会自动后台下载多国语言，下完自动刷新界面。

### 3. 创建UI

Project 右键 → Create → PromptUGUI → UI XML。

让大模型按你的要求（Figma > 截图）写UI，XML的修改会自动反映在Play模式的界面上。

第一个界面大模型没有参考，选用的图素都是默认值，你需要手动修改或个别一一指示，之后会更顺利。

**建议**使用Addressable，`[SerializeField] private AssetReferenceT<TextAsset> xmlSlot` 定义属性，
然后就能在Inspector中把`*.ui.xml`文件拖入`xmlSlot`，在脚本中`await UI.LoadDocumentAsync(xmlSlot);`按需下载和热更。
