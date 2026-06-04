# `<Carousel>` 轮播卡片控件设计

**日期**: 2026-06-04
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. 新增 `Runtime/Controls/Carousel.cs`（Control 薄壳：属性 / BindItems / 运行期状态）
2. 新增 `Runtime/Controls/Internal/CarouselView.cs`（MonoBehaviour：drag 翻页 + 吸附补间 + 自动播放 + 无限循环 + resize 重排 + 驱动指示点）
3. 新增 `Runtime/Controls/Internal/CarouselDot.cs`（单个指示点：Selectable + StateTintReactor，selected 态 = 当前页；可选，也可能内联进 CarouselView）
4. `Runtime/Application/BuiltinPrimitives.cs` 注册 `Carousel`（`runtimeStateAttr:"current"`）
5. `Runtime/Application/ScreenInstantiator.cs` 把 `"Carousel"` 加进「子节点 anchor/margin/size 由父托管」的集合（`selfIsLayoutGroup` 同源）
6. 新增 `Runtime/Core/Lint/CarouselRules.cs`（直接子必须是页 / 卡片不能写 anchor/margin）
7. `Runtime/Core/Lint/IRWalker.cs` 入口 self-check 加 `Carousel` 分支
8. `authoring-promptugui-xml` SKILL.md 新增 `<Carousel>` 行 + 一节用例 + lint codes
9. `scripting-promptugui-csharp` SKILL.md 加 `BindItems` / `Current` / `GoTo` / `Next` / `Previous` / `Playing` / `OnCurrentChanged` 用法
10. 主 spec `2026-05-07-promptugui-description-language-design.md` §5（控件表）追加一行
11. XSD 生成器（随 `[UIAttr]` 手动 regenerate）

**依赖**: 无新增包。复用：`ProceduralBuilders`（共享图层）、`RuntimeStateAttr` 机制（同 Tab `isOn` / Slider `value`）、`StateColorSet` / `StateTintReactor` / `IStateSource`（同 Btn/Tab 状态着色）、`ImageTint`（同 Tab `tint`）、LitMotion（同 Animation/StateTintReactor 补间）、TabBar 的 `itemTemplate` + `BindItems` + `ResolveFactory` 模式。

---

## 1. 背景

作者现在想做「广告 banner 轮播卡」只能手糊（来自真实游戏的写法）：

```xml
<Image anchor="top-stretch" height="100" sprite="UI:Box-Primary-Frame" ...>
  <VStack .../>                              <!-- 卡片内容 -->
  <HStack anchor="bottom-center" spacing="6"><!-- 手画 4 个点 -->
    <Image width="10" height="10" color="#00D4FF"/>
    <Image width="10" height="10" color="#4A6890"/>
    <Image width="10" height="10" color="#4A6890"/>
    <Image width="10" height="10" color="#4A6890"/>
  </HStack>
  <Btn id="btnSeasonJourney" anchor="stretch" color="#00000000"/>
</Image>
```

问题：

- **只有一张卡**——没有第二张可切，更没有自动播放 / 拖动翻页（需求 #1 #2）
- **指示点写死 4 个**，硬编码当前是第 0 个（蓝色），无法跟卡片数 / 当前页联动（需求 #3）
- **没有「模板 + 动态添加」**——卡片来自运营后台，数量不定，没法从代码批量加（需求 #4）
- 指示点颜色 / 形状全靠手写 `<Image>`，没有「常态 / 当前态 / hover / pressed」状态着色这一层（需求 #5）
- resize（横竖屏切换）时整个 Screen ReSolve，手写方案没有「当前播放到第几张」这个状态概念，更谈不上保住它（需求 #6）

需要一个**容器型**控件 `<Carousel>` 统一吸收：水平翻页的卡条 + 自动播放 + 拖动手势 + 自动生成的状态化指示点；卡片用 `itemTemplate` + `BindItems` 动态填充（与 `<TabBar>` / `<ScrollList>` 同款）；当前页是「运行期独占状态」，resize 不重置。

