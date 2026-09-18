# UI Preview —— 库内置的 Play 态 `.ui.xml` 预览工具（UIP）

> 状态：**已对齐**（2026-09-18 两轮：第一轮作者把 commons 从本文拆去 `commons-settings` spec；第二轮取消首次对话框、
> 复用 lint 菜单的定位器、场景存 GUID，§11 推荐列全部采纳。下一步：plan → 分支 `feat/ui-preview-tool` → PR）。
> 相关：`2026-09-17-pages-design.md` §9（`IScreen.FindAll<T>()`——页选择器靠它枚举 `<Pages>`）；
> `2026-09-17-common-attr-runtime-state-design.md`（XML 初态所见即所得，是这个工具有价值的前提）；
> `2026-09-14-document-load-parallel-prefetch-and-source-cache-design.md`（`DocumentCache` / `UnloadAll` 清缓存——工具每次
> 加载都从磁盘拿最新版靠它）；master spec M4.4（`ReloadCommonLibraryAsync` + `HotReload.NotifyAssetChanged`）；
> `2026-09-18-commons-settings-design.md`（commons 由 `PromptUGUISettings.commonLibraries` 声明、`LoadDocumentAsync` 自动
> `EnsureCommonLibrariesAsync`，reload 保 `as=`——本文原 §4.6 / §5 的「commons 快照」方案整个由它取代）；
> `docs~/superpowers/plans/2026-09-18-lint-menu.md` 与 `Editor/UIXmlLintMenu.cs`（Editor 态「src → 资产路径」的定位器已在那里落地，
> 本文抽出来共用，不再写第二套）；`Editor/Preview/PromptUGUIDocumentHost*`（Edit 态的静态预览，本文不替换它，但要在文档里分清两者）；
> `Editor/I18n/PlayModeLocaleMenu.cs`（Play 态右上角的 locale / theme 下拉，本文不重复做 locale）。
> 动机来源：宿主 ssw_re_client 的 `Assets/Tools/UI Preview/`（`UIPreview.cs` + `UIPreview.unity` + `Editor/UIPreviewMenu.cs`）
> ——用了一个月、很好用，但整套是宿主私有代码，别的项目用不上；MCP 驱动它还得反射私有方法
> （memory `unity-uipreview-scene-via-mcp`）。

## 1. 问题

宿主写了一个 Play 态预览工具：扫出工程里全部 `.ui.xml` 列成表，点一行就 `UnloadAll` → 重装 commons → 从**磁盘**读
最新版 `LoadDocumentAsync` → `Open`；改完 xml 保存，库的热重载把界面重开，工具按帧发现 Screen 换了实例就重新收集
`<Pages>` 并把上次选的页选回来。它证明了三件事：

1. **Play 态预览是刚需，Edit 态的 `PromptUGUIDocumentHost` 顶不上。** DocumentHost 走同步 `UI.LoadDocument(label, xml)`
   ——没有 `<Import>`、没有 commons 池、没有主题注册；glass / 动画 / 模态 / 输入 / 热重载事件链也只在 Play 里有。
2. **工具本体 90% 是通用的**：文件索引、磁盘优先的 src 解析、加载流程、多 Screen 切换、Pages 页条、热重载后的重新收集。
3. **只有四个点是宿主私有的**，而它们把整个工具钉死在了那一个工程里：

| 耦合点 | 今天的形态 | 后果 |
|---|---|---|
| 预览场景 | `UIPreview.unity` 里**挂着** `UIPreview` 组件 | 工具组件写进场景资产；换项目要重建场景 |
| commons 地址 | `LoadCommonLibraryAsync("UI/Templates/DefaultTheme.ui.xml")` 写死 | 每个项目一份改过的 C#——**已由 commons-settings 解决**：settings 声明，工具不用知道 |
| boot 契约 | 预览场景不跑 `GameBootstrap` → 主题 `(none)`、图标白方块，靠 MCP 手工反射补 | 用户不知道缺了什么 |
| 入口 | `Tools/Dev/UI Preview _F8` + 写死的场景路径；`OpenScene(Single)` 踢掉正在编辑的场景 | 退出 Play 留在预览场景 |

还有一处是它自己的弱点：src → 磁盘文件靠「唯一后缀匹配」猜。ssw 的 Addressables 地址恰好等于资产路径才没事；地址另取名
（`ui-home`）的工程会静默回落到宿主 resolver——看的不是磁盘版，正是工具要避免的。

目标：把它做成 **PromptUGUI 自带的菜单功能**——装了包就有，`Tools/PromptUGUI/UI Preview`（F8），**零配置**能用、
到 Project Settings 选一次能用自己的场景，宿主一行工具代码都不用写；同时给 MCP / 脚本一组稳定的公开静态入口。

## 2. 否决的方案

**A. 宿主把库提供的组件挂进自己的预览场景（今天形态的库化）。否决。** 组件仍写进场景资产；「即开即用」不成立
（必须先建场景）；并且组件的序列化字段会随项目走，跟 ProjectSettings 重复。

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

**G. 首次对话框（问用内置场景还是自己的 `.unity`）。否决（第二轮）。** commons 不用问之后只剩场景一个问题，而内置场景是个
合理默认——F8 直接起，想换场景去 Project Settings。对话框带来的 `configured` 标记、`Launch()` 的「未配置」分支、MCP 下模态框
卡死的顾虑一起消失。

