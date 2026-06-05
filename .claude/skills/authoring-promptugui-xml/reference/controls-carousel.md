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

每张卡的 `RectTransform` 由控件内部排版——卡片不能写 `anchor` / `margin`（lint `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN`）或 `size`/`width`/`height`（lint **`PUI-CAROUSEL-CARD-SIZE`**，error）。

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

## `current` 是运行期独占状态

`current=` 的值是**初始页**；一旦用户或代码在运行期切换了页面，resize / Variant / Theme 切换都不会把它打回声明值。`current.<variant>` 仍然有效：只要运行期没动过，切到该 Variant 会正常重应用覆盖值；动过之后用户的选择优先（同 Tab `isOn`）。

## Lint 规则

| Code                        | 触发条件                                                                     | 级别    |
| --------------------------- | ---------------------------------------------------------------------------- | ------- |
| `PUI-CAROUSEL-CARD-SIZE`    | `<Carousel>` 的直接子卡写了 `size`/`width`/`height`                         | error   |
| `PUI-CAROUSEL-DOTS-ANCHOR`  | `dots=` 值不是合法的 anchor 关键字（如 `dots="center-center-wrong"`）        | warning（runtime 回退 `bottom-center`） |

（`anchor`/`margin` 违规已由通用规则 `PUI-LAYOUT-ANCHOR` / `PUI-LAYOUT-MARGIN` 覆盖，Carousel 不额外重复）
