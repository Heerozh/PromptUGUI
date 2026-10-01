# 运行时 SpriteSet（整包 / 按需）+ 运行时 .po 目录（RSS）

> 状态：**已实现**（2026-10-01，实施记录见 §16）。§14 的 16 条按推荐执行；plan 阶段设计评审的修订（P1–P14）已写回正文，汇总见 §14.1。
> 实施计划：`docs~/superpowers/plans/2026-10-01-runtime-sprite-sets.md`。分支 `feat/runtime-sprite-sets`。决策编号 `RSS-Dn`。
> 相关：master spec §5.4（`<Icon>`）；`2026-05-08-icon-assets-design.md`（SpriteSet 与名字解析）；
> `2026-09-17-common-attr-runtime-state-design.md` §4.6（`<Icon name>` 运行期独占——本文的刷新必须绕开它）；
> `2026-09-14-scrolllist-row-reuse-design.md` §4.2 与 `2026-09-29-scrolllist-virtualization-design.md`（行复用 / 停放前
> `ReleaseSubscriptions`）；`2026-09-14-document-load-parallel-prefetch-and-source-cache-design.md` §4.2（Awaitable 池化，
> 每个实例恰好 await 一次）；`2026-09-04-external-po-roots-design.md`（.po 的其它来源）。
> 动机：游戏的 UGC 资源包——图标和 .po 在运行时从服务器下载，要和内置 SpriteSet 一样用 `集名:键` 引用。

## 0. 背景与结论

需求原文（摘要）：新增 `UI.RegisterRuntimeSpriteSet(setName, entries)` / `UI.UnregisterRuntimeSpriteSet(setName)`，
entries 是「键 → Sprite + `tiled`」，与 `SpriteSet.EntriesWithMeta` 同口径；运行时集并入查找表，resolver 重装和热重载
都不丢，与已有集重名就抛错；注册和注销都触发重解析，已打开的界面自动刷新，诊断用的 `LoadedSpriteSetNames` 也要准确；
可选的缺图占位，同一个键只告警一次；Sprite / Texture 归调用方所有。下载、磁盘缓存、PNG 解码、运行时拼图集都由游戏侧做。
次要：`.po` 对称地加 `UI.Locale.RegisterRuntimeCatalog`。

讨论中作者提出更进一步的形态：注册时只给集名和一份 URL 清单，遇到 `集:键` 才去下载（KTX2），下完自动刷新。结论：

- **进库**：「按需异步取图 + 同一套 `集:键` 引用」。注册时给一个**取图函数**，库负责去重、占位、刷新和生命周期。
- **留在游戏侧**：「查 URL → 下载 → KTX2 转码 → `Sprite.Create` → 磁盘缓存」，全部写在取图函数里（§3-A）。
- **晚到的图不走 Notify / ReSolve 广播**：由 `<Icon name>` / `<Image sprite>` 内部的 slot 自己刷新（§1.2、§7）。
- 需求里的「整包注册」保留，它是按需的特例：所有键一注册就就绪。

## 1. 问题（现状）

### 1.1 只能在外面包一层 `UI.SpriteResolver`

1. `UseSpriteSetResolver` / `UseAddressableSpriteSetResolver` 重新调用、编辑器热重载的 `Rebuild()`，每次都整体替换
   `UI.SpriteResolver`（`SpriteResolverHelpers.cs:15`、`:29`，`AddressableSpriteResolverHelper.cs:93`）。外面包的那层会被悄悄覆盖。
2. 重解析广播 `BeginSpriteResolverLoad` / `EndSpriteResolverLoad` 是 internal（`UI.cs:50`、`:58`）。
3. `LoadedSpriteSetNames` 是 internal，`BuildLookup` 每次先 `Clear()`（`SpriteResolverHelpers.cs:46`）。外部集缺图时，
   诊断会误报「SpriteSet 未加载」（`UI.cs:151`）。

### 1.2 现有广播刷不到 UGC 图标（比 1.1 更根本）

唯一的刷新手段是 `VariantStore.NotifyChangedInternal()` → 每个打开的 Screen 全量 `ReSolve()`（`Screen.cs:287`、`:1190`）。
它有三个问题：

1. **刷不到代码写入的 key。** `<Icon name>` 是 RuntimeStateAttr（`BuiltinPrimitives.cs:18`）：代码写过之后，ReSolve
   会跳过它（`ControlAttributeApplier.cs:82`），setter 不再执行。`<Image sprite>` 更糟：XML 没声明就不重放，声明了又会被
   打回声明值。UGC 的 key 是数据，几乎都在 `BindItems` 回调里由代码写入——恰好全部刷不到。所以把广播公开（需求第 2 条）
   解决不了问题。
2. **太贵。** 全量重放包括 BindItems 行（`Screen.cs:1186`：500 行的列表重放一次约 550 ms），而且同步、不合帧。
   按 key 陆续到达，等于到一张就重放一次。
3. **只换 sprite 不够。** `Image.OnAfterApply`（`Image.cs:210`）用最终的 sprite 推导 fit 宽高比、自动 sliced 和 FX 材质；
   尺寸是在 `ApplyCommon` 里、apply 的时候一次算定的（`Control.cs:297`、`:390`、`:519`）。

### 1.3 顺带发现的潜在 bug（按代码推断，先写 Red 测试证实）

Addressables resolver 还在加载时（`IsSpriteResolverLoadInFlight`），代码写入的 `icon.Name` 当时会静默留空，
之后要靠 `EndSpriteResolverLoad` 的广播补上。但这次广播正好被 1.2 第 1 条的锁跳过，图标就一直空着。

### 1.4 .po

`UI.PoResolver` 是单个委托。`TranslationStore` 是一张扁平表，后写的覆盖先写的，也不记来源（`TranslationStore.cs:8`、`:23`）。
运行时追加的 .po 只能在外面包一层，再 `ReloadCurrentAsync()` 整表重载；想单独撤掉一份也做不到——它可能覆盖过内置词条。

## 2. 目标

- **G1** 运行时注册 / 注销 SpriteSet，两种形态：**整包集**（键 → Sprite 一次给全）和**按需集**（键 → 异步取图函数）。
  引用写法与内置集完全相同（`集:键`）。
- **G2** 按需集的图晚到时，正在显示它的 `<Icon>` / `<Image>` 自动刷新。代码写入的 key、复用 / 停放过的行都要覆盖，
  而且不触发全量 ReSolve。
- **G3** 运行时集与 `UI.SpriteResolver` 分开存放：resolver 重装、热重载都不丢；重名就抛错；诊断准确。
- **G4** 缺图时显示占位，同一个键只告警一次。
- **G5** 运行时 .po 目录：注册、注销、切语言时自动带上，而且能单独撤掉。
- **G6** 修掉 1.3。

非目标见 §13。

## 3. 否决的方案

**A. 库内置「URL 清单 + 下载 + KTX2 转码」。否决。** 库在运行时不碰内容来源（`SourceResolver` / `PoResolver` 都是注入的）。
KTX2 要靠 KTX for Unity（`com.unity.cloud.ktx`）的原生转码插件，每个平台一份，不能当核心依赖。下载策略（鉴权、CDN 签名、
重试、超时、离线、磁盘缓存、哈希校验）每个游戏都不同，内置进来要么开一堆旋钮，要么谁用都不顺手。取图函数的签名把这些
全部留给游戏，库只管「拿到 Sprite 之后」。示例代码可以另案做成 Sample（§13）。

**B. 图晚到时走全量广播。否决。** 理由见 §1.2 三条。

**C. 中心登记「哪个控件的哪个属性在等」，图到达后按属性名反射回放 setter。否决。** 要在 setter 外部捕获
（control, attr）——代码直接赋值时根本没有这个上下文；要靠反射重调；改动面是 15 个控件、37 个 sprite 属性。
把状态放在控件自己身上（slot）更简单。

