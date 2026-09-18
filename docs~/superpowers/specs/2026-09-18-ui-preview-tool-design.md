# UI Preview —— 库内置的 Play 态 `.ui.xml` 预览工具（UIP）

> 状态：**草案**（待作者按 §11 拍板；拍板后走 plan → 分支 `feat/ui-preview-tool` → PR）。
> 相关：`2026-09-17-pages-design.md` §9（`IScreen.FindAll<T>()`——页选择器靠它枚举 `<Pages>`）；
> `2026-09-17-common-attr-runtime-state-design.md`（XML 初态所见即所得，是这个工具有价值的前提）；
> `2026-09-14-document-load-parallel-prefetch-and-source-cache-design.md`（`DocumentCache` / `UnloadAll` 清缓存——工具每次
> 加载都从磁盘拿最新版靠它）；master spec M4.4（`ReloadCommonLibraryAsync` + `HotReload.NotifyAssetChanged`）；
> `2026-09-18-commons-settings-design.md`（commons 由 `PromptUGUISettings.commonLibraries` 声明、`LoadDocumentAsync` 自动
> `EnsureCommonLibrariesAsync`，reload 保 `as=`——本文原 §4.6 / §5 的「commons 快照」方案整个由它取代）；`Editor/Preview/PromptUGUIDocumentHost*`
> （Edit 态的静态预览，本文不替换它，但要在文档里分清两者）；`Editor/I18n/PlayModeLocaleMenu.cs`（Play 态右上角的
> locale / theme 下拉，本文不重复做 locale）。
> 动机来源：宿主 ssw_re_client 的 `Assets/Tools/UI Preview/`（`UIPreview.cs` + `UIPreview.unity` + `Editor/UIPreviewMenu.cs`）
> ——用了一个月、很好用，但整套是宿主私有代码，别的项目用不上；MCP 驱动它还得反射私有方法
> （memory `unity-uipreview-scene-via-mcp`）。

## 1. 问题

宿主写了一个 Play 态预览工具：扫出工程里全部 `.ui.xml` 列成表，点一行就 `UnloadAll` → 重装 commons → 从**磁盘**读
最新版 `LoadDocumentAsync` → `Open`；改完 xml 保存，库的热重载把界面重开，工具按帧发现 Screen 换了实例就重新收集
`<Pages>` 并把上次选的页选回来。它证明了三件事：

1. **Play 态预览是刚需，Edit 态的 `PromptUGUIDocumentHost` 顶不上。** DocumentHost 走同步 `UI.LoadDocument(label, xml)`
   ——没有 `<Import>`、没有 commons 池、没有主题注册；glass / 动画 / 模态 / 输入 / 热重载事件链也只在 Play 里有。
2. **工具本体 90% 是通用的**：文件索引、磁盘优先的 src 解析（绝对路径 → 工程相对 → 唯一后缀 → 回落宿主 resolver）、
   加载流程、多 Screen 切换、Pages 页条、热重载后的重新收集。
3. **只有四个点是宿主私有的**，而它们把整个工具钉死在了那一个工程里：

| 耦合点 | 今天的形态 | 后果 |
|---|---|---|
| 预览场景 | `UIPreview.unity` 里**挂着** `UIPreview` 组件 | 工具组件写进场景资产；换项目要重建场景 |
| commons 地址 | `LoadCommonLibraryAsync("UI/Templates/DefaultTheme.ui.xml")` 写死 | 每个项目一份改过的 C#——**已由 commons-settings 解决**：settings 声明，工具不用知道 |
| boot 契约 | 预览场景不跑 `GameBootstrap` → 主题 `(none)`、图标白方块，靠 MCP 手工反射补 | 用户不知道缺了什么 |
| 入口 | `Tools/Dev/UI Preview _F8` + 写死的场景路径；`OpenScene(Single)` 踢掉正在编辑的场景 | 退出 Play 留在预览场景 |

目标：把它做成 **PromptUGUI 自带的菜单功能**——装了包就有，`Tools/PromptUGUI/UI Preview`（F8），零配置能用、
配一次能用自己的场景，宿主一行工具代码都不用写；同时给 MCP / 脚本一组稳定的公开静态入口。

## 2. 否决的方案

**A. 宿主把库提供的组件挂进自己的预览场景（今天形态的库化）。否决。** 组件仍写进场景资产；「即开即用」不成立
（必须先建场景）；并且组件的序列化字段（工程根、commons 地址）会随项目走，跟 ProjectSettings 重复。

