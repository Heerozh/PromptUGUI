# `scale="Nx"` 设备像素密度锁定设计

**日期**：2026-05-31
**状态**：设计阶段（待 review，未进入实施）
**作用域**：给已有的 `scale` 属性新增一种取值形态 `Nx`（N 为正整数），语义 = "把这个元素锁定为每个设计单位渲染 N 个物理像素"，即 `RectTransform.localScale = N / canvasFactor`，并在 canvas factor 变化时重算。让像素美术项目里"原生只能做到 12×12 的中文位图字"能在任意自动算出的整数 factor（2/3/4…）下都缩到目标物理尺寸**而不丢像素对齐**。
**依赖**：
- [`2026-05-25-pixel-perfect-scaling-design.md`](2026-05-25-pixel-perfect-scaling-design.md)（`scale-mode="pixel"` + `PixelScaleSolver` 整数 factor）
- 已落地的 box-preserving `scale="N"`（commit `908fea7`，`Screen.ApplyScales` / `ApplyBoxPreservingCompensation`，XML skill "Relative scale (box-preserving)" 段）
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §6（layout）

---

## 1. 背景与目标

### 1.1 问题

`scale-mode="pixel"` 下 CanvasScaler 走 ConstantPixelSize + 整数 factor（`PixelScaleSolver.Solve` fit-inside 取小），factor 随屏幕/设计分辨率自动算出，**常见值是 2、3、4**（如 640×360 设计稿在 1080p 屏上正好 3）。

一个原生 12×12 的中文位图字（中文最小可读尺寸，无法再小），在 factor=3 下渲染为 36×36 物理像素——对像素游戏偏大。作者自然想用已有的 `scale="0.5"` 缩小，但这是个**固定乘数**：

```
每纹素物理像素 = canvasFactor × localScale = 3 × 0.5 = 1.5   ← 非整数 → 字格跨像素 → 糊
```

`scale="N"` 是固定值，canvasFactor 是动态值（2/3/4 随屏幕变），固定 × 动态 → 几乎不可能恒为整数。要脆,作者得为每个可能的 factor 手写不同的 `scale.variant`,且 factor 不是 variant 维度,根本写不全。

### 1.2 关键洞察（来自用户）

不需要"限制 factor 只能偶数 / 取消 3"——那会牺牲所有 sprite 的整数缩放（factor 被迫降级 → 画面留黑边或非整数拉伸）。正确做法是**让 scale 跟着 factor 走，把 factor 约掉**：

```
令 localScale = N / canvasFactor
净物理像素/设计单位 = 设计单位 × (N/f) × f = N        ← f 被约掉,与 f 是否整数无关
```

只要 **N 是正整数**，元素内容（在 `fontSize=原生像素` 前提下 1 纹素 = 1 设计单位）就永远落在 N 个物理像素上 → 像素对齐，且与 factor 取 2/3/4 无关。

- factor 3 + `1x` → localScale 1/3 → 净 1 物理像素/纹素（字体原生 1:1 打屏，最小最脆）
- factor 3 + `2x` → localScale 2/3 → 净 2 物理像素/纹素（12×12 字渲染为 24×24，脆）
- factor 2 + `2x` → localScale 1 → 净 24×24，脆
- factor 4 + `2x` → localScale 1/2 → 净 24×24，脆

**同一句 `scale="2x"` 在所有 factor 下都给出 24×24 的脆字**——文字物理尺寸跨设备恒定（UI 文字本该如此），sprite 的整数 factor 完全不动。

### 1.3 为什么不分支 scale-mode

`localScale = N / canvasFactor` 在三种 canvas 配置下都成立，无需按模式分支：