**H. 预览工具自己写一套 src → 磁盘文件的定位（后缀匹配）。否决（第二轮）。** `Editor/UIXmlLintMenu.cs` 已经有精确的一套：
Addressables 地址 / GUID → 资产路径（读 Addressables settings）、Resources 式短 key 从锚点目录上溯（`ImportClosure.ResolveInResources`）、
`Packages/…` 虚拟路径 ↔ 磁盘（`FileUtil.GetPhysicalPath`）。抽成共用的 `UiXmlLocator`，预览与 lint 一份实现；后缀匹配降为
自定义 resolver 工程的最后兜底。

## 3. 方案总览

```
F8 (Tools/PromptUGUI/UI Preview)
 │  编译错误 → 提示退出；已在预览 Play 中 → 切换面板收起；否则直接起（内置场景，或 Project Settings 里选的那个）
 ▼
SessionState.Pending = true;  EditorApplication.EnterPlaymode()
 │
 ├─ ExitingEditMode：记下原 playModeStartScene → 指向 预览场景（内置：包内 Editor/Preview/UIPreview.unity）
 │                    （本次 F8 才挂的钩子，排在宿主 InitializeOnLoad 时挂的钩子之后 → 最后写的赢）
 ├─ （域重载）宿主 RuntimeInitializeOnLoadMethod 照常跑：resolver / SpriteSet / 主题 / locale
 ├─ EnteredPlayMode / sceneLoaded：注入 GameObject「PromptUGUI UI Preview」+ 覆盖层组件（DontDestroyOnLoad，不进资产）
 │      注入序列：装钩子（工具 resolver / AssetPathToSrc）→ 预解析 settings 里的 commons（诊断行 + served 表）
 │                → await UI.EnsureCommonLibrariesAsync()（主题就位，诊断行才有意义）→ 自动加载上次文件
 │      覆盖层：文件列表 · 磁盘优先 resolver · 加载/切 Screen · Pages 页条 · 诊断行 · 横竖屏 · 主题 · Lint
 │      点文件：UnloadAll → LoadDocumentAsync(资产路径)（自动重装 settings 声明的 commons，经工具 resolver 读盘最新版）→ Open → 收集 Pages
 │      保存 xml：库热重载重开 Screen → 覆盖层按帧发现实例换了 → 重新收集 + 选回上次的页
 └─ EnteredEditMode：还原 playModeStartScene，清 SessionState；覆盖层 OnDestroy 已把 SourceResolver / AssetPathToSrc 还给宿主
```

谁管什么：

| 事 | 谁 | 依据 |
|---|---|---|
| resolver / SpriteSet / 主题 / locale 的装配 | **宿主**：任意场景都跑的 `RuntimeInitializeOnLoadMethod`，或预览场景里的 runner | §2-B |
| 缺了什么 | 库：面板诊断行说出来 | §4.9 |
| commons 地址 | 不用知道：`UnloadAll` 后 `LoadDocumentAsync` 自动 `EnsureCommonLibrariesAsync`，装的是 `PromptUGUISettings.commonLibraries` 声明的那些 | §4.6 |
| 哪些文件能预览 | lint 菜单的 `FindProjectUiXml()`：`Assets/` + embedded 包，排除其它包 | §4.4 |
| src → 磁盘文件 | 共用的 `UiXmlLocator`（Addressables 精确映射 / Resources 上溯 / `Packages/` 物理路径），后缀匹配只做兜底 | §4.4 |
| 预览场景 | 默认内置（包内一个只有相机的场景）；自己的 `.unity` 在 Project Settings 里选，存 GUID | §4.2 |
| 覆盖层 | 库在 Play 时注入，用户的场景资产永远干净 | §4.3 |
| 热重载映射 | 库按「工具亲手解析过的资产路径」反查，查不到回落宿主 `AssetPathToSrc` | §4.5 |
| 个人状态（上次文件 / 页选择 / 收起） | `UserSettings/PromptUGUI/Preview.asset`，不进 git | §4.12 |

## 4. 语义细节

### 4.1 菜单与会话

- 菜单 `Tools/PromptUGUI/UI Preview _F8`。F8 是默认键，Shortcut Manager 里可改。**没有首次对话框**（§2-G）。
- 三种状态：
  - **编辑态** → `Launch()`：场景 = `UIPreviewSettings.sceneGuid` 解析出的 SceneAsset，解析不到（空、或资产被删 / 移走）→ 内置场景
    （被删 / 移走时 `LogWarning` 一句，诊断行也提示，§4.9）；不阻断。
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

- **内置（默认）**：包内 `Editor/Preview/UIPreview.unity`——一台相机（SolidColor 深灰、Overlay 画布不需要它，`canvas="camera"` 的
  Screen 与 glass 需要）。不含 EventSystem、不含灯光。放在 `Editor/` 下：不进任何 build，`playModeStartScene` 不要求
  进 Build Settings，包从 git URL 安装（`Library/PackageCache` 只读）时 Play 态加载只读场景也没问题（§7 验证项）。
- **用户的 `.unity`**：Project Settings → PromptUGUI → UI Preview 页上一个 `SceneAsset` 对象字段，存的是 **GUID**
  （`UIPreviewSettings.sceneGuid`）——场景移动 / 改名不失效；空 = 内置。用户场景里可以有任何东西（ssw 那颗行星背景 +
  灯 + 自己的 runner），**工具不往里写**。
