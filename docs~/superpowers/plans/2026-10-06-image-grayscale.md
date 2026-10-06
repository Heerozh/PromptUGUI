# `<Image>` / `<Icon>` grayscale Implementation Plan

> **For agentic workers:** Steps use checkbox (`- [x]`) syntax for tracking. Red first: every task writes its tests, refreshes
> Unity and sees them fail for the right reason before implementing.

## Context

灰度今天只有禁用态一个入口；`FxImage` 早就带着去色开关（`_Desaturate`），只是作者拨不到。设计见 spec
`docs~/superpowers/specs/2026-10-06-image-grayscale-design.md`（下文 §x 均指它，§4 全按推荐已定）。分支 `feat/image-grayscale`。

**Goal:** `<Image grayscale="true">` / `<Icon grayscale="true">` 把整张图（本体 + 光晕）按亮度去色；与禁用灰互相独立；C#
`Grayscale` 当帧生效；写错标签 / 写错值由 lint 报出。顺带修三处已发现的缺陷（§3.5 `blur` 误报、§3.7 两处文档错误）。

**Architecture:** `FxImage` 把 `_grayed` 拆成作者开关 `_grayscale` 与禁用开关 `_disabledGray`，两者在 `BuildParams` 里或起来进
`FxParams.Desaturate`，光照只看禁用开关。控件层是两个 `[UIAttr] bool` 属性，经 `ImageFxApplier` 落地。shader 与材质缓存不动。

**Tech Stack:** C# 9；Core/Lint 保持纯 C#（UIXmlLint CLI 编译集）。

## Task 0：文档先行

- [x] spec 落盘
- [x] 本计划落盘
- [x] 提交 `docs(spec): grayscale attribute on <Image> / <Icon>`

## Task 1：`FxImage` 的两个开关（spec §3.2）

**Files:** `Runtime/Controls/Internal/FxImage.cs`；`Tests/EditMode/Controls/FxImageTests.cs`

- [x] 红：`FxImage.Grayscale` 单测 —— 设 true 拿到 fx 材质且 `Desaturate`；设 false 回到无材质；禁用开关 true → false 后作者灰仍在；
  作者灰保留 `intensity`、禁用灰熄灯（先加空壳属性让程序集能编译，3 条均因"作者开关不生效"失败）
- [x] 实现：拆开关、`HasMaterialFx`、`BuildParams`
- [x] 刷新 Unity、console 无错、`FxImageTests` + `DisabledGrayscaleTests` + `ImageFxRenderTests` + `FxMaterialCacheTests` 全绿（77/77）
- [x] 提交 `feat(fx): FxImage keeps an authored grayscale apart from the disabled grey`

## Task 2：`grayscale` 属性（spec §3.1 / §3.3 / §3.6）

**Files:** `Runtime/Controls/Image.cs`、`Runtime/Controls/Icon.cs`、`Runtime/Controls/Internal/ImageFxApplier.cs`、
`Editor/XsdGenerator.cs`；测试：`FxImageTests`、`DisabledGrayscaleTests`、`ImageFxRenderTests`、`XsdGeneratorTests`

- [x] 红：spec §5 的 1–11、16（控件先放不带 `[UIAttr]` 的空壳属性；11 + 1 条均因属性未接入失败，渲染测试的前置断言已绿）
- [x] 实现：`ImageFxApplier.SetGrayscale` / `GetGrayscale`；两个控件的 `[UIAttr] bool Grayscale`（apply 之外立即 Flush）；XSD 两行
- [x] 刷新、console 无错、EditMode 相关类 88/88、`XsdGeneratorTests` 61/61；渲染导出图目视确认（红 → 灰，光晕同灰）
- [x] 提交 `feat(image,icon): grayscale attribute`

## Task 3：`PUI-FX-TAG` 跳过模板调用（spec §3.5 顺带修正）

**Files:** `Runtime/Core/Lint/ImageFxRules.cs`；`Tests/EditMode/Lint/ImageFxRulesTests.cs`

- [ ] 红：模板参数叫 `blur` 的调用点不该报 `PUI-FX-TAG`（修正前误报）
- [ ] 实现：`CheckTag` 加 `BuiltinTags.IsBuiltin` 门
- [ ] 提交 `fix(lint): PUI-FX-TAG no longer flags a template parameter named blur`

## Task 4：lint —— `grayscale` 的标签与取值（spec §3.5）

**Files:** `Runtime/Core/Lint/ImageFxRules.cs`；`Tests/EditMode/Lint/ImageFxRulesTests.cs`

- [ ] 红：spec §5 的 12–15
- [ ] 实现：`CheckTag` 认 `grayscale`（按标签给提示）；`CheckImage` 的 CLI 分支加 `PUI-FX-VALUE`
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/` 无新增发现；对一份写错的临时文件确认报出
- [ ] 提交 `feat(lint): grayscale on a tag that drops it, and a value that is not a bool`

## Task 5：skills（spec §3.7）

**Files:** `.claude/skills/authoring-promptugui-xml/SKILL.md`、`.../reference/states.md`、
`.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] XML skill：Image / Icon 表各一行、`#### Grayscale` 小节、Tint 一节的提醒；states.md 的独立性说明
- [ ] C# skill：`Image.Grayscale` 小节
- [ ] 提交 `docs(skills): grayscale on <Image> / <Icon>`

## Task 6：已发现的文档错误（spec §3.7）

- [ ] XML skill tint 示例 `src=` → `sprite=`；C# skill `coin.Glow = v` 改成能编译的写法
- [ ] 提交 `docs(skills): <Image> takes sprite=, not src=; Icon.Glow is a string`

## Task 7：收尾

- [ ] 全量回归：EditMode（`PromptUGUI.Tests.EditMode`）、`PromptUGUI.Tests.EditorOnly`、PlayMode
- [ ] `.lint`：`dotnet format whitespace` / `style` / `analyzers` + `--verify-no-changes --severity warn`
- [ ] 计划勾选、提交
