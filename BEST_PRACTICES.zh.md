# PromptUGUI 最佳实践

[English](BEST_PRACTICES.md) | [中文](BEST_PRACTICES.zh.md)

## 1. 初始化最佳实践

用 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 把解析器、缩放、主题、语言一次性配好：

```csharp
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

public static class UIBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // ① 解析器：.ui.xml / .po / SpriteSet 全部走 Addressables
        UI.UseAddressableResolver();
        UI.Locale.UseAddressableResolver();
        _ = SpriteResolverHelpers.UseAddressableSpriteSetResolver(
            new[] { "SpriteSets-Common", $"SpriteSets-{UserConfig.Language}" });

        // ② 如果是像素游戏：整数倍像素对齐缩放 + 缩放下限
        UI.DefaultScaleMode = ScaleMode.Pixel;
        UI.MinPixelScale = 1.0f;

        // ③ 载入全局模板/主题库（含 <Theme>），并设主题
        _ = UI.LoadCommonLibraryAsync("UI/Templates/DefaultTheme.ui.xml");
        UI.Theme.Set("dark");

        // ④ 用项目自定义对话框覆盖内置 MessageBox
        MessageBox.XmlSrc = "UI/Modals/MessageBox.ui.xml";

        // ⑤ 应用语言：同步返回，.po 后台异步加载
        UI.Locale.Set(UserConfig.Language);
    }
}
```
无论单机还是在线游戏，AA 都是推荐路线，方便加载和日后维护。

`Theme.Set` / `Locale.Set` / SpriteSet resolver 都是 **order-independent**：可以 fire-and-forget（`_ =`）启动加载、紧接着 `Set`，等资源加载完成会**自动重刷所有已打开的界面**。所以 boot 里无需 `await`，也不怕顺序。

**可选（`ScaleMode.Pixel`像素艺术模式）**：让 Canvas 按整数倍缩放，sprite 永远整数像素对齐。也可在XML `<Screen>` 标签里，单独写 `scale-mode="auto"`或`"pixel"`。

---

## 2. 加载、打开界面 + C# 接线

**界面用 `AssetReferenceT<TextAsset>` 槽**：Inspector 拖资源，不手敲字符串 key。

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private AssetReferenceT<TextAsset> _xml;   // Inspector 拖入 .ui.xml

    private async void Start()
    {
        await UI.LoadDocumentAsync(_xml);     // 解析 + 展开模板 + 注册；Editor 下自动热重载
        var screen = UI.Open("MainMenu");     // 实例化 GameObject，返回 IScreen

        // 接线：每个订阅都要 .AddTo(screen)
        screen.Get<Btn>("play").OnClick
              .Subscribe(_ => Game.Start())
              .AddTo(screen);

        screen.Get<Toggle>("mute").OnValueChanged
              .Subscribe(on => Audio.Mute = on)
              .AddTo(screen);
    }
}
```

**`.AddTo(screen)` 是硬规矩。** R3 订阅必须绑到 Screen 生命周期。漏了 → Close 后订阅仍存活、持有已销毁的 GameObject，下次 Open 产生幽灵回调。

**动态列表用 `BindItems` / `BindOptions`**（数据驱动，不要手动 new 子节点）：

```csharp
screen.Get<Dropdown>("quality")
      .BindOptions(Observable.Return(new[] { "Low", "Medium", "High" }))
      .AddTo(screen);

screen.Get<ScrollList>("inv").BindItems(player.Inventory, (slot, item) =>
{
    slot.Get<Text>("label").TextValue = item.Name;
}).AddTo(screen);
```

---

## 3. 主题颜色

**颜色走主题 token，不要硬编码十六进制。** 在 `<Theme>` 里定义命名色，任意 `color=` 属性按名引用；切主题时所有界面自动重新着色。

```xml
<PromptUGUI version="1">
  <Theme name="light">
    <Color name="primary"    value="#ff8800"/>
    <Color name="on-primary" value="#ffffff"/>
    <Color name="bg"         value="#f0f0f0"/>
  </Theme>
  <Theme name="dark" base="light">
    <Color name="primary" value="#cc6600"/>
    <Color name="bg"      value="#10141c"/>
    <!-- on-primary 未重定义 → 沿 base="light" 继承 -->
  </Theme>