- 为什么不放进 `PromptUGUISettings.asset`：它在 `preloadedAssets` 里，一个 `SceneAsset` 引用会把预览场景（连同它引用的一切）
  拖进 Player 构建。预览配置是纯编辑器数据，住 `ProjectSettings/`（§4.12）。
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
- 注入序列（§4.11 展开）：装钩子 → 预解析 commons → `await UI.EnsureCommonLibrariesAsync()` → 自动加载。
- 钩子安装是**懒的、每次加载前都再检查一遍**（§4.4）：`UI.SourceResolver` 不是工具的 → 记下当前值为宿主的、换成工具的；
  `UI.HotReload.AssetPathToSrc` 同理。原因：样例那种在 runner `Start()` 里 `UseResourcesResolver` 的项目，runner 的 Start
  可能排在覆盖层之后，会把工具在注入时装的 resolver 覆盖掉（memory 第 9 条就是这个坑）。
- `OnDestroy`（退出 Play / 用户手删）：把 `SourceResolver` / `AssetPathToSrc` /（内置模式下的）`CanvasConfigurator`
  还给宿主。关掉 Domain Reload 的工程这一步是必须的（`[OnExitingPlayMode] UnloadAll` 刻意保留 resolver）。

### 4.4 文件列表、定位器与 src 解析

**共用定位器 `Editor/UiXmlLocator.cs`**——从 `UIXmlLintMenu.cs` 原样抽出（lint 菜单改调它，行为不变；`UIXmlLintMenuTests` 里 `Physical` / `ToAssetPath` / `IsProjectOwned` 的调用点改指 `UiXmlLocator`，断言一字不动）：

```csharp
internal static class UiXmlLocator
{
    static List<string> FindProjectUiXml();                 // 资产路径：Assets/ + embedded 包，排除其它包；排好序
    static bool IsProjectOwned(string assetPath);
    static string Physical(string assetPath);               // Packages/… 虚拟路径 → 磁盘（FileUtil.GetPhysicalPath）；Assets/… 拼工程根
    static string ToAssetPath(string physical);             // 反向；包内文件给 Packages/<name>/…
    static Dictionary<string, string> AddressableUiXml();   // 地址 → 资产路径 与 GUID → 资产路径（PROMPTUGUI_HAS_ADDRESSABLES 下）
    static string Locate(string src, string anchorAssetPath);   // = 今天 MakeResolver 的函数体：Addressables 精确命中 → Resources 从锚点目录上溯 → null
}
```

- **文件列表** = `FindProjectUiXml()`（AssetDatabase 视角）；每项记 `AssetPath` / `Physical` / `HasScreen`（读盘文本里有 `<Screen`
  且后跟空白、`>` 或 `/`；读不出来按「能预览」放行，让用户点开看到真错）。没 `<Screen>` 的纯模板 / 主题文件不进列表，但留在
  索引里给后缀兜底用。列表来自 AssetDatabase，新建的文件要等 Unity 导入（自动刷新）才出现，面板「Refresh」按钮跑一次
  `AssetDatabase.Refresh()` + 重扫；文本永远读盘（改了没导入也是最新）。
- **不再有**「换工程」「`-project` 参数」「拖文件到 exe」「`scanRoots`」。
- 工具的 `SourceResolver`（磁盘优先，顺序固定；`anchor` = 当前入口文件的资产路径，没有入口时 = 上次文件，都没有则跳过 Resources 上溯）：
  1. 绝对路径且存在 → 读盘；
  2. `Assets/…` / `Packages/…` 且 `Physical(src)` 存在 → 读盘；
  3. `Locate(src, anchor)` 命中 → 读盘（Addressables 地址 / GUID 精确；Resources 式 `X.ui` 从锚点目录上溯到 `Resources/`）；
  4. 索引里唯一后缀匹配：`"/" + AssetPath` 以 `"/" + src` 结尾，**`X.ui` 视同 `X.ui.xml`**——命中多个宁可不选；这是给自定义
     resolver 工程的兜底；
  5. `Assets/` / `Packages/` 开头却没找到 → **抛** `IOException`（不能回落到宿主：用户会以为看的是磁盘最新版）；
  6. 其余（包内置模态 `PromptUGUI/Modals/MessageBox.ui`、宿主 build 内资源）→ 宿主 resolver；宿主为空则抛。
- 每次成功读盘都记进 **served 表**：`资产路径 → 第一次用它取到这个文件的 src`（§4.5 用）。
- 入口文件的 src = 资产路径（`Assets/…/X.ui.xml`），与今天一致；ssw 的 Addressables 地址正好也是这个形态。

### 4.5 热重载映射

- 工具的 `HotReload.AssetPathToSrc = assetPath => served[assetPath] ?? host?.Invoke(assetPath)`。AssetPostprocessor 给的就是资产路径，
  served 表按它记，不用来回换算。
- 为什么工具的要排前面：宿主的映射把资产路径反查成**宿主的** src（Addressables 地址、Resources 相对名），而入口是工具按
  资产路径注册进 DepGraph 的——地址与路径不同的项目，宿主映射查出来的 src 不在 DepGraph 里，热重载静默失效
  （ssw 因为地址 = 路径才恰好没事）。served 表记的就是 DepGraph 里那个字符串。