| canvas 配置 | canvasFactor 来源 | `Nx` 行为 |
|---|---|---|
| pixel 模式（ConstantPixelSize + 整数 factor） | `PixelScaleSolver.Solve` 算出的整数 | 净 N 物理像素/单位，**真脆**（整数 factor + `Canvas.pixelPerfect=true` 顶点吸附） |
| auto 模式 + `reference`（ScaleWithScreenSize） | 连续 factor（如 2.733） | 净 **尺寸**正确（f 被约掉）；但位置受小数父链推到半像素、auto 模式不开 pixelPerfect → **清晰度尽力而为** |
| auto 模式无 `reference`（ConstantPixelSize=1） | 1 | `1x`=identity；`2x`=localScale 2（设计单位本就 = 物理像素） |

所以 `ApplyScales` 里 `Nx` 这条路是统一的——只除以一个缓存好的 `_canvasFactor`，不看模式。模式分支只在"算 factor"那层（已有的 `ApplyPixel` vs `ApplyAuto`）。auto 模式的"清晰度尽力而为"是优雅降级，不报错、不分支。

### 1.4 目标

1. `scale` 属性新增取值形态 `Nx`（N 正整数），与现有正浮点乘数语义并存。
2. 运行期 `Nx` → `localScale = N / canvasFactor`，复用现有 `ApplyBoxPreservingCompensation`。
3. canvas factor 变化（窗口 resize / 设备旋转）时重算 `Nx` 的 localScale，且不让 box 补偿累积。
4. parse 期校验 `Nx`：N 必须正整数；非法形态早失败。
5. variant 可覆盖（`scale.portrait="2x"`），沿用现有基础设施。

### 1.5 显式不做