**D. 新增 `<AsyncIcon>` / `<RemoteImage>` 标签。否决。**
- 同一个道具卡模板 `<Icon name="{{icon}}"/>` 要能同时接内置 key 和 UGC key。
- 集是同步还是按需，由 C# 注册时决定，不该漏进 XML。
- 新标签得把 tint / blur / glow / intensity / rotation / flip / type / mask 和 XSD / skill / lint 全抄一遍。

代价是失去了让 lint 静态检查尺寸的机会，改成运行时告警（§7.5）。

**E. 其余 sprite 属性遇到按需集时「已缓存就显示，否则留空」。否决。** 报不报错取决于时序，测试测不出来。
改为确定性报错（§6.3）。

**F. 未注册的集名当成 pending，等它注册。否决。** 集名拼错会永远没有任何提示。保留立即报错，另外加上「注册之后自愈」
（§6.6）。

**G. 库自动重试失败的请求，或在行离开视野时取消请求。v1 不做。** 这是游戏的策略，在取图函数里自己重试、限流即可。
库只保证同一个 key 只调用一次。

## 4. 方案总览

```
游戏侧                          PromptUGUI
──────                          ──────────
清单 / 下载 / 缓存 / 解码        RuntimeSpriteSets（中心，只管数据）
     │ Sprite                     集名 → 整包表 | 取图函数 + 每 key 状态（未请求 / 加载中 / 就绪 / 缺图）
     ▼                            每 key 的等待者；每集的绑定者；按集名的等待表；告警去重
UI.RegisterRuntimeSpriteSet
                                AsyncSpriteSlot（每个 <Icon> / <Image> 一个，只管自己）
                                  Set(value)：立即贴 | Loading 占位 + 订阅 | Missing 占位
                                  图到达：校验 → 贴图 → 宿主重算依赖 sprite 的状态

                                UI.ResolveSprite（其余 35 个 sprite 属性 + 自定义控件）
                                  整包集：同步命中 / Missing；按需集：确定性报错
```

```csharp
// 按需集：游戏实现取图函数（查清单、下载、KTX2 转码、Sprite.Create、磁盘缓存都在里面）
UI.RegisterRuntimeSpriteSet("ugc", key => UgcIcons.LoadAsync(key),
    new RuntimeSpriteSetOptions { Loading = spinner, Missing = questionMark });

// 整包集：资源包已经下好、解好
UI.RegisterRuntimeSpriteSet("pack42", new Dictionary<string, RuntimeSprite>
{
    ["sword"] = swordSprite,                            // Sprite 隐式转 RuntimeSprite
    ["vine"]  = new RuntimeSprite(vineSprite, tiled: true),
});

UI.UnregisterRuntimeSpriteSet("pack42");   // 返回后由调用方自己 Destroy 这些 Sprite / Texture
```

```xml
<Template name="ItemCard">
  <Param name="icon" default="ui:placeholder"/>
  <Icon name="{{icon}}" size="48"/>   <!-- 内置的 "ui:sword" 和 UGC 的 "ugc:9f3a@2" 都能传进来 -->
</Template>
```

## 5. 公开 API

### 5.1 类型（`PromptUGUI.Application`）

```csharp
public readonly struct RuntimeSprite
{
    public RuntimeSprite(Sprite sprite, bool tiled = false);
    public Sprite Sprite { get; }
    public bool Tiled { get; }          // 同 SpriteSet 条目的 tiled，登记进 SpriteRenderHints
    public static implicit operator RuntimeSprite(Sprite sprite);
}
// default(RuntimeSprite)（Sprite == null）= 缺图

public sealed class RuntimeSpriteSetOptions
{
    public Sprite Loading { get; set; }   // 按需集加载中显示；null = 什么都不画。整包集忽略此项
    public Sprite Missing { get; set; }   // 缺图时显示；null = 什么都不画
}
```

「什么都不画」不是 uGUI 默认的行为：sprite 为 null 的 Image 会画一个实心块（`FxImage.OnPopulateMesh` 先调 base，
库里 `CaptionBuilder.cs:176` 也记过）。做法见 §7.6。`options` 在注册时做快照，之后再改它不影响已注册的集。

### 5.2 `UI` 门面

```csharp
public static void RegisterRuntimeSpriteSet(string setName,
    IReadOnlyDictionary<string, RuntimeSprite> entries, RuntimeSpriteSetOptions options = null);
public static void RegisterRuntimeSpriteSet(string setName,
    Func<string, Awaitable<RuntimeSprite>> load, RuntimeSpriteSetOptions options = null);
public static bool UnregisterRuntimeSpriteSet(string setName);   // 未注册 → false
```

- `setName` 必须匹配 `[A-Za-z0-9_-]+`（与 `<Icon name>` 的 XSD 同规，`XsdGenerator.cs:153`），否则抛 `ArgumentException`。
  `entries` / `load` 为 null 抛 `ArgumentNullException`。
- 与已注册的运行时集或已加载的静态集重名 → 抛 `InvalidOperationException("Duplicate SpriteSet name '…'")`，
  消息里注明对方是哪一类。
- `entries` 在注册时拷贝一份快照。值为 null Sprite 的条目跳过（同 `BuildLookup`）；空键抛 `ArgumentException`。
- key = 引用里**第一个 `:` 之后**的全部（沿用今天的切法），库不解析也不校验其内容。约定：用逻辑名、不带扩展名；
  内容变了就换一个 key（带版本或哈希），不在原 key 上更新（v1 没有 Invalidate，§13）。

### 5.3 取图函数（按需集）的契约

| 项 | 约定 |
|---|---|
| 线程 | 主线程。同步部分（第一个 `await` 之前）可能在属性应用过程中被调用——不要在里面同步开关界面，也不要注册 / 注销集 |
| 完成线程 | 返回的 Awaitable 必须在主线程完成。在后台线程解码的，返回前先 `await Awaitable.MainThreadAsync()`，否则回调会在后台线程改 `Image.sprite` |
| 实例 | 每次调用返回一个新的 Awaitable。Awaitable 是池化的，按 URL 去重的缓存不能把同一个实例交给两个 key |
| 次数 | 一次注册的生命周期内，每个 key 最多调用一次（同 key 并发去重，结果缓存） |
| 同步完成 | 返回已完成的 Awaitable（比如内存缓存命中）→ 当场就绪，不显示 Loading |
| 缺图 | 返回 `default`（`Sprite == null`）→ 按缺图处理（§6.5）。**预期内的失败（404、离线）应该这样报** |
| 空 key | 代码写入的 `"ugc:"` 直接按缺图处理，不调用取图函数 |
| 异常 | 同步抛或异步抛 → 按缺图处理，并 `UILog.Error` 一次（带异常信息）。只用于意外错误 |
| 重试 / 取消 | 库不重试、不取消，需要的话在函数里自己做。想整体重来：注销后重新注册 |
| 所有权 | 库只持有引用，从不销毁。注销后迟到的结果直接丢弃、不引用，由函数背后的缓存负责销毁 |

实现注意：

- 对返回的 Awaitable 只取**一个** awaiter——`IsCompleted` 为真就当场 `GetResult()`，否则在 `OnCompleted` 回调里
  `GetResult()`，保证恰好取一次结果。只用 `GetAwaiter()` / `IsCompleted` / `GetResult()` / `OnCompleted`，Unity < 6 的
  UniTask 垫片（`Runtime/Compat/Awaitable.cs`）也能用。
- **先登记、再调用**：调用取图函数之前，先把这个 key 的条目以「加载中」放进表里。这样取图函数同步重入请求同一个 key 时
  只会排队，不会再调一次。结算是幂等的：条目已不是「加载中」就直接返回；注册已作废就丢弃结果。

### 5.4 所有权（整包集和按需集相同）

