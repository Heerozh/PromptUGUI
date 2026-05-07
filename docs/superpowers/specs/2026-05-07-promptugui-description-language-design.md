# PromptUGUI 描述语言设计

**日期**：2026-05-07
**状态**：设计阶段（待 review，未进入实施）
**作用域**：仅 v1 描述语言与 C# API 设计；不含实现代码、不含 M1 之外的实施排期细节

---

## 1. 背景与目标

PromptUGUI 是一个 Unity 6+ 开源库，把一个紧凑的 XML 描述文件转译为运行时的 uGUI。
目标场景：像素风 SLG，需要同时跑 PC 宽屏与手机竖屏。

**为什么不用现成方案**

- **UI Toolkit**：像素游戏社区反馈少，预期踩坑成本高
- **uGUI 原生工作流**：高度可视化但重；对版本控制不友好（场景/Prefab 二进制 diff），LLM 难以直接生成

**这一层的核心目标**

1. 描述文件**作者面统一**：人写、LLM 写、未来工具生成都走同一份语法
2. 描述文件**精简到一页 skill 能教完**（约 40 行速查）
3. **位置全部基于锚点**，避免绝对坐标膨胀
4. **控件可由用户扩展**，但暴露给描述语言的接口保持极简
5. **数据/事件全部代码侧推送**，描述文件仅产生句柄
6. 未来与 HeTu 服务器（订阅式数据 + RPC）接入零摩擦——通过 R3 `Observable<T>` 统一抽象实现

**不做的事**（详见 §10）：动画、主题 token 系统、本地化、运行时 DOM 编辑 API、绑定表达式、可视化编辑器。

---

## 2. 设计决策一览

下表是设计阶段做过的关键二元选择，便于后续 review 与争议时回溯。

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| D1 | 多平台布局策略 | 同一控件树 + 锚点/尺寸变体 | 避免维护两份；同构利于 diff |
| D2 | 主要作者 | 人 + LLM 双优先 | 排除纯 binary / 纯 fluent C# 路线 |
| D3 | 自定义控件模型 | 描述文件可组合模板 + 代码侧 Prefab 注册 | 兼顾轻量复用与重量级控件 |
| D4 | 样式机制 | 样式与控件类型绑定（PrimaryButton / DangerButton） | 移除 class/token 抽象层；语法最简 |
| D5 | 数据绑定 | 描述文件只标记 id；代码侧主动推送 | 描述语言不引入表达式；R3 自然衔接 |
| D6 | 文件粒度 | Screen + Template 双概念，支持 Import | 大场景与可复用片段并存 |
| D7 | 锚点抽象 | 4×4 预设 + margin/size 二字段，统一向锚点内为正 | 与 uGUI 心智同构，但用户面只剩两个字段 |
| D8 | 文件格式 | XML | 通用工具链（高亮/折叠/Schema/解析器）零成本 |
| D9 | Fragment vs Template | 合并为单一 `<Template>` | KISS |
| D10 | 模板逻辑 | 仅允许 `if="{{p}}"`；无 `For`、无表达式 | 强制把逻辑推到代码侧 |
| D11 | margin 语义 | 始终"从锚点向内为正" | 用户不需根据锚点切换正负号 |
| D12 | 拉伸轴禁出现 size | 严格报错 | 避免歧义 |
| D13 | Variant 切换时机 | 运行时可切换，触发已开 Screen 重解算 | 支持桌面端窗口缩放 |
| D14 | Variant 优先级 | 声明顺序 last-active-wins | 简单，可控 |
| D15 | Variant 块形式动作 | 仅 `<Add>` | 可见性靠 `hidden.var`，覆盖靠内联 attr.var |
| D16 | 自定义控件接入 | `[UIAttr]` + `[Bind]` 反射，缓存 setter | 注册期一次反射，运行零开销 |
| D17 | `Get` 失败 | 抛异常 | UI 错误应在测试期 fail loud |
| D18 | 事件/数据 API 类型 | 统一 `Observable<T>`，禁用 `event`/Action | 第一版即对齐 HeTu 抽象 |

