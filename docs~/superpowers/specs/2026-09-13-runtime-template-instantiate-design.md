# `Screen.Instantiate` —— 按名在运行时实例化一棵模板子树（公开 C# API）

> 状态：**已实现**（分支 `feat/screen-instantiate`，2026-09-13 与作者对齐后跳过 plan 直接实现、分步提交；实施记录见 §13）。
> 需求来源：宿主工程 —— 星图上跟着天体走的铭牌池、飘字、标记：不是列表、按数据成组生成、频繁增删，
> 宿主自己维护位置和池，只要「给我一个模板实例」。
> 相关：
> 主 spec §7.4（模板 id 作用域）/ §9.2（句柄查询）/ §9.5（列表推送 —— `itemTemplate` 的运行时实例化就是从那里长出来的）、
> `2026-05-09-m5-common-controls-design.md` §R1（`ScreenInstantiator.InstantiateNode` 作为 ScrollList 与实例化器的共享入口的出处）、
> `2026-08-26-theme-driven-style-design.md`「追加：动态子树参与 ReSolve 的属性重放」（`_dynamicSubtrees` 从「只登记有 scale 的」拓宽为「所有活着的动态子树」+ `ReSolve(replayDynamicSubtrees)` 分流 —— 本文的实例全部沿用这套语义）、
> `2026-06-29-gamepad-keyboard-navigation-design.md`（`FocusCursor`：宿主自建定位壳、把模板实例化到壳里 —— §5.1 推荐给宿主的模式就是它）、
> `2026-08-31-hug-reveal-flip-checked-design.md` §4.3（`@id` 的词法解析：子树 → 逐层外包 scope → Screen，实例内的 `<Trigger>` 按此解析）。

## 1. 问题

「按模板名在运行时生成一份实例」这件事库里已经能做，但只有三个 `BindItems` 宿主能用：

- `ScrollList.ResolveFactory`（`Runtime/Controls/ScrollList.cs:491`）、`Carousel.ResolveFactory`（`Carousel.cs:222`）、
  `TabGroupCore.ResolveFactory`（`Internal/TabGroupCore.cs:191`）—— **三份逐字相同的实现**：
  查 `owner.Def.Templates[tag]` → `ItemTemplateGuard.EnsureInstantiable` → `UI.GetInstantiator().InstantiateNode(tpl.Body, parent, owner)`；
  查不到模板再退到注册的 Control 类（`new ElementNode(tag)`）。
- 入口全是 `internal`：`UI.GetInstantiator()`、`Screen.RegisterDynamicSubtree`、`ScreenDef.Templates` 的运行时查询、`ItemTemplateGuard`。
  宿主工程拿不到，只能把铭牌硬塞进一个不滚动的 `<ScrollList>` 再用 `BindItems` 全量重建 —— 位置由宿主管、又频繁增删的东西，
  这条路每次变动都是销毁重建整组。

而 `InstantiateNode` + `RegisterDynamicSubtree` 已经具备需求要的全部语义：id 进实例自己的局部 scope（`root.Get<T>("id")`），
不污染 `Screen._byId`；登记为动态子树后 Variant / Theme / locale 的 ReSolve 重放、`scale="Nx"` / `<r>r` 的 `ApplyScales`、
pixel 模式的 `PixelSnap` 都覆盖它（`DynamicSubtreeReSolveTests` / `DynamicSubtreeScaleTests` 钉着）。缺的只是一个公开入口，
和把三份复制收成一份。

调研时另发现两处与需求直接相关、必须一起处理的事实（§5.1、§5.3）：ReSolve 会把实例根的几何打回声明值；
`Screen.Close` 不会释放这类实例上 `.AddTo(instance)` 的订阅。

## 2. 否决的方案

**A. 直接把 `UI.GetInstantiator()` / `ScreenInstantiator.InstantiateNode` 转成 public。否决。**
IR 类型（`ElementNode` / `TemplateDef`）会漏进公开面；调用方得自己去 `ScreenDef.Templates` 找 body、自己记得过
`ItemTemplateGuard`、自己传对 `owner` —— 三个宿主今天就是这么各写一遍的，公开出去只是把复制扩散到宿主工程。

