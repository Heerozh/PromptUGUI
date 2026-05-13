# `<Screen reference="...">` 参考分辨率属性设计

**日期**：2026-05-13
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在 `<Screen>` 上新增可选属性 `reference="WxH"`，让作者直接在 XML 里声明 CanvasScaler 的参考分辨率，把当前"默认 1:1 物理像素 + CanvasConfigurator 手改"的隐式契约显式化为业内通行的 `ScaleWithScreenSize` 配方。支持 `.variant` 形态，满足"一份 XML 横屏 PC + 竖屏手机"核心用例。
**依赖**：[`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5 / §6（Screen 顶层属性、anchor/size 语义）；现有 `VariantResolver` / `ElementNode.VariantOverrides` 基础设施

---

## 1. 背景与目标

每个 `Screen.Open()` 都会建一个独立 Canvas + `CanvasScaler` + `GraphicRaycaster`（`Runtime/Application/Screen.cs:64-77`），但代码里只是 `AddComponent<CanvasScaler>()` —— 不设任何属性，**接受 Unity 默认值**：

- `uiScaleMode = ConstantPixelSize`
- `scaleFactor = 1`
- `referencePixelsPerUnit = 100`

也就是说今天 `<Btn width="240">` 就是 **240 个设备像素**。同一份 XML 在 1080p 屏 / 4K 屏 / iPhone SE / iPad 上视觉大小完全不同，没有任何"设计分辨率"概念。

业内调研结论：**生产项目几乎一律使用 `ScaleWithScreenSize` + 一个参考分辨率**。常见配方：

| 场景 | reference | match |
|---|---|---|
| 横屏 PC / 桌面 | 1920×1080 | 0（锁宽） |
| 竖屏手游 | 1080×1920 / 750×1334 | 1（锁高） |
| 横竖兼容 | 选一个为主 | 0.5（折中） |
| 像素风游戏 | 原生像素（480×270、640×360） | 0 或 0.5 |

当前 `UI.CanvasConfigurator` 已能让 C# 端改 CanvasScaler，但：
- 配置在 XML 之外，**不自描述**；同一份 XML 拿到不同项目里视觉行为不一样
- 项目核心用例是"一份 XML 横屏 PC + 竖屏手机"，这天然是 Variant 维度，C# configurator 表达起来要订阅 `UI.Variants.Changed` 自己做，比 XML 一行 `reference.mobile="..."` 麻烦多

### 触发场景

```xml
<!-- 默认（不设 reference）：CSize + 1，跟今天行为一致 -->
<Screen name="Debug">...</Screen>

<!-- 横屏 PC -->
<Screen name="MainMenu" reference="1920x1080">...</Screen>

<!-- 一份 XML 双形态 -->
<Screen name="MainMenu"
        reference="1920x1080"
        reference.mobile="1080x1920">...</Screen>
```

### 目标

1. 新增可选属性 `reference="WxH"` 在 `<Screen>` 上，不设保留当前 1:1 行为（零迁移成本）。
2. 支持 `.variant` 形态，复用现有 `VariantResolver` / `ElementNode.VariantOverrides` 基础设施。
3. `matchWidthOrHeight` 按参考分辨率朝向自动推断（W ≥ H → 0；H > W → 1），不暴露给 XML。需要 0.5 折中或更精细控制走 `CanvasConfigurator`。
4. XML 应用时机在 `CanvasConfigurator?.Invoke` **之前**，让 C# 仍能完全 override。
5. Variant flip 触发的 `ReSolve` **也重应用 CanvasScaler**，保 `reference.mobile` 翻转即时生效。

### 显式不做

- ❌ `match=` XML 属性 —— 自动按朝向推断已覆盖压倒性多数用例；要 0.5 落 `CanvasConfigurator`。一个属性比两个少 50% API 噪声。
- ❌ `referencePixelsPerUnit=` XML 属性 —— 同上，像素风需要的 PPU=1 落 `CanvasConfigurator`。
- ❌ 项目级默认（`UI.DefaultReferenceResolution`）—— 多数项目确实是一个值复用所有 Screen，但本 PR 只做 per-Screen；以后如 DRY 不够再加 fallback，不破坏现状。
- ❌ 在 `canvas=` 上加 variant 支持 —— 顺路扩，但 canvas mode 几乎不会按平台变；scope creep。
- ❌ "auto-pick reference from device"（如 `reference="auto"`）—— 没明确语义；作者应当显式声明设计分辨率。
- ❌ 修改任何 control 的 sizeDelta / anchor 算法 —— 本特性只动 CanvasScaler，UI 内部坐标系不变。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| RR-D1 | 属性归属 | per-Screen | 用户明确"给 Screen 上面加一个"；项目级默认作为未来可加 fallback |
| RR-D2 | 属性名 | `reference="WxH"` | 跟现有 `size="WxH"` / `cellSize="WxH"` 同格式；短但语义清晰 |
| RR-D3 | 值格式 | `"WxH"`（两个正浮点 / 整数），`x` 分隔 | 跟 `size=` parser 完全对齐；不接受关键字（无 `auto` / `none`），空串 = "无参考" |
| RR-D4 | 默认行为（不设） | `ConstantPixelSize, scaleFactor=1`（保持当前行为） | 零迁移；现有项目升级后视觉零变化 |
| RR-D5 | 设了值的行为 | `ScaleWithScreenSize, referenceResolution=parsed, matchWidthOrHeight=自动推断` | Unity 标准做法，对齐业内 |
| RR-D6 | matchWidthOrHeight 推断规则 | W ≥ H → 0（锁宽），H > W → 1（锁高） | 横屏锁宽、竖屏锁高是 Unity 教程 / asset store 的事实标准；正方形（极少见）锁宽是稳定 fallback |
| RR-D7 | matchWidthOrHeight 自定义 | XML 不暴露；走 `CanvasConfigurator` | 99% 用例不需要；不暴露避免 API 噪声 |
| RR-D8 | Variant 支持 | 是。`reference.mobile="1080x1920"` 等同其他属性 `.variant` 形态 | 项目核心用例"PC 横屏 + 手机竖屏一份 XML"；用户显式同意 |
| RR-D9 | Variant 清空语义 | `reference.var=""` → 该变体下回到 `ConstantPixelSize` | 跟 `size.var=""` 的"清空"语义一致 |
| RR-D10 | 存储位置 | `ScreenDef.Root`（即 `__screen_root__` ElementNode）的 `Attributes["reference"]` + `VariantOverrides["reference"]` | 直接复用 `VariantResolver.ResolveAttribute`；不引入新解析路径；ScreenDef 不增字段 |
| RR-D11 | 应用时机 | `Screen.Open()` 中 `root.GetComponent<Canvas>()` 后取出 CanvasScaler，在 `UI.CanvasConfigurator?.Invoke` 之前应用（CanvasScaler 是 `new GameObject(typeof(CanvasScaler), ...)` 创建期就有的组件，无需 `AddComponent`） | 跟 `canvas=` 模式应用顺序一致；保证 configurator 能 override |
| RR-D12 | Variant flip 重应用 | `ReSolve()` 在每次 variant change 时除了重 apply 控件属性外，**还要重 apply CanvasScaler** | `reference.mobile` 切换必须即时生效 |
| RR-D13 | CanvasConfigurator 与 XML 冲突时 | XML 的重 apply 会覆盖 configurator 在 variant flip 时之前的 CanvasScaler 改动 | 文档明示"两条路径择一"；同时用者罕见，加复杂调度不划算 |
| RR-D14 | Parse-time 校验 | 解析 `reference=` 和每个 `.variant` 值时立即验证 WxH 格式 + 正数；失败抛 `ParseException` | 跟 size 解析对齐；早失败 > 运行时失败 |
| RR-D15 | 无 W、无 H 或负数 | 直接 ParseException，message 形如 `<Screen reference='Wx0'>: both dimensions must be positive (got '{value}')` | 同 size 错误风格 |
| RR-D16 | `<Screen reference="">`（空串、非 variant 情况） | 等同未设 → ConstantPixelSize | 不报错；让 variant 清空语义在非 variant 情况也成立（一致性） |
| RR-D17 | XSD generator 同步 | `WriteScreen` 新增 `reference` 属性声明 + `xs:anyAttribute processContents="lax"`（接受 `reference.<variant>` 形态） | 当前 `WriteScreen` 没 anyAttribute，variant 形态会被 XSD 拒；必须加。其他 control 已经在 `commonAttrs` 里有 anyAttribute |
| RR-D18 | SKILL.md 更新 | Canvas configuration 段补 reference 属性 + 默认行为说明 + cookbook；Common attributes 表中 Screen 顶层加 reference 引用；Common mistakes 表加"4K/手机视觉大小不一"对应条目 | CLAUDE.md 触发：新增 XML 属性，强制 SKILL 更新 |
| RR-D19 | master spec §5 / §6 同步 | 在 §5（内置原语 / 顶层元素）追加一段 `<Screen reference=...>` 说明，引用本文档 | 维持"master spec 是入口、详细设计在 dated doc"workflow |
| RR-D20 | 测试位置 | EditMode：`Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`（parse + 解析后状态断言 + variant resolve）；PlayMode：`Tests/PlayMode/Application/ScreenReferenceResolutionTests.cs`（真 Canvas + CanvasScaler 实际属性 + variant flip） | 跟其他 Application 层测试位置对齐 |

---

## 3. 改动面

### 3.1 `Runtime/Core/Parser/UIDocumentParser.cs`

`ParseScreen`（line 74-）中：

- 在解析 `canvas=` 后追加 `reference=` 解析（含 `.variant` 形态扫描）
- 把基础值 `reference="..."` 写入 `screen.Root.Attributes["reference"]`
- 把 `reference.<variant>="..."` 写入 `screen.Root.VariantOverrides["reference"]`（与现有 `.variant` 解析路径一致）
- 每个写入前调用一个新私有 helper `ValidateReferenceValue(string raw, string contextLabel)`：
  - 空串 → 通过（语义 = 该变体下"无参考"）
  - 非空 → 解析 `WxH`，两个分量必须 > 0；失败抛 `ParseException` 含 line/col

伪代码：

```csharp
foreach (XmlAttribute a in el.Attributes)
{
    if (a.Name == "reference")
    {
        ValidateReferenceValue(a.Value, $"<Screen name='{name}' reference>");
        screen.Root.Attributes["reference"] = a.Value;
    }
    else if (a.Name.StartsWith("reference."))
    {
        var variant = a.Name.Substring("reference.".Length);
        ValidateReferenceValue(a.Value, $"<Screen name='{name}' reference.{variant}>");
        if (!screen.Root.VariantOverrides.TryGetValue("reference", out var list))
        {
            list = new List<(string, string)>();
            screen.Root.VariantOverrides["reference"] = list;
        }
        list.Add((variant, a.Value));
    }
}
```

（这里走"扫描 Screen 元素的属性"的小循环，避免污染主 `ParseElement` 路径。）

### 3.2 `Runtime/Application/Screen.cs`

新增私有方法 `ApplyCanvasScaler(CanvasScaler scaler)`：

```csharp
private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
{
    var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
        Def.Root, "reference", Variants);

    if (string.IsNullOrEmpty(raw))
    {
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        return;
    }

    // raw 在 parse 期已经校验过格式与正数,这里只剩"再 parse 一次"成本
    var size = ParseReferenceResolution(raw);
    scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = size;
    scaler.matchWidthOrHeight  = size.x >= size.y ? 0f : 1f;
}
```

调用位点：

1. `Open()` 中现有 `var canvas = root.GetComponent<Canvas>();` 之后取一句 `var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();`（CanvasScaler 已在 `new GameObject(...)` 时随 typeof 列表创建），紧接 `ApplyCanvasScaler(scaler)`，仍在 `UI.CanvasConfigurator?.Invoke(canvas, Def.Name)` 之前（约 Screen.cs:71→81 之间）
2. `ReSolve()` 末尾追加一次 `ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>())`（处理 variant flip）

`ParseReferenceResolution` 是 internal helper（放 `Runtime/Core/Layout/SizeSpec.cs` 旁边，或新建 `Runtime/Application/ReferenceResolutionParser.cs`），parser 期校验和运行时 re-parse 共用。

### 3.3 `Editor/XsdGenerator.cs`

`WriteScreen`（line 257-）尾部加：

```csharp
// reference="WxH", optional
w.WriteStartElement("xs", "attribute", null);
w.WriteAttributeString("name", "reference");
w.WriteAttributeString("use", "optional");
w.WriteAttributeString("type", "xs:string");
w.WriteEndElement();

