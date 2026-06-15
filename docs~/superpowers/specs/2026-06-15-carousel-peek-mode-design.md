# `<Carousel fill>` 居中选择 / peek 模式设计

**日期**: 2026-06-15
**状态**: 设计阶段（待 review，未进入实施）
**承接**: [`2026-06-04-carousel-design.md`](2026-06-04-carousel-design.md)（v1 全幅翻页轮播）。本文把 v1 §12 Out of Scope 里列的三项扶正成 v2：「peek 露边」「缩放 / 淡出转场」「per-card 自有尺寸」。决策编号续 v1 的 `CAR-Dxx`（v1 到 CAR-D23，本文 CAR-D24 起）。

**一句话**: 给现有 `<Carousel>` 加一个非占满的「居中卡片选择器」排版——卡片用自身尺寸、两侧邻卡露出（peek）、焦点卡最大最亮、越往边越小越淡。**不拆控件**，复用 v1 全部机制（拖动 / 吸附 / loop / autoplay / dots / BindItems / 运行期 `current`）。

**作用域**:
1. `Runtime/Controls/Carousel.cs`：新增 4 个 `[UIAttr]`（`fill` / `spacing` / `edgeScale` / `edgeAlpha`），转写进 `CarouselView`。
2. `Runtime/Controls/Internal/CarouselView.cs`：改 `Reposition()` / `RelayoutNow()`——区分「卡尺寸」与「步距」，叠加 `localScale` / `CanvasGroup.alpha`；拖动 / 吸附的「页宽」换成「步距」。可选把排版数学抽成内部 `CarouselLayout` helper（CAR-D24）。
3. `Runtime/Core/Lint/CarouselRules.cs`：`PUI-CAROUSEL-CARD-SIZE` 改为**仅 `fill="true"`（缺省）时**触发；新增可选 warning `PUI-CAROUSEL-PEEK-NO-SIZE`。
4. `authoring-promptugui-xml` SKILL：`reference/controls-carousel.md` 加 peek 小节 + 主文档属性表补 4 行。
5. 主 spec `2026-05-07-...` §5 控件行无需改（仍是同一个 `<Carousel>`）；本文作为 carousel 的 v2 设计被 v1 spec 引用。
6. XSD 随新 `[UIAttr]` 手动 regenerate。

**依赖**: 无新增包。复用：现有 size 分轴回退（`Control.cs:287/377`）、`CanvasGroup`（uGUI 内建）、LitMotion（已在 CarouselView 用于吸附）、`CarouselRules` / `IRWalker` lint 框架。

---

## 1. 背景与动机

v1 `<Carousel>` 是**全幅翻页**：每张卡被 `CarouselView.Reposition()`（`CarouselView.cs:364`）强制设成视口尺寸 `_pageWidth × _pageHeight`，相邻卡正好相隔一个视口宽（`off * _pageWidth`），静止时只有当前卡可见、邻卡恰好滑出屏外被 `RectMask2D` 裁掉。适合广告 banner。

但「关卡 / 角色选择」这类界面是另一种形态——业界叫 **coverflow / center-mode（居中焦点）轮播**：

- 屏幕中间一张焦点卡，**两侧能瞄到相邻卡的一部分**；
- 用户左右拖动选卡，可配左右箭头按钮（v1 已有 `Next()` / `Previous()` public 方法，直接 `<Btn>` 绑即可，本文不涉及）；
- 越靠边缘的卡越小、越淡（焦点强调）。

这套排版在 v1 里**拼不出来**：卡宽被锁成视口宽（窄卡也不会 peek，只会整张滑出留空隙），且 `Reposition` 从不碰 `localScale` / alpha。两个缺口都落在 `Reposition` 这一个每帧排版咽喉里——纯 XML 组合无法触及。

### 1.1 为什么不拆 `PeekCarousel`（CAR-D24 的前情）

