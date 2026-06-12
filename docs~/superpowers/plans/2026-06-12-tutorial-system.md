# UI.Tutorial 新手引导系统 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 spec [`2026-06-12-tutorial-system-design.md`](../specs/2026-06-12-tutorial-system-design.md):`UI.Tutorial.Run` C# await 引导序列 + SpotlightMask 挖洞遮罩 + 手指/气泡 + 三层输入拦截(指针/ESC/Router guard)+ 断点续。

**Architecture:** overlay 走 Toast 同款骨架(`ModalDocCache.EnsureLoaded` + `UI.OpenModalScreen`,内置 XML 可整张换肤);每步是 tick 驱动状态机(WaitTarget→Active→Done),`TutorialOverlayView.LateUpdate` 驱动、EditMode 测试用 `UI.Tutorial.TickForTests(dt)` 手动驱动;Router 新增前置 guard 链(独立交付件)。

**Tech Stack:** Unity 6 uGUI、`Awaitable`/`AwaitableCompletionSource`(禁 Task)、NUnit EditMode/PlayMode、Unity MCP 跑测试、`dotnet format` lint。

**全局约定(每个 task 都适用):**

- 改完源码先 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`,再 `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认无编译错误,然后跑测试。
- `run_tests` 是异步:拿 `job_id` 后轮询 `mcp__UnityMCP__get_test_job(job_id=...)`。按类过滤用 `group_names=["类名"]`。
- 每个 task 收尾跑 lint:`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`(首次需 `dotnet restore PromptUGUI.Lint.slnx`);有 diff 就 `dotnet format whitespace/style/analyzers` 修掉。**禁止 `dotnet format analyzers --severity info`**(CLAUDE.md 列了会炸的规则)。
- 提交都在 `feat/tutorial-system` 分支;新文件记得让 Unity refresh 生成 `.meta` 后一起 `git add`。
- EditMode 测试类碰 `UI` 的,`[SetUp]`/`[TearDown]` 都要 `UI.ResetForTests()`;fake resolver 模式抄 `Tests/EditMode/Router/RouterReconcileTests.cs`。

---

## File Structure(全部新建/修改一览)

| 文件 | 动作 | 职责 |
|---|---|---|
| `Runtime/Application/Router/NavigationRejectedException.cs` | 新建 | guard 拒绝异常(`RouteException` 是 sealed,独立继承 `Exception`) |
| `Runtime/Application/UI.Router.cs` | 修改 | `AddGuard`/`RemoveGuard`/`BypassGuardsOnce`(internal)/guard 链存储 |
| `Runtime/Application/UI.Router.Reconcile.cs` | 修改 | `Open()` 顶部 `CheckGuards`;`ResetForTestsInternal` 清 guard;routed-modal ESC 加 tutorial gate |
| `Runtime/Application/UI.Modal.cs` | 修改 | `OnEscapePressed` 顶部加 tutorial gate |
| `Runtime/Application/UI.cs` | 修改 | `TryResolvePath` 拆出返回 `IControl` 的 internal 重载;`ResetForTests` 调 `Tutorial.ResetForTestsInternal()` |
| `Runtime/Application/Tutorial/SpotlightMask.cs` | 新建 | 挖洞 Graphic + `ICanvasRaycastFilter` |
| `Runtime/Application/Tutorial/TutorialPlacement.cs` | 新建 | 气泡/手指四向避让纯几何 |
| `Runtime/Application/Tutorial/TutorialClickRelay.cs` | 新建 | `IPointerClickHandler` 转发组件(TapTarget/TapAnywhere 共用) |
| `Runtime/Application/Tutorial/TutorialOverlayView.cs` | 新建 | overlay 视图 + tick 状态机(定位/跟随/推进) |
| `Runtime/Application/Tutorial/TutorialFlow.cs` | 新建 | `Step`/`Navigate`/`Advance`/`TutorialMode`/`Side` |
| `Runtime/Application/UI.Tutorial.cs` | 新建 | `Run`/`UseProgressStore`/`IsActive`/`IsBlockingInput`/`MaskColor`/`TickForTests` |
| `Runtime/Resources/PromptUGUI/Tutorial/TutorialOverlay.ui.xml` | 新建 | 可换肤视觉(mask 占位 Frame、气泡、手指) |
| `Runtime/Resources/PromptUGUI/Tutorial/finger.pxl` | 新建 | 默认手指 sprite(authoring-promptugui-pxl skill 作画) |
| `Tests/EditMode/Router/RouterGuardTests.cs` | 新建 | Task 1 |
| `Tests/EditMode/Tutorial/SpotlightMaskTests.cs` | 新建 | Task 2 |
| `Tests/EditMode/Tutorial/TutorialPlacementTests.cs` | 新建 | Task 3 |
| `Tests/EditMode/Tutorial/TutorialStepTests.cs` | 新建 | Task 5(定位/超时/推进/视觉) |
| `Tests/EditMode/Tutorial/TutorialRunTests.cs` | 新建 | Task 6(生命周期/持久化/guard) |
| `Tests/PlayMode/Tutorial/TutorialPlayTests.cs` | 新建 | Task 7(真实 EventSystem 穿透) |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 修改 | Task 8 |

类型一致性基准(后续 task 全按这套签名,不得改名):

```csharp
namespace PromptUGUI.Application
{
    public enum TutorialMode { Block, Hint }
    public enum Side { Auto, Top, Bottom, Left, Right }   // 若与既有类型撞名,改名 TutorialSide 并全计划同步

    public readonly struct Advance
    {
        internal enum Kind { Default = 0, TapTarget, TapAnywhere, When, Until }
        internal readonly Kind K;
        internal readonly Func<bool> Predicate;
        internal readonly Func<Awaitable> Condition;
        public static Advance TapTarget { get; }
        public static Advance TapAnywhere { get; }
        public static Advance When(Func<bool> predicate);
        public static Advance Until(Func<Awaitable> condition);
    }

    public sealed class TutorialFlow
    {
        public Awaitable Step(string target, string text = null,
            TutorialMode mode = TutorialMode.Block, Advance advance = default,
            Side place = Side.Auto, float padding = 8f, float timeout = -1f);
        public Awaitable Navigate(string name, RouteQuery query = null);
    }

    public static partial class UI
    {
        public static class Tutorial
        {
            public static void UseProgressStore(Func<string, int> load, Action<string, int> save);
            public static Awaitable Run(string id, Func<TutorialFlow, Awaitable> body);
            public static bool IsActive { get; }
            public static string XmlSrc { get; set; }      // = "PromptUGUI/Tutorial/TutorialOverlay.ui"
            public static int SortingOrder { get; set; }   // = 3000(> Toast 2000 > Modal 1000)
            public static string MaskColor { get; set; }   // = "#000000B0"
            internal static bool IsBlockingInput { get; }  // Active 且当前步 mode==Block
            internal static void TickForTests(float dt);
            internal static void ResetForTestsInternal();
        }
    }
}
```

---

### Task 1: Router 导航 guard(独立交付件)

