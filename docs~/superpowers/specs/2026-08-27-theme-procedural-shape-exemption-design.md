# 主题就该管皮肤：让 `PUI-THEME-STYLE-SHAPE` 认得程序化表面的自愈

**日期**：2026-08-27
**状态**：设计中，未实施。
**作用域**：两件事，同一个 PR。
1. 给 `ThemeStyleRules.CheckShape`（`PUI-THEME-STYLE-SHAPE`）加一条**程序化表面自愈豁免**，让「某个主题给控件开程序化形状、别的主题不开」成为合法的纯 `<Theme>` 写法；随后把 `Samples~/CommonControls` 里那 18 行 `attr.glass=` 绕道全部收回 `<Theme name="glass">`。
2. 顺手钉住一个相邻缺陷：`ProceduralSurface.Restore()` 会用**首次退休时的快照**盖掉本轮 pass 刚写进去的 sprite / alpha。与 1 正交（今天的 `.glass` 写法一样中招），但两者都发生在「主题切换 × 程序化表面」这条线上，一起改一起测。

**关联**：`<Style>` / `class=` 合并语义见 [procedural-style](2026-08-23-procedural-style-design.md) §3–§4；`<Theme>` 承载 style pack 与 §6.1 的残留约束见 [theme-driven-style](2026-08-26-theme-driven-style-design.md)；程序化表面的 per-pass reconcile 见 [procedural-surface](2026-08-26-procedural-surface-design.md) §8。本设计**不改** `<Style>` / `<Theme>` / `ProceduralSurface` 的任何语义，只改 lint 的一条判定，以及 `Restore()` 里两行赋值的条件。

---

## 1. 背景：示例里那 18 行 `.glass` 是绕道，不是设计

`CommonControls` 演示的是「同一棵 XML 树，farm 主题渲染成像素木框，glass 主题渲染成磨砂玻璃」。颜色、sprite、勾选框贴图全部走 `<Theme name="glass">` 里的 `<Style>` 覆盖 —— 干净的纯主题路径。唯独**形状**那一组属性走了另一条路，写在**全局基线**上：

```xml
<Style name="btn" sprite="PromptUGUI/Defaults/pugui#pugui_9slice_round"
       color="#E8D2A8" hoverColor="#F5E6C8" textColor="#4A3018" pressedOffset="0,-1"
       radius.glass="10" glass.glass="true"
       borderWidth.glass="1" borderColor.glass="white/0.55"/>
```

`btn` / `tab` / `input` / `toggle` / `slider` / `dropdown` / `list` / `progress` 八个包，18 行，全是这个形状。读起来像「主题和变体是两套并行机制」，实际上不是 —— 作者是被 lint 逼到这条路上的。推导链三步：

**① 对 `ProceduralControl` 来说，「写没写」才是开关，值是什么无所谓。** `Radius` / `Glass` / `BorderWidth` 这些 setter 一被调用就 `Surface.Declare(...)`，挂上 SDF 面并让 Image 退休（`ProceduralSurface.Retire()`：清 sprite + alpha 归零）。所以农场皮**必须一个 procedural 属性都不写** —— 写 `radius=""` 或 `radius="0"` 当基线，木头 9-slice 当场消失。

**② 但 `PUI-THEME-STYLE-SHAPE` 要求「同一样式名在所有主题下解析出同一套属性名」**，报错信息还专门建议「把 `radius` 加进全局 `<Style>` 当基线」—— 正好是 ① 禁止的那件事。

**③ 于是绕道**：把属性**名**留在全局基线上，用 `attr.glass=` 让**值**只在 `glass` 变体激活时落地。SHAPE 看到名字在，闭嘴；farm 下变体没命中 → applier 不调 setter → 表面自己关掉、木头回来。

代价不只是 18 行噪音。它给读者（和 LLM 作者）灌了一个错误的心智模型：「换皮肤要主题和变体配合」。真相是主题一个人就够。

## 2. 现状盘点（实测）

### 2.1 两条路给 applier 的信号完全一样

