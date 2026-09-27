# `.ui.xml` 导入时去注释

> 状态：**已实现**，待 PR（§9 按推荐项已定，实施记录见 §10）。分支 `feat/strip-xml-comments`。
> 相关：`Editor/PoFileImporter.cs`（同一套 importer override 机制）；`Runtime/Core/Parser/LineInfoXmlDocument.cs` +
> `Runtime/Core/Lint/SourceLocation.cs`（`UILog` 的 `src:line` 从这里来）。

## 1. 问题

`.ui.xml` 走 Unity 原生的 `TextScriptImporter`，进包的是逐字节原文，注释全在：

- ssw_re_client：39 个文件 242 KB，其中注释 121 KB（**50%**，393 段），是中文设计说明，如
  `<!-- 模板拆到 Templates/ 子目录（AA Key = 工程路径） -->`。它们在 Addressables 的 `Remote UI` 组里经 CDN 下发，
  下载 bundle、用 AssetStudio 打开即可读到。
- 本包 `Runtime/Resources/PromptUGUI/` 的 8 个 `.ui.xml` 注释约占 19%，进每个用户的包。

目标：**包体里的 `.ui.xml` 不含注释，且运行时报错的行号仍对得上源文件。** 不做 minify，不做混淆（§7）。

## 2. 方案

导入时去注释。一个 `ScriptedImporter` 以 `.xml` 的 override 身份接管所有 `*.ui.xml`，产出去注释后的 `TextAsset`：

```
Foo.ui.xml ──UiXmlImporter──> TextAsset "Foo.ui"（去注释，行号不变）
                                 ├─ Resources.Load / Addressables / 自定义 resolver 读 .text
                                 └─ Player / AssetBundle（同一份导入产物）
```

- 机制与 `.po` 相同（`Editor/PoFileImporter.cs:11`）：`.xml` 是原生扩展名，Unity 手册规定 ScriptedImporter 只能经
  `overrideExts` 注册、再按路径 `SetImporterOverride`；由 AssetPostprocessor 自动分派（§3.3）。ssw 的 `.po` 就是这样导入、
  再经 Addressables 加载的。
- 编辑器里的 TextAsset 也是去注释版（导入产物只有一份），这没有副作用：
  - 全仓库没有代码处理 `XmlComment`，也没有代码依赖 `.ui.xml` 的导入器类型；
  - 需要原文的编辑器工具全读磁盘（lint 菜单、i18n 提取、XSD、UIPreview、图集同步、LinearTint 都是 `File.ReadAllText`）；
    唯一读 `TextAsset.text` 的 `PromptUGUIDocumentHostEditor.cs:78` 只拿去解析；
  - 热重载经 resolver 读 `.text`，行号不变（§3.2），`UILog` 定位照旧。

否决：

| 方案 | 为什么不 |
|---|---|
| 打包前改写源文件、打包后还原 | 构建失败或编辑器崩溃时还原不跑，源文件就停在去注释状态；Player 构建与 Addressables 单独 Build Content 两个入口都要各挂一遍 |
| 构建期切换导入行为（custom dependency + 全量重导） | 同样要覆盖两个构建入口，每次构建重导两遍；换来的「编辑器里保留注释」没有用处（见上） |
| 运行时在 resolver 里去注释 | 注释照样进包 |
| 以复合扩展名 `ui.xml` 注册默认导入器 | 能否压过原生 `.xml` 没有文档保证；override 路线已在 `.po` 上验证 |

## 3. 语义

### 3.1 `UiXmlImporter`（`Editor/UiXmlImporter.cs`）

```csharp
[ScriptedImporter(1, (string[])null, new[] { "xml" })]
internal sealed class UiXmlImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var text = XmlCommentStripper.Strip(File.ReadAllText(ctx.assetPath));
        var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
        ctx.AddObjectToAsset("text", asset);
        ctx.SetMainObject(asset);
    }
}
```

- 主对象名 `Foo.ui`，与 TextScriptImporter 相同；Resources 查找名由路径决定，仍是 `Foo.ui`（§5-14 锁住）。
- 行尾原样保留；`File.ReadAllText` 会去掉 BOM。
- 剥离器原样返回、而原文里有注释时（§3.2 的几种情况），`ctx.LogImportWarning` 说明原因；文件照原样进包。
- 剥离规则变化时 bump 版本号，让缓存的导入结果失效。

### 3.2 `XmlCommentStripper.Strip(string) → string`（`Editor/XmlCommentStripper.cs`，internal，纯 C#）

