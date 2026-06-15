# `CenteredSlideBox` 居中选择器模态设计

**日期**: 2026-06-15
**状态**: 设计阶段（待 review，未进入实施）
**建立在**: peek Carousel（[`2026-06-15-carousel-peek-mode-design.md`](2026-06-15-carousel-peek-mode-design.md)，`fill="false"`）+ 现有模态体系 `ModalRequest<TResult>`（MessageBox / InputBox / MarkdownBox / Loading 同款）。同分支 `feat/carousel-peek-mode`——是 peek 的应用层。决策编号 `CSB-Dx`。

**一句话**: 第 4 个内置模态 `CenteredSlideBox.Open<T>(items, bind, …) → Awaitable<T>`——一个居中卡片选择器弹窗：内部塞一个 `fill="false"` Carousel，用户传**强类型**数据 + bind 回调填卡，`await` 等用户选中，返回**选中的对象**（取消 = `null`）。

**作用域**:
1. 新增 `Runtime/Application/Modals/CenteredSlideBoxRequest.cs`：`CenteredSlideBoxRequest<T> : ModalRequest<T>` + 静态 `CenteredSlideBox` facade + 卡片点击装配。
2. 新增 `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`（+ `.meta`）：backdrop + panel chrome + **内置卡片 `<Template>`** + `<Carousel fill="false">`。
3. `.claude/skills/scripting-promptugui-csharp/SKILL.md`：加 `CenteredSlideBox.Open` 用法（新公开 C# API）。
4. 测试：`Tests/EditMode/.../CenteredSlideBoxTests.cs` + `Tests/PlayMode/.../CenteredSlideBoxPlayTests.cs`。

**Carousel / BuiltinTags / lint / XSD：零改动**——CenteredSlideBox 是 C# 模态 API，不是新 XML 标签；卡模板用现有内置标签。

**依赖**: 无新增包。复用 `ModalRequest<TResult>` / `UI.Modal.OpenAsync` / `Configure` 钩子 / `TryEscape`（同 MarkdownBox），`Carousel.BindItems` + `fill="false"` peek，`PuiButton`（同 Carousel dot 的点击装配）。

---

## 1. 背景与动机

游戏常见「关卡 / 角色选择」弹窗：黑底、中间居中卡片、左右拖选、确认。peek Carousel（`fill="false"`）已经提供了视觉与交互骨架，但每个游戏都手糊一遍「模态壳 + Carousel + 选中→关闭 + await 结果」是样板。把它收成一个内置模态，和 `MessageBox.Open` / `InputBox.Open` 一个味道：一行 `await` 拿到选中结果。