- 被 import 的短地址文件（`UI/Templates/Foo.ui.xml`）改了：served 表命中（工具当初按定位器把它读盘了）→ 返回那个短
  地址 → `ScreensDependingOn` 命中 → `ReloadAsync`；commons 文件改了 → `IsCommons` 命中 → `ReloadCommonLibraryAsync` → 全部 Screen 重开。
  宿主没设 `AssetPathToSrc` 的项目（自定义 resolver）预览照样能热重载。注入时的预解析（§4.9）把 settings 里的 commons 先记进
  served 表——否则首轮 commons 若是宿主在飞的 Ensure 装的（§4.11），这类项目改 commons 文件不会触发重载。
- v1 限制：同一文件被两种 src 引用（`Assets/…/Foo.ui.xml` 与 `UI/Templates/Foo.ui.xml`）只重载先记下的那种；
  `NotifyAssetChanged` 一次只接一个 src。写进文档，不做。

### 4.6 commons 重装

`UnloadAll` 连 commons 池一起清；工具不必重装——`LoadDocumentAsync` 会先 `await UI.EnsureCommonLibrariesAsync()`，按
`PromptUGUISettings.commonLibraries` 的声明装回（commons-settings spec §4.2–4.3）。取源走的是工具当时挂的磁盘优先 resolver，
而 `UnloadAll` 已清掉 `DocumentCache`，所以 commons 也是磁盘最新版。没有快照、没有兜底表；装什么由 settings 说了算。
宿主 boot 那次 fire-and-forget 的 Ensure 若还在飞，工具注入时自己的 `EnsureCommonLibrariesAsync()` 会作为 waiter 等它完成
（§4.11），之后每一次加载都是工具 resolver 读盘。

### 4.7 加载流程（一次点击 / 一次 `Load()`）

```
busy = true; error = null
EnsureHooks()                                  // §4.3：resolver / AssetPathToSrc 懒装
pages = []; pagesScreen = null                  // 旧界面马上销毁，页条别再画它
UI.UnloadAll()                                  // 宿主打开的界面也没了——预览场景本来就不该跑业务
loadedFile = loadedScreen = null; screens = []
names = await UI.LoadDocumentAsync(assetPath)   // 内部先 EnsureCommonLibrariesAsync（§4.6）；入口经工具 resolver 第 2 步读盘
if names.Count == 0: throw "no <Screen> in file (a template-only file can't be previewed on its own)"
screens = names; loadedFile = assetPath
OpenScreen(remembered screen for this file ?? names[0])   // UI.Open + 收集 Pages + 选回记住的页
catch e: Debug.LogException(e); error = e.GetBaseException().Message   // 刻意不弹 MessageBox：会被面板挡住、await 会锁死列表
finally busy = false
```

- 同一行再点一次 = 重新加载（改完 xml 没被 Unity 导入时的手动路径）。
- 多 Screen 文件：页脚一排按钮切 Screen（`UI.Close(old)` → `UI.Open(new)`），选择按文件记住（§4.12）。

### 4.8 Pages / Screen 记忆与热重载后的重新收集

- 与今天逐字相同：`CollectPages(screen)` 用 `screen.FindAll<Pages>()`；键 = `assetPath|pagesId#index`（同 id 的模板实例按
  FindAll 顺序编号）；`Update()` 里 `UI.Get(loadedScreen)` 不是 `pagesScreen` 那个实例 → 重新收集 + 选回记住的页
  （`CloseImmediate` 与 `Open` 之间那一帧是 null，等下一帧）。页条只画 `GameObject != null` 的 Pages，全死就整条不画。
- 记忆写 `UIPreviewUserState`（§4.12），不再写 PlayerPrefs（那是宿主 Player 的注册表键，与工程无关）。

### 4.9 诊断行与 Lint

面板顶部。多数行每次重绘读静态属性；commons 行在注入与「Refresh」时算一次（`Locate` 每条 settings 里的 src）。

| 条件 | 显示 | 颜色 |
|---|---|---|
| 用的是内置场景 | `Built-in preview scene — pick your own under Project Settings › PromptUGUI › UI Preview.` | 灰 |
| `sceneGuid` 非空却解析不到 | `Preview scene not found (moved or deleted?) — using the built-in scene. Fix it under Project Settings › PromptUGUI › UI Preview.` | 黄 |
| `UI.SpriteResolver == null` | `Sprite resolver not set — <Icon> renders as white boxes. Call SpriteResolverHelpers.UseSpriteSetResolver(...) in a [RuntimeInitializeOnLoadMethod].` | 红 |
| `UI.Theme.Current == null` 且 `Available.Count > 0` | `No theme selected — color tokens resolve to white. Pick one:` + 主题按钮（§4.10） | 黄 |
| `UI.Theme.Current == null` 且 `Available.Count == 0` | `No theme registered — no loaded library declares a <Theme>.` | 黄 |
| settings 里每条 commons，预解析命中 | `✓ UI/Templates/DefaultTheme.ui.xml → Assets/_Project/Common/UI/Templates/DefaultTheme.ui.xml` | 灰 |
| settings 里某条 commons，预解析未命中 | `⚠ 'x' not found in the project — the host resolver will be asked (not the file on disk).` | 黄 |
| settings 没列 commons 且 `UI.Theme.Available.Count == 0` | `No common library declared — list your shared Template / Style / Theme files under PromptUGUI Settings → Common Libraries.` | 黄 |
| 注入时的 `EnsureCommonLibrariesAsync()` 抛了 | 异常的 `GetBaseException().Message` | 红 |
| 宿主 resolver 为空 | `Host SourceResolver not set — only files the locator can find on disk resolve.`（信息，不算错） | 灰 |
| 一切正常 | `theme: dark · sprites: ok · locale: zh-Hans · commons: 1` 一行 | 灰 |