---

## 3. 一个完整可读的例子

读完这段就能掌握 80%。

```xml
<?xml version="1.0" encoding="utf-8"?>
<UI version="1">
  <Import src="common/Buttons.ui.xml"/>

  <!-- 可复用的标题面板 -->
  <Template name="TitledPanel">
    <Param name="title"/>
    <Param name="closable" default="true"/>

    <VStack padding="16" spacing="8">
      <HStack height="32" spacing="8">
        <Text style="h2">{{title}}</Text>
        <Frame width="0"/>
        <CloseButton if="{{closable}}" id="close"/>
      </HStack>
      <Slot/>
    </VStack>
  </Template>

  <!-- 主菜单 -->
  <Screen name="MainMenu">
    <Image anchor="stretch" sprite="bg/main"/>

    <VStack id="menuRoot"
            anchor="center" size="480x320" spacing="12"
            anchor.mobile-portrait="bottom-stretch"
            size.mobile-portrait="_,400"
            margin.mobile-portrait="_,16,80,16">
      <PrimaryButton id="playBtn"     size="240x64">开始游戏</PrimaryButton>
      <PrimaryButton id="settingsBtn" size="240x64">设置</PrimaryButton>
      <DangerButton  id="quitBtn"     size="240x64">退出</DangerButton>
    </VStack>

    <Variant when="mobile-portrait">
      <Add into="@root">
        <VirtualJoystick id="vjs" anchor="bottom-left"
                         size="160x160" margin="_,_,40,40"/>
      </Add>
    </Variant>
  </Screen>
</UI>
```

代码侧：

```csharp
var screen = UI.Open("MainMenu");
screen.Get<PrimaryButton>("playBtn").OnClick
      .Subscribe(_ => Game.Start()).AddTo(screen);
screen.Get<DangerButton>("quitBtn").OnClick
      .Subscribe(_ => Application.Quit()).AddTo(screen);
```

---

## 4. 文件骨架

### 4.1 后缀与编码

- 文件后缀：`.ui.xml`（双后缀，让 IDE 自动 XML 高亮，业务工具按 `.ui` 过滤）
- 编码：UTF-8
- 根元素：`<UI version="1">`，`version` 强制必填，预留语言演进

### 4.2 顶层允许元素

| 元素 | 用途 |
|---|---|
| `<Import src="..." [as="ns"]/>` | 引入其他 .ui.xml 中的 Screen/Template |
| `<Screen name="...">` | 完整场景，运行时由 `UI.Open(name)` 打开 |
| `<Template name="...">` | 可复用子树定义，编译期展开 |

跨文件 `Screen` 同名 → 报错。
跨文件 `Template` 同名 → 报错；`as="ns"` 用于消歧（`<ns.TitledPanel/>`）。
注释直接用 XML 标准 `<!-- -->`。

---

## 5. 内置控件原语

刻意保持极少：6 个原语，覆盖布局与最基础视觉，其他全部通过自定义控件或 `<Template>` 扩展。

| 标签 | 作用 | 对应 uGUI |
|---|---|---|
| `<Frame>` | 纯定位容器，无视觉 | 空 RectTransform |
| `<Image>` | 图像 / 9-slice / 纯色块 | Image |
| `<Text>` | 文本 | TMP_Text |
| `<VStack>` | 纵向自动排布 | RectTransform + VerticalLayoutGroup |
| `<HStack>` | 横向自动排布 | RectTransform + HorizontalLayoutGroup |
| `<Grid>` | 网格排布 | RectTransform + GridLayoutGroup |

不开 `<Button>`、`<Toggle>`、`<Slider>`、`<Dropdown>`、`<ScrollList>` 等原语。这些**必须**由代码侧注册的自定义控件提供，因为像素游戏中它们的视觉风格高度差异化（统一原语反而会出歧义）。