**Files:**
- Create: `Runtime/Application/Router/NavigationRejectedException.cs`
- Modify: `Runtime/Application/UI.Router.cs`(guard 存储 + 公共 API)
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(`Open` 检查 + `ResetForTestsInternal` 清理)
- Test: `Tests/EditMode/Router/RouterGuardTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Router
{
    public class RouterGuardTests
    {
        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='bg' anchor='stretch'/>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            { ["home"] = Xml("home"), ["shop"] = Xml("shop") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Guard_ReturnsFalse_OpenThrows_ChainUnchanged_ChangedNotFired()
        {
            UI.Router.Open("home").GetAwaiter().GetResult();
            int changed = 0;
            UI.Router.Changed += () => changed++;
            UI.Router.AddGuard(_ => false);

            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("shop").GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Guard_ReceivesTargetName()
        {
            string seen = null;
            UI.Router.AddGuard(n => { seen = n; return true; });
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", seen);
        }

        [Test]
        public void Guard_AllTrue_NavigationProceeds()
        {
            UI.Router.AddGuard(_ => true);
            UI.Router.AddGuard(_ => true);
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", UI.Router.Current);
        }

        [Test]
        public void RemoveGuard_RestoresNavigation()
        {
            System.Func<string, bool> g = _ => false;
            UI.Router.AddGuard(g);
            UI.Router.RemoveGuard(g);
            UI.Router.Open("shop").GetAwaiter().GetResult();
            Assert.AreEqual("shop", UI.Router.Current);
        }

        [Test]
        public void Guard_AlsoBlocks_NavigateUrl()
        {
            UI.Router.AddGuard(_ => false);
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Navigate("shop").GetAwaiter().GetResult());
        }

        [Test]
        public void BypassGuardsOnce_AllowsExactlyOneNavigation()
        {
            UI.Router.AddGuard(_ => false);
            UI.Router.BypassGuardsOnce();
            UI.Router.Open("shop").GetAwaiter().GetResult();   // 放行一次
            Assert.AreEqual("shop", UI.Router.Current);
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("home").GetAwaiter().GetResult());   // 标记已复位
        }

        [Test]
        public void ResetForTests_ClearsGuardsAndBypass()
        {
            UI.Router.AddGuard(_ => false);
            UI.Router.BypassGuardsOnce();
            UI.ResetForTests();
            SetUp();   // 重建路由表
            UI.Router.Open("shop").GetAwaiter().GetResult();   // 不再被拦
            Assert.AreEqual("shop", UI.Router.Current);
        }
    }
}
```

- [ ] **Step 2: refresh + 跑测试确认失败**

`refresh_unity` 后 `read_console` 应报 CS0246(`NavigationRejectedException`/`AddGuard` 不存在)——编译失败即红。

- [ ] **Step 3: 实现**

`Runtime/Application/Router/NavigationRejectedException.cs`:

```csharp
using System;

namespace PromptUGUI.Application
{
    /// <summary>导航被 UI.Router.AddGuard 注册的 guard 拒绝。</summary>
    public sealed class NavigationRejectedException : Exception
    {
        public string RouteName { get; }
        public NavigationRejectedException(string routeName)
            : base($"navigation to '{routeName}' rejected by guard")
            => RouteName = routeName;
    }
}
```

`UI.Router.cs`(partial 内追加):

```csharp
private static readonly List<Func<string, bool>> _guards = new();
private static bool _bypassGuardsOnce;

/// <summary>导航前置守卫:任一返回 false → Open/Navigate/Back 抛 NavigationRejectedException。</summary>
public static void AddGuard(Func<string, bool> guard)
{
    if (guard == null) throw new ArgumentNullException(nameof(guard));
    _guards.Add(guard);
}

public static void RemoveGuard(Func<string, bool> guard) => _guards.Remove(guard);

/// <summary>下一次 Open 跳过整条 guard 链并复位(Tutorial 内部导航用)。</summary>
internal static void BypassGuardsOnce() => _bypassGuardsOnce = true;

private static void CheckGuards(string name)
{
    if (_bypassGuardsOnce) { _bypassGuardsOnce = false; return; }
    foreach (var g in _guards)
        if (!g(name)) throw new NavigationRejectedException(name);
}
```

`UI.Router.Reconcile.cs` 改两处:`Open` 首行加 `CheckGuards(name);`(在创建 tcs 之前,同步抛);`ResetForTestsInternal` 里加 `_guards.Clear(); _bypassGuardsOnce = false;`。

- [ ] **Step 4: refresh + 跑测试确认通过**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["RouterGuardTests"])` → 全 PASS。再跑整个 Router 目录相关组(`RouterReconcileTests` 等)确认无回归。

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Application/Router/NavigationRejectedException.cs* Runtime/Application/UI.Router.cs Runtime/Application/UI.Router.Reconcile.cs Tests/EditMode/Router/RouterGuardTests.cs*
git commit -m "feat(router): AddGuard/RemoveGuard 导航前置拒绝钩子 + NavigationRejectedException"
```

---

### Task 2: SpotlightMask 挖洞遮罩

**Files:**
- Create: `Runtime/Application/Tutorial/SpotlightMask.cs`
- Test: `Tests/EditMode/Tutorial/SpotlightMaskTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Tutorial;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Tutorial
{
    public class SpotlightMaskTests
    {
        private GameObject _canvasGo;
        private SpotlightMask _mask;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("canvas", typeof(Canvas));
            var go = new GameObject("mask", typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(800, 600);   // 本地坐标系:中心原点,±400/±300
            _mask = go.AddComponent<SpotlightMask>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_canvasGo);

        [Test]
        public void NoHole_BlocksEverywhere()
        {
            _mask.SetHole(null);
            Assert.IsTrue(_mask.HitTestForTests(Vector2.zero));
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(390, 290)));
        }

        [Test]
        public void Hole_PassesInside_BlocksOutside()
        {
            _mask.SetHole(new Rect(-50, -25, 100, 50));   // 中心 100x50 的洞
            Assert.IsFalse(_mask.HitTestForTests(Vector2.zero));            // 洞内 → 穿透
            Assert.IsFalse(_mask.HitTestForTests(new Vector2(49, 24)));     // 洞内边缘
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(51, 0)));       // 洞外
            Assert.IsTrue(_mask.HitTestForTests(new Vector2(0, 26)));
        }

        [Test]
        public void Mesh_NoHole_SingleQuad_4Verts()
        {
            _mask.SetHole(null);
            Assert.AreEqual(4, _mask.PopulateMeshVertexCountForTests());
        }

        [Test]
        public void Mesh_WithHole_FourBands_16Verts()
        {
            _mask.SetHole(new Rect(-50, -25, 100, 50));
            Assert.AreEqual(16, _mask.PopulateMeshVertexCountForTests());
        }

        [Test]
        public void Hole_ClampedToRect_DegenerateBandsSkipped()
        {
            // 洞超出整个 rect → 等效无遮挡区,但不得产出反向 quad
            _mask.SetHole(new Rect(-1000, -1000, 2000, 2000));
            Assert.AreEqual(0, _mask.PopulateMeshVertexCountForTests());
            Assert.IsFalse(_mask.HitTestForTests(Vector2.zero));
        }
    }
}
```

- [ ] **Step 2: refresh,确认编译错误(类不存在)= 红**