**B. 工具自带一套 bootstrap 配置：在 Project Settings 里填 resolver 类型 / SpriteSet 列表 / 主题名 / locale。否决。**
这是把宿主 boot 抄一半到库里，两边必然漂移。宿主的 `RuntimeInitializeOnLoadMethod` 本来就在**任何**场景都跑
（ssw 的 `GameBootstrap` 就是），预览场景里样样都有；样例那种在 runner `Start()` 里装配的项目，选自己的场景
（runner 在里面）也一样有。库只需要知道「宿主装了什么」并把缺的**说出来**（§4.9），不需要自己装。

**C. 扩展 Edit 态的 `PromptUGUIDocumentHost`（补 Import / commons / 主题）。否决。** Play 独有的东西补不了（§1-1）；
且两个入口各修一半是最差的形态。DocumentHost 保留为「把静态布局摆进场景看看」的轻量用法，文档里分清（§8）。

**D. 进 Play 后由工具 `CreateScene` + 卸掉起始场景。否决。** 起始场景的 Awake / Start 已经跑过：ssw 的 Login 会开始
登录流程、`AppFlow.GoTo` 在飞——卸场景救不回来。必须在**进 Play 之前**决定起始场景。

**E. 进 Play 前 `OpenScene(预览场景, Single)`（今天的做法）。否决。** 踢掉用户正在编辑的场景组合（脏场景还要问保存），
退出 Play 后留在预览场景里。`EditorSceneManager.playModeStartScene` 就是为「Play 用另一个场景、编辑态原样保留」做的
——宿主自己的 `DevPlayFromCurrentScene` 也是这么用它的。

**F. 覆盖层用 UI Toolkit 画在 Game view 上（同 `PlayModeLocaleMenu`）。不做。** 文件列表 + 过滤 + 多行页条是
一个真正的面板，IMGUI 在 Play 里画最省事、而且 Player 侧永远不存在（工具在 Editor asmdef）；`PlayModeLocaleMenu`
那种两行下拉才适合叠在 Game view 工具条上。

## 3. 方案总览

```
F8 (Tools/PromptUGUI/UI Preview)
 │  未配置 → 首次对话框：内置空场景 / 选一个 .unity   → 写 ProjectSettings/PromptUGUIPreview.asset
 │  编译错误 → 提示退出；已在预览 Play 中 → 切换面板收起
 ▼
SessionState.Pending = true;  EditorApplication.EnterPlaymode()
 │
 ├─ ExitingEditMode：记下原 playModeStartScene → 指向 预览场景（内置：包内 Editor/Preview/UIPreview.unity）
 │                    （本次 F8 才挂的钩子，排在宿主 InitializeOnLoad 时挂的钩子之后 → 最后写的赢）
 ├─ （域重载）宿主 RuntimeInitializeOnLoadMethod 照常跑：resolver / SpriteSet / 主题 / locale / commons
 ├─ EnteredPlayMode / sceneLoaded：注入 GameObject「PromptUGUI UI Preview」+ 覆盖层组件（DontDestroyOnLoad，不进资产）
 │      覆盖层：文件索引 · 磁盘优先 resolver · 加载/切 Screen · Pages 页条 · 诊断行 · 横竖屏 · 主题
 │      点文件：UnloadAll → LoadDocumentAsync(工程相对路径)（自动重装 settings 声明的 commons，经工具 resolver 读盘最新版）→ Open → 收集 Pages
 │      保存 xml：库热重载重开 Screen → 覆盖层按帧发现实例换了 → 重新收集 + 选回上次的页
 └─ EnteredEditMode：还原 playModeStartScene，清 SessionState；覆盖层 OnDestroy 已把 SourceResolver / AssetPathToSrc 还给宿主
```

谁管什么：