Sprite、Texture、占位图都归调用方。规则只有两条：

- **注册期间不要销毁已经交给库的 Sprite**，否则正在显示它的 Image 会画成一个纯色块。
- `UnregisterRuntimeSpriteSet` 返回后，库的注册表和 slot 不再引用该集的任何对象（§6.6）。其余 35 个属性里已经显示出来的
  Sprite 由各控件自己持有，直到被重放或界面关闭——所以先关掉或切走仍在用它的界面，再注销、再销毁。

按需集的缓存只在注销时整体释放。浏览量大的场景，比如离开 UGC 浏览页时，可以注销后用一个空缓存重新注册：
仍在显示的图标会自动重新请求。

## 6. 解析语义

### 6.1 解析顺序

对含 `:` 的值，先取集名（第一个 `:` 之前）：

- 集名是已注册的运行时集 → 走运行时路径。
- 否则 → 走今天的静态路径（`UI.SpriteResolver`、加载中静默、报错）。

运行时路径不依赖 `UI.SpriteResolver` 是否存在，只用运行时集的游戏不会看到 "SpriteResolver is not registered"。

### 6.2 key 状态（按需集，每次注册一份）

未请求 → 第一次被请求 → 加载中 → 就绪 | 缺图。同步完成的直接进就绪 / 缺图。状态只在这次注册期内有效，注销即丢弃。

### 6.3 同步入口 `UI.ResolveSprite`

使用者：其余 35 个 sprite 属性（Btn / Toggle / Slider / Dropdown / Progress / Tab / … 的 `sprite` / `bg` / `fill` / `icon` 等）、
`ProceduralBuilders`、自定义控件。

| 情况 | 返回 | 日志 |
|---|---|---|
| 整包集命中 | 该 Sprite | — |
| 整包集缺这个键 | `options.Missing`（可为 null） | `UILog.Warn`，每个 (集, 键) 一次 |
| 按需集（不管是否已缓存） | null | `UILog.Error`，每个 (集, 键) 一次：只有 `<Icon name>` / `<Image sprite>` 能等按需集，要在这里用请改成整包注册 |
| 非运行时集 | 今天的行为，逐字不变 | 今天的行为 |

### 6.4 异步入口：`<Icon name>` / `<Image sprite>` 的 slot（§7）

| 情况 | 立即显示 | 之后 |
|---|---|---|
| 整包集命中 / 按需集已就绪 | 该 Sprite | — |
| 按需集加载中（含刚发起、没有同步完成） | `Loading`（可为 null） | 到达后自动换成结果 |
| 缺图（整包缺键、按需返回空或抛异常） | `Missing`（可为 null） | — |
| 静态路径解析失败（集未注册 / 缺键） | null + 今天的报错 | 之后注册了同名运行时集 → 自愈（§6.6）。集名属于已加载的静态集时不登记——同名运行时集一注册就会抛重名，自愈到不了 |
| 静态 resolver 正在加载（§1.3） | null，静默 | 代码写入的值：`EndSpriteResolverLoad` 计数归零时重新解析；XML 写入的值：交给随后的广播重放（§7.7） |
| 其它非运行时值 | 今天的行为：Icon 走 `UI.SpriteResolver`；Image 走 `UI.ResolveSpriteStatic`（今天 `ResolveSprite` 的函数体，含 Resources 路径与 `#slice`） | — |

表里「可为 null」的占位、以及注销后的清空，都是**什么都不画**（§7.6）。静态路径给出的 null 保持今天的样子。

### 6.5 日志

每次注册有三张去重表，随注销一起作废：

- 加载中：不打日志。
- 缺图：`UILog.Warn`，每个 (集, 键) 一次。slot 用 `UILog.Warn(control, …)`；同步入口手里没有控件，用新增的
  `UILog.Warn(string)`（同 `Error(string)`，以 `Applying` 定位）。同步入口和 slot 共用这张表。
- 取图函数抛异常：`UILog.Error`，每个 (集, 键) 一次，带异常信息。异步抛出时没有节点上下文，用第一个还活着的等待者的控件打。
  发生过异常的 key 不再打缺图 Warn（否则一次失败两条日志）。
- 按需集用在同步入口：`UILog.Error`，每个 (集, 键) 一次（§6.3）。
- 静态集：保持今天的报错，不变。

### 6.6 注册 / 注销 / 同名重注册

- **注册**：
  - 按集名等待中的 slot（静态解析失败的，以及上一次注销后在等的）立即重新解析。
  - **整包集**另外广播一次 `VariantStore.NotifyChangedInternal()`，让其余属性里 XML 写死的引用补上。这是低频事件；
    要批量注册就放在开界面之前做。
  - 按需集不广播：其余属性本来就不能用它。
- **注销**：
  1. 先作废这次注册，迟到的结果一律丢弃。
  2. 绑定在这个集上的 slot 全部清空（`sprite = null`，什么都不画，§7.6），转入按集名等待。不显示该集的 Missing 图——
     它也归调用方，马上会被销毁。
  3. 整包集再广播一次。

  返回后，库不再引用该集的任何对象。注销前应先关掉或切走仍在用它的界面，否则其余属性在广播重放时会报「未注册」。
- **同名重注册（换包）** = 注销 + 注册：已显示的图标会自动换成新包里的图。这是 v1 的「动态更新」方式。

### 6.7 与 resolver 重装、热重载、重置的关系

- 运行时集存放在 `UI.SpriteResolver` 之外。`UseSpriteSetResolver` / `UseAddressableSpriteSetResolver` 重新调用、
  `HotReload.SpriteResolverRebuilder` 都碰不到它们。
- `BuildLookup` 发现静态集与运行时集重名时，抛同一个 `Duplicate SpriteSet name`，消息注明对方是运行时注册的。
  它**先预检、再改状态**：静态集互相重名、与运行时集重名都检查完，才 `LoadedSpriteSetNames.Clear()`，不会留下半张名单。
  直接自定义 `UI.SpriteResolver` 委托的工程，静态集名是未知的，没法查重名，此时运行时集按集名优先。
- 已知限制：运行时注册与**正在进行**的 Addressables 静态加载撞名时，注册那一刻检测不到；等静态加载完成时
  `BuildLookup` 抛重名，整批静态集安装失败。文档写明。
- 清空入口是不带版本守卫的 internal `UI.ClearRuntimeRegistrations()`：先把每个注册标记为已作废（之后到达的结果一律丢弃），
  再清空运行时集、各等待表、去重表、运行时 .po 目录与层（§8）。调用方：
  - `UI.ResetForTests`；
  - 新增的 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 钩子（先例 `UI.ResetCommonsStatics`）；
  - 6000.5+ 的 `[OnEnteringPlayMode]` / `[OnExitingPlayMode]` 钩子（`UI.cs:1445`），在 `UnloadAll()` 之后调用。

  原因：关掉 Domain Reload 时，上一次 Play 的注册会带着已销毁的 Sprite 留下来，下一次启动重新注册还会撞名。
  dev 宿主是 6000.0，进出 Play 的钩子编译不进去，所以必须有 SubsystemRegistration 这一条。
  推论：游戏不要在 SubsystemRegistration 阶段注册运行时集或目录（同一阶段的回调之间没有顺序保证）。
- `UI.UnloadAll` 不清空：路由重连之类的 teardown 不该丢掉游戏启动时注册的集。

### 6.8 诊断

- `BuildSpriteResolutionFailureMessage` 列出的已加载集 = 静态集 ∪ 运行时集，后者标 `(runtime)` / `(runtime, on demand)`。
  运行时集不会再被误报为「未加载」。
- `LoadedSpriteSetNames` 保持 internal，只记静态集。`BuildLookup` 里的 `Clear()` 不再影响运行时集。

## 7. AsyncSpriteSlot

