# Color Tokens 设计

**日期**: 2026-05-28
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. 新增 `Runtime/Application/ThemeStore.cs`（注册中心 + `LookupChained` + cycle 校验）
2. 新增 `Runtime/Core/IR/ThemeBlock.cs` + `ColorEntry.cs`（POCO）
3. `Runtime/Core/Parser/UIDocumentParser.cs` 识别 `<Theme>` / `<Color>`
4. `Runtime/Application/DocumentLoader.cs` themes 进 `ThemeStore`（与 commons pool / Templates 同通道）
5. `Runtime/Application/UI.cs` 暴露 `UI.Theme.*`（含 `Resolve(string)->Color`）+ `ResetForTests` hook
6. `Runtime/Application/Screen.cs` 订阅 `Theme.Changed`，触发 `ReSolve`
7. `Runtime/Registry/UIAttrAttribute.cs` 新增 `IsColor` flag；`Runtime/Registry/ControlMeta.cs` 暴露 `ColorAttrs` 集合（与 `SpriteAttrs` 同模式）
8. 控件改造：`Image.cs` / `Text.cs` / `Btn.cs` setter 改调 `UI.Theme.Resolve`；`AnimationSpec.cs` `ParseColorFromTo` 改走 `UI.Theme.Resolve`
9. `Editor/XsdGenerator.cs` 生成 `<Theme>` / `<Color>` schema
10. `Runtime/Core/Lint/ColorLiteralRules.cs` —「现场非法 hex 字面」纯静态规则
11. SKILL: `authoring-promptugui-xml/SKILL.md` + `scripting-promptugui-csharp/SKILL.md`
12. 主 spec `2026-05-07-promptugui-description-language-design.md` §6 / §8 追加 token 解析规则

**依赖**: 无（独立扩展，复用 `LoadCommonLibraryAsync` / `Screen.ReSolve` / `[UIAttr]` 反射 / hot reload 已有机制）

---

## 1. 背景

当前控件颜色全部硬编码字面值：

```xml
<Image color="#ff8800"/>
<Text  color="#222222" text="Hello"/>
<Btn   color="#ff8800" label="Buy"/>
```

问题：

- 主色 / 辅色没集中声明，散落上百处 `.ui.xml`；改色得 grep 替换，漏一处就出现"屏幕里有 3 种主色"的偏色 bug。
- 没有亮 / 暗主题（或其他活动配色）的运行时切换路径。Variant 体系能强行做（每个控件每个属性都写 `color.dark="..."`），但维护成本爆炸。
- 三处控件 setter（`Image.Color` / `Text.Color` / `Btn.Color`）的解析逻辑是复制的，且都用 `if (TryParse) apply` 守门 —— **失败时静默 no-op**，配色错位现场没诊断信号（详见 §8）。

项目里已经有两条完全同构的「键 → 值」解析流水线，主题表自然成为第三条：

