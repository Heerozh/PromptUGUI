# I18n + Fonts 设计：零 key gettext 流 + per-locale 字体表

**日期**：2026-05-08
**状态**：设计阶段（待 review，未进入实施）
**作用域**：多语言 / 字体子系统的 XML 语法增量、Settings 资产、IR 改动、运行时数据流、Editor 抽取与 LLM 翻译菜单、与 Variant 系统的集成、与 hot-reload 的集成、主 spec 同步
**依赖**：基础设计 [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §7（内置控件）/ §8（Variant）/ §10（v1 非目标 — 本 spec 推翻其中 "本地化" 一条）；Icon 设计 [`2026-05-08-icon-assets-design.md`](2026-05-08-icon-assets-design.md)（Settings asset 持久化模式参考）

---

## 1. 背景与目标

像素游戏项目要从单语种铺到多语种发布。常见痛点：

- **改字符串成本高**：传统方案要先定 key（"menu.start"），再写代码 / XML 引用 key，再去翻译表填值；改 UI 文案要同步动三处
- **CJK ↔ 拉丁字体**：同一控件在不同 locale 需要不同 TMP_FontAsset（中文要带汉字字模的字体，英文用拉丁字体更紧凑），手工管理容易漏
- **翻译流程**：要翻一长段文本，传统 .po 配 xgettext 很重，体验割裂

PromptUGUI 已有 Variant 系统（每个 Variant 触发已 open Screen 的 ReSolve），是多语言切换的天然载体。

**本 spec 解决的目标**：

1. **零 key**：作者在 .ui.xml / C# 里直接写源文本字面量；msgid = 字面量；改文案不动 key
2. **混合源语言**：作者可以中英文混写，没有 sourceLocale 概念，LLM 时代翻译模型不需要它
3. **runtime 字体表**：Settings 里每 locale 配 fontType → TMP_FontAsset 表；XML 用 `font="title"` 这种 type 名引用
4. **图文混排**：源文本可包含 TMP 富文本标签（`<sprite>` / `<color>` 等）；翻译流程保留它们
5. **LLM 翻译菜单**：OpenAI 兼容 API + Structured Outputs，自动为空 msgstr 翻译
6. **复用 Variant 通路**：locale 切换 = `Variants.Set(<locale>, true/false)`，省一套独立的 Locale.Changed 事件

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| I18N-D1 | 数据流 | 独立 TranslationStore + 复用 `Variant.Changed` | translations 不进 IR 避免污染；切语言走 ReSolve 复用既有通路 |
| I18N-D2 | 文件格式 | gettext .po | 业界标准；msgctxt / 注释一等公民；LLM 提示效果好；git diff 友好 |
| I18N-D3 | msgctxt 语义 | 默认仅注释；显式 ctx 才进 lookup | XML 重命名 id 不丢翻译；同 msgid 多义可主动消歧 |
| I18N-D4 | 字体语法 | Text/Btn 上 `font="<type>"`，缺省 type=`default` | type 名而非 asset 路径，与 locale 解耦；fallback 自然 |
| I18N-D5 | 文件分片 | per src 镜像路径 + 独立 i18n-custom 目录 | hot reload 粒度自然；用户手写表与抽取产物物理隔离 |
| I18N-D6 | `{{...}}` 处理 | 抽取模板原文作 msgid；runtime 在 msgstr 上反向插值 | 零 author 心智；Template 静态文本也能翻 |
| I18N-D7 | C# 抽取 | Roslyn syntactic walker | Unity 自带；上下文（函数名、注释）齐全；动态参数可识别并 warn |
| I18N-D8 | XML opt-out | `tr="false"` | 隐式抽取 + 显式排除；C# 反过来：调用 `UI.Tr` 才抽 |
| I18N-D9 | Settings 持久化 | PlayerSettings.preloadedAssets + AssetPostprocessor | Unity Localization 同款机制；零 Resources/ 污染 |
| I18N-D10 | locale 列表 | 隐含从 `Settings.locales` 推导 | 单一字段不冗余 |
| I18N-D11 | 默认 locale 启动初始化 | 自动从 `Application.systemLanguage` 推导；不命中 `Configured` 时回退到 `Configured[0]` 并 `LogWarning("丢失 'X', falling back to 'Y'")`无Config `null`（全 miss → msgid 原样） | 玩家开箱即用；命中失败要让开发者第一时间在 console 看到，避免静默用错语言 |
| I18N-D12 | fallback chain | 无 | LLM 时代不需要；miss 直接返 msgid |
| I18N-D13 | AI 配置 | endpoint/model/prompt 进 ProjectSettings；apiKey 进 UserSettings | 团队共享 + key 不进库 |
| I18N-D14 | 触发节奏 | 手动 Extract / 手动 Translate 两个菜单 | 抽取幂等可频繁跑；翻译付费 LLM 需显式 |
| I18N-D15 | 复数 | v1 不上 | 现代 LLM 翻译能接受 `"{0} coins"` 单形式 |
| I18N-D16 | 过时 msgid | 直接删除 | 用户决定；依赖 git 历史与 LLM 重译能力 |
| I18N-D17 | Settings 位置 | 默认 `Assets/PromptUGUI/Settings.asset`，用户可拖动 | 项目级共享，preloadedAssets 自动维护 |
| I18N-D18 | 表优先级 | i18n-custom 覆盖 i18n | 手写是逆水身，永远高于自动抽取 |
| I18N-D19 | TMP 富文本 | CDATA 内嵌；改 parser 一处把 CDATA 计入 textContent | 标准 XML；mixed-content 禁令保留 |
| I18N-D20 | locale 作 Variant 名 | 直接用 locale 字符串作 Variant 名（无 `_locale:` 前缀） | 与 `font.zh-Hans="..."` 这种自定义 .var 写法天然吻合 |

---

## 3. 总体架构

```
                     Author
                       │
     ┌─────────────────┼─────────────────┐
     │                 │                 │
  *.ui.xml         *.cs UI.Tr()      手写 .po
                       │                 │
            ┌──────────┴──────┐          │
            │  Editor 抽取菜单 │          │
            └──────────┬──────┘          │
                       ▼                 │
             Resources/PromptUGUI/       │
             ├─ i18n/<locale>/...po  ◀──┘
             │  (auto-managed)
             └─ i18n-custom/<locale>/...po
                       │
                       │ ┌─ Editor 翻译菜单 (LLM)
                       │ │   填空 msgstr
                       ▼ ▼
                 .po (有译文)
                       │
        ┌──────────────┴──────────────┐
        │                             │
   PromptUGUISettings           UI.Locale.Set()
   (locales / fonts)                  │
        │                             ▼
        │              内部 Variants.Set("<locale>", true/false)
        │                             │
        │              ┌──────────────▼──────────────┐
        │              │ 已 open Screen.ReSolve()     │
        │              └──────────────┬──────────────┘
        │                             │
        ▼                             ▼
TranslationStore<locale, table>   ApplyCommon
        │                             │ 走 TrResolver
        └──────────────────┬──────────┘
                           ▼
                        TMP_Text
```

两条可独立演进的轴：

- **i18n 轴**：作者写中/英/混合 → 抽取器扫成 .po → LLM 填 msgstr → runtime `Tr(msgid)` 查 store
- **字体轴**：Settings 里 `locales[<x>].fonts[<type>]` 配 TMP asset → `<Text font="title">` 通过 type 名解引

两轴交于 `UI.Locale.Set("zh-Hans")` 这一个 API：它同时切 store 和切 fonts，经由 Variants.Changed 一次性 ReSolve。

---

## 4. PromptUGUISettings 资产

**单一 ScriptableObject**，由用户拖在 `Assets/PromptUGUI/Settings.asset`（可拖到任意位置）。Editor `[InitializeOnLoad]` + `AssetPostprocessor` 维护：

- 项目内 ≤ 1 个 PromptUGUISettings 资产（>1 时 Editor 报错）
- 自动加进 `PlayerSettings.preloadedAssets`
- Runtime 通过 `Resources.FindObjectsOfTypeAll<PromptUGUISettings>().FirstOrDefault()` 拿到已加载实例

### 4.1 字段（v1）

```csharp
namespace PromptUGUI.Application {
    [CreateAssetMenu(menuName = "PromptUGUI/Settings", fileName = "Settings")]
    public sealed class PromptUGUISettings : ScriptableObject {
        [Serializable] public sealed class FontEntry {
            public string type;                 // "default" | "title" | "damage" | ...
            public TMP_FontAsset font;
        }
        [Serializable] public sealed class LocaleConfig {
            public string locale;               // BCP-47 推荐: "zh-Hans" / "en" / "ja"
            public List<FontEntry> fonts;       // 必含 type="default" 一项
            // 未来扩展: text direction, number format, ...
        }
        public List<LocaleConfig> locales;      // entries 决定"项目支持哪些 locale"
    }
}
```

### 4.2 校验规则（Editor 时）

- 多个 PromptUGUISettings 资产 → log error，preloadedAssets 不变
- 任一 LocaleConfig 缺 type=`default` → Inspector 标红，runtime fallback 到内置 TMP 默认字体并 log
- 重复 locale 名 → Inspector 标红
- 重复 fontType（同 locale 内）→ Inspector 标红

### 4.3 Runtime 行为

- 启动自动初始化（`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`）走 §6.4 `InitializeIfNeeded()` 算法：
  - `Configured` 为空 → 静默 noop，`Current` 保持 `null`（没法回退也没必要警告）
  - 系统语言映射到 BCP-47 且命中 `Configured` → `Set(it)`
  - 不命中（含映射返回 `null` 的未识别 SystemLanguage） → `LogWarning("[PromptUGUI] 丢失 'X', falling back to 'Y'")` 后 `Set(Configured[0])`，`X` 是 BCP-47 code 或 `SystemLanguage.{Enum}` 名
  - 该自动钩子只在 Player / Play Mode 触发；EditMode 测试不受影响
- 手动 `UI.Locale.Set("ko")`，`ko` 不在 `Settings.locales` 中：**接受**
  - fonts 走内置 TMP default
  - 加载 `Resources/PromptUGUI/i18n/ko/`（找不到就全 miss → msgid 原样输出）
  - **不 throw**：设计哲学是"宁愿不翻译也不阻塞"。该哲学只覆盖手工调 Set 的场景；自动初始化路径会回退（D11）

---

## 5. XML 语法增量

只新增 **三个属性**，都加在 Text/Btn 这种"文本输出"控件上。所有既有 .ui.xml **不需要任何改动**。

### 5.1 `font` 属性

```xml
<Text>开始游戏</Text>                <!-- font 缺省 = "default" -->
<Text font="damage" fontSize="96">9999!</Text>
<Text font="title">设置</Text>

<!-- 跟 Variant 系统天然咬合 -->
<Text font="title" font.zh-Hans="title-cn">设置</Text>
```

- 值为 fontType 名（对应 LocaleConfig.fonts.type），**不是** TMP asset 路径
- ResolveAttribute（Text 控件）时：`type` → 当前 locale 的 LocaleConfig.fonts → TMP_FontAsset → `_tmp.font = ...`
- 当前 locale 找不到该 type、找不到该 LocaleConfig、Settings 整体缺失 → 全部 fallback 到内置 TMP default，不 throw，每条字符串只 warn 一次

### 5.2 `tr` 属性

```xml
<Text>开始游戏</Text>                       <!-- 抽取 + 翻译 -->
<Text tr="false">{{playerName}}</Text>     <!-- 跳过抽取, runtime 原样 -->
<Btn>设置</Btn>                             <!-- 抽取 + 翻译 -->
<Btn tr="false" text="VIP12345"/>          <!-- 跳过 -->
```

- 仅生效于 Text/Btn 的 textContent 和 `text` 属性
- `tr="false"` 时，抽取器跳过、runtime TrResolver 也跳过，直接 set 原文
- 与 `ctx` 同时出现时 `tr="false"` 优先（既然不抽不查，ctx 无意义）

### 5.3 显式 `ctx` 的逃生口（罕用）

```xml
<Text ctx="door">Open</Text>          <!-- msgctxt="door" + msgid="Open" -->
<Btn ctx="file-menu">Open</Btn>       <!-- msgctxt="file-menu" + msgid="Open" -->
```

- 仅当多义性需要消歧时，author 主动写 `ctx`
- 抽取的 ambient context（screen path / sibling strings）始终走 `# comment` 注释，不进 msgctxt

### 5.4 CDATA 与 TMP 富文本

`<Text>金币: <sprite name="coin"/>{{count}}</Text>` 在当前 parser 是 mixed-content error。**支持方式：CDATA**：

```xml
<Text><![CDATA[金币: <sprite name="coin"/>{{count}}]]></Text>
<Btn><![CDATA[<sprite name="play"/> 开始]]></Btn>
<Text><![CDATA[<color=#ff0>警告</color>: 库存不足]]></Text>
```

- Parser 改动：`UIDocumentParser.cs:265` 的 hasText 检测把 `XmlCDataSection` 也计入。`el.InnerText` 已经把 CDATA 当 text 处理 — textContent 提取自动 work
- mixed-content 禁令仍然保留：`<Text>foo<sprite/>bar</Text>` 仍报错；想 inline 标签必须 CDATA 整体包
- 抽取/runtime/翻译三条路径**零改动**：都基于 string，CDATA 解开后跟普通 text 一样
- `{{count}}` 反向插值在 CDATA 解开后的字符串上走正则，完全不受影响
- entity 转义（`&lt;sprite/&gt;`）仍然作为 author 的隐式 fallback 保留

---

## 6. 运行时数据流

### 6.1 IR 改动

`ElementNode` 多两个字段：

```csharp
public class ElementNode {
    // ... 既有字段 ...
    public string TextContentRaw;                      // substitution 前的字符串 (含 {{...}})
    public Dictionary<string,string> TextArgs;         // template 实参 dict (substitution 时填)
    public Dictionary<string,string> AttributesRaw;    // attribute 上含 {{...}} 的原文
}
```

- 解析时：`TextContentRaw = el.InnerText`（不替换），`TextContent`（既有字段）用 substituted 后的值，**保留向后兼容**
- TemplateExpander 在 expand 调用站时，把实参 dict 拷贝到结果 node 的 `TextArgs`
- attribute 上含 `{{}}` 的同理：`Attributes[k]` 是 substituted，`AttributesRaw[k]` 是原文

### 6.2 TranslationStore

```csharp
namespace PromptUGUI.Application {
    internal sealed class TranslationStore {
        // (locale, msgctxt|null, msgid) → msgstr
        Dictionary<(string,string,string), string> _entries;

        public string Lookup(string locale, string ctx, string msgid);  // miss 返 null
        public void Load(string locale, IEnumerable<PoEntry> entries);
        public void UnloadLocale(string locale);
        public void UnloadAll();
    }
}
```

- `UI.Locale.Set("zh-Hans")` 时：枚举 `Resources/PromptUGUI/i18n/zh-Hans/` + `Resources/PromptUGUI/i18n-custom/zh-Hans/` 下所有 TextAsset → 解析成 PoEntry → Load
- i18n-custom 后载入，同 (locale, ctx, msgid) 覆盖 i18n（D18）
- 切 locale 时先 UnloadLocale 上一个，再 Load 新的

### 6.3 TrResolver

```csharp
namespace PromptUGUI.Application {
    internal static class TrResolver {
        public static string Resolve(
            string raw,
            IReadOnlyDictionary<string,string> args,
            string ctx) {

            if (raw == null) return null;
            var locale = UI.Locale.Current;
            var msgstr = locale != null
                ? TranslationStore.Instance.Lookup(locale, ctx, raw)
                : null;
            var template = msgstr ?? raw;                              // miss → 原样
            return args != null && args.Count > 0
                ? Substitution.Apply(template, args)                   // {{name}} 反向插值
                : template;
        }
    }
}
```

`ControlAttributeApplier` 在 apply textContent 与 `text` 属性之前调用 `TrResolver.Resolve(node.TextContentRaw, node.TextArgs, node.Attributes.GetValueOrDefault("ctx"))`。`font` 属性的 type → asset 解析也插在这一步。

### 6.4 UI.Locale API

```csharp
namespace PromptUGUI.Application {
    public static partial class UI {
        public static class Locale {
            public static string Current { get; private set; }
            public static event Action Changed;

            public static void Set(string locale) {
                if (Current == locale) return;
                if (Current != null) Variants.Set(Current, false);
                TranslationStore.Instance.UnloadAll();
                Current = locale;
                if (locale != null) {
                    LoadPoFiles(locale);
                    Variants.Set(locale, true);  // 触发 Variants.Changed → ReSolve
                } else {
                    VariantStore.NotifyChangedInternal();  // null 时也广播
                }
                Changed?.Invoke();
            }

            public static void SetToSystemDefault(string fallback = null);
            public static UnityEngine.Awaitable SetToSystemDefaultAsync(string fallback = null);

            // 启动自动初始化（[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] 调用此方法）。
            // 也可被 app 手动调用作为兜底初始化。语义见 §4.3 / D11。
            public static void InitializeIfNeeded();
            public static IReadOnlyList<string> Configured { get; } // Settings.locales 的 keys
        }

        public static string Tr(string msgid, string ctx = null) =>
            TrResolver.Resolve(msgid, null, ctx);
    }
}
```

`Application.systemLanguage` → BCP-47 映射（硬编码简表）：
- ChineseSimplified → "zh-Hans"
- ChineseTraditional → "zh-Hant"
- English → "en"
- Japanese → "ja"
- Korean → "ko"
- 其它常见 SystemLanguage 各对应；未识别返 null

`SetToSystemDefault(fallback = null)` 是低阶 API：

1. 若 `MapSystemLanguage(systemLanguage)` 返回非 null 且命中 `Configured` → `Set(命中的)`
2. 否则若 `fallback != null` → `Set(fallback)`（即使 `fallback` 不在 `Configured` 中也接受 — 与 `Set` 一致的"信任 caller"立场）
3. 否则 → `Set(MapSystemLanguage(systemLanguage))`（保留"宁愿不翻译也不阻塞"逃生口；映射返回 null 时 `Current` 留为 null，所有 msgid 透出原文）

跟 `InitializeIfNeeded()` 不同：`SetToSystemDefault` 不发警告，因为 (a) `fallback` 是 caller 显式选择，不是降级；(b) 不命中且无 fallback 的"显示原文"路径是 spec §6.4 文档化的逃生口，不是错误。

`SetToSystemDefaultAsync(fallback = null)` 走相同的 resolution 逻辑，但通过 `SetAsync` 让 caller 可以 `await` PO 加载完成（典型用法：登录闪屏想等翻译就位再过场）。

可测性：内部 `SetToSystemDefaultCore(SystemLanguage, IReadOnlyList<string>, string)` / `SetToSystemDefaultAsyncCore(...)` 接受参数注入，共用纯函数 `ResolveSystemDefault(...)`；公共方法读 `Application.systemLanguage` + `Configured` 转发。

`InitializeIfNeeded()` 是高阶启动初始化 API（D11）：
1. 若 `Current != null` → 直接返回（已经被 app / 之前的初始化设过）
2. 若 `Configured.Count == 0` → 静默返回（没有任何配置 locale 时 PromptUGUI 不发表意见）
3. 否则把 `Application.systemLanguage` 经 `MapSystemLanguage` 映射成 BCP-47：
   - 命中 `Configured` → `Set(命中的)`
   - 不命中（含映射返回 `null`） → `Debug.LogWarning("[PromptUGUI] 丢失 'X', falling back to 'Y'")` 后 `Set(Configured[0])`

PromptUGUI runtime 内部用 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` 在 Player / Play Mode 启动时自动跑一次 `InitializeIfNeeded()`。app 之后调 `UI.Locale.Set(savedPreference)` 会自然覆盖（含 PO 表 unload / reload）。可测性：内部 `InitializeIfNeededCore(SystemLanguage)` 接受参数注入，公共 `InitializeIfNeeded()` 读 `Application.systemLanguage` 转发。

### 6.5 locale 名作为 Variant 名

`UI.Locale.Set("zh-Hans")` 内部调用 `Variants.Set("zh-Hans", true)`。这意味着：

- author 写 `font.zh-Hans="title-cn"` 在 Locale.Set("zh-Hans") 后自动生效
- author 写 `<Variant when="zh-Hans">` 也自动咬合 — 用同一个 Variant 即可表达"语言激活时的额外结构"
- 反过来：author 不能用一个 Variant 名同时表达 locale 之外的某种状态（locale 名是保留的）

### 6.6 .po 加载策略

- LoadPoFiles 用 `Resources.LoadAll<TextAsset>("PromptUGUI/i18n/<locale>")` 与 `"PromptUGUI/i18n-custom/<locale>"` 枚举所有 TextAsset
- 解析每个 TextAsset.text → IEnumerable<PoEntry>
- 总文件量预期 < 1MB（KB 级），全量加载到内存可接受
- 依赖 §7.1 的 PoFileImporter 让 .po 在 Unity 中以 TextAsset 形态存在

### 6.7 VariantStore 改动

新增 `internal void NotifyChangedInternal()`（不改激活集合，仅广播 Changed），用于 `UI.Locale.Set(null)` 时让 Screen.ReSolve。其它场景（如 SettingsAsset fonts 改动）也复用此 hook。

---

## 7. .po 词法分析器与扩展名识别

### 7.1 ScriptedImporter

Unity 默认对 `.po` 扩展名不识别为 TextAsset，会作为 `DefaultAsset` 处理 — 这样 `Resources.LoadAll<TextAsset>` 拿不到。需要在 `Editor/PoFileImporter.cs` 加：

```csharp
[ScriptedImporter(version: 1, ext: "po")]
public sealed class PoFileImporter : ScriptedImporter {
    public override void OnImportAsset(AssetImportContext ctx) {
        var text = File.ReadAllText(ctx.assetPath);
        var asset = new TextAsset(text);
        ctx.AddObjectToAsset("text", asset);
        ctx.SetMainObject(asset);
    }
}
```

这让 .po 文件在 Unity 里以 TextAsset 形态存在；gettext 兼容工具（编辑器扩展、外部 .po 编辑器、`msgfmt` 等）仍按标准 .po 识别。

### 7.2 PoParser

放 `Runtime/Core/I18n/PoParser.cs`：

```csharp
namespace PromptUGUI.I18n {
    public sealed class PoEntry {
        public string Msgctxt;   // null = 无 ctx
        public string Msgid;
        public string Msgstr;    // 空字符串 = 未翻译
        public List<string> TranslatorComments;
    }

    public static class PoParser {
        public static IEnumerable<PoEntry> Parse(string source);
        public static string Serialize(IEnumerable<PoEntry> entries);
    }
}
```

支持范围：
- msgid / msgstr / msgctxt
- 多行字符串拼接：`"foo "\n"bar"` → `foo bar`
- `\n` `\t` `\\` `\"` 转义
- `# ...` 翻译者注释，`#: ...` 引用注释，`#. ...` 抽取者注释
- v1 不支持 plurals (msgid_plural / msgstr[N])

约 200 行，纯字符串扫描，0 第三方依赖。

---

## 8. 抽取工具

### 8.1 入口

`Tools → PromptUGUI → Extract Strings`（menu item）。手动触发，不走 AssetPostprocessor（D14）。

### 8.2 XML 扫描

复用既有 `UIDocumentParser`（在 expansion 前的 IR 上跑）：

- 遍历 `Assets/**/*.ui.xml`（走 AssetDatabase 列举）
- 对每个文件：解析得到 raw IR（parse 前 / template expansion 前）
- 走每个 ElementNode：
  - tag ∈ {Text, Btn} 且没有 `tr="false"` 时：
    - 收集 `TextContentRaw`（若非空且非纯 `{{x}}` 单变量）
    - 收集 `Attributes["text"]`（若存在，走同上规则）
  - 含 `{{x}}` → msgid 保留 `{{}}` 占位符
  - **纯**变量场景 `<Text>{{x}}</Text>` 或 `text="{{x}}"`：跳过 + warn（无翻译价值）
- 收集 ambient ctx：`<screen-name>/<id-path>:<attr>`（仅作 `#. ` 抽取者注释）
- 收集 sibling strings：同父 Text/Btn 的 raw text（前 3 个，去重）
- 显式 `ctx="..."` 进 msgctxt
- 检测 msgid 含 TMP 富文本标签 (`<sprite>` / `<color>` / `<b>` / `<i>` / `<size>` / `<link>`) → 输出 `#. Contains TMP rich text tags. Preserve tags and attribute values verbatim.` 注释

### 8.3 C# 扫描

Roslyn syntactic walker：

- 遍历 `Assets/**/*.cs`，跳过 Tests asmdef（不抽取测试代码）
- 解析 `SyntaxTree`，Walker 寻找 InvocationExpression：
  - `UI.Tr(StringLiteral)` → msgid = literal
  - `UI.Tr(StringLiteral, ctx: StringLiteral)` → msgid + msgctxt
  - 任一参数非 StringLiteral / interpolated string / `+` 拼接 → warn 并跳过该 call site
- ambient ctx：`<file>:<line>` + 包含调用的 method 名（写入 `#. in MethodName()`）
- 注释提取：调用语句正上方的 `// ...` 单行注释块（写入 `#. ...`）
- 引用：`#: <relative-path>:<line>`

### 8.4 输出

- XML 每个 src 一个 .po，镜像源路径：
  `Assets/UI/screens/MainMenu.ui.xml` → `Assets/Resources/PromptUGUI/i18n/<locale>/screens/MainMenu.po`
- 所有 C# 调用合并到 `Assets/Resources/PromptUGUI/i18n/<locale>/_code.po`
- 为 Settings.locales 中**每个 locale 都生成**一份（初次空 msgstr）
- 输出文件夹结构示例：

```
Assets/Resources/PromptUGUI/
  i18n/                    # 抽取工具管理
    zh-Hans/
      screens/MainMenu.po
      screens/Battle.po
      common/Buttons.po
      _code.po           # 所有 C# UI.Tr("...")
    en/
      screens/MainMenu.po
      ...
  i18n-custom/             # 用户手写，抽取工具不动
    zh-Hans/
      sfx-paths.po
      proper-nouns.po
    en/
      sfx-paths.po
```

### 8.5 增量与"消失 msgid"

- 已有 .po 中 msgstr 非空且 msgid 仍出现 → **保留**（不复 LLM 翻）
- msgid 不再出现 → **直接删除**（D16；用户依赖 git 历史找回）
- 新出现的 msgid → 添加，msgstr 为空

### 8.6 i18n-custom/ 不动

抽取器**永不**写 `Resources/PromptUGUI/i18n-custom/`。该目录 100% 由 author 手写。用于：
- AI 翻译不准确时的人工 override
- 非翻译字符串（wav 路径、URL 等）按 locale 切换的协同入口

---

## 9. AI 翻译菜单

### 9.1 入口与配置

- 配置面板：`Project Settings → PromptUGUI → Translation`（SettingsProvider）
  - `endpoint`（默认 `https://api.deepseek.com/chat/completions`）
  - `model`（默认 `deepseek-v4-flash`）
  - `systemPrompt`（默认含游戏 UI 翻译指南的中文 prompt；可改）
  - 持久化到 `ProjectSettings/PromptUGUI.asset`（进 git，团队共享）
- API key：`UserSettings/PromptUGUI/Auth.asset`（单字段 ScriptableObject，Unity 默认 .gitignore）
  - 同一 SettingsProvider 面板里编辑，落盘到 UserSettings 路径

### 9.2 触发

`Tools → PromptUGUI → Translate Locale...`：

1. 弹窗选 target locale（从 Settings.locales 列表）
2. 列出该 locale 下所有 .po 中 msgstr 为空的条目数，确认对话框
3. 走 OpenAI 兼容 Chat Completions + Structured Outputs：
   - `response_format = { type: "json_schema", json_schema: { ... } }`
   - schema：`{ translations: [{ msgid, msgctxt|null, msgstr }] }`
   - 单次请求一批（默认 50 条），失败重试 3 次，带指数退避
4. 回写 msgstr，保留注释和 msgctxt
5. 进度条显示；支持中途取消（已完成的批次保留）

### 9.3 LLM 上下文

每条 msgid 同一请求里附：

```json
{
  "msgid": "开始游戏",
  "msgctxt": null,
  "comments": [
    "MainMenu screen, Btn#playBtn label",
    "sibling: 设置, 退出"
  ]
}
```

system prompt 默认模板（用户可在 ProjectSettings 改）：

> 你正在为一款像素风游戏翻译 UI 字符串到 {{targetLocale}}。
>
> 规则：
> 1. 保留所有 `{{x}}` 模板占位符与 `{0}` `{1:C}` 等 C# 格式占位符不变
> 2. 保留 TMP 富文本标签（`<sprite>`、`<color>`、`<b>`、`<size>`、`<link>` 等）的字面形式与属性值不变（特别是 `name="..."`、`color="..."` 等属性内的值是资源 ID，不是文本）；位置可调以符合目标语言语序
> 3. 参考 sibling strings 推断风格一致性（如同屏其它按钮都是动词原形，本条也用动词原形）
> 4. 源文本可能混合多种语言；按目标 locale 翻译整体含义
> 5. 简短直接；UI 空间有限

### 9.4 cancel / 部分失败

- 用户中途 cancel：已 round-trip 的批次保留写盘，未发出的不发
- 单批失败：重试 3 次后跳过该批，log 错误，继续后续批次
- 整体失败率 > 50% 时弹窗提示用户检查配置

---

## 10. Hot Reload 集成

复用既有 `UI.HotReload` 通路。在 `UIAssetPostprocessor` 加 .po 路径分支：

- `Resources/PromptUGUI/i18n/<locale>/...po` 或 `i18n-custom/<locale>/...po` 改动：
  - 若 `<locale> == UI.Locale.Current`：UnloadLocale → LoadPoFiles 该 locale → Variants.Notify() → 所有 open Screen.ReSolve
  - 否则：noop（下次切到该 locale 时 LoadPoFiles 会读最新）
- `Settings.asset` 改动：
  - `locales` 数组增删/重命名：log 提示，不主动 reload（用户编辑期间瞬态）
  - fonts 表变化：若涉及 Current locale，触发 ReSolve

`UnityEditor.AssetDatabase` 批量改动场景（如菜单一键跑抽取后 import 几十个 .po）：AssetPostprocessor 的 `OnPostprocessAllAssets` 一次性收齐变化集，合并成一次 Reload。

---

## 11. 非目标 (v1)

- 复数（plurals）、性别、ICU MessageFormat
- 数字/货币/日期格式本地化（用户自己 `string.Format(culture, ...)`）
- 文字方向（RTL）布局重排
- LLM 以外的翻译 provider（DeepL、Google Translate、本地模型）
- 翻译记忆库（TM）、术语表（glossary）文件管理
- 翻译进度可视化（仅看 .po 里 msgstr 空与否）
- 运行时下载 .po（v1 都打进 build，Resources）
- 字幕、语音文件路径（留给 i18n-custom 用户表自由发挥）
- 字体动态下载（v1 假设 fonts 都打进 build）

---

## 12. 主 Spec 同步

### 12.1 `2026-05-07-promptugui-description-language-design.md` §10

```diff
- ❌ **本地化**：文本由代码侧推送或经过外部 L10N 钩子；描述文件不内置 i18n
+ ✅ **本地化** (M5 起)：见 `2026-05-08-i18n-fonts-design.md`
+   - 零 key gettext 流（msgid = 源文本字面量）
+   - .po 表 + Roslyn / XML 抽取 + LLM 翻译菜单
+   - locale 切换走 Variant.Changed 通路
+   - 字体 type → 每 locale TMP_FontAsset 表（Settings）
```

### 12.2 `.claude/skills/authoring-promptugui-xml/SKILL.md`

补充：

- `<Text>` / `<Btn>` 属性表加 `font` / `tr` / `ctx`
- "Variants: 运行时切换" 节加 "language as variant" 段
- 加 "图文混排：CDATA + TMP 富文本标签" 一段
- 速查页加 i18n cheatsheet：
  - `<Text>开始</Text>` 默认抽取
  - `<Text tr="false">{{x}}</Text>` 跳过
  - `<Text font="title">` 类型字体
  - C# 用 `UI.Tr("...")` 抽取
  - `UI.Locale.Set("zh-Hans")` 切换

---

## 13. 测试策略

按既有 EditMode/PlayMode 组织：

| 文件 | 覆盖 |
|---|---|
| `Tests/EditMode/I18n/PoParserTests.cs` | .po 词法、序列化、roundtrip、注释类型、转义 |
| `Tests/EditMode/I18n/TranslationStoreTests.cs` | load / lookup / unload / i18n-custom 覆盖 |
| `Tests/EditMode/I18n/TrResolverTests.cs` | substitution + miss fallback + ctx 命中 |
| `Tests/EditMode/Editor/StringExtractorXmlTests.cs` | XML 扫描、CDATA、`{{}}`、`tr="false"`、ambient ctx、TMP rich-text 注释 |
| `Tests/EditMode/Editor/StringExtractorCSharpTests.cs` | Roslyn 扫描、动态参数 warn、注释提取 |
| `Tests/EditMode/Application/LocaleSetTests.cs` | Variant.Changed 触发 + 切 locale 触发 ReSolve |
| `Tests/EditMode/Application/LocaleInitializeIfNeededTests.cs` | 启动自动初始化算法（D11 / §6.4）：noop-when-set / noop-when-empty / use-system / warn-and-fallback (mapped) / warn-and-fallback (unknown) |
| `Tests/EditMode/Application/LocaleSetToSystemDefaultTests.cs` | `SetToSystemDefault(fallback)` / `SetToSystemDefaultAsync(fallback)` §6.4：use-system-when-configured / fallback-when-system-not-configured / fallback-when-system-unknown / 无 fallback 保持旧"逃生口"行为 / fallback 不在 Configured 也接受 / configured=null 等价空 / Async 走 SetAsync 让 PO 可 await |
| `Tests/EditMode/Parser/CDataInTextTests.cs` | parser 接受 `<Text><![CDATA[...]]></Text>`、含 `<sprite>` 等 |
| `Tests/EditMode/Application/SettingsAssetTests.cs` | preloadedAssets 自动维护、多 Settings 报错 |
| `Tests/PlayMode/E2E/I18nFontSwapTests.cs` | Set locale → 字体换 + 文字翻译 |
| `Tests/PlayMode/E2E/I18nHotReloadTests.cs` | .po 改 → ReSolve |
| `Tests/PlayMode/E2E/TmpRichTextRoundtripTests.cs` | CDATA 含 sprite 的源文本走完整流程，runtime TMP 渲染正确 |

UnityMCP 跑测试，不批 mode（遵循 CLAUDE.md 约定）。

---

## 14. 风险与未决问题

| # | 风险 | 缓解 |
|---|---|---|
| R1 | LLM 偶尔翻 `<sprite name="coin"/>` 内的 `coin` | system prompt 强调 + .po 自动注释 + AI 翻译菜单可设置 review 步骤（v1 暂不实现 review，依赖 i18n-custom 手工 override） |
| R2 | Roslyn 解析 `Assets/**/*.cs` 慢（大型项目） | 增量扫描（仅 modified 文件 + 已知有 UI.Tr 的）作为 v2 优化；v1 全扫接受 1-3s |
| R3 | 用户在 .po 里手改 msgstr 后再跑抽取被覆盖 | 抽取仅写空 msgstr 与新 msgid，不动已有 msgstr；i18n-custom 是逆水身入口 |
| R4 | preloadedAssets 维护干扰用户已有的 preloadedAssets 配置 | AssetPostprocessor 仅添加，不删除；用户手动从 PlayerSettings 删除 settings asset 时 Editor warn |
| R5 | locale 名作 Variant 名与 author 自定义 Variant 冲突 | author 文档化此约定；XSD 警告（未来）；locale 名通常是 BCP-47（含 `-`），与游戏内 variant（通常是 `mobile` / `pc` 这类）天然不撞 |
| R6 | TextAsset enumerate `Resources.LoadAll<>` 在大量 .po 时慢 | v1 项目规模假设 < 数百 .po（每 locale 几十屏），可接受；v2 考虑生成 manifest |
| R7 | Variant 已激活的 locale 不能被同名 author Variant 重置 | 文档化：locale 是保留的 Variant 名空间，author 不应用同名 |
| R8 | Unity 不识别 .po 扩展为 TextAsset | §7.1 加 ScriptedImporter 把 .po 注册为 TextAsset；外部 gettext 工具仍能识别原始 .po 格式 |
| R9 | preloadedAssets 在 build profile 切换时丢失 | Unity 6 的 build profiles 各自维护 preloadedAssets — AssetPostprocessor 在每次访问 PlayerSettings.preloadedAssets 时校验并补上 |

---

## 15. 实施顺序提示

不在本 spec 范围（留给后续 plan）。粗略：

1. PromptUGUISettings + preloadedAssets 维护
2. PoFileImporter ScriptedImporter
3. PoParser + TranslationStore + TrResolver
4. UI.Locale API + Variant 集成（含 VariantStore.NotifyChangedInternal）
5. parser CDATA + IR `TextContentRaw` / `TextArgs` / `AttributesRaw`
6. Text/Btn 控件接 TrResolver + font type 解析
7. XML 抽取器
8. Roslyn 抽取器
9. AI 翻译菜单
10. Hot reload .po 分支
11. SKILL.md / 主 spec 同步