内部类 `PromptUGUI.Controls.Internal.AsyncSpriteSlot`，`Icon` / `Image` 各持一个。

### 7.1 状态

- **字段**：
  - `Value`：最后写入的字符串。
  - `State`：`None` / `Static` / `WaitingStaticResolver` / `Ready` / `Loading` / `Missing` / `Detached`。
  - 当前绑定的注册实例与 key。
  - 代次 `_gen`：每次 Set / Release / Detach 都加一。凡是可能跑用户代码的调用（取图函数的同步部分、日志回调），
    返回后都比对代次，不一致就放弃——这次 Set 已经被重入的那次取代了。
  - 最近一次 Set 是否发生在宿主自己的 apply 里（供 §7.7 使用）。
- **宿主接口** `ISpriteSlotHost`（Icon / Image 显式实现）：
  - `ResolveStatic(value, out sprite)`：静态解析，返回三态 `Ok` / `Deferred`（静态 resolver 正在加载）/ `Failed`，
    日志与今天逐字一致（Icon 用 `UILog.Error(this, "Icon '…'")`，Image 用 `UILog.Error(string)`，
    `RuntimeSourceAttributionTests` 钉住了这两种 at-line）。由宿主实现，因为 Icon 不看 `:`、什么值都交给 resolver。
  - `Assign(sprite, hideIfNull)`：写到宿主的 Image 上；`hideIfNull` 只由运行时状态传 true（§7.6）。
  - `RefreshDerived()`：重算依赖 sprite 的状态。
  - `SizeDependsOnSprite`：§7.5。

### 7.2 `Set(value)`

- 值与当前 `Value` 相同，且处于运行时状态（`Ready` / `Loading` / `Missing` / `Detached`）→ **什么都不做**。这样 ReSolve
  回放同一个 key 时既不会重发请求，也不会闪回 Loading。
- 其余情况一律重新解析：`++_gen`，从旧 key、旧集和各等待表里退订，再解析。静态值每次都重新解析，和今天一样——
  热重载后的 ReSolve 才能换上新的 Sprite。
- 按需集：先向注册表请求；请求返回、且条目仍是「加载中」时才挂进等待者（同步完成的直接进就绪 / 缺图）。
- 静态路径 `Failed` 且值带 `:` → 按集名登记到等待表（§6.4 的「自愈」）；集名属于已加载的静态集时不登记。
- 静态路径 `Deferred` → 进 `WaitingStaticResolver`（§7.7）。

### 7.3 图到达

1. 先校验：slot 仍绑定在这个（注册实例, key）上、仍是「加载中」，注册仍有效，宿主的 GameObject 也没被销毁。
2. 校验通过 → `Assign(sprite, hideIfNull: true)` → 刷新派生状态（规则见下）。
3. 任一条件不满足 → 丢弃，并顺手把这条登记摘掉。

**派生状态的刷新规则**：这次赋值来自运行时状态（Ready / Loading / Missing / Detached），且宿主**不在**自己的 apply 里
（`Control.InApplyPass` 为 false）→ 调 `RefreshDerived()`；在 apply 里则交给宿主的 `OnAfterApply`。
静态解析出的赋值一律不额外刷新——静态路径的行为与今天完全相同（§12）。这样运行时结果不论同步命中还是晚到，
派生出的 Image 类型都一样，与时序无关。

`InApplyPass` 由 `ControlAttributeApplier.Apply` 设置：先保存旧值再置 true，`finally` 恢复旧值（可能有嵌套 apply）。

通知之前先拷贝快照，每个 slot 的处理单独 `try/catch` + `Debug.LogException`：一个 slot 出错不影响后面的 slot，也不影响随后的广播。
所以在回调里注册 / 注销集也是安全的。

### 7.4 生命周期

- **订阅不能挂进控件的 `Track` 订阅袋。** ScrollList 复用行和停放行时，都会先调 `ReleaseSubscriptions()`
  （`ScrollList.cs:1232`、`:1689`、`:1720`；`Control.cs:803`）。如果 bind 写回的是同一个 key，§7.2 不会重新订阅，
  加载中的图就永远到不了。
- **退订时机**：
  - 换值；
  - 到达或注销通知时发现宿主已销毁；
  - 整个集被注销；
  - `ResetForTests`。

  动态子树里的内层控件不会被单独 Dispose（只随根 GameObject 级联销毁），所以退订不依赖 Dispose。
- **清扫**：行内控件从不单独 Dispose，反复开关 UGC 浏览页时，已销毁的 slot 会在各张表里越积越多。所以绑定表、
  按集名等待表、key 等待者三张表都走同一个 `AddPruned`：插入时若 `count ≥ max(64, 2 × 上次清扫后的存活数)`，
  就扫一遍、摘掉 `owner.GameObject == null` 的条目（均摊 O(1)）。遍历这些表时也顺手摘掉死条目。
- 取图函数应当自带超时：永不完成的 key 会让等它的 slot 一直挂在表里（会被清扫，但 key 条目本身留到注销）。

### 7.5 尺寸

- **规则**：用按需集的 `<Icon>` / `<Image>` 必须写定尺寸——每个轴要么有数值，要么是 stretch。图到达时不会重新排版。
- **理由**：
  - native 尺寸是在 `ApplyCommon` 里一次算定的。单独给一个节点重跑 `ApplyCommon`，会破坏「ApplyCommon 先重置、
    ApplyScales 再膨胀」这对契约（`Control.cs:363` 注释；`ApplyScales` 是 Screen 级的）。
  - 在 Stack 里，没写尺寸的轴会落到 uGUI Image 自己的 ILayoutElement 上，而有没有 Loading 占位图，结果又不一样。
    行为不确定，干脆要求写定尺寸。
  - `<Icon>` 的 `size` 默认就是 `native`（master spec §5.4），这条规则会经常碰到。
- **检测方式**：
  - `Control` 新增 `internal bool SizeFromNative`，由声明和父级类型决定，不看 `GetNativeSize()` 这次返回什么：
    - `ApplyCommon` 开头复位；
    - `size="native"` 分支置位；
    - 自由定位缺轴回退：父级不是 Grid 时置位（Grid 的格子尺寸由 cellSize 决定，`flow="false"` 的 Grid 子节点仍算自由定位）；
    - Stack 里：某轴没写、不是 hug、也不是交叉轴 fill（父级决定）→ 置位。这时 native 为 null，LayoutElement 留 -1，
      由 uGUI Image 自带的 ILayoutElement 按 sprite 接管，所以尺寸照样取决于图。
  - `<Image type="contain|cover">` 不算：它的框由父级和 AspectRatioFitter 驱动，图到达后重算宽高比即可（§7.6）。
  - slot 绑定的是按需集，且宿主的尺寸取决于 sprite → `UILog.Warn(control, …)`。**按 `SourceNode` 去重**（BindItems 的行共用
    模板节点，500 行不能报 500 条），没有 SourceNode 的按控件去重；去重表由 §6.7 的清空入口清掉。
  - 检查点有两处：宿主 `OnAfterApply` 末尾（apply 过程中写入），以及 apply 之外的 `Set`（由代码写入）。
  - 结果只取决于「这个集是不是按需集」和 XML 的尺寸声明，与图有没有缓存无关，因此是确定性的。
  - 已知漏报：没写尺寸的 `<Animation>` / `<Trigger>` 会用子节点 sprite 的 native 尺寸当自己的尺寸（`Trigger.cs:21-33`），
    即使子节点写了尺寸；子节点自己的标志看不到这一点。写进文档。

### 7.6 控件侧改动