- 注入时先 `await UI.EnsureCommonLibrariesAsync()`（§4.11）再画诊断——否则内置场景里点之前 `Theme.Available` 必为空，「No theme registered」是误报。
- **Lint 按钮**（已加载文件时可用）：`UIXmlLintMenu.Lint(new[] { loadedFile }, Report, ConfiguredCommonLibraries())`——同一 asmdef、同一份规则、
  同样打到 Console（每行 ping 资产）；状态行写 `N issue(s)`。改完 xml 看效果之前顺手过一遍 lint，不用离开 Play。
- 文案英文（库的编辑器 UI 语言）。诊断只**说**，不**装**——装配归宿主（§2-B）；唯一例外是内置场景的 `CanvasConfigurator`（§4.2）。

### 4.10 横竖屏与主题

- 两个按钮 **Landscape / Portrait** → `UnityEditor.PlayModeWindow.SetCustomRenderingResolution(w, h, "PromptUGUI Preview")`，
  尺寸来自 `UIPreviewSettings`（默认 1920×1080 / 1080×1920）。库的 `portrait` / `landscape` variant 由 `OrientationTracker`
  自动跟随，ReSolve 照常。退出 Play **不还原** Game view 尺寸——和用户手动改分辨率一样是编辑器状态；写进文档。
- 主题：一排按钮列 `UI.Theme.Available`，当前高亮，点 → `UI.Theme.Set`。`ThemeStore` 是进程级的，`UnloadAll` 不清，
  `Theme.Current` 跨加载保持；重装 commons 会按 (name, src) 替换注册，Available 不变。
- locale 不做：`PlayModeLocaleMenu` 已经在 Game view 右上角提供（配置了 locale 的工程）。

### 4.11 注入序列与自动加载

```
EnsureHooks()                                          // 工具 resolver / AssetPathToSrc（§4.3）
PreResolveCommons()                                    // §4.9 的 commons 行；命中的写进 served 表（§4.5）
try   { await UI.EnsureCommonLibrariesAsync(); }       // 公开 API；宿主 boot 那次还在飞就等它，主题在此就位
catch { 诊断行红字（不阻断） }
if (userState.autoLoadLast && lastFile 仍在列表里) Load(lastFile)   // §4.7；用户先点了别的文件就不做
```

- 为什么先 Ensure 再自动加载：宿主 boot 的 Ensure 是 fire-and-forget（ssw `_ = UI.EnsureCommonLibrariesAsync()`），直接
  `UnloadAll` → `LoadDocumentAsync` 会成为它的 waiter，首轮 commons 是宿主 resolver 取的版本而不是磁盘。等它完成后再
  `UnloadAll`，之后每一轮都是工具 resolver 读盘；诊断行也不再误报。
- `autoLoadLast` 默认 true：这是「F8 → 看到上次那个界面」的关键一步，今天要多点一下。

### 4.12 存储

| 文件 | 类型 | 内容 | git |
|---|---|---|---|
| `ProjectSettings/PromptUGUIPreview.asset` | `ScriptableSingleton<UIPreviewSettings>` + `[FilePath(…, ProjectFolder)]`，文本序列化 | `sceneGuid`（空 = 内置）、`landscape` / `portrait` 尺寸 | **进** |
| `UserSettings/PromptUGUI/Preview.asset` | `ScriptableSingleton<UIPreviewUserState>` | `lastFile`（资产路径）、`screenByFile`、`pageByKey`、`collapsed`、`filter`、`autoLoadLast` | 不进（Unity 模板 `.gitignore` 已含 `/[Uu]ser[Ss]ettings/`；同目录已有 `Auth.asset` 先例） |

- 不动 `Assets/Settings/PromptUGUI_Settings.asset`（字体 / locale / commons，运行时要读；预览配置是纯编辑器的，且 `SceneAsset`
  引用进不得 `preloadedAssets`，§4.2）。
- 不与 `ProjectSettings/PromptUGUI.asset` 合文件：那个文件用 `LoadSerializedFileAndForget` 存单个 `TranslationProvider`。
- Project Settings 页 `Project/PromptUGUI/UI Preview`（与 `Translation` 并列）：`SceneAsset` 对象字段（None = 内置；读写 `sceneGuid`）、
  两组尺寸。没有别的按钮。
- 覆盖层在 Play 里写 `UserSettings`：`ScriptableSingleton.Save(true)` 在编辑器 Play 态可用（Editor asmdef）。

### 4.13 自动化入口（公开静态，`PromptUGUI.Editor.Preview.UIPreview`）

```csharp
public static bool   IsActive       { get; }   // 预览 Play 中且覆盖层活着
public static bool   Launch();                 // 编辑态：起预览 Play（内置或 settings 指定的场景）。Play 中 / 编译错误 → false + LogWarning。不弹任何对话框
public static void   Load(string assetPath);   // "Assets/…/X.ui.xml"；fire-and-forget，轮询 IsBusy / LastError
public static bool   IsBusy         { get; }
public static string LastError      { get; }   // null = 上次加载成功
public static string LoadedFile     { get; }   // 资产路径
public static string LoadedScreen   { get; }
public static IReadOnlyList<string> Screens { get; }
public static void   OpenScreen(string name);
public static bool   Select(string pagesId, string pageId);   // 找不到 → false
public static void   SetOrientation(bool portrait);
public static int    Lint();                   // 对已加载文件跑 §4.9 的 Lint，返回 issue 数；没加载 → -1
public static bool   PanelCollapsed { get; set; }
```