| | 变体路径 `radius.glass="10"` | 主题路径 `<Theme name="glass"><Style radius="10">` |
|---|---|---|
| farm 下 `node.Attributes` 里有 `radius` 吗 | 有键，`VariantResolver` 解出 `null` | **键都没有**（`StyleMerger.ReMerge` 按 `StyleAttrNames` 把上一套 pack 贡献的名字整批删掉，`StyleMerger.cs:115`） |
| applier 行为 | `ControlAttributeApplier.cs:72` `if (v == null) continue` | 同左（键不在 `allKeys` 里，压根不进循环） |
| setter 被调用吗 | 否 | 否 |
| `ProceduralSurface` | `BeginPass()` 清 `_declaredThisPass`（`ProceduralSurface.cs:74`）→ `Reconcile()` 关面板 → `Restore()` 还原 Image | 同左 |

主题路径**更干净**：变体路径留着一个解不出值的键，主题路径连键都不留。运行时早就支持纯 theme。

### 2.2 两种纯 theme 写法各自撞上什么

把 sample 改写成纯 theme 跑 `UIXmlLint`（实测，非推断）：

| 写法 | 结果 |
|---|---|
| 属性只写在 `<Theme name="glass">` 的 `<Style>` 里 | **8 条 `PUI-THEME-STYLE-SHAPE`**（btn / tab / input / toggle / slider / dropdown / list / progress），差集全是 `radius` `glass` `borderWidth` `borderColor` `fillRadius` `handleRadius` |
| 按 SHAPE 的报错建议补基线（`radius="" glass="false" borderWidth="" borderColor=""`） | 8 条 SHAPE 消失，换来 **10 条 `PUI-PROC-SPRITE-CONFLICT`** —— 而且**这条不是误报**，farm 下真会丢贴图 |
| 今天的 `attr.glass=` 绕道 | 0 issue |

第二行值得展开：`DeclaresProcedural` 判的是 `baseValue != null`，`radius=""` 是非 null，于是「声明了程序化」为真；而 `sprite=` 是真贴图 → 报冲突。这不是 lint 保守，是运行时真的会那样做（setter 被调用 = 挂面 = 退休 Image）。**SHAPE 规则给出的修法建议，在程序化属性上是错的。**

### 2.3 孪生规则已经开过口子

变体侧的同一个问题，`VariantBaseRules.cs:90` 早就豁免了：

```csharp
// A control whose procedural surface toggles WHOLESALE with the variant reverts on its own,
// so the base-less form is the correct way to write it — and the idiom a skin wants:
// `radius.glass="10"` shapes the control under one variant and leaves its sprite alone
var proceduralSelfHeals = ProceduralSurfaceRules.AppliesTo(n.Tag) && !DeclaresBaseProcedural(n);
```

`PUI-VARIANT-NO-BASE` 认这个自愈并放行；`PUI-THEME-STYLE-SHAPE`（`ThemeStyleRules.cs:72`）是它的孪生规则，缺这条豁免。两份 spec 同一天写（2026-08-26），theme-driven-style §6.1 落笔时 procedural-surface 的 per-pass reconcile 还不存在 —— **时序遗漏，不是刻意取舍**。本设计补上。

## 3. 设计：CheckShape 的程序化豁免（样式级）

### 3.1 豁免哪些属性名

只豁免**形状类**属性，即 `ProceduralAttrNames.NeedsPanel` 去掉 `weld`，再加上 `InnerLayerRadius`：

```
radius borderWidth borderColor glow glowColor
glass frost depth dispersion lightAngle lightIntensity saturation noise
fillRadius handleRadius frameRadius maskRadius
```

- **`weld` 排除**：Frame 专属，不跨进控件（procedural-surface spec §13.2），`DeclaresProcedural` 里也是这么跳过的。
- **`color` 排除**（尽管 `VariantBaseRules.IsProcedural` 把它算进去）。`color` 在 Image-backed 控件上是普通 tint，主题少写一个 `color` 是真残留；而且它的「回退 alpha」路径正是 §5 要修的那个缺陷 —— 不能一边豁免一边指望它自愈。示例的两套主题都写 `color`，不需要这条豁免。
- **`sprite` 排除**：不在程序化家族里，一个主题写、另一个不写就是真残留。