- **什么都不画**：`FxImage` 新增 internal `DrawNothingWhenEmpty`，`OnPopulateMesh` 开头
  `if (DrawNothingWhenEmpty && sprite == null) { toFill.Clear(); return; }`，切换时 `SetVerticesDirty()`。
  `Assign(sprite, hideIfNull)` 把它设为 `hideIfNull`。不用 `enabled = false`：那会让 `mask="self"` 失效、也会改变 raycast。
  宿主上预置的是普通 Image（不是 FxImage）时才退回 `enabled`。
- `Icon.Name` → `_name = value; _slot.Set(value)`。`ResolveStatic` 搬今天 Icon 的逻辑；`RefreshDerived` 就是
  `ImageFxApplier.Flush(_img)`；`OnAfterApply` = `Flush` → `_slot.AfterPass()`。
- `Image.Sprite` → `_slot.Set(value)`。把 `OnAfterApply` 里依赖 sprite 的那一段（fit 宽高比、没写 `type` 时的 `DeriveType`、
  `WarnIfFxOnNonSimple`、`Flush`）抽成 `RefreshSpriteDerivedState()`，同时也是 `RefreshDerived`。
  `OnAfterApply` 的顺序：`RefreshSpriteDerivedState()` → `_slot.AfterPass()` → `_raycast.EndPass()`。
  `ResolveStatic`：值带 `:`、resolver 为 null 且正在加载 → `Deferred`；否则调 `UI.ResolveSpriteStatic`（今天 `ResolveSprite`
  的函数体，逐字不动），值带 `:` 却解析为 null → `Failed`；不带 `:` 的 Resources 路径总是 `Ok`（sprite 可能为 null，与今天一样静默）。
- `Icon.PeekRuntimeState` 照旧返回最后写入的字符串，运行期独占的语义不变。

### 7.7 静态 resolver 加载中（修 §1.3）

- 静态解析得到 `Deferred` 时，slot 进入 `WaitingStaticResolver`。
- 只有 apply **之外**（由代码）写入的值才登记到静态等待表。XML 写入的值交给 End 之后的广播重放——今天就是这样修好的，
  两边都登记会让「加载完仍缺 key」报两次错。
- 值带 `:` 时，同时登记到按集名等待表：静态加载期间如果注册了同名运行时集，这个 slot 马上改走运行时路径，不必等到 End。
- `EndSpriteResolverLoad` 计数归零时，**先**处理静态等待表，**再**发今天的广播：
  - 先快照并清空静态等待表；
  - 若 `UI.SpriteResolver` 仍为 null（加载失败或没装上）→ 到此为止，不重解析、不打日志
    （否则既有测试 `Icon_silent_when_load_in_flight` 会变红；XML 写入的值照旧由广播报错，与今天一致）；
  - 否则逐个重新解析仍在等待、代次未变的 slot。这些都是代码写入的值，失败照常报错。
- 派生状态与 native 尺寸由随后的广播补上：ReSolve 每一遍都会跑 `ApplyCommon` 和 `OnAfterApply`，即使 `name` 被锁住。
- 其余 35 个属性在加载中的行为不变。
- 只修首次安装：重绑（第二次调用 `UseAddressableSpriteSetResolver` 时旧 resolver 还在）或热重载期间代码写入的图标，
  仍保持旧图（既有行为，§13）。

## 8. 运行时 .po 目录

### 8.1 API（`UI.Locale`）

```csharp
public static void RegisterRuntimeCatalog(string name,
    Func<string, Awaitable<IEnumerable<PoEntry>>> load);            // 发出即不管，失败记日志
public static Awaitable RegisterRuntimeCatalogAsync(string name,
    Func<string, Awaitable<IEnumerable<PoEntry>>> load);            // 异常抛给 await 的一方
public static bool UnregisterRuntimeCatalog(string name);           // 未注册 → false
```

- `load(locale)` 返回这份目录在该语言下的词条，没有就返回空集合。
- `load` 必须自带超时：它只要挂住不返回，切语言就永远不翻 Variant、不发 `Changed`（§8.3）。
- 重名 → 同步抛 `InvalidOperationException`，注册不生效。`RegisterRuntimeCatalogAsync` 因此写成非 async 的包装：
  先同步校验、登记、建层，再返回加载用的 Awaitable。
- `RegisterRuntimeCatalogAsync` 加载失败时异常抛给 await 的一方，**注册保留**（层是空的），下次切语言或 Reload 时重试；
  不想要就自己 `UnregisterRuntimeCatalog`。丢弃 Async 版的返回值会吞掉异常，「发出即不管」请用同步版。
- 同步 / 异步两个版本成对出现，沿用现有惯例（`Set` / `SetAsync`、`ReloadCurrent` / `ReloadCurrentAsync`）。

### 8.2 分层的 `TranslationStore`

- **底层**：今天的那张表（`PoResolver` 或 Resources 加载的内置 .po）。公开的 `Load` / `UnloadLocale` / `UnloadAll` /
  `Lookup` 签名都不变。
- **运行时层**：每个运行时目录一层，按注册顺序排列。`Lookup` 从最后注册的那层往下查，最后才查底层。层用句柄表示：
  `AddLayer()` 返回 Layer 对象，「目录仍然有效」的守卫就是比较引用；`LoadLayer` 对已移除的层什么都不做。
  没有层时，`Lookup` 的路径和今天完全一样。
- **优先级**：运行时目录覆盖内置词条，后注册的覆盖先注册的。官方热修文案正好需要覆盖；但这也意味着 UGC 目录能改写
  内置界面文案——不希望的话在 `load` 里过滤，比如只放行自家的 `msgctxt`。
- **卸载**：`UnloadLocale(locale)` 同时清掉底层和各层里该语言的词条；`UnloadAll()` 清底层和各层的条目、**保留层**
  （测试会直接调 `TranslationStore.Instance.UnloadAll()`，层的身份和顺序属于目录注册表）；注销一份目录 = 整层移除；
  `ClearLayers()` 只由 §6.7 的清空入口调用。

### 8.3 生命周期

| 时机 | 行为 |
|---|---|
| 注册，且 `Current != null` | 只为 `Current` 调一次 `load`，结果进该层；完成后广播一次（`VariantStore.NotifyChangedInternal`，同 `ReloadCurrentAsync`） |
| 注册，且 `Current == null` | 只登记，下次切语言时一起加载 |
| 切语言（`Set` / `SetAsync`） | 内置词条加载完后，同时启动所有目录的 `load(locale)`，再逐个 await（Awaitable 只能 await 一次，同 `AwaitableHelpers.WhenAll`）。单个目录失败只记 `Debug.LogError` 并跳过，不阻断切换。全部完成后才翻 Variant、发 `Changed`（与今天的顺序一致，`UI.cs:367`） |
| `ReloadCurrent(Async)` | 内置词条和所有目录一起重载 |
| 注销 | 移除整层并广播一次；不影响内置词条和其它目录 |
| 竞态 | 每次 `load` 完成时：若 `Locale.Current` 已不是发起时的语言，或者这份目录已被注销 / 重注册，就丢弃结果（同 `LoadPoFilesAsync` 的守卫，`UI.cs:555`）。切语言进行中注册的目录，可能对同一语言加载两次，结果相同、无害 |
| 重置 | §6.7 的清空入口（`ResetForTests`、SubsystemRegistration、Play 模式进出钩子）清空注册并 `ClearLayers()`，不依赖 `ResetForTests` 内部的调用顺序 |

已知行为：代码用 `UI.Tr` 推入的文本不会随注册刷新（`ReloadCurrent` 今天也是这样）；`Locale.Changed` 只在语言真正变化时触发。