头脑风暴里认真评估过「抽 `CarouselBase` + 派生 `Carousel` / `PeekCarousel`」。否决，理由见 CAR-D24：**共用的 base 其实已经是 `CarouselView`**——`Carousel` Control 类只是层薄壳，全部滚动 / 吸附 / loop / autoplay / dots / BindItems / 运行期状态都在 `CarouselView` 一个内部类里。fill 与 peek **唯一**真正分叉的是 `Reposition()` 这一个方法。拆成两个公开标签 = 同一个 CarouselView 上的两个薄壳，内部该分叉的 `Reposition` 照样得分叉，分支没消除、只是换了存放处，却多出两份注册 / XSD / lint / SKILL / 测试。净亏。

---

## 2. 决策一览（续 v1）

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CAR-D24 | 控件形态 | **不拆**，单一 `<Carousel>` 参数化（`fill` 开关）；DRY 缝落在内部（必要时抽 `CarouselLayout` helper），不开第二个公开标签 | 共用 base 已是 `CarouselView`；唯一分叉是 `Reposition` 一个方法；拆标签不消分支只增公开面（§1.1）。且 fill 是 peek 的退化特例（CAR-D25），本就是一个连续谱上的同一控件 |
| CAR-D25 | 模式开关 | `fill` 布尔，默认 `true`（= v1 全幅翻页）。`false` = 卡用自身尺寸 + 邻卡 peek + 焦点强调 | 默认 true → 现有所有 `<Carousel>` 逐字不变；`fill="false"` 直白对应「不占满屏」。**fill=true 是 peek 排版的退化特例**：卡=视口、`spacing=0`、`edgeScale=edgeAlpha=1.0` 时算出来与 v1 逐字一样 |
| CAR-D26 | 卡片尺寸来源（fill=false） | 走 PromptUGUI 既有的**分轴回退**：显式 `size=` → 控件 `GetNativeSize()` → 落空用视口尺寸兜底（永不为 0）。`Reposition` 不再强制卡尺寸，改读卡 resolved 尺寸 | 与全库一致（`Control.cs:287/377`）；`<Image>`/`<Text>`/`<InputField>` 卡可不写 size 用原生（sprite/量出/160×44）；`<Frame>` 基类 native 为 `null`（`Control.cs:177`，无 override），故 Frame 卡**必须**写 `size=`，否则兜成视口（即不 peek，见 CAR-D31 的 lint 提示） |
| CAR-D27 | 卡间距 | `spacing`（px，默认 `0`）；步距 = 卡宽 + spacing。仅 fill=false 生效 | 复用 layout-group 的 `spacing` 词汇（HStack/VStack/Grid），与 Carousel 自身 `dotSpacing` 成「主角 / 点」对，不引入新词 |
| CAR-D28 | 焦点强调基准 = **选中卡**（模型 B） | 声明 `size` = 中心（焦点）卡的尺寸；两侧卡从它**缩 + 淡**下去。`edgeScale`（默认 `1.0`=不缩）、`edgeAlpha`（默认 `1.0`=不淡），按 `|off|` 距中心距离插值 | coverflow 惯例「你正看的那张 = 设计稿尺寸」最直觉；几何安全——焦点卡恒等于声明尺寸，不会因放大顶破视口或压邻卡（模型 A「中心放大」反而易超界）。两套属性默认都 `1.0`=无差别=今天 |
| CAR-D29 | 强调落地手段 | `edgeScale` → 卡根 `localScale`；`edgeAlpha` → 卡上**懒加 `CanvasGroup`** 的 `alpha`（不 Destroy，跨 ReSolve 存活） | localScale 整卡均匀缩放、不重排子节点；CanvasGroup 整子树统一淡入淡出。约定：fill=false 下 Carousel **拥有卡根 localScale**，density `scale=` 别写在卡根（写里层），见 §6.3 |
| CAR-D30 | autoplay 与 fill **不联动** | `interval` 仍默认 `5`、与 `fill` 正交；居中选择器按惯例自己写 `interval="0"` | 「模式相关的隐藏默认」在本仓库 applier / ReSolve / variant 机制里反复出过坑（属性应用顺序、默认值复位）；保持 interval 单一默认更稳。代价仅一句文档：选择器写 `interval="0"` |
| CAR-D31 | peek 无尺寸提示 | 新增**可选** lint warning `PUI-CAROUSEL-PEEK-NO-SIZE`：`fill="false"` 的卡既无 `size=` 又无原生尺寸（如裸 `<Frame>`）→ 提示会兜成视口、邻卡不 peek | CAR-D26 的兜底是「graceful 但可能意外（看着没生效）」；一条 warning 把它显式化，省一轮「为啥没 peek」的排查。runtime 不报错、照常兜底 |
| CAR-D32 | 插值曲线 | `edgeScale` / `edgeAlpha` 按 `clamp(|off|, 0, 1)` **线性** lerp：`off=0`→`1.0`，`|off|≥1`→声明值。不加缓动属性 | YAGNI；线性在 ±1 页范围内观感够用；要曲线后续再加 `edgeEase` |

