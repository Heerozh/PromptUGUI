# `<Markdown>` Markdown 渲染控件设计

**日期**: 2026-06-09
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:

1. 新增 `Runtime/Controls/Markdown.cs`（Control 壳：`text`/`Text`/`BindText`/`Style`/`OnLinkClicked`；`OnAttached` 建内置竖向 ScrollRect 脚手架（Viewport+RectMask2D，§7.0）；set 时重渲整棵子树；异步图加载；无 renderer 时原文降级）
2. 新增 `Runtime/Markdown/IMarkdownRenderer.cs`（接口 `MarkdownRenderResult Render(string md, MarkdownStyle style)`，命名空间 `PromptUGUI`）
3. 新增 `Runtime/Markdown/MarkdownStyle.cs` + `MarkdownRenderResult.cs`（纯 POCO；命名空间 `PromptUGUI`）
4. 新增 `Runtime/Application/UI.Markdown.cs`（`UI` 嵌套静态类：`Renderer` 注入点 / `DefaultStyle` / `ImageResolver` / `UseWebImageResolver()`；同 `UI.Locale` / `UI.Theme` / `UI.Toast` 风格）
5. `Runtime/Application/BuiltinPrimitives.cs` 注册 `Markdown`（`defaultTextAttr:"text"`）
6. `Runtime/Application/ScreenInstantiator.cs` + `Runtime/Core/Lint/`：`<Markdown>` 不应有子**元素**（内容来自 text），新增 lint `PUI-MARKDOWN-NO-CHILDREN`（warning）
7. 新增 `Runtime/MarkdigBackend/PromptUGUI.Markdown.asmdef`（`defineConstraints:["PROMPTUGUI_HAS_MARKDIG"]`、`overrideReferences:true`、`precompiledReferences:["Markdig.dll"]`、refs `PromptUGUI.Runtime`）+ `MarkdigRenderer.cs`（遍历 Markdig AST → `ElementNode` 树；命名空间 `PromptUGUI.MarkdigBackend`，避免与控件类 `Markdown` 撞名）+ 自注册 hook
8. 新增 `Editor/MarkdigDetector.cs`（扫到 `Markdig` 或 `Markdig.Signed` 程序集 → 给工程加 `PROMPTUGUI_HAS_MARKDIG`，扫不到 → 移除；`[InitializeOnLoad]` 类 + 静态构造函数）
9. 新增测试 asmdef `Tests/EditMode/Markdown/PromptUGUI.Tests.EditMode.Markdown.asmdef`（`defineConstraints` 同上，镜像 Addressables 测试 asmdef）+ renderer 树形断言 + 控件集成测试；PlayMode 异步图测试
10. SKILL：`authoring-promptugui-xml`（目录行 + 新增 `reference/controls-markdown.md`）、`scripting-promptugui-csharp`（`Text`/`BindText`/`OnLinkClicked`/`Style`/`UI.Markdown.*`）；Markdig 安装 + `PROMPTUGUI_HAS_MARKDIG` 符号说明并入 `reference/controls-markdown.md` 的 Setup 小节（Markdown 只是控件，不单开顶层 skill）
11. 主 spec `2026-05-07-promptugui-description-language-design.md` §5（控件表）追加一行
12. XSD 生成器随新 `[UIAttr]` 手动 regenerate + substring 断言

**依赖**: **新增软依赖 Markdig**（NuGet，BSD-2-Clause，纯托管 netstandard2.0），经 `PROMPTUGUI_HAS_MARKDIG` 门控、隔离在独立 asmdef，核心包**零强制依赖**。复用：`UI.GetInstantiator().InstantiateNode` + `ChildHostTransform`（同 Carousel/ScrollList 运行期建子树）、DefaultText 的 runtime 锁（`PeekDefaultText`/`_lastAppliedDefaultText`，同 `<Text text>`）、`<RawImage type="contain|cover">` + `AspectRatioFitter`（图片自适应，image-fit / raw-image PR）、TMP 富文本（`<b>`/`<i>`/`<s>`/`<mark>`/`<link>`/`<sprite>`）、`FontApplier`（font type）、`UI.Theme.Resolve`（color token + `/alpha`）、`ProceduralBuilders`、`Awaitable` + `UnityWebRequestTexture`（WebGL 安全的图下载）、SpriteSet `generateTmpSpriteAsset`（行内 `<sprite name>`，inline-tmp-sprite PR #56）、R3 `Subject`（`OnLinkClicked`）。

---

## 1. 背景

目标：让作者能在 `.ui.xml` 里声明、或在 C# 里动态灌入一段 **Markdown 文本**，控件**在自己的子节点里自动渲染并布局**整篇文档——标题、段落、列表、引用、代码块、表格、图片、链接，不走浏览器 / WebView，纯 uGUI + TMP。允许丢失部分格式（HTML、行内图等），但大部分内容正常显示。

典型用法是**动态内容**（第一优先）：XML 里先空着或放占位，运行期从网络 / Resources / 存档拉到 markdown 字符串再灌进去：

```xml
<Markdown id="patchNotes" anchor="stretch" margin="16"/>
```
```csharp
screen.Get<Markdown>("patchNotes").Text = await Http.GetString(patchNotesUrl);
```