### 3.2 判定条件：全有或全无

设 `P` = §3.1 的名字集合，`N_T` = 样式 `S` 在主题 `T` 下解析出的属性名集合。

> **豁免条件**：`{N_T ∩ P | T ∈ 所有主题}` 去重后至多两个元素，且其中一个是 `∅`。
> 满足时，比较 `N_T` 时把 `P` 里的名字整体剔除；不满足时按今天的逻辑原样报。

白话：**程序化那组属性要么整套在、要么整套不在**，才叫「wholesale 切换」，表面才会自己关掉并还原 Image。只要有哪个主题拿了半套（比如 A 写 `{radius, glass}`、B 只写 `{radius}`），B 缺的 `glass` 就真的会卡住 —— 照报。

这个形式是**顺序无关**的，刻意不写成「跟参照主题两两比」。今天的 `CheckShape` 拿排序后的第一个主题当参照、发现不一致就 `break`，三个主题时 `farm(∅) / glass{radius,glass} / neon{radius}` 会因为先跟 `farm` 比而漏掉 `glass` vs `neon` 的真问题。先算全局的「全有或全无」再决定要不要剔除，绕开了这个坑。

### 3.3 精度损失：`<Frame>`（明确接受）

`CheckShape` 是**样式级**规则，跑在展开之前（`DocumentLinter` 有意如此：它诊断的东西正是让展开抛异常的原因），**看不到 tag**。而 `<Frame>` 的面板是直接 `AddComponent` 挂在自己身上、不做 per-pass reconcile（`Frame.cs:24`），**不自愈** —— `VariantBaseRules` 的注释里明确把 Frame 排除在外，它有 node 上下文所以做得到，这里做不到。

于是 `<Frame class="btn">` 这类用法会**漏报**。明确接受，两个理由：

1. 与仓库一贯取向一致 —— `ProceduralSurfaceRules` 的注释原话：「a false positive turns the CLI's non-zero exit into a wall for correct XML」，一直是宁可漏报。
2. Frame 上「一个主题给 radius、另一个不给」本来就少见：Frame 没有 sprite 可保护，作者没有动机去做 wholesale 切换，直接两个主题都写 `radius` 就行。

在 SKILL 里用一句话标出来（§8）。想彻底闭合得把 shape 检查挪到展开后按节点跑 —— 列进 §6 的 YAGNI。

### 3.4 一处已有测试要改指

`ThemeStyleRulesTests.AttributeOnlyOneThemeSets_IsReported` 现在用 `glow='8'` 当「只有一个主题写的属性」举例，而 `glow` 在 `P` 里 —— 加了豁免它会变绿。把那个例子换成非程序化属性（`fontSize` / `padding`），程序化的情形由新测试覆盖（§7）。**这是改指，不是删测试**：断言的语义没变，只是换了个不撞豁免的载体。

## 4. 示例改造：`CommonControls` 转纯 theme

把 18 行 `attr.glass=` 从全局基线搬进 `<Theme name="glass">` 的同名 `<Style>`，去掉 `.glass` 后缀。改完全局基线就是「农场皮的完整描述」，glass 主题就是「和农场不一样的那些项」—— 正是 spec 说的作者心智模型。

**`glass` 变体不会消失**：`<Screen scale-mode="pixel" scale-mode.glass="auto">` 还得靠它（`<Screen>` 挂不住 `class=`，是 parse error），C# 那边 `UI.Variants.Set("glass", …) + UI.Theme.Set(…)` 照旧成对。但示例的叙事从「主题和变体配合换皮」收敛成 **「皮肤 = theme；变体只管 Screen 级 scale-mode 这一件 theme 干不了的事」**，文件头那段注释也跟着重写。

**影响面**：全仓库只有 `CommonControls.ui.xml` 用了 theme-scoped `<Style>`（`ProceduralStyle` 的两个 skin 文件里 `<Theme>` 只有 `<Color>`）。验收：`dotnet run --project .lint/UIXmlLint -- Samples~/` 零 issue，且在 Unity 里两个主题来回切外观与今天逐像素一致。