**B. 运行时实参：`Instantiate(name, IReadOnlyDictionary<string,string> args)`。本里程碑不做。**
模板体在加载期已按 `<Param default>` 预展开（`TemplateExpander.BuildRuntimeTemplates`：`class=` 合并、嵌套模板内联、
`{{param}}` 替换），`ScreenDef.Templates` 里只留展开后的 body。要支持实参就得为每个 ScreenDef 再留一份原始 body +
完整模板表 + 样式表，每次调用跑一遍 `ExpandNode`。需求方的数据本来就走 C# setter（`root.Get<Text>("name").TextValue = …`），
实参只覆盖「作者期常量」这一层。将来要加是纯增量重载，不动本文形状。

**C. 放在控件上：`parent.Instantiate(name)` 扩展方法。不做（可作日后糖）。**
Screen 得靠 `UI.OwnerScreenOf(parent)` 沿 transform 反查；同一件事两个入口。模板归属于 Screen 的文档，入口放 Screen 是对的。

**D. 把实例挂成 parent 控件的 child（`AddChild`），随 parent 的 `Dispose` 级联。否决。**
`AddChild` 会写 `Control.Parent`，`<Trigger>` 的 `@id` 词法解析会从此把宿主控件的 scope 当作实例的外包 scope；
`Children` 是 hug 量算 / 状态 tint fan-out 等的遍历面，动态内容混进去会改变这些控件的既有行为。
`BindItems` 的行也不是宿主的 child（`Parent == null`），实例保持一致：**归调用方 + Screen 所有**，订阅释放由 §5.3 兜底。

**E. 包内提供池（`Screen.Pool(...)`）。本里程碑不做，见 §11。**

## 3. 方案总览

```csharp
public interface IScreen : IDisposable
{
    // …既有成员…

    /// <summary>
    /// 按名在 parent 下实例化一棵子树，返回根控件。名字解析与 itemTemplate= 完全一致：
    /// 先查本 Screen 文档可见的 <Template>（本文件、Import 的 ns.Name、commons），再查注册的 Control tag。
    /// 子节点用 root.Get<T>("id") 取；销毁用 root.Dispose()。
    /// </summary>
    IControl Instantiate(string template, IControl parent);        // 落到 parent 的 ChildHostTransform：与 XML 子节点同落点
    IControl Instantiate(string template, RectTransform parent);   // 逃生舱：宿主自建的定位壳（§5.1）
}
```

`Screen` 实现两者；`UI.Open` / `UI.Get` 返回的是 `Screen`，接口与实现都能调。

保证（全部沿用 `BindItems` 行的既有语义，不新造）：

| | |
|---|---|
| 单一实现 | 三个 `BindItems` 宿主的 `ResolveFactory` 与 `Instantiate` 走同一个内部解析器（§5.6） |
| Param 规则 | 模板每个 `<Param>` 必须有 `default=`（`ItemTemplateGuard`），否则 `ParseException` |
| id 作用域 | 进实例自己的 scope；不进 `Screen._byId`；`root.Get<T>("a/b")` |
| 重解算 | 登记为动态子树：Variant / Theme / locale 重放、`ApplyScales`、`PixelSnap`；resize 只重算 scale（同行） |
| 销毁 | `root.Dispose()`；`Close` 兜底释放订阅；死引用由既有 `PruneDeadDynamicSubtrees` 惰性剪除 |
| XML / XSD / lint | 不动 |

## 4. C# 面

### 4.1 签名与放置

- 两个重载都加在 `IScreen` 上（`Get<T>` 就在接口上），`Screen`（sealed、唯一实现）实现。
- 第一参数名 `template`；接受的值与 `itemTemplate=` 相同（§4.2），文档只说「和 itemTemplate 写的是同一种东西」。
- 返回 `IControl`：模板体的唯一根（或裸 Control）。

### 4.2 名字解析（RTI-D1：与 `itemTemplate=` 完全一致）

1. `Def.Templates.TryGetValue(template)` —— key 是 `TemplateKey.ToString()`：单段 `"Card"` 或带 Import 命名空间的 `"ui.Card"`；
   commons 池的模板已在加载期合并进来。命中 → `ItemTemplateGuard.EnsureInstantiable`，有缺 `default=` 的 `<Param>` → `ParseException`（§4.7）。
   实例化的 body 是加载期预展开的那一份（默认值作实参、`class=` 已合并、嵌套模板已内联）—— **与 `BindItems` 的行共用同一棵 `ElementNode`**；
   每棵实例各自一份 `Dictionary<ElementNode, Control>`，互不干扰（行就是这么做的）。