菜单项 = 这组静态的壳（编辑态 `Launch()`，预览中翻 `PanelCollapsed`）。memory 里那套「反射 `_projectRoot` / `RefreshFiles` /
`ResolveSrc` / `LoadFileAsync`」退休。截图仍由调用方 `ScreenCapture.CaptureScreenshot`（必须独立一次 execute_code）。

## 5. 新公开 API（Runtime，`UI`）

**无。** 原本这里的 `CommonLibraryRef` / `UI.LoadedCommonLibraries` / `CommonsSources` 有序表 / reload 保 `as=` 全部由
`2026-09-18-commons-settings-design.md` 承担（`PromptUGUISettings.commonLibraries` + `UI.EnsureCommonLibrariesAsync()`，
`DepGraph.CommonsSources` 记 src → as）。本文对 Runtime **不加任何东西**；Editor 侧新增的 `UiXmlLocator` 是 internal。

## 6. 实现地图

| 文件 | 内容 |
|---|---|
| `Editor/UiXmlLocator.cs`（新） | 从 `UIXmlLintMenu.cs` 抽出：`FindProjectUiXml` / `IsProjectOwned` / `Physical` / `ToAssetPath` / `AddressableUiXml` / `Locate`（§4.4） |
| `Editor/UIXmlLintMenu.cs` | 改调 `UiXmlLocator`；行为不变；`Lint` / `ConfiguredCommonLibraries` 留在原处 |
| `Editor/Preview/UIPreview.cs` | 菜单项、`[InitializeOnLoad]` 会话状态机（§4.1）、注入（§4.3）、§4.13 静态入口（转发到覆盖层） |
| `Editor/Preview/UIPreviewOverlay.cs` | IMGUI 覆盖层：面板 / 页条 / 诊断行 / 横竖屏 / 主题 / Lint；注入序列；加载流程；Update 重新收集 |
| `Editor/Preview/UIPreviewResolver.cs` | 纯逻辑：解析链（§4.4，`physical` / `exists` / `locate` 注入）、served 表 + `AssetPathToSrc` 组合、后缀匹配、`HasScreen`、`PagesKey`——**不碰 Unity 对象**，可测 |
| `Editor/Preview/UIPreviewRules.cs` | 纯逻辑：F8 的三态决策 `Decide`、场景选择 `PickScene` |
| `Runtime/Application/UIPreviewHost.cs` | 整文件 `#if UNITY_EDITOR`：覆盖层的组件壳（见 §13-1），Player 里不存在这个类型 |
| `Editor/Preview/UIPreviewSettings.cs` / `UIPreviewUserState.cs` | 两个 `ScriptableSingleton` |
| `Editor/Preview/UIPreviewSettingsProvider.cs` | Project Settings 页（`SceneAsset` 字段 ↔ GUID） |
| `Editor/Preview/UIPreview.unity` | 内置场景（相机一台） |
| `Editor/PromptUGUI.Editor.asmdef` | 加 `Unity.InputSystem`、`PromptUGUI.Compat.UniTask`、`UniTask`（Awaitable 在 2022.3 上的 polyfill，与 Runtime 一致） |
| `Editor/AssemblyInfo.cs` | `InternalsVisibleTo("PromptUGUI.Tests.EditMode.Addressables")`（§7-1 的 Addressables 分支测试） |

## 7. 测试（Red first）

EditorOnly（`PromptUGUI.Tests.EditorOnly`）：

1. `UiXmlLocator` 抽取是纯重构：`UIXmlLintMenuTests` 只改被搬走成员的调用点（`Physical` / `ToAssetPath` / `IsProjectOwned` → `UiXmlLocator`），断言不动、全绿。新 `UiXmlLocatorTests`：Resources 式 `Skin.ui` 从锚点目录上溯命中
   `…/Resources/UI/Skin.ui.xml`；都不中 → null；`Physical("Packages/com.promptugui.core/…")` 给出磁盘上存在的路径。Addressables 分支
   （地址 / GUID 精确命中）放 `PromptUGUI.Tests.EditMode.Addressables`（`AddressableHotReloadTests` 的临时条目夹具；`Editor/AssemblyInfo.cs`
   给它加一行 `InternalsVisibleTo`——今天只对 `Tests.EditorOnly` 开）。
2. `HasScreen` 对 `<Screen>` / `<Screen ` / `<Screen/>` 为真、对 `<ScreenFoo>` 为假、读不出来为真。
3. 解析链顺序（`locate` / `exists` / `read` 全用 fake）：绝对路径 > `Physical` > `Locate` > 唯一后缀 > `Assets/` 找不到抛 > 宿主；
   宿主为空且走到第 6 步 → 抛。
4. 后缀兜底：`Foo.ui` 与 `Foo.ui.xml` 都命中 `…/Foo.ui.xml`；命中两个 → 回落宿主（不选）。
5. served 表 + `AssetPathToSrc`：工具解析过的资产路径返回当初的 src（短地址优先于宿主映射）；没解析过的走宿主；宿主为空返回 null；
   预解析写入的条目同样命中。