为什么不在单个 TMP 里用富文本拼完（"路线 A"）？单个 text mesh 放不下真·图片、表格网格、代码块整块背景。本控件走**块级渲染**（"路线 B"）：把 markdown 拆成块，每块映射到一个现有 PromptUGUI 控件，竖向堆叠——`<Image>`/`<RawImage>`/`<Grid>` 这些能力现有控件已经齐备，控件库本身只差"块拆分 + 把块翻译成现有控件子树"这一层。

为什么用 Markdig 而不是自写解析器？Markdown 解析的边角 case 极多（嵌套列表、表格对齐、转义、围栏语言标记…），Markdig 是成熟的完整 CommonMark + GFM 实现，保真度高、维护成本低；代价是引一个 NuGet 包，用软依赖 + 符号门控（同 Addressables）把它对核心包的侵入降到零。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| MD-D1 | 渲染路线 | 块级：Markdig AST → `ElementNode` IR 子树 → `InstantiateNode` → 现有控件 | 复用整条实例化 / 布局 / 属性 / Variant 管线；控件壳只干"调 renderer + 喂 InstantiateNode"两件事；放弃在单 TMP 拼全文（放不下图 / 表 / 代码块底） |
| MD-D2 | 解析器 | Markdig（NuGet），不自写 | 完整 CommonMark + GFM；边角 case 不踩坑；renderer 只负责 AST→IR 翻译 |
| MD-D3 | Markdig 分发 | 软依赖 + `PROMPTUGUI_HAS_MARKDIG` 符号门控 + 独立 asmdef（defineConstraint + `precompiledReferences:["Markdig.Signed.dll"]`）+ editor 自动检测器 | 镜像 `PROMPTUGUI_HAS_ADDRESSABLES`；核心包零二进制、零强制依赖；Markdig 不是 UPM 包 → versionDefines 抓不到、Runtime asmdef 不能直接引 → 必须独立 asmdef 隔离 DLL 引用 |
| MD-D4 | 控件本体放哪 | `Markdown : Control` 放 **Runtime，始终编译**（只碰 Runtime 类型 + `IMarkdownRenderer` 接口，不引 Markdig）；只有 `MarkdigRenderer`（碰 Markdig AST 类型）放门控 asmdef | 标签始终可解析（不会"unknown tag"炸 XML）；主测试套件始终能编控件测试；注册行无需 `#if`；比 Addressables 的"helper 直接 `#if` 掉"更稳——因为这是个**控件/标签**，优雅降级 > 标签消失 |
| MD-D5 | 无 Markdig 降级 | `UI.Markdown.Renderer == null`（未装/未注入）→ 把原始 markdown 当纯文本塞进一个 `<Text wrap>` 子节点 + 一次性 `Debug.LogWarning`（提示装 Markdig + 定义符号） | 不炸、有可读兜底；符号 ON ⟺ Markdig 在场 ⟺ 门控 asmdef 编得过 ⟺ `MarkdigRenderer` 自注入 |
| MD-D6 | renderer 接口契约 | `MarkdownRenderResult Render(string md, MarkdownStyle style)`；返回 `{ ElementNode Root; IReadOnlyList<ImageRequest> Images }`，纯函数、只产 IR + 图请求，不碰 GameObject | renderer 在门控 asmdef 里，返回 Runtime 公开 IR；高度可测（断言树形）；async 图加载留给控件（Runtime），resolver 可注入 |
| MD-D7 | 文本入口属性名 | XML `text=` + `defaultTextAttr:"text"` + C# `Text { get; set; }`（与 `Text`/`Btn`/`InputField`/`Toggle`/`Tab` 一致，不做异类） | 用户直觉就是 `markdown.Text`；全库文本载荷都叫 `text`；复用 DefaultText runtime 锁 |
| MD-D8 | 动态改文本是主路径 | `Text` setter 同步重渲整棵子树；`BindText(Observable<string>)` 响应式；get 返回上次设入的源串 | XML 占位 / 空 + C# 灌入是核心诉求；i18n / 实时刷新走 BindText |
| MD-D9 | runtime 内容锁 | `text` 经 DefaultText 锁：`PeekDefaultText()` 返回当前源串，runtime 改过后 resize/Variant/Theme 的 ReSolve 不拿 XML 声明值打回 | 现成机制（同 `<Text>`）；动态内容稳，声明默认值仅初始用 |
| MD-D10 | 图片来源 | 委托可注入 resolver `Func<string, Awaitable<Texture2D>>`（`UI.Markdown.ImageResolver`，可逐控件覆盖）；内置 `UseWebImageResolver()`（`UnityWebRequestTexture` + URL→Texture 缓存） | 同 `SourceResolver` 哲学（库不自己读资源）；"web 下载"即内置 web resolver；`Awaitable` + 无 `System.Threading` → WebGL 安全 |
| MD-D11 | 图统一用 RawImage | 所有块级图 → `<RawImage type="contain">`，纹理由控件 async 设入；不分流到 `<Image src>` | 任意 web 纹理 `<Image>`（走 SpriteSet sprite）放不下；resolver 已抽象 url/key→Texture，渲染端统一 |
| MD-D12 | 异步流式 | `Text` setter 同步出文字 + 布局；每张图先占位（alt 文本 / 透明框），纹理到了再 swap + AspectRatioFitter 重算 | 文档秒出，图渐进补；不阻塞 UI |
| MD-D13 | 行内格式 | 段落 / 标题的行内（粗 / 斜 / 删 / 行内码 / 链接 / 可选行内图）整体编成**一个 TMP 富文本串**，放一个 `<Text>` | TMP 原生标签直译；一块一个 text mesh，省节点 |
| MD-D14 | 行内码底色 | `<mark=#..>` + 等宽字体（`MarkdownStyle.CodeFont`） | TMP `<mark>` 给文字加底色，无需额外 Graphic |
| MD-D15 | 链接 | `<link="url">` + 颜色 + 下划线；点击命中（`TMP_TextUtilities.FindIntersectingLink`）→ `OnLinkClicked: Observable<string>`；默认**不**自动开 URL | 内部锚点 vs 外链路由交给业务；不替用户决定 |
| MD-D16 | 行内图 | 单独成段的 `![]()` → 块级 `<RawImage>`（无损）；夹在文字中间的行内图：url 命中已知 TMP sprite name → `<sprite name=..>`（复用 `generateTmpSpriteAsset`），否则**丢弃 + 警告**（有损） | TMP 单 mesh 放不下任意 web 纹理；行内图在文档里罕见，可控有损 |
| MD-D17 | HTML | 不支持：HtmlBlock / HtmlInline 剥标签，只保留其纯文本 | 用户明确不需要；纯 uGUI 渲染 HTML 不现实 |
| MD-D18 | 表格 | GFM 表格 → `<VStack>`（每行一个 `<HStack>`，每格 `<Text width="stretch" wrap>` 等分列宽）；表头行加粗；列数 = 各行最大格数 | 纳入首版；等分 `stretch` 列比 `GridLayoutGroup` 定长 cell 更适合变宽文本（自动行高、填满宽度、可换行）；无单元格边框线（v1 有损）；解析需 `.UseAdvancedExtensions()`（含 PipeTables + TaskLists + 删除线）|
| MD-D19 | 任务列表 | `[x]`/`[ ]` → 列表项符号换成勾选字形 `☑`/`☐`（非交互） | 纳入首版；首版不做活 Toggle（渲染文档无需勾选交互） |
| MD-D20 | 样式 | `MarkdownStyle`（纯 C# POCO）+ `UI.Markdown.DefaultStyle` 全局 + 控件 `Style` 覆盖；少量常用项开成 XML 属性 | 字体走 `FontApplier`、颜色走 `UI.Theme.Resolve`；完整控制走 C# |
| MD-D21 | 重渲生命周期 | `Text` set / `BindText` 推值 → Dispose 旧渲染根 + 重新 `InstantiateNode`；动态子节点不在 `Screen._nodeMap`，控件 `Dispose` 时显式释放（同 Carousel/ScrollList） | 简单正确；变 Variant 不重建（内容锁 MD-D9），只有内容真变才重渲 |
| MD-D22 | 异步竞态 | 每次重渲自增一个 `_renderGen` 令牌；图 async 回来先比对令牌，过期则丢弃（不 set 到已重建/已销毁的 RawImage） | 防快速连续 setText / Close 时旧图回填到新树 |
| MD-D23 | 文本转义 | renderer 产出的**字面文本段**把 `&`/`<`/`>` 转义成 `&amp;`/`&lt;`/`&gt;`（TMP 解码），防 markdown 正文里的 `<`/`&` 破坏 TMP 标签 | 正确性必需；TMP 富文本对未转义 `<` 会当标签吃掉 |
| MD-D24 | 文档高度 / 滚动 | 控件**自带竖向 `ScrollRect`**（无开关，默认就套）：`OnAttached` 建 `Viewport(RectMask2D)` + 渲染根作为 `ScrollRect.content`（`anchor=top-stretch`、pivot 顶、竖向 `ContentSizeFitter`），宽度跟视口、高度跟内容；超视口自动可滚 | 用到 Markdown 基本不会只有一两行，文档高度天然不定；默认内置滚动省去作者每次手套容器，比加 `scroll=` 开关更省心；复用 ScrollList 同款 ScrollRect+Viewport+Content 模式 |