---

## 3. XML 形态

### 3.1 居中卡片选择器（主新用例）

```xml
<!-- 全黑底 + 居中选卡；卡 240×320，相邻露边、越边越小越淡；手动拖动无自动播放 -->
<Frame anchor="stretch" color="black">
  <Carousel id="levelSelect" anchor="center" size="600x360"
            fill="false" spacing="24"
            edgeScale="0.8" edgeAlpha="0.45"
            interval="0" loop="true"
            itemTemplate="LevelCard"
            dots="bottom-center" dotSprite="UI:dot"
            dotColor="#555" dotSelectedColor="#fff"/>

  <!-- 左右箭头：绑 v1 已有的 Previous()/Next()，无需库改动 -->
  <Btn id="prev" anchor="center-left"  size="48x48" margin="_,_,_,16">‹</Btn>
  <Btn id="next" anchor="center-right" size="48x48" margin="_,16,_,_">›</Btn>
</Frame>
```

```xml
<Template name="LevelCard">
  <Param name="title"/>
  <Frame size="240x320">                       <!-- ← 焦点卡尺寸；fill=false 下卡片必须自定尺寸 -->
    <Image anchor="stretch" sprite="UI:card_bg"/>
    <Text id="title" anchor="bottom-stretch" height="40" align="center">{{title}}</Text>
  </Frame>
</Template>
```

- peek 露出多少 = `(carousel 宽 − 卡宽)/2 − spacing`，**由卡尺寸 vs carousel 尺寸隐式决定，没有单独的 `peek` 属性**（这正是头脑风暴里嫌 `peek`/`cardWidth` 不直观的解法：根本不需要它们）。
- 箭头按钮的 C# 接法（不在本 spec 作用域，仅示意）：`screen.Get<Btn>("prev").OnClick.Subscribe(_ => car.Previous()).AddTo(screen);`

### 3.2 与 v1 全幅模式的对照（默认不变）

```xml
<!-- 不写 fill（默认 true）= v1 全幅 banner，逐字不变 -->
<Carousel id="banner" anchor="top-stretch" height="100"
          itemTemplate="BannerCard" interval="5" dots="bottom-center"/>
```

### 3.3 动态卡片（BindItems，同 v1）

`fill="false"` 与 `itemTemplate` + `BindItems` 正交，照常用（§5）。卡片模板根写 `size=` 即可。

---

## 4. 新增属性表（并入主文档 Carousel 行）

| 属性 | 取值 | 默认 | 作用 |
|---|---|---|---|
| `fill` | bool | `true` | `true` = 卡撑满视口、一卡一页（v1）。`false` = 卡用自身尺寸、邻卡 peek、焦点强调（CAR-D25） |
| `spacing` | float（px） | `0` | 相邻卡间距；步距 = 卡宽 + spacing。**仅 `fill="false"`**（CAR-D27） |
| `edgeScale` | float | `1.0` | 边卡缩放（中心 `1.0`=声明尺寸，越远越小）。**仅 `fill="false"`**（CAR-D28） |
| `edgeAlpha` | float | `1.0` | 边卡不透明度（中心 `1.0`，越远越淡）。**仅 `fill="false"`**（CAR-D28） |

约束补充（并入既有约束块）：