### 5.1 通用属性（任何标签可用）

| 属性 | 作用 |
|---|---|
| `id` | 在所属 Screen / Template 内唯一的句柄 |
| `anchor` | 9 预设之一（详见 §6） |
| `size` / `width` / `height` | 尺寸（详见 §6） |
| `margin` | 距锚点的内向距离（详见 §6） |
| `pivot` | 透传 RectTransform pivot；缺省随 anchor 自动推导 |
| `padding` | 容器内边距（仅 VStack/HStack/Grid/Frame） |
| `spacing` | 子项间距（仅 VStack/HStack/Grid） |
| `hidden` | 初始隐藏（GameObject SetActive false） |
| `interactable` | 初始不可交互（CanvasGroup.interactable false） |

### 5.2 文本内容简写

```xml
<Text>金币: 1234</Text>          <!-- 等价 <Text text="金币: 1234"/> -->
<PrimaryButton>开始游戏</PrimaryButton>
```

控件想吃这种简写，需在注册时声明 `defaultTextAttr`（默认值 `"text"`）。Frame/VStack 等容器不支持。

### 5.3 控件特有属性

由各标签自行声明：
- `<Image sprite="bg/main" color="#FFFFFFAA" type="sliced|simple|filled|tiled"/>`
- `<Text font="..." size="32" color="..." align="left|center|right" wrap="true"/>`

完整属性表见各控件 README（不在本 spec 范围）。

---

## 6. 锚点与尺寸

### 6.1 anchor —— 4×4 网格

`anchor="<vertical>-<horizontal>"`

| | left | center | right | stretch |
|---|---|---|---|---|
| **top** | top-left | top-center | top-right | top-stretch |
| **center** | center-left | center | center-right | center-stretch |
| **bottom** | bottom-left | bottom-center | bottom-right | bottom-stretch |
| **stretch** | stretch-left | stretch-center | stretch-right | stretch |

别名：`center` = `center-center`；`stretch` = `stretch-stretch`；`fill` = `stretch`。

### 6.2 size / width / height

```xml
<Image anchor="top-right"      size="240x80"/>     <!-- 两轴点锚 -->
<Image anchor="stretch-left"   width="200"/>       <!-- 仅水平点锚 -->
<Image anchor="top-stretch"    height="64"/>       <!-- 仅竖向点锚 -->
<Image anchor="stretch"/>                          <!-- 双轴拉伸 -->
```

**严格规则**：拉伸轴上**禁止**出现 `size` / `width` / `height` 的相应分量。如 `anchor="top-stretch"` + `width="..."` 是非法的 → 编译期报错。

### 6.3 margin —— 统一为"从锚点向内的距离"

```
margin="16"           四边都 16
margin="16,8"         上下 16，左右 8
margin="16,8,4,12"    T=16, R=8, B=4, L=12
```

任何锚点下 margin 的方向语义恒定："离锚点/锚定边向内为正"。

举例：

```xml
<Btn anchor="top-right"      size="240x80" margin="16"/>           <!-- 距右上 16 -->
<Bar anchor="top-stretch"    height="64"  margin="0,8,_,8"/>       <!-- 顶部全宽 -->
<Side anchor="stretch-right" width="200"  margin="16,0,16,_"/>     <!-- 右侧全高 -->
<BG  anchor="stretch"        sprite="bg"/>                         <!-- 全屏 -->
```

`_` 表示该位"不参与布局"，仅可读性。允许全省。

### 6.4 pivot 自动推导

| anchor | 自动 pivot |
|---|---|
| top-left | (0, 1) |
| top-right | (1, 1) |
| center | (0.5, 0.5) |
| bottom-stretch | (0.5, 0) |
| stretch | (0.5, 0.5) |

仅当需要绕非中心点旋转/缩放时才显式 `pivot="0.5,1"` 等。

### 6.5 在 VStack/HStack/Grid 内的特殊行为