---

## 3. 包结构 / 依赖门控（照搬 Addressables 软依赖）

| 放哪 | 内容 | 依赖 Markdig？ |
|---|---|---|
| **Runtime**（始终编译，无 Markdig 引用） | `Markdown : Control`、`IMarkdownRenderer`、`MarkdownStyle`、`MarkdownRenderResult`、`ImageRequest`、`UI.Markdown` 静态门面、`BuiltinPrimitives` 注册行、lint | 否 |
| **`PromptUGUI.Markdown` asmdef**（`defineConstraints:["PROMPTUGUI_HAS_MARKDIG"]` + `precompiledReferences:["Markdig.Signed.dll"]` + refs `PromptUGUI.Runtime`） | `MarkdigRenderer : IMarkdownRenderer`（命名空间 `PromptUGUI.MarkdigBackend`）+ 自注册 hook | 是 |
| **PromptUGUI.Editor** | `MarkdigDetector`：`AppDomain` 扫到 `Markdig` 程序集 → 加 `PROMPTUGUI_HAS_MARKDIG`，否则移除 | — |

**符号怎么定义？** Markdig 非 UPM 包，`versionDefines` 抓不到（它只认 manifest 里的 UPM 包名），所以不能像 Addressables 那样自动 versionDefine。改由 `Editor/MarkdigDetector.cs`（`[InitializeOnLoad]` 类 + 静态构造函数）扫 `AppDomain.CurrentDomain.GetAssemblies()`，发现 `Markdig` 或 `Markdig.Signed` 就往 `PlayerSettings` 的 Scripting Define Symbols 加 `PROMPTUGUI_HAS_MARKDIG`、消失就移除。这样"装了 Markdig（NuGetForUnity / DLL）→ 自动点亮"，无需用户手动配符号。

