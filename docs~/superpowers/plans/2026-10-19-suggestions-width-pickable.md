# `<Suggestions>`：面板宽度、对齐，与不可选的行 — Implementation Plan

> **For agentic workers:** 按 Task 顺序执行，步骤用 checkbox（`- [x]`）跟踪。每个 Task Red 先行。
> 设计依据：`docs~/superpowers/specs/2026-10-19-suggestions-width-pickable-design.md`（下称 spec）。2026-10-19 与作者对齐（SW-D1…D4 按推荐）。

**Goal：** `popupWidth`；`placement` 的 `-start` / `-end`；绑定时 `Interactable = false` 的行显示但不可选。

**Architecture：**
- `PopupPlacer.Solve` 多一个 `alignEnd` 参数（起点右对齐，夹边规则不变），由 `PopupLayout.AlignEnd` 传进来；TabMenu 不传。
- `Suggestions.Rebuild` 绑定前复位行的可交互，绑定后抄到行壳的 `FocuslessPress.Interactable`。高亮、`HighlightedIndex`、`CommitChosen` 都问 `Pickable(i)`。
- lint 的 `placement` / 数值检查与运行时同一张表。

---

## Task 1：宽度与对齐（Red 先行）

- [x] Red：`PopupPlacerTests`（三条）、`SuggestionsWidthTests`、`SuggestionsRulesTests`（两条）。
- [x] 实现：`Solve(alignEnd)`、`PopupLayout.AlignEnd`、`PopupWidth`、`Placement` 解析、lint。

## Task 2：不可选的行（Red 先行）

- [x] Red：`SuggestionsUnpickableTests`。
- [x] 实现：复位与抄写、`MoveHighlight` 跳过、`HighlightedIndex` / `CommitChosen` 拒绝。
- [x] 跑 Suggestions / PopupPlacer / TabMenu 定位相关测试，绿。

## Task 3：文档与收尾

- [x] XML skill：`controls-suggestions.md`、`states.md`、主 `SKILL.md` 的 `<Suggestions>` 段。C# skill：Suggestions 小节与速查表。
- [x] `dotnet format --verify-no-changes`；UIXmlLint 跑 `Runtime/Resources/` 与 `Samples~/`。
- [x] 全量回归：EditMode / EditorOnly / PlayMode。
- [x] spec 补实施记录；提交、推送、开 PR。