</PromptUGUI>
```

```xml
<Image color="bg"/>
<Text  color="on-primary">开始</Text>
<Btn   color="primary">购买</Btn>
```

```csharp
UI.Theme.Set("dark");   // 运行时切换，已打开界面自动重刷
```

- 主题文件通过 `UI.LoadCommonLibraryAsync(...)`（§1）或 `<Import src="themes/main"/>` 注册。
- **token 优先于字面量**：注册了名为 `red` 的 token，`color="red"` 就解析成它。
- 单主题项目可省略 `Theme.Set`，加载后自动选中那一个。

---

## 4. SpriteSet（图标 / 图集）

**共享图标和 UI 切片建 SpriteSet**（`Create → PromptUGUI → Sprite Set`，设 `setName` + 源目录），XML 里按名引用，打包时**只含被 XML 引用到的图**（package-time pruning）：

```xml
<Icon name="Solar16Bold:Essentional, UI/Crown" color="primary" size="16x16"/>
<Image sprite="UI:Button-Small"/>
```

- `<Icon>` 只能用 `setName:icon-name` 格式
- `<Image sprite=>` 等控件：
    - **`setName:icon-name`格式** 走SpriteSet图集
    - **`ui/dialog` 格式** 走 `Resources.Load`（适合一次性 / 原型）。
- 改完跑 `Tools → PromptUGUI → Sprite → Sync Atlases` 打包引用到的图。

**AA：一个 Label 收编整组 SpriteSet。** 给 SpriteSet 资源打 Addressables label，Addressables 自动拉取依赖的 SpriteAtlas：

```csharp
// 多 label 默认 Union（并集）：通用图集 + 当前语言图集
await SpriteResolverHelpers.UseAddressableSpriteSetResolver(
    new[] { "SpriteSets-Common", $"SpriteSets-{lang}" });
```

> 一个 label 可对应多个 SpriteSet。可 `await`（无空图闪烁），也可 fire-and-forget（加载中 `<Icon>` 静默留空、下完自动重刷）。

---

## 5. 多国语言 & 字体

始终设置多国语言，PromptUGUI 支持自动翻译，多国语言是免费的。

Project 右键 → Create → PromptUGUI → Settings，设置有哪些语言和对应的字体Type 。

**源文本即 key，零键名。** XML 里写什么，什么就是 msgid；代码里用 `UI.Tr(...)` 包裹：

```xml
<Text>开始游戏</Text>                <!-- 文本本身即 msgid，自动提取 -->
<Text tr="false">{{playerName}}</Text>   <!-- 玩家名等不翻译 -->
<Btn ctx="door">Open</Btn>           <!-- ctx 给「同字不同义」消歧 -->
```

```csharp
var label = string.Format(c, UI.Tr("Total: {0:C}"), price);   // 代码里的字符串也进 .po
```

**字体走 Settings 注册的 font type，不是文件路径。** 切语言时按 locale 自动解析到对应 `TMP_FontAsset`：

```xml
<Text font="title">设置</Text>
<Text font="title" font.zh-Hans="title-cn">设置</Text>   <!-- 按语言覆盖字体 -->
```

**`.po` 走 AA是最佳实践**：执行一遍 `Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files` ，它会自动给.po打 `Locale:<locale>` label，打完后整个目录即可移出Resource目录。运行时：

```csharp
UI.Locale.Set("en");              // 同步；下载中先显示 msgid，下完自动重刷
await UI.Locale.SetAsync("en");   // 等下载 + 重刷完成（之后要立刻读 UI.Tr 时用这个）
```

> **带文字的 SpriteSet 按语言拆 label**（`SpriteSets-zh-Hans` / `SpriteSets-en`），启动时只挂当前语言那份 —— 即 §1 里的 `$"SpriteSets-{lang}"`。

---

## 6. XML 书写最佳实践

**`<Screen>`：用 `reference` + `reference.portrait` 让一份 XML 同时供横竖屏。** `reference` 是设计分辨率，CanvasScaler 切到按屏缩放，并按朝向自动锁边（W≥H 锁宽、H>W 锁高）。`portrait` / `landscape` 是库**自动跟踪**的朝向变体（见下方 Variant）。

```xml
<Screen name="MainMenu" reference="640x360" reference.portrait="360x640">
```

**正常内容始终用 `<SafeArea>` 打头，** 全屏内容比如背景图可放在SafeArea外面。

内容统一套一层 `<SafeArea>` 并给 `margin`；刘海屏会吸收这个 margin：实际margin=max(margin, 非安全区空间)。比如：在 PC 这种没安全区的设备，你写的margin生效，内容不会紧贴窗口边框；在刘海屏则部分margin自动被刘海吸收，不会让刘海显得异常大，自动根据屏幕朝向判断。

```xml
<Screen name="MainMenu" reference="640x360">
  <Image anchor="stretch" color="bg"/>      <!-- 出血底图：SafeArea 同级 -->
  <SafeArea margin="6,_,6,_">
    ...内容...
  </SafeArea>