- `fill="false"` 时卡片**可以**写 `size`/`width`/`height`（CAR-D26）；`fill="true"`（缺省）仍**不能**（lint error，见 §7）。
- 卡片仍**不能**写 `anchor` / `margin`（不分模式，沿用 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`）——卡片定位永远由控件托管。
- `spacing` / `edgeScale` / `edgeAlpha` 在 `fill="true"` 下无效果（被忽略）。

---

## 5. C# API 增量

`Carousel` 仅新增 4 个 `[UIAttr]` setter，转写进 `_view`：

```csharp
[UIAttr, Preserve] public bool  Fill      { set => _view.SetFill(value); }       // 默认 true
[UIAttr, Preserve] public float Spacing   { set => _view.SetSpacing(value); }    // 默认 0
[UIAttr, Preserve] public float EdgeScale { set => _view.SetEdgeScale(value); }  // 默认 1.0
[UIAttr, Preserve] public float EdgeAlpha { set => _view.SetEdgeAlpha(value); }  // 默认 1.0
```

- 既有 public 面（`BindItems` / `Current` / `GoTo` / `Next` / `Previous` / `Playing` / `OnCurrentChanged` / `Count`）**一律不变**。peek 选择器的左右按钮就接现成的 `Next()` / `Previous()`，无新方法。
- `GetNativeSize()` 仍 `200×120`（控件自身，与卡片尺寸无关）。
- 不新增运行期状态：焦点页仍是已有的 `current`（runtimeStateAttr，resize 不重置）；`edgeScale`/`edgeAlpha`/`spacing`/`fill` 都是普通幂等属性，ReSolve 正常重应用。

---

## 6. 内部架构（CarouselView）

唯一实质改动在排版。v1 的 `Reposition()`（`CarouselView.cs:364`）把「卡尺寸」与「步距」混为一个 `_pageWidth`；peek 模式要把它们拆开。

### 6.1 新增 / 拆分的字段

```
_fill       : bool   = true       // CAR-D25
_spacing    : float  = 0          // CAR-D27
_edgeScale  : float  = 1          // CAR-D28
_edgeAlpha  : float  = 1
_cardW/_cardH : float             // 卡的「槽位尺寸」（fill=true → 视口；fill=false → resolved 卡尺寸）
_stride     : float               // 相邻卡中心距（fill=true → 视口宽；fill=false → _cardW + _spacing）
```

`_pageWidth`/`_pageHeight`（v1 既有）保留作「视口尺寸」语义；新增 `_cardW/_cardH`、`_stride` 区分「卡尺寸」「步距」。

### 6.2 `RelayoutNow()` 改动（`CarouselView.cs:385`）

```
viewport 尺寸 → _pageWidth/_pageHeight（同 v1）
if (_fill) {
    _cardW = _pageWidth;  _cardH = _pageHeight;  _stride = _pageWidth;     // 退化 = v1
} else {
    (_cardW, _cardH) = MeasureCard();        // 见 §6.4：resolved 卡尺寸，回退视口
    _stride = _cardW + _spacing;
}
_scroll = _current;  Reposition();
```

### 6.3 `Reposition()` 改动（核心，`CarouselView.cs:364`）

```
for each card i:
    off = i - _scroll                                  // loop 时 Mathf.Repeat 折进 [-N/2, N/2)（同 v1）
    rt.anchor/pivot = center                           // 同 v1
    rt.sizeDelta   = (_cardW, _cardH)                  // fill=true → 视口；fill=false → 卡尺寸
    rt.anchoredPos = (off * _stride, 0)                // 步距换成 _stride（v1 是 _pageWidth）
    // —— 焦点强调 —— 总是算、总是写（自复位），不短路：
    t = clamp(|off|, 0, 1)                             // CAR-D32 线性
    s = lerp(1, _edgeScale, t);  a = lerp(1, _edgeAlpha, t)
    rt.localScale = Vector3.one * s                    // fill=true / edge*=1 时 s=1，自动复位
    ApplyAlpha(card, a)                                // a<1 才懒加 CanvasGroup；见下
