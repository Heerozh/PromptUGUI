# `<Screen scale-mode="pixel">` 像素级整数缩放设计

**日期**：2026-05-25
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在 `<Screen>` 上新增可选属性 `scale-mode="auto|pixel"` + C# 项目级默认 `UI.DefaultScaleMode`，让像素美术项目通过 ConstantPixelSize + 整数倍 scaleFactor + 0.5x snap 兜底，实现"sprite 像素永远整数对齐到屏幕像素"。补足 [`2026-05-13-screen-reference-resolution-design.md`](2026-05-13-screen-reference-resolution-design.md) §10 显式 punt 的"像素风游戏的整数倍 scaling"开放问题。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5 / §6；[`2026-05-13-screen-reference-resolution-design.md`](2026-05-13-screen-reference-resolution-design.md)（reference 属性与 Variant 基础设施）

---

## 1. 背景与目标

### 1.1 现状

`Screen.ApplyCanvasScaler` (`Runtime/Application/Screen.cs:136-152`)：

```csharp
if (!parsed.HasValue)
{
    scaler.uiScaleMode = ConstantPixelSize;
    scaler.scaleFactor = 1f;
    return;
}
scaler.uiScaleMode = ScaleWithScreenSize;
scaler.referenceResolution = size;
scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
```

两条路径：

- **无 `reference=`**：ConstantPixelSize, scaleFactor=1。XML 数字 = 物理像素。响应式靠 anchor 自然完成。
- **有 `reference=`**：ScaleWithScreenSize, match 按朝向自动推断。**连续分数缩放**——1366×768 屏 + 1920×1080 设计稿，整套 UI 按 0.711 倍渲染。

对像素美术，第二条死了：32×32 sprite 被映射到 22.77 px，无论用 point filter 还是 bilinear 都会出现"等距不等长"的视觉瑕疵（一个 4 像素的描边在 0.711x 下会变成 3 或 2 像素，相邻边宽不一致）。第一条干净但完全不响应屏幕。

### 1.2 业内做法

像素艺术游戏（Celeste、Stardew Valley、Dead Cells、Hyper Light Drifter、CrossCode）一律采用：

1. **ConstantPixelSize** + 手算的 **整数倍** scaleFactor
2. 设计一个"逻辑分辨率"（如 480×270 / 640×360 / 1920×1080），运行期算 `factor = min(floor(screenW/dW), floor(screenH/dH))`
3. UI 元素的 width/height 保持原始小数字，**位置**通过 anchor + margin 响应屏幕
4. 子-1x 屏幕场景：要么声明最小支持分辨率（玩家活该），要么允许 0.5x（保 2x2 干净降采样）

Unity 自带 `PixelPerfectCamera` 只服务 Sprite Renderer / 2D Tilemap 那条管线，**对 uGUI 不生效**。这是 Unity Canvas 上必须自己实现的功能。

### 1.3 目标

1. 新增 C# 全局 `UI.DefaultScaleMode = ScaleMode.Auto | ScaleMode.Pixel`，默认 `Auto`（零迁移）。
2. 新增 XML 属性 `<Screen scale-mode="auto|pixel">`，per-Screen override 全局默认；支持 `.variant`。
3. `Pixel` 模式：ConstantPixelSize + factor ∈ {... 0.25, 0.5, 1, 2, 3, 4 ...}，fit-inside 取小，0.5x snap 兜底子-1x。
4. 沿用现有 `reference="WxH"` 当设计分辨率；Pixel 模式必须配 reference，缺则降级 1x 并 LogError。
5. 窗口 resize 实时重算：订阅 Screen 已有的 `RectTransformDimensionsChanged`。
6. Variant flip / hot reload 走现有 `ReSolve` 路径。
7. `UI.CanvasConfigurator` 仍是最后一层 escape hatch（可改 PPU、可强制不同 factor）。

### 1.4 显式不做