- [ ] **Step 3: 实现**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 引导挖洞遮罩(spec §5.2):洞外四块环形带渲染遮罩色并拦截 raycast,
    /// 洞内不渲染、IsRaycastLocationValid 返回 false → 点击穿透到下层真实控件。
    /// 不用 shader/stencil,WebGL 安全。
    /// </summary>
    internal sealed class SpotlightMask : MaskableGraphic, ICanvasRaycastFilter
    {
        private Rect? _hole;   // 本地坐标(pivot 居中)

        /// <summary>null = 无洞(整屏遮罩,纯说明页/等待目标期)。</summary>
        public void SetHole(Rect? holeInLocalSpace)
        {
            _hole = holeInLocalSpace;
            SetVerticesDirty();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPoint, eventCamera, out var local))
                return false;
            return HitTest(local);
        }

        private bool HitTest(Vector2 local) => !(_hole.HasValue && _hole.Value.Contains(local));

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (_hole == null) { AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax); return; }

            // 洞夹紧到自身 rect,四块环形带:上、下、左、右(左右带只到洞的上下沿)
            var h = _hole.Value;
            float hx0 = Mathf.Max(h.xMin, r.xMin), hx1 = Mathf.Min(h.xMax, r.xMax);
            float hy0 = Mathf.Max(h.yMin, r.yMin), hy1 = Mathf.Min(h.yMax, r.yMax);
            if (hx1 <= hx0 || hy1 <= hy0) { AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax); return; }

            AddQuad(vh, r.xMin, hy1, r.xMax, r.yMax);   // 上
            AddQuad(vh, r.xMin, r.yMin, r.xMax, hy0);   // 下
            AddQuad(vh, r.xMin, hy0, hx0, hy1);         // 左
            AddQuad(vh, hx1, hy0, r.xMax, hy1);         // 右
        }

        private void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1)
        {
            if (x1 <= x0 || y1 <= y0) return;   // 退化带跳过
            int i = vh.currentVertCount;
            var c = color;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        // —— 测试钩子 —— //
        internal bool HitTestForTests(Vector2 local) => HitTest(local);

        internal int PopulateMeshVertexCountForTests()
        {
            using var vh = new VertexHelper();
            OnPopulateMesh(vh);
            return vh.currentVertCount;
        }
    }
}
```

注意 `Hole_ClampedToRect` 测试:洞吞掉整个 rect 时四带全退化 → 0 顶点(上面 `hx1<=hx0` 分支只处理"洞与 rect 不相交"回整屏;相交但全覆盖时走四带各自退化)。核对实现与两条测试语义一致后再跑。

- [ ] **Step 4: refresh + `group_names=["SpotlightMaskTests"]` 全 PASS**

- [ ] **Step 5: lint + commit**

```bash
git add Runtime/Application/Tutorial/ Tests/EditMode/Tutorial/
git commit -m "feat(tutorial): SpotlightMask 挖洞遮罩 — 四带 mesh + ICanvasRaycastFilter 穿透"
```

---

### Task 3: TutorialPlacement 气泡/手指避让几何

**Files:**
- Create: `Runtime/Application/Tutorial/TutorialPlacement.cs`
- Test: `Tests/EditMode/Tutorial/TutorialPlacementTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialPlacementTests
    {
        private static readonly Rect Overlay = new(-960, -540, 1920, 1080);
        private static readonly Vector2 Bubble = new(300, 100);
        private const float Gap = 60f;   // 手指 + 间距占用

        [Test]
        public void Auto_TargetNearBottom_PlacesTop()
        {
            var target = new Rect(-50, -520, 100, 60);   // 贴近下边缘
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreEqual(Side.Top, r.Side);
            Assert.Greater(r.BubblePos.y, target.yMax);
        }

        [Test]
        public void Auto_TargetNearTop_PlacesBottom()
        {
            var target = new Rect(-50, 460, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreEqual(Side.Bottom, r.Side);
            Assert.Less(r.BubblePos.y, target.yMin);
        }

        [Test]
        public void Auto_TargetNearRightEdge_NeverOverflows()
        {
            var target = new Rect(900, -30, 50, 60);   // 贴右缘,Right 放不下
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreNotEqual(Side.Right, r.Side);
            // 气泡完全在 overlay 内
            Assert.GreaterOrEqual(r.BubblePos.x - Bubble.x / 2, Overlay.xMin);
            Assert.LessOrEqual(r.BubblePos.x + Bubble.x / 2, Overlay.xMax);
        }

        [Test]
        public void ExplicitPlace_Respected()
        {
            var target = new Rect(-50, -30, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Left);
            Assert.AreEqual(Side.Left, r.Side);
            Assert.Less(r.BubblePos.x, target.xMin);
        }

        [Test]
        public void FingerAngle_PointsAtTarget()
        {
            var target = new Rect(-50, -30, 100, 60);
            // 手指默认素材朝上;气泡在上方 → 手指在气泡与目标之间、旋转 180° 朝下指目标
            Assert.AreEqual(180f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Top).FingerAngle);
            Assert.AreEqual(0f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Bottom).FingerAngle);
            Assert.AreEqual(90f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Right).FingerAngle);
            Assert.AreEqual(-90f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Left).FingerAngle);
        }

        [Test]
        public void FingerPos_BetweenBubbleAndTarget()
        {
            var target = new Rect(-50, -30, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Top);
            Assert.Greater(r.FingerPos.y, target.yMax);
            Assert.Less(r.FingerPos.y, r.BubblePos.y - Bubble.y / 2);
        }
    }
}
```

- [ ] **Step 2: refresh → 编译错误 = 红**

- [ ] **Step 3: 实现**

```csharp
using UnityEngine;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 气泡+手指四向避让(spec §5.3),全部在 overlay 本地坐标(中心原点)。
    /// 纯函数,EditMode 可测。gap = 手指长度 + 间距,气泡中心 = 目标边缘 + gap + 半气泡。
    /// </summary>
    internal static class TutorialPlacement
    {
        internal readonly struct Result
        {
            public readonly Side Side;
            public readonly Vector2 BubblePos;   // 气泡中心
            public readonly Vector2 FingerPos;   // 手指中心(气泡与目标之间的 gap 中点)
            public readonly float FingerAngle;   // Z 旋转;素材默认朝上(0°=指上)
            public Result(Side s, Vector2 b, Vector2 f, float a)
            { Side = s; BubblePos = b; FingerPos = f; FingerAngle = a; }
        }

        internal static Result Choose(Rect overlay, Rect target, Vector2 bubbleSize,
            float gap, Side place)
        {
            if (place != Side.Auto) return Build(place, target, bubbleSize, gap, overlay);

            Side best = Side.Top;
            float bestScore = float.MinValue;
            foreach (var s in new[] { Side.Top, Side.Bottom, Side.Left, Side.Right })
            {
                var r = Build(s, target, bubbleSize, gap, overlay);
                var bubble = new Rect(r.BubblePos - bubbleSize / 2f, bubbleSize);
                float overflow = Overflow(bubble, overlay);
                // 零溢出里选剩余空间最大;有溢出则溢出最小
                float room = s switch
                {
                    Side.Top => overlay.yMax - target.yMax,
                    Side.Bottom => target.yMin - overlay.yMin,
                    Side.Left => target.xMin - overlay.xMin,
                    _ => overlay.xMax - target.xMax,
                };
                float score = overflow > 0f ? -1000f - overflow : room;
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return Build(best, target, bubbleSize, gap, overlay);
        }

        private static Result Build(Side s, Rect t, Vector2 b, float gap, Rect overlay)
        {
            Vector2 c = t.center, bubble, finger;
            float angle;
            switch (s)
            {
                case Side.Top:
                    bubble = new Vector2(c.x, t.yMax + gap + b.y / 2f);
                    finger = new Vector2(c.x, t.yMax + gap / 2f);
                    angle = 180f; break;
                case Side.Bottom:
                    bubble = new Vector2(c.x, t.yMin - gap - b.y / 2f);
                    finger = new Vector2(c.x, t.yMin - gap / 2f);
                    angle = 0f; break;
                case Side.Left:
                    bubble = new Vector2(t.xMin - gap - b.x / 2f, c.y);
                    finger = new Vector2(t.xMin - gap / 2f, c.y);
                    angle = -90f; break;
                default:   // Right
                    bubble = new Vector2(t.xMax + gap + b.x / 2f, c.y);
                    finger = new Vector2(t.xMax + gap / 2f, c.y);
                    angle = 90f; break;
            }
            // 沿副轴夹紧让气泡不出屏(主轴出屏由 Auto 评分排除)
            bubble.x = Mathf.Clamp(bubble.x, overlay.xMin + b.x / 2f, overlay.xMax - b.x / 2f);
            bubble.y = Mathf.Clamp(bubble.y, overlay.yMin + b.y / 2f, overlay.yMax - b.y / 2f);
            return new Result(s, bubble, finger, angle);
        }

        private static float Overflow(Rect r, Rect bounds) =>
            Mathf.Max(0f, bounds.xMin - r.xMin) + Mathf.Max(0f, r.xMax - bounds.xMax)
            + Mathf.Max(0f, bounds.yMin - r.yMin) + Mathf.Max(0f, r.yMax - bounds.yMax);
    }
}
```

手指角度约定:素材画成"指尖朝上";`Top`(气泡在上)→ 手指要朝下指目标 → 180°。与 Task 3 测试和 Task 5 视图代码一致,不得改。

- [ ] **Step 4: refresh + `group_names=["TutorialPlacementTests"]` 全 PASS**
- [ ] **Step 5: lint + commit**(`feat(tutorial): 气泡/手指四向避让纯几何`)

---

### Task 4: overlay XML + finger.pxl + TutorialClickRelay

无独立测试(资产 + 5 行组件),由 Task 5 的测试覆盖。

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Tutorial/TutorialOverlay.ui.xml`
- Create: `Runtime/Resources/PromptUGUI/Tutorial/finger.pxl`
- Create: `Runtime/Application/Tutorial/TutorialClickRelay.cs`