```

- **localScale 总是写、不短路**：fill=true 或 `edgeScale=1` 时 `s=1`，正好把上一轮 peek 留下的缩放复位——避免 Variant 把 `fill` 从 false 切回 true 时卡片卡在缩放态（本仓库典型的 ReSolve/variant 复位坑，见 [[project_variant_control_attr_needs_base]] 同类）。代价仅一次廉价赋值。
- `ApplyAlpha(card, a)`：`a < 1` 时才 `EnsureCanvasGroup`（懒加、缓存、不 Destroy）并设 alpha；`a == 1` 时若卡上**已有** CanvasGroup 则复位 alpha=1，否则不挂。→ 纯 fill carousel 永不挂 CanvasGroup；peek→fill 切换时已有的 CanvasGroup 被复位。
- 于是 `fill="true"`（`_stride=_cardW=_pageWidth`、`edge*=1`）→ 与 v1 `Reposition` **视觉逐字等价**：localScale 复位 1、无新增 CanvasGroup。
- **localScale 归属**：fill=false 下 Carousel 写卡根 localScale，作者别在卡根再用 density `scale=`（会被覆盖）；要 density 缩放写卡的里层节点。文档 + CAR-D31 邻近处提示。

### 6.4 `MeasureCard()`（CAR-D26）

peek 模式假定**卡片等尺寸**（整数索引 + 连续 `_scroll` 的吸附模型要求步距恒定）。取代表卡 = **第 0 张**（卡等尺寸量哪张都一样，第 0 张在 BindItems 重建后始终存在）的 resolved 尺寸：

```
读第 0 张卡 rt.rect 尺寸：
  w = rect.width  > 0 ? rect.width  : _pageWidth     // 落空（裸 Frame sizeDelta=0）兜视口
  h = rect.height > 0 ? rect.height : _pageHeight
```

卡尺寸已在属性 apply 阶段（`Reposition` 之前）由既有 size 分轴回退算好（显式 / native）。混用不同尺寸卡 = 未定义（out of scope，§9），与 v1「所有卡 = 视口」的等尺寸前提一脉相承。

### 6.5 拖动 / 吸附（步距替换）

v1 的拖动把本地位移除以 `_pageWidth` 换算成 scroll 单位、并 clamp 到 ±1 页（`CarouselView.cs:466-467`），吸附阈值用 `_pageWidth * SnapThreshold`（`:476-477`）。**全部把 `_pageWidth` 换成 `_stride`**：

```
_scroll = _dragStartScroll - clamp(dxLocal, -_stride, _stride) / _stride
EndDrag: |_dragLocalX| ≥ _stride * SnapThreshold → 翻页
```

fill=true 时 `_stride == _pageWidth`，拖动行为逐字不变。其余（像素级跟手 `ScreenPointToLocalPointInRectangle`、整段转发外层、无主轴锁）一行不动。

### 6.6 可选重构（CAR-D24 的「内部 base」）

若 `Reposition` / `RelayoutNow` 两条路（fill / peek）在实现时显得臃肿，把「给定 `_scroll` / 模式 / 尺寸 → 每卡 (pos, size, scale, alpha)」的纯数学抽成内部 `CarouselLayout`（无 MonoBehaviour 依赖、可单测）。这是头脑风暴里「CarouselBase」DRY 直觉的落点——**内部 helper，不是公开标签**。plan 阶段按代码量定要不要抽。

---

## 7. Lint 改动

`Runtime/Core/Lint/CarouselRules.cs`：

1. **`PUI-CAROUSEL-CARD-SIZE` 加 fill 门控**（`CheckCard`）：仅当父 Carousel **未声明 `fill="false"`**（即 fill=true 缺省）时，才对卡片的 `size`/`width`/`height` 报 error。`fill="false"` 下卡片写尺寸合法、不报。
   - 注意 `CheckCard` 现在按「直接子」单独检查（`CarouselRules.cs:47`），拿不到父的 `fill`。实现上让父 self-check（`CheckCarousel`）把 `fill` 读出来，遍历直接子时按模式决定是否对每个子跑 `CheckCard` 的 size 分支——即把 size 检查从「子规则」上移到「父驱动子」，与现有 `IRWalker` 的父子遍历一致。plan 阶段定具体串法（IRWalker 已持父节点）。
2. **新增 `PUI-CAROUSEL-PEEK-NO-SIZE`（warning，CAR-D31）**：`fill="false"` 的直接子卡既无 `size`/`width`/`height`（含 variant 覆盖）→ warning「该卡无尺寸，将兜成视口大小、邻卡不会 peek；给卡根写 size= 或换自带原生尺寸的控件（如 Image）」。
   - 局限：lint 是纯 IR 静态分析，看不到运行期 `GetNativeSize()`。故只对「XML 里没写 size 的卡」warn；其中 `<Image src=...>` 这种其实有原生尺寸的会被误 warn。**折中**：只在卡根 tag 是「已知无原生尺寸」的容器（`Frame` / `VStack` / `HStack` / `Grid`）时才 warn，其余 tag 放过。这样既抓住主坑（裸 Frame），又不误伤 Image/Text 卡。tag 白名单与 `BuiltinTags` 同源。

更新后规则表：

| Code | 触发条件 | 级别 |
|---|---|---|
| `PUI-CAROUSEL-CARD-SIZE` | 卡片写 `size`/`width`/`height` **且父 `fill` 非 false** | error |
| `PUI-CAROUSEL-PEEK-NO-SIZE` | `fill="false"` 卡根是无原生尺寸容器（Frame/VStack/HStack/Grid）且未写尺寸 | warning |
| `PUI-CAROUSEL-DOTS-ANCHOR`（v1） | `dots=` 非法锚点 | warning |

`anchor`/`margin` 仍由 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` 覆盖（Carousel 在 `selfIsLayoutGroup` 名单，不分模式）。