**不变式**（也是测试判据）：

1. 输入在注释 / CDATA / PI / 标签层面畸形 → **原样返回**。运行时在同一位置报同一个 XML 错误：剥离既不会把坏文件「修好」，也不会造出新错误。
2. 否则输出不含注释，且解析器看到的 XML 树与原文相同：元素、属性、每个元素的直接文本，以及每个元素的 `Line`。
   `UIDocumentParser` 的 IR 只由这几样派生，所以 IR 也相同。

单遍扫描：

| 遇到 | 处理 |
|---|---|
| `<!--` | 注释，止于第一个 `--`，其后必须是 `>`（XML 不许注释内出现 `--`）；否则畸形 |
| `<![CDATA[` … `]]>` | 原样保留（其中的 `<!--` 不是注释） |
| `<?` … `?>` | 原样保留；但 `<?xml` 声明不在文件开头 → 畸形（声明前有注释本来就解析失败，剥掉注释会把它「修好」） |
| 以 `<!` 开头的其他结构（`<!DOCTYPE` 等） | 整个输入原样返回（运行时 XmlReader 默认禁 DTD，本来就解析不了） |
| 其他 `<` … `>` | 标签，原样保留；找 `>` 时跳过引号内的内容（属性值里可以有字面 `>`，如 `if="a > b"`） |
| 任一结构到文件尾仍未闭合 | 畸形 |
| 任一标签里出现 `xml:space` | 整个输入原样返回（见下） |

删掉的注释，它内部的换行放哪？以「两个标记之间的文本段与注释」为一个 run。原文里 run 内每段文本都是 XmlReader 的一个独立节点，
其中全是 XML 空白（空格 / Tab / CR / LF）的段是 Whitespace 节点，被 `PreserveWhitespace = false` 丢弃：

- **run 的文本全是空白** → 原位只留注释内部的换行。多几个换行的空白照样被丢弃，不改变任何节点，其后元素的行号不变。
  **包内、`Samples~`、ssw 三处共 497 段注释全属此类。**
- **run 里有非空白文本**（注释夹在 `<Text>` 的文字里）→ 只输出非空白的文本段；注释和全空白的文本段一起删掉。全空白段不能留：
  注释一删，它会和相邻文字并成同一个节点，原本被丢弃的空白就进了文本（`abc<!--x-->⏎<!--y-->def` 原文是 `abcdef`）。删掉的
  换行记账，补到下一个全空白 run 的开头（通常就是 `</Text>` 之后的缩进），行号在下一个元素处追平。
- **标签里出现 `xml:space`** → 整个输入原样返回。`preserve` 作用域里全空白段是有意义的，删注释、补换行都可能改文本；
  为一个语料里没人用的特性做作用域分析不值得。导入器给一条警告（§3.1）。

### 3.3 自动分派（同文件里的 `AssetPostprocessor`）

```
OnPostprocessAllAssets：对 imported ∪ moved 中以 ".xml" 结尾的 path
  want = path 以 ".ui.xml" 结尾（Ordinal，同库内其它 .ui.xml 判断）
  cur  = GetImporterOverride(path)
  want 且 cur != UiXmlImporter   → SetImporterOverride<UiXmlImporter>(path)
  !want 且 cur == UiXmlImporter  → ClearImporterOverride(path)      // Foo.ui.xml 改名为 Foo.xml
```

- 只读包（`PackageInfo.source` 不是 Embedded / Local）跳过：写不了它的 meta。本包的 meta 在仓库里就带着 override（§4），
  所以 git / registry 安装同样生效。
- 新文件会先被原生导入一次、再切过来重导一次；热重载与图集同步因此多触发一次，无害（`.po` 也是这样）。
- **已有文件的补齐**：只注册 override 型导入器不会让既有的 `.xml` 重导，postprocessor 看不到它们。所以 `[InitializeOnLoad]`
  + `delayCall` 把 `AssetDatabase.GetAllAssetPaths()` 里的 `.ui.xml` 过一遍同一个分派函数；已一致的直接跳过，所以之后每次域重载
  只是一次字符串过滤。

## 4. 迁移与影响

- **meta 一次性改写**：每个 `.ui.xml.meta` 多出 `importerOverride` 块，GUID 不变。本包 `Runtime/Resources/` 的 8 个由宿主导入时写出、
  随本 PR 提交；`Samples~/` Unity 看不见，其中只有 CommonControls 带 meta（1 个），从生成的 meta 复制同一块过去；ProceduralStyle
  整个示例不带 meta，导入示例时由 postprocessor 装上。ssw 的 39 个由其作者提交。