2. 未命中 → `UI.Registry.Has(template)` → `new ElementNode(template)` 裸节点（无任何属性）。`Instantiate("Text", …)` 得到一个空 Text。
3. 都不是 → `KeyNotFoundException`（§4.7）。

模板优先于同名 Control tag（与三个宿主今天的顺序相同）。

### 4.3 parent（RTI-D2）

| 重载 | 落点 | 用途 |
|---|---|---|
| `IControl parent` | `((Control)parent).ChildHostTransform` —— XML 子节点会落到的地方：`<Frame>`/`<VStack>` 是自身 RT；`<Btn>`/`<Toggle>`/`<Tab>` 是内容宿主；`<Collapsible>` 是 body；`<Animation>` 是 offset proxy | 常规：往 XML 里写好的容器里放 |
| `RectTransform parent` | 就是它 | 宿主自建的定位壳（§5.1）；自定义控件 prefab 内层 |

两者都校验 **parent 必须在本 Screen 的 `RootGameObject` 之下**（`parent.IsChildOf(root.transform)`），否则 `ArgumentException`。
理由：实例登记在**本** Screen 的 `_dynamicSubtrees` 上、scale 按本 Screen 的 canvasFactor 算；而子树里 `Toggle.group`、
嵌套 `<ScrollList itemTemplate>`、`<Trigger>` 的 Screen 级 `@id` 回退都靠 `UI.OwnerScreenOf` 沿 transform 上溯到 Screen 根 ——
挂到别处这些会**静默失效**，不如当场报错。

parent 是 LayoutGroup（`<VStack>`/`<HStack>`/`<Grid>`，或 `ChildHostTransform` 带 LayoutGroup 的宿主）时，实例根走布局子节点路径
（`LayoutElement`、`flow`），模板根上的 `anchor` / `margin` 按既有规则在运行时 `Debug.LogWarning` —— 与 `InstantiateNode` 今天对行的处理一致
（按组件判断 `parentIsLayoutGroup`）。实例追加在 parent 末位；调用方要排序自己 `root.RectTransform.SetSiblingIndex`。

### 4.4 返回值与 id 作用域

- `root.ScopedIds` = 实例内全部 `id`（`InstantiateNode` 的 `ReplaceScopedIds(scope)`）；`root.Get<T>("a/b")` 沿路径下钻。
- **不进 `screen.Get`**：`_byId` 不写；同一模板多份实例的同名 id 不冲突（主 spec §7.4）。模板根自己带 `id` 时它也出现在自己的 scope 里（无害）。
- `root.Id` = 模板根声明的 id 或 null；GameObject 名 = id 或 tag。
- `Control.Parent == null`（与行同）：实例内 `<Trigger on="…@x">` 的解析顺序是 触发器子树 → 实例内逐层外包 scope（到实例根为止）→ 本 Screen 顶层 id。

### 4.5 生命周期（RTI-D3：`Dispose`，不是 `Destroy`）

| 事件 | 行为 |
|---|---|
| `root.Dispose()` | 递归释放 `.AddTo(root)` / `.AddTo(root 的任一子控件)` 挂的订阅，再销毁 `HostGameObject`（根是 scale 包装的 `<Text>` 时销毁的是 wrapper，不留空壳）。EditMode `DestroyImmediate`，PlayMode `Destroy`（既有 `Control.Dispose`）。**这是唯一推荐的销毁方式。** |
| `Object.Destroy(root.GameObject)` | 容忍：登记表按 `Root.GameObject == null` 惰性剪除（§5.4），不报错；但 `.AddTo(instance)` 的订阅**泄漏**到 Screen 关闭 —— skill 明说别这么干。不提供 `Screen.Destroy(instance)` 之类的额外 API。 |
| `screen.Close()` / `UI.Close` | 既有：`Dispose` `_nodeMap` 里的控件；**新增**：`Dispose` `_dynamicSubtrees` 里仍活着的根（§5.3）。 |
| 根被外部销毁（场景重载，未走 Close） | 同今天：`OnRootDestroyedExternally` 只清表不 Dispose。 |
| 热重载 | Screen 整棵重建，实例随旧根销毁；调用方在自己的 `OnOpened` 入口重新 `Instantiate` —— 与「重新 `BindItems`」同一条规则。 |
| `root.Hidden = true / false` | `HostGameObject.SetActive`；跨 ReSolve 保持（§5.2）。 |

`Instantiate` 在 Screen 未 `Open` 或已 `Close` 后调用 → `InvalidOperationException`。