- ❌ `Nx` 里 N 取小数（`1.5x`）——非整数 N 不保证像素对齐，违反本特性存在理由；要非整数缩放用已有的裸乘数 `scale="1.5"`。
- ❌ 改 `PixelScaleSolver` / factor 计算 / 限制 factor 取值——本特性完全不碰 canvas factor，sprite 缩放零影响。
- ❌ Screen 级 / 项目级"默认文字密度"——本 PR 只做 per-element 原语；要全局默认后续单独立项（开放问题 Q1）。
- ❌ 改裸乘数 `scale="N"` 的语义——`scale="2"`（裸乘数 localScale=2，粗 2×）与 `scale="2x"`（净 2 物理像素/单位）是两件事，文档讲清。
- ❌ auto 模式下为 `Nx` 强行开 `Canvas.pixelPerfect` 来抢清晰度——pixelPerfect 是 pixel 模式契约的一部分，auto 模式开它会改既有行为；auto 模式 `Nx` 只保证尺寸。
- ❌ 新 C# 公开 API（无 `UI.*` 变化）。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| SDD-D1 | 语法 | 复用 `scale` 属性,新增 `Nx` 取值（如 `scale="2x"`） | 与裸乘数同样产出 localScale + box 补偿,只是乘数从 factor 推导;沿用业界 `@1x/@2x` DPR 直觉,零新属性 |
| SDD-D2 | N 取值 | **正整数**（≥1） | 用户明确;只有整数 N 才保证净 N 物理像素整数对齐;小数走裸乘数 |
| SDD-D3 | 语义 | `localScale = N / canvasFactor`,box-preserving 补偿照旧（inv = `1/localScale`） | 把 factor 约掉 → 净 N 物理像素/设计单位,与 factor 是否整数无关 |
| SDD-D4 | 是否按 scale-mode 分支 | **不分支**;`ApplyScales` 统一除以缓存的 `_canvasFactor` | 公式在 pixel/auto/无-reference 三种配置都成立（§1.3）;分支只在算 factor 那层（已存在） |
| SDD-D5 | factor 来源 | Screen 新增字段 `_canvasFactor`,在 `ApplyPixel`/`ApplyAuto` 末尾缓存"本次实际生效的 factor" | 确定性、可被 `CanvasSizeOverride` 驱动、EditMode 同步可测;**不读 `Canvas.scaleFactor`**（Unity 渲染期才更新,同步 Open 下为 stale,且 `CanvasScaler.scaleFactor` 在 ScaleWithScreenSize 下是被忽略的默认 1） |
| SDD-D6 | auto 模式 factor 公式 | 无 reference → 1;有 reference → `(refW≥refH) ? screenPx.x/refW : screenPx.y/refH` | 复刻 Unity ScaleWithScreenSize 在 match∈{0,1} 端点的输出（`ApplyAuto` 已按朝向设 match 0/1）;screenPx 同 pixel 模式取 `CanvasSizeOverride ?? ReadCanvasRectSize`（pixelRect） |
| SDD-D7 | factor 变化重算 | `OnCanvasDimensionsChanged`:若 `_hasDeviceScale` 则走 `ReSolve()`（含 ApplyCommon 重置基线 + ApplyCanvasScaler + ApplyScales）;否则保持现有轻量 `ApplyCanvasScaler` 单跑 | 必须重置基线再 ApplyScales,否则 box 补偿的 1/localScale 膨胀会**累积**;ReSolve 是已被测试覆盖的"从基线重建"路径,复用最稳;无 `Nx` 时零行为变化 |
| SDD-D8 | 重算的再入保护 | 复用现有 `_isReapplyingScaler` guard 包住 ReSolve 调用 | ReSolve 内 ApplyCanvasScaler 改 scaleFactor → 改 root rect → 可能回触 OnCanvasDimensionsChanged;guard 早返兜底（既有机制） |
| SDD-D9 | `_hasDeviceScale` 计算 | Open 时扫一遍:任一节点 `scale` 基值或 variant 值匹配 `Nx` → true | 让无 `Nx` 的旧内容 resize 路径完全不变;一次性计算,O(nodes) |
| SDD-D10 | LayoutGroup 子节点 | 与裸乘数一致:localScale 应用,box 补偿跳过（"保留未缩放槽位"footgun 不变） | `ApplyBoxPreservingCompensation` 已有 LayoutGroup 跳过分支,`Nx` 自动继承 |
| SDD-D11 | `_canvasFactor ≤ 0` 兜底 | 视作 1（`Nx` → localScale = N） | 防御性;PixelScaleSolver/auto 公式正常都返回正数 |
| SDD-D12 | parse 校验 | `scale` 末尾是 `x` → 前缀须 `int≥1`,否则 ParseException;无 `x` 走现有正浮点校验;`<Animation>` 仍豁免 | `1.5x`/`0x`/`x`/`-1x` 早失败;`<Animation scale="1:0.5">` 不受影响（已有豁免） |
| SDD-D13 | XSD generator | 核对 `scale` 在 XSD 是否被 pattern/enum 约束;若为 `xs:string`/anyAttribute 则无需改;否则放开以接受 `Nx` | 多数通用属性走 anyAttribute;plan 阶段核实 |
| SDD-D14 | SKILL 更新范围 | XML skill:`scale` 属性行 + "Relative scale" 段加 `Nx` 说明 + 中文字例子;**C# skill 不动**（无 C# API 变化） | 属性取值新增 → XML skill（CLAUDE.md 规则） |
| SDD-D15 | master spec 同步 | 在 master spec relative-scale 相关处追加一行引用本文档 | 维持 spec workflow（参照 PX-D19） |
| SDD-D16 | 测试位置 | 扩 `Tests/EditMode/Application/ScaleAttributeTests.cs`(`Nx` parse + 各 factor 下 localScale + resize 重算 + 不累积) | 与现有 box-preserving 测试同文件,共用 `CanvasSizeOverride` 模式 |

---

## 3. 改动面

### 3.1 `Runtime/Core/Parser/UIDocumentParser.cs` — `ValidateScale`

现 `ValidateScale`（line 527）只接受正浮点。改为先识别 `Nx`：