| 系统 | 注册中心 | 注册 API | 切换 API | 解析点 | 回流 |
|---|---|---|---|---|---|
| i18n | `TranslationStore` | `UI.Locale.LoadPoAsync` | `UI.Locale.Set` | `ControlAttributeApplier`（`text` attr） | `Locale.Changed` → `Screen.ReSolve` |
| Variant | `VariantStore` | `<Variant when=...>` XML 块 | `UI.Variants.Set` | `VariantResolver.ResolveAttribute` | `VariantStore.Changed` → `Screen.ReSolve` |
| Sprite | （直接转字符串） | （无独立注册） | （无切换；Addressables 解析器换） | 控件 `[UIAttr(IsSprite=true)]` setter via `UI.ResolveSprite` | （无回流） |
| **Theme** | **`ThemeStore`** | **`<Theme name="...">` XML 块**（随 `LoadCommonLibraryAsync` 或 `<Import>` 进入） | **`UI.Theme.Set`** | **控件 `[UIAttr(IsColor=true)]` setter via `UI.Theme.Resolve`** | **`Theme.Changed` → `Screen.ReSolve`** |

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CT-D1 | 引用语法 | `color="primary"` 不加 sigil | 作者偏好，最简；shadow 规则把"theme 优先于字面"显式写到 SKILL，可接受 |
| CT-D2 | 主题来源 | XML 表（`<Theme>` 块嵌进 `.ui.xml`），不引 `.colors.xml` 新后缀 | 复用 commons pool / Import / hot reload / DepGraph 所有现成机制 |
| CT-D3 | 注册通道 | `UI.LoadCommonLibraryAsync` 顺带注册 + `.ui.xml` 内 `<Import>` 顺带注册 | 不加新 loader；作者按 Screen 粒度 Import 也行，全局 boot load 也行 |
| CT-D4 | 切换 API | `UI.Theme.Set(name)`；事件 `UI.Theme.Changed` | 跟 `UI.Locale` / `UI.Variants` 同构 |
| CT-D5 | 主题必须命名 | `<Theme>` 必须 `name=` | 避免"默认主题 vs 命名主题"二义；多主题项目零歧义 |
| CT-D6 | 跨主题继承 | `<Theme name="dark" base="light">`，token 缺失沿 base 链回溯 | 同 CSS `:root` + `.dark` override 心智；新增暗主题不强迫补齐所有 token |
| CT-D7 | 字面值兼容 | token 没命中 → fall back `ColorUtility.TryParseHtmlString` | 老 `.ui.xml` 不需要改；混用 token 与 hex 自由 |
| CT-D8 | shadow 顺序 | theme token > hex / 命名色字面 | token 是显式声明，字面是 ad-hoc；token 优先符合"声明覆盖默认"直觉。CSS 命名色被影子在游戏 UI 几乎无影响 |
| CT-D9 | 解析失败行为 | 抛 `ParseException`，带节点上下文 | 顺带修 §8 描述的"静默 no-op" bug；Variant override 错值也立刻报错 |
| CT-D10 | setter 签名 / 解析点 | setter 保持 `string`；内部直接 `UI.Theme.Resolve(value)`；`IsColor=true` flag 用于 lint / 工具静态发现 color attr | 与 `IsSprite` / `UI.ResolveSprite` 既有惯例一致（`Image.cs:35` 等），applier 不加新分支；错误诊断走 `meta.Apply` 现有的 `TargetInvocationException` 解包 + `ApplyOne` 节点上下文包装 |
| CT-D11 | `Animation.char-color` | 不挂 `IsColor=true`（值是 `from:to` 二元）；`AnimationSpec.SetCharColor` 内部对 `from:to` 两段各自调 `UI.Theme.Resolve` | `char-color="primary:secondary"` 不是单值；但单段解析失败诊断信号与 `Image.Color` 一致 |
| CT-D12 | 多主题项目初始 `Current` | 多主题 → `Current == null`（首次 `Set` 前 color attr 走字面）；单主题 → 自动设为该唯一主题 | 多主题项目必须显式选；单主题项目零配置可用 |
| CT-D13 | `Theme.Changed` 触发条件 | `Set(name)` 切换 **以及** hot reload 替换了当前主题表里任一 token 值 | 改 XML 即时生效，不需要手动切回切去 |
| CT-D14 | token 命名空间 | flat name；`name` 字符集 `[a-z0-9-]`（kebab-case） | 简单；后续要分组（`button.primary`）再开 `<Group>` 包装，本期 YAGNI |
| CT-D15 | `<Color>` value 校验 | 加载时立刻 `TryParseHtmlString` 校验，非法 → parse error | 早爆错；运行时 `Lookup` 拿到的永远是合法 `Color` |
| CT-D16 | 跨文档同名冲突 | `<Theme name="dark">` 来自两个 src → parse error | 与 Template `(ns, name)` 冲突语义一致 |
| CT-D17 | `base` 校验 | 加载完所有 commons + imports 后统一验证：base 存在、无环；失败 → parse error | 跨文档场景下早期校验不可能（base 可能在另一个文件），延后到"注册阶段结束"的 hook |

---

## 3. XML 语法

### 3.1 `<Theme>` 块

