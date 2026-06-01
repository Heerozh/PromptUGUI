# `scale="<r>R"` factor 相对像素吸附缩放设计

**日期**：2026-06-01
**状态**：设计阶段（待 review，未进入实施）
**作用域**：给已有的 `scale` 属性再新增一种取值形态 `<r>R`（r 为正浮点），语义 = "把这个元素缩放到当前 canvas factor 的 r 倍，但实效缩放吸附到最近的整数"，即 `localScale = round(canvasFactor × r) / canvasFactor`，并在 canvas factor 变化时重算。让作者能写出"比满屏小、但在任意整数 factor（2/3/4…）下都保持像素对齐、且随窗口响应"的字体/元素——填补现有 `scale="N"`（响应但奇数 factor 糊）与 `scale="Nx"`（永远对齐但不随窗口长大）之间的空档。
**依赖**：
- [`2026-05-31-scale-device-density-design.md`](2026-05-31-scale-device-density-design.md)（已合并：`scale="Nx"`、`_canvasFactor` 缓存、`_hasDeviceScale` 门控的 resize→ReSolve 重算路径、`ApplyBoxPreservingCompensation`）
- [`2026-05-25-pixel-perfect-scaling-design.md`](2026-05-25-pixel-perfect-scaling-design.md)（`scale-mode="pixel"` + `PixelScaleSolver` 整数 factor；`PixelScalePowerOfTwo` / `MinPixelScale`）
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §6（layout）

---

## 1. 背景与目标

### 1.1 问题

像素美术项目里，位图字最小可读尺寸已固定（如 12×12 中文）。`scale-mode="pixel"` 下 CanvasScaler 走 ConstantPixelSize + 整数 factor（`PixelScaleSolver.Solve` fit-inside 取小），factor 随屏幕自动算出，常见 2/3/4。作者想让文字**比满屏渲染更小**，于是用 `scale="0.5"`，但这是**固定乘数**：

```
净物理像素/设计单位 = canvasFactor × localScale = 3 × 0.5 = 1.5   ← 非整数 → 字格跨像素 → 糊
```

现有两种写法都不能同时满足"更小 + 随窗口长大 + 任意 factor 都对齐"：

| 写法 | localScale | factor=2 净 | factor=3 净 | factor=4 净 | 随窗口长大？ | 对齐？ |
|---|---|---|---|---|---|---|
| `scale="0.5"`（正浮点裸乘数） | 0.5 固定 | 1 ✓ | **1.5 ✗ 糊** | 2 ✓ | 是 | 仅当 0.5×f 为整数 |
| `scale="2x"`（device-density） | 2/f | 2 ✓ | 2 ✓ | 2 ✓ | **否（净恒为 2）** | 是 |

`scale="Nx"` 把 factor 完全约掉，净尺寸跨 factor 恒定——文字不随窗口长大；`scale="N"` 随窗口长大但奇数 factor 必糊。作者真正想要的是"**随 factor 成比例、但实效落在整数上**"。

### 1.2 关键洞察

把"相对 factor 的倍率"和"吸附到整数"两件事合到一个公式里，用已经在手的 `_canvasFactor` 一步算出，无需新增 variant 维度、无需 driver、无反馈环：

```
令 r = 作者写的相对倍率（正浮点，如 0.5）
effective = round(canvasFactor × r)          // 吸附到最近整数 → 必然像素对齐
localScale = effective / canvasFactor          // 把 factor 约回去
净物理像素/设计单位 = canvasFactor × localScale = effective = 整数      ← 与 f 是否整数无关
```

- factor 2 + `0.5R` → effective round(1)=1 → localScale 0.5 → 净 1（12 字 → 12 物理像素）
- factor 3 + `0.5R` → effective round(1.5)=**2** → localScale 2/3 → 净 2（12 字 → 24 物理像素）
- factor 4 + `0.5R` → effective round(2)=2 → localScale 0.5 → 净 2
- factor 6 + `0.5R` → effective round(3)=3 → localScale 0.5 → 净 3