// 接受 reference.<variant> 等带点号变体属性
w.WriteStartElement("xs", "anyAttribute", null);
w.WriteAttributeString("processContents", "lax");
w.WriteEndElement();
```

`anyAttribute` 必须放在所有显式 `<xs:attribute>` 之后（schema 顺序约束）。

### 3.4 `Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`（新）

见 §6。

### 3.5 `Tests/PlayMode/Application/ScreenReferenceResolutionTests.cs`（新）

见 §6。

### 3.6 `.claude/skills/authoring-promptugui-xml/SKILL.md`

- "Canvas configuration" 段补 reference 属性 + 默认行为 + cookbook（见 §7）
- Common mistakes 表加一行"UI 在不同屏上视觉大小不一"
- File anatomy 表的 `<Screen>` 行注解里加 `[reference="WxH"]`

### 3.7 `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

§5（顶层元素 / Screen）末尾追加约 5 行说明 `reference=` 存在 + 引用本文档。

---

## 4. 触发与重算流

```
Parse:
  <Screen reference="1920x1080" reference.mobile="1080x1920">
        │
        ▼
  ParseScreen: 校验两个值 → 写入 Root.Attributes / Root.VariantOverrides
        │
        ▼
  ScreenDef stored

Open:
  Screen.Open()
        ├─ new GameObject(..., typeof(CanvasScaler), ...)
        ├─ GetComponent<CanvasScaler>()
        ├─ ApplyCanvasScaler(scaler)          ← 1st apply
        │     └─ VariantResolver.Resolve("reference") 拿到当前激活值
        │     └─ size.x >= size.y ? match=0 : match=1
        ├─ UI.CanvasConfigurator?.Invoke()    ← 用户最后机会 override
        ├─ Instantiate controls
        └─ Subscribe Variants.Changed → ReSolve

Variant flip (UI.Variants.Set("mobile", true)):
  Variants.Changed → Screen.ReSolve
        ├─ 控件属性重 apply（现有路径）
        └─ ApplyCanvasScaler(scaler)          ← variant 翻转生效:
                                                  reference.mobile="1080x1920"
                                                  → match auto 翻 0→1
```