| 事 | 谁 | 依据 |
|---|---|---|
| resolver / SpriteSet / 主题 / locale 的装配 | **宿主**：任意场景都跑的 `RuntimeInitializeOnLoadMethod`，或预览场景里的 runner | §2-B |
| 缺了什么 | 库：面板诊断行说出来 | §4.9 |
| commons 地址 | 不用知道：`UnloadAll` 后 `LoadDocumentAsync` 自动 `EnsureCommonLibrariesAsync`，装的是 `PromptUGUISettings.commonLibraries` 声明的那些 | §4.6 |
| 预览场景 | 内置（包内一个只有相机的场景）或用户自己的 `.unity`（ProjectSettings 记路径） | §4.2 |
| 覆盖层 | 库在 Play 时注入，用户的场景资产永远干净 | §4.3 |
| 热重载映射 | 库按「工具亲手解析过的文件」反查，查不到回落宿主 `AssetPathToSrc` | §4.5 |
| 个人状态（上次文件 / 页选择 / 收起） | `UserSettings/PromptUGUI/Preview.asset`，不进 git | §4.12 |

## 4. 语义细节

### 4.1 菜单与会话

- 菜单 `Tools/PromptUGUI/UI Preview _F8`。F8 是默认键，Shortcut Manager 里可改。
- 三种状态：
  - **编辑态**：未配置（`UIPreviewSettings.instance.configured == false`）→ 首次对话框（§4.2）；配置了 → `Launch()`。
    `EditorUtility.scriptCompilationFailed` 为真时提示「先修编译错误」并返回——`EnterPlaymode()` 遇编译错误不会触发
    `ExitingEditMode`，否则挂起的 Pending 标记会让**下一次**普通 Play 误进预览场景。
  - **预览 Play 中**（`IsActive`）：切换面板收起 / 展开（与面板内 F1 同义）。
  - **普通 Play 中**：菜单置灰。
- 会话状态放 `SessionState`（跨进 Play 的域重载存活，和 `DevPlayFromCurrentScene` 同一手法）：
  `PromptUGUI.Preview.Pending`（bool）/ `PromptUGUI.Preview.Active`（bool）/ `PromptUGUI.Preview.PrevStartScene`（GUID，"" = 原本为空）。
- **接管 `playModeStartScene` 的时机是 `ExitingEditMode`，不是 F8 那一刻。** 宿主可能也在 `ExitingEditMode` 里写它
  （ssw 的 `DevPlayFromCurrentScene` 把它钉在 Login）。多播委托按订阅顺序执行：本工具的 `playModeStateChanged` 钩子在
  **F8 那一刻**才挂（宿主的在 `[InitializeOnLoad]` 时早已挂好），所以本工具排在后面、最后写的赢。F8 之外的时间不挂
  这个钩子——不在预览时对宿主零影响。
- `EnteredEditMode`：还原 `playModeStartScene`（按 `PrevStartScene` GUID；"" → null），清三个 SessionState 键，摘钩子。
  Play 没起来（用户在 ExitingEditMode 后取消、或别的工具 `isPlaying = false`）也走这一条。
- `[InitializeOnLoad]` 静态构造（每次域重载都跑）：`Active` 为真且 `Application.isPlaying` → 挂 `sceneLoaded` +
  `EnteredPlayMode` 注入钩子（§4.3）；`Pending` / `Active` 为真却 `!isPlayingOrWillChangePlaymode` → 视为上次会话残留，
  清掉并还原 `playModeStartScene`。关掉 Domain Reload 的工程没有这一步，F8 时挂的钩子本身就活到 `EnteredEditMode`。

### 4.2 预览场景

首次对话框（`DisplayDialogComplex`，英文文案）：**Built-in empty scene** / **Pick a .unity…** / Cancel。

- **内置**：包内 `Editor/Preview/UIPreview.unity`——一台相机（SolidColor 深灰、Overlay 画布不需要它，`canvas="camera"` 的
  Screen 与 glass 需要）。不含 EventSystem、不含灯光。放在 `Editor/` 下：不进任何 build，`playModeStartScene` 不要求
  进 Build Settings，包从 git URL 安装（`Library/PackageCache` 只读）时 Play 态加载只读场景也没问题（§7 验证项）。
- **用户的 `.unity`**：`OpenFilePanel` 限定在工程内，存工程相对路径。文件不存在 / 不是 SceneAsset → F8 时报错并把
  用户带到 Project Settings 页。用户场景里可以有任何东西（ssw 那颗行星背景 + 灯 + 自己的 runner），**工具不往里写**。
- 两种模式在注入时都做一件事：场景里没有 `EventSystem` 就建一个（`#if ENABLE_INPUT_SYSTEM` → `InputSystemUIInputModule`，
  否则 `StandaloneInputModule`——和 `UI.Navigation.Enable` 同一判断；Editor asmdef 加 `Unity.InputSystem` 引用）。
