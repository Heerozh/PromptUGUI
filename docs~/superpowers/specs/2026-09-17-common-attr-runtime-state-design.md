# `hidden` / `interactable` 的运行期接管 + `<Icon name>` 运行期独占（RTS）

> 状态：**已对齐**（2026-09-17 作者按 §11 推荐项拍板，全部取第一列；实现分支 `feat/common-attr-runtime-state`，bug 修正跳过 plan 直接实现）。
> 相关：`2026-09-17-pages-design.md` §11（本文是那里另案的那条）；`2026-08-31-collapsible-design.md` §4.7 与
> `2026-08-29-tabmenu-design.md`（`expanded` / `isOn` 的 `RuntimeStateAttr` 契约——本文把同一契约推广到两个
> 通用属性和 `<Icon name>`）；master spec §5.1（通用属性表）/ §8.3（基础值与复位边界，本文修一句）；
> `reference/controls-tabs.md`；`Samples~/CommonControls`（主题 `<Style>` 用 `hidden` 切皮层——本文必须保住）。
> 动机来源：宿主 ssw_re_client 里所有「C# 管显隐 / 可用态的节点 XML 不写 hidden、初态在 Section 构造时设、
> 尺寸变化后延两帧重设」的 workaround（Planet / SelectionDock / MessageBox icon）。

## 1. 问题

`ControlAttributeApplier` 已经有一套「声明值 = 初态；运行期改过不打回；没动过时 Variant 照常覆盖」的机制
（`ControlRegistry.Entry.RuntimeStateAttr` + `PeekRuntimeState` + `_lastAppliedRuntimeState`），`isOn` / `value` /
`current` / `expanded` / `selected` 都走它。但三个最常被代码改的属性没有：

1. **`interactable` 每次 ReSolve 无条件重放。** `ControlAttributeApplier.cs:151` 把未声明当 `true`
   （`var interactable = interactableStr != "false"`），`Control.ApplyCommon` 末尾无条件 `Interactable = interactable`
   （`Control.cs:470`）。**C# 禁用的按钮在下一次 resize / Variant / Theme 后被重新启用，写不写 XML 都一样。**
2. **`hidden` 声明即重放。** `if (hidden.HasValue) Hidden = hidden.Value;`（`Control.cs:469`）。声明了
   `hidden="true"` 的占位 / 徽章被代码露出后，ReSolve 又藏回去；宿主的对策只能是**不写**——初态由 C# 在
   Section 构造时设，预览（只加载 XML）就看不到真实初态。
3. **`<Icon name>` 是普通 `[UIAttr]`。** 代码换的图标（库自己的 `MessageBoxRequest.Bind` 就这么干：`iconCtl.Name = Icon`）
   横竖屏一翻回到 XML 默认（2026-08-31 UIPreview 实测：`icon:"Solar96Bold:Medicine/Test Tube"` 竖屏变回 Info Circle）。

宿主为此攒下的规矩：「由 C# 决定显隐的节点 XML 里不写 hidden」「双管的要在尺寸变化后延两帧重设」
「验证时一定要切一次横竖屏再看」。`<Pages>` 解决了「N 选 1」那一类；这三条是**单个节点**的，`<Pages>` 管不到。

## 2. 否决的方案

**A. 粘性标志：公共 setter 一被代码调用就标记「运行期接管」。否决。** `Tab.bind` 直接 `GameObject.SetActive`、
`Pages` 走 `Hidden`、`<Show>` / Add 块各写各的，标志只能覆盖经过 setter 的那条路；applier 自己也经 setter 写，
还得加 `IsApplying` 门。值比对对任何写法一视同仁，而且是库里**已有**的那一套（§1），一种解释讲完。

**B. `hidden` 未声明时缺省写 `false`（与 `interactable` 对称，让 `hidden.portrait=` 不配基础值也能自愈）。否决。**
`<Show>` / `<Animation reveal>` / `TabMenu` 弹窗这类控件在 apply 钩子里改自己的 activeSelf，基线会在它们
自藏**之后**被抓成「藏」，下一次 ReSolve 缺省 `false` 就把它们露出来。「声明了才写」是今天的边界，保持；
`PUI-VARIANT-NO-BASE` 对 `hidden` 的例外原样不动。