净尺寸 = `round(f × r)` **随 factor 单调增长（响应窗口）**，又**永远是整数（像素对齐）**——正好是 §1.1 表里缺的那一格。同一句 `scale="0.5R"` 在所有 factor 下都对齐，且窗口越大字越大。`scale="Nx"` 解的"奇数 factor 落到整数实效"在这里自动达成，而且不丢响应性。

### 1.3 与 `Nx` 的关系（净尺寸对比）

| 形态 | localScale | 净物理像素/单位 | 随窗口长大 | 像素对齐 | 定位 |
|---|---|---|---|---|---|
| `scale="N"`（裸乘数） | N（字面） | N×f | 是 | 仅当 N×f 整数 | 渲染密度乘数，非像素契约 |
| `scale="Nx"`（device-density） | N/f | N（恒定） | **否** | 是（pixel 模式真脆） | 跨设备物理尺寸恒定的 UI 文字 |
| `scale="<r>R"`（本特性） | round(f·r)/f | round(f·r)（增长） | **是** | 是（pixel 模式真脆） | 随窗口缩放但保持对齐的小字/元素 |

三者都产出 localScale + 复用同一套 box-preserving 补偿；区别只在 localScale 怎么从 r 和 f 推导。`R` 与 `Nx` 同样依赖 `_canvasFactor`、同样需要 resize 重算——因此**复用 `Nx` 已落地的全部基础设施**（§3）。

### 1.4 为什么不分支 scale-mode（与 `Nx` 同理）

`localScale = round(f·r) / f` 在三种 canvas 配置下都成立，`ApplyScales` 里这条路只除以缓存好的 `_canvasFactor`，不看模式：

| canvas 配置 | `_canvasFactor` 来源 | `<r>R` 行为 |
|---|---|---|
| pixel（ConstantPixelSize + 整数 factor） | `PixelScaleSolver.Solve` 算出的整数（已含 `PixelScalePowerOfTwo` 吸附 + `MinPixelScale` 钳制） | 净 `round(f·r)` 物理像素/单位，**真脆**（整数 factor + pixelPerfect 顶点吸附） |
| auto + `reference`（ScaleWithScreenSize） | 连续 factor（如 2.733） | effective=round(2.733·r) 仍整数；净尺寸 = effective（整数）；但位置受小数父链推到半像素、auto 不开 pixelPerfect → **清晰度尽力而为** |
| auto 无 `reference`（ConstantPixelSize=1） | 1 | effective=round(r)；`0.5R`→round(0.5)=1→ localScale 1；`0.25R`→round(0.25)=0→钳到 1→ localScale 1 |

模式分支只在"算 factor"那层（已有的 `ApplyPixel` / `ApplyAuto`，且 `_canvasFactor` 缓存的就是钳制/吸附后的最终值）。auto 模式"清晰度尽力而为"是优雅降级，不报错、不分支——与 `Nx` 完全一致。

### 1.5 目标

1. `scale` 属性新增取值形态 `<r>R`（r 正浮点），与裸乘数 `N`、device-density `Nx` 三者并存。
2. 运行期 `<r>R` → `localScale = max(1, round(canvasFactor × r)) / canvasFactor`，复用 `ApplyBoxPreservingCompensation`。
3. canvas factor 变化（resize / 旋转）时重算 `<r>R` 的 localScale，且不让 box 补偿累积——复用 `Nx` 的 resize→ReSolve 门控路径。
4. parse 期校验 `<r>R`：r 必须正浮点；`0R`/`-0.5R`/`R` 等早失败。
5. variant 可覆盖（`scale.portrait="0.5R"`），沿用现有基础设施。

### 1.6 显式不做