## 5. 相邻缺陷：`Restore()` 盖掉本轮写入的 sprite / alpha

### 5.1 两处

```csharp
private void Restore()                       // ProceduralSurface.cs:174
{
    if (!_retired || _hostImage == null) return;
    _retired = false;
    _hostImage.sprite = _retiredSprite;      // ← 无条件
    _hostImage.type = _retiredType;          // ← 无条件
    var c = _hostImage.color;
    _hostImage.color = new Color(c.r, c.g, c.b, _retiredColor.a);   // ← 无条件
}
```

`_retiredSprite` / `_retiredColor` 是**首次** `Retire()` 时抓的，而 `Retire()` 由 `Reconcile()` 调用、`Reconcile()` 在 `OnAfterApply` 里跑 —— 也就是**在本轮所有 setter 跑完之后**。所以抓到的不是「控件的内置默认」，是「那一轮作者声明的最终值」。

关掉程序化模式的那一轮，作者的 `sprite=` / `color=` setter 已经把新值写进 Image 了，`Restore()` 紧接着用旧快照盖掉。

**触发条件**：首次 `Open` 时程序化模式就是开的。示例里 `UI.Theme.Set("glass")` 发生在 `UI.Open` 之前（比如持久化了上次的皮肤选择），切回 farm 时：木头 sprite 被写成 glass 那轮的 `sprite="none"`（null），alpha 被按回 `white/0.22` 的 0.22。

第二条不依赖启动顺序的路径：程序化模式**保持开着**的同时 sprite 换过（主题 A→B 都是玻璃但贴图不同），之后再关 —— 快照停在 A，还原成 A。

**与 §3 正交**：今天的 `.glass` 写法一模一样中招，改不改 lint 都在。放同一个 PR 是因为两者都在「主题切换 × 程序化表面」这条线上，测试夹具共用。

### 5.2 Red test 先行

三条 EditMode 测试，全部零资产依赖（`UI.SpriteResolver` 是公开可设的 `Func<string, Sprite>`，1×1 纹理 `Sprite.Create` 即可）：

1. **alpha 不该被按回去** —— `<Btn id='b' color='#E8D2A8' color.mobile='white/0.22' radius.mobile='10'/>`，`Variants.Set("mobile", true)` **先于** `Open`，然后置 false，断言 `bg.color.a == 1`。
2. **本轮写入的 sprite 不该被盖** —— 同形状，`sprite='s:wood' sprite.mobile='none'`，断言退出程序化后 `bg.sprite` 是 wood。
3. **没人声明 sprite 时，内置默认仍要回来**（守卫，今天就是绿的，防止修法改过头）—— `<Btn id='b' radius.mobile='8'/>` 往返一次，断言 `bg.sprite` 非 null 且等于内置 9-slice。

一并补一条**主题维度**的等价用例（用 `<Theme>` 而非变体驱动同一往返），钉住 §4 改造后的示例形状不会踩回这个坑。

### 5.3 修法

不需要动八个控件的 `Sprite` setter。`Retire()` 每轮都把 sprite 写成 null，所以**关模式那一轮 Image 的 sprite 非 null，只可能是本轮 setter 写的**：

```csharp
if (_hostImage.sprite == null)                 // 本轮没人声明 sprite → 还原内置默认
{
    _hostImage.sprite = _retiredSprite;
    _hostImage.type = _retiredType;
}
if (!_hasFill)                                 // 本轮没人声明 color → 还原退休前的 alpha
    _hostImage.color = new Color(c.r, c.g, c.b, _retiredColor.a);
```

alpha 那半用的是**现成的** `_hasFill` —— `SetFill` 由控件的 `color=` setter 调、`BeginPass` 每轮清零，语义正好是「本轮声明过颜色吗」。sprite 那半用 Image 自身的状态当信号，不新增字段。