## 9. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Application/RuntimeSprite.cs`（新） | `RuntimeSprite`、`RuntimeSpriteSetOptions` |
| `Runtime/Application/RuntimeSpriteSets.cs`（新，internal） | 注册表、key 状态、等待者 / 绑定者 / 按集名等待表（带清扫）、静态等待表、去重表、同步解析分支 |
| `Runtime/Application/UI.RuntimeSprites.cs`（新，partial `UI`） | `RegisterRuntimeSpriteSet` ×2、`UnregisterRuntimeSpriteSet`、`ClearRuntimeRegistrations`、SubsystemRegistration 钩子 |
| `Runtime/Application/UI.Locale.RuntimeCatalogs.cs`（新，partial `Locale`） | §8.1 的目录 API、`LoadRuntimeCatalogsAsync` |
| `Runtime/Controls/Internal/AsyncSpriteSlot.cs`（新） | §7，含 `ISpriteSlotHost` |
| `Runtime/Application/UI.cs` | `ResolveSprite` = 运行时分支 + `ResolveSpriteStatic`（今天的函数体）；`BuildSpriteResolutionFailureMessage` 的集名单（§6.8）；`EndSpriteResolverLoad`（§7.7）；`LoadPoFilesAsync` 接目录（§8）；重置与 Play 模式钩子调清空入口 |
| `Runtime/Application/UILog.cs` | `Warn(string)` |
| `Runtime/Application/ControlAttributeApplier.cs` | `InApplyPass` 的保存 / 置位 / 恢复 |
| `Runtime/Application/SpriteResolverHelpers.cs` | `BuildLookup` 先预检（含与运行时集重名）再改状态 |
| `Runtime/Application/TranslationStore.cs` | 分层（§8.2），公开签名不变 |
| `Runtime/Controls/Control.cs` | `InApplyPass`、`SizeFromNative`（§7.5） |
| `Runtime/Controls/Internal/FxImage.cs` | `DrawNothingWhenEmpty`（§7.6） |
| `Runtime/Controls/Icon.cs` / `Image.cs` | 实现 `ISpriteSlotHost`；`Image` 拆出 `RefreshSpriteDerivedState` |

不改动的部分：

- Core 的纯 C# 子集（IR / Parser / Template / Lint）。
- XSD 和 lint：集是不是按需由 C# 注册决定，静态检查无从知道。
- `SpriteAtlasSyncer`：它只为已配置的 SpriteSet 收集引用，`ugc:` 这类集名会被忽略（`SpriteAtlasSyncer.cs:814`）。

## 10. 测试（Red first）

**测试手法**：每个 key 用一个 `AwaitableCompletionSource<RuntimeSprite>` 控制完成时机。EditMode 下 `SetResult` 会同步跑完
continuation，`LocaleSetAsyncTests.Set_rapid_consecutive_with_pending_resolver_discards_stale_load` 已经这样用了。
已完成的结果用 `AwaitableHelpers.Completed` / `Faulted`。每个测试类在 `[SetUp]` / `[TearDown]` 里调 `UI.ResetForTests()`。

按任务拆分的完整清单与执行顺序在实施计划里；这里按文件列出。

`Tests/EditMode/Application/RuntimeSpriteSetTests.cs`（注册表 + 同步入口）：

- 注册校验：`Invalid_set_name_throws`、`Null_entries_or_loader_throws`、`Empty_key_in_entries_throws`、`Null_sprite_entries_are_skipped`、
  `Entries_and_options_are_snapshotted_at_register`、`Duplicate_runtime_name_throws`、`Name_colliding_with_static_set_throws_in_both_orders`、
  `Unregister_unknown_returns_false`
- 整包集：`Eager_set_resolves_through_ResolveSprite`、`Eager_missing_key_returns_Missing_and_warns_once`、
  `Eager_set_in_sync_attribute_works`（`<Btn sprite="pack:x">`）、`Works_without_any_SpriteResolver`、`Tiled_entry_registers_render_hint`
- 按需集的 key 状态机（经 internal `Request`）：`Request_completes_synchronously_via_raw_OnCompleted`（尽早验证 ACS 上裸 `OnCompleted` 同步执行）、
  `OnDemand_set_in_sync_attribute_errors_once_even_when_cached`、`Request_invokes_provider_once_per_key`、
  `Sync_completed_request_is_ready_without_waiting`、`Pending_request_settles_on_completion`、`Provider_default_result_marks_key_missing`、
  `Provider_sync_throw_marks_missing_and_errors_once`、`Provider_async_fault_marks_missing_and_errors_once`、
  `Provider_returning_null_awaitable_marks_missing`、`Empty_key_is_missing_without_calling_provider`、
  `Reentrant_request_for_same_key_does_not_reinvoke_provider`、`Result_after_unregister_is_dropped`、
  `Result_for_old_registration_does_not_touch_reregistered_set`、`Tiled_result_registers_render_hint`
- 诊断与静态集共存：`Failure_message_lists_runtime_sets_and_does_not_say_not_loaded`、`Runtime_set_survives_UseSpriteSetResolver_rebind`、
  `Runtime_set_survives_sprite_hot_reload_rebuild`、`LoadedSpriteSetNames_lists_static_sets_only`、`Static_collision_throws_before_touching_loaded_names`
- 广播与生命周期：`Eager_register_after_open_refreshes_xml_declared_btn_sprite`、`Eager_unregister_broadcasts_once`、
  `OnDemand_register_and_unregister_do_not_broadcast`、`UnloadAll_keeps_runtime_sets`、`ResetForTests_clears_sets_and_drops_pending_results`、
  `ClearRuntimeRegistrations_clears_sets`；守卫 `#if UNITY_6000_5_OR_NEWER` 内另有 `Play_mode_entry_clears_runtime_sets`

`Tests/EditMode/Controls/ControlSizeFromNativeTests.cs`：

- `Native_keyword_sets_SizeFromNative`、`Omitted_size_in_free_positioning_sets_SizeFromNative`、`Explicit_size_clears_SizeFromNative_on_next_pass`、
  `Stretched_axis_does_not_set_SizeFromNative`、`Grid_cell_child_does_not_set_SizeFromNative`、`Stack_child_with_omitted_axis_sets_SizeFromNative`、
  `Stack_cross_fill_axis_does_not_set_SizeFromNative`、`Stack_child_with_both_axes_written_does_not_set_SizeFromNative`、
  `InApplyPass_is_true_only_inside_own_apply`

`Tests/EditMode/Controls/AsyncSpriteSlotTests.cs`（Icon / Image）：

- 核心：`Pending_shows_Loading_then_result`、`Sync_completed_provider_never_shows_Loading`、`Same_key_requested_once_across_icons`、
  `Code_written_icon_name_refreshes_on_arrival`（核心：`name` 已被运行期独占）、`Stale_arrival_after_key_change_is_ignored`、
  `Arrival_after_destroy_is_silent`、`ReSolve_while_pending_does_not_rerequest_or_flash`、`Provider_null_shows_Missing_and_warns_once`、
  `Provider_exception_shows_Missing_and_errors_once`、`Variant_override_switches_ondemand_key`、`Pending_with_null_Loading_draws_nothing`、
  `Missing_with_null_placeholder_draws_nothing`、`Reentrant_set_from_provider_leaves_slot_consistent`、`Icon_static_value_reresolves_on_every_set`
- 注册 / 注销：`Unregister_clears_then_reregister_refreshes`、`Detached_icon_draws_nothing`、`Unknown_set_errors_then_heals_on_register`、
  `Destroyed_slots_are_pruned_from_registry_tables`、`Register_from_inside_arrival_callback_is_safe`
- Image：`Image_pending_shows_Loading_then_result`、`Late_arrival_rederives_image_state`（contain 宽高比；带 border 的 sprite 变 Sliced）、
  `Sync_hit_and_late_arrival_derive_same_image_type`、`Explicit_type_is_kept_on_late_arrival`、`Code_written_static_image_sprite_keeps_previous_type`
- 尺寸：`Native_sized_icon_on_ondemand_set_warns_once`、`Explicit_size_does_not_warn`、`Eager_set_native_size_does_not_warn`、
  `Image_cover_without_size_does_not_warn`、`Grid_cell_icon_without_size_does_not_warn`、`Code_written_ondemand_name_on_native_icon_warns`、
  `Bound_rows_warn_once_per_template_node`