**C. 把 `hidden` / `interactable` 加进主题 `<Style>` 的禁用名单（同 `text` / `isOn`）。否决。**
`Samples~/CommonControls` 用 `<Style name="skin-wood" hidden="false"/>` / `<Style name="skin-glass" hidden="true"/>`
按主题切皮层——「hidden 是公共属性，`<Style>` 能设」是写在样例里的承诺。没被代码碰过的节点照常跟主题；
碰过的不再跟——写进文档（§4.4）。

**D. 逐控件加 `[UIAttr(RuntimeOwned = true)]` 之类的通用标记。不做。** 每个控件一个 `RuntimeStateAttr` 槽
今天够用；通用属性走 `ApplyCommon`，本来就不在 `Meta` 循环里，加标记也落不到它们身上。等第二个「一个控件
两个运行期属性」的需求出现再说。

## 3. 方案总览

三处改动，全部沿用 §1 的机制：

| 属性 | 解算 | 锁（`!initial`） | 基线 |
|---|---|---|---|
| `hidden` | 声明了才有值（不变） | `_lastAppliedHidden.HasValue && Hidden != _lastAppliedHidden` → 本 pass 不写 | 真正写了才更新：`_lastAppliedHidden = Hidden` |
| `interactable` | 声明值 ?? `true`（不变，保住自愈） | `_lastAppliedInteractable.HasValue && PeekInteractable != _lastAppliedInteractable` → 不写 | 真正写了才更新：`= PeekInteractable` |
| `<Icon name>` | 控件私有属性（不变） | 注册 `runtimeStateAttr: "name"`，`PeekRuntimeState() => _name` | 现有 `_lastAppliedRuntimeState` 路径 |

```xml
<!-- 之后可以这样写：初态在 XML、预览所见即所得、代码改过 ReSolve 不打回 -->
<Text id="noOptions" hidden="true">当前没有可建造的建筑</Text>
<Btn id="applyJobs" interactable="false">应用</Btn>
<Icon id="icon" name="Solar96Bold:Essentional, UI/Info Circle"/>
```

```csharp
noOptions.Hidden = false;      // 从此归代码，横竖屏 / 主题 / resize 都不再藏回去
applyJobs.Interactable = true;
icon.Name = "Solar96Bold:Medicine/Test Tube";
```

## 4. 语义细节

### 4.1 锁的判定与基线（与 `RuntimeStateAttr` 逐字相同）

- 初次 apply（`initial == true`）永远不锁：声明值就是初态。
- 之后每个 pass（ReSolve：resize / Variant / Theme / locale；动态子树重放同路），对每个属性：
  **当前值 ≠ 上次真正写下的值 → 运行期接管 → 本 pass 跳过**；相等 → 照常解算并写入。
- 基线只在**真正写了**的那个 pass 末尾更新（`ControlAttributeApplier.cs:170-174` 那条注释的理由：锁住的
  属性保持旧基线，锁才能持续）。未声明的 `hidden` 从不写、从不更新基线、也永远没有可锁的东西。
- 基线存回读值（`bool?`），不是 XML 字面量。

### 4.2 与库内其它显隐写法的关系