**`MarkdigRenderer` 自注入时机**（Play + Edit/Test 都要）：

```csharp
// PromptUGUI.MarkdigBackend，门控 asmdef
internal static class MarkdigBootstrap
{
    [UnityEngine.RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    static void Install()
    {
        Inject();
        PromptUGUI.Application.UI.OnReset += Inject;   // 每次 ResetForTests 后重注入（测试隔离）
    }
    static void Inject() => PromptUGUI.Application.UI.Markdown.Renderer ??= new MarkdigRenderer();
}
```

`MarkdigRenderer` 无状态。`UI.ResetForTests()` **清** `Renderer` + `ImageResolver` + `DefaultStyle` + 图缓存；门控 asmdef 既在 domain load 注入、又订阅 `UI.OnReset` 在每次 reset 末尾（§UI.cs `OnReset?.Invoke()`）重注入。好处：测试里临时设的假 renderer 在下次 reset 后被真 renderer 取代，全量套件隔离干净；没装 Markdig 的工程 `OnReset` 无此订阅 → `Renderer` 保持 null → 降级。`UI.OnReset` 需从 `internal` 放宽到 `public`（或加 public 注册 API）让门控 asmdef 订阅。

---

## 4. XML 形态

### 4.1 动态内容（主用例，MD-D8）

```xml
<!-- 空着 / 占位，运行期 C# 灌入 -->
<Markdown id="patchNotes" anchor="stretch" margin="16"/>
<Markdown id="help"       anchor="stretch" text="加载中…"/>
```

### 4.2 静态内联（小型/固定文档，配 CDATA 防 markdown 里的 `<`/`&` 破坏 XML）

```xml
<Markdown id="about" anchor="stretch" margin="16" bodyFont="default"><![CDATA[
# 关于本作

一个 **像素风** 横竖屏自适应的小游戏。

- 列表项一
- 列表项二

更多见 [官网](https://example.com)。
]]></Markdown>
```

`text` 注册为 `defaultTextAttr`，内部文本（含 CDATA）天然流入（parser 把 `el.InnerText.Trim()` 写进 `node.TextContent`，`ControlAttributeApplier` 映射到 `defaultTextAttr`）。

### 4.3 少量样式直接写在标签上（完整样式走 C# `Style`）

```xml
<Markdown id="doc" anchor="stretch"
          bodyFont="default" codeFont="mono"
          linkColor="primary" spacing="8"/>
```

---

## 5. 属性表

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `text` | markdown 源串（XML 内联用 CDATA） | `""` | 文档内容；`defaultTextAttr`；runtime 改过后 ReSolve 不打回（MD-D7/D9） |
| `bodyFont` | font type 名 | `"default"` | 正文 / 标题字体（走 `FontApplier`） |
| `codeFont` | font type 名 | `"default"` | 行内码 / 代码块字体（建议配等宽 font type） |
| `linkColor` | color（token/hex/`/alpha`） | 主题链接色 | 链接文字色（`<link>` 段）|
| `spacing` | float | `MarkdownStyle` 默认 | 块间竖向间距 |
| `wrap` | bool | `true` | 段落是否换行（传给各段 `<Text>`）|

> 仅暴露最常用的几项为属性；字号阶梯 / 引用条色 / 列表缩进 / hr 粗细等完整项走 C# `Style`（§6.4）。color 类取值同其它控件（token / hex / CSS 名 / `/alpha`）。`text` 之外的属性是幂等的，ReSolve 正常重应用。

约束：

- `<Markdown>` 内容来自 `text`，**不应写子元素**；写了 → lint `PUI-MARKDOWN-NO-CHILDREN`（warning），运行期忽略子元素（不会被当卡片之类处理）。

---

## 6. C# API

### 6.1 `Markdown : Control`（Runtime，始终编译）

```csharp
namespace PromptUGUI.Controls
{
    public sealed class Markdown : Control
    {
        [UIAttr("text"), Preserve]
        public string Text { get; set; }        // set = 重渲；get = 上次设入的源串

        [UIAttr, Preserve] public string BodyFont  { set; }   // 默认 "default"
        [UIAttr, Preserve] public string CodeFont  { set; }   // 默认 "default"
        [UIAttr(IsColor = true), Preserve] public string LinkColor { set; }
        [UIAttr, Preserve] public float  Spacing   { set; }
        [UIAttr, Preserve] public bool   Wrap      { set; }   // 默认 true

        public MarkdownStyle Style { get; set; }              // 逐控件样式；null = UI.Markdown.DefaultStyle
        public Func<string, Awaitable<Texture2D>> ImageResolver { get; set; } // 逐控件覆盖；null = 全局

        public Observable<string> OnLinkClicked { get; }      // 点击 [..](url) → url

        public IDisposable BindText(Observable<string> source);  // 订阅 + 每次重渲；AddTo(screen) 释放

        internal override string PeekDefaultText();           // => 当前源串（DefaultText 锁，MD-D9）
        public override void Dispose();                       // 释放动态子树 + R3 订阅 + 取消在途图请求
    }
}
```