```csharp
private static void ValidateScale(string raw, string contextLabel)
{
    if (string.IsNullOrEmpty(raw))
        throw new ParseException(
            $"{contextLabel}: value cannot be empty " +
            $"(expected a positive number like '0.5', or a device-density like '2x')");

    // Device-density form 'Nx': N is a POSITIVE INTEGER → localScale = N / canvasFactor
    // at runtime (lock to N physical pixels per design-unit; see scale-device-density spec).
    if (raw.Length >= 2 && raw[raw.Length - 1] == 'x')
    {
        var num = raw.Substring(0, raw.Length - 1);
        if (int.TryParse(num, System.Globalization.NumberStyles.None,
                         System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1)
            return;
        throw new ParseException(
            $"{contextLabel}: invalid device-density '{raw}' " +
            $"(expected a positive integer before 'x', e.g. '1x' or '2x')");
    }

    // Plain multiplier (existing): positive float.
    if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0f)
        throw new ParseException(
            $"{contextLabel}: invalid value '{raw}' " +
            $"(expected a positive number like '0.5', or a device-density like '2x')");
}
```

`NumberStyles.None` 确保 `1.5x`（`int.TryParse("1.5")` fail）、`+2x`、`2 x` 等都被拒。`<Animation>` 豁免（line 503 的 `if (!(tag=="Animation" && ns==null))`）保持不变。

### 3.2 `Runtime/Application/Screen.cs` — 字段 + factor 缓存

新增字段：

```csharp
private float _canvasFactor = 1f;   // 本次 ApplyCanvasScaler 实际生效的 factor；'Nx' 的除数
private bool  _hasDeviceScale;      // 任一节点用了 scale='Nx'（含 variant）→ resize 走 ReSolve
```

`ApplyPixel` 末尾（line 205-206 设 scaleFactor 处）缓存：

```csharp
// ...existing PixelScaleSolver.Solve + MinPixelScale clamp...
scaler.uiScaleMode = ConstantPixelSize;
scaler.scaleFactor = factor;
_canvasFactor = factor;            // ← 新增
```

`ApplyAuto` 两条分支都缓存：

```csharp
private void ApplyAuto(UnityEngine.UI.CanvasScaler scaler)
{
    var raw = ...ResolveAttribute("reference"...);
    var parsed = ReferenceResolutionParser.Parse(raw, ...);
    if (!parsed.HasValue)
    {
        scaler.uiScaleMode = ConstantPixelSize;
        scaler.scaleFactor = 1f;
        _canvasFactor = 1f;        // ← 新增
        return;
    }
    var size = parsed.Value;
    scaler.uiScaleMode = ScaleWithScreenSize;
    scaler.referenceResolution = size;
    scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
    // 复刻 Unity ScaleWithScreenSize 在 match∈{0,1} 端点的输出，供 'Nx' 用（确定性、可测）
    var screenPx = UI.CanvasSizeOverride != null ? UI.CanvasSizeOverride() : ReadCanvasRectSize();
    _canvasFactor = size.x >= size.y
        ? (size.x > 0f ? screenPx.x / size.x : 1f)
        : (size.y > 0f ? screenPx.y / size.y : 1f);   // ← 新增
}
```

### 3.3 `Runtime/Application/Screen.cs` — `ApplyScales` 识别 `Nx`

`ApplyScales`（line 222）解析处插入 `Nx` 分支：

```csharp
var raw = VariantResolver.ResolveAttribute(node, "scale", Variants);
var rt = kv.Value.RectTransform;
if (rt == null) continue;

if (TryParseDeviceScale(raw, out var devN))
{
    var f = _canvasFactor > 0f ? _canvasFactor : 1f;
    var dv = devN / f;
    rt.localScale = new Vector3(dv, dv, 1f);
    ApplyBoxPreservingCompensation(rt, dv);
    continue;
}

// ...existing: float.TryParse → localScale=v + ApplyBoxPreservingCompensation,
//    或 unresolved/non-numeric → localScale=1...
```

helper：

```csharp
private static bool TryParseDeviceScale(string raw, out int n)
{
    n = 0;
    if (string.IsNullOrEmpty(raw) || raw[raw.Length - 1] != 'x') return false;
    return int.TryParse(raw.Substring(0, raw.Length - 1),
        System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1;
}
```