| 写法 | 今天 | 之后 |
|---|---|---|
| `Tab.bind` 的页声明了 `hidden="false"`（少见但合法） | ReSolve 先把它露出、再看 TabBar 的 OnAfterApply 是否来得及藏——`_nodeMap` 无序，看运气 | 页被 Tab 藏起 = 当前 ≠ 基线 → 锁住，稳定 |
| `<Pages>` 的页 | `PUI-PAGES-CHILD-HIDDEN` 禁写 hidden | 不变：容器拥有 activeSelf，规则保留（§5） |
| `Screen.Instantiate` 实例根 `Hidden = true` 做池 | 未声明 → 不碰 | 不变 |
| `BindItems` 行里模板参数 `hidden="{{builtHidden}}"` + bind 回调改 `Hidden` | ReSolve 打回模板默认 → 宿主延两帧整体重绑 | bind 写过即锁，不再打回 |
| Add 块（inactive 时 `SetActive(false)` 根） | ReSolve 跳过 inactive 块的节点 | 不变 |
| `<Show>` / `<Animation reveal>` 自己切 activeSelf | 节点不声明 hidden → 不碰 | 不变（否决 B 的原因） |

### 4.3 `interactable` 的两条路

`Btn` / `Tab` / `Toggle` / `TabMenu` 的 `Interactable` override 都先 `base.Interactable = value`（CanvasGroup 是
真相源），`PeekInteractable`（不懒建 CanvasGroup）读的就是它，锁的判定对四者一致。它们的 `OnAfterApply`
把 CanvasGroup 状态桥到 `Selectable.interactable`——读状态不走 setter，锁住后照样桥出正确的值。

未声明时仍每 pass 写 `true`：这是 `interactable.portrait="false"` 不配基础值也能自愈的来源
（`VariantBaseRules.SelfHeals` 里 `interactable` 在自愈集合里，不动）。代价是每个控件都有 CanvasGroup——
今天就如此，不在本文改。

### 4.4 主题 `<Style>` 与 Variant

- 没被代码碰过的节点：主题 / Variant 切换照常重应用（皮层切换、`hidden.portrait=`、`interactable.mobile=`）。
- 被代码碰过的节点：不再跟主题 / Variant，直到 Screen 重开（同 `isOn`）。文档写明；样例的皮层节点没人碰，不受影响。
- `PUI-VARIANT-NO-BASE`：`hidden` 仍需基础值（不自愈），`interactable` 仍自愈——规则与测试都不改，只改注释。

### 4.5 已知边界（与 `isOn` 共有，非目标）

锁靠「当前 ≠ 基线」。极端序列：锁住期间 Variant 把声明值换成另一个（基线没更新），代码又把值改回**恰好等于**
旧基线 → 下一次 ReSolve 判「没动过」→ 重应用当前变体的声明值。`isOn` / `value` 今天同样如此；修它要粘性标志
（否决 A），不值。

### 4.6 `<Icon name>`

- `Icon` 记住 `_name`（setter 里存原始字符串，解析失败也存——锁比对的是「代码写了什么」）；
  `PeekRuntimeState() => _name`；`BuiltinPrimitives`：`reg.Register<Icon>("Icon", null, runtimeStateAttr: "name")`。
- locale 变体（`name.zh-Hans=`）：没动过照常跟 locale；动过以代码为准。`MessageBox` 的 icon 从此横竖屏不回退。
- `UIDocumentParser.ThemeStyleForbiddenAttrs` 顺手补 `selected`（`<Pages>` 那次漏了；`name` 撞 `<Style name=>`
  本来就写不进样式包，不用列）。

## 5. lint

- **不加新规则。** `PUI-PAGES-CHILD-HIDDEN` 保留（容器所有权，不是运行时危险了但仍是矛盾声明）。
- `VariantBaseRules` 只改注释；`ThemeStyleForbiddenAttrs` 加 `selected`（parse error，`UIDocumentParserTests` 加一条）。

## 6. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Controls/Control.cs` | `internal bool? _lastAppliedHidden, _lastAppliedInteractable;`；`ApplyCommon(..., bool? hidden, bool? interactable, ...)`：两者都 `HasValue` 才写 |
| `Runtime/Application/ControlAttributeApplier.cs` | 解算后、`ApplyCommon` 前判锁（把锁住的置 null）；`OnAfterApply` 后按「真正写了」更新两条基线 |
| `Runtime/Controls/Icon.cs` | `_name` + `PeekRuntimeState` |
| `Runtime/Application/BuiltinPrimitives.cs` | `Icon` 注册 `runtimeStateAttr: "name"` |
| `Runtime/Core/Parser/UIDocumentParser.cs` | `ThemeStyleForbiddenAttrs` / `ForbiddenAttrReason` 加 `selected` |
| `Runtime/Core/Lint/VariantBaseRules.cs` | 注释：hidden 例外的理由多一句「且两者运行期接管」 |
| `Runtime/Application/Screen.cs` | ReSolve 里关于 inactive Add 块与 `hidden="false"` 的注释同步 |