- ❌ `referencePixelsPerUnit` 的 XML 属性——像素美术 PPU 是 sprite 级别属性，靠 sprite asset 配置 + `UI.CanvasConfigurator` 手改 CanvasScaler 解决，不进 XML。
- ❌ 自定义 fit policy（`fit="contain|cover|width|height"`）——fit-inside 是像素艺术 UI 的事实标准；要 lock-axis 行为应该用 `scale-mode="auto"` 走 ScaleWithScreenSize。一个属性比两个少 50% API 噪声。
- ❌ 自定义 snap 步长（除 0.5x 外允许 0.75x、1.5x 等）——破坏整数像素对齐，违反 Pixel 模式存在的理由。
- ❌ 把 `UI.DefaultScaleMode` 也搞成 Variant 维度——全局枚举不进 VariantStore；要 per-orientation 切换走 XML `scale-mode.portrait="..."`。
- ❌ Auto 模式行为变化——本特性完全增量，旧 `<Screen>` 无 `scale-mode` 且 `UI.DefaultScaleMode` 默认 `Auto` 时行为零变化。
- ❌ 移除或重命名现有 `reference=` 属性。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| PX-D1 | API 形态 | 项目级 `UI.DefaultScaleMode` + per-Screen `scale-mode` XML 属性 override | 用户明确选项 B：像素艺术项目天然全项目都要这模式，per-Screen 写一遍太冗；但留 override 给特殊 Screen（如 WebView 嵌入） |
| PX-D2 | 属性名 | `scale-mode` | 跟 Unity `CanvasScaler.ScaleMode` 同义，零猜测成本；横线连接符与 `font-size` / `text-align` 等保持一致 |
| PX-D3 | 取值枚举 | `auto` / `pixel` | `auto` = "继承全局/继承现有逻辑"，`pixel` = 新模式。不加 `stretch` / `constant` 等中间态，避免 scope creep |
| PX-D4 | 全局枚举类型 | `public enum ScaleMode { Auto = 0, Pixel = 1 }` | 默认值 0 = Auto，向前兼容；放 `PromptUGUI.Application` 命名空间 |
| PX-D5 | Fit policy（aspect 不匹配时） | fit-inside：`factor = min(floor(W/dW), floor(H/dH))` 再 snap | 唯一保证两轴都不超出屏幕的策略；slack 由 anchor=stretch 吸收，与 PromptUGUI 既有响应式机制衔接 |
| PX-D6 | sub-1x 兜底 | 1/2^n snap（0.5, 0.25, 0.125, ...） | 用户明确选项 C；保 2x2 均匀降采样，sprite point filter 干净；下方无硬下限（极端小屏自然降到 1/16 等） |
| PX-D7 | snap 算法 | `raw >= 1 ? Floor(raw) : 1/Pow(2, Ceil(Log2(1/raw)))` | 纯函数；上方整数 floor、下方 1/2^n floor；分支边界 1.0 处连续 |
| PX-D8 | Pixel + 缺 reference | LogError + 降级 ConstantPixelSize=1，不抛异常 | parse 期算不准（variant + global default 共同决定最终值），无法早失败；运行期 LogError 让作者看到 |
| PX-D9 | Pixel + reference 解析失败 | 同 PX-D8 | parse 期已校验过 reference 格式（既有逻辑），到这里理论上不会触发；防御性兜底 |
| PX-D10 | resize 触发 | 订阅 `Screen.RectTransformDimensionsChanged`，回调中重跑 `ApplyCanvasScaler`；订阅入 `_subscriptions` Close 时清理 | 已有 event，无新基础设施；ApplyCanvasScaler 改 CanvasScaler 不动 RectTransform，无递归风险 |
| PX-D11 | Variant flip 触发 | 现有 `ReSolve` 末尾已调 `ApplyCanvasScaler`（既有逻辑），自动覆盖 | 零额外工作 |
| PX-D12 | Canvas size 来源 | `RootGameObject.GetComponent<RectTransform>().rect`（Canvas 自身的 rect）；可被 `internal static Func<Vector2> CanvasSizeOverride` 注入 | rect 直接反映窗口（ScreenSpaceOverlay）；override 给 EditMode 测试用，跟 `OrientationTracker.ScreenSizeOverride` 同款 |
| PX-D13 | parse 期校验 | `scale-mode` 值非 `auto` / `pixel` → ParseException；其余延迟到运行时 | 拼写错（如 `pixel-perfect` / `integer`）必须早失败；缺 reference 是运行期校验（D8） |
| PX-D14 | Variant 支持 | `scale-mode.portrait="pixel"` 等，复用 `VariantOverrides` 基础设施 | 与 reference 一致；像素游戏极少需要按 variant 切 scale-mode，但成本几乎为零 |
| PX-D15 | XSD generator | `WriteScreen` 加 `scale-mode` 显式 attr 声明；anyAttribute 已由旧 reference spec 加过，搭车 | 跟 D13 配合让 IDE 自动补全 |
| PX-D16 | `UI.CanvasConfigurator` 顺序 | 不变：先 `ApplyCanvasScaler`，再 `Configurator.Invoke`；Configurator 永远能 override | 已有契约；不破坏 |
| PX-D17 | `UI.ResetForTests` | 恢复 `DefaultScaleMode = ScaleMode.Auto`、清掉 `CanvasSizeOverride` | 测试隔离，跟 OrientationTracker 同款 |
| PX-D18 | SKILL.md 更新范围 | XML skill: Canvas 段加 `scale-mode` + cheatsheet 一行 + File anatomy 标识；C# skill: 加一句 `UI.DefaultScaleMode`；不加对比表、不动 Common mistakes | 用户明确简化 |
| PX-D19 | master spec §5 同步 | 末尾追加 5 行说明 `scale-mode` 存在 + 引用本文档 | 维持 spec workflow |
| PX-D20 | 测试位置 | EditMode：`PixelScaleSolverTests.cs`（纯函数）+ `ScreenScaleModeTests.cs`（parse + Apply）；PlayMode：可选，先不写 | EditMode + CanvasSizeOverride 已能覆盖 resize；PlayMode 留 plan 阶段判 |

