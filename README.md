# PromptUGUI

这是一个适合大模型驱动开发的 uGUI 框架，提供极其精简的 UI 描述语言 `.ui.xml` 和一个运行时解析器，翻译成uGUI结构。

如果你需要uGUI的自由，但又需要大模型快速理解和拼接界面，这个包可能适合你。

- 极简的XML描述语言，符合大模型习惯，节约Token
  - 运行时解析，支持热更新和响应式UI
  - XSD Schema定义语法，编辑器自动提示和检查，减少出错
  - 允许创建模板做自定义控件，也可以直接把Prefab当成模板
- 自动icon引用
  - 把目录添加到IconSet，xml里用`<Icon name="solar:Forward" />`引用，并且只会打包用到的
  - 建议使用web开发习惯的图标库，适合大模型选用
- 已内置全自动多国语言系统：
  - 自动提取界面文本，以及代码中Tr()包裹的字符串
  - 自动携带上下文交给OpenAI兼容的模型自动翻译。对于翻译不准确的内容，用户可在文字上添加注释，会携带到上下文

## 安装/升级方法

### Claude Code

（可选：打开Unity和Unity MCP），在项目目录执行以下提示词：

```
我希望安装 PromptUGUI 到本Unity项目，请curl获取后遵循指导： https://github.com/Heerozh/PromptUGUI/raw/refs/heads/main/install_for_claude.md
```

升级同样。

> Skill 文件遵循开放 [Agent Skills](https://agentskills.io) 规范，兼容平台（Codex / Gemini CLI 等）也可复用，只需放到对应平台的 skill 目录。

## 使用方法

1. 创建IconSet

    Project 右键 → Create → PromptUGUI → IconSet，拖一个PNG图标集目录（比如Font Awesome）
    到Project，并设为IconSet Folder，此后Skill会自动发现你所拥有的所有图标。

    使用公开图集，或使用类似文件名方便大模型正确选用。

2. 设置字体和多国语言 (可选)

    Project 右键 → Create → PromptUGUI → Settings，设置字体Type (以后font="..."使用type名) 和多国语言以及字体。

    设置好即可，以后一键翻译会自动提取界面文本和代码中Tr()包裹的字符串。

3. 创建UI

    Project 右键 → Create → PromptUGUI → UI XML。

    让大模型按你的要求（Figma > 截图）写UI。