- ❌ `floor` / `ceil` 变体（`<r>F` / `<r>C`）——用户只要 round；如需"永不超过请求倍率"的 floor 语义后续再立项。
- ❌ 改 `scale="Nx"` / 裸乘数 `scale="N"` 的语义——三种形态各自独立，文档讲清对照。
- ❌ 改 `PixelScaleSolver` / factor 计算 / `PixelScalePowerOfTwo` / `MinPixelScale`——本特性完全不碰 canvas factor，只读最终缓存的 `_canvasFactor`。
- ❌ Screen 级 / 项目级"默认文字密度"——仍是 per-element 原语（与 `Nx` 开放问题 Q1 一致）。
- ❌ 引入 canvas-pixel-factor 维度的内置 variant（如 `scale.pixel3x`）——经评估其需要 per-Screen factor 驱动全局 store、求值顺序反馈环、端数命名等结构性问题；本公式方案在 `ApplyScales` 内纯函数求解，无这些代价（详见本次 brainstorming 结论）。
- ❌ 新 C# 公开 API（无 `UI.*` 变化）。

---

## 2. 决策一览（CRS = Canvas-Relative Scale）

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CRS-D1 | 语法 | 复用 `scale` 属性，新增 `<r>R` 取值（如 `scale="0.5R"`），与 `N` / `Nx` 并存 | 同样产出 localScale + box 补偿，只是乘数从 `round(f·r)/f` 推导；后缀字母区分语义（裸数=字面、`x`=密度、`R`=相对吸附），沿用作者已习得的"后缀决定数字含义"规律，零新属性 |
| CRS-D2 | r 取值类型 | **正浮点**（>0，可小数：`0.5R`/`0.25R`/`1.5R`） | r 是 factor 的倍率，分数缩小正是本特性目的；与 `Nx` 的"正整数 N"形成对比（`Nx` 的 N 必整数因为它直接是净像素数） |
| CRS-D3 | 语义 | `effective = round(canvasFactor × r)`；`localScale = effective / canvasFactor`；box 补偿照旧（inv = `1/localScale`） | 净 = effective = 整数 → 像素对齐；effective 随 f 增长 → 响应窗口（§1.2） |
| CRS-D4 | 取整方式 | **round-half-away-from-zero**：`Mathf.Floor(f·r + 0.5f)` | 用户已确认 factor3×0.5=1.5 应取 **2**；`Mathf.Floor(x+0.5)` 保证 .5 一律向上（1.5→2、2.5→3），避开 `Mathf.Round` 的银行家舍入（Round(2.5)=2）反直觉。r=0.5 时每个奇数 factor 都落在 .5，取整规则影响最大，必须确定 |
| CRS-D5 | effective 下限 | `Mathf.Max(1f, …)` 钳到 ≥1 | 当 f·r < 0.5（如 factor 1 + `0.25R` → round(0.25)=0）净 0 无意义。最小可对齐净尺寸是 1 物理像素/单位 → effective 钳到 1 → localScale=1/f。防 localScale=0（注：round-half-up 下 round(0.5)=1，恰好 0.5 不触发钳制） |
| CRS-D6 | 是否按 scale-mode 分支 | **不分支**；`ApplyScales` 统一除以缓存的 `_canvasFactor` | 公式在 pixel/auto/无-reference 三配置都成立（§1.4）；与 `Nx` 同一条路 |
| CRS-D7 | factor 来源 | 复用 `Nx` 已有的 `_canvasFactor`（`ApplyPixel`/`ApplyAuto` 末尾缓存的最终生效 factor，已含 `PixelScalePowerOfTwo` 吸附 + `MinPixelScale` 钳制） | `R` 读"实际生效"的 factor → 与 pow2/min 钳制自动组合，无需额外处理 |
| CRS-D8 | factor 变化重算门控 | 把 `Nx` 的 `_hasDeviceScale` 门控**泛化为"含 factor 依赖型 scale"**：重命名内部 `_hasDeviceScale`→`_hasFactorScale`、`DeclaresDeviceScale`→`DeclaresFactorScale`、`RecomputeHasDeviceScale`→`RecomputeFactorScale`；检测同时认 `Nx` 和 `<r>R` | `R` 与 `Nx` 都依赖 `_canvasFactor`、resize 都须 ReSolve；门控语义本就该是"localScale 依赖 factor 吗"。纯内部字段/方法名，无公开面变化。（若 reviewer 偏好最小 diff，可保留旧名仅扩检测——但名字会失真） |
| CRS-D9 | 解析期校验 | `ValidateScale` 在现有"`x`→整数 / 否则浮点"基础上，先识别 `R` 后缀（前缀须正浮点）；`<Animation>` 豁免不变 | `0R`/`-0.5R`/`R`/`1e2R` 之外的非法早失败；`R` 与 `x` 后缀互斥（末字符不同），分支顺序无关 |
| CRS-D10 | 大小写 | `R` 严格大写（镜像 `x` 严格小写）；`0.5r` 小写 → ParseException，提示用大写 `R` | 两后缀各自唯一大小写，`2x` vs `2R` 视觉差异大反而防混淆；与 `Nx` 开放问题"只接受小写 x"对称。（若 reviewer 想大小写不敏感，plan 阶段再放开） |
| CRS-D11 | r>1 是否允许 | 允许（`2R` → 净 round(2f) 比满屏更大、仍对齐）；无上限 | 公式对 r>1 同样成立；放大型对齐缩放有合法用途，作者自负 |
| CRS-D12 | ApplyScales 解析顺序 | `TryParseDeviceScale`(x) → `TryParseRelativeScale`(R) → 现有 `float.TryParse`（裸乘数）→ 否则 identity | 三分支后缀互斥；`R` 必须在裸 float 之前拦截（`float.TryParse("0.5R")` 会 fail → 否则落到 identity） |
| CRS-D13 | `_canvasFactor ≤ 0` 兜底 | 视作 1（复用 `Nx` 既有 `f = _canvasFactor>0 ? _canvasFactor : 1` 写法） | 防御性；正常 factor 恒为正 |
| CRS-D14 | LayoutGroup 子节点 | 与 `N`/`Nx` 一致：localScale 应用，box 补偿在 LayoutGroup 下跳过 | `ApplyBoxPreservingCompensation` 已有 LayoutGroup 分支，`R` 自动继承 |
| CRS-D15 | XSD generator | 核对 `scale` 在 XSD 是否被 pattern/enum 约束；多数通用属性走 anyAttribute → 大概率无需改；plan 阶段核实 | 与 `Nx`（SDD-D13）同样处置 |
| CRS-D16 | SKILL 更新范围 | XML skill：`scale` 属性行 + "Relative scale" 段加 `<r>R` 形态说明 + 三形态对照表 + 中文小字例子 + auto 模式 caveat；**C# skill 不动** | 属性取值新增 → XML skill（CLAUDE.md 规则）；无 C# API 变化 |
| CRS-D17 | master spec 同步 | 在 master spec §6 `scale="Nx"` 那一行旁追加 `<r>R` 形态一行 + 引用本文档 | 维持 spec workflow（参照 SDD-D15） |
| CRS-D18 | 测试位置 | 扩 `Tests/EditMode/Application/ScaleAttributeTests.cs`（共用 `OpenScreen` + `CanvasSizeOverride` 模式） | 与 `N`/`Nx` box-preserving / resize 测试同文件 |