---

## 3. 改动面

### 3.1 新文件 `Runtime/Application/ScaleMode.cs`

```csharp
namespace PromptUGUI.Application
{
    public enum ScaleMode
    {
        Auto = 0,   // existing behavior: reference set → ScaleWithScreenSize, else ConstantPixelSize=1
        Pixel = 1,  // ConstantPixelSize + integer factor (+0.5x snap below 1); requires reference
    }
}
```

### 3.2 新文件 `Runtime/Application/PixelScaleSolver.cs`

```csharp
using UnityEngine;

namespace PromptUGUI.Application
{
    internal static class PixelScaleSolver
    {
        public static float Solve(Vector2 screen, Vector2 design)
        {
            if (screen.x <= 0f || screen.y <= 0f || design.x <= 0f || design.y <= 0f)
                return 1f;
            float raw = Mathf.Min(screen.x / design.x, screen.y / design.y);
            if (raw >= 1f) return Mathf.Floor(raw);
            int n = Mathf.CeilToInt(Mathf.Log(1f / raw, 2f));
            return 1f / Mathf.Pow(2f, n);
        }
    }
}
```

### 3.3 `Runtime/Application/UI.cs`

- 新增 `public static ScaleMode DefaultScaleMode { get; set; } = ScaleMode.Auto;`
- 新增 `internal static System.Func<Vector2> CanvasSizeOverride;`（EditMode 测试注入，参照 `OrientationTracker.ScreenSizeOverride`）
- `ResetForTests`：恢复 `DefaultScaleMode = ScaleMode.Auto`；清 `CanvasSizeOverride = null`

### 3.4 `Runtime/Core/Parser/UIDocumentParser.cs`

`ParseScreen` 现有 `reference=` / `reference.<variant>=` 解析块之后，追加对称的 `scale-mode=` / `scale-mode.<variant>=` 处理：