**残余空洞**（明确记录，不修）：某主题写 `sprite="none"` 而另一主题**完全不写** `sprite=`，快照会是 null，切回去拿不到内置默认。这个形状本身就是 SHAPE 规则要报的（`sprite` 不在 §3.1 的豁免集里），变体侧则由 `PUI-VARIANT-NO-BASE` 报（`sprite` 不在自愈白名单里）—— 两条路都已经有 lint 挡着，运行时不必再兜。

## 6. 不做的事（YAGNI 记录）

- **展开后按节点跑 shape 检查**（能拿到 tag，把 §3.3 的 Frame 漏报闭合）。要把 `CheckShape` 拆成「展开前样式级 + 展开后节点级」两遍，而它现在**故意**跑在展开之前。等真有人被 Frame 那个洞咬到再说。
- **让 `ReSolve` 对「消失的属性」回放控件默认值**。theme-driven-style §6.1 已经否掉过：237 个 `[UIAttr]` setter 各自定义复位语义，十倍工作量，且 `mask` / `type` / `fit` 根本没有干净的默认。本设计正相反 —— 承认「有些属性天生自愈」，只让 lint 认得这件事。
- **把 `color` 也纳入 §3.1 的豁免**。见 §3.1 的理由；等 §5 修完、`_hasFill` 路径被测试钉死之后可以重新评估。
- **给 `<Frame>` 加 per-pass reconcile 让它也自愈**。那是改运行时语义，影响面远超本设计，且 Frame 没有需要保护的 sprite。

## 7. 测试（Red 先行）

**`PromptUGUI.Tests.EditMode` / Lint**（`ThemeStyleRulesTests`）

1. `ProceduralShapeOnlyOneThemeSets_IsExempt` —— 全局 `<Style name='btn' sprite='…'/>` + glass 主题加 `radius glass borderWidth borderColor`，断言无 `ShapeCode`。
2. `PartialProceduralSet_IsStillReported` —— 三主题，一个 `∅`、一个 `{radius, glass}`、一个 `{radius}`，断言仍报（钉 §3.2 的「全有或全无」与顺序无关性）。
3. `NonProceduralAttributeAlongsideProcedural_IsStillReported` —— 差集是 `{radius, fontSize}`，断言仍报（豁免只吃 `P` 里的名字）。
4. `SpriteOnlyOneThemeSets_IsStillReported` —— 钉 `sprite` 不在豁免集里（§5.3 残余空洞的静态兜底）。
5. `InnerLayerRadius_IsExempt` —— `fillRadius` / `handleRadius`。
6. `AttributeOnlyOneThemeSets_IsReported` —— 改指到 `fontSize`（§3.4）。

**`PromptUGUI.Tests.EditMode` / Controls**（新建 `ProceduralSurfaceRestoreTests`）

7–10. §5.2 的四条（alpha / sprite / 内置默认守卫 / 主题维度等价）。

**CLI 回归**

11. `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/ Samples~/` 零 issue（今天的基线：13 个文件 0 issue；改造后的示例是这条规则最真实的夹具）。
12. `ProceduralAttrNamesTests` 照旧 —— §3.1 的名字集合从 `ProceduralAttrNames` 派生，不新开一份手抄清单，drift 由已有测试守着。

## 8. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md` 的 **Theme-scoped styles** 一节：「Always declare the global `<Style>` with the full attribute set」这条要加例外 —— 形状类属性（`radius` / `glass` / `border*` / `*Radius`…）**不要**给基线，一个主题整套写、别的主题一个不写才是正确写法；并说明为什么（写了基线 = 打开程序化模式 = 丢 sprite），附 `PUI-PROC-SPRITE-CONFLICT` 的交叉指引。同时点出 §3.3 的 Frame 例外。
- 那一节现有的示例代码 `<Style name="card" … radius="16"/>` + `<Theme name="pixel"><Style name="card" … radius="0"/></Theme>` 保留 —— Frame-backed 的 card 上它是对的 —— 但要加一句区分「Frame 上写基线 OK，Image-backed 控件上不 OK」。
- `reference/glass.md`：换皮场景补一段「一个主题玻璃、一个主题贴图」的正确写法。
- `reference/states.md` / `controls-progress.md`：若引用了 `radius.<variant>` 的旧惯用法，改指主题写法。
- lint 代码表里 `PUI-THEME-STYLE-SHAPE` 一行补上豁免说明。

