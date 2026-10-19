# `@id` 认路径；`click@` 可以指向模板实例

> 状态：**已实施**（实施记录见 §9）。**已对齐**（2026-10-19，与作者对齐，按推荐定：路径给所有 `@id`；「实例里唯一的 Btn」回退只给 `click`；回退时锚点是 id 指名的控件）。
> 来源：搜索浮层的接入反馈：图标行里的放大镜是一个模板实例，最外层是 `<Frame>`（里面一颗 `<Btn>`，外加角标），`<Popover on="click@实例">` 直接抛异常，只能退回 C# `Toggle(anchor)`。读源码做的评估；复现由 §6 的 Red 测试给出。
> 相关：
> `2026-08-31-hug-reveal-flip-checked-design.md`（§4.3 `@id` 的词法解析）、
> `2026-10-19-popover-design.md`（§4.3 `on="click@id"`、§5.6 推迟解析）。

## 1. 问题

- **`click@x` 要求 x 本身是 `<Btn>`。** `<Popover on="click@x">`、`<Trigger on="click@x">` / `<Animation on="click@x">` 都是这样。x 是一个模板实例、最外层不是 `<Btn>` 时，抛 `InvalidOperationException`：
  - `TriggerSourceResolver.FindTriggerBtn`（Popover）：词法查找拿到实例根，不是 `<Btn>` 就抛；
  - `TriggerSourceResolver.FindBtn`（Trigger）：先在触发器自己的子树里按 id 找 `<Btn>`，找不到再词法查找；拿到的实例根同样不是 `<Btn>`。
- **`@id` 只认单段 id。** `click@x/btn` 解析不报格式错，但每一层作用域都查不到「x/btn」这个 id，报「找不到」。
  - 同一份文档里，C# 的 `Get("x/btn")`、`screen.Focus("x/btn")`、`<Add into="#x/btn">` 都认路径：首段找顶层 id，之后逐段进模板实例的 id 表。
- 模板最外层不放 `<Btn>` 很常见：角标、红点、冷却遮罩要和按钮叠在同一块里，最外层得是 `<Frame>`。

## 2. 否决的方案

**A. 要求模板把 `<Btn>` 放在最外层。否决。** 角标、红点不能做按钮的子物体，否则会跟着按钮吃按下态、算进它的点击区。

**B. 所有 `@id` 种类都做「实例里唯一的那个」回退。否决。**
- `state-*` / `checked` / `expand` / `ready` 的裸写法各有自己的就近规则（向上找祖先，或由近及远），再加一条「向下找唯一的」会让同一个 `@x` 在不同种类下指向不同方向。
- `click` 的裸写法本来就是「子树里唯一的 `<Btn>`」，回退与它同义，只给 `click`。其它种类用路径写清楚。

**C. 回退时锚点用找到的那颗 `<Btn>`。否决。**
- 作者写的是 x，面板就挂在 x 上。x 往往就是那一整块图标位（含角标），按钮可能比它小。
- x 与按钮一样大时两者没区别。要挂在按钮上，写路径 `click@x/btn`。

## 3. 方案总览

1. **路径**：所有 `@id` 都认 `a/b/c`（§4.1），规则与 `Get(idPath)` / `<Add into="#…">` 相同。
2. **`click@x` 的回退**：x 不是 `<Btn>`，就用 x 子树里唯一的 `<Btn>`（§4.2）。
3. **浮层的锚点**：回退时是 x，点击来源是 x 里那颗 `<Btn>`（§4.3）。

## 4. 作者面

### 4.1 路径

```xml
<HStack id="icons" height="44" spacing="4" childAlign="middle-right">
  <IconSlot id="bag" icon="ui:bag"/>
  <IconSlot id="search" icon="ui:search"/>     <!-- 模板：<Frame> 里一颗 <Btn id="btn"> + 一个角标 -->
</HStack>

<Popover id="searchPop" on="click@search/btn" placement="over-end" width="320">…</Popover>
<Show on="checked@settings/sound">…</Show>
```