### 4.6 ReSolve / Variant / Theme / scale

实例经 `RegisterDynamicSubtree` 登记，与 `BindItems` 的行完全同一张表、同一条路径：

- **状态路径**（Variant 翻转、主题切换、locale 切换 → `ReSolve(replayDynamicSubtrees: true)`）：`ReSolveDynamicSubtrees` 对实例每个节点重放
  `ControlAttributeApplier.Apply(…, initial: false)` —— 颜色 token、`class=`、`attr.variant` 覆盖、`text=` 的重翻译全部到达。
- **runtime-takeover 锁照常生效**：代码 set 过的 `TextValue` / `isOn` / `value` 不被打回声明值（`DefaultTextLockedByRuntime` / `RuntimeStateLockedByRuntime`）。
- **resize 路径**（`ReSolve(replayDynamicSubtrees: false)`）：不重放属性，只 `ApplyScales`（`Nx` / `<r>r` 依赖 canvasFactor）—— 行的分流理由原样适用。
- `scale=` 声明的节点：`_dynamicScaleBaseline` 首次捕获、之后每次先还原再补偿（既有）。实例可能是 Screen 里唯一的 factor-scale 来源，
  `RegisterDynamicSubtree` 已把 `_hasFactorScale` 置上。
- pixel 模式：`AttachPixelSnaps(root.GameObject)`（既有）。
- 代价：与行同价、线性（theme spec「追加」一节实测每行 ~4.4 μs）；藏着的实例（`Hidden`）照样重放。几百个铭牌是毫秒级。

### 4.7 错误

| 情形 | 异常 | 消息（形态） |
|---|---|---|
| 名字既不是可见模板也不是注册 Control | `KeyNotFoundException` | `Instantiate("Card"): 'Card' is neither a <Template> visible to screen 'S' nor a registered Control` |
| 模板有无 `default=` 的 `<Param>` | `ParseException`（`ItemTemplateGuard`） | `Instantiate("Card"): <Template name='Card'> has required <Param> tint with no default. …give each a default=…`（正文与 itemTemplate 那条同源，只换前缀；见 §5.6） |
| `template` / `parent` 为 null 或空 | `ArgumentNullException` / `ArgumentException` | |
| parent 不在本 Screen 之下 | `ArgumentException` | `Instantiate("Card"): parent 'LabelLayer' is not inside screen 'S'` |
| Screen 未 Open / 已 Close | `InvalidOperationException` | `Instantiate("Card"): screen 'S' is not open` |

RTI-D4：未知名字用 `KeyNotFoundException` 而非 `ParseException` —— 它与 `screen.Get(id)` 同族（运行期按名查一个 Screen 内的东西，没找到）；
`ParseException` 留给 XML 写错的情形（缺 default 的 Param）。三个 `BindItems` 宿主对 `itemTemplate=` 未知名字**仍抛 `ParseException`**、消息不变 —— 那是属性值错。

## 5. 语义细节

### 5.1 几何：ReSolve 会把实例根打回声明值 —— 宿主用「定位壳」

`ReSolveDynamicSubtrees` 重放 `ApplyCommon`，实例根的 anchor / sizeDelta / anchoredPosition **全部重置为模板声明值**。宿主若直接改
`root.RectTransform.anchoredPosition` 来定位铭牌，切横竖屏 / 切主题 / 切语言那一帧会跳回模板声明位置（每帧跟随的场景看不出来；
静态标记会跳）。

**不为此新造「几何接管锁」。** 推荐（并写进 skill）的模式是库内 `FocusCursor` 已经在用的：宿主自建一个定位用的 RectTransform 壳，
把模板实例化到壳里，模板根写 `anchor="stretch"`（或 `center` + 固定 size）—— 重放只重置实例相对壳的几何，壳由宿主全权定位：

```csharp
var layer = screen.Get("labelLayer");                                  // <Frame id="labelLayer" anchor="stretch"/>
var shell = new GameObject("np", typeof(RectTransform)).GetComponent<RectTransform>();
shell.SetParent(layer.RectTransform, worldPositionStays: false);
shell.sizeDelta = new Vector2(120, 32);
var plate = screen.Instantiate("Nameplate", shell);                     // 模板根 anchor="stretch"
plate.Get<Text>("name").TextValue = body.Name;
// 每帧：shell.anchoredPosition = WorldToCanvas(body.Position);
// 销毁：plate.Dispose(); Object.Destroy(shell.gameObject);
```