- [ ] **Step 1: 写 overlay XML**

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Tutorial/TutorialOverlay.ui" reference="1920x1080" reference.portrait="1080x1920">
    <!-- mask:纯容器占位,SpotlightMask 组件由 C# 挂载(遮罩色走 UI.Tutorial.MaskColor) -->
    <Frame id="mask" anchor="stretch"/>
    <!-- bubbleRoot:气泡+手指,位置由 C# 每帧摆放 -->
    <Frame id="bubbleRoot" size="0x0">
      <Image id="bubble" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round" color="#222222EE" size="300x100">
        <Text id="bubbleText" anchor="stretch" margin="16" fontSize="22" align="center"/>
      </Image>
      <Image id="finger" size="48x48"/>
    </Frame>
  </Screen>
</PromptUGUI>
```

写完跑 XML lint:`dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Tutorial/TutorialOverlay.ui.xml` → exit 0。
(`finger` 的 sprite 属性在 Step 2 画完 .pxl 后回填;9-slice sprite 名以 `Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml` 实际引用为准,先抄过来核对。)

- [ ] **Step 2: 画 finger.pxl**

调用 `authoring-promptugui-pxl` skill,要求:16x16、透明底、指尖朝上的手指/箭头形,文件 `Runtime/Resources/PromptUGUI/Tutorial/finger.pxl`;按该 skill 文档的引用方式回填 XML 的 `finger` sprite 属性。refresh 后用 `read_console` 确认 importer 无报错。

- [ ] **Step 3: TutorialClickRelay**

```csharp
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 临时挂到目标 GO(TapTarget)或 mask GO(TapAnywhere)的点击转发器,步骤结束移除。
    /// 与 GO 上既有 Button 等 IPointerClickHandler 并存(uGUI 对命中 GO 上所有 handler 逐个执行)。
    /// </summary>
    internal sealed class TutorialClickRelay : MonoBehaviour, IPointerClickHandler
    {
        internal Action OnClicked;
        public void OnPointerClick(PointerEventData _) => OnClicked?.Invoke();
        internal void FireForTests() => OnClicked?.Invoke();
    }
}
```

- [ ] **Step 4: refresh、console 无 error;lint;commit**(`feat(tutorial): overlay XML 骨架 + finger.pxl + 点击转发器`)

---

### Task 5: TutorialOverlayView + TutorialFlow.Step(tick 状态机核心)

最大的一个 task:Step 的 等待目标→显示→推进 全链路。`UI.Tutorial` 静态壳一并建立(`Run` 留到 Task 6,本 task 先提供 internal 的 `BeginSessionForTests`/`TickForTests` 钩子让 Step 可独测)。

**Files:**
- Create: `Runtime/Application/Tutorial/TutorialOverlayView.cs`
- Create: `Runtime/Application/Tutorial/TutorialFlow.cs`
- Create: `Runtime/Application/UI.Tutorial.cs`
- Modify: `Runtime/Application/UI.cs`(`TryResolvePath` 拆 internal 重载,返回 `IControl`)
- Test: `Tests/EditMode/Tutorial/TutorialStepTests.cs`

- [ ] **Step 1: 先做 `UI.cs` 小重构(无行为变化)**

把现有 `TryResolvePath(string, out RectTransform)` 的查找体抽成新 internal 重载,原方法转调:

```csharp
/// <summary>同 TryResolvePath,但回传 IControl(path==screen 名时 control=null、rect=root)。</summary>
internal static bool TryResolvePath(string path, out IControl control, out UnityEngine.RectTransform rect)
{
    control = null; rect = null;
    if (string.IsNullOrEmpty(path)) return false;
    string bestKey = null;
    foreach (var key in _open.Keys)
    {
        bool match = path == key || path.StartsWith(key + "/", System.StringComparison.Ordinal);
        if (match && (bestKey == null || key.Length > bestKey.Length)) bestKey = key;
    }
    if (bestKey == null) return false;
    var screen = _open[bestKey];
    if (path.Length == bestKey.Length)
    {
        rect = screen.RootGameObject.GetComponent<UnityEngine.RectTransform>();
        return rect != null;
    }
    string idPath = path.Substring(bestKey.Length + 1);
    try
    {
        control = screen.Get(idPath);
        rect = control?.RectTransform;
        return rect != null;
    }
    catch (System.Collections.Generic.KeyNotFoundException) { return false; }
}

internal static bool TryResolvePath(string path, out UnityEngine.RectTransform rect)
    => TryResolvePath(path, out _, out rect);