- 只有**内置**模式补一个 `UI.CanvasConfigurator ??= (canvas, _) => canvas.worldCamera = Camera.main`（宿主没设才补）
  ——那台相机是工具自己的。用户场景模式不碰它。

### 4.3 覆盖层注入与生命周期

- 注入点：`SceneManager.sceneLoaded`（`[InitializeOnLoad]` 在域重载里挂上，赶得上首场景）与
  `PlayModeStateChange.EnteredPlayMode` 二选一先到者，幂等（`FindAnyObjectByType<UIPreviewOverlay>()` 已有则跳过）。
- `new GameObject("PromptUGUI UI Preview")` + `UIPreviewOverlay`（**`PromptUGUI.Editor` asmdef 里的 MonoBehaviour**：
  Editor 程序集的组件在编辑器 Play 里 `AddComponent` 合法，只是不能序列化进资产——本来也不进）+ `DontDestroyOnLoad`
  （宿主 boot 若切场景也活着）。Player 侧连类型都不存在，不需要 `PromptUGUIPreviewBuildStripper` 那种剥离。
- 钩子安装是**懒的、每次加载前都检查**（§4.4）：`UI.SourceResolver` 不是工具的 → 记下当前值为宿主的、换成工具的；
  `UI.HotReload.AssetPathToSrc` 同理。原因：样例那种在 runner `Start()` 里 `UseResourcesResolver` 的项目，runner 的 Start
  可能排在覆盖层之后，会把工具在 Start 里装的 resolver 覆盖掉（memory 第 9 条就是这个坑）。
- `OnDestroy`（退出 Play / 用户手删）：把 `SourceResolver` / `AssetPathToSrc` /（内置模式下的）`CanvasConfigurator`
  还给宿主。关掉 Domain Reload 的工程这一步是必须的（`[OnExitingPlayMode] UnloadAll` 刻意保留 resolver）。

### 4.4 文件索引与 src 解析

- 工程根 = `Path.GetDirectoryName(Application.dataPath)`——编辑器工具，**不再有**「换工程」「`-project` 参数」「拖文件到 exe」。
- 扫描根：`UIPreviewSettings.scanRoots`，默认 `["Assets"]`；`Directory.EnumerateFiles(root, "*.ui.xml", 递归)` +
  显式 `EndsWith(".ui.xml")` 复核（Win32 通配会命中 8.3 短名）。每个文件记 `Abs` / `Rel`（工程相对，`/` 分隔）/
  `HasScreen`（文本里有 `<Screen` 且后跟空白、`>` 或 `/`；读不出来按「能预览」放行，让用户点开看到真错）。
  没 `<Screen>` 的纯模板 / 主题文件不进列表，但留在索引里给后缀匹配用。
- 工具的 `SourceResolver`（磁盘优先，顺序固定）：
  1. 绝对路径且存在 → 读盘；
  2. 工程相对路径（`Assets/...`、`Packages/...`）存在 → 读盘；
  3. 索引里唯一后缀匹配：`"/" + Rel` 以 `"/" + src` 结尾；**`X.ui` 视同 `X.ui.xml`**（Resources 式 src 带 `.ui` 不带 `.xml`，
     宿主的 `UseResourcesResolver` 就是这个形态）——命中多个宁可不选（回落宿主），免得静默选错；
  4. `Assets/` 开头却没找到 → **抛** `IOException`（不能回落到宿主：用户会以为看的是磁盘最新版）；
  5. 其余（包内置模态 `PromptUGUI/Modals/MessageBox.ui`、宿主 build 内资源）→ 宿主 resolver；宿主为空则抛。
- 每次成功读盘都记进 **served 表**：`绝对路径 → 第一次用它取到这个文件的 src`（§4.5 用）。
- 入口文件的 src = 工程相对路径（`Assets/…/X.ui.xml`），与今天一致；ssw 的 Addressables 地址正好也是这个形态。

### 4.5 热重载映射

- 工具的 `HotReload.AssetPathToSrc = assetPath => served[abs(assetPath)] ?? host?.Invoke(assetPath)`。
- 为什么工具的要排前面：宿主的映射把资产路径反查成**宿主的** src（Addressables 地址、Resources 相对名），而入口是工具按
  工程相对路径注册进 DepGraph 的——地址与路径不同的项目，宿主映射查出来的 src 不在 DepGraph 里，热重载静默失效
  （ssw 因为地址 = 路径才恰好没事）。served 表记的就是 DepGraph 里那个字符串。