不想要壳的场景（每帧都会重写位置的飘字）直接 `Instantiate("Floater", layer)` 即可 —— 重放造成的一帧跳动会被下一帧覆盖。
这就是 `RectTransform parent` 重载必须存在的原因。

### 5.2 `Hidden` 跨 ReSolve 保持（池的原语）

`ApplyCommon` 只在节点**声明了** `hidden=` 时才写 `Hidden`（`if (hidden.HasValue)`，主 spec §8.3）。所以 `root.Hidden = true` 藏起来的实例
跨 Variant / Theme 重放仍是藏的；模板根若声明了 `hidden=`，声明值在重放时赢 —— 这和任何静态节点一样，skill 里提一句「拿来做池的模板根别写 `hidden=`」。

### 5.3 `Screen.Close` 释放实例的订阅（RTI-D5）

今天 `Close` 只 `Dispose` `_nodeMap` 里的控件。`BindItems` 的行由各自宿主兜底（`ScrollList.ClearSlots` / `CarouselView.ClearCards` /
`TabGroupCore.ClearTabs` / `Markdown._renderedRoot.Dispose()`），而 `Instantiate` 的实例挂在任意父节点下、不在父的 `Children` 里 ——
GO 会随根级联销毁，但 `.AddTo(instance)` 的订阅没人释放，会一直往已销毁的控件上打（skill「幻影回调」那条）。

改法：`Close` 在 `Dispose` `_nodeMap` 之后，再对 `_dynamicSubtrees` 里 `Root.GameObject != null` 的根调 `Dispose()`。
对已被宿主释放过的行是幂等 no-op：订阅袋已置 null、`DestroyImmediate` 过的 `HostGameObject == null` 直接返回；PlayMode 下 `Destroy` 延迟，
第二次 `Destroy` 同一对象 Unity 静默忽略。`OnRootDestroyedExternally` 路径保持不 Dispose（与既有注释一致：GO 正在销毁中）。

### 5.4 死引用剪除的时机（既有语义，写清楚）

`Dispose` 不会主动从 `_dynamicSubtrees` 摘掉条目（根控件不持有 owner 引用）；`PruneDeadDynamicSubtrees` 在下一次
`RegisterDynamicSubtree` / `ReSolveDynamicSubtrees` / `ApplyScales` 时按 `Root.GameObject == null` 剪。EditMode 下 `DestroyImmediate`
即刻为 null；PlayMode 下要到本帧 `Destroy` 结算之后 —— 同帧内「Dispose 后立刻 Instantiate」会让死条目多活一轮，仅此而已。
一次登记的剪除是 O(n) 扫描，n 是活着 + 未剪的实例数，几百量级下微秒级。

测试钩子：`internal int LiveDynamicSubtreeCount`（先 prune 再计数），`InternalsVisibleTo` 已覆盖测试程序集。

### 5.5 与 Open 的时序

`Instantiate` 要在 `UI.Open` 返回之后调用（正常用法只能拿到 Open 之后的 Screen）。自定义控件在 setter / `OnAttached` 里反查 Screen 再调
`Instantiate` 属于 Open 中途、根尚未 `SetActive(true)`，TMP 量算不可靠 —— 不支持，与 `IsOpening` 期间的其它约束同类，不额外防御。

### 5.6 单一实现（RTI-D6）

一个内部解析器，四个调用方：

```csharp
// Runtime/Application/Screen.cs（或同目录新文件；plan 定）
internal static bool TryResolveTemplateFactory(
    Screen owner,            // 可为 null：与今天 owner 为 null 时只剩 Control 分支的退化行为一致
    string name,
    string context,          // "Instantiate(\"Card\")" / "<ScrollList itemTemplate='Card'>"，只进错误消息
    out Func<RectTransform, IControl> factory);
```

- 命中模板 → 过 guard（缺 default 直接抛 `ParseException`，前缀用 `context`）→ factory = `parent => UI.GetInstantiator().InstantiateNode(tpl.Body, parent, owner)`。
- 命中 Control → factory = `parent => InstantiateNode(new ElementNode(name), parent, owner)`。
- 都不是 → 返回 false；**由调用方决定抛什么**：三个宿主抛今天那条 `ParseException`（消息不变），`Instantiate` 抛 `KeyNotFoundException`。