为什么不用现成的 `<ScrollList>`（它也是 ScrollRect + Mask）？ScrollList 是「自由滚动的列表」，没有翻页吸附、没有自动播放、没有无限循环、没有「当前页」概念，更没有指示点。把这些塞进 ScrollList 会让它分裂出一个完全不同的行为模式；新写 `<Carousel>` 零回归，且能为「翻页」这个语义专门设计（见 CAR-D2）。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CAR-D1 | 控件形态 | 单一 all-in-one `<Carousel>`：视口 + 卡条 + 指示点全内建；不拆 `<Carousel>` + 独立 `<CarouselDots>` | 卡片动态 → 点数必须跟卡数自动同步，拆开会逼作者手动维护两者一致；对标 TabBar all-in-one；正好吸收 §1 的手写样板 |
| CAR-D2 | 翻页实现 | 自写 `IDragHandler` pager（卡条 + LitMotion 吸附），**不用** `ScrollRect` | 无限循环用「自管取模数学」比 ScrollRect 首尾克隆 + 瞬移 hack 干净；翻页要 snap-to-page、要禁掉自由惯性与竖向弹性；自管 drag 不重复绑定卡片数据；`IDragHandler` 是标准 uGUI，代码量小 |
| CAR-D3 | 无限循环 | `current = (current ± 1 + N) % N`；每张卡放「离 current 最近的等价槽位」，首尾无缝、零克隆节点 | 不需要复制 GameObject、不需要把数据绑两份；只需在 current 变化时重排各卡 x |
| CAR-D4 | 循环 / 到头停 | `loop="true"`（默认，用户选定）/ `loop="false"` 两端 clamp + 回弹 | 默认无限循环最贴近 web banner；clamp 留给「步骤引导」类场景 |
| CAR-D5 | 当前页是运行期状态 | 注册 `runtimeStateAttr="current"`；`PeekRuntimeState()` 返回索引字符串 | ReSolve（resize / Variant / Theme / Locale）不打回声明默认值；需求 #6 的核心机制，复用 Tab `isOn` 同款 |
| CAR-D6 | resize 重排 | `CarouselView` 监听 `OnRectTransformDimensionsChange`，重算页宽 + 把卡条**瞬移**（无动画）到 current | 逻辑索引 + 视觉位置都保住；页宽变了不重算会错位 |
| CAR-D7 | 自动播放开关 | `interval` 秒，默认 `5`（不写也自动播）；`interval="0"` / 空显式关闭；`Count ≤ 1` 不推进 | 需求 #1「默认情况下会自动切」→ 默认开 |
| CAR-D8 | 自动播放计时基准 | `Time.unscaledDeltaTime`，在 `CarouselView.Update()` 累加 | UI banner 不该随 `Time.timeScale=0`（游戏暂停）冻结；WebGL 安全（无 .NET 线程，走 Unity Update 循环） |
| CAR-D9 | 自动播放暂停 / 恢复 | 拖动按下时暂停；松手 / 任何手动跳转后**重置**计时器从 0 再数 `interval` | 用户正在看 / 操作时不打断；要永久停用 `Playing=false` 或 `interval=0` |
| CAR-D10 | 吸附判定 | 松手时按 `位移 > 页宽 × 0.2` **或** flick 速度超阈值 → 翻到相邻页；否则回弹当前页；LitMotion 补间 `transition` 秒（默认 `0.3`，OutCubic） | 翻页吸附标准做法；阈值 + 速度双判定手感好 |
| CAR-D11 | 拖动主轴锁 | `OnBeginDrag` 判断主轴；非水平方向不处理、不 eat 事件 | carousel 放进竖向 `<ScrollList>` 时竖向滑动交还外层，不互抢 |
| CAR-D12 | 卡片内可点击元素 | 卡内 `<Btn>` 等正常可点：Unity 事件系统按 drag-threshold 区分——轻点 → Btn 的 click，拖动 → viewport 的 IDragHandler | 需求场景里卡片带 `<Btn>`（`btnSeasonJourney`）；无需特殊处理 |
| CAR-D13 | 卡片来源 | 静态 XML 子 **或** `itemTemplate` + `BindItems`，两种都支持；混用时 BindItems 赢（先 Dispose 静态卡再重建） | 与 TabBar / ScrollList 心智一致；`itemTemplate` 默认 `"Frame"` |
| CAR-D14 | 卡片是被托管的页 | 作者**不能**在卡片上写 `anchor` / `margin` / `size`（CarouselView 按视口尺寸排）；Carousel 加进「子由父托管」集合 + lint | 与 TabBar 子项规则一致；手写会被覆盖，提前 lint 拦下 |
| CAR-D15 | `ChildHostTransform` | Carousel override 返回卡条 `Strip` 的 RectTransform | 静态 XML 子卡 + BindItems 建的卡都进 Strip（对标 Animation 的 `_offsetProxy`）；指示点由 carousel 自建挂 root，不走这条 |
| CAR-D16 | 指示点形态 | 自动生成一排 dot；每个 dot = Selectable，current 页对应 dot 处 Selected 态；复用 `StateColorSet` / `StateTintReactor`；点击 dot → 动画 `GoTo` | 复用现有状态着色，与 Btn/Tab 一致；点可点击（用户选定） |
| CAR-D17 | 指示点显隐 | `dots=`（锚点）空 / `none` 不生成；`Count ≤ 1` 自动隐藏整排 | 单卡无意义 |
| CAR-D18 | 指示点 selected 驱动 | 由 carousel 主动推（每页变化时给各 dot 的 state source 设 selected），**不**用 uGUI ToggleGroup | dot 选中态跟随 current，不是用户互斥点选；hover/pressed 仍由各 dot 自己的 Selectable 叠加 |
| CAR-D19 | 方向 | v1 **仅横向**；竖向 YAGNI（`direction` 留 v2） | web banner 全是横向；竖向视觉规范（卡比例 / 点排布）另说 |
| CAR-D20 | 页变化事件 | `OnCurrentChanged: Observable<int>`，任何来源（auto / drag / dot-click / code）都 fire，相同值去重 | 给业务接「当前第几张」（埋点 / 联动） |
| CAR-D21 | `Current` setter | XML / Variant 写 `current="2"` → `GoTo(2, animated:false)`；运行期 `car.Current` 是 getter | 初始页可声明；运行期改过后 ReSolve 不打回（CAR-D5） |
| CAR-D22 | BindItems 重建保 current | 重建后若 `新 Count > 旧 current` 保持 current，否则 clamp 到 `Count-1`（空列表 → -1 / 视口空） | 数据刷新不该粗暴跳回第 0 张 |
| CAR-D23 | 三段式指示点（`dotTriSlice`，v1.1 追加） | 用 `Sprite.Create` 把 `dotSprite` 切成左/中/右 3 个子 sprite 分配给各点，**不用** RectMask2D | RectMask2D 方案每点要多一层超宽子 Image + mask + 单独 raycast 图形，还要把状态着色重接到子 Image；`Sprite.Create` 让每个点仍是普通 Image，现有 `dotColor`/`dotSelectedColor`/`dotTint`/点击/reactor 全部零改动。子 sprite 缓存按源 sprite 复用、`OnDestroy` 释放，避免 ReSolve 反复分配。1 个 bool 开关，不新增 sprite 属性（选中态走颜色） |