**Core 纯 C# 约束**不受影响（改动都在 `Application` / `Controls` / 一处 parser 名单）。

## 7. 测试（Red first）

`Tests/EditMode/Application/CommonAttrRuntimeStateTests.cs`：

1. `Undeclared_interactable_disabled_by_code_survives_resolve`（§1 第 1 条，今天必红）
2. `Declared_interactable_false_enabled_by_code_survives_resolve`
3. `Declared_hidden_true_shown_by_code_survives_resolve`（§1 第 2 条）
4. `Declared_hidden_false_hidden_by_code_survives_resolve`
5. `Initial_apply_never_locks`（重开 Screen 回声明值）
6. `Untouched_hidden_variant_override_still_applies`（`hidden="false" hidden.portrait="true"`）
7. `Untouched_interactable_variant_override_self_heals_without_base`（钉住 §4.3：`interactable.portrait="false"` 单独写）
8. `Touched_then_variant_flip_keeps_code_value`（锁优先于变体）
9. `Theme_style_hidden_switch_still_flips_untouched_layers`（样例的 skin-wood / skin-glass 模式）
10. `Touched_node_stops_following_theme_hidden`（§4.4 第二条，文档化的行为）
11. `Tab_bound_page_declaring_hidden_false_stays_hidden_after_resolve`（§4.2 第一行）
12. `BindItems_row_hidden_written_by_bind_survives_resolve`（模板 `hidden="{{p}}"` + bind 改 `Hidden`）
13. `Instantiate_root_hidden_pool_still_survives_resolve`（回归）
14. `Btn_code_disable_reaches_button_after_resolve`（override 桥接）
15. `Lock_baseline_is_not_refreshed_while_locked`（锁住两次 ReSolve 仍在；反向钉 §4.1 第三条）

`Tests/EditMode/Controls/IconTests`（或新文件）：`Name_set_by_code_survives_resolve`、`Untouched_name_locale_variant_still_applies`、
`MessageBox_icon_survives_orientation_flip`（走 `MessageBox.Open(icon:)` + `Variants.Set("portrait")`）。
`UIDocumentParserTests`：主题 `<Style selected=…>` 是 parse error。`VariantBaseRulesTests` 不改、必须仍绿。

## 8. SKILL / 文档更新（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：通用属性表 `hidden` / `interactable` 两行改成「initial state; runtime-owned
  once code writes it (same contract as `isOn`); an untouched node still follows `.variant` / theme」；
  主题 `<Style>` 禁用表下加一行说明 `hidden` / `interactable` 允许但「碰过的节点不跟」；`<Icon>` 小节 `name` 标 runtime-owned；
  `<Pages>` 小节「页不写 hidden」措辞改成所有权理由。
- `scripting-promptugui-csharp/SKILL.md`：新小节 *Runtime-owned common attributes*（`Hidden` / `Interactable` /
  `Icon.Name`：写过即归代码；不再需要「XML 不写 hidden、构造时设初态、resize 后重设」）；TabBar / ScrollList 段落里
  「ReSolve 会把 BindItems 出来的卡片属性打回模板默认」的例子改掉；cheatsheet 补两行。
- master spec §5.1 两行 + §8.3 复位边界那段（`hidden` 仍不自愈，但两者运行期接管）；`BEST_PRACTICES*.md` 一句。

## 9. 宿主迁移（另案，ssw_re_client）