---

## 3. 改动面

> 全部建立在已合并的 device-density（`Nx`）实现之上；下面行号以当前主干为准。

### 3.1 `Runtime/Core/Parser/UIDocumentParser.cs` — `ValidateScale`（line 527）

在现有 `x` 分支与 float 分支之间插入 `R` 分支（注释同步补充 `R` 形态）：

```csharp
// scale="<r>R" (r positive float) is the canvas-relative snapped form: localScale =
// round(canvasFactor × r) / canvasFactor at runtime — scales with the factor but the
// effective (net physical px/unit) snaps to the nearest integer, keeping pixel alignment
// while still responding to window size. See 2026-06-01-scale-canvas-relative-snap-design.md.
if (raw.Length >= 2 && raw[raw.Length - 1] == 'R')
{
    var num = raw.Substring(0, raw.Length - 1);
    if (float.TryParse(num, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var rr) && rr > 0f)
        return;
    throw new ParseException(
        $"{contextLabel}: invalid canvas-relative scale '{raw}' " +
        $"(expected a positive number before uppercase 'R', e.g. '0.5R' or '0.25R')");
}
```

错误信息（`x` 分支与末尾兜底）补上 `, or a canvas-relative scale like '0.5R'`，让作者三种形态一眼齐全。`<Animation>` 豁免（line 503）不变。