- 复用与 §1.3：`Reused_row_same_key_still_receives_arrival`、`Code_written_icon_name_during_static_load_refreshes_on_End`（今天必红）、
  `Code_written_native_icon_gets_native_size_after_End`、`End_without_installed_resolver_stays_silent`、
  `Xml_declared_static_miss_after_load_logs_once`、`Waiting_slot_heals_when_runtime_set_registers_during_static_load`

`Tests/PlayMode/Controls/RuntimeSpriteSetPlayTests.cs`：`Ondemand_icon_arrives_after_real_frames`（真实帧调度下的到达）。

`Tests/EditMode/I18n/TranslationStoreTests.cs`（追加）：`Layer_entry_overrides_base`、`Later_layer_overrides_earlier_layer`、
`Removing_layer_restores_base`、`UnloadLocale_clears_base_and_layers`、`UnloadAll_clears_layer_entries_but_keeps_layers`、
`Load_into_removed_layer_is_ignored`、`Empty_msgstr_in_layer_falls_through`

`Tests/EditMode/Application/RuntimePoCatalogTests.cs`：

- `Duplicate_catalog_name_throws`、`Invalid_catalog_arguments_throw`、`Unregister_unknown_catalog_returns_false`、
  `Registered_before_Set_is_loaded_on_Set`、`Registered_after_Set_loads_current_and_retranslates_open_text`、`Unregister_restores_builtin_translation`、
  `Later_catalog_overrides_earlier_and_builtin`、`RegisterAsync_propagates_loader_exception`、`RegisterAsync_failure_keeps_registration_for_next_switch`、
  `Unregister_during_pending_load_drops_result`
- `Locale_switch_loads_catalog_for_new_locale`、`Catalog_failure_does_not_block_locale_switch`、`Stale_catalog_load_after_locale_switch_is_dropped`、
  `ReloadCurrent_reloads_catalogs`、`Set_with_sync_catalogs_completes_synchronously`、`Pending_catalog_delays_variant_flip_until_loaded`、
  `ResetForTests_clears_catalogs`、`ClearRuntimeRegistrations_clears_catalogs_and_layers`

**回归**：`IconRuntimeStateTests`、`SpriteResolverLoadInFlightTests`、`ResolveSpriteTests`、`SpriteResolverTests`、`ImageFitTests`、
`ImageFx*`、`LocaleSetAsyncTests` 保持全绿；最后跑完整的 EditMode / EditorOnly / PlayMode。

## 11. SKILL / 文档更新（同一 PR，英文）

- **`scripting-promptugui-csharp/SKILL.md`**：
  - 在 *Sprite resolver* 之后新增一节 *Runtime sprite sets (downloaded / UGC packs)*，内容包括：
    - 两种形态与 API；
    - 取图函数契约表；
    - 所有权；
    - 占位与日志；
    - 哪些属性能用按需集；
    - 写定尺寸的规则；
    - 注册 / 注销 / 重注册的刷新行为；
    - 重名规则；
    - 一段取图函数示例：`UnityWebRequest` → `Texture2D` → `Sprite.Create`。KTX2 只作为建议写给游戏代码——用
      KTX for Unity，注意纹理方向和色彩空间。
  - *`sprite=` dual-syntax* 一节补上解析顺序和按需集的报错规则。
  - *Error handling* 一段补上：加载中由代码写入的 icon 名，加载完成后现在会刷新（§7.7）。
  - *Locale & i18n* 一节新增 *Runtime .po catalogs*。
  - cheatsheet；*Common mistakes* 加一行：「UGC 图标不显示 → 没写定尺寸」。
- **`authoring-promptugui-xml/SKILL.md`**：`<Icon>` 表的 `name` / `size` 两行、`<Image>` 的 `sprite` 一行各加一句；
  `reference/icons.md` 新增一节 *Runtime sprite sets*：写法不变、按需集只能用在 `<Icon name>` / `<Image sprite>`、要写定尺寸、
  占位图与缺图图、Sync 工具会忽略这些集。
- **`using-promptugui-addressables/SKILL.md`**：一行说明——运行时集不受 `UseAddressableSpriteSetResolver` 重新调用的影响；
  加载中由代码写入的 icon 名现在会刷新。
- **master spec §5.4**：加一行指向本文。

## 12. 兼容性

- 不注册运行时集时，行为与今天逐字相同：解析时先查运行时集，表是空的就直接走今天的路径。
- 唯一可见的行为变化是 §1.3 的修复：resolver 加载中由代码写入的 icon 名，加载完成后会显示出来。
- `TranslationStore` 的公开签名不变；不注册目录时，查表路径也不变。

## 13. 非目标 / 后续

- 下载、磁盘缓存、解码、运行时拼图集——由游戏侧负责。可以另外做一个 Sample（`Samples~/RuntimeSpriteSets`：
  UnityWebRequest + PNG）；KTX2 辅助方法（用 `PROMPTUGUI_HAS_KTX` 门控，类比 Addressables）等有需求再另案。
- 请求取消、重试、优先级——由取图函数自己处理。
- 单个 key 的淘汰 / `InvalidateRuntimeSprite`——用不可变 key 加整集重注册代替。
- TMP 内联 `<sprite name>`——运行时集不会进烘焙好的 TMP_SpriteAsset。
- 其余 35 个 sprite 属性支持按需集；把 slot 公开给自定义控件。v1 只在内部用，有需求时再逐个接入或公开（RSS-D12）。
- `Image.sprite` 的运行期独占。代码写入的值在 ReSolve 时会被 XML 声明值打回，这是既有行为；文档建议代码驱动的
  `<Image>` 不在 XML 里写 `sprite`，或者改用 `<Icon>`。
- 在 UI Preview 工具里注册运行时集。
- 运行时集按纹理断批的优化——图集页由资源包的构建侧负责。
- 重绑（第二次调用 `UseAddressableSpriteSetResolver` 时旧 resolver 还在）或热重载期间由代码写入的图标，保持旧图——
  §1.3 只修首次安装，这是既有行为。
- 注销后处于 Detached 的 slot，不会因为之后出现同名的**静态**集而恢复（只等同名运行时集）。

## 14. 决策表

「状态」一列：**已对齐** = 讨论中已确认；**新定** = 写 spec 时按推荐补定的。作者在 2026-10-01 说「开始写 plan」，全部按推荐执行。