无新轮询、无 LateUpdate；CanvasScaler 应用都是幂等（同输入 → 同输出），多触发无副作用。

---

## 5. 公开 API 表

| 状态 | 签名 / 行为 | 说明 |
|---|---|---|
| 新 XML 属性 | `<Screen reference="WxH" reference.<variant>="WxH">` | 可选；不设 = 当前行为 |
| 新解析期错误 | `reference="Wx0"` / `reference="0x100"` / `reference="-1x100"` → ParseException | 两个分量必须 > 0 |
| 新解析期错误 | `reference="abc"` / `reference="100"` (无 x) → ParseException | 跟 size 解析风格一致 |
| 不变 | `UI.CanvasConfigurator` 签名与触发时机 | 仍在 `Screen.Open()` 内一次性触发；XML 配置在它之前应用 |
| 内部新增 | `Screen.ApplyCanvasScaler` private method | 实现细节 |
| 内部新增 | `ReferenceResolutionParser`（或寄生于现有 SizeSpec helper） | parse 期与 runtime 共用 |
| 不变 | 其他所有控件 / Variant / Template / Hot reload 外部行为 | XSD 增加 `reference` 与 anyAttribute；其余不动 |

---

## 6. 测试矩阵

### EditMode (`Tests/EditMode/Application/ScreenReferenceResolutionTests.cs`)