```csharp
if (el.HasAttribute("scale-mode"))
{
    var v = el.GetAttribute("scale-mode");
    ValidateScaleModeValue(v, $"<Screen name='{name}' scale-mode>");
    rootNode.Attributes["scale-mode"] = v;
}

foreach (XmlAttribute a in el.Attributes)
{
    if (!a.Name.StartsWith("scale-mode.")) continue;
    var variant = a.Name.Substring("scale-mode.".Length);
    if (variant.Contains('.'))
        throw new ParseException(
            $"<Screen name='{name}' {a.Name}>: invalid variant attribute name " +
            $"(variant suffix must be 'scale-mode.variant' with no further dots)",
            ...);
    ValidateScaleModeValue(a.Value, $"<Screen name='{name}' {a.Name}>");
    if (!rootNode.VariantOverrides.TryGetValue("scale-mode", out var list))
    {
        list = new List<(string, string)>();
        rootNode.VariantOverrides["scale-mode"] = list;
    }
    list.Add((variant, a.Value));
}
```

新私有 helper `ValidateScaleModeValue(string raw, string contextLabel)`：

- 空串 → 通过（语义 = "继承上一层 / fallback 到全局 default"）
- `"auto"` / `"pixel"` → 通过
- 其他 → `ParseException`，message 形如 `<Screen name='X' scale-mode>: invalid value 'Y' (expected 'auto' or 'pixel')`

### 3.5 `Runtime/Application/Screen.cs`

`ApplyCanvasScaler` 改为：

```csharp
private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
{
    var mode = ResolveScaleMode();
    if (mode == ScaleMode.Pixel)
    {
        ApplyPixel(scaler);
        return;
    }
    ApplyAuto(scaler);  // existing logic, extracted into a method
}

private ScaleMode ResolveScaleMode()
{
    var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
        Def.Root, "scale-mode", Variants);
    if (string.IsNullOrEmpty(raw)) return UI.DefaultScaleMode;
    return raw == "pixel" ? ScaleMode.Pixel : ScaleMode.Auto;
}

private void ApplyAuto(UnityEngine.UI.CanvasScaler scaler)
{
    // ... 现有 ApplyCanvasScaler body 原样搬过来 ...
}

private void ApplyPixel(UnityEngine.UI.CanvasScaler scaler)
{
    var refRaw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
        Def.Root, "reference", Variants);
    var design = ReferenceResolutionParser.Parse(
        refRaw, $"<Screen name='{Def.Name}' reference> (pixel mode runtime)");
    if (!design.HasValue)
    {
        Debug.LogError(
            $"[PromptUGUI] <Screen name='{Def.Name}' scale-mode='pixel'>: " +
            $"requires a reference='WxH' to compute integer scale factor. " +
            $"Falling back to ConstantPixelSize, scaleFactor=1.");
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        return;
    }
    var screenSize = UI.CanvasSizeOverride != null
        ? UI.CanvasSizeOverride()
        : ReadCanvasRectSize();
    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
    scaler.scaleFactor = PixelScaleSolver.Solve(screenSize, design.Value);
}

private Vector2 ReadCanvasRectSize()
{
    var rt = RootGameObject.GetComponent<RectTransform>();
    var rect = rt.rect;
    return new Vector2(rect.width, rect.height);
}
```

`Open()` 末尾、在 SetActive(true) 之后追加 resize 订阅：

```csharp
RectTransformDimensionsChanged += OnRectResizedReapplyScaler;
// 在 Close() 里相应解订阅 (置 null 或 -=)
```

`OnRectResizedReapplyScaler` 实现：

```csharp
private bool _isApplyingScaler;
private void OnRectResizedReapplyScaler()
{
    if (_isApplyingScaler) return;  // defensive; ApplyCanvasScaler 修改 CanvasScaler 不应回触 OnRectTransformDimensionsChange
    _isApplyingScaler = true;
    try { ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>()); }
    finally { _isApplyingScaler = false; }
}
```

`Close()` 现有清理路径已有 `RectTransformDimensionsChanged = null;`，自动覆盖本订阅，无需新代码。

