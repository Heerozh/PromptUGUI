# M4 设计：Import / Auto-import / Hot reload / XSD

**日期**：2026-05-08
**状态**：设计阶段（待 review，未进入实施）
**作用域**：M4 子语言扩展与配套 Editor/工具能力的 C# API + XML 语义设计；不含实施代码或 task 拆分细节
**依赖**：基础设计 [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §7.6 / §9 / §12

---

## 1. 背景

主 spec §12 把 M4 列为"Import + 跨文件命名空间；编辑器内热重载；XSD 自动生成"。M1-M3 完成后再次 review，新增需求与边界细节：

1. **Import 路径机制**：库本体 content-agnostic 的设计需保持，文件→build 包含的责任不应进库。
2. **常驻模板**：像素游戏项目中 `PrimaryButton` / `DangerButton` 等模板每个文件都 `<Import>` 一遍噪音过大；需一种"启动期载入、后续文件免 Import 即可使用"的机制。
3. **作者面热重载**：保存 `.ui.xml` 后 Editor Game View 自动刷新，VariantStore 状态保留。
4. **IDE 体验**：在 VS Code / Rider 里写 `.ui.xml` 时希望有原语 + 项目级自定义控件的属性补全。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| M4-D1 | 文件→内容机制 | 全局 `Func<string,string> SourceResolver` 一次注册 | 库不持有"文件"概念；用户决定 Resources / Addressables / 自有路径 |
| M4-D2 | `src` 路径语义 | 对库不透明（key），不做相对路径解析 | 避免追踪每节点来源；resolver 自定义即可实现相对解析 |
| M4-D3 | 常驻模板 API | `UI.LoadCommonLibrary(src, [as])` 启动期一次性 | 与 C# `global using` 心智对齐；生命周期清晰 |
| M4-D4 | Template 命名冲突 | 全部硬错；`as="ns"` 显式消歧 | 与基础 spec D17 fail loud / §4.2 一致 |
| M4-D5 | 命名空间语法 | 调用处 `<ns.TemplateName/>` | 与基础 spec §7.6 既有写法一致 |
| M4-D6 | Hot reload 触发 | Editor 自动 + 运行时手动 API | Editor 自动靠 AssetPostprocessor；运行时手动给 mod / 调试用 |
| M4-D7 | Reload 状态语义 | Close → 重解析 → Open；VariantStore 不动 | ScreenView.OnOpen 重跑、订阅自然重建；inline Subscribe 失效 |
| M4-D8 | Reload 解析失败 | 抛异常 + 保留旧状态 | 唯一软失败例外；编辑器手感 > fail loud |
| M4-D9 | XSD 生成时机 | Editor 菜单触发，不挂 AssetPostprocessor | 控件注册时机不可靠；强制人工触发更确定 |
| M4-D10 | XSD 覆盖 | 静态原语 + 动态扫 ControlRegistry；不含 Template | Template 集合按文档动态合并，不可枚举 |
| M4-D11 | XSD 内 Template 用法处理 | `controlGroup` 末尾置 `xs:any processContents="lax"` | 让 `<TitledPanel>` 不被红线，代价是真打错 tag 也不会被 XSD 抓到 |
| M4-D12 | `LoadDocument(label,xml)` 与 hot reload | 不参与依赖图，Reload 抛异常 | 没有 entrySrc 就无法重新拉取；与 D8 风格相反但可解释 |

---

## 3. 一个完整可读的例子

启动期：

```csharp
// 一次注入路径策略与反向映射
UI.UseResourcesResolver("UI");
//  内部等价于：
//  UI.SourceResolver  = src => Resources.Load<TextAsset>($"UI/{src}").text;
//  UI.HotReload.AssetPathToSrc = path => path 起头 "Assets/Resources/UI/" 时
//                                       去掉前缀 + 去掉 ".ui.xml" 后缀

// 把通用按钮模板进全局池（src 写法等同 Resources 命名）
UI.LoadCommonLibrary("common/Buttons");

// 把第三方组件库放进 ml 命名空间
UI.LoadCommonLibrary("third-party/MyLib", @as: "ml");

// 注册自定义控件（M1 行为）
BuiltinPrimitives.Register(UI.Registry);
```

Screen 文件：

```xml
<!-- screens/MainMenu.ui.xml -->
<PromptUGUI version="1">
  <Import src="screens/Inventory"/>   <!-- 仅本文件可见 -->

  <Screen name="MainMenu">
    <PrimaryButton id="play">开始</PrimaryButton>     <!-- 来自 commons -->
    <ml.Foo id="x"/>                                  <!-- 来自 commons (namespaced) -->
    <Inventory id="inv"/>                             <!-- 来自 Import -->
  </Screen>
</PromptUGUI>
```

代码侧加载：

```csharp
UI.LoadDocumentFromSrc("screens/MainMenu");
var screen = UI.Open("MainMenu");
screen.Get<PrimaryButton>("play").OnClick.Subscribe(_ => Game.Start()).AddTo(screen);
```

Editor 内修改任何 `.ui.xml` 保存 → AssetPostprocessor 调 `UI.HotReload.NotifyAssetChanged(path)` → 受影响 Screen 自动 Reload，VariantStore 状态保留，ScreenView.OnOpen 重跑。

---

## 4. 文档生命周期与依赖图

### 4.1 SourceResolver

```csharp
public static Func<string, string> SourceResolver { get; set; }
```

库自身永不读文件系统；所有对 `<Import>` 与 `LoadCommonLibrary` / `LoadDocumentFromSrc` 的内容获取都走这个全局回调。`src` 字符串对库不透明：库只把它当 dict key（depGraph、cache）传递。

未设 `SourceResolver` 时调任何需要它的 API → `InvalidOperationException`。

### 4.2 Bootstrap 期：commons 池

```csharp
public static void LoadCommonLibrary(string src, string @as = null);
```

行为：

1. `SourceResolver(src)` → xml；空字符串/null → `IOException`
2. 调用 `UIDocumentParser.Parse(xml)` 得到 raw `UIDocument`
3. 若 raw 含任何 `<Screen>` → `ParseException`（commons 仅 Templates + Import）
4. 递归解析其 `<Import>` 链（同样禁含 `<Screen>`）
5. 把所有抽出的 Templates 合并进 `_commonsPool`
6. 若 `@as` 非空，则该批 Templates 仅以 `<as.TemplateName>` 调用，裸名不可见
7. 任何同名冲突（与 `_commonsPool` 已有项）→ `TemplateException`，调用回滚（已塞进的 Templates 撤回）
8. depGraph 注册：`_commonsSources.Add(src)`；`_srcToDeps[src] = {src + 所有递归解析的子 src}`；为本批每个 Template 在 `_commonsPool` 上记录其来源 src（实现层加一个 `OriginSrc: string` 字段到 `TemplateDef`，仅用于 reload 时筛选回收）

### 4.3 运行期：每个 Screen 文档

```csharp
public static IReadOnlyList<string> LoadDocumentFromSrc(string src);
public static void                  LoadDocument(string label, string xml);  // 已有，保留
```

`LoadDocumentFromSrc(src)` 流程：

1. `SourceResolver(src)` → xml
2. `Parse(xml)` 得 raw doc
3. 递归解析 raw 与其 Import 链，收集到 `entryDeps = {src + nested srcs}`
4. 构造 expansion 期可见的 templates 字典：`commonsPool ∪ entryDeps 各自的 Templates ∪ raw.Templates`
5. 任何同名冲突（含 commons↔file、Import↔file、Import↔Import 等）→ `TemplateException`
6. `TemplateExpander.Expand(...)` → 展开后的 `UIDocument`
7. 对每个 `Screen s in expanded.Screens`：
   - 若 `_docs.ContainsKey(s.Name)` → `InvalidOperationException`（**Reload 内部路径走 §7.2 step 4 先清掉 `_docs[name]`，绕开此检查**）
   - `_docs[s.Name] = s`
   - depGraph 注册：`_screenDeps[s.Name] = { EntrySrc: src, AllDeps: entryDeps }`；`_srcToDeps[src] = entryDeps`
8. 返回 `List<string>{ s.Name ... }`

`LoadDocument(label, xml)` 保留 M1 行为：raw xml 直接解析展开；不进 depGraph、不能 hot reload。

### 4.4 依赖图数据结构

```csharp
internal static class DepGraph {
    // src ∈ commonsPool 时的反向索引
    static readonly HashSet<string> _commonsSources = new();
    // src → 这个 src 解析时所有用到的子 src
    static readonly Dictionary<string, HashSet<string>> _srcToDeps = new();
    // screenName → entrySrc + 所有依赖 src
    static readonly Dictionary<string, ScreenDep> _screenDeps = new();
    record ScreenDep(string EntrySrc, HashSet<string> AllDeps);
}
```

变更某个 src（hot reload）时：
- 若是 commons src → 重新载入这个 commons + 所有现存 Screen reload（保守策略，commons 改动 blast radius 全局）
- 若是 Screen 依赖链上的 src → 仅 reload 受影响 Screen 集合

---

## 5. Import / Auto-import 语法层

### 5.1 `<Import>` 规则

```xml
<PromptUGUI version="1">
  <Import src="panels/Inventory"/>
  <Import src="panels/Settings" as="settings"/>
  ...
</PromptUGUI>
```

| 规则 | 行为 |
|---|---|
| 出现位置 | `<PromptUGUI>` 直接子元素；嵌套出现 → `ParseException` |
| `src` | 透传 SourceResolver；库不解析为路径 |
| `as="ns"` | 该 Import 抽出的 Templates 仅以 `<ns.TemplateName/>` 调用 |
| 递归 | 被 Import 的文件可有自己的 `<Import>` 与 `<Template>`；不能含 `<Screen>` |
| 循环 | A→B→A → `ParseException` 列出完整链路 |
| 重复 src | 同一文件 Import 同 src 多次 → `ParseException`（KISS） |
| 不同文件共享 src | 同一解析期内被两个不同文件 Import → caching：解析一次 |

### 5.2 命名空间语法 `<ns.Tag/>`

Parser：在 `ParseElement` 里第一次出现 `el.Name.IndexOf('.')` 时把 `(ns, tag)` 拆给 `ElementNode`；现有 `ElementNode` 加字段 `Namespace?`。

TemplateExpander：查表用 `(ns, name)`：
- `ns == null` → 在裸名空间里查
- `ns == "x"` → 在 `_commonsPool[x.*]` 与本文件 `Import as="x"` 抽出的 Templates 里查
- 找不到 → `TemplateException("unknown ns or template")`

### 5.3 冲突矩阵（汇总）

全部硬报错；唯一逃生口是双方任一挂 `as=ns`。

| 场景 | 行为 |
|---|---|
| 两个 commons 都定义 `Foo` | 第二次 LoadCommonLibrary 报错 |
| commons 与某 Screen 文件本地 Template 同名 | LoadDocumentFromSrc 报错 |
| commons 与 Import 同名 | LoadDocumentFromSrc 报错 |
| 一个 Screen 文件内部 Import 链同名 | LoadDocumentFromSrc 报错 |
| 任一以上情形若一方挂 `as="ns"` | 因被 namespace 隔离而合法 |

### 5.4 与基础 spec §7.6 的一致性

主 spec §7.6 已示例 `<Import src="..." [as="ns"]/>` 与 `<ns.TitledPanel/>` 用法。本节是其行为细化与边界规则。主 spec §4.2 "跨文件 Template 同名 → 报错" 在 M4 拓展为"任意来源同名 → 报错"，含义被强化但方向一致。主 spec §7.6 的"as= 仅在两个 Import 暴露同名 Template 时强制；常态不需要" 在 M4 的硬报错矩阵下应改为"as= 是唯一显式消歧手段；两 Import 同名时必须用"——M4 实施末尾顺手同步主 spec。

---

## 6. C# API 总表

### 6.1 顶层 facade 增量

```csharp
public static class UI {
    public static Func<string, string> SourceResolver { get; set; }

    // commons
    public static void LoadCommonLibrary(string src, string @as = null);
    public static void ReloadCommonLibrary(string src);

    // 以 src 加载 Screen 文档
    public static IReadOnlyList<string> LoadDocumentFromSrc(string src);

    // 已有：raw xml 加载（不进 depGraph、不可 hot reload）
    public static void LoadDocument(string label, string xml);

    // 显式 reload（Player 也可调）
    public static void Reload(string screenName);

    // 清理 API（粒度自上而下）
    //   UnloadAllCommonLibraries(): 仅清 _commonsPool + _commonsSources +
    //                               commons 在 _srcToDeps 内的项
    //   UnloadAll():                清以上 + _docs + _open + _screenDeps；
    //                               保留 SourceResolver / AssetPathToSrc / Registry
    public static void UnloadAllCommonLibraries();
    public static void UnloadAll();

#if UNITY_EDITOR
    public static class HotReload {
        public static Func<string, string> AssetPathToSrc { get; set; }
        public static void NotifyAssetChanged(string assetPath);
        public static bool Enabled { get; set; } = true;
    }
#endif

    // 内置 helper（覆盖主流"放进 Resources/UI/"用法）
    public static void UseResourcesResolver(string rootPath);
}
```

### 6.2 `UseResourcesResolver` 实现要点

```csharp
public static void UseResourcesResolver(string rootPath) {
    var root = rootPath.TrimEnd('/');
    SourceResolver = src => {
        var ta = Resources.Load<TextAsset>($"{root}/{src}");
        if (ta == null) throw new IOException($"Resources lookup failed: {root}/{src}");
        return ta.text;
    };
#if UNITY_EDITOR
    HotReload.AssetPathToSrc = assetPath => {
        var prefix = $"Assets/Resources/{root}/";
        if (!assetPath.StartsWith(prefix)) return null;
        var rel = assetPath.Substring(prefix.Length);
        return rel.EndsWith(".ui.xml")
             ? rel.Substring(0, rel.Length - ".ui.xml".Length)
             : null;
    };
#endif
}
```

放在 Runtime asmdef，使用 `#if UNITY_EDITOR` 包裹 Editor 部分；运行时 build 内只剩 SourceResolver 设置。

---

## 7. Hot reload 设计

### 7.1 入口分流

```
Editor 自动:  AssetPostprocessor →  UI.HotReload.NotifyAssetChanged(path)
运行时手动:                          UI.Reload(screenName)
                                     UI.ReloadCommonLibrary(src)
```

`NotifyAssetChanged(assetPath)` 算法：

```
src = AssetPathToSrc(assetPath)
if src == null:                       return                # 非项目文件
if src ∈ DepGraph._commonsSources:    ReloadCommonLibrary(src)
else:
    affected = { name : DepGraph._screenDeps[name].AllDeps.Contains(src) }
    foreach name in affected: Reload(name)
```

### 7.2 `Reload(screenName)` 语义

```
1. wasOpen     = _open.ContainsKey(name)
2. dep         = _screenDeps[name]
   dep == null → InvalidOperationException("not loaded by src; cannot reload")
3. xml = SourceResolver(dep.EntrySrc)
4. 重走 §4.3 步骤 2-6（解析、合并、Expand）到 expanded UIDocument
   失败 → 抛异常，_docs / _open / _screenDeps 全保留，状态等于"什么都没发生"
5. wasOpen ? Close(name)               # Screen.Dispose() 触发 CompositeDisposable；
                                         _open 移除；_docs 暂保留
6. 移除 _docs[name] 与 _screenDeps[name]，避免 §4.3 step 7 的重复检查报错
7. 走 §4.3 step 7：把新 ScreenDef 写回 _docs[name]，更新 _screenDeps[name]
8. wasOpen ? Open(name)                # ScreenView.OnOpen 重跑；inline 订阅失效
```

`VariantStore` 是 `UI` 静态字段、不归 Screen 持有，所有步骤不动它，状态自然存活。
注意 step 3-4 的"先解析后销毁"顺序：解析失败时旧 GameObject 仍在场，作者眼里就是"保存出错没生效"，与 M4-D8 一致。

### 7.3 `ReloadCommonLibrary(src)`

```
1. 用 TemplateDef.OriginSrc 标记把 _commonsPool 中属于本 src 与其依赖链的 Templates 全部抽出（先暂存，便于失败回滚）
2. 重新 SourceResolver(src) + 解析 + 走 §4.2 步骤
   失败 → 把暂存项放回 _commonsPool；抛异常，commons 状态原样
3. 一律 Reload 所有 _screenDeps 中的 Screen
   * M4 v1 简化策略：commons 改动频率低，统一 reload 所有 Screen 即可；
     精确依赖追踪（哪个 Screen 真用过哪个 commons 模板）留 M5+ 视痛点再做。
```

### 7.4 AssetPostprocessor

`PromptUGUI.Editor` asmdef（新增），与 Runtime 分离：

```csharp
class UIAssetPostprocessor : AssetPostprocessor {
    static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                       string[] moved, string[] movedFrom) {
        if (!UI.HotReload.Enabled || UI.HotReload.AssetPathToSrc == null) return;
        foreach (var p in imported) if (p.EndsWith(".ui.xml"))
            UI.HotReload.NotifyAssetChanged(p);
        foreach (var p in moved)    if (p.EndsWith(".ui.xml"))
            UI.HotReload.NotifyAssetChanged(p);
    }
}
```

`deleted` 与 `movedFrom` 暂不响应——若 Screen 文件被删除/重命名，运行时直接抛"resolver 找不到"是合理的。

### 7.5 Reload 期间的并发与 throttling

EditMode 下文件保存可能短时连续触发多次（IDE 写入策略）。M4 v1 不做 debounce：每次 NotifyAssetChanged 直接走流程。Reload 本身代价主要在 GameObject 重建，单 Screen 几十毫秒级别；连 reload 几次不会卡爆 Editor。M5 视体感再加节流。

---

## 8. XSD 生成

### 8.1 API

```csharp
#if UNITY_EDITOR
public static class XsdGenerator {
    public static string Generate();
    public static void   GenerateToFile(string assetPath = "Assets/PromptUGUI.gen.xsd");
}
#endif
```

Editor 菜单 `Tools/PromptUGUI/Generate XSD` 直调 `GenerateToFile()`。

### 8.2 Schema 结构

```
targetNamespace = "https://prompt-ugui/v1"
elementFormDefault = "qualified"

<xs:element name="PromptUGUI">     # version 必填属性 + Import|Screen|Template 子选
<xs:element name="Import">         # src 必填、as 可选
<xs:element name="Screen">         # name 必填、子: controlGroup + Variant
<xs:element name="Template">       # name 必填、子: Param* + 单 root
<xs:element name="Param">          # name 必填、default 可选
<xs:element name="Slot"/>          # 空元素
<xs:element name="Variant">        # when 必填、子: Add+
<xs:element name="Add">            # into 必填、at 可选

<xs:attributeGroup name="commonAttrs">     # id/anchor/size/width/height/margin/
                                            # pivot/padding/spacing/hidden/interactable

<xs:element name="Frame">          # 静态：commonAttrs
<xs:element name="Image">          # 静态：commonAttrs + sprite/color/type
<xs:element name="Text">           # 静态：commonAttrs + font/size/color/align/wrap/text
<xs:element name="VStack">         # 静态：commonAttrs
<xs:element name="HStack">         # 静态：commonAttrs
<xs:element name="Grid">           # 静态：commonAttrs + columns
<xs:element name="Btn">            # 静态：commonAttrs + sprite/color/text

# 动态部分：foreach reg in ControlRegistry
<xs:element name="{reg.Tag}">      # commonAttrs + 反射 [UIAttr] 标记的字段
                                    # 类型：string（M4 不区分细节）

<xs:group name="controlGroup">     # 7 原语 + 所有自定义控件 + xs:any (lax)
```

### 8.3 已知限制（spec 必须明示）

| 想表达 | XSD 实际能做的 |
|---|---|
| `attr.variantName` 仅限通用属性 | `xs:anyAttribute namespace="##local" processContents="lax"`，IDE 不报红但不补全 variant |
| `anchor=stretch + size 同轴互斥` | XSD 表达不出，仍由 Parser 报错 |
| Template 调用必须满足声明的 `<Param>` | 不可枚举，不进 XSD；运行时 Parser/Expander 校验 |
| 模板名补全 | 不在 XSD 内；后续 M5+ 可考虑 LSP 插件 |

XSD 定位是 IDE 自动补全 + 拼写错检查；运行时正确性以 Parser/TemplateExpander 兜底。

### 8.4 实现要点

- 现有 `ControlRegistry` 仅有 `Resolve(tag)` / `Has(tag)`；M4.6 在其上加只读 `IEnumerable<(string Tag, Entry)> All { get; }`（不开 setter，仅遍历）
- XsdGenerator 拿到每个 Entry 后**直接对 `Entry.ControlType` 重新反射**取 `[UIAttr]` 标记的 public properties；不依赖 `ControlMeta` 暴露内部字典（保持 ControlMeta 实现自由）
- 属性类型映射到 XSD 类型：`string→xs:string`、`int→xs:int`、`float→xs:float`、`bool→xs:boolean`；不支持的类型 → 跳过 + warning log（与 ControlMeta.BuildSetter 兼容范围对齐）
- 输出按 `(tag asc, attrName asc)` 排序，保证 snapshot 稳定
- 生成器内部用 `XmlWriter`，缩进 2 空格、UTF-8、BOM-less

### 8.5 IDE 集成

`.ui.xml` 文件根加：

```xml
<PromptUGUI version="1"
  xmlns="https://prompt-ugui/v1"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xsi:schemaLocation="https://prompt-ugui/v1 PromptUGUI.gen.xsd">
```

或项目级 `.xmlcatalog` / `xml-model` 关联——具体 IDE 的接入文档放进 README，不在 spec 范围。

---

## 9. 错误处理矩阵

| 场景 | 行为 | 时机 |
|---|---|---|
| `SourceResolver` 未设但调相关 API | `InvalidOperationException` | 调用瞬间 |
| `SourceResolver(src)` 返回 null/空字符串 | `IOException` 携带 src | 解析前 |
| `<Import>` 出现在 `<Screen>` / `<Template>` 内 | `ParseException` | 解析期 |
| Common library 文件含 `<Screen>` | `ParseException` | LoadCommonLibrary 内 |
| 循环 Import (A→B→A) | `ParseException`，列完整链路 | 解析期 |
| 同文件内重复 `<Import src=X/>` | `ParseException` | 解析期 |
| Template 跨任意来源同名 | `TemplateException` | merge 期 |
| `<ns.Foo/>` 但 ns 未声明 | `TemplateException` | Expand 期 |
| `Reload(name)` 内重解析失败 | 抛异常，**保留旧状态**（唯一软失败例外）| Reload 内 |
| `AssetPathToSrc(path)` 返回 null | 静默忽略（非项目文件） | NotifyAssetChanged 内 |
| `Reload` 一个由 `LoadDocument(label,xml)` 加载的 doc | `InvalidOperationException` | Reload 内 |
| `XsdGenerator.GenerateToFile` 写入失败 | 透传 IO 异常 | 菜单触发 |

---

## 10. 测试策略

放进 `Tests/EditMode`，沿用 NUnit。新增 `ResetForTests` 也清 commons / depGraph。

| 集合 | 关键 case |
|---|---|
| `ImportTests` | 单层 / 嵌套 / 循环 / 重复 src / Import 含 Screen 报错 |
| `AutoImportTests` | LoadCommonLibrary 单/多/with `as` / 冲突报错 / Bootstrap 后 LoadDocument 看到模板 |
| `NamespaceTests` | `<ns.Foo/>` 解析、跨文件冲突走 `as=` 解决、未声明 ns 报错 |
| `HotReloadTests` | `NotifyAssetChanged` 直调；fakeFiles dict 模拟改文件；Reload 后 ScreenView.OnOpen 重跑；commons 改了带动 reload；Variant 状态保留；Reload 异常保留旧状态；LoadDocument(raw) reload 抛异常 |
| `XsdTests` | 空 Registry / 含原语 / 含 mock 自定义控件 → snapshot；快照 `Tests/Fixtures/expected.xsd` |
| `SamplesSmokeTest`（可选） | 读真 Sample 的 ui.xml 跑通 Open；防 Sample 漂移 |

AssetPostprocessor 本身只做"路径过滤 + 转调"，无逻辑可测；保留一个手测 checklist 写入 PR 描述。

---

## 11. Sample 迁移

`Samples~/MainMenu/MainMenuRunner.cs`：

**M3 现状**：

```csharp
[SerializeField] TextAsset _xml;
void Start() {
    BuiltinPrimitives.Register(UI.Registry);
    UI.LoadDocument("main", _xml.text);
    UI.Open("MainMenu");
    ...
}
```

**M4 目标**：

```csharp
void Start() {
    BuiltinPrimitives.Register(UI.Registry);
    UI.UseResourcesResolver("UI");
    UI.LoadDocumentFromSrc("MainMenu");
    UI.Open("MainMenu");
    ...
}
```

文件位置变更：`Samples~/MainMenu/MainMenu.ui.xml` → `Samples~/MainMenu/Resources/UI/MainMenu.ui.xml`。

迁移作为 M4 计划末段独立 task，含手测 checklist：
- Editor 改 `.ui.xml` 保存 → Game 视图自动刷新（Variant 不变）
- 调试入口调 `UI.Reload("MainMenu")` 同样生效
- Player build（disable Editor 路径）下打开仍能正常 `UI.Open`

---

## 12. 实施分期建议（M4 内部）

每段单独一 PR；写 plan 时按这个粒度切。

| seg | 内容 | 验收 |
|---|---|---|
| **M4.1** | 顶层 `<Import>` parser + 循环检测 + 跨文件 Template 合并；不含 commons | 单 Screen + 一层 Import 跑通 |
| **M4.2** | `LoadCommonLibrary` + 全局 commons 池 + `LoadDocumentFromSrc` + 依赖图 | bootstrap 后 Screen 文件能用 commons 模板 |
| **M4.3** | `as="ns"` 命名空间（commons 与 Import 一致语法） | conflict + namespace 用例覆盖 |
| **M4.4** | `UI.Reload` + `UI.ReloadCommonLibrary` 运行时 API + `HotReload.NotifyAssetChanged` 入口 + fakeFiles 测试 | EditMode 测试模拟改文件触发 reload，状态/订阅检查通过 |
| **M4.5** | `PromptUGUI.Editor` asmdef + AssetPostprocessor + `UseResourcesResolver` helper + Sample 迁移 | 真 Editor 内改 Sample .ui.xml → 自动 reload |
| **M4.6** | XsdGenerator + 菜单 + snapshot 测试 + 主 spec §7.6 / §12 同步 | 注册一组虚拟控件 → 生成 → IDE 内可补全 |

---

## 13. 与主 spec 的同步

M4 实施末段需要回填主 spec 以下位置（不改主 spec 结构）：

- §4.2 "跨文件 Template 同名 → 报错；as="ns" 用于消歧" → 强化为"任意来源（含 commons）同名报错；as="ns" 是唯一显式消歧"
- §7.6 "as= 仅在两个 Import 暴露同名 Template 时强制；常态不需要" → 改为"as= 是唯一显式消歧手段，多源同名时必填"
- §12 M4 行的"内容" / "验收" 列：把本 spec 12 节的 6 段切片简要写回去

---

## 14. 显式非目标（M4 不做）

为避免 scope creep：

- ❌ **LSP 插件 / VS Code extension**：XSD 已能覆盖 80% 自动补全场景，LSP 留 M5+
- ❌ **commons 精确依赖追踪**：M4 v1 commons 改 → 一律 reload 所有 Screen；性能优化等到有真痛点再做
- ❌ **AssetPostprocessor 节流 / debounce**：测下来不卡再说
- ❌ **deleted / movedFrom 资产响应**：抛 resolver 异常已经够用
- ❌ **Player build 内热重载**：Editor-only；mod 场景显式调 `UI.Reload` 即可
- ❌ **XSD 进 Template**：不可枚举
- ❌ **Import 条件编译/Variant**：所有 Import 静态、无条件
- ❌ **多 Slot 命名**：仍由主 spec §10 拒绝
- ❌ **运行时新增 / 移除 commons**：commons 是启动期一次性载入；M4 不支持运行期 add/remove（unload 全清是测试用例外）

---

## 15. 风险与开放问题

| # | 风险 / 问题 | 应对 |
|---|---|---|
| M4-R1 | commons 改 → 全量 Screen reload 在大项目可能慢 | M4.4 末做轻量 profiling；M5+ 加精确依赖追踪 |
| M4-R2 | `LoadDocument(label, xml)` 与 `LoadDocumentFromSrc` 同时存在导致心智割裂 | spec §3 / sample 默认走 src 版本；raw 版本仅 README 标"低层级 / 测试 / 嵌入式 xml" |
| M4-R3 | `as="ns"` 与 Template 内 `<ns.Foo/>` 让 parser 与 expander 复杂度上升 | 在 IR 层加 `ElementNode.Namespace` 字段，把它向下传到 expander 一处；测试用例覆盖 |
| M4-R4 | Hot reload 期间 Variant 切换冲突（reload 中途用户切 Variant） | M4 v1 不加锁：reload 是同步 API，期间不会有切换；如未来异步化再考虑 |
| M4-R5 | XSD 1.0 表达力不足，IDE 实际体验可能仍有红线 | 在 README 写明"XSD 仅自动补全；运行时 Parser 是真理"；IDE 体验由 M5+ LSP 解决 |
| M4-R6 | AssetPostprocessor 在 domain reload 期间触发，UI 静态状态可能已重置 | NotifyAssetChanged 的 src 在 depGraph 找不到时静默忽略——已经覆盖 |
| M4-R7 | `Samples~/` 路径下 `Resources/` 目录在 package 安装时 Unity 行为？ | M4.5 验收：装包到独立项目后确认 Resources.Load 能读到；若不行回退用 TextAsset Inspector 引用 + UI.LoadDocument(raw) 写法（牺牲热重载） |
| M4-R8 | `ReloadCommonLibrary` 不保留原始 `as=` 命名空间 | 用 as= 加载的 commons 在 reload 时按裸名重装；如有冲突需 UnloadAllCommonLibraries + 重 bootstrap。M5+ 可在 commonsPool 上记录原 as= 字段以恢复 |

---

_Spec 结束。下一步：用 writing-plans skill 把 M4 拆成可执行实施计划。_