---

## 3. XML 形态

### 3.1 动态卡片（需求 #4，主用例）

```xml
<Template name="BannerCard">
  <Image anchor="stretch" sprite="UI:Box-Primary-Frame" color="primary-lighter" tint="linear">
    <Image anchor="stretch" sprite="UI:Box-Primary-Bg" mask="self" color="primary-darker" tint="linear"/>
    <VStack anchor="stretch" margin="8,8,_,8" spacing="4" childAlign="upper-left">
      <Text id="title" fontSize="12" color="#FFFFFF" tr="false"/>
      <Text id="subtitle" fontSize="12" color="#FFB838" tr="false" scale="0.5r"/>
      <Text id="countdown" fontSize="12" color="on-primary" tr="false" scale="0.5r"/>
    </VStack>
    <Btn id="cta" anchor="stretch" color="#00000000"/>
  </Image>
</Template>

<Carousel id="banner" anchor="top-stretch" height="100"
          anchor.portrait="stretch-right" width.portrait="177" height.portrait=""
          itemTemplate="BannerCard"
          interval="5" loop="true" transition="0.3"
          dots="bottom-center" dotSize="10x10" dotSpacing="6" dotMargin="_,_,12,_"
          dotSprite="UI:dot" dotColor="#4A6890" dotSelectedColor="#00D4FF" dotTint="linear"/>
```

C# 端 `BindItems` 推数据、点数自动 = 卡数（见 §5.3）。

### 3.2 静态卡片（CAR-D13）

不需要动态数据时，直接写若干子节点，每个子节点就是一张卡：

```xml
<Carousel id="intro" anchor="stretch" interval="4" dots="bottom-center" dotSprite="UI:dot">
  <Image sprite="UI:slide1"/>
  <Image sprite="UI:slide2"/>
  <Image sprite="UI:slide3"/>
</Carousel>
```

（卡片不写 `anchor` / `size`——由 Carousel 排成视口大小，CAR-D14。）

### 3.3 关掉自动播放 / 不要指示点

```xml
<!-- 纯手动拖动、无自动播放、无指示点 -->
<Carousel id="gallery" anchor="stretch" interval="0" dots="" itemTemplate="Photo"/>
```

---

## 4. 属性表

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `itemTemplate` | tag / `<Template>` 名 | `"Frame"` | `BindItems` 实例化的卡片元素；与 ScrollList 同解析 |
| `interval` | float（秒） | `5` | 自动播放间隔；`0` / 空 = 关闭；`Count ≤ 1` 不推进（CAR-D7） |
| `loop` | bool | `true` | `true` 无限循环 / `false` 到头 clamp + 回弹（CAR-D4） |
| `transition` | float（秒） | `0.3` | 吸附 / 自动切换 / 点击跳转的补间时长（OutCubic，CAR-D10） |
| `current` | int | `0` | 初始页；运行期是「独占状态」，ReSolve 不打回（CAR-D5/D21） |
| `dots` | anchor 字符串（如 `bottom-center`） | (none) | 指示点行锚点；空 / `none` 不生成；`Count ≤ 1` 自动隐藏（CAR-D17） |
| `dotSize` | `WxH` | `8x8` | 单个点尺寸 |
| `dotSpacing` | float | `6` | 点间距 |
| `dotMargin` | `T,R,B,L`（支持 `_` 占位） | (none) | 指示点行相对锚点的边距（如 `_,_,12,_` 抬离底边 12） |
| `dotSprite` | sprite key | (none) | 点的形状（需求 #5）；走 `UI.ResolveSprite`；不写 = uGUI 默认白底方块 |
| `dotSelectedSprite` | sprite key | (none) | **可选**：当前页的点换成它（`overrideSprite`，同 Tab `selectedSprite`）；不写 = 仅靠颜色区分 |
| `dotColor` | color | uGUI 白 | 非当前态点底色（需求 #5） |
| `dotSelectedColor` | color | (none，回退 `dotColor`) | 当前态点底色（需求 #5） |
| `dotHoverColor` | color | (none) | **可选**：鼠标悬停点的色（点可点击，CAR-D16） |
| `dotPressedColor` | color | (none) | **可选**：按下点的色 |
| `dotTint` | `multiply` \| `linear` | `multiply` | 点 bg 的 tint 混合模式（需求 #5），同 Tab/Btn `tint` |
| `dotTriSlice` | bool | `false` | 把单张 `dotSprite` 横向等比切成 3 段（左帽 / 可平铺中段 / 右帽）分摊到各点，整排连成一条 `<= == == == =>`。2 点 = 左+右；N≥3 = 左 + 中×(N-2) + 右（≤1 卡指示点本就隐藏）。sprite 须设计成 3 等宽段、中段可平铺；atlas sprite 不能旋转/tight-pack（轴对齐子矩形才好切）。选中态仍走**颜色**，不需要额外的 selected 切图。**源图的 9-slice 边框按段保留**：左帽段保留 left border、右帽段保留 right border、内部切口边 = 0（可拉伸），top/bottom 全段保留——否则段会被整体拉伸。实现：`Sprite.Create`（带 border）切 3 个子 sprite（见 CAR-D23），每个点仍是普通 Image，状态着色/tint/点击不变 |