`<Theme>` 是 `.ui.xml` 顶层元素，与 `<Screen>` / `<Templates>` / `<Import>` 同级：

```xml
<UIDocument>
  <Theme name="light">
    <Color name="primary"   value="#ff8800"/>
    <Color name="secondary" value="#0080ff"/>
    <Color name="label-fg"  value="#222222"/>
    <Color name="bg"        value="#f5f5f5"/>
  </Theme>

  <Theme name="dark" base="light">
    <Color name="primary"  value="#cc6600"/>
    <Color name="label-fg" value="#e6e6e6"/>
    <Color name="bg"       value="#1a1a1a"/>
    <!-- secondary 未声明 → 从 base="light" 继承 #0080ff -->
  </Theme>
</UIDocument>
```

约束：

- `<Theme name="...">` 必须有 `name`；缺 → parse error。
- `<Theme>` 可选 `base="other-theme-name"`；引用必须在所有 commons + imports 加载完成后能找到（CT-D17）。
- 同文档内 `<Theme>` 重名 → parse error。
- 跨文档 `<Theme>` 重名（commons + Screen import 合并阶段） → parse error，消息提示两个 src。
- `<Theme>` 子元素只接受 `<Color>`；其他标签 → parse error。

### 3.2 `<Color>` 节点

```xml
<Color name="primary" value="#ff8800"/>
```

约束：

- `name` 必须，字符集 `[a-z0-9-]`（kebab-case）；含大写、下划线、点 → parse error（CT-D14）。
- `value` 必须，能被 `ColorUtility.TryParseHtmlString` 接受（`#rgb` / `#rrggbb` / `#rrggbbaa` / CSS 命名色）；非法 → parse error（CT-D15）。
- 同 `<Theme>` 内 `<Color>` 重名 → parse error。

### 3.3 引用

```xml
<Image color="primary"/>
<Text  color="label-fg" text="Hi"/>
<Btn   color="primary"  label="Buy"/>
```

解析顺序（在 `ControlAttributeApplier`）：

1. 若 `UI.Theme.Current != null`：在 `Current` 主题查 token
2. 命中 → 用该 `Color`
3. 没命中 → 沿 `base` 链回溯（dark → light → ...）
4. 没命中 → `ColorUtility.TryParseHtmlString(value)` 走字面解析
5. 字面也失败 → `throw ParseException("attribute color=\"xxx\": unknown color token (no theme entry, not a valid hex/named literal)")`
6. `Theme.Current == null` 时：跳过 1–3，直接走 4–5

### 3.4 与 Variant 互动

Variant override 写 token 名或字面值一视同仁：

```xml
<Image id="btn-bg" color="primary">
  <Variant when="alert" color="#ff0000"/>     <!-- 字面 -->
  <Variant when="sale"  color="secondary"/>   <!-- token -->
</Image>
```

`VariantResolver.ResolveAttribute` 给 applier 一个 raw string，applier 走 §3.3 流程。Variant 状态切换走 `VariantStore.Changed → ReSolve`，主题状态切换走 `Theme.Changed → ReSolve`，两条路径独立但都汇到同一个解析器。

---

## 4. C# API

### 4.1 注册（不引新 loader）

```csharp
// 启动时一次，主题块跟 Templates 一起进 commons 池
await UI.LoadCommonLibraryAsync("themes/main");

// 或在某个 Screen 的 .ui.xml 里：
// <Import src="themes/main"/>
// 那一刻 themes/main.ui.xml 里的 <Theme> 块也被注册
```

`DocumentLoader.LoadAndMerge` 解析完文档后，把 `<Theme>` 块写进新增的 `_themeRegistry`（`ThemeStore.Instance`），与 `_commonsPool` 同生命周期。Imports 链上重复的 `<Theme name>` → parse error（CT-D16）。

### 4.2 公共 API