子节点的 `anchor` 与 `margin` **被布局组接管而失效**。仅 `size` / `width` / `height` 生效（被写入 LayoutElement.preferredWidth/Height）。

子节点显式写了 `anchor` → **编译期警告**（不静默丢弃，避免误导）。

容器自身仍用 `anchor` + `size`/`margin` 在父级里定位。

---

## 7. Template：复用与组合

### 7.1 定义

```xml
<Template name="TitledPanel">
  <Param name="title"/>
  <Param name="closable" default="true"/>
  <Param name="icon"     default=""/>

  <VStack padding="16" spacing="8">
    <HStack height="32" spacing="8">
      <Image if="{{icon}}" sprite="{{icon}}" size="32x32"/>
      <Text style="h2">{{title}}</Text>
      <Frame width="0"/>
      <CloseButton if="{{closable}}" id="close"/>
    </HStack>
    <Slot/>
  </VStack>
</Template>
```

**约定**

- `<Param>` 必须紧跟 `<Template>` 开头；其他位置出现的 Param 视为普通自定义控件
- `default` 缺省 → 该参数必填，调用方未传则编译期报错
- `<Slot/>` 出现 0 或 1 次（v1 不支持多 slot）
- 同名 Template 跨文件冲突 → 报错；用 `Import as="ns"` 或重命名

### 7.2 调用

```xml
<TitledPanel anchor="center" size="600x400" title="背包">
  <Grid columns="6" spacing="4">
    <ItemSlot/>  ...
  </Grid>
</TitledPanel>
```

属性 = 参数；元素内容 = 注入到 `<Slot/>`。形式上与原生标签完全一致。

### 7.3 替换规则（仅 Template 内有效）

| 用法 | 例 |
|---|---|
| 属性插值 | `text="{{title}}"` |
| 属性内拼接 | `sprite="icons/{{icon}}.png"` |
| 文本节点 | `<Text>{{title}}</Text>` |
| 条件元素 | `<X if="{{closable}}"/>` |

**`if` 是唯一允许的逻辑结构**：
- 仅检查参数 truthy（非空串、非 false、非 0、非 null）
- 不支持 `!`、`==`、`&&`、`||` 等
- 不支持 `<Else>`、`<For>`

如果模板需要更多逻辑 → 它不该是模板，应改为代码侧自定义控件。

### 7.4 ID 作用域

模板内的 `id` 是**模板实例局部命名空间**：

```xml
<Template name="Dialog">
  <Frame>
    <CloseButton id="close"/>
    <Slot/>
  </Frame>
</Template>

<Screen name="Game">
  <Dialog id="confirm">
    <Text>真的吗？</Text>
  </Dialog>
</Screen>
```

代码侧：

```csharp
var dialog   = screen.Get("confirm");        // Dialog 实例
var closeBtn = dialog.Get("close");          // 模板内部
// 或：screen.Get("confirm/close")
```

同一模板可被实例化多次，id 不冲突。

### 7.5 Screen 与 Template 区别

| | Screen | Template |
|---|---|---|
| 顶层用法 | `UI.Open(name)` | 不可独立打开 |
| Canvas 归属 | 自己的根 Canvas | 嵌入父 Screen 的 Canvas |
| 生命周期 | 有 OnOpen/OnClose 钩子 | 跟随父节点 |
| 可作为标签使用 | 否 | 是 |

需要"既能独立打开又能嵌入"的场景：定义为 Template，再用一个简单 Screen 包一层。

### 7.6 Import

```xml
<Import src="common/Buttons.ui.xml"/>
<Import src="common/Panels.ui.xml" as="ui"/>

<Screen name="X">
  <PrimaryButton/>           <!-- 来自 Buttons -->
  <ui.TitledPanel/>          <!-- 来自 Panels，带前缀消歧 -->
</Screen>
```

`as=` 仅在两个 Import 暴露同名 Template 时强制；常态不需要。

---

## 8. Variant：平台与上下文变体

### 8.1 模型