- 被 import 的短地址文件（`UI/Templates/Foo.ui.xml`）改了：served 表命中（工具当初按后缀把它读盘了）→ 返回那个短
  地址 → `ScreensDependingOn` 命中 → `ReloadAsync`。宿主没设 `AssetPathToSrc` 的项目（自定义 resolver）预览照样能热重载。
- v1 限制：同一文件被两种 src 引用（`Assets/…/Foo.ui.xml` 与 `UI/Templates/Foo.ui.xml`）只重载先记下的那种；
  `NotifyAssetChanged` 一次只接一个 src。写进文档，不做。

### 4.6 commons 重装

`UnloadAll` 连 commons 池一起清；工具不必重装——`LoadDocumentAsync` 会先 `await UI.EnsureCommonLibrariesAsync()`，按
`PromptUGUISettings.commonLibraries` 的声明装回（commons-settings spec §4.2–4.3）。取源走的是工具当时挂的磁盘优先 resolver，
而 `UnloadAll` 已清掉 `DocumentCache`，所以 commons 也是磁盘最新版（今天靠 `MatchIndex` 达到的效果，保持）。没有快照、没有
兜底表、没有竞态：宿主 boot 完没完成都无所谓，装什么由 settings 说了算。

### 4.7 加载流程（一次点击 / 一次 `Load()`）

```
busy = true; error = null
EnsureHooks()                                  // §4.3：resolver / AssetPathToSrc 懒装
pages = []; pagesScreen = null                  // 旧界面马上销毁，页条别再画它
UI.UnloadAll()                                  // 宿主打开的界面也没了——预览场景本来就不该跑业务
loadedFile = loadedScreen = null; screens = []
names = await UI.LoadDocumentAsync(rel)         // rel = 工程相对路径；内部先 EnsureCommonLibrariesAsync（§4.6）
if names.Count == 0: throw "no <Screen> in file (a template-only file can't be previewed on its own)"
screens = names; loadedFile = rel
OpenScreen(remembered screen for this file ?? names[0])   // UI.Open + 收集 Pages + 选回记住的页
catch e: Debug.LogException(e); error = e.GetBaseException().Message   // 刻意不弹 MessageBox：会被面板挡住、await 会锁死列表
finally busy = false
```

- 同一行再点一次 = 重新加载（改完 xml 没被 Unity 导入时的手动路径）。
- 多 Screen 文件：页脚一排按钮切 Screen（`UI.Close(old)` → `UI.Open(new)`），选择按文件记住（§4.12）。

### 4.8 Pages / Screen 记忆与热重载后的重新收集

- 与今天逐字相同：`CollectPages(screen)` 用 `screen.FindAll<Pages>()`；键 = `rel|pagesId#index`（同 id 的模板实例按
  FindAll 顺序编号）；`Update()` 里 `UI.Get(loadedScreen)` 不是 `pagesScreen` 那个实例 → 重新收集 + 选回记住的页
  （`CloseImmediate` 与 `Open` 之间那一帧是 null，等下一帧）。页条只画 `GameObject != null` 的 Pages，全死就整条不画。
- 记忆写 `UIPreviewUserState`（§4.12），不再写 PlayerPrefs（那是宿主 Player 的注册表键，与工程无关）。

### 4.9 诊断行（面板顶部，每次重绘算一遍，全是读静态属性）

| 条件 | 显示 | 颜色 |
|---|---|---|
| `UI.SpriteResolver == null` | `Sprite resolver not set — <Icon> renders as white boxes. Call SpriteResolverHelpers.UseSpriteSetResolver(...) in a [RuntimeInitializeOnLoadMethod].` | 红 |
| `UI.Theme.Current == null` 且 `Available.Count > 0` | `No theme selected — color tokens resolve to white. Pick one:` + 主题按钮（§4.10） | 黄 |
| `UI.Theme.Current == null` 且 `Available.Count == 0` | `No theme registered — load a common library that declares <Theme>.` | 黄 |
| `PromptUGUISettings.Instance?.commonLibraries` 为空且 `UI.Theme.Available.Count == 0` | `No common library declared — list your shared Template / Style / Theme files under PromptUGUI Settings → Common Libraries.` | 黄 |
| 宿主 resolver 为空 | `Host SourceResolver not set — only files under the scan roots resolve.`（信息，不算错） | 灰 |
| 一切正常 | `commons: N · theme: dark · sprites: ok · locale: zh-Hans` 一行 | 灰 |