```csharp
namespace PromptUGUI.Application;

public static partial class UI
{
    public static class Theme
    {
        public static string Current { get; }                          // 当前主题名；未设置时为 null
        public static IReadOnlyCollection<string> Available { get; }   // 已注册全部 theme name

        public static event Action<string> Changed;                    // (newName) — 切换 + hot reload 都触发

        public static void Set(string name);                           // 切换；未注册 → ArgumentException
        public static Color? Lookup(string token);                     // 程序化查；含 base 回溯；Current==null 或未命中 → null
    }
}
```

### 4.3 初始 `Current` 解算（CT-D12）

`LoadCommonLibraryAsync` 注册完所有主题后：

- `Available.Count == 0`：`Current` 保持 null。color attr 走字面解析。
- `Available.Count == 1`：`Current` 自动设为该唯一主题，触发一次 `Changed`。零配置体验。
- `Available.Count >= 2`：`Current` 保持 null。color attr 走字面解析直到首次 `Set`。

### 4.4 切换

```csharp
UI.Theme.Set("dark");
// → 触发 Theme.Changed("dark")
// → Screen（在 ctor 订阅了 Theme.Changed）调 ReSolve()
// → 所有打开的 Screen 上每个 IsColor=true 的 attr 重解析并 setter
```

### 4.5 Hot reload（CT-D13）

`Editor/UIAssetPostprocessor.cs` 已经接管 `.ui.xml` 改动：

- 改的文件是 commons 库（`UI.HotReload.NotifyAssetChanged(src)` 走 `ReloadCommonLibraryAsync`）：替换 `ThemeStore` 该来源贡献的全部 `<Theme>` 块；若 `Current` 主题的 token 值变了 → 触发 `Theme.Changed(Current)`。
- 改的文件是 Screen src：常规 `ReloadAsync(src)` 跑完后，如该 Screen 重新计算的 `_themeRegistry` 影响了 Current 主题 → 同样触发 `Theme.Changed`。

---

## 5. 控件层改造

### 5.1 `[UIAttr]` 新增 `IsColor` flag

`Runtime/Registry/UIAttr.cs`：

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class UIAttrAttribute : Attribute
{
    public string Name { get; }
    public bool IsSprite { get; set; }
    public bool IsColor  { get; set; }     // 新增
    public UIAttrAttribute(string name = null) { Name = name; }
}
```

`ControlMeta` 暴露 `IsColorAttribute(string attrName) -> bool`，applier 拦截分支用。

### 5.2 setter 改造（CT-D10）

setter 签名保持 `string`（与 `IsSprite` 既有惯例一致 —— 见 `Image.cs:35-39` 的 `Sprite` 走 `UI.ResolveSprite(value)`）；只是把"过 `TryParseHtmlString` 静默落地"换成"过 `UI.Theme.Resolve(value)` 失败就抛"：

```csharp
// Image.cs
[UIAttr(IsColor = true), Preserve]
public string Color { set => _img.color = UI.Theme.Resolve(value); }

// Text.cs
[UIAttr(IsColor = true), Preserve]
public string Color { set => _tmp.color = UI.Theme.Resolve(value); }