- Variant 是带名字的开关，由代码侧管理：
  ```csharp
  UI.Variants.Set("mobile-portrait", true);
  UI.Variants.Set("pc", false);
  ```
- 多个开关可同时为真
- 切换 Variant 触发已实例化 Screen 的**重新解算**（不重建 GameObject，只刷新被覆盖的属性值）——支持 PC 端窗口缩放等场景

### 8.2 内联属性覆盖（90% 用法）

```xml
<VStack id="menuRoot"
        anchor="center" size="480x320"
        anchor.mobile-portrait="bottom-stretch"
        size.mobile-portrait="_,320"
        margin.mobile-portrait="_,16,40,16">
  ...
</VStack>
```

任何属性都可加 `.variantName` 后缀；多个后缀可并存。

### 8.3 解析规则

按属性的**声明顺序**扫描所有 `attr.X` 形式：取**最后一个 X 当前为真**的那个；都不真则用基础值。

```xml
<X size="100" size.mobile="200" size.tablet="150"/>
```

| 当前激活 | 结果 |
|---|---|
| 都不开 | 100 |
| `mobile` | 200 |
| `tablet` | 150 |
| `mobile`+`tablet` 都开 | 150（声明在后） |

需要不同优先级，调整声明顺序即可。

### 8.4 块形式：仅 `<Add>`

```xml
<Variant when="mobile-portrait">
  <Add into="#menuRoot" at="end">
    <Frame height="40"/>
  </Add>
  <Add into="@root">
    <VirtualJoystick id="vjs" anchor="bottom-left" size="160x160" margin="_,_,40,40"/>
  </Add>
</Variant>
```

- `into` 指定目标父节点：`#id` 或 `@root`（Screen 根）
- `at` 控制插入位置：`start` / `end`（默认）/ 整数索引
- 移除元素 → 用 `hidden.variant="true"`，无需 Remove
- 修改属性 → 用内联 `attr.variant`，无需 Override

### 8.5 不可覆盖

下列字段**禁止**带 `.variant` 后缀，编译期报错：

- `id`
- 标签名本身
- `<Param>` 的 `default`

理由：避免 Variant 切换造成控件身份/类型/契约漂移，使代码侧 `Get<T>` 永远稳定。

---

## 9. 代码侧 C# 接口

### 9.1 顶层 facade

```csharp
public static class UI {
    public static IScreen Open(string screenName);
    public static void   Close(string screenName);
    public static IScreen Get(string screenName);

    public static class Variants {
        public static void Set(string name, bool active);
        public static bool IsActive(string name);
    }

    public static class Registry {
        public static void Register<T>(string tag, GameObject prefab) where T : Control;
    }
}
```

### 9.2 句柄查询

```csharp
PrimaryButton btn  = screen.Get<PrimaryButton>("playBtn");
IControl       any = screen.Get("playBtn");
IControl   nested  = screen.Get("confirmDialog/close");
IEnumerable<ItemSlot> slots = screen.GetAll<ItemSlot>();
```

- `Get` 找不到 → 抛异常（fail loud）
- `TryGet` 提供给运行时不确定存在的场景

### 9.3 自定义控件作者模式

一个自定义控件 = Prefab + 类 + 一次注册。

```csharp
public class PrimaryButton : Control {
    [UIAttr] public string Text {
        get => _label.text;
        set => _label.text = value;
    }

    public Observable<Unit> OnClick => _btn.OnClickAsObservable();

    [Bind] TMP_Text _label;
    [Bind] Button   _btn;
}

UI.Registry.Register<PrimaryButton>("PrimaryButton",
    Resources.Load<GameObject>("UI/PrimaryButton"));
```

约定：
- `[UIAttr]` 标记的属性 = 描述文件该 tag 上同名属性自动写入
- `[Bind]` 标记的字段 = Prefab 内同名子节点自动 wire
- 反射只在注册期一次，运行期使用缓存的 setter（零额外 GC）

