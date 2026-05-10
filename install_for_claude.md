# PromptUGUI 安装指南（写给 AI 助手）

> 本文档写给 AI 编程助手（Claude Code / Copilot / Cursor 等）。
> 当用户说"帮我装一下 PromptUGUI"或把这份文件给你时，按以下步骤**顺序**执行，每步做完对应验证再进下一步。

所有路径以**用户的 Unity 项目根目录**（`<project root>`，即 `Packages/`、`Assets/` 所在层）为基准。

## 前置条件

1. Unity 6000.0+ 项目
2. `<project root>/Packages/manifest.json` 存在
3. R3（Cysharp）已装好——PromptUGUI 运行时依赖。通常通过 NuGetForUnity 安装 `R3` 包；如未装，先告诉用户装好再继续

## 步骤 1：把包加到 manifest

读 `<project root>/Packages/manifest.json`，在 `dependencies` 对象里加一行（如已存在则跳过）：

```json
"com.promptugui.core": "https://github.com/heerozh/PromptUGUI.git"
```

也可以让用户走 Unity → Window → Package Manager → "+" → "Add package from git URL"，URL 同上。

**验证**：等 Unity 完成 import（可调 `mcp__UnityMCP__refresh_unity(compile="request", mode="standard", wait_for_ready=true)`），确认 `<project root>/Packages/com.promptugui.core/` 目录存在，含 `Runtime/`、`Editor/`、`package.json`。

## 步骤 2：复制 LLM 编辑 Skill 到用户项目

包内自带一份给 LLM 读的 XML 编写指南，路径：

```
<project root>/Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml/SKILL.md
```

把整个目录复制到用户项目的 `.claude/skills/` 下（**项目作用域**，跟仓库走，团队共享）：

**Unix / macOS / WSL：**
```bash
mkdir -p .claude/skills
cp -r Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml .claude/skills/
```

**Windows PowerShell：**
```powershell
New-Item -ItemType Directory -Force -Path .claude/skills | Out-Null
Copy-Item -Recurse -Force `
  Packages/com.promptugui.core/.claude/skills/authoring-promptugui-xml `
  .claude/skills/
```

如目标已存在，覆盖即可（**幂等**——包升级时 skill 也要跟着升级，重新跑这一步）。

> 备选：如果用户希望该 skill 跨所有项目可用，复制到 `~/.claude/skills/` 而不是项目内 `.claude/skills/`。默认推荐项目作用域。

**验证**：用 Read 工具读 `.claude/skills/authoring-promptugui-xml/SKILL.md` 第 1 行，应是 `---`（YAML frontmatter 起始）。

## 步骤 3：注入项目级 CLAUDE.md 约定

使用 PromptUGUI 的项目里，所有面向玩家的文本都应走 `Tr(...)` 包裹（i18n 约定，由项目自己接入翻译表，包本身不强制）。这个约定要让**所有 AI 会话默认知道**——写到项目根的 `CLAUDE.md`。

**操作**：
- 如果 `<project root>/CLAUDE.md` 不存在 → 创建，写入下面整段
- 如果存在 → 先 grep `Tr() 包裹约定`，已存在跳过此步（**幂等**）；不存在则**追加**到文件末尾，**不要覆盖**既有内容

要追加（或写入）的内容：

```markdown
## i18n: Tr() 包裹约定

项目里凡是会出现在 UI 上、玩家能读到的C#代码中的字符串，都用 `Tr(...)` 包裹(`UI` namespace下)。

**要包裹**：
- C# 给 UI 控件赋值的字符串：`label.Text = Tr("Start Game")`
- 错误提示弹窗、tooltip 文案

**不要包裹**：
- `.ui.xml` 里无须包裹
- `Debug.Log`、异常消息、内部日志
- 文件路径、URL、asset 键、format specifier、JSON / SQL 片段
- `nameof(...)`、反射 identifier
- 单字符 / 纯标点 / 纯数字字符串

**判断标准**：玩家能在屏幕上看到 → 包；工程内部用 → 不包。
```

**验证**：再读 `CLAUDE.md`，确认含 `Tr() 包裹约定` 字样且原有内容未丢失。

## 步骤 4：整体验收

依次确认：

1. ✓ `Packages/com.promptugui.core/Runtime/` 存在
2. ✓ `.claude/skills/authoring-promptugui-xml/SKILL.md` 存在，frontmatter 完整
3. ✓ `CLAUDE.md` 含 Tr() 包裹约定章节，原内容保留
4. ✓ （可选）`mcp__UnityMCP__refresh_unity(compile="request", mode="standard")` 后 `mcp__UnityMCP__read_console(action="get", types=["error"])` 无编译错误

全部通过即安装完成。下次会话 Claude Code 会自动加载 `CLAUDE.md`；用户编辑 `.ui.xml` 时 `authoring-promptugui-xml` skill 按需触发。

## 升级 / 卸载

**升级包**（`git pull` 等价于 manifest 里 commit 推进）：重跑**步骤 2**——把包内最新 skill 覆盖到 `.claude/skills/`。CLAUDE.md 章节通常无需变动，除非 release notes 提到约定变更。

**卸载**：
1. 从 `manifest.json` 删除 `com.promptugui.core`
2. 删除 `.claude/skills/authoring-promptugui-xml/`
3. 从 `CLAUDE.md` 删除 `## i18n: Tr() 包裹约定` 章节（如果项目里别的库也共用同名约定，保留）