文案英文（库的编辑器 UI 语言）。诊断只**说**，不**装**——装配归宿主（§2-B）；唯一例外是内置场景的 `CanvasConfigurator`（§4.2）。

### 4.10 横竖屏与主题

- 两个按钮 **Landscape / Portrait** → `UnityEditor.PlayModeWindow.SetCustomRenderingResolution(w, h, "PromptUGUI Preview")`，
  尺寸来自 `UIPreviewSettings`（默认 1920×1080 / 1080×1920）。库的 `portrait` / `landscape` variant 由 `OrientationTracker`
  自动跟随，ReSolve 照常。退出 Play **不还原** Game view 尺寸——和用户手动改分辨率一样是编辑器状态；写进文档。
- 主题：一排按钮列 `UI.Theme.Available`，当前高亮，点 → `UI.Theme.Set`。`ThemeStore` 是进程级的，`UnloadAll` 不清，
  `Theme.Current` 跨加载保持；重装 commons 会按 (name, src) 替换注册，Available 不变。
- locale 不做：`PlayModeLocaleMenu` 已经在 Game view 右上角提供（配置了 locale 的工程）。

### 4.11 自动加载上次文件

`UIPreviewUserState.autoLoadLast`（默认 true）：注入后直接加载上次文件（commons 由 `LoadDocumentAsync` 自动装载，不用等宿主 boot）。
用户先点了别的文件就取消。这是「F8 → 看到上次那个界面」
的关键一步，今天要多点一下。

### 4.12 存储

| 文件 | 类型 | 内容 | git |
|---|---|---|---|
| `ProjectSettings/PromptUGUIPreview.asset` | `ScriptableSingleton<UIPreviewSettings>` + `[FilePath(…, ProjectFolder)]`，文本序列化 | `configured`、`sceneMode`（BuiltIn / Custom）、`scenePath`、`landscape` / `portrait` 尺寸、`scanRoots` | **进** |
| `UserSettings/PromptUGUI/Preview.asset` | `ScriptableSingleton<UIPreviewUserState>` | `lastFile`、`screenByFile`、`pageByKey`、`collapsed`、`filter`、`autoLoadLast` | 不进（Unity 模板 `.gitignore` 已含 `/[Uu]ser[Ss]ettings/`；同目录已有 `Auth.asset` 先例） |

- 不动 `Assets/Settings/PromptUGUI_Settings.asset`（字体 / locale 配置，运行时要读；预览配置是纯编辑器的）。
- 不与 `ProjectSettings/PromptUGUI.asset` 合文件：那个文件用 `LoadSerializedFileAndForget` 存单个 `TranslationProvider`。
- Project Settings 页 `Project/PromptUGUI/UI Preview`（与 `Translation` 并列）：场景模式 + 场景选择器、
  两组尺寸、扫描根，以及一个 **Reset first-run** 按钮。改场景 / 地址不用删文件重问。
- 覆盖层在 Play 里写 `UserSettings`：`ScriptableSingleton.Save(true)` 在编辑器 Play 态可用（Editor asmdef）。

### 4.13 自动化入口（公开静态，`PromptUGUI.Editor.Preview.UIPreview`）

```csharp
public static bool   IsActive       { get; }   // 预览 Play 中且覆盖层活着
public static bool   Launch();                 // 编辑态：按配置进 Play。未配置 → false + LogWarning，**不弹对话框**（MCP 里模态框会卡死一切）
public static void   Load(string projectPath); // "Assets/…/X.ui.xml"；fire-and-forget，轮询 IsBusy / LastError
public static bool   IsBusy         { get; }
public static string LastError      { get; }   // null = 上次加载成功
public static string LoadedFile     { get; }   // 工程相对路径
public static string LoadedScreen   { get; }
public static IReadOnlyList<string> Screens { get; }
public static void   OpenScreen(string name);
public static bool   Select(string pagesId, string pageId);   // 找不到 → false
public static void   SetOrientation(bool portrait);
public static bool   PanelCollapsed { get; set; }
```