`ScrollList` / `Carousel` / `TabGroupCore` 的 `ResolveFactory` 各缩成「调用解析器 + 未命中抛自己那条」。`ItemTemplateGuard.EnsureInstantiable`
改为接收 `context` 前缀（正文里「point itemTemplate at」改成不含属性名的说法），`ItemTemplateExpansionTests` 的子串断言（`tint` / `Row`）不受影响。

`Screen.Instantiate(template, RectTransform parent)` = 校验 → `TryResolveTemplateFactory(this, template, ctx, out f)` → `f(parent)`；
`IControl` 重载 = 取 `ChildHostTransform` 后转发。

## 6. 实现地图

| 文件 | 改动 |
|---|---|
| `Runtime/Application/Screen.cs` | `IScreen` +2 方法；`Screen.Instantiate` ×2（校验 / 转发）；`TryResolveTemplateFactory`；`Close` 释放活着的动态子树根；`LiveDynamicSubtreeCount` 测试钩子 |
| `Runtime/Controls/Internal/ItemTemplateGuard.cs` | `EnsureInstantiable(string context, TemplateDef tpl)`：前缀参数化 |
| `Runtime/Controls/ScrollList.cs` / `Carousel.cs` / `Internal/TabGroupCore.cs` | `ResolveFactory` 改为调解析器；未命中抛原 `ParseException` |
| `Runtime/Application/ScreenInstantiator.cs` | 不动（`InstantiateNode` 原样） |
| XML / XSD / lint / `.ui.xml` 样例 | 不动 |

Core 的 CLI 编译子集（`Core/IR` / `Parser` / `Template` / `Lint`）不碰 —— 全部改动在 `Application` / `Controls`。

## 7. 测试（Red 先行；`Tests/EditMode/Application/ScreenInstantiateTests.cs`，跑在 ssw_re_client）

沿用 `DynamicSubtreeReSolveTests` 的样板（`UI.ResetForTests` / `UI.LoadDocument` / `ColorOf`）。

| # | 测试 | 断言 |
|---|---|---|
| 1 | `Instantiate_Template_RootGetReachesScopedIds` | `screen.Instantiate("Row", layerRT)` 返回非 null；`root.Get<Text>("label")` 命中；`root.GameObject.transform.parent == layerRT` |
| 2 | `Instantiate_DoesNotPolluteScreenIds` | 两份实例各自 `Get("label")` 是不同控件；`screen.Get("label")` 抛 `KeyNotFoundException` |
| 3 | `Instantiate_ControlTag_GivesBareControl` | `Instantiate("Text", …)` 返回 `Text` |
| 4 | `Instantiate_UnknownName_ThrowsKeyNotFound` | 消息含名字与 Screen 名 |
| 5 | `Instantiate_RequiredParam_ThrowsParseException_NamingTheApi` | `ParseException`；消息含 `Instantiate("Row")`、`tint`、`Row` |
| 6 | `Instantiate_ParentOutsideScreen_ThrowsArgument` | 裸 `new GameObject` 的 RT、以及另一个 Screen 的控件，都抛 `ArgumentException` |
| 7 | `Instantiate_AfterClose_ThrowsInvalidOperation` | |
| 8 | `Instantiate_IControlParent_LandsOnChildHost` | parent 是 `<Btn id="b">`：`root.transform.parent == ((Control)btn).ChildHostTransform`（≠ `btn.RectTransform`） |
| 9 | `Instantiate_SetText_SurvivesReSolve` | `TextValue = "bound"` → `screen.ReSolve()` → 实例仍在、TMP 文本仍是 "bound"（需求测试第 2 条） |
| 10 | `Instantiate_FollowsVariantFlip` | `color.alt` → `UI.Variants.Set("alt", true)` → 实例内颜色变 |
| 11 | `Instantiate_HiddenSurvivesReSolve` | `root.Hidden = true` → `ReSolve()` → 仍 `Hidden` |
| 12 | `Dispose_ThenInstantiate_NoLeakedRegistration` | `root.Dispose()`（GO 为 null）→ 再 `Instantiate` 不抛 → `LiveDynamicSubtreeCount == 1`（需求测试第 3 条） |
| 13 | `Dispose_ReleasesTrackedSubscriptions` | `.AddTo(root)` 与 `.AddTo(root.Get("label"))` 的 Disposable 在 `root.Dispose()` 后已释放 |
| 14 | `Close_DisposesLiveInstances` | `.AddTo(root)` 的 Disposable 在 `screen.Close()` 后已释放（§5.3） |
| 15 | `Close_AfterHostDisposedRows_IsIdempotent` | `ScrollList.BindItems` 一行 + 一个 `Instantiate` 实例 → `Close()` 不抛、不产生 error 日志 |

