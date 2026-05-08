# PromptUGUI

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