## 9. 里程碑拆分

| M | 内容 | 验收 |
|---|---|---|
| **M0** | §7 的 1–5 与 7–10 全部写成 Red（含 6 的改指），确认 1/5 与 7/8 真的红 | EditMode 跑通，红的条数与预期一致 |
| **M1** | `ThemeStyleRules.CheckShape` 豁免落地 | 1–6 转绿；`UIXmlLint` 对今天的 sample 仍零 issue（豁免不改变现状） |
| **M2** | `ProceduralSurface.Restore()` 两处加条件 | 7–10 转绿；`ProceduralSurfaceRolloutTests` / `ProceduralSurfaceContractTests` 全绿（不许回归） |
| **M3** | 示例转纯 theme + 文件头注释重写 | 11 零 issue；Unity 里 farm↔glass 往返外观与今天一致 |
| **M4** | SKILL 更新（§8） | —— |

`dotnet format --verify-no-changes --severity warn` 每个 M 后跑一次。

## 10. 实施记录

### M0（分支 `test/theme-procedural-shape-red`）

Red test 全部落地，**EditMode 2485 passed → 2479 passed / 6 failed**，6 条全是新写的，其余一条没动。

| 测试 | 位置 | 状态 | 实测失败信息 |
|---|---|---|---|
| `ProceduralShapeOnlyOneThemeSets_IsExempt` | `ThemeStyleRulesTests` | RED（M1） | `Expected: <empty> But was: <LintIssue>` |
| `InnerLayerRadius_IsExempt` | 同上 | RED（M1） | 同上 |
| `NonProceduralAttributeAlongsideProcedural_IsStillReported` | 同上 | **半红**（M1） | 报告本身在（`fontSize` 是真残留），但消息里**还带着 `radius`** —— 正是会把作者引去加那个毁掉另一套皮的基线 |
| `PartialProceduralSet_IsStillReported` | 同上 | GREEN（守卫） | —— |
| `SpriteOnlyOneThemeSets_IsStillReported` | 同上 | GREEN（守卫） | —— |
| `LeavingProceduralMode_KeepsTheAlphaThisPassDeclared` | `ProceduralSurfaceRestoreTests`（新建） | RED（M2） | `Expected: 1.0 But was: 0.2196` —— 正好是 `#FFFFFF38` 的 alpha，玻璃皮的值漏了过来 |
| `LeavingProceduralMode_KeepsTheSpriteThisPassDeclared` | 同上 | RED（M2） | `Expected: same as <Sprite> But was: null` |
| `ThemeDrivenRoundTrip_KeepsWhatTheIncomingThemeDeclared` | 同上 | RED（M2） | 同上 |
| `LeavingProceduralMode_RestoresTheAlphaWhenNoColourIsDeclared` | 同上 | GREEN（守卫） | 防止 M2 修过头变成「什么都不还原」 |
| `LeavingProceduralMode_RestoresTheBuiltinSpriteWhenNoneIsDeclared` | 同上 | GREEN（守卫） | 同上 |

**§3.4 的改指比预想多一处。** `AttributeOnlyOneThemeSets_IsReported` 之外，`StyleOnlyOneThemeDeclares_IsReported` 也拿 `glow='4'` 当载体，同样会被豁免吃掉。两条一起换成 `fontSize`（载体从 `<Image>` 换成 `<Text>`，让夹具名副其实），断言语义未变。

**一个夹具陷阱，值得写进记录。** `ThemeDrivenRoundTrip_…` 第一版用 `UI.LoadDocument(label, xml)` 建文档，**绿了** —— 不是因为没缺陷，是因为同步重载**有意绕过** `RegisterThemesAndAutoSet`（`ThemeStyleSwitchTests` 的类注释写着这件事），`ThemeStore` 里压根没有 `glass`，`ResolveStyles` 原样返回全局包，玻璃皮从未上身，断言空跑通过。改用 fake-files + `LoadDocumentAsync` 后立刻转红。夹具里补了一条 `Assume.That(BgOf(b).sprite, Is.Null)` 守着这个坑 —— 以后谁再把它退回同步路径，会在 Assume 上就地暴露，而不是伪装成绿色。