> 实现注意：类名 `Markdown` 与 `<Text>` 控件类型 `Text` 同处 `PromptUGUI.Controls` 命名空间；本类**不**按裸名引用 `Text` 类型（只用字符串标签 `"Text"` 建 IR），避免与任何同名标识符歧义。

### 6.2 `UI.Markdown`（嵌套静态门面，同 `UI.Locale`/`UI.Theme`/`UI.Toast`）

```csharp
public static partial class UI
{
    public static class Markdown
    {
        public static IMarkdownRenderer Renderer { get; set; }   // 门控 asmdef 在 domain load + 每次 OnReset 后注入
        public static MarkdownStyle DefaultStyle { get; set; } = MarkdownStyle.CreateDefault();
        public static Func<string, Awaitable<Texture2D>> ImageResolver { get; set; }

        public static void UseWebImageResolver();   // 装内置 UnityWebRequestTexture + URL→Texture 缓存
        // internal: ResetForTestsInternal() 清 Renderer + ImageResolver + 重置 DefaultStyle + 清图缓存
        //           （门控 asmdef 经 UI.OnReset 在本次 reset 末尾重注入真 Renderer）
    }
}
```

### 6.3 `IMarkdownRenderer` + 结果类型（Runtime 公开，MD-D6）

```csharp
namespace PromptUGUI
{
    public interface IMarkdownRenderer
    {
        MarkdownRenderResult Render(string markdown, MarkdownStyle style);
    }

    public sealed class MarkdownRenderResult
    {
        public ElementNode Root { get; set; }                  // 渲染根（含全部块的 IR 子树）
        public IReadOnlyList<ImageRequest> Images { get; set; } // 待 async 加载的块级图
    }

    public readonly struct ImageRequest
    {
        public string NodeId { get; }   // 树内 RawImage 节点的生成 id（控件用 root.Get<RawImage>(NodeId) 定位）
        public string Url { get; }       // 交给 ImageResolver
        public string Alt { get; }       // 占位 / 失败兜底文本
    }
}
```

### 6.4 `MarkdownStyle`（纯 POCO，MD-D20）

```csharp
public sealed class MarkdownStyle
{
    public float[] HeadingSizes;     // h1..h6 字号（长度 6）
    public float   BodySize;         // 正文字号
    public string  BodyFont;         // font type
    public string  CodeFont;         // 等宽 font type
    public string  LinkColor;        // token/hex
    public string  CodeBackground;   // 行内码 / 代码块底色（token/hex，支持 /alpha）
    public string  QuoteBarColor;    // 引用左条色
    public float   BlockSpacing;     // 块间距
    public float   ListIndent;       // 每级列表缩进
    public string  BulletGlyph;      // 无序列表符号（默认 "•"）
    public string  CheckedGlyph;     // 任务列表已选（默认 "☑"）
    public string  UncheckedGlyph;   // 任务列表未选（默认 "☐"）
    public string  HrColor;          // 分割线色
    public float   HrThickness;      // 分割线粗细
    public static MarkdownStyle CreateDefault();
}
```

### 6.5 用法示例

```csharp
var md = screen.Get<Markdown>("patchNotes");

UI.Markdown.UseWebImageResolver();                    // 一次性：开启 web 图下载
md.OnLinkClicked.Subscribe(Application.OpenURL).AddTo(screen);

md.Text = await Http.GetString(url);                  // 文字秒出、图异步补
md.BindText(localizedMarkdownStream).AddTo(screen);   // 或响应式（i18n / 实时）
```

---

## 7. 渲染管线 / 块映射

```
md 串 ──Markdig.Parse──> MarkdownDocument(AST) ──遍历──> ElementNode 树 + ImageRequest[]
                                                              │
                              控件 InstantiateNode(Root, ChildHostTransform, owner)
                                                              │
                              对每个 ImageRequest: _ = LoadImageAsync(id, url)（异步补）
```

renderer（门控 asmdef）只产 IR + 图请求；控件（Runtime）负责实例化 + async 图。

### 7.0 程序化层级（固定，MD-D24）

```
Markdown (root RectTransform + ScrollRect[vertical only])
└── Viewport (RectTransform + RectMask2D, anchor stretch)        ← ChildHostTransform；裁掉出框内容
    └── Root  (VStack, anchor top-stretch, pivot 顶,             ← renderer 产出；= ScrollRect.content
              VerticalLayoutGroup + ContentSizeFitter[vertical])    高度跟内容、宽度跟视口
        ├── block 0  (Text / RawImage / Grid / 嵌套 VStack …)
        ├── block 1
        └── …
```

- `OnAttached`：建 `Viewport`(RectMask2D，stretch 填满 root) + `ScrollRect`(`horizontal=false`、`vertical=true`、`viewport=Viewport`)；`ChildHostTransform` 指向 `Viewport`。
- 每次重渲：把新 `Root` 实例化进 `Viewport`、设 `ScrollRect.content = Root.RectTransform`、滚动位置归顶（§9.1）。
- 自建 ScrollRect（同 Carousel 自建 Viewport/Strip），不复用 `<ScrollList>` 控件——结构同款但 ScrollList 走 itemTemplate+BindItems 的列表语义，这里是单棵渲染树。

### 7.1 块级映射