回归：`ScrollListTests` / `ScrollListStaticChildrenTests` / `CarouselBindItemsTests` / `TabBarBindItemsTests` / `TabMenuBindItemsTests` /
`ItemTemplateExpansionTests` / `DynamicSubtreeReSolveTests` / `DynamicSubtreeScaleTests` / `InstantiateNodeTests` 全绿（解析器合并 + guard 前缀改动）。
PlayMode 不加新测试（`Destroy` 延迟语义是既有的，§5.4 只是写清楚）。

## 8. SKILL / 文档更新（同一 PR 内，英文）

- `.claude/skills/scripting-promptugui-csharp/SKILL.md`
  - 新小节 **"Instantiating a template from C# (`screen.Instantiate`)"**，放在 "List / option push" 之后：两个重载与落点；名字解析 = `itemTemplate=`；
    `root.Get<T>`；`root.Dispose()` vs `Destroy(GameObject)`；`Close` 兜底；`Hidden` 做池 + 十行 Rent/Return 示例；**定位壳模式**（§5.1，附上面那段代码）；
    Param 必须有 default；热重载后重新 Instantiate；ReSolve 代价与行同。
  - "Quick reference" 加 `screen.Instantiate(template, parent)` / `root.Dispose()` 两行。
  - "Common mistakes (C#)" 加两行：`Destroy(root.GameObject)` 漏订阅 → 用 `Dispose`；实例在切 Variant 时跳回原位 → 定位壳。
- `.claude/skills/authoring-promptugui-xml/SKILL.md`：`itemTemplate=` 走完整展开那段（约 L1092）加一句 —— `screen.Instantiate(...)` 走同一条规则，
  同样要求每个 `<Param>` 有 `default=`。
- 主 spec `2026-05-07-…-design.md` §9.2：加 `IControl inst = screen.Instantiate("Card", parent);` 一行 + 指向本文。
- 本文 §13 实施记录（合并前补）。

## 9. 非目标

- 运行时实参（§2 B）。
- 包内池 / 按 key 复用（§11）。
- 几何接管锁（§5.1 用定位壳）。
- `Screen.Destroy(instance)` / 计数 / 枚举实例的 API。
- `IControl.Instantiate` 扩展糖（§2 C）。
- 新 XML 属性、XSD、lint 规则。

## 10. 已定的决策（2026-09-13 与作者对齐）

| 编号 | 决策 |
|---|---|
| RTI-D1 | 第一参数接受与 `itemTemplate=` 相同的东西：模板名（含 `ns.Name`）优先，其次注册 Control tag |
| RTI-D2 | parent 两个重载：`IControl`（落 `ChildHostTransform`）+ `RectTransform`（定位壳）；都必须在本 Screen 根之下 |
| RTI-D3 | 销毁 = `root.Dispose()`；`Destroy(GameObject)` 容忍但漏订阅；不加额外销毁 API |
| RTI-D4 | 未知名字抛 `KeyNotFoundException`（与 `Get` 同族）；缺 default 的 Param 抛 `ParseException`；宿主的 `itemTemplate=` 错误不变 |
| RTI-D5 | `Screen.Close` 兜底 `Dispose` 活着的动态子树根 |
| RTI-D6 | 一个内部解析器，四个调用方；`ItemTemplateGuard` 前缀参数化 |
| RTI-D7 | 池不进本里程碑；spec 留 §11 |

## 11. 后续：池（不在本里程碑）

宿主侧的最小池，在本文三条保证（`Hidden` 跨 ReSolve 保持、`Dispose` 级联、藏着也重放）之下就是：

```csharp
sealed class PlatePool {
    readonly Stack<IControl> _free = new();
    public IControl Rent() { if (_free.TryPop(out var c)) { c.Hidden = false; return c; } return screen.Instantiate("Nameplate", layer); }
    public void Return(IControl c) { c.Hidden = true; _free.Push(c); }
}
```