| 用例 | 期望 |
|---|---|
| `<Screen>` 无 reference attr | `Root.Attributes` 不含 `"reference"` 键 |
| `<Screen reference="1920x1080">` | `Root.Attributes["reference"] == "1920x1080"` |
| `<Screen reference="" >` 显式空串 | 写入空串（不报错），运行时按"无参考"处理 |
| `<Screen reference="1920x1080" reference.mobile="1080x1920">` | `Root.VariantOverrides["reference"]` 含 `("mobile", "1080x1920")` |
| `<Screen reference="Wx0">` | `ParseException`，message 含 `"reference"` 和 `"positive"` |
| `<Screen reference="100">` | `ParseException`（缺 `x`） |
| `<Screen reference="abcxdef">` | `ParseException` |
| `<Screen reference.tablet="-1x100">` | `ParseException`（variant 形态也走同一校验） |
| Open Screen + 默认变体 | `CanvasScaler.uiScaleMode == ConstantPixelSize`、`scaleFactor == 1`（不设 reference 时） |
| Open Screen with reference=1920x1080 | `uiScaleMode == ScaleWithScreenSize`、`referenceResolution == (1920,1080)`、`matchWidthOrHeight == 0` |
| Open Screen with reference=1080x1920 | `matchWidthOrHeight == 1`（竖屏锁高） |
| Open Screen with reference=1000x1000 | `matchWidthOrHeight == 0`（W ≥ H 规则，正方形锁宽） |
| Open + `UI.Variants.Set("mobile", true)` | ReSolve 后 CanvasScaler 切到 `reference.mobile` 值；match 从 0 翻到 1 |
| `UI.Variants.Set("mobile", false)` 切回 | CanvasScaler 回到基础 reference 值；match 翻回 0 |
| `reference.mobile=""` 显式清空 | mobile 激活时 → uiScaleMode 切回 `ConstantPixelSize` |
| `UI.CanvasConfigurator` 在 XML reference 之后跑 | configurator 设置的 `referenceResolution` 在 Open 时是最终值（不被 XML 覆盖） |