```

refresh + 跑既有 `PathResolveTests`、`ToastOverlayTests` 确认无回归,单独 commit:`refactor: TryResolvePath 拆出返回 IControl 的 internal 重载`。

- [ ] **Step 2: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialStepTests
    {
        private const string MainXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='main'>
  <Btn id='shopBtn' size='200x80'>Shop</Btn>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "main" ? MainXml : null);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TutorialFlow BeginFlow() => UI.Tutorial.BeginSessionForTests();

        // —— 等待目标出现 —— //
        [Test]
        public void Step_TargetNotResolvable_StaysPending_NoHole()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/shopBtn", text: "hi");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            Assert.IsNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);   // 等待期整屏遮罩
        }

        [Test]
        public void Step_TargetAppears_HoleOpens_AndTapTargetAdvances()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/shopBtn", text: "hi");
            UI.Tutorial.TickForTests(0.016f);

            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            UI.Tutorial.TickForTests(0.016f);

            Assert.IsNotNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);
            var relay = UI.Get("main").Get<PromptUGUI.Controls.Btn>("shopBtn")
                .GameObject.GetComponent<TutorialClickRelay>();
            Assert.IsNotNull(relay, "TapTarget 应在目标 GO 挂 relay");
            relay.FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
            Assert.IsNull(relay.OnClicked, "步骤结束 relay 应被拆除");   // 或断言组件已销毁
        }

        [Test]
        public void Step_Timeout_Throws()
        {
            var flow = BeginFlow();
            var step = flow.Step("main/missing", timeout: 1f);
            UI.Tutorial.TickForTests(0.6f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            UI.Tutorial.TickForTests(0.6f);   // 累计 1.2s > 1s
            var ex = Assert.Throws<System.TimeoutException>(() => step.GetAwaiter().GetResult());
            StringAssert.Contains("main/missing", ex.Message);
        }

        [Test]
        public void Step_TargetDestroyed_ReturnsToWaiting_HoleCloses()
        {
            var flow = BeginFlow();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            var step = flow.Step("main/shopBtn");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsNotNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);

            UI.Close("main");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            Assert.IsNull(UI.Tutorial.ViewForTests.Mask.HoleForTests);
        }

        // —— 推进方式 —— //
        [Test]
        public void Step_NullTarget_TapAnywhere_MaskClickAdvances()
        {
            var flow = BeginFlow();
            var step = flow.Step(null, text: "干得好");
            UI.Tutorial.TickForTests(0.016f);
            var relay = UI.Tutorial.ViewForTests.Mask.GetComponent<TutorialClickRelay>();
            Assert.IsNotNull(relay);
            relay.FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_AdvanceWhen_PredicatePolledPerTick()
        {
            bool flag = false;
            var flow = BeginFlow();
            var step = flow.Step(null, advance: Advance.When(() => flag));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            flag = true;
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        [Test]
        public void Step_AdvanceUntil_CompletesWithCondition()
        {
            var acs = new AwaitableCompletionSource();
            var flow = BeginFlow();
            var step = flow.Step(null, advance: Advance.Until(() => acs.Awaitable));
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(step.GetAwaiter().IsCompleted);
            acs.SetResult();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
        }

        // —— 参数校验 —— //
        [Test]
        public void Step_TapAnywhere_HintMode_Throws()
        {
            var flow = BeginFlow();
            Assert.Throws<System.ArgumentException>(() =>
                flow.Step(null, mode: TutorialMode.Hint, advance: Advance.TapAnywhere));
        }

        [Test]
        public void Step_TapTarget_NullTarget_Throws()
        {
            var flow = BeginFlow();
            Assert.Throws<System.ArgumentException>(() =>
                flow.Step(null, advance: Advance.TapTarget));
        }

        // —— Block / Hint 视觉差异 —— //
        [Test]
        public void HintMode_MaskDisabled_NotRaycastTarget()
        {
            var flow = BeginFlow();
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
            UI.Open("main");
            flow.Step("main/shopBtn", mode: TutorialMode.Hint);
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsFalse(UI.Tutorial.ViewForTests.Mask.enabled);
            Assert.IsFalse(UI.Tutorial.ViewForTests.Mask.raycastTarget);
            Assert.IsFalse(UI.Tutorial.IsBlockingInput);
        }

        [Test]
        public void BlockMode_IsBlockingInput_True_DuringStep_FalseAfter()
        {
            var flow = BeginFlow();
            var step = flow.Step(null, text: "x");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.IsBlockingInput);
            UI.Tutorial.ViewForTests.Mask.GetComponent<TutorialClickRelay>().FireForTests();
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(step.GetAwaiter().IsCompleted);
            Assert.IsFalse(UI.Tutorial.IsBlockingInput);
        }

        // —— 文案与气泡 —— //
        [Test]
        public void Step_Text_AppliedToBubble_NullText_BubbleHidden()
        {
            var flow = BeginFlow();
            flow.Step(null, text: "点这里");
            UI.Tutorial.TickForTests(0.016f);
            Assert.IsTrue(UI.Tutorial.ViewForTests.BubbleRootActiveForTests);
            Assert.AreEqual("点这里", UI.Tutorial.ViewForTests.BubbleTextForTests);
        }
    }
}
```

- [ ] **Step 3: refresh → 编译错误 = 红**

- [ ] **Step 4: 实现**

`Runtime/Application/Tutorial/TutorialFlow.cs`(enums + Advance + flow;`Step` 做参数校验/fast-forward/编号,实际执行交给 view):

```csharp
using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    public enum TutorialMode { Block, Hint }
    public enum Side { Auto, Top, Bottom, Left, Right }

    public readonly struct Advance
    {
        internal enum Kind { Default = 0, TapTargetK, TapAnywhereK, WhenK, UntilK }
        internal readonly Kind K;
        internal readonly Func<bool> Predicate;
        internal readonly Func<Awaitable> Condition;
        private Advance(Kind k, Func<bool> p, Func<Awaitable> c) { K = k; Predicate = p; Condition = c; }
        public static Advance TapTarget => new(Kind.TapTargetK, null, null);
        public static Advance TapAnywhere => new(Kind.TapAnywhereK, null, null);
        public static Advance When(Func<bool> predicate) =>
            new(Kind.WhenK, predicate ?? throw new ArgumentNullException(nameof(predicate)), null);
        public static Advance Until(Func<Awaitable> condition) =>
            new(Kind.UntilK, null, condition ?? throw new ArgumentNullException(nameof(condition)));
    }

    /// <summary>一次 UI.Tutorial.Run 的会话句柄;Step 顺序编号,支撑断点续。</summary>
    public sealed class TutorialFlow
    {
        private readonly string _id;
        private readonly int _resume;
        private readonly Action<string, int> _save;
        private int _stepIndex;
        internal Tutorial.TutorialOverlayView View;   // 懒创建(Task 5 由 BeginSessionForTests 注入)

        internal TutorialFlow(string id, int resume, Action<string, int> save)
        { _id = id; _resume = resume; _save = save; }

        public Awaitable Step(string target, string text = null,
            TutorialMode mode = TutorialMode.Block, Advance advance = default,
            Side place = Side.Auto, float padding = 8f, float timeout = -1f)
        {
            var kind = advance.K == Advance.Kind.Default
                ? (target != null ? Advance.Kind.TapTargetK : Advance.Kind.TapAnywhereK)
                : advance.K;
            if (kind == Advance.Kind.TapTargetK && target == null)
                throw new ArgumentException("Advance.TapTarget requires a target path");
            if (kind == Advance.Kind.TapAnywhereK && mode == TutorialMode.Hint)
                throw new ArgumentException("Advance.TapAnywhere requires TutorialMode.Block");

            int index = _stepIndex++;
            if (index < _resume) return AwaitableHelpers.Completed();   // fast-forward:无视觉无等待

            return RunStep(index, new StepConfig
            {
                Target = target, Text = text, Mode = mode, AdvanceKind = kind,
                Predicate = advance.Predicate, Condition = advance.Condition,
                Place = place, Padding = padding, Timeout = timeout,
            });
        }

        private async Awaitable RunStep(int index, StepConfig cfg)
        {
            var view = await UI.Tutorial.EnsureOverlay(this);
            var acs = new AwaitableCompletionSource();
            view.BeginStep(cfg, acs);
            try { await acs.Awaitable; }
            finally { view.EndStep(); }
            _save?.Invoke(_id, index + 1);
        }

        public Awaitable Navigate(string name, RouteQuery query = null)
        {
            UI.Router.BypassGuardsOnce();
            return UI.Router.Open(name, query);
        }
    }

    internal struct StepConfig
    {
        public string Target, Text;
        public TutorialMode Mode;
        public Advance.Kind AdvanceKind;
        public Func<bool> Predicate;
        public Func<Awaitable> Condition;
        public Side Place;
        public float Padding, Timeout;
    }
}
```

`Runtime/Application/Tutorial/TutorialOverlayView.cs`(状态机;只列结构与关键逻辑,执行者按此实现):