为什么是模态而不是普通 Screen？因为调用语义是**阻塞式问答**（"选哪个？"→ await → 拿答案），正是模态体系 `ModalRequest<TResult>` 的形状。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| CSB-D1 | 控件形态 | 第 4 个内置模态，照 MarkdownBox：`CenteredSlideBoxRequest<T> : ModalRequest<T>` + 静态 facade + `XmlSrc` 指向内置 `.ui.xml` + `UI.Modal.OpenAsync` | 复用全套模态机制（层级/排序/ESC/背景/configure/ct）；零新框架 |
| CSB-D2 | 数据传入 | 泛型 `Open<T>(IReadOnlyList<T> items, Action<IControl,T> bind, …)`，**同 `Carousel.BindItems`**。`T` 任意（强类型 / `Dictionary` / `JObject`），bind 回调填卡 | PromptUGUI 惯用法、类型安全、零魔法、用户已熟。**「JSON」不是特例**：调用方先反序列化成自己的类型；库这层不碰 JSON（是特性不是缺陷） |
| CSB-D3 | 返回值 | `Awaitable<T>`，返回**选中的对象**；`where T : class`，取消 = `null` | 比返回 `string id` 更通用（`picked?.Id` 即 id），无需额外 `idOf`/tuple 管线；`null` 取消干净（同 InputBox） |
| CSB-D4 | 卡片模板位置 | 卡片 `<Template name="Card">` 写在 **`CenteredSlideBox.ui.xml` 自己文档里**，Carousel 用 **v1 现成的 `itemTemplate` 机制**（同文档 `ResolveFactory`）解析 | **绕开跨文档难题**：模态走 `UI.LoadDocument`（同步，不合并 commons/imports，见 `ModalDocCache`），调用方文档里的模板模态看不见——但同文档的 `<Template>` 解析正常。于是 `(cardSrc,cardTemplate)` / async 模板加载 / Carousel 的 `SetItemFactory` 钩子全部不需要，**Carousel 零改动** |
| CSB-D5 | 通用卡片槽位 | 内置卡 = `cover`(Image，stretch) + `name`(Text，底部)；bind 填这俩，用不到的不碰 | 「封面 + 标题」覆盖大多数关卡/角色选择；保持「简易」 |
| CSB-D6 | 换一种卡片样式 | `XmlSrc` 是**可写静态**（同所有模态）→ 指向你自己的 XML（保持 id 契约）。命名变体 = 抄 ~10 行 facade、传不同 `XmlSrc`（`CenteredSlideBoxRequest<T>` 持 `XmlSrc` 字段） | 用户想要的「新建一种 CenteredSlideBox」；比 subclass 轻，无继承 |
| CSB-D7 | 选中交互（A+C） | **点侧卡** → 居中它（`car.GoTo(i)`，不确认）；**点居中卡** → 确认（`close(items[i])`）；**确认按钮** → 确认居中（`close(items[car.Current])`） | coverflow 直觉（点哪个看哪个）+ 显式确认按钮（可发现性）；用户选定 A+C |
| CSB-D8 | 卡片点击装配 | 每张卡挂**透明 raycast catcher + `PuiButton`**，`onClick` → 居中/确认（**click 语义**，非 OnPointerDown）；拖动不实现 `IDragHandler` → 冒泡给 `CarouselView` | 必须区分「点」与「拖」：`PuiButton.onClick` 只在 click（无拖动）触发，同 Carousel dot 的装配；拖动照常翻页（同 CAR-D12） |
| CSB-D9 | 取消（三通道） | `×` Btn / 背景 Image / ESC → `close(null)`；`TryEscape(out r){ r=null; return true; }` | 照 MarkdownBox；选择器永远可取消 |
| CSB-D10 | chrome 参数 | `title`（空→隐藏）/ `confirmLabel`（空→内置 XML 的 confirm 默认文案，如 `"OK"`；要本地化自己传 tr 后的串）/ `configure`（post-bind 钩子，够到 panel/carousel 调 peek 参数）/ `mode` / `ct` | 镜像 MessageBox 的可定制面；peek 参数（spacing/edgeScale/edgeAlpha）烤进内置 XML 默认，想调走 configure |
| CSB-D11 | 边界 | 空 `items` → carousel 空 + 确认 `Interactable=false`（只能取消）；1 卡 → 居中无邻卡，确认返回它；`ct` 取消 → null | 选择器无项时不该能「确认」；单项是合法退化 |

---

## 3. 公开 API

```csharp
namespace PromptUGUI.Application.Modals
{
    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口（CSB-D6）。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        public static UnityEngine.Awaitable<T> Open<T>(
            System.Collections.Generic.IReadOnlyList<T> items,
            System.Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            System.Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
            => UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);
    }
}
```

调用：

```csharp
record Level(string Id, string Name, Sprite Cover);

var picked = await CenteredSlideBox.Open(
    levels,
    bind: (card, lv) => { card.Get<Text>("name").TextValue = lv.Name;
                          card.Get<Image>("cover").Sprite   = lv.Cover; },
    title: "选择关卡");
if (picked != null) StartLevel(picked);     // 选中对象；要 id 就 picked.Id
```

---

## 4. 内部：`CenteredSlideBoxRequest<T>`

```csharp
public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class   // public（同其它 modal request）：命名变体 facade / 测试直接 new
{
    public IReadOnlyList<T> Items;
    public Action<IControl, T> BindCard;
    public string Title;
    public string ConfirmLabel;
    public string XmlSrcOverride;                    // CSB-D6：命名变体 facade 可传；null→静态默认

    public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

    public override void Bind(IScreen screen, Action<T> close)
    {
        // —— title ——
        var titleCtl = screen.Get<Text>("title");
        if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
        else titleCtl.TextValue = Title;

        // —— 取消三通道（CSB-D9）——
        screen.Get<Btn>("close").OnClick.Subscribe(_ => close(null)).AddTo(screen);
        screen.Get<Image>("backdrop").OnPointerDown.Subscribe(_ => close(null)).AddTo(screen);

        // —— carousel + 卡 ——
        var car = screen.Get<Carousel>("cards");
        int idx = 0;
        car.BindItems(Items, (IControl card, T item) =>
        {
            int i = idx++;                            // BindItems 按 0..n-1 顺序回调
            BindCard?.Invoke(card, item);
            AttachCardClick(card, i, car, close);     // CSB-D7/D8
        }).AddTo(screen);

        // —— 确认按钮（CSB-D7）——
        var ok = screen.Get<Btn>("confirm");
        if (!string.IsNullOrEmpty(ConfirmLabel)) ok.Text = ConfirmLabel;
        if (Items.Count == 0) ok.Interactable = false;            // CSB-D11
        ok.OnClick.Subscribe(_ =>
        {
            int cur = car.Current;
            if (cur >= 0 && cur < Items.Count) close(Items[cur]);
        }).AddTo(screen);
    }

    // ESC / 背景 → 取消（CSB-D9）
    public override bool TryEscape(out T result) { result = null; return true; }

    // 每张卡：透明 raycast catcher + PuiButton，click（非拖动）→ 居中或确认。
    private void AttachCardClick(IControl card, int i, Carousel car, Action<T> close)
    {
        var go = card.GameObject;
        var img = go.GetComponent<UnityEngine.UI.Image>() ?? go.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0, 0, 0, 0);            // 透明，仅 raycast
        img.raycastTarget = true;
        var btn = go.AddComponent<PuiButton>();        // onClick = click 语义；拖动冒泡给 CarouselView
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            if (car.Current == i) close(Items[i]);     // 点居中卡 = 确认
            else car.GoTo(i, animated: true);          // 点侧卡 = 居中
        });
    }
}
```