---

## 8. 向后兼容

- `fill` 默认 `true`、`spacing`/`edgeScale`/`edgeAlpha` 默认全是「无效果」值 → **现有所有 `<Carousel>` 行为逐字不变**。
- `Reposition`/`RelayoutNow`/拖动在 fill=true 分支与 v1 逐字等价（§6.3/6.5），有回归测试钉死。
- v1 文档示例（卡根 `<Frame>` 撑满、`<Image sprite>` 卡）继续工作：fill=true 仍强制视口尺寸，CARD-SIZE lint 仍对它们生效。
- lint：现有写了 `size` 的卡（若有）在 fill=true 下仍报 error，行为不变；只有显式 `fill="false"` 才放开。

---

## 9. Out of Scope（v2 仍不做）

- **不等尺寸卡片**：peek 假定卡等尺寸（步距恒定）。混用不同 size 的卡 = 未定义（§6.4）。
- **3D / 旋转 coverflow**（卡片透视倾斜）：v2 只有平面缩放 + 淡出。
- **缓动曲线属性**（`edgeEase`）：v2 线性（CAR-D32）。
- **多于相邻一张的可见层数控制**：可见张数由卡宽 vs carousel 宽隐式决定，无显式「slidesPerView」。
- **固定屏边渐变遮罩**（不跟卡、纯 vignette）：这个本来就能用两个渐变 `<Image>` 盖在 Carousel 上拼出来（头脑风暴里确认过），不进控件。
- **竖向 peek**：沿用 v1 CAR-D19，横向 only。
- **左右箭头按钮内建**：v1 `Next()`/`Previous()` 已够，作者自己放 `<Btn>` 绑（§3.1 示意），不进控件。

---

## 10. 测试计划

EditMode（`Tests/EditMode/Controls/Carousel*Tests.cs`，复用既有夹具）:

- **fill=true 回归**：`Reposition` 后各卡 pos/size 与 v1 逐字相同；localScale 恒 1、无 CanvasGroup。
- **peek 排版数学**：fill=false 下卡 sizeDelta=声明尺寸、`anchoredPos = off*(cardW+spacing)`、焦点卡 scale=1/alpha=1、`|off|=1` 卡 scale=edgeScale/alpha=edgeAlpha；中间值线性。
- **MeasureCard 兜底**：fill=false + 裸 Frame 无 size → 兜视口尺寸（不为 0、不 NaN）。
- **拖动 / 吸附用步距**：fill=false 下拖动换算与阈值用 `_stride`；拖 >0.2 步距翻页、不足回弹。
- **属性正交**：`spacing`/`edgeScale`/`edgeAlpha` 在 fill=true 下被忽略（无 CanvasGroup、scale 不变）。
- **ReSolve 幂等**：resize / Variant 重应用后 peek 视觉不变、`current` 不重置、CanvasGroup 不重复挂。
- **fill 经 Variant 切换复位**：`fill.portrait` 把 false→true 后卡 `localScale` 复位 1、已有 CanvasGroup alpha 复位 1（不卡在缩放 / 半透明态）。
- **lint**：CARD-SIZE 在 fill=true 报 / fill=false 不报；PEEK-NO-SIZE 对裸 Frame warn、对 Image 卡不 warn。