```csharp
using System;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// overlay 视图 + 每步状态机:WaitTarget(整屏遮罩,逐 tick 解析路径,累计超时)
    /// → Active(开洞、摆气泡手指、装推进监听、逐 tick 跟随/检活/轮询 When)
    /// → 完成(acs.SetResult)或超时(acs.SetException(TimeoutException))。
    /// LateUpdate 调 Tick(Time.unscaledDeltaTime);EditMode 测试经 UI.Tutorial.TickForTests 手动驱动。
    /// </summary>
    internal sealed class TutorialOverlayView : MonoBehaviour
    {
        internal SpotlightMask Mask;          // mask Frame GO 上 AddComponent
        private RectTransform _overlayRect;   // overlay 根
        private RectTransform _bubbleRoot;    // bubbleRoot Frame
        private IControl _bubble; private Text _bubbleText; private RectTransform _finger;

        private StepConfig _cfg;
        private AwaitableCompletionSource _acs;
        private bool _stepActive;             // 有进行中的步骤
        private bool _targetLive;             // Active 相
        private float _waited;
        private IControl _targetCtl; private RectTransform _targetRect;
        private TutorialClickRelay _relay;    // TapTarget 在目标 GO / TapAnywhere 在 mask GO
        private bool _untilStarted;

        internal void Init(Screen screen) { /* Get mask/bubbleRoot/bubble/bubbleText/finger 各 id,
            mask GO AddComponent<SpotlightMask>,色 = UI.Tutorial.MaskColor(用 Controls.Image.Color
            同款解析链,grep 其 setter 找解析入口),bubbleRoot 初始 SetActive(false) */ }

        internal void BeginStep(StepConfig cfg, AwaitableCompletionSource acs)
        {
            _cfg = cfg; _acs = acs; _stepActive = true; _targetLive = false;
            _waited = 0f; _untilStarted = false;
            bool block = cfg.Mode == TutorialMode.Block;
            Mask.enabled = block; Mask.raycastTarget = block;
            Mask.SetHole(null);
            ApplyBubbleText(cfg.Text);          // null → bubbleRoot inactive
            if (cfg.Target == null) EnterActive(null, null);   // 纯说明页直接 Active
        }

        internal void EndStep()
        {
            RemoveRelay(); _stepActive = false; _targetLive = false;
            Mask.SetHole(null); HideBubble();
        }

        internal void Tick(float dt)
        {
            if (!_stepActive) return;
            if (!_targetLive && _cfg.Target != null)
            {
                if (UI.TryResolvePath(_cfg.Target, out var ctl, out var rect))
                    EnterActive(ctl, rect);
                else
                {
                    _waited += dt;
                    if (_cfg.Timeout >= 0f && _waited > _cfg.Timeout)
                    { Fail(new TimeoutException($"tutorial step target '{_cfg.Target}' not found")); return; }
                }
            }
            if (_targetLive)
            {
                if (_cfg.Target != null && (_targetRect == null))   // Unity 假 null:目标被销毁
                { LeaveActive(); return; }
                if (_cfg.Target != null) UpdateVisuals();           // 逐帧跟随(洞 + 气泡 + 手指)
                if (_cfg.AdvanceKind == Advance.Kind.WhenK && _cfg.Predicate()) Complete();
            }
        }

        private void EnterActive(IControl ctl, RectTransform rect)
        {
            _targetCtl = ctl; _targetRect = rect; _targetLive = true; _waited = 0f;
            if (_cfg.AdvanceKind == Advance.Kind.TapTargetK)
                _relay = AttachRelay(rect.gameObject);
            else if (_cfg.AdvanceKind == Advance.Kind.TapAnywhereK)
                _relay = AttachRelay(Mask.gameObject);
            if (_cfg.AdvanceKind == Advance.Kind.UntilK && !_untilStarted)
            { _untilStarted = true; _ = AwaitCondition(); }
            if (_cfg.Target != null) UpdateVisuals();
            else CenterBubble();
        }

        private void LeaveActive()   // 目标销毁 → 回等待态
        { RemoveRelay(); _targetLive = false; Mask.SetHole(null); }

        private void UpdateVisuals()
        {
            var local = WorldRectToLocal(_targetRect, _overlayRect);
            var hole = new Rect(local.xMin - _cfg.Padding, local.yMin - _cfg.Padding,
                local.width + 2 * _cfg.Padding, local.height + 2 * _cfg.Padding);
            if (_cfg.Mode == TutorialMode.Block) Mask.SetHole(hole);
            var bubbleSize = ((RectTransform)_bubble.RectTransform).rect.size;
            var r = TutorialPlacement.Choose(_overlayRect.rect, local, bubbleSize,
                gap: 60f, _cfg.Place);
            /* bubbleRoot anchoredPosition = r.BubblePos(注意 bubbleRoot 锚点居中);
               finger.anchoredPosition = r.FingerPos - r.BubblePos(finger 是 bubbleRoot 子节点,转相对坐标);
               finger.localEulerAngles = (0,0,r.FingerAngle) */
        }

        private async Awaitable AwaitCondition()
        { try { await _cfg.Condition(); Complete(); } catch (Exception ex) { Fail(ex); } }

        private TutorialClickRelay AttachRelay(GameObject go)
        {
            var r = go.AddComponent<TutorialClickRelay>();
            r.OnClicked = Complete;
            return r;
        }

        private void RemoveRelay()
        {
            if (_relay == null) return;
            _relay.OnClicked = null;
            if (UnityEngine.Application.isPlaying) Destroy(_relay); else DestroyImmediate(_relay);
            _relay = null;
        }

        private void Complete() { if (_stepActive) { _stepActive = false; _acs.TrySetResult(); } }
        private void Fail(Exception ex) { if (_stepActive) { _stepActive = false; _acs.TrySetException(ex); } }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        internal static Rect WorldRectToLocal(RectTransform target, RectTransform overlayRect)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var srcCanvas = target.GetComponentInParent<Canvas>();
            Camera srcCam = srcCanvas != null ? srcCanvas.worldCamera : null;
            var dstCanvas = overlayRect.GetComponentInParent<Canvas>();
            Camera dstCam = dstCanvas != null ? dstCanvas.worldCamera : null;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(srcCam, corners[i]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, sp, dstCam, out var lp);
                min = Vector2.Min(min, lp); max = Vector2.Max(max, lp);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal bool IsBlockingStep => _stepActive && _cfg.Mode == TutorialMode.Block;
        // 测试钩子:HoleForTests(SpotlightMask 加 internal Rect? HoleForTests => _hole)、
        // BubbleRootActiveForTests、BubbleTextForTests
    }
}
```

`Runtime/Application/UI.Tutorial.cs`(本 task 版本:不含 Run,只含静态配置 + overlay 创建 + 测试钩子):

```csharp
using System;
using PromptUGUI.Application.Modals;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        public static class Tutorial
        {
            public static string XmlSrc { get; set; } = "PromptUGUI/Tutorial/TutorialOverlay.ui";
            public static int SortingOrder { get; set; } = 3000;   // > Toast(2000) > Modal(1000)
            public static string MaskColor { get; set; } = "#000000B0";

            private static Func<string, int> _load;
            private static Action<string, int> _save;
            public static void UseProgressStore(Func<string, int> load, Action<string, int> save)
            { _load = load; _save = save; }

            internal static TutorialFlow Active;            // Task 6 的 Run 设置;测试经 BeginSessionForTests
            private static TutorialOverlayView _view;
            private static string _overlayKey;

            public static bool IsActive => Active != null;
            internal static bool IsBlockingInput => _view != null && _view.IsBlockingStep;

            internal static async Awaitable<TutorialOverlayView> EnsureOverlay(TutorialFlow flow)
            {
                if (_view != null) return _view;
                await ModalDocCache.EnsureLoaded(XmlSrc);
                var (screen, key) = UI.OpenModalScreen(XmlSrc);
                _overlayKey = key;
                var canvas = screen.RootGameObject.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
                _view = screen.RootGameObject.AddComponent<TutorialOverlayView>();
                _view.Init(screen);
                return _view;
            }

            internal static void DestroyOverlay()
            {
                if (_overlayKey != null) UI.CloseModalScreen(_overlayKey);
                _overlayKey = null; _view = null;
            }

            internal static void ResetForTestsInternal()
            { Active = null; _view = null; _overlayKey = null; _load = null; _save = null; }

            // —— 测试钩子 —— //
            internal static TutorialFlow BeginSessionForTests()
            { var f = new TutorialFlow("test", 0, null); Active = f; return f; }
            internal static void TickForTests(float dt) => _view?.Tick(dt);
            internal static TutorialOverlayView ViewForTests => _view;
        }
    }
}
```