约束：

- Carousel 直接子节点（卡片）**不能**写 `anchor` / `margin` / `size`（CAR-D14）；写了 lint error。
- color 类属性取值同其它控件：hex / CSS 命名色 / theme token（见主 spec Color Tokens）。

---

## 5. C# API

### 5.1 `Carousel`

```csharp
public sealed class Carousel : Control
{
    // —— 行为 ——
    [UIAttr, Preserve] public string ItemTemplate { set; }     // 默认 "Frame"
    [UIAttr, Preserve] public float  Interval     { set; }     // autoplay 秒；默认 5；0=关
    [UIAttr, Preserve] public bool   Loop         { set; }     // 默认 true
    [UIAttr, Preserve] public float  Transition   { set; }     // 补间秒；默认 0.3
    [UIAttr, Preserve] public int    Current      { get; set; } // 运行期状态；set = GoTo(v, animated:false)

    // —— 指示点 ——
    [UIAttr, Preserve] public string Dots        { set; }       // 锚点；空/none=隐藏
    [UIAttr, Preserve] public string DotSize     { set; }       // "WxH"
    [UIAttr, Preserve] public float  DotSpacing  { set; }
    [UIAttr, Preserve] public string DotMargin   { set; }
    [UIAttr(IsSprite = true), Preserve] public string DotSprite         { set; }
    [UIAttr(IsSprite = true), Preserve] public string DotSelectedSprite { set; }
    [UIAttr(IsColor  = true), Preserve] public string DotColor          { set; }
    [UIAttr(IsColor  = true), Preserve] public string DotSelectedColor  { set; }
    [UIAttr(IsColor  = true), Preserve] public string DotHoverColor     { set; }
    [UIAttr(IsColor  = true), Preserve] public string DotPressedColor   { set; }
    [UIAttr, Preserve] public string DotTint     { set; }       // multiply | linear

    // —— 查询 / 控制 ——
    public int  Count   { get; }
    public int  Current { get; }            // 当前页索引（= 属性 getter；运行期状态）
    public bool Playing { get; set; }       // 暂停 / 恢复自动播放
    public void GoTo(int index, bool animated = true);
    public void Next(bool animated = true);
    public void Previous(bool animated = true);

    public Observable<int> OnCurrentChanged { get; }  // 任意来源页变化；相同值去重

    // —— 动态卡片（同 ScrollList / TabBar）——
    public IDisposable BindItems<T>(
        Observable<IReadOnlyList<T>> source,
        Action<IControl, T> bind);                       // slot = 卡片根（itemTemplate body）

    public IDisposable BindItems<T, TSlot>(
        Observable<IReadOnlyList<T>> source,
        Action<TSlot, T> bind) where TSlot : class, IControl;

    internal override string PeekRuntimeState();         // => Current.ToString()（CAR-D5）
}
```

### 5.2 运行期状态落地（需求 #6）

```csharp
// BuiltinPrimitives.cs
reg.Register<Carousel>("Carousel", null, runtimeStateAttr: "current");

// Carousel.cs
internal override string PeekRuntimeState() => _view.CurrentIndex.ToString();
```

`ControlAttributeApplier`（ReSolve 时）比较 `PeekRuntimeState()` 与 `_lastAppliedRuntimeState`：相等 → Variant 声明值赢（重应用 `current.portrait` 之类）；不等（运行期已翻过页）→ 跳过 `current` 的重应用。其它属性（`interval`/`loop`/`dot*` …）照常幂等重应用。**真实状态存在 `CarouselView`（MonoBehaviour，跨 ReSolve 存活），不在 Control 壳里**——所以连自动播放计时器都不丢。

### 5.3 用法示例

```csharp
var car = screen.Get<Carousel>("banner");

car.BindItems(bannerStream, (IControl card, Banner b) => {
    card.Get<Text>("title").TextValue     = b.Title;
    card.Get<Text>("subtitle").TextValue  = b.Subtitle;
    card.Get<Text>("countdown").TextValue = b.Countdown;
    card.Get<Btn>("cta").OnClick.Subscribe(_ => Game.Open(b.Link)).AddTo(screen);
}).AddTo(screen);

car.OnCurrentChanged.Subscribe(i => Analytics.Banner(i)).AddTo(screen);

// 程序控制：
car.Next();              // 下一张（带动画，loop 时从尾绕回头）
car.GoTo(0, animated:false);
car.Playing = false;     // 暂停自动播放
```

---

## 6. 程序化层级（固定）