### 3.6 `Editor/XsdGenerator.cs`

`WriteScreen` 在 `reference` 显式 attr 声明之后、`anyAttribute` 之前，追加：

```csharp
// scale-mode="auto|pixel", optional
w.WriteStartElement("xs", "attribute", null);
w.WriteAttributeString("name", "scale-mode");
w.WriteAttributeString("use", "optional");
// enum 约束
w.WriteStartElement("xs", "simpleType", null);
w.WriteStartElement("xs", "restriction", null);
w.WriteAttributeString("base", "xs:string");
foreach (var v in new[] { "auto", "pixel" })
{
    w.WriteStartElement("xs", "enumeration", null);
    w.WriteAttributeString("value", v);
    w.WriteEndElement();
}
w.WriteEndElement();  // restriction
w.WriteEndElement();  // simpleType
w.WriteEndElement();  // attribute
```

`anyAttribute` 已由旧 reference spec 加过，`scale-mode.<variant>` 形态自动被吸收。

### 3.7 SKILL 同步

**`.claude/skills/authoring-promptugui-xml/SKILL.md`**

"Canvas / scaler attributes on `<Screen>`" 段尾部追加：

````markdown
- `scale-mode="auto|pixel"` (+ `.variant`)：默认 `auto` = 现有 reference 逻辑（`ScaleWithScreenSize` + match 自动推断）。`pixel` 模式必须配 `reference="WxH"`，CanvasScaler 切到 `ConstantPixelSize` + 整数 factor（fit-inside 取小；屏幕 < 设计时 snap 到 1/2、1/4、1/8 等保 2x2 干净降采样）。用于像素美术 / 等距图项目——sprite 永远整数倍渲染到屏幕像素。项目级默认通过 C# `UI.DefaultScaleMode = ScaleMode.Pixel` 一次性设置；具体 Screen 想退回连续缩放写 `scale-mode="auto"`。
````

File anatomy 表里 `<Screen>` 行的 Notes 列追加 `[scale-mode="auto|pixel"]` 标识。

Quick-reference cheatsheet 在 `reference="WxH"` 那行下面加一行：

```
              scale-mode="auto|pixel"          pixel = ConstantPixelSize + integer factor
                                               requires reference; project default via UI.DefaultScaleMode
```

**`.claude/skills/scripting-promptugui-csharp/SKILL.md`**

"Canvas configuration" 段（如无则在 SpriteResolver 相邻位置）追加一句：

````markdown
**像素美术整数缩放**：`UI.DefaultScaleMode = ScaleMode.Pixel`（启动期一次性设置）让所有 `<Screen>` 默认走 ConstantPixelSize + 整数倍 scaleFactor。需要每个 Screen 配 `reference="WxH"` 作为设计分辨率。具体某个 Screen 想 opt-out 用 XML `scale-mode="auto"`。
````

**`.claude/skills/using-promptugui-addressables/SKILL.md`**：不动。

### 3.8 master spec 同步

`docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` §5 Screen 段，在 `reference=` 说明之后追加：

> `scale-mode="auto|pixel"`（可选，支持 `.variant`）：`pixel` 切 CanvasScaler 到 ConstantPixelSize + 整数倍 scaleFactor（用于像素艺术）。详见 [`2026-05-25-pixel-perfect-scaling-design.md`](2026-05-25-pixel-perfect-scaling-design.md)。

---

## 4. 触发与重算流