| Markdown 块 | ElementNode | 备注 |
|---|---|---|
| Document | `Root` = `<VStack spacing=BlockSpacing>` | 控件把它当唯一托管子节点实例化 |
| Heading h1–h6 | `<Text>` fontSize=HeadingSizes[n-1] + `<b>` | font=BodyFont |
| 段落 | `<Text wrap>` + 行内富文本串 | 见 §7.2 |
| 无序列表 | `<VStack>`，每项 = `<HStack>`（符号 `<Text>` + 内容 `<Text>`） | 嵌套 → 内容里再嵌 `<VStack>`；缩进 = ListIndent×层级 |
| 有序列表 | 同上，符号为 `"1."` 递增 | start 跟 Markdig |
| 任务列表项 | 项符号换 CheckedGlyph/UncheckedGlyph | MD-D19，非交互 |
| 引用 `>` | `<HStack>`：左 `<Image width=HrThickness color=QuoteBarColor>` + 右 `<VStack>`（递归块） | 可嵌套 |
| 围栏代码块 ``` | `<Text font=CodeFont wrap=false>`，代码背景色通过行内 TMP `<mark=…>` 标签应用（无独立容器节点）| 语言标记首版忽略（无语法高亮）|
| 分割线 `---` | `<Image width=stretch height=HrThickness color=HrColor>` | |
| 块级图 `![alt](url)` | `<RawImage type="contain">` + 生成 id → `ImageRequest` | 纹理 async 设入（§8）|
| GFM 表格 | `<VStack>` → 每行 `<HStack>` → 每格 `<Text width="stretch" wrap>`；表头行加粗 | MD-D18；等分列、无边框线 |
| HtmlBlock | 当纯文本（剥标签）/ 丢弃 | MD-D17 |

### 7.2 行内映射（编成一个 TMP 富文本串，MD-D13）

| 行内 | TMP |
|---|---|
| 字面文本 | 转义 `& < >` → `&amp; &lt; &gt;`（MD-D23）|
| `**粗**` | `<b>…</b>` |
| `*斜*` | `<i>…</i>` |
| `~~删~~` | `<s>…</s>` |
| `` `码` `` | `<mark=CodeBackground><font="CodeFont">…</font></mark>`（MD-D14）|
| `[文字](url)` | `<link="url"><color=LinkColor><u>文字</u></color></link>`（MD-D15）|
| 行内图 | 命中 TMP sprite name → `<sprite name=..>`；否则丢弃 + 警告（MD-D16）|

### 7.3 链接点击

控件给根 `<Text>` 们装一个共享的指针点击监听（或在控件层用 `IPointerClickHandler`），命中时 `TMP_TextUtilities.FindIntersectingLink(tmp, screenPos, cam)` 取 `linkId`（= url），`_onLinkClicked.OnNext(url)`。默认不开 URL。

---

## 8. 图片 / web 下载（MD-D10/D11/D12/D22）

- 块级图 → `<RawImage type="contain">` 占位（先无纹理 / alt 文本）。
- 控件实例化后，对每个 `ImageRequest`：

```csharp
async Awaitable LoadImageAsync(int gen, ImageRequest req)
{
    var resolver = ImageResolver ?? UI.Markdown.ImageResolver;
    if (resolver == null) { /* 显示 alt 文本兜底 */ return; }
    var tex = await resolver(req.Url);
    if (gen != _renderGen || tex == null) return;          // MD-D22 过期/失败丢弃
    _renderedRoot.Get<RawImage>(req.NodeId).Texture = tex;  // 触发 AspectRatioFitter 重算
}
```

- 内置 `UI.Markdown.UseWebImageResolver()`：`UnityWebRequestTexture.GetTexture(url)` + `await SendWebRequest()`（`Awaitable`，无 `System.Threading`，WebGL 安全）；同一 URL 进静态 `Dictionary<string,Texture2D>` 缓存，缓存持有纹理所有权（控件重渲 / Dispose **不**销毁共享纹理）；`ResetForTests` 清缓存并销毁其中纹理。
- 本地 / Resources / Addressables 图：用户自己装对应 resolver（库不替用户读资源，同 `SourceResolver` 哲学）。

---

## 9. 生命周期 / ReSolve / 降级

### 9.1 重渲序列（`Text` set / `BindText` 推值，MD-D21）

1. `_renderGen++`（作废在途图请求）。
2. Dispose 旧 `_renderedRoot`（连带其子树；动态建的不在 `Screen._nodeMap`，必须显式 Dispose，同 Carousel/ScrollList）。
3. `renderer = UI.Markdown.Renderer`；为空 → 降级（MD-D5）：`_renderedRoot = InstantiateNode(<Text wrap text=原文>, host, owner)` + 一次性 warning，返回。
4. `var result = renderer.Render(Text, Style ?? UI.Markdown.DefaultStyle)`。
5. `_renderedRoot = InstantiateNode(result.Root, ChildHostTransform, UI.OwnerScreenOf(this))`（`ChildHostTransform` = `Viewport`，§7.0）。
6. `_scrollRect.content = _renderedRoot.RectTransform`；`_scrollRect.verticalNormalizedPosition = 1f`（新内容滚动归顶）。
7. 对 `result.Images` 逐个 `_ = LoadImageAsync(_renderGen, req)`。

降级路径（步骤 3）同样把兜底 `<Text>` 实例化进 `Viewport` 并设为 `ScrollRect.content`。renderer 产的 `Root` 是 `<VStack anchor="top-stretch" pivot 顶 spacing=BlockSpacing>` + 竖向 `ContentSizeFitter`：宽度跟视口、高度跟内容自顶向下生长，超视口由 ScrollRect 滚动（MD-D24 / §7.0）。

**重渲时机（合并，避免一次 apply 里多次重渲）**：所有会影响渲染的 setter（`Text` + 样式属性）只**标脏**；初始 apply 期间不立即渲染，统一在 `OnAfterApply()`（晚于 `ApplyCommon`，控件已定尺寸）`RenderIfDirty()` 一次。控件已 live 之后（运行期 `Text=` / `BindText` 推值）setter 直接 `RenderIfDirty()` 同步重渲。用一个 `_applied` 标志区分两阶段。

### 9.2 ReSolve（resize / Variant / Theme / Locale）

- `text` 经 DefaultText 锁（MD-D9）：runtime 灌过 → 不被 XML 声明值打回；只有声明值确实变（如 Variant 改 `text.portrait`）且 runtime 没动过时才重渲。
- 其它属性（`bodyFont`/`spacing`/`linkColor`…）幂等重应用；**注意**：样式变更需重渲才生效（它们烘进了已实例化的子树）。首版策略：样式属性 setter 标脏，`OnAfterApply` 末尾若脏且有内容则重渲一次（避免每个 setter 各重渲一遍）。

### 9.3 降级（MD-D5）

无 renderer → 原文进 `<Text wrap>` + 一次性 `Debug.LogWarning("<Markdown> needs Markdig: install it (NuGetForUnity / DLL); the editor auto-defines PROMPTUGUI_HAS_MARKDIG when found.")`。

---

## 10. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| `Text == null`/`""` | 清空子树，不渲染 |
| 未装 Markdig（`Renderer==null`） | 原文 `<Text>` 兜底 + 一次性 warning（MD-D5）|
| markdown 语法错误 / 半截 | Markdig 宽容解析，基本不抛；renderer try/catch 兜底 → 降级原文 + warning |
| 图 url 无 resolver | 该图显示 alt 文本占位（不 throw）|
| 图下载失败 / 超时 / 404 | resolver 返回 null → 保留 alt 占位（§8）|
| 快速连续 set `Text` | `_renderGen` 作废旧树的在途图（MD-D22）|
| set `Text` 后立即 Close | Dispose 取消在途图、释放 R3、销毁子树 |
| `<Markdown>` 写了子元素 | lint `PUI-MARKDOWN-NO-CHILDREN`（warning）；运行期忽略 |
| 行内图 url 不命中 sprite | 丢弃该行内图 + 一次性 warning（MD-D16）|
| 表格列数不齐（行比表头多/少格） | 按表头列数截断 / 补空（Markdig 已规整）|
| 巨型文档（上千块） | 首版不虚拟化；文档过大建议作者自行分页（Out of Scope 记一笔）|

---

## 11. Lint 规则

`Runtime/Core/Lint/MarkdownRules.cs`（新文件，static `CheckMarkdown(ElementNode)`，同 `TabRules`/`CarouselRules` 模式）；`IRWalker` 入口加 `Markdown` 分支；`ScreenInstantiator` 同源 warning。

| Code | 触发 | 信息（节选） | 级别 |
|---|---|---|---|
| `PUI-MARKDOWN-NO-CHILDREN` | `<Markdown>` 有子元素 | "Markdown 内容来自 text=（或内联 CDATA），子元素会被忽略；删除子元素。" | warning |

---

## 12. 测试策略

- **renderer 树形**（`PromptUGUI.Tests.EditMode.Markdown`，defineConstraint `PROMPTUGUI_HAS_MARKDIG`，镜像 Addressables 测试 asmdef）：给定 markdown 断言 `MarkdownRenderResult.Root` 的标签/属性/嵌套（纯函数，无 GameObject）——标题字号、列表嵌套、引用条、代码块底色、表格 Grid 列数、任务列表字形、行内 `<b>/<i>/<s>/<mark>/<link>`、转义、图请求收集。
- **控件集成**（同上 asmdef，需 renderer 在场）：`Text` set → 子树层级；`BindText` 推值重渲；DefaultText 锁（runtime set 后 ReSolve 不打回）；`OnLinkClicked`；图占位 → 假 resolver 返回纹理后 swap；`_renderGen` 作废旧图；Dispose 释放。
- **降级**（主 EditMode 套件，控件始终编译）：临时 `UI.Markdown.Renderer = null` → 原文进 `<Text>` + 不抛。
- **PlayMode**：`UseWebImageResolver` 用本地/假 URL 走一遍 `UnityWebRequestTexture` 异步路径（或注入假 resolver 测流式占位→swap 的帧序）。
- 主套件保持绿；新控件标签始终存在（MD-D4）→ 无需给现有测试加 `#if`。