6. `PagesKey` 组合与同 id 编号。
7. launch 规则（纯函数 `Decide(isPlaying, isActive, compilationFailed)` → Launch / ToggleCollapse / Refuse(reason)）。
8. 场景选择（纯函数）：GUID 解析出路径 → 用它；空 → 内置；非空却解析不到 → 内置 + 警告文案。

手工验证（ssw_re_client，Unity MCP）：

9. F8 → **不弹对话框**，直接进内置场景 → 覆盖层在、诊断行对（commons 行 ✓、主题 dark、sprites ok）→ 自动加载上次文件 → 截图横 / 竖
   → Stop → `playModeStartScene` 还原为 Login（`DevPlayFromCurrentScene` 开着）。
10. Project Settings 选 ssw 的 `UIPreview.unity`（摘掉组件后）→ 行星背景在、覆盖层在、场景文件 `git status` 无改动；把场景改名 → 诊断行黄字 + 仍能进内置场景。
11. 保存一个被预览文件的 import 依赖（短地址）→ 热重载 → 页条选回上次的页；保存 `DefaultTheme.ui.xml` → commons 热重载 → 界面换色。
12. 地址 ≠ 路径：临时把某条 Addressables 地址改成 `ui-test`，`<Import src="ui-test">` 仍读盘（改文件不导入也能看到），改回。
13. `UIPreview.Launch()` / `Load()` / `Select()` / `Lint()` 从 execute_code 直接调通，不再反射。
14. 关掉 Domain Reload（Enter Play Mode Options）跑一遍 9——钩子路径不同。
15. 包以 git URL 安装的工程（`PromptUGUIDev` 或临时工程）：内置场景在只读 PackageCache 里也能作 `playModeStartScene`。
16. 内置场景 + 一个没有 `RuntimeInitializeOnLoadMethod` 的工程（样例）：三条诊断（sprite / theme / 宿主 resolver）各触发一次，文案对。

## 8. 文档更新（同一 PR，英文）

- `README.md`（英 / 中）「Usage」加一节 **UI Preview**：F8 即用、自己的场景去 Project Settings › PromptUGUI › UI Preview、
  boot 契约一句话（"your `[RuntimeInitializeOnLoadMethod]` runs in the preview scene too — resolvers, sprite sets, theme and locale
  come from it"）、与 DocumentHost 的分工。
- `.claude/skills/scripting-promptugui-csharp/SKILL.md`：「Tooling」段（现在讲 `FindAll<Pages>` 那里）加 UI Preview 的自动化入口表（§4.13）
  ——给驱动 Unity 的助手用。
- `CLAUDE.md`（本仓库）项目布局表：`Editor/Preview/` 一行；`Editor/UiXmlLocator.cs` 一句（lint 菜单与预览共用）。
- 宿主侧（PR 合并后另做）：memory `unity-uipreview-scene-via-mcp` 重写为公开入口版；ssw `CLAUDE.md` 的「编辑器里的开发回路」
  改指 `Tools/PromptUGUI/UI Preview`。

## 9. 宿主迁移（ssw_re_client，另案）

1. 升级包与删本地工具**同一提交**——两个 `_F8` 会撞快捷键：删 `Assets/Tools/UI Preview/UIPreview.cs`、`Editor/UIPreviewMenu.cs`
   （及 meta）；`UIPreview.unity` 里删掉挂 `UIPreview` 的 GameObject，其余（行星、灯、相机、EventSystem）留着当用户场景。
2. Project Settings → PromptUGUI → UI Preview → Scene 选 `Assets/Tools/UI Preview/UIPreview.unity`（不选也能用，只少了行星背景）。
   commons 不用配：`PromptUGUI_Settings.asset → Common Libraries` 里已经列了 `UI/Templates/DefaultTheme.ui.xml`。
3. 验 §7-9 ~ 13。`DevPlayFromCurrentScene` 不改——§4.1 的订阅顺序保证预览赢；若实测输了，才在它里面按
   `SessionState.GetBool("PromptUGUI.Preview.Pending")` 让路（一行）。

## 10. 非目标

- 独立 exe 预览（拖 `.ui.xml` 到 exe、`-project` 参数）：库版是编辑器工具；ssw 的本地工具随迁移删除（§11-6）。
- 模态框（MessageBox 等）的一键预览按钮：它们要走真 `Bind`；磁盘 resolver 让 memory 第 5 条的 MCP 步骤照样成立（短地址经定位器读盘），先不做按钮。
- 面板里的 locale 切换（`PlayModeLocaleMenu` 已有）、截图按钮、Game view 尺寸的退出还原。
- 替换或删除 `PromptUGUIDocumentHost`。
- 库自己装配 resolver / SpriteSet / 主题（§2-B）。
- 同一文件被两种 src 引用时两边都热重载（§4.5 的 v1 限制）。

## 11. 已定的决策