包内做池（`Screen.Pool(template, parent)` → `Rent / Return / Clear`，或按 key 的 `Sync(items, key, bind)`）绕不开一个语义问题：
**复用时要不要把实例重置到声明状态**。「不重置」是 `BindItems` 的契约（bind 负责写全）但对池是暗坑（上一个租户的 `TextValue` / 颜色 / Toggle 状态留着）；
「重置」= 用 `initial: true` 重放一遍声明属性 —— 可行，但 Animation 播放态、InputField 文本、嵌套 ScrollList 的行都不在属性里，
且上一个租户 `.AddTo(instance)` 的订阅得有「只释放订阅不销毁 GO」的入口。等原语用起来之后另开 spec 决定。

按 key 复用的 `Sync` 若做，应与 `ScrollList.BindItems` 的「按 key 差量而非全量重建」一起设计 —— 那是同一件事。

## 12. 开放问题（留给 plan / 实现期）

1. 解析器放 `Screen` 的静态方法还是 `Application/` 下独立的 internal static class —— 看 `Screen.cs` 的体量决定。
2. 模板体因根 `if=` 求值为假而展开为空（`BuildRuntimeTemplates` 回退为原始 body、`BodyExpanded=false`）：今天 itemTemplate 也会撞到，行为未定义。
   本文不扩大范围；若实现期顺手，`TryResolveTemplateFactory` 里对「guard 通过后仍 `BodyExpanded == false`」的模板给一条清楚的 `ParseException` 即可，
   三个宿主一并受益。
3. `Instantiate` 的 `template` 是否 `Trim()` —— 与 `itemTemplate=` 保持一致（今天不 Trim）。

## 13. 实施记录（2026-09-13）

### 13.1 与设计的偏差

- **解析器的家**（§12.1）：独立文件 `Runtime/Application/TemplateFactoryResolver.cs`（`internal static bool TryResolve(Screen owner, string name, string context, out Func<RectTransform, IControl> factory)`），没塞进 `Screen.cs`。四个调用方：`ScrollList` / `Carousel` / `TabGroupCore` 的 `ResolveFactory` 各缩成 5 行（未命中仍抛自己那条 `ParseException`，消息逐字不变），`Screen.Instantiate` 未命中抛 `KeyNotFoundException`。
- **`Close` 的扫表不按 `GameObject == null` 过滤**（§5.3 写的是「仍活着的根」）：EditMode 下 `_nodeMap` 那一轮 `DestroyImmediate` 会顺着级联把实例的 GO 先销毁掉，此时 `Root.GameObject` 已是 null、但订阅袋还没倒 —— `Control.Dispose` 先 `DisposeSubscriptionsRecursive` 再看 GO，对每个条目无条件调一次才是对的；对已被宿主释放过的行是 no-op（袋已空、GO 为 null 直接返回；PlayMode 下重复 `Destroy` 被 Unity 忽略）。`Close_DisposesLiveInstances` 在注释掉这一行时确认为红。
- **顺手修掉的既有泄漏**：`TabGroupCore.Dispose` 不调 `ClearTabs`，`TabBar` / `TabMenu` 经 `BindItems` 建出来的 Tab 上 `.AddTo(tab)` 的订阅在 `Close` 时此前无人释放；扫表后一并覆盖。
- `ItemTemplateGuard.EnsureInstantiable(context, tpl)` 的正文从「An item template is instantiated without an invocation … point itemTemplate at a template」改为不含属性名的说法；`ItemTemplateExpansionTests` 的子串断言不受影响。

### 13.2 未做（按 §12 留给实现期的开放问题）

- §12.2 根 `if=` 为假导致 body 未展开的既有边角：未加额外报错，行为与 itemTemplate 今天一致。
- §12.3 `template` 不 `Trim()`，与 `itemTemplate=` 一致。

### 13.3 落地清单

| 提交 | 内容 |
|---|---|
| `docs(spec)` | 本文 |
| `refactor(template)` | `TemplateFactoryResolver` + `ItemTemplateGuard` 前缀参数化 + 三个宿主改调解析器；回归 78 条全绿 |
| `feat(screen)` | `IScreen.Instantiate` ×2、`Screen.Instantiate` 实现、`Close` 扫表、`LiveDynamicSubtreeCount` 测试钩子；`ScreenInstantiateTests` 15 条；EditMode 3792 / PlayMode 201 全绿；`dotnet format --verify-no-changes --severity warn` 通过 |
| `docs(skill)` | C# skill 新小节 "Instantiating a template from C#"（含定位壳与十行池）+ cheatsheet + Common mistakes 两行；XML skill `itemTemplate=` 展开段补一句；主 spec §9.2 加指针；本节 |