- **适用范围**：每一种 `@id`：
  - `<Trigger>` / `<Animation>` / `<Show>` 的 `on=`，`<Animation>` 的 `reverse-on=`，所有种类（`click` / `hover-*` / `press` / `state-*` / `expand` / `collapse` / `checked` / `unchecked` / `ready` / `cooling` / `lift` / `drop`）；
  - `<Popover>` 的 `on="click@…"`。
- **解析**：
  - 首段按今天的词法规则找：先看自己的子树，再逐层看外面的作用域（模板实例的 id 表；外层元素自己的 id 也算），最后看 Screen 顶层；
  - 之后每一段，在上一段那个控件的 id 表（`ScopedIds`，即模板实例里的 id）里找。
- **与 C# 的关系**：路径的写法与 `Get("a/b")` 相同。区别只在首段：`@` 是词法查找，近的作用域优先；`Get` 只看 Screen 顶层。
- **报错**：首段找不到，报今天的「三处都没找到」。后面某一段找不到，报「`b` 不在 `a` 里」，并列出 `a` 的 id 表里有哪些 id（最多 8 个）。
- `<Trigger on="click@a/b">` 跳过今天「先在自己子树里按 id 找 `<Btn>`」的那一步：路径不是单个 id，直接走路径解析。

### 4.2 `click@x` 的回退

- **x 是 `<Btn>`**：不变。
- **x 不是 `<Btn>`**：在 x 的子树里找 `<Btn>`。`<Btn>` 是叶子，不进它的子树，与裸 `on="click"` 相同。
  - **正好一个**：用它；
  - **一个都没有**：抛错，「x 是 `<Frame>`，里面也没有 `<Btn>`」；
  - **多于一个**：抛错，列出它们的 id（没 id 的写 `<Btn>`），提示用 `click@x/<id>`。
- 用于 `<Trigger on="click@x">`、`<Animation on="click@x">`、`<Popover on="click@x">`。`hover-*` / `press` / `state-*` 等不回退（§2-B）。
- 「在浮层自己里面」的检查对 x 和找到的 `<Btn>` 都做。

### 4.3 `<Popover on="click@x">` 的锚点

- **锚点是 x**（id 指名的控件），点击来源是 x 里的那颗 `<Btn>`。x 本身是 `<Btn>` 时，两者是同一个，同今天。
- **打开 / 关闭**：
  - 按那颗 `<Btn>`：关着就以 x 为锚点打开；开着且锚点是 x 就关（原因 `Trigger`）；
  - C# `Open()` / `Toggle()`（不带参数）以 x 为锚点。
- **外部按下**：x 算「里面」，包括它里面的按钮和角标。
- **锚点失效**：看 x，同今天的规则。x 没有 `Selectable` 时，「可交互」看它的 `interactable`。
- **关闭时还选中项**：x 可选就还给 x；否则还给点击来源那颗 `<Btn>`；再不行，还给打开时的选中项。
- **`Anchor` 属性**：返回 x。

## 5. 内核改动

- `TriggerSourceResolver.ResolveId`：
  - 按 `/` 切段：首段走今天的词法查找，之后逐段查 `ScopedIds`；
  - 找不到时的报错见 §4.1；
  - 单段 id 的行为不变。
- `TriggerSourceResolver.FindBtn`（Trigger）：
  - 带 `/` 的 id 跳过子树按 id 找的那一步；
  - 词法结果不是 `<Btn>` 时，走「唯一 `<Btn>`」回退。
- `TriggerSourceResolver.FindTriggerBtn` → `FindClickSource`（Popover）：返回 `(IControl named, Btn btn)`；回退与检查同 §4.2。
- `Popover`：
  - 记下 `_trigger`（点击来源 `<Btn>`）和 `_triggerAnchor`（id 指名的控件）；
  - `ToggleFromTrigger` / `Open()` / `Toggle()` 用 `_triggerAnchor`；
  - `RestoreTarget` 在锚点不可选、且锚点就是 `_triggerAnchor` 时，退到 `_trigger`。
- lint：`@id` 本来就交给运行时（`StateTriggerRules` 不静态解析 `@id`），`PopoverRules.IsValidOn` / `TriggerSpec.Parse` 只拦 `@`，路径原样放行，不用改。