### PlayMode (`Tests/PlayMode/Application/ScreenReferenceResolutionTests.cs`)

| 用例 | 期望 |
|---|---|
| 真 Canvas + reference=1920x1080，Screen.width / Screen.height 不变化场景下子节点 RectTransform.rect 大小 | 等同于 referenceResolution=1920x1080 时 CanvasScaler 的输出（按 ratio 缩放） |
| Variant flip 触发 CanvasScaler 重 apply | 真 Canvas 上 CanvasScaler 属性切换；子节点 `LayoutRebuilder.ForceRebuildLayoutImmediate` 后 rect 改变 |
| 多个 Screen 同时开（不同 reference）互不影响 | Screen A 的 CanvasScaler 不受 Screen B variant 切换影响 |

### Editor 验证（手工）

Device Simulator 切到 iPhone 14 Pro + 一个写 `reference="1080x1920"` 的 Screen → 比较与不写 reference 时的视觉大小差异。

### 兼容性回归

- 跑所有现有 EditMode + PlayMode 测试 → 全绿
- M4 `XsdGeneratorTests`：确认生成的 `.xsd` 在 `Screen` 元素上包含 `reference` 与 `anyAttribute`
- 老 `.ui.xml`（不带 reference attr）行为与今天一致

---

## 7. SKILL.md 同步

在 "Canvas configuration" 段末尾追加：

````markdown
**Pixel units & scaling.** 默认情况下 `<Screen>` 创建的 `CanvasScaler` 是
`ConstantPixelSize, scaleFactor=1`，所以 `width="240"` ≡ 240 个**设备像素** ——
同一 XML 在 1080p / 4K / 不同手机上视觉大小不一致。要按"设计分辨率"自动缩放
（业内默认配法），在 `<Screen>` 上声明 `reference="WxH"`：

```xml
<Screen name="MainMenu" reference="1920x1080">...</Screen>

<!-- 横屏 PC + 竖屏手机一份 XML -->
<Screen name="MainMenu"
        reference="1920x1080"
        reference.mobile="1080x1920">...</Screen>
```

- `reference="WxH"` → CanvasScaler 切到 `ScaleWithScreenSize`，referenceResolution
  即该值。`matchWidthOrHeight` 按朝向自动推断：W ≥ H 锁宽（0），H > W 锁高（1）。
- 未设 / `reference=""` → 保留默认 ConstantPixelSize 行为。
- `.variant` 形态：`reference.mobile="..."` 同其他属性 variant 规则；变体切换时
  CanvasScaler 立即重应用。
- 要 `match=0.5` 折中或改 `referencePixelsPerUnit`：走 `UI.CanvasConfigurator`
  手改。**不要在两条路径同时改 CanvasScaler** —— variant flip 时 XML 路径会覆盖
  configurator 的改动。
````

Common mistakes 表加一行：

````markdown
| UI 在不同屏上视觉大小不一（4K 上变邮票、手机上变巨人） | `<Screen>` 没设 `reference=`，走默认 `ConstantPixelSize, scaleFactor=1`，XML 数字直接 = 设备像素 | 在 `<Screen>` 上加 `reference="1920x1080"`（或你的设计分辨率），切到 `ScaleWithScreenSize` |
````

