# UI.Tutorial 新手引导系统:路径定位 + 挖洞遮罩 + 全屏输入拦截

**日期**:2026-06-12
**状态**:设计阶段(已 review 通过,待实施计划)
**作用域**:新增 `UI.Tutorial` 门面(C# await 步骤序列)、`SpotlightMask` 挖洞遮罩、`TutorialOverlay.ui.xml` 可换肤视觉层、`UI.Router.AddGuard` 导航前置拒绝钩子。不新增作者可写的 XML 元素/属性。
**关联**:定位复用 Toast 的 `"screenName/idPath"` 最长前缀匹配(`UI.TryResolvePath`,见 [`UI.Toast`] 体系);坐标转换复用 `ToastPosition.TryResolveLocalPoint` 三段式;置顶与输入拦截复用 Modal 的 `overrideSorting + sortingOrder` 手法。公开 C# API 新增 → `scripting-promptugui-csharp` 必须更新(见 §10);`authoring-promptugui-xml` 不动(overlay XML 是内部资产)。

---

## 1. 背景与目标

新手引导是手游标配,但每个项目都要重新手搓"遮罩 + 手指 + 拦输入 + 等控件出现"这套脏活。PromptUGUI 已具备三块关键地基:

1. **全局控件路径**:`UI.TryResolvePath("screenName/idPath")`(UI.cs)对开屏列表做最长前缀匹配,再经 `Screen.Get` 的 ScopedIds 递归定位——Toast 的 `At(controlPath)` 已在用;
2. **跨 Canvas 坐标转换**:`ToastPosition.TryResolveLocalPoint` 的 世界坐标 → 屏幕坐标 → overlay 本地坐标 三段式;
3. **置顶输入拦截**:Modal 的 overlay Canvas `overrideSorting + sortingOrder` + 全屏 raycast Image。

缺口只有两处:**挖洞穿透**(只许点目标)与 **deeplink/导航拒绝钩子**(Router 目前只有 `onEnter` 后置钩子)。

v1 目标(用户确认):核心(挖洞遮罩+目标穿透、手指+气泡、全屏拦截+Router guard、等待目标出现、resize/移动跟随) + 进度持久化委托 + 弱引导档位(Hint) + 气泡自动避让。**非目标**见 §9。

## 2. C# API

`Runtime/Application/UI.Tutorial.cs` + `Runtime/Application/Tutorial/TutorialFlow.cs`:

```csharp
public static partial class UI
{
    public static class Tutorial
    {
        // 持久化委托(同 SourceResolver 哲学:存哪是调用方的事;不注册 = 每次从头)
        public static void UseProgressStore(Func<string, int> load, Action<string, int> save);

        // 一段引导 = 一次 Run;id 用于持久化断点;body 内一步一 await
        public static Awaitable Run(string id, Func<TutorialFlow, Awaitable> body);

        public static bool IsActive { get; }
    }
}

public sealed class TutorialFlow
{
    public Awaitable Step(
        string target,                        // "screenName/idPath";null = 纯说明页(无洞、无手指指向)
        string text = null,                   // 气泡文案,走现有 i18n;null = 不显示气泡
        TutorialMode mode = TutorialMode.Block,   // Block 强引导 / Hint 弱引导
        Advance advance = default,            // 默认:target!=null → TapTarget;target==null → TapAnywhere
        Side place = Side.Auto,               // 气泡+手指方位:Auto 避让 / Top / Bottom / Left / Right
        float padding = 8f,                   // 挖洞相对目标 rect 的外扩(设计单位)
        float timeout = -1f);                 // 等目标出现的上限秒数;-1 = 无限

    // 脚本内发起导航(带 bypass 标记,guard 放行)
    public Awaitable Navigate(string name, RouteQuery query = null);
}

public enum TutorialMode { Block, Hint }
public enum Side { Auto, Top, Bottom, Left, Right }

public readonly struct Advance
{
    public static Advance TapTarget { get; }                    // 点击目标控件本体
    public static Advance TapAnywhere { get; }                  // 点任意处
    public static Advance When(Func<bool> predicate);           // 逐帧轮询谓词
    public static Advance Until(Func<Awaitable> condition);     // await 外部条件
}
```

用法示例:

```csharp
UI.Tutorial.UseProgressStore(id => PlayerPrefs.GetInt("tut_" + id, 0),
                             (id, n) => PlayerPrefs.SetInt("tut_" + id, n));

await UI.Tutorial.Run("first-purchase", async t =>
{
    await t.Step("main/shopBtn", text: "点这里进商店");
    await t.Step("shop/items/0/buyBtn", text: "买下它");
    await t.Step(null, text: "做得好！", advance: Advance.TapAnywhere);
    await t.Step("main/bagBtn", text: "去背包看看", mode: TutorialMode.Hint,
                 advance: Advance.When(() => bagOpened));
});
```

设计要点:

- **`Run(id, body)` 包裹式会话**:setup(创建 overlay、注册 guard、置 `IsActive`)与 teardown(销毁 overlay、注销 guard)在 try/finally 中,body 抛异常也保证清理。重入保护:`Run` 嵌套/并发调用直接抛 `InvalidOperationException`(引导天然全局独占)。
- **断点续(fast-forward)**:`Run` 开始时 `load(id)` 取得已完成步数 `n`;`TutorialFlow` 内部维护步序号,序号 < n 的 `Step` **瞬时完成、不显示任何视觉**;每步真实完成后 `save(id, 序号+1)`。整段跑完后 `save(id, int.MaxValue)` 哨兵,下次 `Run` 直接整段瞬过。把游戏状态推进到断点对应场景是脚本自己的事——fast-forward 的 `Step` 不等待目标出现、不校验路径,脚本应在步骤间用 `t.Navigate` 等幂等操作铺路(深链 reconcile 保证"已在目标页时 Navigate 是 no-op")。
- **`Advance.When` 轮询**:每帧检查(overlay view 的 Update),谓词为真即推进。不用 R3 依赖,保持 Runtime 零三方。
- **取消**:v1 不提供 `CancellationToken`(无跳过按钮,引导只能走完或进程退出;退出后靠持久化续)。

## 3. 输入拦截(Block 模式)

三层封锁,`Run` 激活、Block 步骤期间生效:

1. **指针**:overlay Canvas `overrideSorting = true`,`sortingOrder = TutorialSortingOrderBase`(常量,取值高于 `UI.Modal.SortingOrderBase + 合理栈深`,如 Modal 基数 + 1000)。全屏 `SpotlightMask` 是 raycast target,吃掉一切指针事件——除洞内。
2. **挖洞穿透**:`SpotlightMask : MaskableGraphic, ICanvasRaycastFilter`,`IsRaycastLocationValid` 在洞矩形(目标 rect + padding,overlay 本地坐标)内返回 **false** → raycast 穿透到下层真实控件。`Advance.TapTarget` 因此不需要任何 hack:直接订阅目标控件自身的点击(`Btn.OnClick`;非 Btn 目标退化为在目标 GameObject 上临时挂 `IPointerClickHandler` 监听组件,步骤结束移除)。
3. **导航/deeplink**:新增 `UI.Router.AddGuard` / `RemoveGuard`(§4)。引导注册"全拒"guard;`t.Navigate` 内部置 bypass 标记后调 `UI.Router.Open`,guard 检查标记放行。
4. **ESC/手柄**:overlay 根挂 `ModalEscapeListener` 同款双轨监听(New Input System / Legacy),Block 步骤期间吞掉 escape 不做任何事(防止 ESC 关掉引导底下的 Modal)。

Hint 模式:`SpotlightMask.enabled = false` + `raycastTarget = false`,ESC 监听不吞,guard 仍注册(整段 `Run` 期间导航锁定保持一致——弱引导步骤玩家可以无视提示,但不能 deeplink 跳走打断脚本时序)。

**纯说明页(`target == null`)+ TapAnywhere**:无洞,整屏 mask 可点,点击即推进。

## 4. `UI.Router.AddGuard`(独立交付件)

```csharp
public static partial class UI
{
    public static partial class Router
    {
        // 返回 false → 本次 Open/Navigate 立即失败(不改栈、不触发 Changed)
        public static void AddGuard(Func<string /*name*/, bool> guard);
        public static void RemoveGuard(Func<string, bool> guard);
    }
}
```

- `Open(name, query)` 与 `Navigate(url)`(解析后)在 reconcile **之前**逐个调用 guard,任一返回 false → 抛 `NavigationRejectedException`(不静默:调用方能区分"被拦"与"成功",与 reconcile 失败回滚的既有错误语义一致)。
- guard 列表为静态 List,`ResetForTests` 清空。
- 引导的 bypass:`TutorialFlow.Navigate` 在调用前设置内部静态标志(`Router.BypassGuardsOnce` internal),guard 链检查前若标志置位则跳过整链并复位。单线程主循环,无并发问题。
- 独立价值:调用方可用它做"有未保存修改时拦截返回"等场景,写入 C# SKILL。

## 5. 视觉层

### 5.1 TutorialOverlay.ui.xml(内置、可换肤)

`Runtime/Resources/PromptUGUI/Tutorial/TutorialOverlay.ui.xml`,克隆 Toast/LoadingOverlay 的骨架与加载机制(`TutorialOverlayView.XmlSrc` 静态属性可整张替换):

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Tutorial/TutorialOverlay.ui" reference="1920x1080" reference.portrait="1080x1920">
    <!-- mask: 纯 RectTransform 容器(Frame 无 Graphic);C# 在其 GameObject 挂 SpotlightMask + 应用 MaskColor -->
    <Frame id="mask" anchor="stretch"/>
    <!-- bubbleRoot: 气泡+手指容器,锚点居中(对齐 TutorialPlacement 中心原点坐标系);C# 每帧摆位 -->
    <Frame id="bubbleRoot" anchor="center" size="0x0">
      <Image id="bubble" anchor="center" size="300x100"
             sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round" color="#222222EE">
        <Text id="bubbleText" anchor="stretch" margin="16" fontSize="22" align="center" color="white"/>
      </Image>
      <!-- finger: 复用内置 caret(本身朝下);视图按 FingerAngle+180 旋转使其指向目标 -->
      <Image id="finger" anchor="center" size="48x48"
             sprite="PromptUGUI/Defaults/pugui.png#pugui_caret"/>
    </Frame>
  </Screen>
</PromptUGUI>
```

- 遮罩颜色、气泡皮肤、手指 sprite 都在 XML 里,用户照 Modal 的老办法整张覆盖换肤。
- 手指默认资产复用内置 caret 子精灵 `PromptUGUI/Defaults/pugui.png#pugui_caret`(本身朝下的下拉箭头);`.pxl` 需宿主工程 SpriteSet + Sync Atlases 工具,无法零配置作为内置资产从包 Resources 运行时加载,故不用。四方位通过旋转同一张 sprite 实现,换肤仍是"整张覆盖 overlay XML"。
- `SpotlightMask` 组件不是 XML 控件(无作者可写面),由 `TutorialOverlayView` 在 `mask` 节点的 GameObject 上 AddComponent 并接管渲染;遮罩色取 XML 里 mask 节点的 color 属性约定(读 `Frame` 背景色,缺省 `#000000B0`)。

### 5.2 SpotlightMask

`Runtime/Application/Tutorial/SpotlightMask.cs`:

- `MaskableGraphic` 子类,`OnPopulateMesh` 生成洞外四块 quad(上/下/左/右环形带);洞矩形为空(纯说明页/无目标)时退化为整屏单 quad。不用 shader/stencil,WebGL 安全。
- `ICanvasRaycastFilter.IsRaycastLocationValid`:洞内 → false(穿透),洞外 → true(拦截)。
- 公开 `SetHole(Rect? holeInLocalSpace)`,变更时 `SetVerticesDirty()`。
- v1 洞为矩形(含 padding),不做圆角/圆形——遮罩半透明,边角形状感知度低。

### 5.3 定位、跟随与避让

`Runtime/Application/Tutorial/TutorialTargetLocator.cs` + `TutorialOverlayView.cs`:

- **等待目标出现**:`Step` 进入时逐帧尝试 `UI.TryResolvePath(target)`,成功且 `GameObject != null` 才开始显示;超过 `timeout` 抛 `TimeoutException`(-1 无限等)。覆盖异步开屏、BindItems 列表项晚到等场景。
- **逐帧跟随**:`TutorialOverlayView.LateUpdate` 每帧重算目标 rect → overlay 本地坐标(ToastPosition 三段式:`TransformPoint` 世界坐标 → `WorldToScreenPoint` → `ScreenPointToLocalPointInRectangle`,取 rect 四角而非中心点),更新洞与气泡/手指。resize、ReSolve、Variant 切换、目标自身动画全自动跟上,无需订阅事件。
- **失效重等**:目标 GameObject 中途被销毁(BindItems 重建等)→ 退回"等待目标出现"状态重新解析路径;期间洞收起(整屏遮罩),气泡保留。
- **气泡避让(`Side.Auto`)**:在 Top/Bottom/Left/Right 四向中,选目标与 overlay 边界之间剩余空间最大的一侧。手指贴在气泡与目标之间,按方位旋转(Top→手指朝下,依此类推)。`place` 显式指定则跳过计算。
  - 实现勘误(2026-06-12):气泡中心在每侧均做双轴夹紧,保证恒不越过全屏 overlay,因此"溢出量"对任意现实输入恒为 0 —— 选边判据退化为单一的"剩余空间最大",不再需要原设想的"溢出为零优先 / 全溢出取最小"二级评分(那条分支在夹紧前提下永不可达,已从代码移除)。
- **纯说明页**:无洞;气泡屏幕居中,手指隐藏。

## 6. 生命周期与时序

```
UI.Tutorial.Run(id, body)
  ├─ 重入检查 → load(id) 得 fastForwardCount
  ├─ 创建 overlay Screen(LoadDocument 内置 XML)、AddComponent SpotlightMask、注册 Router guard
  ├─ await body(flow)
  │    └─ flow.Step(...) × N:
  │         序号 < fastForwardCount → 立即返回
  │         否则: await 目标可解析(timeout) → 显示洞/气泡/手指
  │                → await advance 条件 → 隐藏视觉 → save(id, 序号+1)
  ├─ save(id, int.MaxValue)
  └─ finally: 注销 guard、销毁 overlay Screen
```

- overlay Screen 不进 `_open` 字典(同 Toast/LoadingOverlay 惯例),不参与路径解析、不被 Router reconcile 触碰。
- `Screen.Close()` 的 EditMode `DestroyImmediate` 分支照常生效,EditMode 测试可同步驱动。

## 7. 边界与防御

| 场景 | 行为 |
|---|---|
| `Run` 嵌套/并发 | 抛 `InvalidOperationException` |
| 目标路径永不出现且 `timeout=-1` | 永久等待(脚本作者责任);建议测试/开发期传有限 timeout |
| 目标中途销毁 | 退回等待态重新解析(§5.3) |
| 步骤期间横竖屏切换 | LateUpdate 逐帧跟随,洞/气泡自动重排;Variant ReSolve 不影响 overlay(独立 Screen) |
| 引导期间代码直接调 `UI.Router.Open`(非 `t.Navigate`) | guard 拒绝,抛 `NavigationRejectedException` |
| Block 步骤压在已开 Modal 之上 | sortingOrder 更高,Modal 不可点;ESC 被引导吞掉,Modal 不会被误关 |
| 未注册 ProgressStore | load 恒 0、save no-op,每次从头 |
| TapTarget 但目标非 Btn | 临时挂 `IPointerClickHandler` 监听组件,步骤结束移除 |

## 8. 测试

EditMode(`UI.ResetForTests` + fake resolver 模式,仿 `DocumentLoaderTests`):

1. **Guard**:AddGuard 返回 false → `Open` 抛 `NavigationRejectedException` 且栈不变、`Changed` 不触发;RemoveGuard 后恢复;bypass 标记放行一次并复位。
2. **fast-forward**:store 返回 n → 前 n 个 Step 瞬时完成不创建视觉;每步完成 save 序号;整段完成 save 哨兵。
3. **Locator**:路径不可解析时挂起;注入 Screen 后解析成功;timeout 抛 `TimeoutException`;目标销毁后退回等待态。
4. **避让几何**:给定目标 rect 与 overlay 尺寸,四向选择正确;`place` 覆盖生效;纯说明页居中。
5. **SpotlightMask**:`SetHole` 后 `IsRaycastLocationValid` 洞内 false / 洞外 true;洞为 null 整屏 true;mesh 顶点数符合四象限/单 quad 预期。
6. **Run 生命周期**:body 抛异常 → guard 已注销、overlay 已销毁;重入抛异常。

PlayMode(仿 `CarouselPlayTests`):

7. 真实 EventSystem 下,Block 步骤点击洞外控件无响应、点击洞内目标 Btn 触发 OnClick 且步骤推进;
8. `Advance.When` 谓词翻真后下一帧推进。

## 9. 非目标(v1)

- 跳过按钮 / `CancellationToken` 中途取消(用户明确不选;持久化已覆盖"杀进程续"诉求)。
- XML 数据驱动的 `.tutorial.xml` 步骤描述(将来可作为薄层叠加在 `TutorialFlow` 之上)。
- 圆形/圆角洞、洞边缘描边动画、手指点击动效(换肤层将来可加)。
- 文案气泡富文本(纯 `<Text>`;要图文混排等 `<Markdown>` 集成另议)。
- 多段引导编排/依赖图(脚本层面自己 if/else)。

## 10. SKILL 更新

- `scripting-promptugui-csharp/SKILL.md`:新增「Tutorial 新手引导」一节(`UseProgressStore` / `Run` / `Step` 签名、`Advance` 四式、Block/Hint、`t.Navigate` 与 guard 的关系、断点续语义),以及 `UI.Router.AddGuard/RemoveGuard` 条目(独立用途示例:未保存拦返回)。
- `authoring-promptugui-xml`:不动(overlay XML 非作者可写面,无新元素/属性)。