## 6. 测试（Red 先行）

- **EditMode `TriggerIdPathTests`**：
  - `ResolveId("inst/btn")` 拿到实例里的控件；
  - 首段走词法：模板实例里的 `@a/b` 先找本实例；
  - 第二段找不到时，报错里有段名和那张 id 表；
  - `<Trigger on="click@inst/btn">` / `<Show on="checked@inst/t">` 跟着实例里的控件；
  - `<Popover on="click@inst/btn">` 由那颗按钮开合。
- **EditMode `ClickFallbackTests`**：
  - `<Trigger on="click@inst">`（最外层 `<Frame>`、一颗 `<Btn>`）由那颗按钮触发；
  - 0 颗、2 颗都报错，2 颗时列出 id、提示路径；
  - x 本身是 `<Btn>` 时不变；
  - `hover-enter@inst` 不回退，照旧报「不是指针事件源」。
- **EditMode `PopoverInstanceTriggerTests`**：
  - `on="click@inst"`：按里面的按钮打开，`Anchor` 是 inst；再按关闭，原因 `Trigger`；
  - `Open()` 以 inst 为锚点；
  - inst 不可选时，关闭后选中项回到那颗按钮；
  - 按钮在浮层里时报错。

## 7. SKILL / 文档更新（同 PR，英文）

- **XML skill**：
  - `reference/animations.md`：`@id` 解析一段加上路径和 `click` 回退，`on=` 表的 `click@<id>` 行同步；
  - `reference/controls-popover.md`：`on=` 的行与 *Opening* 一节（路径、回退、锚点是 x）；
  - 主 `SKILL.md` 讲 Template 与 id 作用域的地方，点明 `@a/b` 与 `Get("a/b")` 同形。
- **C# skill**：`Get(idPath)` 一节提一句：XML 的 `@a/b` 用同样的路径，首段走词法查找。

## 8. 决策

| # | 题目 | 定 | 理由 |
|---|---|---|---|
| IP-D1 | 路径范围 | 所有 `@id` | 一条规则；与 `Get` / `Add into` 同形 |
| IP-D2 | 首段 | 词法查找（不变） | 模板里的 `@a/b` 先找本实例 |
| IP-D3 | 回退范围 | 只给 `click` | 与裸 `on="click"` 同义；其它种类有各自的就近方向（§2-B） |
| IP-D4 | 回退时的锚点 | id 指名的控件 | 作者写的就是它（§2-C） |
| IP-D5 | 还选中项 | x 不可选就还给点击来源 | 「关了回到触发钮」对手柄用户仍成立 |

## 9. 实施记录

**先红后绿**：三个测试类先写，红在预期处。不用桩，用到的 API 都已存在。
- 路径五条：报「`slot/btn` 三处都没找到」（复现 §1 的第二条）；
- 回退四条：报「`slot` 是 `Frame`，不是 `<Btn>`」（复现 §1 的第一条）；
- 浮层五条：在 `SetUp` 的 `Open` 处就抛了同样的错。

**改线前就绿的**（守住不变的行为）：
- `A_Btn_named_directly_is_unchanged`；
- `Hover_does_not_fall_back`。

**与设计的偏差 / 补充**：
- 测试模板一开始叫 `Slot`。这是模板插槽的保留标签，在 Screen 里调用会报「只能在 `<Template>` 里」，改名为 `IconCell`。spec 里的示例 `IconSlot` 不受影响。
- `FindBtn` 不另外跳过「子树里按 id 找」：带 `/` 的 id 不会等于任何一个 `<Btn>` 的 id，那一步自然落空。
- 第二段起找不到时，报错列出那个实例 id 表里的 id（最多 8 个），不是实例时说明「只有模板实例有 id 表」。

**验证**：
- EditMode 5667 / EditorOnly 471 / PlayMode 313 全绿。
- `dotnet format --verify-no-changes` 干净。
- UIXmlLint 对 `Runtime/Resources/` 与 `Samples~/` 零问题。