- **直接引用会断一次**：主对象的 local fileID 由 `4900000`（TextScriptImporter）变成 identifier 的哈希
  （ssw 里 `.po` 实测为 `2811297602134710623`）。场景 / prefab / SO 里**直接拖入**的 `TextAsset` 引用（例如
  `PromptUGUIDocumentHost` 的 XML 字段）会变成 Missing，需要重拖。不受影响：`Resources.Load`、Addressables 的地址 / GUID、
  `AssetReferenceT<TextAsset>`。ssw 已查：唯一的引用是 `Lobby.unity` 的 `AssetReference`。此条写进 PR 描述。
- **覆盖不到**：不经导入管线的 XML，即 `StreamingAssets/` 里的文件，以及运行时从网络或磁盘读来的字符串。
- 运行时零改动：没有新 API，也没有新开销。

## 5. 测试（Red first，`PromptUGUI.Tests.EditorOnly`）

`XmlCommentStripperTests`（「树相同」= 输入与输出各 `LineInfoXmlDocument.Parse` 一次，逐元素比名字、属性、直接文本、`Line`）：

1. 元素之间的单行 / 多行注释 → 无注释，树相同。
2. 序言里的头注释（`<?xml?>` 与根元素之间）、根元素之后的注释。
3. CDATA 与 PI 内的 `<!--` 原样保留。
4. 属性值含 `>` 的标签。
5. `<Text>` 文字中间的单行 / 多行注释，以及两段注释之间夹着空白 → 文本逐字相同，其后兄弟元素的 `Line` 不变。
6. 畸形输入原样返回（同一实例）：未闭合的注释 / CDATA / PI / 标签、注释内含 `--`、`<!--->`、`<!DOCTYPE`、
   `<?xml` 声明前有注释。
7. CRLF 输入 → 输出里的换行仍是 CRLF。
8. 标签里有 `xml:space` → 原样返回，原因里写明 `xml:space`。
9. 无注释 → 原样返回。
10. 语料：`Runtime/Resources/` 与 `Samples~/` 的全部 `.ui.xml` → 无注释、树相同、`UIDocumentParser.Parse` 不抛。

`UiXmlImporterTests`（仿 `PoFileImporterTests`，临时目录 `Assets/PromptUGUIUiXmlTmp`）：

11. 写入带注释的 `a.ui.xml` → Refresh → override 自动为 `UiXmlImporter`；`LoadAssetAtPath<TextAsset>` 无注释、行数与源文件相同、
    `name == "a.ui"`。
12. `b.xml` 不被接管（无 override，文本仍含注释）。
13. `a.ui.xml` 改名为 `a.xml` → override 被清除，文本恢复含注释。
14. `Resources/` 下的 `c.ui.xml` → `Resources.Load<TextAsset>("…/c.ui")` 可取，且无注释。
15. 补齐：清掉一个 `.ui.xml` 的 override（模拟升级前的文件）→ 调补齐函数 → override 回来，文本无注释。

手工（ssw_re_client，Unity MCP）：

16. 升级后 39 个 meta 带上 override；进 Play，Login → Lobby → Round 正常；故意写错一个属性，`UILog` 的 `src:line` 与源文件一致。
17. 经 Addressables 读回 `Remote UI` 组里的 TextAsset，无注释。

## 6. 文档（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：一句：注释在导入时剥离，不进包体，不影响报错行号。
- `scripting-promptugui-csharp/SKILL.md`：resolver 段一句：`.ui.xml` 的 `TextAsset.text` 是去注释后的文本（行号保留）。
- `AGENTS.md`：`Editor/` extras 里加一行，点名 `UiXmlImporter`。

## 7. 非目标

- **minify**：注释才是大头；删换行会让 `src:line` 全变成 `:1`。
- **混淆 / 加密**：格式层面的打乱一键可还原；加密的密钥在客户端里（IL2CPP 下 hook `UIDocumentParser.Parse` 即可导出明文），
  而 `.po` 和图标仍是明文。需要时另案，与 `.po` 一起做。
- **关闭剥离的开关**（按工程 / 按文件）：不变式保证没有语义影响；有需要再加。
- 把直接 `TextAsset` 引用改写成新 fileID 的迁移工具。

## 8. 实现地图