---

## 13. 整合点

### 13.1 主 spec `2026-05-07-...-design.md` §5（控件表）追加一行

> `<Markdown>` | Markdown 文档渲染容器；块级映射到现有控件（标题/段落/列表/引用/代码块/表格/分割线/图/链接）；动态 `text` 灌入、resize 不重置；软依赖 Markdig（`PROMPTUGUI_HAS_MARKDIG`）| Control 壳 + IR 子树（详见 [`2026-06-09-markdown-control-design.md`](2026-06-09-markdown-control-design.md)）

### 13.2 `authoring-promptugui-xml/SKILL.md`

1. Built-in primitives 表追加 `<Markdown>` 行（attrs 见 §5）。
2. 主文档加 stub 指针 → 新增 `reference/controls-markdown.md`：动态/静态/CDATA 用例、属性、支持的 markdown 子集与有损项（HTML 剥离、行内图、无语法高亮）、lint code、"内容来自 text、子元素被忽略"，**以及 Markdig 安装 + `PROMPTUGUI_HAS_MARKDIG` 符号门控的 "Setup" 小节**（直接并入本 reference，不单开 skill）。

### 13.3 `scripting-promptugui-csharp/SKILL.md`

- 新增 `Markdown` 段：`Text` get/set（动态灌入主路径）、`BindText`、`OnLinkClicked`、`Style`、逐控件 `ImageResolver`；`UI.Markdown.DefaultStyle` / `UseWebImageResolver` / 全局 `ImageResolver`。
- 一句：`text` 是运行期内容，set 后 resize/Variant/Theme 不打回（同 `<Text>` 的 DefaultText 锁）。

