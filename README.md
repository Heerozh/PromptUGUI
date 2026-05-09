# PromptUGUI

这是一个适合大模型驱动开发的 uGUI 框架，提供极其精简的 UI 描述语言 `.ui.xml` 和一个运行时解析器，翻译成uGUI结构。

如果你需要uGUI，但又需要大模型快速理解和拼接界面，这个包可能适合你。

- 极简的XML描述语言，符合大模型习惯，节约Token
  - 运行时解析，支持热更新和响应式UI
  - XSD Schema定义语法，编辑器自动提示和检查，减少出错
  - 允许模板xml自定义控件，也可以直接把Prefab当成模板
- 自动icon引用
  - 把目录添加到IconSet，xml里用<Icon name="solar:Forward" />引用，并且只会打包用到的
  - 建议使用web开发习惯的图标库，适合大模型选用
- 已内置全自动多国语言系统：
  - 自动提取界面文本，以及代码中Tr()包裹的字符串
  - 自动携带上下文交给OpenAI兼容的模型自动翻译。对于翻译不准确的内容，用户可在文字上添加注释，会携带到上下文


## Claude Code 集成（可选）

本包内置一份 LLM 写 `.ui.xml` 的 skill，安装后 Claude Code 在编辑 `.ui.xml` 时会自动加载、按当前实现的语法生成或修改文件。

> Skill 文件在仓库的 `.claude/skills/authoring-promptugui-xml/`。Unity 的 UPM 不会把以 `.` 开头的目录 import 为 asset，但文件本身存在于 `Packages/com.promptugui.core/.claude/...`，可以直接 copy 出来用。

### 安装到当前用户（所有项目可用）

**macOS / Linux：**

```bash
cp -r Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml \
  ~/.claude/skills/
```

**Windows (PowerShell)：**

```powershell
Copy-Item -Recurse `
  ".\Packages\com.promptugui.core\.claude\skills\authoring-promptugui-xml" `
  -Destination "$env:USERPROFILE\.claude\skills\"
```

### 只装到当前 Unity 项目

把目标改成项目根的 `.claude/skills/` 即可，例如：

```bash
mkdir -p .claude/skills
cp -r Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml \
  .claude/skills/
```

### 跟随包版本同步更新（推荐给长期项目）

软链替代 copy，包升级后 skill 自动跟新：

**macOS / Linux：**

```bash
ln -s "$(pwd)/Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml" \
  ~/.claude/skills/authoring-promptugui-xml
```

**Windows (管理员 PowerShell)：**

```powershell
New-Item -ItemType SymbolicLink `
  -Path "$env:USERPROFILE\.claude\skills\authoring-promptugui-xml" `
  -Target "$(Resolve-Path '.\Packages\com.promptugui.core\.claude\skills\authoring-promptugui-xml')"
```

### 验证

安装后在 Claude Code 任意 session 里：

```
/skills
```

应该能看到 `authoring-promptugui-xml` 列在可用 skill 中。之后向 Claude 描述要做的 UI（"给我做个登录界面，左边输入框右边按钮..."），它会基于本包当前实现的语法生成 `.ui.xml`，无需你逐字解释属性名。

> Skill 文件遵循开放 [Agent Skills](https://agentskills.io) 规范，未来兼容平台（Codex / Gemini CLI 等）也可复用，只需放到对应平台的 skill 目录。