| 文件 | 内容 |
|---|---|
| `Editor/XmlCommentStripper.cs`（新） | §3.2 |
| `Editor/UiXmlImporter.cs`（新） | 导入器 + 自动分派 + 已有文件补齐（§3.1、§3.3） |
| `Runtime/Resources/PromptUGUI/**/*.ui.xml.meta`、`Samples~/**/*.ui.xml.meta` | override 块（§4） |
| `Tests/EditMode/Editor/XmlCommentStripperTests.cs`、`UiXmlImporterTests.cs`（新） | §5-1 ~ 15 |
| 两个 skill、`AGENTS.md` | §6 |

顺序：stripper（§5-1 ~ 10 Red → Green）→ 导入器与分派（§5-11 ~ 15）→ meta 与文档 → ssw 手工验证（§5-16、17）→ PR。

## 9. 决策（已定，均按推荐）

| # | 项 | 决定 | 未采纳 |
|---|---|---|---|
| 1 | 开关 | 所有 `.ui.xml` 一律剥离，不设开关 | `PromptUGUISettings` 加开关（默认开） |
| 2 | 编辑器里的 TextAsset | 同样剥离（同一份导入产物，§2 已论证无副作用） | 仅构建时剥离（见 §2 否决表） |
| 3 | stripper 放哪 | `Editor/`（只有导入器用；EditorOnly 测试能访问 Runtime 与 Editor 的 internal） | `Runtime/Core/Parser`（纯 C#，CLI 将来可复用） |
| 4 | `Samples~` 的 meta | 预置 override 块 | 不动，用户导入 sample 时由 postprocessor 补写 |

## 10. 实施记录（2026-09-27，分支 `feat/strip-xml-comments`）

提交：`4d1b3f1` spec → `c1d15da` stripper → `5914b44` 导入器与分派 → `dc45702` meta → 文档。

- **动手前对草案的修正**（已并入 §3，不是实现偏离）：
  - `xml:space` 由「欠账作废、照删」改为整份原样返回。原写法在 `preserve` 作用域外也会出错：文本 run 里夹在两段注释之间的空白，
    原文里是被丢弃的 Whitespace 节点，删注释后会并进文字。
  - 于是文本 run 的规则同样要把全空白段一起删（§3.2 第二条）。§5-5 的 `abc<!--x-->⏎<!--y-->def` 用例专门锁这一点。
  - 新增「`<?xml` 声明不在开头 → 原样返回」：声明前有注释本来就解析失败，剥掉注释会把它「修好」。
  - 新增已有文件的补齐（§3.3）：只注册 override 型导入器不会让既有 `.xml` 重导，postprocessor 看不到它们。
  - 原样返回时导入器记一条 warning（§3.1）。
- **meta 的样子**：Unity 在 `importerOverride` 之后保留原来的 `TextScriptImporter:` 段（兜底设置），与 `.po` 相同。
  9 个 meta 的 override 块逐字一致（`Hash: e414a684a99476a84a1982c4d8665451`）。
- **验证**：
  - 新测试 `XmlCommentStripperTests` 18/18、`UiXmlImporterTests` 5/5，均先 Red 后 Green。
  - 全量 EditMode + EditorOnly + Addressables 4908/4908、PlayMode 245/245；`dotnet format --verify-no-changes` 通过。
  - ssw_re_client：补扫后 39/39 带 override；逐个比较磁盘原文与导入后的 TextAsset，39/39 无注释、行数相同、元素名 / 行号 /
    属性 / 直接文本全部一致（Round.ui.xml 等 393 段注释）。这组比较代替了 §5-16 里「故意写错一个属性看 `UILog` 行号」：
    行号对全部元素逐个比过，不必再改用户文件。
  - §5-16 冒烟：进 Play，游戏直接进入 Round，`Round` Screen 正常建立，Console 无 PromptUGUI 错误 / 警告。
  - §5-17：Play 中经 `Addressables.LoadAssetAsync<TextAsset>` 读回 `Remote UI` 组 39/39 无注释、行数相同（Play Mode Script
    为 Use Asset Database）。**未做真正的 bundle 构建**：它会改写 ssw 的 content state；bundle 序列化的正是这份导入产物。
- **ssw 侧**：39 个 `.ui.xml.meta` 已在 ssw 工作区被改写，由其作者提交。
- **跑测试的小坑**：加入导入器后的那次域重载，补扫会批量重导全部 `.ui.xml`，紧接着启动的测试可能初始化超时，重跑即可。