### 3.2 `Runtime/Application/Screen.cs` — `ApplyScales`（line 242）插入 `R` 分支

在 `TryParseDeviceScale`（line 257-264）分支之后、裸 float 分支（line 266）之前插入：

```csharp
if (TryParseRelativeScale(raw, out var relR))
{
    var f = _canvasFactor > 0f ? _canvasFactor : 1f;
    // round-half-up to nearest integer effective (≥1), then divide the factor back out:
    // net physical px/unit = effective (integer → pixel-aligned), grows with f (responsive).
    var eff = Mathf.Max(1f, Mathf.Floor(f * relR + 0.5f));
    var dv = eff / f;
    rt.localScale = new Vector3(dv, dv, 1f);
    ApplyBoxPreservingCompensation(rt, dv);
    continue;
}
```

helper（紧邻 `TryParseDeviceScale`，line 309 旁）：

```csharp
// scale="<r>R" (r positive float): localScale = round(canvasFactor·r) / canvasFactor →
// scales relative to the factor but snaps the net physical-px/unit to the nearest integer
// so it stays pixel-aligned at any factor. Returns false for the 'Nx' and plain-multiplier
// forms (handled by TryParseDeviceScale / float.TryParse).
private static bool TryParseRelativeScale(string raw, out float r)
{
    r = 0f;
    if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'R') return false;
    return float.TryParse(raw.Substring(0, raw.Length - 1),
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out r) && r > 0f;
}
```

同时更新 `ApplyScales` 上方注释（line 236-238）：把"Plain-multiplier 无 factor 依赖、Nx 依赖 factor"改为"`Nx` 与 `<r>R` 两种形态依赖 `_canvasFactor`，resize 须经 ReSolve 重算"。

### 3.3 `Runtime/Application/Screen.cs` — 门控泛化（CRS-D8）

把 `Nx` 专用的门控泛化为"含 factor 依赖型 scale"。**仅内部重命名 + 扩检测**：

```csharp
private bool _hasFactorScale;   // 原 _hasDeviceScale（line 35）：任一节点用了 scale="Nx" 或 "<r>R"

// 原 RecomputeHasDeviceScale（line 286）
private void RecomputeFactorScale()
{
    _hasFactorScale = false;
    foreach (var node in _nodeMap.Keys)
        if (DeclaresFactorScale(node)) { _hasFactorScale = true; break; }
}

// 原 DeclaresDeviceScale（line 296）：base 或 variant 值匹配 Nx 或 <r>R
private static bool DeclaresFactorScale(ElementNode node)
{
    if (node.Attributes.TryGetValue("scale", out var baseVal)
        && (TryParseDeviceScale(baseVal, out _) || TryParseRelativeScale(baseVal, out _))) return true;
    if (node.VariantOverrides.TryGetValue("scale", out var list))
        foreach (var (_, value) in list)
            if (TryParseDeviceScale(value, out _) || TryParseRelativeScale(value, out _)) return true;
    return false;
}
```

调用点同步改名：`Open`（line 143）、`ReSolve`（line 482）、`OnCanvasDimensionsChanged`（line 376 的 `if (_hasDeviceScale)`）。逻辑路径完全不变——只是门控现在对 `<r>R` 也开。无 factor 依赖型 scale 的 Screen 仍走轻量 `ApplyCanvasScaler`，零行为变化。

### 3.4 触发与重算流（与 `Nx` 同一条路）