```
Carousel (root RectTransform + CarouselView[MonoBehaviour, IBeginDrag/IDrag/IEndDragHandler])
├── Viewport (RectTransform + RectMask2D)          ← 裁掉出框卡片；填满 root；IDragHandler 实际接在它/root
│   └── Strip (RectTransform)                       ← Carousel.ChildHostTransform 指向它（CAR-D15）
│       ├── Card[0] … Card[N-1]                     ← itemTemplate 实例 / 静态 XML 子；各自 sized=视口、绝对定位
│
└── Indicator (RectTransform + HorizontalLayoutGroup)  ← 按 dots= 锚点定位；Count≤1 隐藏
    ├── Dot[0] (RectTransform + UnityImage + Button + StateTintReactor)
    ├── …                                            ← Selected 态 = 当前页（carousel 推，CAR-D18）
    └── Dot[N-1]
```

- **Viewport**：`RectMask2D`（比 `Mask` 轻，无需额外 Graphic）；anchor stretch 填满 root。
- **Strip**：无 LayoutGroup（卡片由 `CarouselView` 手动定位）；尺寸不重要（只是 Card 的 parent）。
- **Card**：每张 RT 锚点 middle-center，`sizeDelta` = 视口尺寸；x 由 `CarouselView` 按 §7.4 取模算出。
- **Indicator**：dot 数随卡数重建；整排尺寸由 `dotSize × N + dotSpacing` 决定，锚点 = `dots`，偏移 = `dotMargin`。
- **Dot**：`Button`（可点）+ `UnityImage`（bg / 形状）+ `StateTintReactor`（按状态着色）。

---

## 7. 行为细节

### 7.1 初始化序列

1. `ScreenInstantiator` 创建 `Carousel` GameObject → `OnAttached()`：建 `Viewport` + `Strip` + `CarouselView`（`AddComponent`），暂不建 Indicator。
2. `Carousel.ChildHostTransform` 已指向 `Strip` → ScreenInstantiator 把**静态 XML 子卡**建进 Strip。
3. 属性 apply（DFS post-order）：`interval` / `loop` / `transition` / `current` / `dot*` 写入 `CarouselView`（dot 样式存为 `StateColorSet` 待用）。
4. `Carousel.OnAfterApply()`：
   - 收集 Strip 下的静态卡 → `_view.SetCards(staticCards)`；
   - `_view.RebuildIndicator()`（按当前卡数建 dot）；
   - `_view.LayoutTo(current, animated:false)`（首帧定位）；
   - `_view.StartAutoplayIfNeeded()`。
5. 用户 code（晚于 OnAfterApply）调 `BindItems` → `Rebuild`（§7.7）覆盖静态卡。

### 7.2 自动播放（CAR-D7/D8/D9）

```csharp
// CarouselView.Update()
if (!_playing || _interval <= 0f || _count <= 1 || _dragging || _animating) return;
_elapsed += Time.unscaledDeltaTime;
if (_elapsed >= _interval) { _elapsed = 0f; GoTo(_current + 1, animated:true); }
```

- 拖动 (`_dragging`) 或正在补间 (`_animating`) 时不累加。
- 任何手动跳转（drag 翻页 / dot 点击 / `Next` / `GoTo`）都 `_elapsed = 0`（重新数）。
- `Playing` setter 直接翻 `_playing`；`interval=0` → 永不推进。

### 7.3 拖动翻页（CAR-D10/D11/D12）

```
OnBeginDrag:  若 |delta.x| < |delta.y| → 主轴非水平，return（不 eat，交外层；CAR-D11）
              否则 _dragging=true，取消在播的补间
OnDrag:       _stripX += eventData.delta.x（跟手）；clamp（loop=false 时两端加阻尼）
OnEndDrag:    _dragging=false
              翻页判定：|累计位移| > 页宽×0.2  ||  |flick 速度| > 阈值
                → GoTo(current ± 1)        （方向看位移符号）
                否则 → 回弹 GoTo(current)
```

卡内 `<Btn>` 不受影响：移动未超过 EventSystem 的 drag threshold 时算 click（给 Btn），超过才进 OnBeginDrag（给 viewport）。

### 7.4 无限循环排布（CAR-D3）

不维护「固定卡条」，而是每次 `current` 变化时把每张卡放到「离 current 最近的等价槽位」：

```csharp
// 卡 i 相对 current 的最近偏移（loop 时考虑 ± N 的等价位置）
int Offset(int i, int current, int n, bool loop) {
    int raw = i - current;
    if (!loop) return raw;
    if (raw >  n / 2) raw -= n;     // 从右边绕到左边更近
    if (raw < -n / 2) raw += n;     // 从左边绕到右边更近
    return raw;
}
// 卡 i 的 x = Offset(i, current, n, loop) * pageWidth + dragOffset
```

效果：current 左右相邻槽位永远落着 `(current-1+N)%N` 和 `(current+1)%N`，首↔尾翻页无缝、无需克隆节点。`loop=false` 时 `Offset` 退化为线性 `i-current`，两端外无卡（回弹）。

### 7.5 吸附补间

`GoTo(target, animated)`：
- 规整 target：`loop` → `(target % N + N) % N`；非 loop → `Clamp(0, N-1)`。
- `animated=false`：立即 `_current=target`、重排各卡 x、刷新指示点、`OnCurrentChanged`。
- `animated=true`：`_animating=true`，LitMotion 把 `dragOffset`→0 的同时把 `_current` 视觉滑到 target（按 `transition` 秒、OutCubic）；补间结束 `_animating=false`、归一化各卡 x（避免累积误差）。
- 任意来源的 `_current` 变化（且与上次不同）→ `_onCurrentChanged.OnNext(_current)`，并 `_elapsed=0`。