File anatomy 表里 `<Screen>` 行的 Notes 列补上 `[reference="WxH"]` 标识。

---

## 8. 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 用户同时在 XML 写 `reference=` 且在 `CanvasConfigurator` 里改 `CanvasScaler`，variant flip 时 XML 路径覆盖 configurator 改动 | 看似 race condition、行为不直观 | SKILL.md 明示"两条路径择一"；configurator 仍可处理 reference 以外的属性（PPU 等）不冲突 |
| Variant flip 时 ReSolve 重 apply CanvasScaler 触发 `OnRectTransformDimensionsChange` 链反应 | 子节点 RectTransform 重排（这正是期望行为，但调用栈深） | apply 是幂等的；deepening recursion 不存在因为不会循环调用自己 |
| 老代码 / 第三方 control 缓存了 `CanvasScaler.referenceResolution`，variant flip 后没刷新 | 自定义 control 行为可能落后一拍 | 已订阅 `RectTransformDimensionsChanged` 的逻辑会自动感知；纯缓存型代码本来就有此问题（独立于本特性） |
| XSD generator 加 anyAttribute 后 IDE 自动补全变弱（schema 不再约束 `<Screen>` 属性精确集合） | Editor 体验小幅退化 | 不可避免：variant 形态本身就是开放命名空间。控件元素早就这样了 |
| 极端 W=H（正方形参考）match 强制 0 | 极少见场景下可能不是用户想要 | 走 `CanvasConfigurator` 改 match；可在 SKILL FAQ 提示 |
| `<Screen reference="">` 在非 variant 情况算空 = 不参考 | 跟"未设"等价，可能让作者以为可以 toggle | 文档明示空字符串语义；不报错保持一致性 |
| Hot reload 时 reference 改动是否生效 | 应当生效：hot reload = Close + Reopen，新值在 Open 时应用 | 测试矩阵不必专测（hot reload 路径其他 milestone 已覆盖） |

---

## 9. 实施顺序（plan 时细化）

1. EditMode red 测：`<Screen reference="Wx0">` 期望 `ParseException`；`<Screen reference="1920x1080">` Open 后期望 CanvasScaler 属性集
2. `UIDocumentParser.ParseScreen` 解析 + 校验 reference 与 `.variant` 形态（独立 helper `ValidateReferenceValue` / `ParseReferenceResolution`）
3. `Screen.Open` 新增 `ApplyCanvasScaler` 私有方法，调用点放在 `AddComponent<CanvasScaler>` 之后、`CanvasConfigurator?.Invoke` 之前
4. `Screen.ReSolve` 末尾追加 `ApplyCanvasScaler` 重 apply
5. EditMode 全套用例补完 → 全绿
6. PlayMode 三条用例 → 全绿
7. `Editor/XsdGenerator.WriteScreen` 加 `reference` attr + `anyAttribute`
8. `XsdGeneratorTests` 加断言（`Screen` 元素含 reference 属性 + anyAttribute）
9. SKILL.md 三处同步（Canvas configuration、Common mistakes、File anatomy）
10. master spec §5 末尾追加 5 行
11. host Unity 项目手工：Device Simulator 切手机 + 用 variant 看视觉差异

---

## 10. 开放问题

| 问题 | 处置 |
|---|---|
| 是否需要在 XSD 用 `xs:pattern` 约束 `reference` 值的 WxH 格式 | 不约束。parser 兜底；pattern 写法繁、易错；其他 size 类属性也未约束 |
| 项目级默认（`UI.DefaultReferenceResolution`） | 留 v2。本 PR 只做 per-Screen。Fallback 后续可加，不破坏现状 |
| `canvas=` 是否同时获得 variant 支持 | 不在本 PR 范围。需要时单独立项 |
| `referencePixelsPerUnit` 是否加 XML 属性 | 不加。像素风需要的 PPU=1 落 `CanvasConfigurator`，scope minimal |
| Match 自动推断是否暴露为可观察 API（供 Control 子类感知） | 不暴露。Control 不应依赖 Canvas 级别的 match |
| 像素风游戏的"整数倍 scaling"模式是否要做 | 不做。Unity 没原生 ScaleMode 支持这个，需自定义脚本；不属于本 PR scope |