- Planet.ui.xml：`noOptions` / `noJobs` / `avatar` / `dLock` / `dAction` 等按真实初态写回 `hidden=` / `interactable=`，
  `PlanetDockSection` 构造时的初态设置删掉；`RerenderAfterResolveAsync` 延两帧重绑看还剩什么理由（`hidden` /
  `name` / `text` 都运行期独占后大概率可删）。SelectionDock 同理。
- MessageBox icon 不用宿主动。
- memory 三条（`promptugui-resolve-replays-declared-hidden-only` / `promptugui-icon-name-reverts-on-resolve` /
  「延两帧」）改写成新契约。

## 10. 非目标

- 粘性标志 / §4.5 的边界。
- `hidden` 缺省 `false`（否决 B）。
- 通用「任意属性运行期接管」标记（否决 D）。
- 每控件 CanvasGroup 的懒建优化。
- 其它控件私有属性（`color` / `sprite` …）的接管——需求出现再按 `RuntimeStateAttr` 逐个登记。

## 11. 已定的决策 / 待作者确认

| # | 决策 | 备选 |
|---|---|---|
| D1 | 机制 = 现有值比对 + 「真正写了才更新基线」 | 粘性标志（§2-A） |
| D2 | `hidden` 仍「声明了才写」；`PUI-VARIANT-NO-BASE` 不变 | 缺省 false（§2-B） |
| D3 | `interactable` 仍「未声明当 true」，保自愈，加锁 | 也改成声明了才写（丢自愈，lint 要改） |
| D4 | 主题 `<Style>` 继续允许 `hidden` / `interactable`；碰过的节点不跟主题 | 禁用（§2-C，砸样例） |
| D5 | `<Icon name>` 一并登记为 `runtimeStateAttr`（MessageBox 是库自己的 bug） | 只做两个通用属性，Icon 另案 |
| D6 | `ThemeStyleForbiddenAttrs` 顺手补 `selected` | 单独 PR |
| D7 | `PUI-PAGES-CHILD-HIDDEN` 保留 | 放宽 |

## 12. 里程碑

- **M1** `hidden` / `interactable`：`Control` 基线 + `ApplyCommon` 签名 + applier 判锁；测试 1–15。
- **M2** `<Icon name>` + `selected` 进主题禁用名单；测试。
- **M3** 文档（§8）+ spec 实施记录；整套 EditMode + EditorOnly 全绿。
- 宿主（另案）：§9。

## 13. 实施记录

分支 `feat/common-attr-runtime-state`，跳过 plan：spec → Red 测试（14 条先红：两条通用属性、Icon 三条、MessageBox icon、
主题 `<Style selected>`）→ M1 `hidden` / `interactable`（`55cb0c6`）→ M2 `<Icon name>` + `selected`（`25c90e2`）→ M3 文档。
整套 `PromptUGUI.Tests.EditMode` 4340/4340、`EditorOnly` 全绿，`dotnet format` 干净。

### 13.1 与设计的偏差

- 没有。实现逐条落在 §3 的表：锁在 `ControlAttributeApplier.ApplyCore` 解算后、`ApplyCommon` 前判（锁住的置 null），
  基线在 `OnAfterApply` 之后按「本 pass 真正写了」更新；`ApplyCommon` 的 `interactable` 参数改成 `bool?`。
- 测试里多钉了一条 `Lock_baseline_is_not_refreshed_while_locked`（两次 ReSolve 后锁仍在）——它就是 §4.1 第三条
  「基线只在真正写了的 pass 更新」的反向证据；漏掉这一条的实现会在第二次 ReSolve 把值打回去。
- `Initial_apply_never_locks` 要经 `UI.Close(name)` 重开：`Screen.Close()` 靠根 GO 的哨兵 `OnDestroy` 反注销 `UI._open`，EditMode 里不跑，`UI.Open` 会把关掉的实例还回来。

### 13.2 未做 / 另案

- §9 宿主迁移（Planet / SelectionDock 把初态写回 XML、删构造时的初态与「延两帧」）；三条 memory 改写。
- §10 全部非目标。