> 卡根（默认 `<Frame>`）本无 Graphic；`AttachCardClick` 给它补一个**透明 raycast Image**（不挡视觉、不挡拖动——drag 不被 PuiButton 处理，沿用 Carousel 的 viewport 冒泡）。这也使「点击命中整张卡」与卡的具体槽位无关（换皮卡无需特意留点击面）。代价：卡内若有自己的按钮会被这层吃掉——v1 选择器整卡即选，可接受；需要卡内交互属高级场景（自定 XmlSrc 自理）。

facade 见 §3（`CenteredSlideBox.Open` 就是它，照 MessageBox 风格）。**命名变体** = 复制这 ~10 行、给 request 传不同 `XmlSrcOverride`（CSB-D6）。

---

## 5. 内置 `CenteredSlideBox.ui.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <!-- 通用卡片：封面 + 标题。⚠️ <Template> 必须是文档顶层元素（<Screen> 的兄弟，
       不能嵌进 <Screen> —— 解析器只从 <PromptUGUI> 直接子提取模板）。换皮 = 换这段 + 整个 XmlSrc（CSB-D6）-->
  <Template name="Card">
    <Frame size="240x320">
      <Image id="cover" anchor="stretch"/>
      <Text  id="name"  anchor="bottom-stretch" height="40" align="center" tr="false"/>
    </Frame>
  </Template>

  <Screen name="CenteredSlideBox">
    <!-- ⚠️ backdrop 与 panel 是兄弟（backdrop 在底层、panel 在上层），不能把 panel 嵌进 backdrop——
         否则点 panel 任意处都会冒泡到 backdrop 的 OnPointerDown 误关。点 backdrop 空白区取消。 -->
    <Image id="backdrop" anchor="stretch" color="#000000A0"/>
    <Frame id="panel" anchor="center" size="720x460">
      <Text id="title" anchor="top-stretch" height="48" align="center" tr="false"/>
      <Btn  id="close" anchor="top-right" size="36x36" margin="6,6,_,_">×</Btn>

      <Carousel id="cards" anchor="stretch" margin="56,16,72,16"
                fill="false" interval="0" loop="true"
                spacing="24" edgeScale="0.82" edgeAlpha="0.45"
                itemTemplate="Card"
                dots="bottom-center" dotColor="#666" dotSelectedColor="#fff"/>

      <Btn id="confirm" anchor="bottom-center" size="160x48" margin="_,_,12,_">OK</Btn>
    </Frame>
  </Screen>