`dotnet format --verify-no-changes --severity warn` 无输出。

### M1 —— `CheckShape` 的豁免

按 §3.2 落地，形式与 spec 一致：豁免集从 `ProceduralAttrNames` 派生（`NeedsPanel` 去 `weld` + `InnerLayerRadius`），「全有或全无」跨全部主题算一次再进两两比较。

写代码时确认了 §3.2 那段顺序无关性的推导不是纸上谈兵：`CheckShape` 的循环拿排序第一的主题当参照并在首个不一致处 `break`，所以豁免**只能**在循环外算。放进循环就会分别放过 `plain-vs-full` 与 `plain-vs-partial`，永远发现不了 `full` 与 `partial` 彼此不一致 —— `PartialProceduralSet_IsStillReported` 就是钉这个的（主题名刻意排成 `a-plain` 最先）。

EditMode **2485 → 2482 passed / 3 failed**（只剩 M2 的三条）。`UIXmlLint` 对仓库 13 个文件仍零 issue —— 豁免不改变今天的判定。§2.2 那份纯 theme 草稿：**8 条 SHAPE → 0 issue**。

### M2 —— `Restore()` 只还本轮没写过的

按 §5.3，两处加条件，不新增按属性的标志位。实现时确认了两件 spec 里推断过的事：

- `_hasFill` 的语义确实是「本轮声明过颜色吗」—— 八个控件的 `color=` setter（`Btn.cs:215` 等）都是先 `ColorApplier.Apply(_bg, spec)` 再 `Surface.SetFill(...)`，内层（`Slider.FillSurface` / `Progress.FrameSurface`）各有自己的 `_hasFill`，逐层成立。
- sprite 那半不需要新信号：`Retire()` 每轮把它清成 null，所以关模式那一轮非 null 只可能是本轮 setter 写的。

EditMode **2485/2485**、PlayMode **171/171**、EditorOnly **308/308** 全绿。

### M3 —— 示例转纯 theme

18 行 `attr.glass=` 搬进 `<Theme name="glass">`，文件头三条规矩的第 2 条补上形状例外，`<Screen>` 那段与 `CommonControlsRunner` 的注释改成「变体只剩 scale-mode 一处」的叙事。

**外观不变是验过的，不是眼看的。** 写了个脚本按合并语义（global → theme 覆盖，按属性名原子；老写法再叠上变体命中）把 old/new 两份文件的**解析后属性映射**逐条比：12 个样式 × 2 套皮，**全部逐条相同**。比肉眼比对可靠，也比「跑一遍看看」快。

`UIXmlLint` 13 文件 0 issue，EditMode 2485/2485。

### M4 —— SKILL

- `SKILL.md` 主题一节：「全局 `<Style>` 要写全套属性」加例外段落，讲清「形状属性是开关不是值」、豁免的全有或全无、`sprite` / `color` 不豁免的理由，以及 `<Frame>` 不自愈这条漏报。既有的 `card` / `radius="16"` 例子保留并注明「它落在 `<Frame>` 上所以对」。
- `reference/glass.md`：新增「One theme glass, another bitmap」一节 —— 这是玻璃最常见的换皮场景，正面给出写法并写明「不要给像素侧加基线」。
- lint 代码表补豁免说明。
- `states.md` / `controls-progress.md` 查过，没有需要改指的旧惯用法（`radius.mobile` 那处带基值，是合法的变体写法）。

## 11. 收尾状态

四个里程碑全部完成，三套测试全绿（EditMode 2485 / PlayMode 171 / EditorOnly 308），`UIXmlLint` 13 文件 0 issue，`dotnet format --verify-no-changes --severity warn` 无输出。

分支 `test/theme-procedural-shape-red`，四个 commit 一一对应 M0–M3，M4 并入最后一个。**未合并到 main。**