### 7.6 resize 重排（需求 #6，CAR-D6）

`CarouselView` 实现 `OnRectTransformDimensionsChange()`（MonoBehaviour 回调）：

```csharp
void OnRectTransformDimensionsChange() {
    if (!isActiveAndEnabled) return;
    _pageWidth = _viewport.rect.width;          // 重算页宽
    LayoutTo(_current, animated:false);         // 用保住的 _current 瞬移重排
    RepositionIndicator();                      // 指示点行按新尺寸/锚点重定位
}
```

`_current` 是 MonoBehaviour 字段，ReSolve 不重建 GameObject → 不丢；CAR-D5 又保证 `current` 属性不被打回。逻辑 + 视觉双保险。

### 7.7 BindItems 重建（CAR-D13/D22）

```csharp
public IDisposable BindItems<T>(Observable<IReadOnlyList<T>> src, Action<IControl,T> bind)
    => BindItems<T, IControl>(src, bind);

public IDisposable BindItems<T,TSlot>(Observable<IReadOnlyList<T>> src, Action<TSlot,T> bind)
    where TSlot : class, IControl
    => src.Subscribe(items => Rebuild(items, bind));

void Rebuild<T,TSlot>(IReadOnlyList<T> items, Action<TSlot,T> bind) where TSlot : class, IControl {
    if (_factory == null) _factory = ResolveFactory(_itemTemplate ?? "Frame");  // 同 TabBar.ResolveFactory
    int prev = _view.CurrentIndex;
    ClearCards();                                          // Dispose 所有现有卡（含静态）
    foreach (var item in items) {
        var node = _factory(_view.StripRect);              // 实例化进 Strip
        bind((TSlot)node, item);                           // 失败抛 InvalidCastException（同 ScrollList）
        _view.AddCard(node);
    }
    int next = items.Count == 0 ? -1 : Mathf.Clamp(prev, 0, items.Count - 1);   // CAR-D22
    _view.RebuildIndicator();
    _view.LayoutTo(next, animated:false);
    _view.StartAutoplayIfNeeded();
}
```

### 7.8 指示点状态（CAR-D16/D18）

- `RebuildIndicator()`：清空 Indicator → 按卡数建 N 个 `Dot`，每个 dot：
  - `UnityImage`：`sprite = DotSprite`（`ResolveSprite`）、`tint` 套 `ImageTint.Apply`；
  - `Button`：`onClick` → `GoTo(thisIndex, animated:true)`；
  - `StateTintReactor`：用 `StateColorSet.Resolve(hover, pressed, selected, disabled)`（这里 selected=`DotSelectedColor`，base=`DotColor`），同 Tab。
  - 可选 `DotSelectedSprite` → selected 时 `overrideSprite` 换图（同 Tab `selectedSprite`）。
- `Count ≤ 1` → Indicator `SetActive(false)`。
- `current` 变化时：旧 dot 设 Normal、新 dot 设 Selected（carousel 主动推到各 dot 的 state source，hover/pressed 仍由各 dot Selectable 自行叠加）。

---

## 8. 边界 / 错误处理

| 场景 | 处理 |
|---|---|
| `Count == 0`（空列表 / 无卡） | 视口空；`Current = -1`；Indicator 隐藏；不自动播 |
| `Count == 1` | 不自动播、拖动回弹原位；Indicator 隐藏（CAR-D17） |
| 卡片写了 `anchor` / `margin` | lint error `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`（卡片由父托管）；runtime 被 CarouselView 重排覆盖 |
| 卡片写了 `size` / `width` / `height` | lint error `PUI-CAROUSEL-CARD-SIZE`；runtime 被 CarouselView 重排覆盖 |
| Carousel 直接子是非法节点（理论上任何元素都能当卡，无此情形） | 仅校验「不能在卡上写 anchor/margin/size」；不限制卡的 tag |
| `itemTemplate` 指向不存在的 tag / Template | `ResolveFactory` 抛 `ParseException`（同 ScrollList / TabBar） |
| `dots="xxx"` 不是合法锚点 | lint warning `PUI-CAROUSEL-DOTS-ANCHOR`；runtime 回退 `bottom-center` |
| `dotSize` 解析失败 | 回退默认 `8x8` + LogWarning |
| `interval` 负数 | 视为 `0`（关闭） |
| `BindItems` 传空列表 | `ClearCards`；`Current=-1`；`OnCurrentChanged.OnNext(-1)`；Indicator 隐藏 |
| 拖到一半 Screen 被 Close | `CarouselView` 随 GameObject Destroy；LitMotion handle `TryCancel`（同 Animation）；R3 订阅 `AddTo(screen)` 释放 |
| carousel 嵌在竖向 ScrollList 里、用户竖滑 | `OnBeginDrag` 主轴非水平 → 不处理，事件冒泡给外层 ScrollRect（CAR-D11） |
| resize 时正在补间 | `OnRectTransformDimensionsChange` 取消补间，瞬移到 `_current`（CAR-D6） |
| 卡内 `<Btn>` 点击 vs 翻页拖动 | EventSystem drag-threshold 区分；无需特殊代码（CAR-D12） |

---

## 9. Lint 规则