`UI.cs` 的 `ResetForTests()` 里(`Router.ResetForTestsInternal();` 旁)加 `Tutorial.ResetForTestsInternal();`。

实现注意:

- `RunStep` 里 `EnsureOverlay` 是首个 await;EditMode 测试中 `ModalDocCache.EnsureLoaded` 对 Resources 内置 XML 同步完成(Toast 测试同款前提),故 `flow.Step(...)` 返回后 view 已就绪、可立即 `TickForTests`。若 `ModalSourceLoader.LoadAsync` 对 Resources 路径有真实异步点,照 `ToastOverlayTests` 的等待方式处理。
- `Step_TargetNotResolvable_StaysPending_NoHole` 等测试在 `BeginFlow` 后第一次 Tick 前就要有 view → `BeginSessionForTests` 不创建 overlay,首个 `Step` 创建;测试第一行 `flow.Step(...)` 后 view 即存在。
- `TimeoutException` 走 `Fail` → `acs.TrySetException`;测试用 `step.GetAwaiter().GetResult()` 解包。

- [ ] **Step 5: refresh + `group_names=["TutorialStepTests"]` 全 PASS;再全量 EditMode 防回归**

- [ ] **Step 6: lint + commit**(`feat(tutorial): TutorialFlow.Step + overlay 视图 tick 状态机(定位/跟随/四式推进)`)

---

### Task 6: UI.Tutorial.Run 生命周期 + 持久化 + guard + ESC gate

**Files:**
- Modify: `Runtime/Application/UI.Tutorial.cs`(加 `Run`)
- Modify: `Runtime/Application/UI.Modal.cs`(`OnEscapePressed` 顶部 gate)
- Modify: `Runtime/Application/UI.Router.Reconcile.cs`(routed-modal ESC lambda gate)
- Test: `Tests/EditMode/Tutorial/TutorialRunTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialRunTests
    {
        private Dictionary<string, int> _store;

        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Btn id='b' size='100x40'>x</Btn>
</Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _store = new Dictionary<string, int>();
            UI.Tutorial.UseProgressStore(
                id => _store.TryGetValue(id, out var v) ? v : 0,
                (id, n) => _store[id] = n);
            var files = new Dictionary<string, string> { ["home"] = Xml("home") };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // 驱动一个 Block+TapAnywhere 步骤到完成
        private static void ClickThrough()
        {
            UI.Tutorial.TickForTests(0.016f);
            UI.Tutorial.ViewForTests.Mask
                .GetComponent<PromptUGUI.Application.Tutorial.TutorialClickRelay>().FireForTests();
            UI.Tutorial.TickForTests(0.016f);
        }

        [Test]
        public void Run_SavesProgressPerStep_AndSentinelOnFinish()
        {
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a");
                await t.Step(null, text: "b");
            });
            Assert.IsTrue(UI.Tutorial.IsActive);
            ClickThrough();
            Assert.AreEqual(1, _store["t1"]);
            ClickThrough();
            run.GetAwaiter().GetResult();
            Assert.AreEqual(int.MaxValue, _store["t1"]);
            Assert.IsFalse(UI.Tutorial.IsActive);
            Assert.IsNull(UI.Tutorial.ViewForTests);   // overlay 已销毁
        }

        [Test]
        public void Run_Resume_FastForwardsCompletedSteps()
        {
            _store["t1"] = 1;   // 第 0 步已完成
            int shown = 0;
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a"); shown++;   // fast-forward:同步完成
                Assert.AreEqual(1, shown);                 // 不等点击就到这
                await t.Step(null, text: "b"); shown++;
            });
            ClickThrough();                                // 只需推进第 1 步
            run.GetAwaiter().GetResult();
            Assert.AreEqual(2, shown);
        }

        [Test]
        public void Run_Sentinel_WholeRunInstant_NoOverlay()
        {
            _store["t1"] = int.MaxValue;
            UI.Tutorial.Run("t1", async t =>
            {
                await t.Step(null, text: "a");
                await t.Step(null, text: "b");
            }).GetAwaiter().GetResult();   // 无需任何 Tick
            Assert.IsNull(UI.Tutorial.ViewForTests, "全程 fast-forward 不应创建 overlay");
        }

        [Test]
        public void Run_BlocksRouterNavigation_AndReleasesAfter()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.Throws<NavigationRejectedException>(
                () => UI.Router.Open("home").GetAwaiter().GetResult());
            ClickThrough();
            run.GetAwaiter().GetResult();
            UI.Router.Open("home").GetAwaiter().GetResult();   // 引导结束放行
            Assert.AreEqual("home", UI.Router.Current);
        }

        [Test]
        public void Flow_Navigate_BypassesGuard()
        {
            var run = UI.Tutorial.Run("t1", async t =>
            {
                await t.Navigate("home");
                await t.Step(null, text: "a");
            });
            UI.Tutorial.TickForTests(0.016f);
            Assert.AreEqual("home", UI.Router.Current);   // 内部导航放行
            ClickThrough();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Run_BodyThrows_GuardRemoved_OverlayDestroyed()
        {
            var run = UI.Tutorial.Run("t1",
                t => throw new System.InvalidOperationException("boom"));
            Assert.Throws<System.InvalidOperationException>(() => run.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Tutorial.IsActive);
            Assert.IsNull(UI.Tutorial.ViewForTests);
            UI.Router.Open("home").GetAwaiter().GetResult();   // guard 已注销
        }

        [Test]
        public void Run_Reentry_Throws()
        {
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.Throws<System.InvalidOperationException>(
                () => UI.Tutorial.Run("t2", async t => await t.Step(null)).GetAwaiter().GetResult());
            ClickThrough();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void Run_NoStore_AlwaysFromScratch()
        {
            UI.ResetForTests();   // 清掉 store
            UI.SourceResolver = src => AwaitableHelpers.Completed<string>(null);
            var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
            Assert.IsTrue(UI.Tutorial.IsActive);   // load 缺省 0,正常显示
            ClickThrough();
            run.GetAwaiter().GetResult();          // save 缺省 no-op,不抛
        }
    }
}
```

- [ ] **Step 2: refresh → 红**(`Run` 不存在)

- [ ] **Step 3: 实现**

`UI.Tutorial.cs` 加:

```csharp
private static readonly Func<string, bool> _rejectAll = _ => false;

public static async Awaitable Run(string id, Func<TutorialFlow, Awaitable> body)
{
    if (id == null) throw new ArgumentNullException(nameof(id));
    if (body == null) throw new ArgumentNullException(nameof(body));
    if (Active != null)
        throw new InvalidOperationException("UI.Tutorial.Run: a tutorial is already running");

    int resume = _load?.Invoke(id) ?? 0;
    var flow = new TutorialFlow(id, resume, _save);
    Active = flow;
    Router.AddGuard(_rejectAll);
    try
    {
        await body(flow);
        _save?.Invoke(id, int.MaxValue);
    }
    finally
    {
        Router.RemoveGuard(_rejectAll);
        DestroyOverlay();
        Active = null;
    }
}
```

(`BeginSessionForTests` 同步改用 `new TutorialFlow("test", 0, null)` 已是如此;`TutorialFlow` 构造已带 `_save`,Task 5 的签名不变。)

