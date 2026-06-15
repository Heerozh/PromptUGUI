# Carousel（轮播卡）

> Part of the **authoring-promptugui-xml** skill. Main reference: [`../SKILL.md`](../SKILL.md). The `<Carousel>` attribute table lives in the main doc's built-in primitives catalog; read this for the dynamic/static-card patterns, dots config, card layout constraints, and lint rules.

`<Carousel>` 是水平翻页轮播卡容器：**自动播放 + 拖动吸附 + 无限循环 + 状态化指示点**。卡片用 `itemTemplate` + C# `BindItems` 动态填充（同 `<ScrollList>`），也接受静态 XML 子卡。当前页 `current` 是**运行期独占状态**（同 Tab `isOn`）—— resize / Variant / Theme 切换不重置。

## 动态卡（BindItems，§3.1）

```xml
<Carousel id="banner" anchor="top-stretch" height="200"
          itemTemplate="BannerCard"
          interval="5" loop="true" transition="0.3"
          dots="bottom-center" dotSize="8x8" dotSpacing="6"
          dotSprite="ui:dot" dotSelectedSprite="ui:dot_on"
          dotColor="#888888" dotSelectedColor="#ffffff"/>
```

```xml
<Template name="BannerCard">
  <Param name="title"/>
  <Frame>
    <Image anchor="stretch" sprite="ui:banner_bg"/>
    <Text id="title" anchor="bottom-stretch" height="32"
          align="center">{{title}}</Text>
    <Btn id="cta" anchor="bottom-right" size="80x28" margin="0,8,8,0">详情</Btn>
  </Frame>
</Template>
```

C#-side binding:

```csharp
screen.Get<Carousel>("banner")
      .BindItems(banners, (IControl card, Banner b) => {
          card.Get<Text>("title").TextValue = b.Title;
          card.Get<Btn>("cta").OnClick.Subscribe(_ => Open(b.Link)).AddTo(screen);
      }).AddTo(screen);
```

## 静态卡（§3.2）

```xml
<Carousel id="intro" anchor="center" size="360x200"
          loop="false" interval="0"
          dots="bottom-center" dotColor="#666" dotSelectedColor="#fff">
  <Frame>
    <Image anchor="stretch" sprite="ui:slide1"/>
  </Frame>
  <Frame>
    <Image anchor="stretch" sprite="ui:slide2"/>
  </Frame>
  <Frame>
    <Image anchor="stretch" sprite="ui:slide3"/>
  </Frame>
</Carousel>
```

## 禁用自动播放 / 隐藏指示点（§3.3）

```xml
<!-- interval="0" → 无自动播放；dots 省略（或 dots="none"）→ 无指示点 -->
<Carousel id="gallery" anchor="stretch" interval="0"/>
```

## 卡片布局约束

每张卡的 `RectTransform` 由控件内部排版——卡片不能写 `anchor` / `margin`（lint `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`）。默认 `fill="true"`（全幅翻页）下卡片也不能写 `size`/`width`/`height`（lint **`PUI-CAROUSEL-CARD-SIZE`**，error）——控件把每张卡撑满视口；`fill="false"`（peek，见下）反过来**要求**卡片自带尺寸。

```xml
<!-- WRONG — 卡片自己写了 anchor，会触发 PUI-LAYOUT-ANCHOR -->
<Carousel id="x">
  <Frame anchor="stretch"/>   <!-- error: PUI-CAROUSEL-CARD-SIZE 或 PUI-LAYOUT-ANCHOR -->
</Carousel>

<!-- RIGHT — 什么都不写；控件会把它撑满视口 -->
<Carousel id="x">
  <Frame/>
</Carousel>
```

## 居中选择 / peek 模式（`fill="false"`）

默认 `fill="true"`：卡片撑满视口、一卡一页（banner，上面的用例）。`fill="false"` 切到**居中卡片选择器**——卡片用自身 `size`、两侧邻卡露出、焦点卡可放大、越往边越淡：