| # | 项 | 决定 |
|---|---|---|
| 1 | 覆盖层所在程序集 | `PromptUGUI.Editor`（Player 零痕迹） |
| 2 | 内置场景 | 包内 `Editor/Preview/UIPreview.unity`，只有相机 |
| 3 | 首次对话框 | **不要**（第二轮）：内置场景是默认，自己的场景去 Project Settings 选 |
| 4 | 默认快捷键 | F8（Shortcut Manager 可改） |
| 5 | 自动加载上次文件 | 开：注入 → Ensure → 加载 |
| 6 | ssw 的 exe 预览模式 | 放弃（删本地工具） |
| 7 | 个人状态存放 | `UserSettings/PromptUGUI/Preview.asset` |
| 8 | src → 磁盘文件定位 | （第二轮）复用 lint 菜单的定位器，抽 `UiXmlLocator`；后缀匹配只做兜底 |
| 9 | 文件列表 | （第二轮）`FindProjectUiXml()`（`Assets/` + embedded 包），没有 `scanRoots` |
| 10 | 场景引用形态 | （第二轮）GUID，存 `ProjectSettings/PromptUGUIPreview.asset`；不进 `PromptUGUISettings.asset` |
| 11 | 注入序列 | （第二轮）先 `await EnsureCommonLibrariesAsync()` 再诊断、再自动加载 |
| 12 | Lint 按钮 | （第二轮）做——同 asmdef 里几行 |

## 12. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| M0 | `UiXmlLocator` 抽取（纯重构）+ `UiXmlLocatorTests`（§7-1） | `UIXmlLintMenuTests` 断言不动、全绿；EditorOnly 全绿 |
| M1 | 设置单例 + Project Settings 页 + 会话状态机 + 内置场景 + 注入一个空覆盖层（§7-7、8 Red → Green） | §7-9 的 F8 → Play → 覆盖层在 → Stop → 还原 |
| M2 | 覆盖层搬家：解析链 / 列表 / 加载 / 多 Screen / 页条 / 个人状态 / §4.13 静态入口（§7-2~6 Red → Green） | §7-11、12、13 |
| M3 | 注入序列（Ensure + 预解析 + 自动加载）、诊断行、横竖屏、主题按钮、Lint 按钮 | §7-16 三条诊断各触发一次；§7-10 |
| M4 | 文档（§8）；宿主迁移（§9）另案 | lint 过、PR |

## 13. 实施记录（2026-09-18，分支 `feat/ui-preview-tool`）

M0（`e8cc5f9`）、M1（`a191c23`）、M2（`f4f7c5b`）、M3（`076147d`）已落地；与上文的出入：

1. **覆盖层不能是 Editor 程序集里的 MonoBehaviour。** §11-1 的推定错了：`AddComponent` 时 Unity 拒绝——
   "Can't add script behaviour 'UIPreviewOverlay' because it is an editor script"。改为 `Runtime/Application/UIPreviewHost.cs`
   （整文件 `#if UNITY_EDITOR`，同 `AddressableResolverHelper` 的手法）做组件壳、转发 `Update` / `OnGUI` / `OnDestroy` 三条消息，
   `UIPreviewOverlay` 是 Editor 程序集里实现 `UIPreviewHost.IOverlay` 的普通类。Player 零痕迹的目标不变（类型都不存在），
   不需要 `PromptUGUIPreviewBuildStripper` 那种剥离。
2. **顺手修了库里一个死锁**（`Runtime/Application/UI.cs`）：`EnsureCommonLibrariesAsync` 先 `ReleaseCommonsWaiters` 再在 `finally`
   里放下 `_ensuringCommons`，而 waiter 的续体是同步跑的——§4.11 的注入序列（等宿主那次 → `UnloadAll` → `LoadDocumentAsync` → 再 Ensure）
   正好在标志还立着的时候再次进来，把自己挂到一张永远没人放行的 waiter 表上，`LoadAsync` 永远 Busy（ssw 实测复现）。
   现在先放标志再放行 waiter；回归测试 `EnsureCommonLibrariesTests.Ensure_AWaiterThatUnloadsAndEnsuresAgain_StartsANewLoad_InsteadOfWaitingForever`。
3. 纯逻辑拆成两个文件：`UIPreviewResolver.cs`（解析链 / served / `HasScreen` / `PagesKey`）与 `UIPreviewRules.cs`（`Decide` / `PickScene`）。
4. `UiXmlLocator.MakeResolver()` 保留 lint 菜单原来"一次 run 只读一次 Addressables 表"的形态，`Locate(src, anchor)` 是单次查询的便捷壳；
   `UIXmlLintMenu.Report` 改 internal 给 Lint 按钮复用。
5. `Load(file)` 除资产路径 / 绝对路径外也接受唯一的路径尾巴（`Planet.ui.xml`）——MCP 里少打一截。
6. 面板改为单个 `GUILayout` 区域、列表吃剩余高度（诊断行数不定，手算高度不再可行）；`PanelMaxHeight` 720。
7. `ProjectSettings/PromptUGUIPreview.asset` 只在 Project Settings 页改过东西后才落盘（`ScriptableSingleton` 只在 `Save` 时写）——
   用默认值的工程不会多出一个文件。
8. 已验证（ssw_re_client，Unity MCP）：§7-9（`DevPlayFromCurrentScene` 打开时预览场景照样赢，Stop 后 `playModeStartScene` 回到 Login，
   SessionState 清空）；§7-11 入口文件与 `DefaultTheme.ui.xml` 的热重载都重开了 Screen、`newView` 选回；§7-13 全部静态入口；
   横 / 竖屏截图；sprite 诊断红字（把 `SpriteResolver` 置空验的）。**未验**：§7-10（宿主迁移另案）、§7-14（关 Domain Reload）、
   §7-15（git URL 安装）、§7-16 的主题 / commons 两条黄字。