通用属性（`anchor` / `size` / `margin` / 等）由 `Control` 基类统一处理；子类不需也不允许覆盖。

### 9.4 事件 = R3 流

所有事件统一暴露为 `Observable<T>`：

```csharp
screen.Get<PrimaryButton>("playBtn")
      .OnClick
      .Subscribe(_ => StartGame())
      .AddTo(screen);
```

`IScreen` 实现 `IDisposable` / `ICancelable`，是订阅生命周期 owner。

**严格约束**：库暴露的所有事件接口禁止使用 C# `event` 或 `Action` 回调，必须是 `Observable<T>`。这是为 HeTu 接入预留的唯一约束。

### 9.5 数据绑定 = 代码侧推送

```csharp
playerGoldRP.Subscribe(g => screen.Get<Text>("goldLabel").Text = $"金币: {g}")
            .AddTo(screen);

// 可选糖：
playerGoldRP.BindText(screen.Get<Text>("goldLabel"), g => $"金币: {g}")
            .AddTo(screen);
```

**列表也是代码侧推送**——列表控件本身是自定义控件：

```xml
<ScrollList id="inv" anchor="stretch" itemTemplate="ItemSlot"/>
```

```csharp
var list = screen.Get<ScrollList<Item>>("inv");
list.BindItems(player.Inventory, (slot, item) => {
    slot.Icon  = item.Icon;
    slot.Count = item.Count;
});
```

`itemTemplate` 是已注册 tag 名；ScrollList 内部按需要实例化。

### 9.6 Screen 生命周期（可选 ScreenView）

简单场景直接 Open + Get + Subscribe；复杂场景继承 `ScreenView`：

```csharp
public class InventoryView : ScreenView {
    protected override string ScreenName => "Inventory";

    protected override void OnOpen() {
        Get<PrimaryButton>("closeBtn").OnClick
            .Subscribe(_ => Close()).AddTo(this);
        Player.Gold.BindText(Get<Text>("gold"), g => $"{g}").AddTo(this);
    }

    protected override void OnClose() { /* save state etc. */ }
}

UI.Bind<InventoryView>();   // 注册：Screen name → 类
```

`UI.Open("Inventory")` 时若有 Bound class 则同时实例化它，调用 OnOpen。

### 9.7 HeTu 接入预留

HeTu 订阅返回 `Observable<T>`（设计上对齐 R3）。所以**当前 API 不需要为 HeTu 改任何东西**：

```csharp
// 今天：本地 ReactiveProperty
playerGoldRP.BindText(...).AddTo(screen);

// 未来：HeTu 订阅
HeTu.Sub<int>("player.gold").BindText(...).AddTo(screen);
```

预留点仅一处：库的所有事件/绑定 API 必须 `Observable<T>`，不得退化为 event / Action。这条在 §9.4 已明确为硬约束。

---

## 10. 显式非目标（v1 不做）

为防止后续讨论 scope creep：

- ❌ **动画**：用 Unity Animator / DOTween，不在描述文件管
- ❌ **主题 token / 全局样式表**：风格通过控件类型变体表达（PrimaryButton vs DangerButton），不再做 token 层
- ❌ **本地化**：文本由代码侧推送或经过外部 L10N 钩子；描述文件不内置 i18n
- ❌ **运行时 DOM 编辑 API**：想动态加节点请重建 Screen
- ❌ **绑定表达式 / 模板循环 `<For>`**：列表是代码侧推送的自定义控件
- ❌ **多 Slot 命名**：单匿名 Slot 够用；真有多 slot 需求重新评估
- ❌ **可视化编辑器**：但保留 ScreenView 等抽象使其将来可加
- ❌ **样式 class / inheritance**：见 D4

---

## 11. PromptUGUI 描述语言速查（一页 skill）

下面 40 行就是未来给 LLM 的 system prompt 片段，作为对"语言精简"目标的最终验证。