```xml
<Frame anchor="stretch" color="black">          <!-- 全黑底 -->
  <Carousel id="sel" anchor="center" size="600x360"
            fill="false" spacing="24" edgeScale="0.8" softness="120"
            interval="0" itemTemplate="LevelCard"
            dots="bottom-center" dotSprite="ui:dot"/>
  <!-- 左右翻页箭头：C# 端绑 car.Previous()/Next()，无需库改动 -->
  <Btn id="prev" anchor="center-left"  size="48x48" margin="_,_,_,16">‹</Btn>
  <Btn id="next" anchor="center-right" size="48x48" margin="_,16,_,_">›</Btn>
</Frame>
```

```xml
<Template name="LevelCard">
  <Param name="title"/>
  <Frame size="240x320">                         <!-- 焦点卡尺寸；fill=false 下卡片必须自定尺寸 -->
    <Image anchor="stretch" sprite="ui:card_bg"/>
    <Text id="title" anchor="bottom-stretch" height="40" align="center">{{title}}</Text>
  </Frame>
</Template>
```

- **卡片必须有尺寸**：卡根写 `size=`（裸 `<Frame>`），或用自带原生尺寸的控件（`<Image>` = sprite 尺寸、`<Text>` = 量出）。无尺寸的容器卡（`Frame`/`VStack`/`HStack`/`Grid`）会兜成视口、不 peek（lint `PUI-CAROUSEL-PEEK-NO-SIZE` 提示）。
- **peek 露出多少 = `(carousel 宽 − 卡宽)/2 − spacing`**，由卡尺寸 vs carousel 尺寸**隐式**决定，没有单独的 peek 属性。
- `edgeScale`（默认 `1.0`）/ `edgeAlpha`（默认 `1.0`）：基准是**选中卡**——中心 = 声明尺寸 / 不透明，按距中心距离线性插值到边值。`1.0` = 无效果；`<1` 缩 / 淡；`edgeScale>1` 也允许（放大邻卡）。
- `softness`（默认 `0`，设计单位 int）：视口**左右边缘的羽化淡出宽度**，直接写视口 `RectMask2D.softness.x`——靠近视口边缘的卡片**像素**渐隐（空间淡出、溶进背景），区别于 `edgeAlpha` 的「整卡按第几张统一变暗」。典型配 `fill="false"` 全屏选择器做边缘 fade（`CenteredSlideBox` 默认皮肤即用它）。注意它**不被 `fill` 门控**（直接作用于视口遮罩），但只有卡片溢出到视口边缘时才看得到。
- `spacing`（默认 `0`，px）：相邻卡间距，步距 = 卡宽 + spacing。
- 这三个属性**仅 `fill="false"` 生效**；`fill="true"` 下被忽略，行为与 v1 全幅逐字等价（变体把 fill 切回 true 也会复位缩放/透明）。
- `fill="false"` 下 Carousel 占用卡根的 `localScale`（做 `edgeScale`）；卡根别再写 density `scale=`，要缩放写卡的里层节点。
- autoplay 与 fill 正交：选择器自己写 `interval="0"`。左右箭头 = `<Btn>` 绑 C# `car.Previous()` / `car.Next()`。

## `current` 是运行期独占状态

`current=` 的值是**初始页**；一旦用户或代码在运行期切换了页面，resize / Variant / Theme 切换都不会把它打回声明值。`current.<variant>` 仍然有效：只要运行期没动过，切到该 Variant 会正常重应用覆盖值；动过之后用户的选择优先（同 Tab `isOn`）。

## Lint 规则

| Code                        | 触发条件                                                                     | 级别    |
| --------------------------- | ---------------------------------------------------------------------------- | ------- |
| `PUI-CAROUSEL-CARD-SIZE`    | `<Carousel>`（`fill` 非 `"false"`，即默认全幅）的直接子卡写了 `size`/`width`/`height` | error   |
| `PUI-CAROUSEL-PEEK-NO-SIZE` | `fill="false"` 的卡根是无原生尺寸容器（`Frame`/`VStack`/`HStack`/`Grid`）且没写 `size`（会兜成视口、不 peek） | warning |
| `PUI-CAROUSEL-DOTS-ANCHOR`  | `dots=` 值不是合法的 anchor 关键字（如 `dots="center-center-wrong"`）        | warning（runtime 回退 `bottom-center`） |

（`anchor`/`margin` 违规已由通用规则 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` 覆盖，Carousel 不额外重复）