```
Parse:
  <Text scale="0.5R"> → ValidateScale: 末尾 'R' + 前缀 float 0.5>0 → 通过；raw "0.5R" 存入 Attributes["scale"]

Open:
  ApplyCanvasScaler → ApplyPixel: scaleFactor = 3（含 pow2/min 钳制）→ _canvasFactor = 3
  ...instantiate, attributes applied...
  RecomputeFactorScale → 扫描 (任一 scale 为 Nx 或 <r>R) → _hasFactorScale = true
  ApplyScales:
        raw "0.5R" → TryParseRelativeScale → r=0.5
        eff = max(1, floor(3×0.5 + 0.5)) = floor(2.0) = 2
        localScale = 2 / 3 = 0.6667
        ApplyBoxPreservingCompensation(rt, 0.6667)   ← inv = 1.5

Window resize / 旋转 (factor 3 → 4):
  Canvas RectTransform 变 → RectDimensionsRelay → OnCanvasDimensionsChanged
        └─ _hasFactorScale → ReSolve()
              ├─ ControlAttributeApplier.Apply(每节点)  ← RT 重置回 margin 基线（消除上次 inv=1.5 膨胀）
              ├─ ApplyCanvasScaler → _canvasFactor = 4
              └─ ApplyScales: eff = floor(4×0.5+0.5)=2；localScale = 2/4 = 0.5；box inv = 2.0
        (无 factor 依赖型 scale 的 Screen 仍只跑 ApplyCanvasScaler)

Variant flip (scale.portrait="0.25R" 生效):
  Variants.Changed → ReSolve → ApplyCanvasScaler + ApplyScales（resolve 出 "0.25R"，按当前 factor 重算）
```

再入保护复用既有 `_isReapplyingScaler`（line 362 区）；`ReadCanvasRectSize` 读 `pixelRect` 切断反馈环（既有）。

---

## 4. 公开行为表

| 状态 | 形态 / 行为 | 说明 |
|---|---|---|
| 新 XML 取值 | `scale="<r>R"`（r 正浮点，+ `scale.<variant>="<r>R"`） | `localScale = max(1, round(canvasFactor×r)) / canvasFactor`；净 = round(f·r) 物理像素/单位，随 factor 增长且整数对齐；factor 变化时重算 |
| 不变 | `scale="Nx"`（device-density，N 正整数） | 仍 `localScale = N/canvasFactor`，净恒 N，不随窗口长大 |
| 不变 | `scale="N"`（正浮点裸乘数） | 仍 `localScale = N`，factor-independent，不重算 |
| 新解析期错误 | `scale="0R"` / `scale="-0.5R"` / `scale="R"` / `scale="0.5r"`(小写) → `ParseException`，msg 含 `canvas-relative scale` + `positive number before uppercase 'R'` | r 须正浮点、后缀须大写 `R` |
| 行为变化（含 `Nx` **或** `<r>R` 的 Screen） | resize/旋转时走 `ReSolve` 而非轻量 `ApplyCanvasScaler` | 门控从 `_hasDeviceScale` 泛化为 `_hasFactorScale`；无 factor 依赖型 scale 的 Screen 零变化 |
| caveat | auto 模式下 `<r>R` 净尺寸正确、清晰度尽力而为 | 同 `Nx`：pixel 模式才有整数 factor + pixelPerfect 顶点吸附保证真脆（§1.4） |
| 组合 | `<r>R` 读最终 `_canvasFactor`（已含 `PixelScalePowerOfTwo` 吸附 + `MinPixelScale` 钳制） | 自动与这两个全局设置组合，无需特殊处理 |
| 不变 | C# API（`UI.*` / `IScreen` / 等） | 本特性无 C# 公开面变化 |
| 不变 | `PixelScaleSolver` / canvas factor / sprite 缩放 / `<Animation scale="1:0.5">` | 完全不碰 |

---

## 5. 测试矩阵

扩 `Tests/EditMode/Application/ScaleAttributeTests.cs`（共用现有 `OpenScreen` + `CanvasSizeOverride` 模式）：

### Parser