注意：`ApplyScales` 的注释要更新——原来写"No dependence on canvas factor"，现在 `Nx` 分支依赖 `_canvasFactor`，且 resize 需重算（见 §3.5）。

### 3.4 `Runtime/Application/Screen.cs` — Open 时算 `_hasDeviceScale`

`Open()` 在 `ApplyScales()`（line 137）之前（`_nodeMap` 已填充后）算一次：

```csharp
_hasDeviceScale = false;
foreach (var node in _nodeMap.Keys)
{
    if (DeclaresDeviceScale(node)) { _hasDeviceScale = true; break; }
}
ApplyScales();
```

`DeclaresDeviceScale`：检查 `node.Attributes["scale"]` 与 `node.VariantOverrides["scale"]` 各值是否 `TryParseDeviceScale` 通过。

### 3.5 `Runtime/Application/Screen.cs` — `OnCanvasDimensionsChanged` 重算

现 `OnCanvasDimensionsChanged`（line 297）在 guard 内只跑 `ApplyCanvasScaler`。改为：

```csharp
if (_isReapplyingScaler) return;
_isReapplyingScaler = true;
try
{
    var scaler = RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
    if (scaler == null) return;
    if (_hasDeviceScale)
        ReSolve();                 // 重置基线 + ApplyCanvasScaler(更新 _canvasFactor) + ApplyScales
    else
        ApplyCanvasScaler(scaler); // 现有轻量路径，零行为变化
}
finally { _isReapplyingScaler = false; }
```

`ReSolve()` 已是 `ControlAttributeApplier.Apply`(重置 RT 到 margin 基线) → `ApplyCanvasScaler`(更新 `_canvasFactor`) → `ApplyScales`(用新 factor 重算 `Nx`)。这条路被 `Variant_reset_restores_base_geometry_and_does_not_accumulate` 测试证明 box 补偿不累积，正好满足 §3.3 的"重置基线再补偿"前提。

---

## 4. 触发与重算流

```
Parse:
  <Text scale="2x"> → ValidateScale: 末尾 'x' + 前缀 int 2≥1 → 通过；raw "2x" 存入 Attributes["scale"]

Open:
  ApplyCanvasScaler → ApplyPixel/ApplyAuto: 设 scaleFactor + 缓存 _canvasFactor (= 3)
  ...instantiate, attributes applied...
  _hasDeviceScale = 扫描节点 (任一 scale='Nx') → true
  ApplyScales:
        raw "2x" → TryParseDeviceScale → n=2
        localScale = 2 / _canvasFactor(3) = 0.6667
        ApplyBoxPreservingCompensation(rt, 0.6667)   ← inv = 1.5

Window resize / 设备旋转 (factor 3 → 2):
  Canvas RectTransform 变 → RectDimensionsRelay → OnCanvasDimensionsChanged
        └─ _hasDeviceScale → ReSolve()
              ├─ ControlAttributeApplier.Apply(每节点)  ← RT 重置回 margin 基线（消除上次 inv 膨胀）
              ├─ ApplyCanvasScaler → _canvasFactor = 2
              └─ ApplyScales: localScale = 2/2 = 1.0；box 补偿 inv = 1.0
        (无 Nx 的 Screen 仍只跑 ApplyCanvasScaler，行为不变)

Variant flip (scale.portrait="3x" 生效):
  Variants.Changed → ReSolve → ApplyCanvasScaler + ApplyScales（resolve 出 "3x"）

Close:
  RectTransformDimensionsChanged = null（既有清理路径）
```

---

## 5. 公开行为表