```
Parse:
  <Screen scale-mode="pixel" reference="480x270">
        │
        ▼
  ParseScreen: 校验 "pixel" ∈ {auto, pixel} → 写入 Root.Attributes
        │
        ▼
  ScreenDef stored

Open:
  Screen.Open()
        ├─ new GameObject(..., typeof(CanvasScaler), ...)
        ├─ canvas.renderMode = ...
        ├─ ApplyCanvasScaler(scaler)
        │     ├─ ResolveScaleMode() → Pixel (XML 显式 or 全局 default)
        │     └─ ApplyPixel:
        │           ├─ ResolveAttribute("reference") → "480x270"
        │           ├─ design = (480, 270)
        │           ├─ screenSize = CanvasRect.rect (或 override)
        │           ├─ factor = PixelScaleSolver.Solve(screenSize, design)
        │           └─ scaler.scaleFactor = factor
        ├─ UI.CanvasConfigurator?.Invoke()    ← 用户最后机会 override
        ├─ Instantiate controls
        ├─ Subscribe Variants.Changed → ReSolve
        └─ Subscribe RectTransformDimensionsChanged → OnRectResizedReapplyScaler

Window resize (玩家拖窗口 / 旋转设备):
  Canvas RectTransform 维度变 → RectDimensionsRelay → RectTransformDimensionsChanged
        └─ OnRectResizedReapplyScaler
              └─ ApplyCanvasScaler(scaler)
                    └─ ApplyPixel: 新 screenSize → 新 factor

Variant flip (UI.Variants.Set("portrait", true)):
  Variants.Changed → Screen.ReSolve
        ├─ 控件属性重 apply
        └─ ApplyCanvasScaler(scaler)         ← scale-mode.portrait 形态切换生效

Close:
  RectTransformDimensionsChanged = null;     ← 现有清理路径覆盖订阅
```

`ApplyCanvasScaler` 改 `scaleFactor` 不触发 `RectTransformDimensionsChanged`（CanvasScaler 是渲染期决策，不动 RectTransform 维度）；`_isApplyingScaler` flag 是防御性兜底。

---

## 5. 公开 API 表

| 状态 | 签名 / 行为 | 说明 |
|---|---|---|
| 新枚举 | `PromptUGUI.Application.ScaleMode { Auto, Pixel }` | public |
| 新 C# 属性 | `UI.DefaultScaleMode { get; set; } = ScaleMode.Auto;` | public; 启动期设置 |
| 新 XML 属性 | `<Screen scale-mode="auto\|pixel" scale-mode.<variant>="...">` | optional；不设 = 继承 `UI.DefaultScaleMode` |
| 新解析期错误 | `scale-mode="invalid"` → `ParseException`，msg 含 `'auto'` / `'pixel'` | 拼写错早失败 |
| 新运行期警告 | `scale-mode=pixel` + 缺 `reference` → `Debug.LogError` + 降级 `ConstantPixelSize=1` | parse 期算不准最终模式（variant + default 影响），运行时兜底 |
| 不变 | `UI.CanvasConfigurator` 签名与触发时机 | XML / default 之后跑；可 override 一切 |
| 不变 | 现有 `reference=` 属性、所有控件、Variant、Template、Hot reload | 完全增量；旧项目升级无变化 |
| 内部新增 | `PixelScaleSolver.Solve` | 纯函数；可独立 unit test |
| 内部新增 | `UI.CanvasSizeOverride` | internal；测试注入 Canvas 尺寸 |

---

## 6. 测试矩阵

### EditMode `Tests/EditMode/Application/PixelScaleSolverTests.cs`（新，纯算法）

| 屏幕 | 设计 | 期望 factor |
|---|---|---|
| 1920×1080 | 1920×1080 | 1 |
| 3840×2160 | 1920×1080 | 2 |
| 5760×3240 | 1920×1080 | 3 |
| 7680×4320 | 1920×1080 | 4 |
| 3840×1620 (21:9 on 16:9) | 1920×1080 | 1 |
| 1920×2160 (taller) | 1920×1080 | 1 |
| 1366×768 | 1920×1080 | 0.5 |
| 1280×720 | 1920×1080 | 0.5 |
| 960×540 | 1920×1080 | 0.5 |
| 480×270 | 1920×1080 | 0.25 |
| 240×135 | 1920×1080 | 0.125 |
| 100×100 | 1920×1080 | 0.03125 |
| 0×100 / 100×0 / 1920×0 | 1920×1080 | 1 (fallback) |
| 1920×1080 | 0×0 | 1 (fallback) |

### EditMode `Tests/EditMode/Application/ScreenScaleModeTests.cs`（新，parse + apply）