| 用例 | 期望 |
|---|---|
| `scale="0.5R"` / `scale="0.25R"` / `scale="1R"` / `scale="2R"` | DoesNotThrow |
| `scale="1.5R"`（r 允许小数） | DoesNotThrow |
| `scale="0R"` | ParseException，msg 含 `canvas-relative scale` |
| `scale="-0.5R"` | ParseException |
| `scale="R"` | ParseException |
| `scale="0.5r"`（小写） | ParseException，msg 提示大写 `R` |
| `scale.mobile="0.5R"`（variant） | DoesNotThrow |
| 既有 `scale="2x"` / `scale="0.5"` / `<Animation scale="1:0.5">` | 仍各自 DoesNotThrow（三形态共存，豁免不变） |

### Runtime（localScale = max(1, round(f·r)) / f）

| 场景（`scale-mode`, reference, CanvasSizeOverride → factor） | scale | 期望 localScale | 净 |
|---|---|---|---|
| pixel, 1920x1080, factor 2 | `0.5R` | 0.5 | 1 |
| pixel, 1920x1080, factor 3 | `0.5R` | 2/3 (≈0.6667) | 2 |
| pixel, 1920x1080, factor 4 | `0.5R` | 0.5 | 2 |
| pixel, 1920x1080, factor 5 | `0.5R` | 3/5 (0.6)（round-half-up：2.5→3） | 3 |
| pixel, 1920x1080, factor 6 | `0.5R` | 0.5 | 3 |
| pixel, factor 1 | `0.5R` | 1.0（round(0.5)=1，未触发钳制） | 1 |
| pixel, factor 1 | `0.25R` | 1.0（round(0.25)=0→钳到 1，验证下限） | 1 |
| pixel, factor 3 | `0.25R` | 1/3（round(0.75)=1） | 1 |
| pixel, factor 4 | `0.25R` | 0.25（round(1)=1） | 1 |
| pixel, factor 8 | `0.25R` | 0.25（round(2)=2） | 2 |
| pixel, factor 3 | `1R` | 1.0（round(3)=3，identity） | 3 |
| pixel, factor 2 | `2R` | 2.0（round(4)=4，放大对齐） | 4 |
| auto, reference 1920x1080, factor 2 | `0.5R` | 0.5（round(1)=1） | 1 |
| auto, 无 reference（factor 1） | `0.5R` | 1.0（round(0.5)=1） | 1 |

### Box-preserving（`<r>R` 复用 `ApplyBoxPreservingCompensation`）

| 用例 | 期望 |
|---|---|
| pixel factor 3 + `<Frame anchor='stretch' margin='10,10,10,10' scale='0.5R'>` | localScale 0.6667（eff 2）；anchors 按 inv=1.5 居中加宽 [-0.25,1.25]；sizeDelta = -20×1.5 = -30（与 `scale="2x"` @factor3 数值一致，可对照） |
| LayoutGroup 子节点 `scale='0.5R'` | localScale 应用；anchors 不加宽（补偿跳过）；LE.preferredWidth = 未缩放声明值 |

### Resize 重算 + 不累积 + 门控

| 用例 | 期望 |
|---|---|
| Open pixel `0.5R` @ factor 2 (localScale 0.5)，改 `CanvasSizeOverride` 到 factor 3，触发 dimension-changed | localScale = 2/3（eff 2）；box 补偿按 inv=1.5 重算，**不在上次基础上累积** |
| 同上来回切 factor 2→3→2 | 回到 factor 2 时 localScale 精确回 0.5、几何回 inv=2 基线（参照现有 `Nx`/variant 不累积测试） |
| Screen 只含 `<r>R`（无 `Nx`） resize | `_hasFactorScale` 为 true → 走 ReSolve 重算（验证门控泛化生效） |
| Screen 既无 `Nx` 也无 `<r>R` resize | 仍只跑 `ApplyCanvasScaler`（不进 ReSolve）；现有 pixel-scale 测试全绿 |

### 兼容回归

- 现有 `ScaleAttributeTests` 全部用例（裸乘数 + `Nx`）→ 全绿
- 现有 pixel-mode / `PixelScalePowerOfTwo` / `MinPixelScale` / variant / box-preserving 测试 → 全绿