PlayMode（`Tests/PlayMode/Controls/CarouselPlayTests.cs`）:

- fill=false 下拖动翻页落在正确焦点；autoplay（若开）+ peek 不崩；CanvasGroup alpha 随焦点平滑变化（采样两帧）。

---

## 11. SKILL / docs 整合

### 11.1 `authoring-promptugui-xml/reference/controls-carousel.md`
- 加「居中选择 / peek 模式」小节：§3.1 用例、`fill`/`spacing`/`edgeScale`/`edgeAlpha` 语义、peek 量隐式（卡宽 vs 控件宽）、「fill=false 卡片须自定尺寸，裸 Frame 写 size、Image/Text 可用原生」、「Carousel 占卡根 localScale，density scale 写里层」。
- Lint 表加 `PUI-CAROUSEL-PEEK-NO-SIZE`，并标注 `PUI-CAROUSEL-CARD-SIZE` 现仅 fill=true。

### 11.2 主文档 `SKILL.md`
- Built-in primitives 表 `<Carousel>` 行补 `fill` / `spacing` / `edgeScale` / `edgeAlpha` 四属性（stub 指向 reference）。

### 11.3 `scripting-promptugui-csharp/SKILL.md`
- 无新 C# 方法（左右按钮用现成 `Next()`/`Previous()`）；可加一句「居中选择器的左右箭头 = `<Btn>` 绑 `Previous()`/`Next()`」。

### 11.4 XSD
- 随 4 个新 `[UIAttr]` 手动 regenerate；生成器测试加 `fill`/`edgeScale` 等 substring 断言（同既有约定）。

---

## 12. 风险与回滚

| 风险 | 缓解 |
|---|---|
| fill=true 分支被改出回归 | §6 全程把 fill=true 设计成 v1 逐字特例 + §10 回归测试钉死；`Reposition` 里 `if(!_fill)` 短路强调逻辑 |
| `MeasureCard` 在属性 apply 之前跑 → 卡尺寸还没 resolved | `RelayoutNow` 由 `OnAfterApply`（apply 之后）与 resize 触发，与 v1 时序一致；落空再兜视口 |
| CanvasGroup 与卡内已有 CanvasGroup / Add 块 SetActive 冲突 | `EnsureCanvasGroup` 复用已有组件（GetComponent 优先）；alpha 是乘性视觉、不碰 active |
| `fill` 经 Variant/ReSolve 从 false 切 true，卡片卡在 peek 的 scale/alpha | `Reposition` **总写** localScale（lerp 自复位）；已有 CanvasGroup 即复位 alpha=1（§6.3） |
| localScale 与卡内 density `scale=` 打架 | 约定 + 文档：fill=false 下 Carousel 拥有卡根 localScale，density 写里层（CAR-D29/§6.3） |
| 不等尺寸卡导致吸附错位 | 文档声明等尺寸前提（§6.4/§9）；`MeasureCard` 取代表卡，混用未定义但不崩 |
| PEEK-NO-SIZE 误伤有原生尺寸的卡 | 只对已知无原生尺寸容器 tag（Frame/VStack/HStack/Grid）warn（§7） |
| CARD-SIZE 门控要拿父 `fill`，但 `CheckCard` 现按子单独跑 | 把 size 检查上移到父驱动（IRWalker 已持父节点）；plan 阶段定串法 |
| XSD 不自动更新 | 同所有新 `[UIAttr]`，手动 regenerate（CLAUDE.md 已说明） |