| 用例 | 期望 |
|---|---|
| `<Screen scale-mode="pixel" reference="1920x1080">` parse | `Root.Attributes["scale-mode"] == "pixel"` |
| `<Screen scale-mode="auto">` parse | `Root.Attributes["scale-mode"] == "auto"` |
| `<Screen>` 无 scale-mode parse | `Root.Attributes` 不含 `"scale-mode"` 键 |
| `<Screen scale-mode="invalid">` parse | `ParseException`，message 含 `scale-mode` 和 `'auto'` 或 `'pixel'` |
| `<Screen scale-mode="">` parse | 通过（语义同未设） |
| `<Screen scale-mode.portrait="pixel" scale-mode.landscape="auto">` parse | `VariantOverrides["scale-mode"]` 含两条 |
| `<Screen scale-mode.foo.bar="pixel">` parse | `ParseException`（variant suffix 多点） |
| Open `<Screen scale-mode="pixel" reference="1920x1080">` (Canvas 1920×1080) | `uiScaleMode == ConstantPixelSize`，`scaleFactor == 1` |
| Open same Screen，`CanvasSizeOverride = () => (3840, 2160)` | `scaleFactor == 2` |
| Open same Screen，`CanvasSizeOverride = () => (1366, 768)` | `scaleFactor == 0.5` |
| Open `<Screen scale-mode="pixel">` 无 reference | `LogError` 含 `requires reference`，`uiScaleMode == ConstantPixelSize`，`scaleFactor == 1` |
| `UI.DefaultScaleMode = Pixel` + `<Screen reference="1920x1080">` 无显式 scale-mode | 走 Pixel 分支 |
| `UI.DefaultScaleMode = Pixel` + `<Screen scale-mode="auto" reference="1920x1080">` | 走 Auto 分支（ScaleWithScreenSize） |
| `UI.DefaultScaleMode = Auto` + `<Screen scale-mode="pixel" reference="1920x1080">` | 走 Pixel 分支 |
| Variant flip `<Screen scale-mode.portrait="pixel" reference.portrait="1080x1920">` | 切 portrait 后 `uiScaleMode == ConstantPixelSize`；切 landscape 后回原行为 |
| Open Pixel Screen 后 `CanvasSizeOverride` 改变 + 手动 trigger `RectTransformDimensionsChanged` | scaleFactor 按新尺寸重算 |
| `UI.ResetForTests` 后 | `UI.DefaultScaleMode == Auto`，`UI.CanvasSizeOverride == null` |
| `UI.CanvasConfigurator` 在 Pixel apply 之后跑且改 scaleFactor | 最终 scaleFactor = configurator 值 |

### PlayMode（可选，plan 阶段决定）

| 用例 | 期望 |
|---|---|
| 真窗口 Pixel Screen，Game View 切换分辨率 | scaleFactor 实时跳整数台阶 |

实际上 EditMode + `CanvasSizeOverride` 已经覆盖 resize 逻辑，PlayMode 兜底是否必要由 plan 阶段判定。

### 兼容性回归

- 所有现有 EditMode + PlayMode 测试 → 全绿
- `XsdGeneratorTests`：Screen 元素含 `scale-mode` 显式 attr 声明（enum auto/pixel）
- 老 `.ui.xml`（不带 scale-mode + `UI.DefaultScaleMode == Auto`）行为零变化

---