| 状态 | 形态 / 行为 | 说明 |
|---|---|---|
| 新 XML 取值 | `scale="Nx"`（N 正整数，+ `scale.<variant>="Nx"`） | `localScale = N / canvasFactor`，net N 物理像素/设计单位；factor 变化时重算 |
| 不变 | `scale="N"`（正浮点裸乘数） | 仍是 `localScale = N`，factor-independent，不重算 |
| 新解析期错误 | `scale="1.5x"` / `scale="0x"` / `scale="x"` / `scale="-1x"` → `ParseException`，msg 含 `device-density` + `positive integer before 'x'` | N 必须正整数 |
| 行为变化（仅含 `Nx` 的 Screen） | resize/旋转时走 `ReSolve` 而非轻量 `ApplyCanvasScaler` | 重算 `Nx` localScale；无 `Nx` 的 Screen 零变化 |
| caveat | auto 模式下 `Nx` 尺寸正确、清晰度尽力而为 | pixel 模式才有整数 factor + pixelPerfect 顶点吸附保证真脆（§1.3） |
| 不变 | C# API（`UI.*` / `IScreen` / 等） | 本特性无 C# 公开面变化 |
| 不变 | `PixelScaleSolver` / canvas factor / sprite 缩放 | 完全不碰 |
| 不变 | `<Animation scale="1:0.5">` from:to | parser 豁免不变 |

---

## 6. 测试矩阵

扩 `Tests/EditMode/Application/ScaleAttributeTests.cs`（共用现有 `OpenScreen` + `CanvasSizeOverride` 模式）：

### Parser

| 用例 | 期望 |
|---|---|
| `scale="2x"` | DoesNotThrow |
| `scale="1x"` / `scale="10x"` | DoesNotThrow |
| `scale="1.5x"` | ParseException，msg 含 `device-density` |
| `scale="0x"` | ParseException |
| `scale="x"` | ParseException |
| `scale="-1x"` | ParseException |
| `scale.mobile="2x"`（variant） | DoesNotThrow |
| `<Animation scale="1:0.5">` | DoesNotThrow（豁免不变） |

### Runtime（localScale = N / factor）

| 场景（`scale-mode`, reference, CanvasSizeOverride） | scale | 期望 localScale |
|---|---|---|
| pixel, 1920x1080, (5760,3240)→factor 3 | `1x` | 1/3 (≈0.3333) |
| pixel, 1920x1080, (5760,3240)→factor 3 | `2x` | 2/3 (≈0.6667) |
| pixel, 1920x1080, (5760,3240)→factor 3 | `3x` | 1.0 |
| pixel, 1920x1080, (3840,2160)→factor 2 | `2x` | 1.0 |
| pixel, 480x270, (1920x1080)→factor 4 | `2x` | 0.5 |
| auto, 1920x1080, (3840,2160)→factor 2 | `1x` | 0.5 |
| auto, 无 reference（factor 1） | `2x` | 2.0 |
| auto, 无 reference（factor 1） | `1x` | 1.0 |

### Box-preserving（`Nx` 复用 §`ApplyBoxPreservingCompensation`）

| 用例 | 期望 |
|---|---|
| pixel factor 3 + `<Frame anchor='stretch' margin='10,10,10,10' scale='2x'>` | localScale 0.6667；anchors 按 inv=1.5 居中加宽 [-0.25,1.25]；sizeDelta = -20×1.5 = -30 |
| LayoutGroup 子节点 `scale='2x'` | localScale 应用；anchors 不加宽（补偿跳过）；LE.preferredWidth = 未缩放声明值 |

### Resize 重算 + 不累积

| 用例 | 期望 |
|---|---|
| Open pixel `1x` @ factor 3 (localScale 1/3)，改 `CanvasSizeOverride` 到 factor 2，触发 dimension-changed | localScale = 1/2；box 补偿按 inv=2 重算，**不在上次 inv=3 基础上累积** |
| 同上来回切 factor 3→2→3 | 回到 factor 3 时 localScale 精确回 1/3、几何回 inv=3 基线（参照现有 variant 不累积测试） |
| 无 `Nx` 的 Screen resize | 仍只跑 ApplyCanvasScaler（不进 ReSolve）；现有 pixel-scale 测试全绿 |