ESC gate 两处:

- `UI.Modal.cs` 找到 `OnEscapePressed`(ESC 关 ad-hoc 模态的入口),方法体首行加:

```csharp
if (Tutorial.IsBlockingInput) return;   // Block 引导期间吞 ESC(spec §3)
```

- `UI.Router.Reconcile.cs` 的 routed-modal `esc.OnEscape` lambda(`if (IsTop(captured) && !UI.Modal.IsAnyOpen) _ = Back();`)改为:

```csharp
if (UI.Tutorial.IsBlockingInput) return;
if (IsTop(captured) && !UI.Modal.IsAnyOpen) _ = Back();
```

ESC gate 的测试加进 `TutorialRunTests`(此处补一条):

```csharp
[Test]
public void EscDuringBlockStep_DoesNotCloseModal()
{
    var run = UI.Tutorial.Run("t1", async t => await t.Step(null, text: "a"));
    UI.Tutorial.TickForTests(0.016f);
    var msg = MessageBox.Open("hi");   // 引导下层有个 ad-hoc 模态(不 await)
    UI.Modal.FireEscapeForTests();      // 若无此钩子,找 UI.Modal 既有测试的 ESC 触发方式(ModalEscapeListener.FireForTests)
    Assert.IsTrue(UI.Modal.IsAnyOpen, "Block 引导期间 ESC 不得关掉模态");
    ClickThrough();
    run.GetAwaiter().GetResult();
}
```

(`MessageBox.Open` 的可用性与 ESC 触发钩子以 `Tests/EditMode/Modals/` 既有测试写法为准,照抄其 SetUp;若 MessageBox 需先加载内置 XML,同样照抄。)

- [ ] **Step 4: refresh + `group_names=["TutorialRunTests"]` 全 PASS;全量 EditMode + EditorOnly 防回归**

- [ ] **Step 5: lint + commit**(`feat(tutorial): UI.Tutorial.Run 会话生命周期 — 断点续/导航锁/ESC gate`)

---

### Task 7: PlayMode 测试(真实 EventSystem 穿透与推进)

**Files:**
- Create: `Tests/PlayMode/Tutorial/TutorialPlayTests.cs`

- [ ] **Step 1: 写测试**(参考 `Tests/PlayMode/` 既有测试的 SetUp/相机/EventSystem 模板,尤其 `CarouselPlayTests` 同款骨架)

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class TutorialPlayTests
    {
        private const string MainXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='main'>
  <Btn id='target' anchor='center' size='200x80'>GO</Btn>
  <Btn id='other' anchor='top-left' size='200x80' margin='20'>NO</Btn>
</Screen></PromptUGUI>";

        // SetUp:UI.ResetForTests + resolver + EventSystem(无则建)照既有 PlayMode 测试模板

        [UnityTest]
        public IEnumerator BlockStep_RaycastBlockedOutsideHole_PassesInside()
        {
            // 1) 开 main 屏,起 Run + Step("main/target")
            // 2) 等两帧让 LateUpdate 开洞
            // 3) 用 EventSystem.RaycastAll 打"other"按钮中心的屏幕坐标 → 首个命中应是 SpotlightMask
            // 4) 打"target"中心 → 首个命中应是 target 的可点击 Graphic(穿透)
            yield return null;
        }

        [UnityTest]
        public IEnumerator TapTarget_ClickAdvances_StepCompletes()
        {
            // ExecuteEvents.Execute<IPointerClickHandler>(targetGo, pointerData, pointerClickHandler)
            // 模拟真实点击路径;等一帧,断言 step awaitable 完成、save 被调
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdvanceWhen_PredicateFlips_AdvancesNextFrame()
        {
            // 不调 TickForTests,纯靠 LateUpdate 驱动:翻 flag 后 yield 两帧,断言完成
            yield return null;
        }
    }
}
```

三条用例的注释即实现要求,补全为真实代码(屏幕坐标取 `RectTransformUtility.WorldToScreenPoint(null, rt.position)`,Overlay canvas camera 为 null)。

- [ ] **Step 2: `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["TutorialPlayTests"])` 全 PASS;全量 PlayMode 防回归**

- [ ] **Step 3: lint + commit**(`test(tutorial): PlayMode 穿透/推进用例`)

---

### Task 8: SKILL 更新 + 收尾

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: SKILL 更新(英文)**

新增两节:

1. **Tutorial(`UI.Tutorial`)**:`UseProgressStore(load, save)` 委托语义(默认每次从头);`Run(id, body)` 包裹会话 + 重入抛 `InvalidOperationException`;`Step` 全参数(target 路径 = Toast 同款 `"screenName/idPath"`、`TutorialMode.Block/Hint`、`Advance.TapTarget/TapAnywhere/When/Until` 与默认推断、`Side` 避让、`padding`、`timeout`);断点续 fast-forward 语义与 `int.MaxValue` 哨兵;`t.Navigate` 才能在引导中导航(直接 `UI.Router.Open` 会被拒);换肤(`UI.Tutorial.XmlSrc` 整张覆盖、`MaskColor`、`SortingOrder`);完整 Run 示例(抄 spec §2 用例)。
2. **Router guards**:`UI.Router.AddGuard/RemoveGuard` + `NavigationRejectedException`,独立示例(未保存修改拦 `Back`)。

- [ ] **Step 2: 终验**

按顺序全跑:

```
refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
read_console(types=["error"]) → 0 error
run_tests EditMode PromptUGUI.Tests.EditMode → 全绿
run_tests EditMode PromptUGUI.Tests.EditorOnly → 全绿
run_tests PlayMode PromptUGUI.Tests.PlayMode → 全绿
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx → exit 0
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/ → exit 0
```

- [ ] **Step 3: commit + push + PR**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): UI.Tutorial 引导 API + Router guard 条目"
git push -u origin feat/tutorial-system
gh pr create --title "feat: UI.Tutorial 新手引导系统 — 路径定位/挖洞遮罩/全屏拦截/断点续" --body "..."
```

PR body 列:spec 链接、三层拦截机制、新公共 API 面、测试数、视觉 QA 待用户(手指/气泡/遮罩观感)。

---

## Self-Review 记录

- **Spec 覆盖**:§2 API(Task 5/6)、§3 三层拦截(Task 1 guard、Task 2 挖洞、Task 6 ESC gate)、§4 guard(Task 1)、§5 视觉(Task 2/3/4/5)、§6 生命周期(Task 6)、§7 边界表(分散在 Task 5/6 测试)、§8 测试(Task 1-7)、§10 SKILL(Task 8)。spec §3 "overlay 挂 ModalEscapeListener 吞 ESC"在计划中改为 Modal 侧 gate(`IsBlockingInput` 检查)——因为各 ModalEscapeListener 独立监听全局输入,引导侧"吞"无法阻止 Modal 侧实例触发;机制变更,行为与 spec 一致,实施时在 spec §3 加一行勘误注记。
- **类型一致性**:`Advance.Kind` 成员带 `K` 后缀避免与静态属性 `TapTarget` 撞名;`TutorialFlow` 构造 `(id, resume, save)` 三参在 Task 5/6 一致;`StepConfig`/`BeginStep`/`EndStep`/`Tick`/`IsBlockingStep` 名称全计划统一。
- **已知留白(执行时按指引现场核对,非 TBD)**:MaskColor 的色值解析入口(grep `Controls.Image.Color` setter)、9-slice sprite 名(抄 MessageBox.ui.xml)、MessageBox EditMode 测试模板(抄 Tests/EditMode/Modals/)、PlayMode 模板(抄 CarouselPlayTests)、finger.pxl(authoring-promptugui-pxl skill)。