`Runtime/Core/Lint/CarouselRules.cs`（新文件）；`IRWalker.WalkNode` 入口 self-check 加 `Carousel` 分支；`ScreenInstantiator` 同源 `Debug.LogWarning`。

| Code | 触发条件 | 信息（节选） | 级别 |
|---|---|---|---|
| `PUI-CAROUSEL-CARD-SIZE` | Carousel 直接子（卡片）写了 `size` / `width` / `height` | "Carousel 卡片由控件排成视口大小，不能写 size/width/height；删除即可。" | error |
| `PUI-CAROUSEL-DOTS-ANCHOR` | `dots=` 值非空且不是合法锚点关键字 | "Carousel.dots 需要锚点关键字（如 bottom-center）或留空；运行期回退 bottom-center。" | warning |

> **职责切分（不双报）**：Carousel 加进 `ScreenInstantiator` 的「子由父托管」集合（与 `selfIsLayoutGroup` 同源），让现有 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` 负责卡片上的 `anchor` / `margin`；`PUI-CAROUSEL-CARD-SIZE` **只**管 `size` / `width` / `height`（layout-group 规则不覆盖这三个）。两条规则的属性集合互不相交，同一属性只会被报一次。

---

## 10. 实现要点

### 10.1 `Runtime/Controls/Carousel.cs`（薄壳）

```csharp
public sealed class Carousel : Control
{
    private CarouselView _view;
    private Func<RectTransform, IControl> _factory;
    private StateColorSet _dotColors;     // hover/pressed/selected + base
    // dot sprite / tint / size / spacing / margin / dots-anchor 暂存字段

    public override void OnAttached()
    {
        var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
        // anchor stretch + RectMask2D
        var strip = ProceduralBuilders.AddChild(viewport, "Strip");
        _view = GameObject.AddComponent<CarouselView>();
        _view.Init(RectTransform, viewport, strip);
        _view.OnCurrent = i => _onCurrentChanged.OnNext(i);
    }

    protected internal override Transform ChildHostTransform => _view.StripRect;   // CAR-D15

    internal override void OnAfterApply()
    {
        _view.SetStaticCards();           // 收集 Strip 下已建的静态卡
        _view.ConfigureDots(_dotColors, /* sprite, tint, size, spacing, margin, anchor */);
        _view.RebuildIndicator();
        _view.LayoutTo(_view.CurrentIndex, animated: false);
        _view.StartAutoplayIfNeeded();
    }

    internal override string PeekRuntimeState() => _view.CurrentIndex.ToString();