### 13.4 Markdig 安装 / 符号 → 并入 `reference/controls-markdown.md` 的 "Setup" 小节（不单开 skill）

- 装 Markdig（NuGetForUnity / 手放 DLL；宿主用签名变体 `Markdig.Signed`）；editor `MarkdigDetector` 自动定义 `PROMPTUGUI_HAS_MARKDIG`（或手动配）；门控 asmdef 自动点亮 `MarkdigRenderer`；未装时 `<Markdown>` 原文降级。
- **不像 Addressables 那样单开顶层 skill**：Markdown 只是一个控件，安装/符号说明作为它 reference 文档的一个小节即可（避免 skill 目录膨胀）。

### 13.5 XSD

随新 `[UIAttr]` 手动 regenerate；生成器测试加 `<Markdown>` substring 断言。

---

## 14. Out of Scope（首版不做）

- **语法高亮**——代码块只给等宽 + 底色，不解析语言做着色。
- **行内图任意 web 纹理**——只支持命中 TMP sprite 的行内图；其余有损（MD-D16）。
- **HTML 渲染**——剥离（MD-D17）。
- **横向滚动**——内置 ScrollRect 仅竖向（MD-D24）；超宽表格 / `wrap=false` 长代码行被 `RectMask2D` 横向裁掉；横向滚动（代码/表格）留 v2。
- **超大文档虚拟化 / 懒渲染 / 分页**——首版一次性建全树；超大文档作者自行分段。
- **可点击任务列表**——`[x]` 仅作字形展示，不可勾选。
- **脚注 / 定义列表 / 数学公式 / 表情 emoji shortcode**——Markdig 扩展，首版不接。
- **自定义 block → 控件 的用户映射钩子**——首版映射固定在 `MarkdigRenderer`；以后可开扩展点。
- **选择 / 复制文本**——TMP 默认不可选；要可选另说。

---

## 15. 风险与回滚

| 风险 | 缓解 |
|---|---|
| `MarkdigDetector` 漏检测 Markdig（NuGetForUnity 装法各异）→ 符号没定义、`<Markdown>` 一直降级 | 检测器扫 `AppDomain` 程序集名（最稳）；mini-skill 给手动定义符号的退路 |
| Runtime 控件 + 门控 asmdef 的 renderer 注入时序：控件先 set `Text` 而 renderer 未注入 | 注入走 `RuntimeInitializeOnLoadMethod`(BeforeSceneLoad) + `InitializeOnLoadMethod`，早于任何 Screen.Open；真出现 null → 降级（MD-D5）不炸 |
| TMP 富文本转义不全 → 正文 `<`/`&` 破坏标签 | MD-D23 字面段统一转义 `& < >`；renderer 单测覆盖 |
| TMP `<mark>` / `<link>` / `<sprite>` 在当前 TMP 版本行为差异 | 复用项目已用的 TMP 能力（inline-tmp-sprite PR 已验证 `<sprite>`）；plan 加冒烟测 |
| 异步图回填到已重建/已销毁的 RawImage → NRE | `_renderGen` 令牌比对 + Dispose 置无效（MD-D22）|
| web 图缓存纹理泄漏 | 缓存集中持有；`ResetForTests` 销毁；运行期长生命周期缓存是有意为之（重复 url 复用）|
| 独立 asmdef 在没装 Markdig 的机器上编不过 | `defineConstraints` 保证符号未定义时该 asmdef 整体不编译；检测器保证符号 ON ⟺ Markdig 在场 |
| `InstantiateNode` 动态子树的 id（图节点）作用域 | 复用 Carousel 已验证路径（`card.Get<Text>("title")` 同款）；plan 加断言 |
| 样式属性变更需重渲才生效，易漏 | §9.2 标脏 + `OnAfterApply` 统一重渲一次；文档写明"样式变更触发重渲" |
| 嵌套布局（ScrollRect.content 的 ContentSizeFitter + 多层 VStack/列表/引用的 LayoutGroup + 异步图改高）布局时序导致 content 高度算错 / 滚动条不更新 | 实例化后 `LayoutRebuilder.ForceRebuildLayoutImmediate(content)`；图 swap 后再强制重算一次；plan 加 PlayMode 烟雾测（长文档可滚到底、图回填后高度增长） |
| Markdig IL2CPP code-stripping | mini-skill 提示按需加 `link.xml`；Markdig 纯托管、无反射重灾区，风险低 |
| XSD 不自动更新 | 同所有新 `[UIAttr]`，手动 regenerate（CLAUDE.md 已说明）|