// Btn.cs
[UIAttr(IsColor = true), Preserve]
public string Color { set => _bg.color = UI.Theme.Resolve(value); }
```

`UI.Theme.Resolve(string) -> Color`：

```csharp
public static class Theme
{
    public static Color Resolve(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new Exception("empty color value");

        if (Current != null)
        {
            var hit = ThemeStore.Instance.LookupChained(Current, value);
            if (hit.HasValue) return hit.Value;
        }
        if (ColorUtility.TryParseHtmlString(value, out var c))
            return c;
        throw new Exception(
            $"unknown color token \"{value}\" (no entry in theme " +
            $"'{Current ?? "(none)"}', not a valid hex/named literal)");
    }
}
```

`IsColor=true` flag 由 `ControlMeta` 在反射阶段记录到 `ColorAttrs` 集合（与 `SpriteAttrs` 同模式），供 lint / 静态工具发现 color attr 集合用 —— **runtime 解析路径不读这个 flag**，setter 直接调 `UI.Theme.Resolve`。

### 5.3 错误诊断如何串起来

`UI.Theme.Resolve` 抛 `Exception` → 反射 setter 包成 `TargetInvocationException` → `ControlMeta.Apply` 现有解包逻辑（`ControlMeta.cs:33-38`）剥一层 → `ControlAttributeApplier.ApplyOne` 现有 `try/catch`（`ControlAttributeApplier.cs:105-111`）把节点上下文包上：

```
<Image id='avatar'> attribute color="primaru": unknown color token "primaru"
(no entry in theme 'light', not a valid hex/named literal)
```

不需要在 applier 加新分支；走完全跟 `IsSprite` / `Image.Sprite` 一致的既有通路。这是相对早先 spec 草案的精简（早先打算让 applier 拦截 color attr 单独路径；与项目惯例不符，且 `ApplyOne` 已经够用）。

### 5.4 `Animation.char-color` 特殊处理（CT-D11）

`Animation.CharColorAttr` setter 不挂 `IsColor=true`（它的 string 是 `from:to` 二元，不能整段过 `UI.Theme.Resolve`）。改 `AnimationSpec.SetCharColor` 内部对两段分别 resolve：

```csharp
// AnimationSpec.cs
public void SetCharColor(string v)
{
    ParseColorFromTo(v, out CharColorFrom, out CharColorTo);
    HasCharColor = true;
}

private static void ParseColorFromTo(string v, out Color from, out Color to)
{
    var parts = v.Split(':');
    if (parts.Length != 2)
        throw new Exception($"char-color=\"{v}\": expected 'from:to'");
    from = UI.Theme.Resolve(parts[0]);
    to   = UI.Theme.Resolve(parts[1]);
}
```

`char-color="primary:secondary"` 可以工作；`char-color="primary:#ff0000"` 可以混用；任意一段失败 → `UI.Theme.Resolve` 抛 → setter 抛 → `ApplyOne` 既有 try/catch 包成 `<Animation> attribute char-color="primary:#bagval": ...` 带节点上下文的 `ParseException`。

主题切换不重跑 `Animation` 关键帧（动画已经在播了，重算 from/to 的语义不清楚）；下一次 `<Animation>` Apply 时按新 token 值。Spec 显式声明这个边界，避免后续争议。

---

## 6. "解析失败静默 no-op" bug 修复

### 6.1 现状

`Runtime/Controls/Image.cs:42` / `Text.cs:88` / `Btn.cs:100`：

```csharp
public string Color {
    set {
        if (string.IsNullOrEmpty(value)) return;
        if (ColorUtility.TryParseHtmlString(value, out var c))
            _img.color = c;
        // ← 没有 else 分支
    }
}
```

`TryParseHtmlString` 返回 `false` → setter 直接 fall through。控件颜色保持上次的值（procedural 默认 / 上次 Apply 写入 / Unity Image 默认白）。无诊断输出。

最阴的场景是 Variant override：

```xml
<Image id="bg" color="#ff0000">
  <Variant when="dark" color="#bagval"/>   <!-- 笔误，非法 -->
</Image>
```

切到 dark variant 后 `Screen.ReSolve` 走到 `Image.Color setter`，`#bagval` 解析失败 → no-op → 控件保持 `#ff0000`。整个生命周期里 dark 分支静悄悄不生效，作者看不出来。

### 6.2 修复

随 §5 改造一起：

- 三处 setter 改为 `set => _x.color = UI.Theme.Resolve(value);`；`if (TryParse) ...` 静默守门全部删掉。
- `UI.Theme.Resolve` 解析失败必抛（token 没命中 + 字面也无效 → `Exception`）。
- `meta.Apply` 反射解包 + `ApplyOne` 现有 `try/catch` 把异常包成 `<Image id='bg'> attribute color="#bagval": unknown color token "#bagval" ...` 带节点位置的 `ParseException`。

### 6.3 破坏性变更声明

现有 `.ui.xml` 里若**真的**写过非法 hex（如 `color="#ff800"` 漏一位），从静默升级成 parse error。