> resize 触发机制：复用 `2026-05-25-pixel-perfect-scaling` 测试里"改 `CanvasSizeOverride` + 手动 trigger dimension-changed"的同款手法（plan 阶段对齐具体调用入口）。

### 兼容回归

- 现有 `ScaleAttributeTests` 全部用例 → 全绿（裸乘数路径未动）
- 现有 pixel-mode / variant / box-preserving 测试 → 全绿

---

## 7. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| `_canvasFactor` 缓存与 Unity 实际 `Canvas.scaleFactor` 在 auto 模式偏差 | `Nx` 净尺寸略偏 | auto 公式精确复刻 Unity match∈{0,1} 端点输出；pixel 模式直接用我们算的整数 factor，无偏差 |
| 含 `Nx` 的 Screen 每次 resize 走整套 ReSolve（比裸 ApplyCanvasScaler 重） | resize 期 O(nodes) 重算 | resize/旋转是低频事件；ReSolve 是已有路径；无 `Nx` 的 Screen 不受影响（`_hasDeviceScale` 门控） |
| auto 模式作者以为 `Nx` 一定脆，实际位置半像素发糊 | 期望落差 | XML skill 明示"`Nx` 真脆只在 `scale-mode='pixel'`；auto 模式尺寸对、清晰度尽力" |
| 作者混淆 `scale="2"`（裸乘数粗 2×）与 `scale="2x"`（净 2 物理像素脆） | 用错 | XML skill 并列两者对照说明 |
| `fontSize` ≠ 字体原生像素时 `Nx` 仍不脆（如原生 12 却写 fontSize 8 → 1 纹素≠1 单位） | 净 N 物理像素是"每设计单位"，纹素仍可能非整数 | XML skill 强调前提"`Nx` 配合 `fontSize=原生像素`"；这是位图字固有约束，非本特性能解 |
| ReSolve 内 ApplyCanvasScaler 改 scaleFactor 回触 OnCanvasDimensionsChanged | 潜在递归 | `_isReapplyingScaler` guard（既有）；ReadCanvasRectSize 读 pixelRect 切断反馈环（既有） |

---

## 8. 实施顺序（plan 时细化）

1. EditMode red 测：parser `Nx` 接受/拒绝表 + 各 factor 下 localScale 表（先红）
2. `UIDocumentParser.ValidateScale` 加 `Nx` 分支
3. `Screen.cs`：加 `_canvasFactor` / `_hasDeviceScale` 字段；`ApplyPixel`/`ApplyAuto` 缓存 factor
4. `Screen.ApplyScales` 加 `TryParseDeviceScale` 分支；更新过时注释
5. `Screen.Open` 算 `_hasDeviceScale`
6. `Screen.OnCanvasDimensionsChanged` 按 `_hasDeviceScale` 路由 ReSolve / ApplyCanvasScaler
7. EditMode 补 box-preserving + resize 不累积用例 → 全绿
8. 核实 XSD `scale` 约束（SDD-D13）；必要时放开
9. XML SKILL 同步（`scale` 行 + Relative scale 段 + 中文字例子 + 两形态对照 + caveat）
10. master spec 追加引用
11. host Unity：pixel 模式多 factor（2/3/4）下肉眼验中文位图字脆度 + resize 实时重算

---

## 9. 开放问题

| 问题 | 处置 |
|---|---|
| 是否要 Screen 级 / 项目级"默认文字密度"（免得每个 `<Text>` 都写 `scale`） | 不本 PR；per-element 原语先落地，全局默认后续看需求单独立项 |
| auto 模式 `Nx` 要不要也开 pixelPerfect 抢清晰度 | 不做；会改 auto 模式既有渲染行为，超 scope；auto 模式 `Nx` 定位为"尺寸对、清晰度尽力" |
| `Nx` 里 N 上限（如 `999x`） | 不设硬上限；N 远大于 factor 只是放大，作者自负 |
| 是否允许大写 `2X` | 只接受小写 `x`，与现有属性取值大小写惯例一致（plan 阶段如有异议再议） |