</PromptUGUI>
```

**id 契约**（换皮 XML 必须保留）：`backdrop`(Image) / `panel` / `title`(Text) / `close`(Btn) / `confirm`(Btn) / `cards`(Carousel `fill="false" itemTemplate="…"`) + 卡模板里 bind 用到的槽位（默认 `cover`/`name`）。卡模板根的 `size=` 决定卡尺寸（peek MeasureCard 读它）。⚠️ **换皮卡片的根应是无 Graphic 的容器**（`<Frame>`/Stack）——`AttachCardClick` 会给卡根补一层透明 raycast `Image`（color 置 0）；若卡根本身是带可见 sprite 的 `<Image>`，会被这层置成透明。尺寸/配色/peek 参数都是合理默认，想调走 `configure`。

> 内置 XML 会过 UIXmlLint（吃自己的 `fill="false"` + 带 `size` 的卡 = 狗粮验证 peek 模式 lint 门控正确）。

---

## 6. 交互流（A+C，CSB-D7/D8/D9）

```
开窗 → BindItems 建卡（每卡挂透明 raycast + PuiButton）→ 居中默认页(current=0)
用户拖动 / 左右（无内置箭头，但 dots 可点）→ 翻页，焦点卡放大不淡（peek）
点侧卡  → car.GoTo(i) 居中它（动画），不返回
点居中卡 → close(items[i]) → await 返回该项
点确认  → close(items[car.Current])
× / 点背景 / ESC → close(null) → await 返回 null
```

`UI.Modal.OpenAsync` 负责：加载内置 XML（`ModalDocCache`）、实例化模态 Screen、跑 `Bind` 再跑 `Configure`、ESC 监听调 `TryEscape`、`close(result)` → 关窗 + 完成 `Awaitable<T>`。

---

## 7. 边界 / 错误处理（CSB-D11）

| 场景 | 处理 |
|---|---|
| `items` 空 | carousel 空；确认 `Interactable=false`（灰）；只能 ×/ESC/背景 取消 → null |
| `items` 1 张 | 居中、无邻卡 peek；确认/点它返回该项 |
| `bind` 为 null | 卡只有模板默认外观（不填槽）；不报错 |
| `ct` 取消 | `UI.Modal.OpenAsync` 关窗 + Awaitable 取消（同其它模态） |
| 换皮 XML 缺 id（如无 `confirm`） | `screen.Get<Btn>("confirm")` 抛 `KeyNotFoundException`——契约违例，开发期即炸（同 MessageBox 对必备 id 的假设） |
| 卡模板根无 `size`（fill=false） | peek 兜成视口尺寸 + lint `PUI-CAROUSEL-PEEK-NO-SIZE`（内置卡有 size，不触发） |

---

## 8. 测试

EditMode（照 `MessageBoxRequest` / `InputBoxRequest` 的模态驱动套路 + `UI.ResetForTests`；内置 XML 走 Resources）:

- **确认按钮 → 返回居中项**：Open 拿 Awaitable；先 `GoTo(k)`（或拖）把第 k 项居中；点 confirm；await == `items[k]`。
- **点居中卡 → 返回该项**：点 current 卡的 PuiButton；await == `items[current]`。
- **点侧卡 → 居中不返回**：点非 current 卡；`car.Current` 变成该索引；Awaitable 未完成。
- **取消三通道**：点 `close` / 点 `backdrop` / 触发 ESC（`TryEscape`）→ await == `null`。
- **空列表**：确认 `Interactable==false`；ESC → null。
- **title=null** → title 隐藏；**confirmLabel** → 按钮文案改。
- **bind 填槽**：bind 设 `name`/`cover`，断言卡上对应控件值。

PlayMode：烟雾——开窗、拖一格、点确认，结果非空不崩。

---

## 9. 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Runtime/Application/Modals/CenteredSlideBoxRequest.cs` | `CenteredSlideBoxRequest<T>` + `CenteredSlideBox` facade + `AttachCardClick` | 新建 |
| `Runtime/Resources/PromptUGUI/Modals/CenteredSlideBox.ui.xml`(+.meta) | backdrop + panel + title/close/confirm + `<Carousel fill="false">` + `<Template name="Card">` | 新建 |
| `Tests/EditMode/Application/Modals/CenteredSlideBoxTests.cs` | §8 EditMode | 新建 |
| `Tests/PlayMode/Application/Modals/CenteredSlideBoxPlayTests.cs` | §8 PlayMode 烟雾 | 新建 |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | `CenteredSlideBox.Open` 用法 + 槽位契约 + 换皮 | 改 |

---

## 10. Out of Scope

- **内置左右箭头按钮**：用户要的话自己在 panel 加 `<Btn>` 绑 `car.Previous()/Next()`（换皮 XML 里加）；不进默认 chrome。
- **多选**：v1 单选（选一个返回）。多选另设计。
- **卡内交互元素**（卡里再放按钮）：被整卡点击层吃掉；高级场景自定 XmlSrc 自理（CSB-D8 注）。
- **per-call 换皮参数**：v1 靠静态 `XmlSrc` / 命名变体 facade；不加 `Open(..., xmlSrc)` 参数（保持签名简单，YAGNI）。
- **非 class 的 `T`（值类型）**：`where T : class` 限定（取消用 null 哨兵）；值类型数据包一层引用类型。
- **搜索 / 分组 / 分页**：简易选择器，大量项请自建 ScrollList 界面。

---

## 11. 跟现有体系的整合点

- `scripting-promptugui-csharp/SKILL.md`：模态小节加 `CenteredSlideBox.Open<T>`（数据=items+bind 同 BindItems、返回选中对象 null=取消、A+C 交互、槽位契约 cover/name、换皮 XmlSrc）。
- XML / Addressables skill：无关，不动。
- 主 spec / XSD / lint：无新 XML 标签或属性，不动。
- 复用 peek Carousel（本分支前序工作）——CenteredSlideBox 是它落地的第一个「成品级」用例。