Migration：
- 实施 PR 里附带一次 `Runtime/Resources/` 全量 grep + 内部项目 `.ui.xml` lint pass，确认无现存非法 hex。
- `UIXmlLint` CLI 新增规则 `color-literal-must-parse`（独立于 token 的纯静态字面校验），让作者本地能跑出来。

---

## 7. 与现有系统的交互

### 7.1 Variants

§3.4 已说明。Variant override 的 token / 字面值都过 `ColorResolver`；解析错的 variant 切换瞬间立刻 throw（取代当前的"切了但没换色"沉默失败）。

### 7.2 Locale

无直接耦合。`Theme.Changed` 与 `Locale.Changed` 各自触发 `Screen.ReSolve`；同一 ReSolve pass 里 color attr 走 `ColorResolver`，text attr 走 `TrResolver`，互不影响。

### 7.3 Hot reload

§4.5 已说明。`DepGraph` 把 `.ui.xml` 文件 → 它贡献的 `<Theme name>` 集合 也建索引；hot reload 替换主题表时按这个索引精准更新 `ThemeStore`。

### 7.4 `LoadCommonLibraryAsync` / `<Import>`

主题块的合并规则跟 Templates 完全平行：

| 来源 | 注册到 | 冲突 |
|---|---|---|
| `LoadCommonLibraryAsync(src)` 文档里的 `<Theme>` | `ThemeStore`（commons 部分） | 跨 commons src 同名 → parse error |
| Screen src 自身的 `<Theme>` | `ThemeStore`（screen-scoped 部分，与 commons 合并） | 与 commons / 其他 Screen import 链上同名 → parse error |
| Screen `<Import src=...>` 拉入文档里的 `<Theme>` | 同 Screen src 行为 | 同上 |

CT-D17：`base` 引用的校验延后到注册阶段最后做（commons 池所有主题 + Screen 临时合并主题都到位之后）。

### 7.5 EditMode 测试

`UI.ResetForTests()` 现状是把 commons 池 / locale store 全清，主题改造后需要追加：

- `ThemeStore.Instance.Clear()`
- `Theme.Current` 重置为 null
- `Theme.Changed` 订阅清空

测试用例统一在 `[SetUp]` / `[TearDown]` 通过 `UI.ResetForTests()` 重建。

---

## 8. 错误诊断

| 场景 | 异常类型 | 错误消息样例 |
|---|---|---|
| `<Theme>` 缺 `name` | ParseException | `<Theme>: missing required attribute 'name'` |
| `<Color>` 缺 `name` 或 `value` | ParseException | `<Color value="#ff8800">: missing required attribute 'name'` |
| `<Color value>` 非法 | ParseException | `<Color name="primary" value="#xyz">: invalid color literal` |
| token name 非 kebab-case | ParseException | `<Color name="primaryColor">: token name must be kebab-case [a-z0-9-]` |
| 同 Theme 内 token 重名 | ParseException | `<Theme name="light"> declares 'primary' twice` |
| 跨文档 Theme 重名 | ParseException | `duplicate <Theme name="dark"> in 'themes/main' and 'themes/extra'` |
| `base` 不存在 | ParseException | `<Theme name="dark" base="brigth">: base theme 'brigth' not found (did you mean 'bright'?)` |
| `base` 成环 | ParseException | `<Theme> base cycle: dark → night → dark` |
| color attr 无法解析 | ParseException（applier 包装） | `<Image id='avatar'> attribute color="primaru": unknown color token (no entry in theme 'light', not a valid hex/named literal)` |
| `UI.Theme.Set("nope")` 未注册 | ArgumentException | `UI.Theme.Set: theme 'nope' not registered (available: light, dark)` |
| `UI.Theme.Lookup` 失败 | 不抛，返回 `null` | （程序化 API；调用方决定怎么处理） |

---

## 9. SKILL.md 更新

按 CLAUDE.md「Triggers requiring a SKILL update」：

### 9.1 `authoring-promptugui-xml/SKILL.md`

- 顶层元素列表加 `<Theme>` 行
- 新增 §「色 token」：`<Theme>` / `<Color>` 语法、`base` 继承、shadow 规则
- `color` 属性说明从"hex / 命名色字面值"扩展为"token > 字面"两步解析
- 错误清单追加 §8 表的相关条目