| # | 决策 | 备选 | 状态 |
|---|---|---|---|
| RSS-D1 | 库只管「拿到 Sprite 之后」；查 URL / 下载 / 转码 / 缓存都在游戏的取图函数里 | 库内置 URL + KTX2（§3-A） | 已对齐 |
| RSS-D2 | 同一个注册模型有两种形态：整包（字典）和按需（取图函数）；v1 两种都做 | 只做整包（需求原样），按需另案 | 已对齐 |
| RSS-D3 | 晚到刷新靠 `<Icon name>` / `<Image sprite>` 内部的 slot，绕开 ReSolve | 全量广播（§3-B）；中心登记 + 反射回放（§3-C）；新标签（§3-D） | 已对齐 |
| RSS-D4 | 其余 sprite 属性只走同步；遇到按需集时确定性报错（即使已缓存） | 已缓存就显示（§3-E） | 已对齐 |
| RSS-D5 | 按需集的图必须写定尺寸，运行时每个控件告警一次；图到达不重新排版。不采用「用 Loading 图撑出尺寸」 | 允许 Loading 图撑尺寸；图到达后单节点重跑 `ApplyCommon`（与 `scale` 冲突） | 新定 |
| RSS-D6 | 库只持引用、从不销毁；v1 不做单 key 淘汰，注销是释放点 | 库内 LRU / 引用计数 | 已对齐 |
| RSS-D7 | 每次注册内，每个 key 最多调用一次取图函数；不重试、不取消 | 库内重试 / 离开视野时取消（§3-G） | 已对齐 |
| RSS-D8 | 缺图显示每集的 Missing 图，每 key 只 Warn 一次；取图异常 Error 一次；加载中静默，Loading 图可选 | 缺图也走 Error（会刷屏） | 已对齐 |
| RSS-D9 | 未注册的集名照旧立即报错，并在之后注册同名集时自愈 | 只报错不自愈；当成 pending（§3-F） | 新定（自愈部分） |
| RSS-D10 | 注销时 slot 清空并等待重注册；整包集注册 / 注销各广播一次，按需集不广播 | 一律广播；一律不广播 | 已对齐 |
| RSS-D11 | 运行时集与 `UI.SpriteResolver` 分开存；重名两个方向都抛错 | 合并进 resolver 委托 | 已对齐 |
| RSS-D12 | slot 在 v1 是内部类；自定义控件经 `UI.ResolveSprite` 遇到按需集时得到确定性报错 | v1 就公开 `SpriteSlot` | 新定 |
| RSS-D13 | key 是逻辑名、不带扩展名，内容变了就换 key；v1 不做 Invalidate | 加 `InvalidateRuntimeSprite(set, key)` | 新定 |
| RSS-D14 | slot 等待静态 resolver 加载完成，顺带修掉 §1.3 | 不修，另案 | 已对齐 |
| RSS-D15 | .po 用分层 `TranslationStore`：运行时目录覆盖内置、后注册覆盖先注册；单个目录失败不阻断切语言 | 扁平合并 + 注销时整表重建；内置优先 | 新定 |
| RSS-D16 | Play 模式进出时清空运行时集和目录；`UnloadAll` 不清 | 一直保留；`UnloadAll` 也清 | 新定 |

### 14.1 plan 阶段的修订（设计评审，已写回正文）

| # | 修订 | 落点 |
|---|---|---|
| P1 | 不带版本守卫的清空入口 `UI.ClearRuntimeRegistrations()` + SubsystemRegistration 钩子（dev 宿主 6000.0 没有进出 Play 钩子） | §6.7 |
| P2 | `UILog.Warn(string)` | §6.5 |
| P3 | 派生状态只在「运行时状态的赋值、且不在自己的 apply 里」时刷新；静态赋值不变 | §7.3 |
| P4 | `Control.InApplyPass`：保存旧值、置 true、`finally` 恢复 | §7.3 |
| P5 | null 占位什么都不画（`FxImage.DrawNothingWhenEmpty`），不是 uGUI 的实心块 | §5.1、§7.6 |
| P6 | End 唤醒只管代码写入的 slot；resolver 仍为 null 时静默；带 `:` 的同时进按集名等待 | §7.7 |
| P7 | 三张表清扫已销毁的 slot（`AddPruned`）；已加载静态集的集名不进按集名等待 | §7.4、§7.2 |
| P8 | 重入安全：先登记再调用、slot 代次、结算幂等、逐个 `try/catch` | §5.3、§7.1、§7.3 |
| P9 | `BuildLookup` 先预检再改状态 | §6.7 |
| P10 | `SizeFromNative` 按声明与父级类型判定（排除 Grid、交叉轴 fill）；尺寸告警按节点去重 | §7.5 |
| P11 | 静态解析三态、由宿主实现；`UI.ResolveSpriteStatic` 逐字保留今天的函数体与日志 | §7.1、§7.6 |
| P12 | 三张去重表；异常压掉同 key 的缺图 Warn；空 key 直接缺图；options 快照 | §6.5、§5.1、§5.3 |
| P13 | `TranslationStore` 层用句柄；`UnloadAll` 保留层；`ClearLayers` 只由清空入口调用 | §8.2、§8.3 |
| P14 | 新代码不用 `GetInstanceID`；新 `.cs` 的 `.meta` 一起提交 | 实施约束 |

## 15. 里程碑

- **M1** 运行时集注册表、按需集的 key 状态机、同步入口、诊断、重名检查、resolver 重装不丢、清空入口；对应 `RuntimeSpriteSetTests`。
- **M2** `Control` 管道（`InApplyPass` / `SizeFromNative`）、`AsyncSpriteSlot`、Icon / Image 接入、尺寸告警、静态 resolver 加载中的修复；
  对应 `ControlSizeFromNativeTests`、`AsyncSpriteSlotTests`（含 §1.3 的 Red 测试）、PlayMode `RuntimeSpriteSetPlayTests`。
- **M3** 运行时 .po 目录（分层 `TranslationStore`）；对应 `TranslationStoreTests`（追加）、`RuntimePoCatalogTests`。
- **M4** 文档（§11）与实施记录；跑完整的 EditMode / EditorOnly / PlayMode，`dotnet format` 干净。

## 16. 实施记录

分支 `feat/runtime-sprite-sets`，按实施计划 `docs~/superpowers/plans/2026-10-01-runtime-sprite-sets.md` 执行：
spec + plan（`f3aa603`）→ M1 运行时集注册表 + 同步入口（`1fc9709`）→ M2 slot + Icon / Image（`4218107`）→
M3 运行时 .po 目录（`e886e7a`）→ M4 文档。

测试（宿主 ssw_re_client，Unity 6000.7.0b1）：`PromptUGUI.Tests.EditMode` 4781/4781、`PromptUGUI.Tests.EditorOnly` 459/459、
`PromptUGUI.Tests.PlayMode` 259/259、`PromptUGUI.Tests.EditMode.Addressables` 29/29；`dotnet format --verify-no-changes
--severity warn` 干净。新增测试类：`RuntimeSpriteSetTests`、`ControlSizeFromNativeTests`、`AsyncSpriteSlotTests`、
`RuntimePoCatalogTests`、`TranslationStoreTests`（追加 8 条）、PlayMode `RuntimeSpriteSetPlayTests`。
§1.3 的潜在 bug 由 Red 测试证实（`Code_written_icon_name_during_static_load_refreshes_on_End` 改动前红）后修复。

### 16.1 与设计的偏差

- **宿主接口**：§7.1 写的是宿主提供 `Assign(sprite, hideIfNull)`；实现改为宿主只暴露 `SlotGraphic`，由 slot 自己写图。
  原因：普通 Image 退回 `enabled` 时需要记住「是不是 slot 关掉的」，这份状态放在 slot 里只写一次。
- **清扫的语义**：§7.4 的 `AddPruned` 是均摊的——表涨到上次存活数的两倍才扫，所以一次插入不保证立刻清掉死条目；
  测试 `Destroyed_slots_are_pruned_from_registry_tables` 按「越过阈值后只剩存活的」断言。
- **「SpriteResolver 未注册」报错**：在 `UI.ResolveSprite` 与 `<Icon>` 的这条报错末尾追加已注册的运行时集名单
  （`UI.RuntimeSetsHint()`），只用运行时集的工程看到这条时能直接对上名字。
- **测试顺序**：M2 的注册表重写一次带上了 Task 8（注册 / 注销唤醒、清扫）与 Task 10（尺寸告警）的实现，这两组测试
  写出来时已经是绿的，作为验证而非 Red。其余任务均先红后绿。

### 16.2 未做 / 另案

- §13 全部非目标。
- dev 宿主 PromptUGUIDev（6000.0）本次没有编译验证：MCP 只连着 ssw_re_client。新代码不依赖 6000.1+ API
  （集合里存对象引用，不用 `GetInstanceID` / `EntityId`），守卫 `UNITY_6000_5_OR_NEWER` 的那条测试在 6000.0 上编译不进去，
  清空逻辑由不带守卫的 `ClearRuntimeRegistrations` 测试覆盖。