菜单项 = `Launch()` 之前多一道首次对话框；其余全部经这组静态。memory 里那套「反射 `_projectRoot` / `RefreshFiles` /
`ResolveSrc` / `LoadFileAsync`」退休。截图仍由调用方 `ScreenCapture.CaptureScreenshot`（必须独立一次 execute_code）。

## 5. 新公开 API（Runtime，`UI`）

**无。** 原本这里的 `CommonLibraryRef` / `UI.LoadedCommonLibraries` / `CommonsSources` 有序表 / reload 保 `as=` 全部由
`2026-09-18-commons-settings-design.md` 承担（`PromptUGUISettings.commonLibraries` + `UI.EnsureCommonLibrariesAsync()`，
`DepGraph.CommonsSources` 记 src → as）。本文对 Runtime **不加任何东西**。

## 6. 实现地图

| 文件 | 内容 |
|---|---|
| `Editor/Preview/UIPreview.cs` | 菜单项、`[InitializeOnLoad]` 会话状态机（§4.1）、注入（§4.3）、§4.13 静态入口（转发到覆盖层） |
| `Editor/Preview/UIPreviewOverlay.cs` | IMGUI 覆盖层：面板 / 页条 / 诊断行 / 横竖屏 / 主题；加载流程；Update 重新收集 |
| `Editor/Preview/UIPreviewIndex.cs` | 纯逻辑：扫描、`HasScreen`、后缀匹配（含 `.ui` ≙ `.ui.xml`）、served 表、`AssetPathToSrc` 组合——**不碰 Unity 对象**，可测 |
| `Editor/Preview/UIPreviewSettings.cs` / `UIPreviewUserState.cs` | 两个 `ScriptableSingleton` |
| `Editor/Preview/UIPreviewSettingsProvider.cs` | Project Settings 页 |
| `Editor/Preview/UIPreview.unity` | 内置场景（相机一台） |
| `Editor/PromptUGUI.Editor.asmdef` | 加 `Unity.InputSystem`、`PromptUGUI.Compat.UniTask`、`UniTask`（Awaitable 在 2022.3 上的 polyfill，与 Runtime 一致） |

## 7. 测试（Red first）

EditMode：
1–4. （作废：commons 的装载 / reload 保 `as` 由 commons-settings spec §7-1~15 覆盖，本文不再有 Runtime 测试。）

EditorOnly（`PromptUGUI.Tests.EditorOnly`，纯逻辑 `UIPreviewIndex`，用临时目录夹具）：
5. 扫描只收 `.ui.xml`，`HasScreen` 对 `<Screen>` / `<Screen ` / `<Screen/>` 为真、对 `<ScreenFoo>` 为假、读不出来为真。
6. 解析顺序：绝对路径 > 工程相对 > 唯一后缀（`Foo.ui` 与 `Foo.ui.xml` 都命中 `…/Foo.ui.xml`）> `Assets/` 找不到抛 > 宿主。
7. 后缀命中两个 → 回落宿主（不选）。
8. served 表 + `AssetPathToSrc`：工具解析过的路径返回当初的 src（短地址优先于宿主映射）；没解析过的走宿主；宿主为空返回 null。
9. `PagesKey` 组合与同 id 编号。
10. 会话规则（纯函数 `UIPreviewSession.Decide(settings, isPlaying, compilationFailed, isActive)` → Launch / ToggleCollapse / Refuse(reason)）。

手工验证（ssw_re_client，Unity MCP）：
11. F8 → 进内置场景 → 覆盖层在 → 点文件 → 截图横 / 竖 → Stop → `playModeStartScene` 还原为 Login（`DevPlayFromCurrentScene` 开着）。
12. 改成用户场景（ssw 的 `UIPreview.unity` 摘掉组件后）→ 行星背景在、覆盖层在、场景文件 `git status` 无改动。
13. 保存一个被预览文件的 import 依赖（短地址）→ 热重载 → 页条选回上次的页。
14. `UIPreview.Launch()` / `Load()` / `Select()` 从 execute_code 直接调通，不再反射。
15. 关掉 Domain Reload（Enter Play Mode Options）跑一遍 11——钩子路径不同。
16. 包以 git URL 安装的工程（`PromptUGUIDev` 或临时工程）：内置场景在只读 PackageCache 里也能作 `playModeStartScene`。