### 9.2 `scripting-promptugui-csharp/SKILL.md`

- `UI.*` 列表追加 `UI.Theme.Current` / `Available` / `Set` / `Lookup` / `Changed`
- 自定义控件章节追加 `[UIAttr(IsColor = true)]` 的指南，setter 收 `Color` 不是 `string`
- 提示 `UI.ResetForTests` 在新版本会清主题状态（影响有自建主题状态的测试）

### 9.3 `using-promptugui-addressables/SKILL.md`

**不更新**。主题表通过 `LoadCommonLibraryAsync` 走，与现有 Addressables resolver 通道无新交互（commons 库本身可以是 Addressables 资源，但那是 §M 已有能力）。

---

## 10. 影响文件

| 类型 | 文件 |
|---|---|
| 新增 | `Runtime/Application/ThemeStore.cs`（含 `LookupChained`、cycle 校验） |
| 新增 | `Runtime/Core/IR/ThemeBlock.cs`、`Runtime/Core/IR/ColorEntry.cs`（POCO） |
| 改 | `Runtime/Core/Parser/UIDocumentParser.cs` — 识别 `<Theme>` / `<Color>` |
| 改 | `Runtime/Application/DocumentLoader.cs` — themes 进 `ThemeStore`、跨文档冲突、base 校验 |
| 改 | `Runtime/Application/UI.cs` — `UI.Theme` 子类（`Current` / `Available` / `Set` / `Lookup` / `Resolve` / `Changed` / `ResetForTestsInternal`） |
| 改 | `Runtime/Application/Screen.cs` — 订阅 `Theme.Changed` |
| 改 | `Runtime/Registry/UIAttrAttribute.cs` — `IsColor` flag |
| 改 | `Runtime/Registry/ControlMeta.cs` — `ColorAttrs` 集合（与 `SpriteAttrs` 同模式） |
| 改 | `Runtime/Controls/Image.cs` / `Text.cs` / `Btn.cs` — `[UIAttr(IsColor=true)]` + setter 改调 `UI.Theme.Resolve` |
| 改 | `Runtime/Controls/Internal/AnimationSpec.cs` — `ParseColorFromTo` 改走 `UI.Theme.Resolve` |
| 改 | `Editor/XsdGenerator.cs` — 输出 `<Theme>` / `<Color>` XSD |
| 改 | `Editor/UIAssetPostprocessor.cs` — 主题块 hot reload 路径 |
| 新增 lint | `Runtime/Core/Lint/ColorLiteralRules.cs` —「现场非法 hex 字面」纯静态规则 |
| 新增测试 | `Tests/EditMode/Theme/` — parser、resolver、shadow、base 继承、cycle、hot reload、ReSolve |
| 新增测试 | `Tests/PlayMode/Theme/` — runtime `Set` 切换 + 已开 Screen 重涂 e2e |

---

## 11. 显式遗留 / 后续

- **非颜色 token**（字号、间距、圆角等）：本 spec 范围外。`IsColor` 是为后续 `IsSize` / `IsFont` 拓展铺路，结构一致但语义独立，分次 PR。
- **token namespace / 分组**：现在只支持 flat name。后续要分组（`button.primary` / `text.label`）可加 `<Group name="button">` 包装，本期 YAGNI。
- **lint：引用未声明的 token**：本期靠运行时 `ParseException` 兜底。未来 `UIXmlLint` 可静态扫 `color="..."` 引用对照 `ThemeStore`，加 warning。
- **ScriptableObject palette 桥接**（设计师在 Inspector 改色 → 自动写回 XML）：不在范围内；独立 PR。
- **`Animation` token 切换不重算关键帧**：CT-D11 的边界。如果后续发现项目里需要"主题切换瞬间正在播的动画也染色"，再开 ticket。
- **token 别名 / 计算**（`<Color name="primary-hover" derive="primary" lighten="10%"/>`）：CSS-in-JS 风格的 token 运算；本期不做。