</Screen>
```

**布局经验**：

1. **要背景就直接拿 `<Image>` 当容器**（它能放子节点，少一层）。
2. **工具栏 / 可变数量按钮用 `anchor="top-stretch"` + `childAlign` + `spacing`** —— 横跨整行、childAlign 推到一边，增减按钮不用动布局。**别**写 `anchor="top-right"` 却不给 `width`（rect 塌成 0 宽，按钮全挤一起）。
3. **等分用 stretch**：LayoutGroup 内 `width="stretch"`（`stretch*2` 加权）；自由定位用 `anchor="X-stretch"` + margin，或 `width="50%"`。

```xml
<HStack anchor="top-stretch" height="24" margin="_,6,_,_"
        spacing="4" childAlign="middle-right">
  <Btn size="22x22" sprite="UI:Button-Small">
    <Icon anchor="center" name="Solar16Bold:Settings, Fine Tuning/Settings" size="16x16"/>
  </Btn>
</HStack>
```

**复用 = `<Template>`。** 重复结构抽成模板：`<Param>` 收参、`{{var}}` 字符串替换、`<Slot/>` 收子节点。展开发生在解析期（运行时看不到模板调用，按内置标签一样用）。

```xml
<Template name="IconTab">
  <Param name="text"/>
  <Param name="icon"/>
  <Param name="isOn" default="false"/>
  <Tab text="{{text}}" icon="{{icon}}" isOn="{{isOn}}"/>
</Template>

<TabBar id="topbar" itemTemplate="IconTab">
  <IconTab text="战力" icon="Solar16Bold:Security/Shield Minimalistic" isOn="true"/>
  <IconTab text="胜率" icon="Solar16Bold:Business"/>
</TabBar>
```

**要写「行为」= Control 子类（C#）。** 模板只复用视觉/布局（无代码）；需要新组件或新交互时，继承 `Control` 重写 `OnAttached`，用 `[UIAttr]` 暴露属性。
**硬规矩：`[UIAttr]` / `[Bind]` 必须配 `[Preserve]`** —— 否则 IL2CPP（Medium+ 裁剪）下 Player 包里属性会静默失效、无报错。

```csharp
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Registry;

public sealed class Badge : Control
{
    private UnityEngine.UI.Image _img;
    public override void OnAttached()
        => _img = GameObject.GetComponent<UnityEngine.UI.Image>()
               ?? GameObject.AddComponent<UnityEngine.UI.Image>();

    [UIAttr(IsColor = true), Preserve]      // ← 两个特性都要写
    public string Color { set => _img.color = UI.Theme.Resolve(value); }
}
// UI.Registry.Register<Badge>("Badge", optionalPrefab: null);
```

**Variant：运行时切布局，不重建 GameObject。** C# 端 `UI.Variants.Set("mobile", true)` 切换；任意属性追加 `.变体名` 即覆写，切换时只重新应用属性值（订阅、引用全部存活不变）。

```xml
<VStack anchor="center" size="480x320"
        anchor.mobile="bottom-stretch"
        size.mobile="" height.mobile="400" margin.mobile="_,16,80,16">