---

## 6. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 重命名 `_hasDeviceScale`→`_hasFactorScale` 等触及多处调用点 | 编译/回归风险 | 纯内部、机械改名；调用点固定 3 处（Open/ReSolve/OnCanvasDimensionsChanged）；改完先 compile-check 再跑测试 |
| 作者混淆 `0.5`（糊）/ `2x`（不长大）/ `0.5R`（长大+对齐）三形态 | 用错形态 | XML skill 三形态对照表 + 净尺寸列 + "什么时候用哪个"一句话指引 |
| auto 模式作者以为 `<r>R` 一定脆，实际位置半像素发糊 | 期望落差 | XML skill 明示"真脆只在 `scale-mode='pixel'`；auto 模式尺寸/净对、清晰度尽力"（与 `Nx` 同款 caveat） |
| round-half 行为（factor5×0.5=2.5→3）作者预期不一致 | 个别 factor 净尺寸差 1 | spec/skill 写明取整规则（round-half-away-from-zero），例子覆盖奇数 factor |
| 含 `<r>R` 的 Screen 每次 resize 走整套 ReSolve | resize 期 O(nodes) 重算 | resize/旋转低频；ReSolve 已被测试覆盖；无 factor 依赖型 scale 的 Screen 不受影响（门控） |
| `_canvasFactor` 在 auto 模式与 Unity 实际值偏差 | `<r>R` 净尺寸略偏（同 `Nx`） | auto 公式精确复刻 Unity match∈{0,1} 端点输出（既有）；pixel 模式用整数 factor 无偏差 |

---

## 7. 实施顺序（plan 时细化）

1. EditMode red 测：parser `<r>R` 接受/拒绝表 + 各 factor 下 localScale 表（先红）
2. `UIDocumentParser.ValidateScale` 加 `R` 分支 + 错误信息补 `0.5R` 提示
3. `Screen.cs`：加 `TryParseRelativeScale` helper；`ApplyScales` 插入 `R` 分支（在 `Nx` 后、裸 float 前）；更新 `ApplyScales` 上方注释
4. `Screen.cs`：门控泛化——重命名 `_hasDeviceScale`/`DeclaresDeviceScale`/`RecomputeHasDeviceScale` 为 `_hasFactorScale`/`DeclaresFactorScale`/`RecomputeFactorScale`，检测加 `TryParseRelativeScale`；同步 3 处调用点
5. compile-check（read_console error）→ 跑 `ScaleAttributeTests` 转绿
6. EditMode 补 box-preserving + resize 不累积 + 门控用例 → 全绿
7. 全量 EditMode 回归（裸乘数 / `Nx` / pixel-scale / variant 全绿）
8. 核实 XSD `scale` 约束（CRS-D15）；必要时放开接受 `<r>R`
9. XML SKILL 同步（`scale` 行 + Relative scale 段 + 三形态对照表 + 中文小字例子 + caveat）
10. master spec §6 追加 `<r>R` 一行 + 引用本文档
11. host Unity：pixel 模式多 factor（2/3/4/5）下肉眼验 `0.5R` 中文位图字脆度 + resize 实时随窗口长大重算

---

## 8. 开放问题

| 问题 | 处置 |
|---|---|
| 是否要 `floor`/`ceil` 变体（`0.5F`/`0.5C`） | 不本 PR；round 先落地，确有"永不超过/不低于请求倍率"需求再加后缀，公式天然可扩展 |
| 是否允许小写 `0.5r` / 大小写不敏感 | 默认严格大写 `R`（CRS-D10）；reviewer 如倾向不敏感，plan 阶段放开（只影响 parser 一处 + helper 一处） |
| `<r>R` 与 Screen 级 / 项目级"默认文字密度"如何配合 | 与 `Nx` 开放问题合并；per-element 原语先落地 |
| r 上限 | 不设硬上限（CRS-D11）；r 很大只是放大对齐，作者自负 |