```
# PromptUGUI 描述语言 (.ui.xml) 速查

## 文件骨架
<UI version="1">
  <Import src="path.ui.xml" [as="ns"]/>
  <Screen   name="...">  body  </Screen>
  <Template name="..."> [<Param name="p" [default=""]/>...] body </Template>
</UI>

## 内置原语 (6)
<Frame>            纯定位容器
<Image sprite="" color=""/>
<Text>文本</Text>     或 <Text text="..."/>
<VStack spacing="" padding="">
<HStack spacing="" padding="">
<Grid columns="" spacing="" padding="">

## 自定义控件
注册后写法等同 <PascalCase .../>。

## 通用属性
id anchor size|width|height margin pivot padding spacing hidden interactable

## anchor
"<v>-<h>"  v ∈ {top,center,bottom,stretch}  h ∈ {left,center,right,stretch}
别名: center, stretch, fill

## 尺寸
size="WxH"  /  width="W"  /  height="H"     拉伸轴禁出现

## margin (向锚点内为正)
"X" | "V,H" | "T,R,B,L"   "_" = 占位

## 文本内容
<Btn>开始</Btn> 等价 <Btn text="开始"/>

## 模板插值 (仅 Template 内)
{{p}}            在属性值/文本中替换
if="{{p}}"       仅 truthy 时保留该元素
<Slot/>          注入子节点

## ID 路径
<D id="d"><B id="b"/></D>  →  screen.Get("d/b")

## Variant
内联:  attr.var="..."     (last-active-wins; 多个 .var 可并存)
块:    <Variant when="var">
         <Add into="#id|@root" at="end|start|N">...</Add>
       </Variant>
不可带 .var: id, 标签名, <Param default>
```

---

## 12. 实施分期建议

每个 M 是一个独立 plan + PR 节奏。本 spec 仅交付到设计；实施计划由 writing-plans 流程产出。

| 阶段 | 内容 | 验收 |
|---|---|---|
| **M1 核心** | XML parser → Tree IR；6 原语；Screen Open/Close；`Get<T>`；自定义控件注册（`[UIAttr]` + `[Bind]`）；anchor/size/margin 系统 | 跑通"主菜单 + 三按钮 + 点击事件"完整闭环 |
| **M2 模板** | `<Template>` + `<Param>` + `<Slot>` + `{{}}` 替换 + `if=` | 用 TitledPanel 包背包 Grid |
| **M3 变体** | 内联 `attr.var`；块 `<Variant>/<Add>`；运行时切换重解算 | 同一 Screen 在 mobile-portrait 与 pc 间切换 |
| **M4 可用性** | `<Import>` + 跨文件命名空间；编辑器内热重载；XSD 自动生成（IDE 补全） | 大型项目可拆文件协作 |
| **M5 生态** | 内置 ScrollList / Toggle / Slider / Dropdown 自定义控件参考实现 | 用户零代码即可用上常用控件 |

---

## 13. 风险与开放问题

| # | 风险 / 问题 | 应对 |
|---|---|---|
| R1 | XML 解析性能（启动时大量描述文件） | 缓存 IR，预制实例池；M1 末做 profiling |
| R2 | Variant 重解算开销 | 仅刷被覆盖属性，不重建 GameObject；M3 末 profiling |
| R3 | LayoutGroup 与 Variant 同时驱动可能冲突 | 子节点在 Stack 内 `anchor.var` 同样视作非法（与 §6.5 一致），编译期警告 |
| R4 | 自定义控件 Prefab 与 description 字段对齐错误 | M1 提供注册期一次性校验：检查 `[UIAttr]` 标记的属性与 prefab 是否匹配 |
| R5 | ID 路径冲突（用户在 Screen 与 Template 内用同名 id） | 路径访问天然消歧；GetAll 返回所有匹配 |
| R6 | 跨文件 Template 同名冲突且双方都未 alias | 编译期硬报错，要求其一改名或 alias |

---

_Spec 结束。下一步：用 writing-plans skill 把 M1 拆成可执行实施计划。_