```

- 要按变体**插入元素**用 `<Variant when="mobile"><Add into="#id">...</Add></Variant>`（无 Remove/Replace；要隐藏写 `hidden.mobile="true"`）。
- **保留变体名**：`portrait` / `landscape`（朝向，自动跟踪）和 `<locale>`（比如 `sprite.zh-Hans`）是库的保留变体，会自动设置True/False。

**`tint="linear"`：像素图全程域变色。** 把要变色的 sprite **预先按灰度绘制**（128 灰为中性），运行时用 Linear Light 混合 —— 既能压暗也能提亮，一张灰度图变出整套配色。默认的 `multiply` 只能压暗。

```xml
<Image sprite="UI:TabBar-Frame" color="primary-light" tint="linear"/>
```

**圆角 / 头像裁切用 `mask="self"`** 可用 Image 自身 sprite 当裁切形状，不让内容超出圆角边框：

```xml
<Image sprite="UI:Frame-Mask" anchor="stretch" mask="self">
  <Image id="avatar" anchor="stretch" margin="3" color="primary"/>
</Image>
```

> 💡 写完任何 `.ui.xml` 后跑一遍 lint CLI（`dotnet run --project .lint/UIXmlLint -- <file>`）：它把布局组子节点上的非法 `anchor`/`margin` 等问题升级成 error，比 Unity 的 warning 更难漏。

---

## 7. 模态对话框

**MessageBox 异步阻塞式，直接 `await` 拿结果：**

```csharp
using PromptUGUI.Application.Modals;

var r = await MessageBox.Open(UI.Tr("保存修改？"), MsgBtn.Yes | MsgBtn.No | MsgBtn.Cancel);
if (r == MsgBtn.Yes) await game.SaveAsync();
```

**自定义外观：`MessageBox.XmlSrc = "..."`（§1 里设一次）。** 模态本质就是普通 `<Screen>`，anchor / margin / Variant / locale 全照常工作。**必须满足**的前置条件（否则运行时抛异常）：

- 文件里的 **`<Screen name="...">` `name` 必须与 `XmlSrc` 逐字节相等**。

自定义 XML 需带固定 id：`text` / `title` / `ok` / `cancel` / `yes` / `no` / `close`（`icon` 可选）

**Loading 遮罩**：非交互、代码自己关，幂等：

```csharp
var loading = Loading.Open(UI.Tr("加载中..."));
try { await DoWorkAsync(); }
finally { loading.Close(); }
```

**排队 vs 叠加**（`mode` 参数）：

- `ModalMode.Popup`（默认）—— 立即叠在当前对话框上方，用于「从一个弹窗里再开确认框」。
- `ModalMode.Queued` —— 堵塞，直到等整个对话框栈清空（没有其他模态窗时）再显示；多个 Queued 按 FIFO 依次弹出，避免互相覆盖。**注意，此模式用`await`调用2次会导致死锁。**

---

## 8. 动效（可选）

**入场动画：`<Animation>` 包住元素，`on="open"` 时自动播。**

```xml
<Animation type="fadein" duration="0.3s">
  <Text>Welcome</Text>
</Animation>
```

菜单逐项错峰（v1 无 stagger 糖，写多个兄弟带递增 `delay`）：

```xml
<VStack>
  <Animation type="slidein-left" delay="0.0s"><Btn>开始</Btn></Animation>
  <Animation type="slidein-left" delay="0.05s"><Btn>设置</Btn></Animation>
  <Animation type="slidein-left" delay="0.10s"><Btn>退出</Btn></Animation>
</VStack>
```

**按钮手感**：用 preset `type="pulse"` 配 `on="click@<id>"` 做点击反馈（`<Animation>` 还支持低层 `translate`/`scale`/`rotate`/`fade` 组合 + 各种 easing）：

```xml
<Animation type="pulse" on="click@buy">
  <Btn id="buy">购买</Btn>
</Animation>
```

> C# 端可订阅 `Get<Trigger>("x").OnFire`，或 `Get<Animation>("x").Fire()` 手动触发（`on="manual"` / 重播入场动画）。