    // [UIAttr] 属性 setter 都转写到 _view（或暂存字段，OnAfterApply 时下发）
    // BindItems<T> / BindItems<T,TSlot> / Count / Current / Playing / GoTo / Next / Previous / OnCurrentChanged
}
```

### 10.2 `Runtime/Controls/Internal/CarouselView.cs`（MonoBehaviour，核心）

```csharp
internal sealed class CarouselView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // 字段：_viewport / _strip / _indicator / _cards / _dots
    //       _current / _count / _interval / _loop / _transition / _playing
    //       _elapsed / _dragging / _animating / _stripDragX / _pageWidth
    //       _dotColors / _dotSprite / _dotTint / _dotSize / _dotSpacing / _dotMargin / _dotsAnchor
    public RectTransform StripRect => _strip;
    public int CurrentIndex => _current;
    public Action<int> OnCurrent;

    public void GoTo(int index, bool animated) { /* §7.5 */ }
    public void Next(bool animated)     => GoTo(_current + 1, animated);
    public void Previous(bool animated) => GoTo(_current - 1, animated);

    void Update() { /* §7.2 自动播放 */ }
    void OnRectTransformDimensionsChange() { /* §7.6 resize 重排 */ }
    void IBeginDragHandler.OnBeginDrag(PointerEventData e) { /* §7.3 */ }
    void IDragHandler.OnDrag(PointerEventData e)           { /* §7.3 */ }
    void IEndDragHandler.OnEndDrag(PointerEventData e)     { /* §7.3 */ }

    void LayoutTo(int current, bool animated) { /* §7.4 排布 + 可选补间 */ }
    public void RebuildIndicator() { /* §7.8 */ }
    public void StartAutoplayIfNeeded() { _elapsed = 0f; }
    // AddCard / ClearCards / SetStaticCards / ConfigureDots
}
```

> LitMotion 补间用 `LMotion.Create(...).Bind(...)`（同 `StateTintReactor.cs:133`）；持 `MotionHandle`，新拖动 / resize / Close 时 `TryCancel`。

### 10.3 `Runtime/Controls/Internal/CarouselDot.cs`（可选拆分）

单个 dot 的 state source + StateTintReactor 安装。若逻辑简单可内联进 `CarouselView.RebuildIndicator`；plan 阶段定。每个 dot 需要一个 carousel 驱动的 `IStateSource`（selected = 是否当前页），其余复用 `StateTintInstaller.Install` / `StateColorSet`。

### 10.4 `Runtime/Application/BuiltinPrimitives.cs`

```csharp
reg.Register<Carousel>("Carousel", null, runtimeStateAttr: "current");
```

### 10.5 `Runtime/Application/ScreenInstantiator.cs`

```csharp
var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar" or "Carousel";
```

self-check 入口追加 `Carousel` 分支调用 `CarouselRules`。

### 10.6 Lint 文件

`Runtime/Core/Lint/CarouselRules.cs`（static class，`CheckCarousel(ElementNode)` 返回 `IEnumerable<LintIssue>`，同 `TabRules` / `MaskAttributeRules` 模式）；`IRWalker.cs` 入口加分支。

---

## 11. 跟现有 spec / SKILL 的整合点

### 11.1 主 spec `2026-05-07-promptugui-description-language-design.md`

§5（控件表）追加一行：

> `<Carousel>` | 水平翻页轮播卡容器；自动播放 + 拖动 + 无限循环 + 状态化指示点；itemTemplate + BindItems 动态卡片；当前页是运行期状态（resize 不重置）| RectTransform + RectMask2D + 自管卡条（详见 [`2026-06-04-carousel-design.md`](2026-06-04-carousel-design.md)）

### 11.2 `authoring-promptugui-xml/SKILL.md`

1. Built-in primitives 表追加 `<Carousel>` 行（attrs 列见 §4）。
2. 新增 "Carousel" 小节：动态用例（§3.1）、静态用例（§3.2）、关闭项（§3.3）、lint codes（§9）、「卡片不能写 anchor/margin/size」「当前页是运行期独占状态（同 Tab isOn），resize 不重置」两句话。
3. Quick reference 末尾加一行。

### 11.3 `scripting-promptugui-csharp/SKILL.md`

- `BindItems` 段补：Carousel 同 ScrollList 风格的两个重载（默认 `Action<IControl,T>`）。
- 新增 `car.Current` / `GoTo` / `Next` / `Previous` / `Playing` / `OnCurrentChanged` 速查。
- 一句：当前页是运行期状态，resize / Variant / Theme 切换不重置（同 Tab `isOn` / Slider `value`）。

### 11.4 addressables skill 无关，不动。

### 11.5 XSD

随新 `[UIAttr]` 手动 `Tools → PromptUGUI → Schema → Generate XSD`；XSD 生成器测试加 `<Carousel>` 的 substring 断言（同既有约定）。

---

## 12. Out of Scope

- **竖向轮播 / `direction`**——CAR-D19 留 v2；web banner 全横向。
- **「peek」露边**（同时露出相邻卡的一小条）——v1 全幅翻页；以后可加 `peek` / `padding` 属性。
- **cross-fade / 缩放等转场**——v1 只有 slide；要 fade 后续加 `transition-style`。
- **per-card 不同尺寸 / 自适应高度**——所有卡 = 视口尺寸。
- **指示点用数字 / 缩略图 / 进度条形态**——v1 只有点（sprite + 状态色）；复杂指示器自己用 `OnCurrentChanged` 画。
- **键盘 / 手柄导航**——v1 只有指针拖动 + dot 点击；要方向键自己接 `Next`/`Previous`。
- **嵌套 Carousel**——不阻拦，但拖动主轴锁（CAR-D11）只区分水平/垂直，同向嵌套需作者自理。
- **自动播放方向反转 / 来回弹（ping-pong）**——v1 单向递增（loop 绕回，非 loop 停尾）。

---

## 13. 风险与回滚

| 风险 | 缓解 |
|---|---|
| `OnRectTransformDimensionsChange` 在 `OnAfterApply` 之前/布局未稳时触发 → 页宽为 0 | guard `isActiveAndEnabled` + `_pageWidth<=0` 时跳过；首帧 layout 由 OnAfterApply 兜底 |
| `_current` 存在 MonoBehaviour 而 `Control` 壳被 ReSolve 重新 apply 属性 | 真实状态只在 `CarouselView`；`current` setter 经 lock-by-runtime 拦截（CAR-D5）；其它属性幂等 |
| 自动播放 `Update` 在 Editor EditMode 不跑（MonoBehaviour.Update 仅 PlayMode）| EditMode 测 `GoTo`/`Next`/loop 数学 + 状态保持（直接调方法）；自动播放的时间推进放 PlayMode 测 |
| 卡内 `<Btn>` 与拖动抢事件 | 依赖 EventSystem drag-threshold（CAR-D12）；PlayMode 测轻点触发 Btn、拖动触发翻页 |
| LitMotion 补间未结束就被新拖动/ resize 打断导致卡条错位 | 每次 GoTo/拖动开始/resize 先 `TryCancel` 旧 handle；补间结束统一归一化各卡 x（§7.5） |
| `RectMask2D` 与卡内 `mask="self"`（用户例子里有）叠加 | RectMask2D 是矩形裁剪，与 Image `mask`（sprite alpha mask）正交，不冲突；plan 加一条 PlayMode 烟雾测 |
| 无限循环排布在 N=2 时左右相邻都指向同一张卡 | `Offset` 在 N=2 时 `+1` 与 `-1` 等价（都是另一张）——视觉正确（两张来回）；加单测覆盖 N=2 |
| 加进 `selfIsLayoutGroup` 引入意外副作用（如 ContentSizeFitter） | plan 阶段确认该 flag 仅 gate 子 anchor/margin 校验与应用，不挂 LayoutGroup 组件；必要时改用独立 flag |
| `dots=` 锚点解析复用现有 AnchorResolver 还是自写关键字表 | plan 阶段定：优先复用 `AnchorResolver` 的关键字解析，保证与 `anchor=` 一致 |
| XSD 不自动更新 | 同所有新 `[UIAttr]`，手动 regenerate；CLAUDE.md 已说明 |