## 8. 文档更新（同一 PR，英文）

- `README.md`（英 / 中）「Usage」加一节 **UI Preview**：F8、首次选场景、boot 契约一句话（"your `[RuntimeInitializeOnLoadMethod]`
  runs in the preview scene too — resolvers, sprite sets, theme and locale come from it"）、与 DocumentHost 的分工。
- `.claude/skills/scripting-promptugui-csharp/SKILL.md`：「Tooling」段（现在讲
  `FindAll<Pages>` 那里）加 UI Preview 的自动化入口表（§4.13）——给驱动 Unity 的助手用。
- `CLAUDE.md`（本仓库）项目布局表：`Editor/Preview/` 一行。
- 宿主侧（PR 合并后另做）：memory `unity-uipreview-scene-via-mcp` 重写为公开入口版；ssw `CLAUDE.md` 的「编辑器里的开发回路」
  改指 `Tools/PromptUGUI/UI Preview`。

## 9. 宿主迁移（ssw_re_client，另案）

1. 删 `Assets/Tools/UI Preview/UIPreview.cs`、`Editor/UIPreviewMenu.cs`（及 meta）；`UIPreview.unity` 里删掉挂 `UIPreview` 的
   GameObject，其余（行星、灯、相机、EventSystem）留着当用户场景。
2. Project Settings → PromptUGUI → UI Preview：Custom scene = `Assets/Tools/UI Preview/UIPreview.unity`。commons 不用配：
   `PromptUGUI_Settings.asset → Common Libraries` 里已经列了 `UI/Templates/DefaultTheme.ui.xml`（commons-settings 迁移）。
3. 验 §7-11 ~ 14。`DevPlayFromCurrentScene` 不改——§4.1 的订阅顺序保证预览赢；若实测输了，才在它里面按
   `SessionState.GetBool("PromptUGUI.Preview.Pending")` 让路（一行）。

## 10. 非目标

- 独立 exe 预览（拖 `.ui.xml` 到 exe、`-project` 参数）：库版是编辑器工具。ssw 若还要 exe 模式得自留一份——§11 问作者。
- 模态框（MessageBox 等）的一键预览按钮：它们要走真 `Bind`；磁盘 resolver 让 memory 第 5 条的 MCP 步骤照样成立（短地址按后缀读盘），先不做按钮。
- 面板里的 locale 切换（`PlayModeLocaleMenu` 已有）、截图按钮、Game view 尺寸的退出还原。
- 替换或删除 `PromptUGUIDocumentHost`。
- 库自己装配 resolver / SpriteSet / 主题（§2-B）。

## 11. 已定的决策 / 待作者确认（推荐第一列）

| # | 项 | 推荐 | 备选 |
|---|---|---|---|
| 1 | 覆盖层所在程序集 | `PromptUGUI.Editor`（Player 零痕迹） | Runtime + `#if UNITY_EDITOR`（同 DocumentHost，得配剥离器） |
| 2 | 内置场景 | 包内 `Editor/Preview/UIPreview.unity`，只有相机 | 不带场景、F8 时 `NewScene` 顶掉当前场景（§2-E 的问题） |
| 3 | 首次对话框问什么 | 只问场景（commons 由 settings 声明，工具不问） | — |
| 4 | 默认快捷键 | F8（Shortcut Manager 可改） | 不给默认键 |
| 5 | 自动加载上次文件 | 开（注入后直接加载） | 关，保持今天「进去再点」 |
| 6 | ssw 的 exe 预览模式 | 放弃（删本地工具） | 保留本地 `UIPreview.cs` 只为 exe |
| 7 | 个人状态存放 | `UserSettings/PromptUGUI/Preview.asset` | `EditorPrefs`（跨工程串键） |

## 12. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| M1 | 设置单例 + Project Settings 页 + 首次对话框 + 会话状态机 + 内置场景 + 注入一个空覆盖层 | §7-11 的 F8 → Play → 覆盖层在 → Stop → 还原 |
| M2 | 覆盖层搬家：索引 / resolver / 加载 / 多 Screen / 页条 / 个人状态 / §4.13 静态入口（§7-5~10 Red → Green） | §7-13、14 |
| M3 | 诊断行、横竖屏、主题按钮、自动加载 | 内置场景下三条诊断各触发一次 |
| M4 | 文档（§8）；宿主迁移（§9）另案 | lint 过、PR |