## 7. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 用户同时 `UI.DefaultScaleMode = Pixel` 和 `UI.CanvasConfigurator` 改 scaleFactor，每次 resize 都覆盖 configurator | configurator 失效，行为不直观 | C# skill 明示"两条路径择一"；configurator 仍可改 `referencePixelsPerUnit` 等其他属性不冲突 |
| Pixel 模式 + reference 是非整数比例的设计稿（如 1920×1200 = 16:10），在 16:9 屏上永远有 letterbox | 美术上"画布永远满"被打破 | 这正是 fit-inside 该有的行为；像素美术游戏 Stardew/Celeste 都是这样 |
| 极小屏（< 1/8 设计分辨率）factor 掉到 1/16 等极端值 | UI 元素小到不可点 | 算法不设硬下限是有意的——作者应声明最小支持分辨率拒绝启动；超出本特性 scope |
| `Pixel` 模式下 `<Btn width="240">` 在 4K 屏渲染为 240×2 = 480 物理像素，跟作者直觉"240 = 设计像素"一致 | 无 | 这正是设计目标；XML skill 提一句"Pixel 模式下 width=N 是设计像素，渲染为 N*factor 物理像素" |
| Variant flip 时 ApplyCanvasScaler 重 apply 触发 OnRectTransformDimensionsChange 链反应 | 子节点 RectTransform 重排 | 改 CanvasScaler.scaleFactor 不触发 RT 维度变化（实测）；防御性 `_isApplyingScaler` flag 兜底 |
| Hot reload 时 scale-mode 改动是否生效 | 应当生效：hot reload = Close + Reopen | 测试矩阵不专测（hot reload 路径其他 milestone 已覆盖） |
| `UI.DefaultScaleMode` 是全局 mutable static，测试间隔污染 | EditMode 测可能受前一个测影响 | `UI.ResetForTests` 已覆盖；新增 SetUp/TearDown 模板进 `ScreenScaleModeTests` |
| Pixel 模式作者忘了设 reference，靠 LogError 才发现 | 跑起来 UI 全部 1x，看似正常但响应式失效 | LogError 措辞清晰；可考虑增加 XML 注释建议；不强制 parse 错因为 variant 形态情况复杂 |

---

## 8. 实施顺序（plan 时细化）

1. EditMode red 测：`PixelScaleSolver.Solve` 算法表 + `<Screen scale-mode="invalid">` 期望 ParseException
2. 新文件 `ScaleMode.cs` + `PixelScaleSolver.cs`
3. `UI.cs` 加 `DefaultScaleMode` + `CanvasSizeOverride` + `ResetForTests` 同步
4. `UIDocumentParser.ParseScreen` 加 `scale-mode` 解析 + `ValidateScaleModeValue` helper
5. `Screen.cs` 重构 `ApplyCanvasScaler` 为 `ResolveScaleMode` + `ApplyAuto`(extracted) + `ApplyPixel`
6. `Screen.Open` 末尾订阅 `RectTransformDimensionsChanged` → `OnRectResizedReapplyScaler`
7. EditMode 全套用例补完 → 全绿
8. `XsdGenerator.WriteScreen` 加 `scale-mode` 显式 attr + enum 约束
9. `XsdGeneratorTests` 加断言
10. SKILL.md 同步（XML / C# 两处）+ master spec §5 追加
11. host Unity 项目手工：Device Simulator 切多种分辨率 + 像素 sprite 验证整数对齐

---

## 9. 开放问题

| 问题 | 处置 |
|---|---|
| 是否要 PlayMode 测试兜底 | 留 plan 阶段判；EditMode + CanvasSizeOverride 理论上够 |
| 是否需要 `UI.DefaultScaleMode` 之外的项目级 reference fallback（`UI.DefaultReference`）| 不本 PR 做；旧 spec RR-D1 已 punt，本特性也 punt |
| 极小屏 (factor < 0.0625) 是否要硬下限 | 不加；作者声明最小支持分辨率是作者职责 |
| `scale-mode="stretch"` 等中间态是否后续要加 | 不在本 PR；要时单独立项；当前 `auto` + `pixel` 已覆盖核心用例 |
| `referencePixelsPerUnit` 是否要为 Pixel 模式自动算（如 = factor） | 不自动；PPU 是 sprite asset 级配置，由 `UI.CanvasConfigurator` 手改；自动会让 sprite asset 的 PPU 设置无效 |
| Pixel 模式下 anchor=stretch 的元素是否要按物理屏幕还是按设计矩形 stretch | 物理屏幕（既有行为）；这正是"位置响应式 / 内容不缩放"的实现机制 |
