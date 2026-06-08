# Toast 提示文字系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增独立于模态体系的 `UI.Toast` 子系统——屏幕上短暂浮现、按文本长度自动淡出自销毁、可视觉堆叠的纯文本提示（图文混排），无边框无焦点不挡输入。

**Architecture:** 克隆 `LoadingOverlay` 的"独立子系统 + 自有 materialize pump + epoch teardown"骨架（`ToastOverlay` 静态管理器），每条 toast 一份 Screen（复用 `UI.OpenModalScreen`/`CloseModalScreen` + `ModalDocCache`），加上模态/Loading 都没有的逐帧时序（`ToastView : UIBehaviour` 跑淡入淡出+计时+位置回收）。纯逻辑（时长公式 `ToastDuration`、堆叠偏移 `ToastStack`、定位解析 `ToastPosition`、路径解析 `UI.TryResolvePath`）抽成不依赖逐帧的单元走 EditMode；真正依赖时间推进的走 PlayMode。

**Tech Stack:** Unity 6 uGUI + TMP；`UnityEngine.Awaitable`（禁用 .NET Threading）；R3 不涉及；测试 NUnit（EditMode）+ `[UnityTest]`（PlayMode），经 UnityMCP 运行。

**设计来源**：`docs~/superpowers/specs/2026-06-08-toast-tip-system-design.md`（spec §编号在下文引用）。

---

## File Structure

**新建（`PromptUGUI.Runtime` asmdef，无需改 asmdef——`Runtime/` 下新文件自动纳入）：**

- `Runtime/Application/Toasts/ToastStackMode.cs` — `enum { Default, Stacked, Sequential }`
- `Runtime/Application/Toasts/ToastDuration.cs` — 纯静态：`Compute(text, override, knobs) → hold`
- `Runtime/Application/Toasts/ToastStack.cs` — 纯静态：高度序列 → 各目标偏移
- `Runtime/Application/Toasts/ToastPosition.cs` — public readonly struct：四来源 + `TryResolve` + `GroupKey` + Vector2 隐式
- `Runtime/Application/Toasts/ToastView.cs` — `internal UIBehaviour`：淡入淡出状态机 + 计时 + 位置 lerp
- `Runtime/Application/Toasts/ToastOverlay.cs` — `internal static`：materialize pump + 分组 + 准入队列 + 布局重算 + teardown
- `Runtime/Application/UI.Toast.cs` — `UI` 的 partial 嵌套静态类：`Show` 三重载 + 全局旋钮
- `Runtime/Resources/PromptUGUI/Toast.ui.xml` — 内置默认模板（裸 `<Text id="text">`）

**修改：**

- `Runtime/Application/UI.cs` — 新增 internal `TryResolvePath`；teardown 链（`ResetForTests` + `UnloadAll`）挂 `ToastOverlay.CancelAllForTeardown`
- `.claude/skills/scripting-promptugui-csharp/SKILL.md` — 新增 "Toast" 节

**测试新建：**

- `Tests/EditMode/Toast/ToastDurationTests.cs`
- `Tests/EditMode/Toast/ToastStackTests.cs`
- `Tests/EditMode/Toast/PathResolveTests.cs`
- `Tests/EditMode/Toast/ToastPositionTests.cs`
- `Tests/EditMode/Toast/ToastOverlayTests.cs`（准入 + 定位应用 + facade 缺省）
- `Tests/PlayMode/Toast/ToastLifecyclePlayModeTests.cs`

> **每个 Task 末尾通用收尾**（除非该 Task 全是 `.cs`-only 改动）：
> 1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
> 2. `mcp__UnityMCP__read_console(action="get", types=["error"])` — 必须无编译错误
> 3. lint：`cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx`（首次需先 `dotnet restore PromptUGUI.Lint.slnx`）
> 4. commit（在分支 `feat/toast-tip-system`，`git add` 含新生成的 `.meta`）。commit message 末尾加：
>    `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## Task 1: 纯逻辑 — ToastStackMode + ToastDuration

**Files:**
- Create: `Runtime/Application/Toasts/ToastStackMode.cs`
- Create: `Runtime/Application/Toasts/ToastDuration.cs`
- Test: `Tests/EditMode/Toast/ToastDurationTests.cs`

- [ ] **Step 1: 写枚举（无需测试）**

`Runtime/Application/Toasts/ToastStackMode.cs`：

```csharp
namespace PromptUGUI.Application.Toasts
{
    /// <summary>Toast 的堆叠行为，由每次 <see cref="UI.Toast.Show"/> 选择。</summary>
    public enum ToastStackMode
    {
        /// <summary>“继承全局 <see cref="UI.Toast.DefaultStackMode"/>” 的哨兵，仅作 Show 参数缺省值用。</summary>
        Default = 0,

        /// <summary>立刻浮现，旧的被顶离基准锚点，多条共存。</summary>
        Stacked = 1,

        /// <summary>排队，等当前可见 toast 全部消失后才单独浮现（FIFO）。</summary>
        Sequential = 2,
    }
}
```

- [ ] **Step 2: 写失败测试**

`Tests/EditMode/Toast/ToastDurationTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Toasts;

namespace PromptUGUI.Tests.Toast
{
    public class ToastDurationTests
    {
        // 固定旋钮：base=1.0, perChar=0.06, min=1.5, max=5.0
        private static float Compute(string text, float over = 0f)
            => ToastDuration.Compute(text, over, 1.0f, 0.06f, 1.5f, 5.0f);

        [Test]
        public void Short_text_clamped_to_min()
            => Assert.AreEqual(1.5f, Compute("hi"), 1e-4f);   // 1.0+2*0.06=1.12 → min 1.5

        [Test]
        public void Scales_with_length()
            => Assert.AreEqual(2.8f, Compute(new string('x', 30)), 1e-4f);  // 1.0+30*0.06=2.8

        [Test]
        public void Long_text_clamped_to_max()
            => Assert.AreEqual(5.0f, Compute(new string('x', 200)), 1e-4f); // 1.0+12=13 → max 5

        [Test]
        public void Explicit_override_wins()
            => Assert.AreEqual(3.0f, Compute(new string('x', 200), 3.0f), 1e-4f);

        [Test]
        public void Null_text_is_min()
            => Assert.AreEqual(1.5f, Compute(null), 1e-4f);
    }
}
```

- [ ] **Step 3: 运行确认失败**

`mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` 然后 `mcp__UnityMCP__read_console(action="get", types=["error"])`。
Expected: 编译错误 `ToastDuration` 不存在。

- [ ] **Step 4: 实现**

`Runtime/Application/Toasts/ToastDuration.cs`：

```csharp
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>停留时长公式（纯函数，无逐帧依赖）。hold = clamp(min, base + 字数*perChar, max)。</summary>
    internal static class ToastDuration
    {
        internal static float Compute(string text, float holdOverride,
            float baseSec, float perChar, float min, float max)
        {
            if (holdOverride > 0f) return holdOverride;
            int chars = text?.Length ?? 0;     // 原始字符串长度作代理（<sprite> 标记会略拉长，可接受）
            return Mathf.Clamp(baseSec + chars * perChar, min, max);
        }
    }
}
```

- [ ] **Step 5: 运行确认通过**

`mcp__UnityMCP__refresh_unity(...)` → `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="ToastDurationTests")`。
Expected: 5 passed。

- [ ] **Step 6: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add Runtime/Application/Toasts/ToastStackMode.cs Runtime/Application/Toasts/ToastDuration.cs \
        Tests/EditMode/Toast/ToastDurationTests.cs
git add -A Runtime/Application/Toasts Tests/EditMode/Toast   # 含新生成 .meta
git commit -m "feat(toast): ToastStackMode + ToastDuration 时长公式（纯逻辑）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: 纯逻辑 — ToastStack 堆叠偏移

**Files:**
- Create: `Runtime/Application/Toasts/ToastStack.cs`
- Test: `Tests/EditMode/Toast/ToastStackTests.cs`

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Toast/ToastStackTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Toasts;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastStackTests
    {
        // heights 按到达顺序 oldest→newest。newest（末位）落基准，旧的沿 dir 被顶开。
        [Test]
        public void Single_sits_at_base()
        {
            var t = ToastStack.ComputeTargets(new[] { 40f }, 10f, Vector2.up, new Vector2(0, 5));
            Assert.AreEqual(new Vector2(0, 5), t[0]);
        }

        [Test]
        public void Newer_pushes_older_up()
        {
            // i0 oldest=30, i1=50, i2 newest=100; spacing=10; dir=+y; base=(0,0)
            var t = ToastStack.ComputeTargets(new[] { 30f, 50f, 100f }, 10f, Vector2.up, Vector2.zero);
            Assert.AreEqual(new Vector2(0, 0), t[2]);     // newest at base
            Assert.AreEqual(new Vector2(0, 110), t[1]);   // +100+10
            Assert.AreEqual(new Vector2(0, 170), t[0]);   // +110+50+10
        }

        [Test]
        public void Direction_down_for_top_group()
        {
            var t = ToastStack.ComputeTargets(new[] { 40f, 60f }, 10f, Vector2.down, new Vector2(0, -20));
            Assert.AreEqual(new Vector2(0, -20), t[1]);   // newest at base
            Assert.AreEqual(new Vector2(0, -90), t[0]);   // base + down*(60+10)
        }

        [Test]
        public void Empty_returns_empty()
            => Assert.AreEqual(0, ToastStack.ComputeTargets(new float[0], 10f, Vector2.up, Vector2.zero).Length);
    }
}
```

- [ ] **Step 2: 运行确认失败** — `refresh_unity` → `read_console`。Expected: `ToastStack` 不存在。

- [ ] **Step 3: 实现**

`Runtime/Application/Toasts/ToastStack.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// 一组同位置 toast 的目标偏移（纯函数）。heights 按到达顺序 oldest→newest：
    /// newest 落基准 basePos，每条旧的沿 dir 被顶开“所有比它新的高度+spacing”之和。
    /// </summary>
    internal static class ToastStack
    {
        internal static Vector2[] ComputeTargets(
            IReadOnlyList<float> heights, float spacing, Vector2 dir, Vector2 basePos)
        {
            int n = heights.Count;
            var result = new Vector2[n];
            float cum = 0f;
            for (int i = n - 1; i >= 0; i--)   // 从 newest 往回累加
            {
                result[i] = basePos + dir * cum;
                cum += heights[i] + spacing;
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: 运行确认通过** — `run_tests(... filter="ToastStackTests")`。Expected: 4 passed。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/Toasts Tests/EditMode/Toast
git commit -m "feat(toast): ToastStack 堆叠偏移计算（纯逻辑）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: UI.TryResolvePath — 控件路径解析

**Files:**
- Modify: `Runtime/Application/UI.cs`（在 `Get(string screenName)`（约 778 行）下方新增 internal 方法）
- Test: `Tests/EditMode/Toast/PathResolveTests.cs`

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Toast/PathResolveTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class PathResolveTests
    {
        private const string ScreenXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='Hud'>
    <Frame id='panel' anchor='center' size='100x100'>
      <Image id='coin' anchor='center' size='20x20'/>
    </Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "Hud" ? ScreenXml : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Resolves_screen_and_idpath()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsTrue(UI.TryResolvePath("Hud/panel/coin", out var rt));
            Assert.AreEqual("coin", rt.gameObject.name);   // ScreenInstantiator names GO by id
        }

        [Test]
        public void Empty_idpath_returns_screen_root()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsTrue(UI.TryResolvePath("Hud", out var rt));
            Assert.AreEqual(UI.Get("Hud").RootGameObject, rt.gameObject);
        }

        [Test]
        public void Missing_screen_returns_false()
        {
            Assert.IsFalse(UI.TryResolvePath("Nope/x", out var rt));
            Assert.IsNull(rt);
        }

        [Test]
        public void Missing_id_returns_false()
        {
            UI.LoadDocumentAsync("Hud").GetAwaiter().GetResult();
            UI.Open("Hud");
            Assert.IsFalse(UI.TryResolvePath("Hud/nope", out _));
        }
    }
}
```

> 注：`rt.gameObject.name` 等于控件 id —— `ScreenInstantiator` 用 id 命名 GameObject（与既有约定一致；若实测不符则改断言为 `UI.Get("Hud").Get("panel/coin").RectTransform == rt`）。

- [ ] **Step 2: 运行确认失败** — `refresh_unity` → `read_console`。Expected: `TryResolvePath` 不存在。

- [ ] **Step 3: 实现** —— 在 `Runtime/Application/UI.cs` 的 `public static Screen Get(string screenName)`（约 778-779 行）正下方插入：

```csharp
        /// <summary>
        /// 解析 "&lt;screenName&gt;/&lt;idPath&gt;" → 控件 RectTransform。screen 名按 _open 实际注册键
        /// 做“最长前缀匹配”（screen 名本身可含斜杠，故不能数斜杠），其后 / 起为 id-path。
        /// idPath 为空 → 该 Screen root。任一步未命中 → false（Toast 控件定位据此退回默认位）。
        /// </summary>
        internal static bool TryResolvePath(string path, out RectTransform rect)
        {
            rect = null;
            if (string.IsNullOrEmpty(path)) return false;

            string bestKey = null;
            foreach (var key in _open.Keys)
            {
                bool match = path == key
                    || path.StartsWith(key + "/", System.StringComparison.Ordinal);
                if (match && (bestKey == null || key.Length > bestKey.Length))
                    bestKey = key;
            }
            if (bestKey == null) return false;

            var screen = _open[bestKey];
            if (path.Length == bestKey.Length)   // path == screen 名，无 id-path → root
            {
                rect = screen.RootGameObject.GetComponent<RectTransform>();
                return rect != null;
            }

            string idPath = path.Substring(bestKey.Length + 1);
            try
            {
                var ctl = screen.Get(idPath);
                rect = ctl?.RectTransform;
                return rect != null;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { return false; }
        }
```

> `_open` / `Screen` / `IControl.RectTransform` 都在同程序集可见。`UI.cs` 顶部已 `using UnityEngine;`（`RectTransform` 可直接用）。

- [ ] **Step 4: 运行确认通过** — `run_tests(... filter="PathResolveTests")`。Expected: 4 passed。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/UI.cs Tests/EditMode/Toast
git commit -m "feat(toast): UI.TryResolvePath 控件路径解析（最长前缀 screen 名 + id-path 下钻）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: ToastPosition — 定位值类型

**Files:**
- Create: `Runtime/Application/Toasts/ToastPosition.cs`
- Test: `Tests/EditMode/Toast/ToastPositionTests.cs`

> 依赖 Task 3 的 `UI.TryResolvePath`。`TryResolve` 在 Control/ControlPath 未命中时返回 false（**不**自己退默认 —— 退默认由 `ToastOverlay` 做，避免 `ToastPosition` 反向依赖 `UI.Toast`）。

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Toast/ToastPositionTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application.Toasts;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastPositionTests
    {
        private static RectTransform MakeCanvasRect(float w, float h)
        {
            var go = new GameObject("toastCanvas", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        [Test]
        public void Bottom_anchors_to_bottom_edge()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.Bottom.TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0.5f, 0f), r.Anchor);
            Assert.AreEqual(new Vector2(0.5f, 0f), r.Pivot);
            Assert.AreEqual(new Vector2(0f, 120f), r.BasePos);
            Assert.AreEqual(Vector2.up, r.Dir);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Top_grows_down()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.Top.TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0f, -120f), r.BasePos);
            Assert.AreEqual(Vector2.down, r.Dir);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Coord_is_center_relative()
        {
            var rt = MakeCanvasRect(1920, 1080);
            Assert.IsTrue(ToastPosition.At(new Vector2(0, 200)).TryResolve(rt, 120f, out var r));
            Assert.AreEqual(new Vector2(0.5f, 0.5f), r.Anchor);
            Assert.AreEqual(new Vector2(0, 200), r.BasePos);
            Object.DestroyImmediate(rt.gameObject);
        }

        [Test]
        public void Vector2_implicitly_converts()
        {
            ToastPosition p = new Vector2(10, 20);   // 隐式
            p.TryResolve(MakeCanvasRect(100, 100), 0f, out var r);
            Assert.AreEqual(new Vector2(10, 20), r.BasePos);
        }

        [Test]
        public void Unspecified_default_flag_set()
            => Assert.IsTrue(default(ToastPosition).IsUnspecified);

        [Test]
        public void Preset_is_not_unspecified()
            => Assert.IsFalse(ToastPosition.Bottom.IsUnspecified);

        [Test]
        public void GroupKey_presets_differ_coords_round()
        {
            Assert.AreNotEqual(ToastPosition.Top.GroupKey(), ToastPosition.Bottom.GroupKey());
            Assert.AreEqual(ToastPosition.At(new Vector2(0.4f, 0.4f)).GroupKey(),
                            ToastPosition.At(new Vector2(0.3f, 0.1f)).GroupKey());   // 同舍入到 (0,0)
        }

        [Test]
        public void ControlPath_miss_returns_false()
        {
            // 没有任何已开 Screen → 路径解析必失败 → TryResolve false（由 overlay 退默认）
            PromptUGUI.Application.UI.ResetForTests();
            Assert.IsFalse(ToastPosition.At("Nope/x").TryResolve(MakeCanvasRect(100, 100), 0f, out _));
        }
    }
}
```

- [ ] **Step 2: 运行确认失败** — Expected: `ToastPosition` 不存在。

- [ ] **Step 3: 实现**

`Runtime/Application/Toasts/ToastPosition.cs`：

```csharp
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// Toast 的定位来源（spec §4）。预设 Top/Bottom/Center、坐标、控件引用、控件路径四选一。
    /// 解析在“显示时刻”进行，落到 toast 自己 Canvas 的本地坐标系。
    /// </summary>
    public readonly struct ToastPosition
    {
        internal enum Kind { Unspecified = 0, Top, Bottom, Center, Coord, Control, ControlPath }

        /// <summary>解析结果：基准位 + anchor/pivot + 堆叠方向（单位向量）。</summary>
        internal readonly struct Resolved
        {
            public readonly Vector2 BasePos, Anchor, Pivot, Dir;
            public Resolved(Vector2 basePos, Vector2 anchor, Vector2 pivot, Vector2 dir)
            { BasePos = basePos; Anchor = anchor; Pivot = pivot; Dir = dir; }
        }

        private readonly Kind _kind;
        private readonly Vector2 _coord;
        private readonly IControl _control;
        private readonly string _path;

        private ToastPosition(Kind k, Vector2 coord, IControl ctl, string path)
        { _kind = k; _coord = coord; _control = ctl; _path = path; }

        public static readonly ToastPosition Top    = new(Kind.Top, default, null, null);
        public static readonly ToastPosition Bottom = new(Kind.Bottom, default, null, null);
        public static readonly ToastPosition Center = new(Kind.Center, default, null, null);

        public static ToastPosition At(Vector2 coords)     => new(Kind.Coord, coords, null, null);
        public static ToastPosition At(IControl control)   => new(Kind.Control, default, control, null);
        public static ToastPosition At(string controlPath) => new(Kind.ControlPath, default, null, controlPath);

        // Vector2 是 struct，隐式转换合法。IControl/string 各由 UI.Toast.Show 的专用重载承接
        // （C# 禁止到/从接口类型的转换运算符，CS0552）。
        public static implicit operator ToastPosition(Vector2 coords) => At(coords);

        internal bool IsUnspecified => _kind == Kind.Unspecified;

        /// <summary>同源 → 同组（互相顶）；异源独立。预设按 Kind、坐标按四舍五入、控件按引用/路径分组。</summary>
        internal object GroupKey() => _kind switch
        {
            Kind.Coord       => new Vector2Int(Mathf.RoundToInt(_coord.x), Mathf.RoundToInt(_coord.y)),
            Kind.Control     => (object)_control,
            Kind.ControlPath => _path,
            _                => _kind,   // Top/Bottom/Center/Unspecified
        };

        /// <summary>
        /// 解析到 toast Canvas 本地坐标。Control/ControlPath 未命中 → 返回 false（调用方退默认）。
        /// </summary>
        internal bool TryResolve(RectTransform toastCanvasRect, float edgeInset, out Resolved r)
        {
            switch (_kind)
            {
                case Kind.Top:
                    r = new Resolved(new Vector2(0f, -edgeInset), new(0.5f, 1f), new(0.5f, 1f), Vector2.down);
                    return true;
                case Kind.Bottom:
                    r = new Resolved(new Vector2(0f, edgeInset), new(0.5f, 0f), new(0.5f, 0f), Vector2.up);
                    return true;
                case Kind.Center:
                    r = new Resolved(Vector2.zero, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                    return true;
                case Kind.Coord:
                    r = new Resolved(_coord, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                    return true;
                case Kind.Control:
                case Kind.ControlPath:
                    if (TryResolveLocalPoint(toastCanvasRect, out var local))
                    {
                        r = new Resolved(local, new(0.5f, 0.5f), new(0.5f, 0.5f), Vector2.up);
                        return true;
                    }
                    r = default;
                    return false;
                default:
                    r = default;
                    return false;
            }
        }

        private bool TryResolveLocalPoint(RectTransform toastCanvasRect, out Vector2 local)
        {
            local = default;
            RectTransform target;
            if (_kind == Kind.Control) target = _control?.RectTransform;
            else if (!UI.TryResolvePath(_path, out target)) return false;
            if (target == null) return false;

            var srcCanvas = target.GetComponentInParent<Canvas>();
            Camera srcCam = srcCanvas != null ? srcCanvas.worldCamera : null;   // Overlay → null（正确）
            Vector3 worldCenter = target.TransformPoint(target.rect.center);
            Vector2 screenPt = RectTransformUtility.WorldToScreenPoint(srcCam, worldCenter);

            var toastCanvas = toastCanvasRect.GetComponentInParent<Canvas>();
            Camera toastCam = toastCanvas != null ? toastCanvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                toastCanvasRect, screenPt, toastCam, out local);
        }
    }
}
```

- [ ] **Step 4: 运行确认通过** — `run_tests(... filter="ToastPositionTests")`。Expected: 8 passed。

> 若 `ControlPath_miss_returns_false` 因 `UI.TryResolvePath` 在无开屏时正常返回 false 而通过即可。跨 Canvas 的世界→本地数值正确性放 PlayMode（Task 8），这里只验证来源分派与分组。

- [ ] **Step 5: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/Toasts Tests/EditMode/Toast
git commit -m "feat(toast): ToastPosition 定位值类型（四来源 + TryResolve + 分组键）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 内置模板 Toast.ui.xml

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Toast.ui.xml`

- [ ] **Step 1: 写模板**（默认裸文字、无边框、`id="text"` 由管理器写入并定位/测量）

`Runtime/Resources/PromptUGUI/Toast.ui.xml`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Toast.ui" reference="1920x1080" reference.portrait="1080x1920">
    <!-- 管理器把 id="text" 当文本节点（写 TextValue + 测 GetNativeSize），并接管它的
         anchor/pivot/anchoredPosition/sizeDelta。默认无 id="content" 包裹 → 直接定位文本本身。
         换肤示例（加圆角底 pill）见 C# SKILL 的 Toast 节。 -->
    <Text id="text" anchor="center" align="center" fontSize="40" color="white"/>
  </Screen>
</PromptUGUI>
```

- [ ] **Step 2: 跑 UIXmlLint 校验**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Toast.ui.xml
```
Expected: exit 0，无 error。

- [ ] **Step 3: commit**

```bash
git add -A Runtime/Resources/PromptUGUI/Toast.ui.xml
git commit -m "feat(toast): 内置 Toast.ui.xml 默认模板（裸 <Text>）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: ToastView — 单条 toast 逐帧行为

**Files:**
- Create: `Runtime/Application/Toasts/ToastView.cs`

> 纯 `UIBehaviour`，逐帧逻辑（Update）只在 PlayMode 真正跑，故本 Task 不写 EditMode 测试（行为测试在 Task 8 PlayMode）。仅需编译通过 + 提供 Task 7 要调用的 API。

- [ ] **Step 1: 实现**

`Runtime/Application/Toasts/ToastView.cs`：

```csharp
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// 挂在 toast Screen root 上的单条生命周期驱动：淡入→停留→淡出→通知管理器移除。
    /// 用 <see cref="Time.unscaledDeltaTime"/>（游戏暂停 timeScale=0 时 toast 仍能淡出）。
    /// 每帧把 content 节点平滑 lerp 到管理器分配的堆叠目标位（位置回收）。
    /// 用 UIBehaviour（与 CarouselView 一致）以可靠收到 Unity 生命周期回调。
    /// </summary>
    internal sealed class ToastView : UIBehaviour
    {
        private enum Phase { FadeIn, Hold, FadeOut, Done }

        private CanvasGroup _cg;
        private RectTransform _content;
        private float _fadeIn, _fadeOut, _hold;
        private Action<ToastView> _onComplete;

        private Phase _phase;
        private float _t;
        private Vector2 _target;
        private bool _hasTarget;
        private const float ReflowTau = 0.08f;   // 位置平滑时间常数（越小越快贴目标）

        internal void Init(CanvasGroup cg, RectTransform content,
            float fadeIn, float hold, float fadeOut, Action<ToastView> onComplete)
        {
            _cg = cg; _content = content;
            _fadeIn = Mathf.Max(1e-4f, fadeIn);
            _fadeOut = Mathf.Max(1e-4f, fadeOut);
            _hold = Mathf.Max(0f, hold);
            _onComplete = onComplete;
            _phase = Phase.FadeIn; _t = 0f;
            if (_cg != null) _cg.alpha = 0f;
        }

        /// <param name="snap">true=立刻就位（最新一条落基准，无滑动）；false=平滑 lerp（被顶开/回收）。</param>
        internal void SetTarget(Vector2 target, bool snap)
        {
            _target = target; _hasTarget = true;
            if (snap && _content != null) _content.anchoredPosition = target;
        }

        internal float MeasuredHeight => _content != null ? _content.rect.height : 0f;

        internal bool IsEvicting => _phase == Phase.FadeOut || _phase == Phase.Done;

        // 管理器分配的堆叠目标位（materialize/reflow 时即确定，不依赖 Update）。供测试断言。
        internal Vector2 CurrentTarget => _target;

        /// <summary>MaxVisible 超额：立刻切到淡出，快速挤走。</summary>
        internal void Evict()
        {
            if (_phase == Phase.FadeOut || _phase == Phase.Done) return;
            _phase = Phase.FadeOut; _t = 0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_hasTarget && _content != null)
            {
                float k = 1f - Mathf.Exp(-dt / ReflowTau);
                _content.anchoredPosition = Vector2.Lerp(_content.anchoredPosition, _target, k);
            }

            _t += dt;
            switch (_phase)
            {
                case Phase.FadeIn:
                    if (_cg != null) _cg.alpha = Mathf.Clamp01(_t / _fadeIn);
                    if (_t >= _fadeIn) { if (_cg != null) _cg.alpha = 1f; _phase = Phase.Hold; _t = 0f; }
                    break;
                case Phase.Hold:
                    if (_t >= _hold) { _phase = Phase.FadeOut; _t = 0f; }
                    break;
                case Phase.FadeOut:
                    if (_cg != null) _cg.alpha = 1f - Mathf.Clamp01(_t / _fadeOut);
                    if (_t >= _fadeOut)
                    {
                        if (_cg != null) _cg.alpha = 0f;
                        _phase = Phase.Done;
                        _onComplete?.Invoke(this);
                    }
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: 编译确认** — `refresh_unity` → `read_console`。Expected: 无错误。

- [ ] **Step 3: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/Toasts
git commit -m "feat(toast): ToastView 单条淡入淡出+计时+位置回收（UIBehaviour, unscaled time）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: ToastOverlay 管理器 + UI.Toast 门面

**Files:**
- Create: `Runtime/Application/Toasts/ToastOverlay.cs`
- Create: `Runtime/Application/UI.Toast.cs`
- Test: `Tests/EditMode/Toast/ToastOverlayTests.cs`

> 两文件互相引用（门面读旋钮、管理器调门面旋钮；门面 `Show` 调管理器），同程序集一起编译。EditMode 下 materialize pump 因 fake resolver 同步完成 → `Show` 同步建出 toast；`ToastView.Update` 不跑（无 play loop），故用内置测试钩子 `CompleteOldestForTests` 模拟生命周期结束来验证 Sequential 提升。

- [ ] **Step 1: 写失败测试**

`Tests/EditMode/Toast/ToastOverlayTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Toasts;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastOverlayTests
    {
        private const string ToastXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Toast'>
    <Text id='text' anchor='center' align='center' fontSize='40' color='white'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "test/Toast" ? ToastXml : null);
            UI.Toast.XmlSrc = "test/Toast";
            UI.Toast.DefaultPosition = ToastPosition.Bottom;
            UI.Toast.DefaultStackMode = ToastStackMode.Stacked;
            UI.Toast.MaxVisible = 5;
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Stacked_shows_immediately()
        {
            UI.Toast.Show("hi");
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Text_written_into_text_node()
        {
            UI.Toast.Show("已保存");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual("已保存", screen.Get<Text>("text").TmpComponent.text);
        }

        [Test]
        public void Toast_does_not_block_raycasts()
        {
            UI.Toast.Show("x");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var cg = screen.RootGameObject.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg);
            Assert.IsFalse(cg.blocksRaycasts);
            Assert.IsFalse(cg.interactable);
        }

        [Test]
        public void SortingOrder_above_modal_band()
        {
            UI.Toast.SortingOrder = 2000;
            UI.Toast.Show("x");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual(2000, screen.RootGameObject.GetComponent<Canvas>().sortingOrder);
        }

        [Test]
        public void Bottom_position_applied_to_text_node()
        {
            UI.Toast.EdgeInset = 120f;
            UI.Toast.Show("x", ToastPosition.Bottom);
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var rt = screen.Get<Text>("text").RectTransform;
            Assert.AreEqual(new Vector2(0.5f, 0f), rt.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0f), rt.pivot);
            // newest 落基准：anchoredPosition.y == EdgeInset
            Assert.AreEqual(120f, rt.anchoredPosition.y, 0.5f);
        }

        [Test]
        public void Sequential_waits_then_promotes()
        {
            UI.Toast.Show("a", mode: ToastStackMode.Sequential);
            UI.Toast.Show("b", mode: ToastStackMode.Sequential);
            Assert.AreEqual(1, ToastOverlay.ActiveCount);   // 仅 a 显示
            Assert.AreEqual(1, ToastOverlay.QueuedCount);   // b 等待

            Assert.IsTrue(ToastOverlay.CompleteOldestForTests());   // 模拟 a 结束
            Assert.AreEqual(1, ToastOverlay.ActiveCount);   // b 提升
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Stacked_two_coexist_and_older_pushed_up()
        {
            UI.Toast.Show("first", ToastPosition.Bottom);
            UI.Toast.Show("second", ToastPosition.Bottom);
            Assert.AreEqual(2, ToastOverlay.ActiveCount);

            // 读“分配到的目标位”（reflow 即定，不依赖 Update）：older 被顶得更高、newer 落基准。
            var screens = System.Linq.Enumerable.ToList(ToastOverlay.ActiveScreens);
            var firstTarget = screens[0].RootGameObject.GetComponent<ToastView>().CurrentTarget;
            var secondTarget = screens[1].RootGameObject.GetComponent<ToastView>().CurrentTarget;
            Assert.Greater(firstTarget.y, secondTarget.y, "先来的(older)目标位更高");
            Assert.AreEqual(UI.Toast.EdgeInset, secondTarget.y, 0.5f, "最新一条落基准 EdgeInset");
        }

        [Test]
        public void MaxVisible_evicts_oldest()
        {
            UI.Toast.MaxVisible = 2;
            UI.Toast.Show("1", ToastPosition.Bottom);
            UI.Toast.Show("2", ToastPosition.Bottom);
            UI.Toast.Show("3", ToastPosition.Bottom);   // 触发挤最老
            // 最老那条进入 FadeOut（IsEvicting）；EditMode 不 tick，故仍在 _live，但已标记淡出
            Assert.IsTrue(ToastOverlay.OldestIsEvictingForTests());
        }

        [Test]
        public void Default_mode_resolves_to_DefaultStackMode()
        {
            UI.Toast.DefaultStackMode = ToastStackMode.Sequential;
            UI.Toast.Show("a");          // mode 缺省 → Default → Sequential
            UI.Toast.Show("b");          // 第二条应排队
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(1, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Path_overload_falls_back_to_default_on_miss()
        {
            // "Nope/x" 解析不到 → 退回 DefaultPosition(Bottom)，仍显示，不抛
            Assert.DoesNotThrow(() => UI.Toast.Show("x", "Nope/x"));
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败** — Expected: `UI.Toast` / `ToastOverlay` 不存在。

- [ ] **Step 3: 实现门面** `Runtime/Application/UI.Toast.cs`：

```csharp
using System;
using PromptUGUI.Application.Toasts;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// 轻量提示文字子系统（spec §3）。独立于模态：不进 dialog 栈、不挡输入、定时自淡出。
        /// </summary>
        public static class Toast
        {
            public static string XmlSrc { get; set; } = "PromptUGUI/Toast.ui";   // 带 .ui 后缀
            public static int SortingOrder { get; set; } = 2000;                  // 须高于 Modal(1000)
            public static ToastPosition DefaultPosition { get; set; } = ToastPosition.Bottom;
            public static ToastStackMode DefaultStackMode { get; set; } = ToastStackMode.Stacked;
            public static int MaxVisible { get; set; } = 5;

            public static float FadeInSeconds { get; set; } = 0.2f;
            public static float FadeOutSeconds { get; set; } = 0.4f;
            public static float Spacing { get; set; } = 12f;
            public static float EdgeInset { get; set; } = 120f;
            public static Vector2 Padding { get; set; } = new(24f, 12f);   // content 比文字大出的边距
            public static float HoldBase { get; set; } = 1.0f;
            public static float HoldPerChar { get; set; } = 0.06f;
            public static float HoldMin { get; set; } = 1.5f;
            public static float HoldMax { get; set; } = 5.0f;

            // canonical：preset / Vector2(隐式) / ToastPosition.At(...)
            public static void Show(string text, ToastPosition position = default,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null)
            {
                if (position.IsUnspecified) position = DefaultPosition;
                if (mode == ToastStackMode.Default)
                    mode = DefaultStackMode == ToastStackMode.Default ? ToastStackMode.Stacked : DefaultStackMode;
                float hold = ToastDuration.Compute(text, holdSeconds < 0f ? 0f : holdSeconds,
                    HoldBase, HoldPerChar, HoldMin, HoldMax);
                ToastOverlay.Show(new ToastOverlay.ToastEntry
                {
                    Text = text, Position = position, Mode = mode, Hold = hold, Configure = configure,
                });
            }

            // 控件路径字符串（"<screenName>/<idPath>"）
            public static void Show(string text, string controlPath,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null)
                => Show(text, ToastPosition.At(controlPath), mode, holdSeconds, configure);

            // 控件引用（专用重载，因 IControl 不能隐式转 ToastPosition — CS0552）
            public static void Show(string text, IControl control,
                ToastStackMode mode = ToastStackMode.Default, float holdSeconds = 0f,
                Action<IScreen> configure = null)
                => Show(text, ToastPosition.At(control), mode, holdSeconds, configure);
        }
    }
}
```

- [ ] **Step 4: 实现管理器** `Runtime/Application/Toasts/ToastOverlay.cs`：

```csharp
using System;
using System.Collections.Generic;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Application.Toasts
{
    /// <summary>
    /// Toast 子系统（spec §6/§8）。克隆 LoadingOverlay 的 materialize pump + epoch teardown，
    /// 再加分组堆叠布局与 Stacked/Sequential 准入。每条 toast 一份 Screen。
    /// </summary>
    internal static class ToastOverlay
    {
        internal sealed class ToastEntry
        {
            public string Text;
            public ToastPosition Position;
            public ToastStackMode Mode;
            public float Hold;
            public Action<IScreen> Configure;
        }

        private sealed class LiveToast
        {
            public object GroupKey;
            public ToastView View;
            public RectTransform Content;
            public Screen Screen;
            public string Key;
            public ToastPosition.Resolved Resolved;
        }

        private static readonly List<LiveToast> _live = new();        // 到达顺序
        private static readonly Queue<ToastEntry> _pending = new();    // 待 materialize
        private static readonly Queue<ToastEntry> _waiting = new();    // Sequential 等清空
        private static bool _materializing;
        private static int _materializeEpoch;

        internal static int ActiveCount => _live.Count;
        internal static int QueuedCount => _pending.Count + _waiting.Count;

        internal static IEnumerable<Screen> ActiveScreens
        {
            get { foreach (var t in _live) if (t.Screen != null) yield return t.Screen; }
        }

        internal static void Show(ToastEntry entry)
        {
            bool sequential = entry.Mode == ToastStackMode.Sequential;
            if (sequential && !IsIdle()) _waiting.Enqueue(entry);
            else QueueForMaterialize(entry);
        }

        private static bool IsIdle() =>
            _live.Count == 0 && _pending.Count == 0 && !_materializing;

        private static void QueueForMaterialize(ToastEntry e)
        {
            _pending.Enqueue(e);
            if (!_materializing) _ = MaterializePump();
        }

        private static async Awaitable MaterializePump()
        {
            if (_materializing) return;
            _materializing = true;
            int epoch = _materializeEpoch;
            try
            {
                while (_pending.Count > 0)
                {
                    if (epoch != _materializeEpoch) return;
                    var entry = _pending.Dequeue();
                    try { await Materialize(entry); }
                    catch (Exception ex) { Debug.LogError($"[PromptUGUI] Toast 显示失败: {ex}"); }
                }
            }
            finally
            {
                if (epoch == _materializeEpoch)
                {
                    _materializing = false;
                    PromoteWaiting();
                }
            }
        }

        private static async Awaitable Materialize(ToastEntry entry)
        {
            string src = UI.Toast.XmlSrc;
            await ModalDocCache.EnsureLoaded(src);   // 唯一 await，须在加入 _live 之前
            var (screen, key) = UI.OpenModalScreen(src);

            var root = screen.RootGameObject;
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = UI.Toast.SortingOrder;

            var cg = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0f;

            // 文本节点（必需）+ 定位/测量节点（content 优先，回退 text）
            Text textCtl = TryGet<Text>(screen, "text");
            RectTransform content = TryGet<IControl>(screen, "content")?.RectTransform
                                    ?? textCtl?.RectTransform;
            if (textCtl == null || content == null)
            {
                Debug.LogError("[PromptUGUI] Toast 模板缺 id=\"text\" 节点，无法显示。");
                UI.CloseModalScreen(key);
                return;
            }

            textCtl.TextValue = entry.Text ?? "";
            entry.Configure?.Invoke(screen);

            // 尺寸：content = 文字 native + 2*Padding
            Vector2 native = textCtl.GetNativeSize() ?? Vector2.zero;
            content.sizeDelta = native + 2f * UI.Toast.Padding;

            // 定位：解析失败（控件路径/引用失效）→ 退回 DefaultPosition + warning
            var canvasRect = root.GetComponent<RectTransform>();
            var pos = entry.Position;
            if (!pos.TryResolve(canvasRect, UI.Toast.EdgeInset, out var resolved))
            {
                Debug.LogWarning("[PromptUGUI] Toast 控件定位解析失败，退回默认位置。");
                pos = FallbackPosition();
                pos.TryResolve(canvasRect, UI.Toast.EdgeInset, out resolved);
            }
            content.anchorMin = content.anchorMax = resolved.Anchor;
            content.pivot = resolved.Pivot;

            var live = new LiveToast
            {
                GroupKey = pos.GroupKey(),
                Content = content,
                Screen = screen,
                Key = key,
                Resolved = resolved,
            };

            EvictIfNeeded(live.GroupKey);
            _live.Add(live);

            var view = root.AddComponent<ToastView>();
            live.View = view;
            view.Init(cg, content,
                UI.Toast.FadeInSeconds, entry.Hold, UI.Toast.FadeOutSeconds, OnViewComplete);

            ReflowGroup(live.GroupKey, newest: live);
        }

        // DefaultPosition 若被设成 Control/ControlPath/Unspecified（不该），兜底 Bottom，避免递归失败。
        private static ToastPosition FallbackPosition()
        {
            var d = UI.Toast.DefaultPosition;
            return d.IsUnspecified ? ToastPosition.Bottom : d;
        }

        private static void ReflowGroup(object groupKey, LiveToast newest)
        {
            var members = _live.FindAll(t => Equals(t.GroupKey, groupKey));   // 到达顺序
            if (members.Count == 0) return;
            var basis = members[members.Count - 1].Resolved;   // 用最新一条的基准快照
            var heights = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                heights[i] = members[i].Content != null ? members[i].Content.rect.height : 0f;
            var targets = ToastStack.ComputeTargets(heights, UI.Toast.Spacing, basis.Dir, basis.BasePos);
            for (int i = 0; i < members.Count; i++)
                members[i].View?.SetTarget(targets[i], snap: members[i] == newest);
        }

        private static void EvictIfNeeded(object groupKey)
        {
            int max = UI.Toast.MaxVisible;
            if (max <= 0) return;
            var members = _live.FindAll(t => Equals(t.GroupKey, groupKey));
            // 即将再加一条 → 已达上限就从最老起逐条挤走，直到留出一个名额
            for (int i = 0; members.Count - i >= max; i++)
                members[i].View?.Evict();
        }

        private static void OnViewComplete(ToastView view)
        {
            int idx = _live.FindIndex(t => t.View == view);
            if (idx < 0) return;
            var live = _live[idx];
            _live.RemoveAt(idx);
            UI.CloseModalScreen(live.Key);
            ReflowGroup(live.GroupKey, newest: null);   // 其余回收，无 snap
            PromoteWaiting();
        }

        private static void PromoteWaiting()
        {
            if (!IsIdle()) return;
            while (_waiting.Count > 0)
            {
                QueueForMaterialize(_waiting.Dequeue());
                return;
            }
        }

        internal static void CancelAllForTeardown()
        {
            _live.Clear();
            _pending.Clear();
            _waiting.Clear();
            _materializeEpoch++;     // 抛弃在途 pump
            _materializing = false;
            // toast Screen 在 UI._open 里，由 UnloadAll/ResetForTests 的 _open 循环统一关
        }

        // —— 测试钩子 —— //
        internal static bool CompleteOldestForTests()
        {
            if (_live.Count == 0) return false;
            OnViewComplete(_live[0].View);
            return true;
        }

        internal static bool OldestIsEvictingForTests()
            => _live.Count > 0 && _live[0].View != null && _live[0].View.IsEvicting;

        private static T TryGet<T>(Screen screen, string id) where T : class, IControl
        {
            try { return screen.Get<T>(id); }
            catch (KeyNotFoundException) { return null; }
            catch (InvalidCastException) { return null; }   // id 存在但类型不符（content 可为非 Text）
        }
    }
}
```

> `TryGet<IControl>(screen, "content")` 用 `IControl` 约束直接拿任意控件；`TryGet<Text>(screen,"text")` 要求 Text。`screen.Get<T>` 类型不符抛的是 `InvalidCastException`（见 `Screen.Get<T>`），一并 catch。

- [ ] **Step 5: 运行确认通过** — `refresh_unity` → `run_tests(... filter="ToastOverlayTests")`。Expected: 10 passed。

> 若 `Bottom_position_applied` 的 `anchoredPosition.y` 因 `Padding`/native 测量在 EditMode 下与 120 偏差超过容差：放宽断言为 `Assert.AreEqual(120f, rt.anchoredPosition.y, 1f)`（newest 的 base.y 不含高度项，应恰为 EdgeInset；偏差只可能来自浮点）。

- [ ] **Step 6: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/Toasts Runtime/Application/UI.Toast.cs Tests/EditMode/Toast
git commit -m "feat(toast): ToastOverlay 管理器 + UI.Toast 门面（准入/分组/堆叠/定位）

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: teardown 接线 + PlayMode 时序测试

**Files:**
- Modify: `Runtime/Application/UI.cs`（`UnloadAll` 约 810 行、`ResetForTests` 约 974 行各加一行）
- Test: `Tests/PlayMode/Toast/ToastLifecyclePlayModeTests.cs`

- [ ] **Step 1: 接 teardown** —— 在 `Runtime/Application/UI.cs` 两处，紧挨 `Modals.LoadingOverlay.CancelAllForTeardown();` 之后各加一行。

`UnloadAll`（约 810-812）：

```csharp
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
```

`ResetForTests`（约 974-976）：

```csharp
            Modal.CancelAllForTeardown();
            Modals.LoadingOverlay.CancelAllForTeardown();
            Toasts.ToastOverlay.CancelAllForTeardown();
            Modals.ModalDocCache.Clear();
```

- [ ] **Step 2: 编译确认** — `refresh_unity` → `read_console`。Expected: 无错误（`ActiveCount==0` 在 teardown 后由下方测试间接验证）。

- [ ] **Step 3: 写 PlayMode 测试** `Tests/PlayMode/Toast/ToastLifecyclePlayModeTests.cs`：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Toasts;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Toast
{
    public class ToastLifecyclePlayModeTests
    {
        private const string ToastXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Toast'>
    <Text id='text' anchor='center' align='center' fontSize='40' color='white'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "test/Toast" ? ToastXml : null);
            UI.Toast.XmlSrc = "test/Toast";
            UI.Toast.DefaultPosition = ToastPosition.Bottom;
            UI.Toast.DefaultStackMode = ToastStackMode.Stacked;
            // 收紧时长让测试快，但留足窗口避开帧 dt 抖动：
            // 单条生命周期 = FadeIn(0.1) + Hold(0.3) + FadeOut(0.1) = 0.5s。
            UI.Toast.FadeInSeconds = 0.1f;
            UI.Toast.FadeOutSeconds = 0.1f;
            UI.Toast.HoldBase = 0.3f;
            UI.Toast.HoldMin = 0.3f;
            UI.Toast.HoldMax = 0.5f;
            UI.Toast.HoldPerChar = 0f;
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Toast_fades_in_holds_then_self_destroys()
        {
            UI.Toast.Show("hi");
            yield return null;
            Assert.AreEqual(1, ToastOverlay.ActiveCount);

            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var cg = screen.RootGameObject.GetComponent<CanvasGroup>();

            yield return new WaitForSeconds(0.05f);    // 淡入中（0~0.1）
            Assert.Greater(cg.alpha, 0f);

            yield return new WaitForSeconds(0.15f);    // t≈0.2：淡入已完成、停留中（0.1~0.4）
            Assert.AreEqual(1f, cg.alpha, 0.05f);

            yield return new WaitForSeconds(0.7f);      // t≈0.9 > 0.5 生命周期 → 自毁
            Assert.AreEqual(0, ToastOverlay.ActiveCount, "toast 应已自销毁");
        }

        [UnityTest]
        public IEnumerator Sequential_second_appears_after_first_gone()
        {
            UI.Toast.Show("a", mode: ToastStackMode.Sequential);
            UI.Toast.Show("b", mode: ToastStackMode.Sequential);
            yield return null;
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(1, ToastOverlay.QueuedCount);

            yield return new WaitForSeconds(0.7f);     // a 走完整生命周期(0.5) → b 提升并开始
            // b 在 a 结束后才被提升，再走 0.5s；只断言最终全部归零（队列排空 + 无可见）
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(0, ToastOverlay.ActiveCount);
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [UnityTest]
        public IEnumerator Stacked_two_coexist_then_collapse()
        {
            UI.Toast.Show("first", ToastPosition.Bottom);
            UI.Toast.Show("second", ToastPosition.Bottom);
            yield return null;
            Assert.AreEqual(2, ToastOverlay.ActiveCount);

            yield return new WaitForSeconds(1.0f);     // 两条都走完生命周期(各 0.5s)
            Assert.AreEqual(0, ToastOverlay.ActiveCount);
        }
    }
}
```

> PlayMode 程序集 `PromptUGUI.Tests.PlayMode` 已存在；新文件放 `Tests/PlayMode/Toast/` 自动纳入。`AwaitableHelpers` 在 Runtime（internal，已 `InternalsVisibleTo` PlayMode 测试）。

- [ ] **Step 4: 运行 PlayMode** — `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="ToastLifecyclePlayModeTests")`。Expected: 3 passed。

> PlayMode `run_tests` 在本机偶发不稳（见项目记忆）。若 hang/“failed to initialize”：先确保场景已保存、必要时重启 Unity 后重跑；编译检查（Step 2）更可靠。

- [ ] **Step 5: 跑全量 EditMode 防回归** — `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])`。Expected: 全绿（含既有用例）。

- [ ] **Step 6: lint + commit**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx; cd ..
git add -A Runtime/Application/UI.cs Tests/PlayMode/Toast
git commit -m "feat(toast): teardown 接线 + PlayMode 生命周期时序测试

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: C# SKILL.md 更新

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

> 按 CLAUDE.md：新公开 C# API（`UI.Toast` + `ToastPosition` + `ToastStackMode`）必须在同一 PR 反映到 C# skill（英文）。

- [ ] **Step 1: 先定位插入点** — 读 `.claude/skills/scripting-promptugui-csharp/SKILL.md`，找到 "Modal dialogs" 节末尾（Loading/InputBox 之后），在其后新增 "Toast (transient tips)" 节。

- [ ] **Step 2: 新增内容**（英文，紧跟 Modal 节后）：

````markdown
## Toast (transient tips)

`UI.Toast.Show(...)` shows a short, borderless, **non-interactive** text tip that fades out on
its own and **stacks** when called repeatedly. It is **not** a modal — it sits above everything
(`SortingOrder` 2000), never blocks input, and there is no result to await.

```csharp
UI.Toast.Show("Saved");                                  // bottom-center, stacked, auto duration
UI.Toast.Show("Level up!", ToastPosition.Top);
UI.Toast.Show("Crit!", new Vector2(0, 200));             // reference-resolution coords (center origin, +y up)
UI.Toast.Show("+10", someControl);                       // at a control (IControl reference)
UI.Toast.Show("+10 coins <sprite name=\"coin\">", "Hud/rewardBtn");  // at a control by path
UI.Toast.Show("Combo!", ToastStackMode.Sequential);      // wait for prior toasts to clear first
UI.Toast.Show("Held 3s", ToastPosition.Center, holdSeconds: 3f);
```

**Positioning** — `ToastPosition`: presets `Top` / `Bottom` / `Center`; `At(Vector2)` exact coords;
`At(IControl)` / `At(string path)` at a control. The path form is `"<screenName>/<idPath>"` —
resolved at show time by longest-prefix screen-name match against open screens, then id-path
drill-down (same scheme as `screen.Get("a/b")`). A failed path (screen closed, id missing, control
destroyed) falls back to `DefaultPosition` with a `Debug.LogWarning` — it never throws.

**Stacking** — `ToastStackMode`: `Stacked` (default; new tip pops in at the anchor, older ones slide
away; same position = one stack, capped at `MaxVisible`) or `Sequential` (queues until all visible
tips clear). Toasts at different positions form independent stacks.

**Duration** — `holdSeconds` defaults to `clamp(HoldMin, HoldBase + textLength*HoldPerChar, HoldMax)`;
pass a positive `holdSeconds` to override. Fade uses unscaled time (works while the game is paused).

**Skinning** — `UI.Toast.XmlSrc` (default `"PromptUGUI/Toast.ui"`) points at a `.ui.xml` whose
`<Screen name>` must byte-equal `XmlSrc`. The manager writes the text into `id="text"` and
repositions/sizes `id="content"` (falling back to `id="text"` when there is no wrapper). The default
template is a bare `<Text id="text">`. To add a rounded pill background, wrap it:

```xml
<Screen name="MyUI/Toast.ui" reference="1920x1080" reference.portrait="1080x1920">
  <Image id="content" sprite="MyUI/pill.png#pill_9slice" type="sliced">
    <Text id="text" anchor="stretch" align="center" fontSize="40" color="white"/>
  </Image>
</Screen>
```

Then `UI.Toast.XmlSrc = "MyUI/Toast.ui";`. Inline `<sprite>`, i18n and autosize all work because the
tip is a normal `<Text>`.

**Tunable knobs** (static, on `UI.Toast`): `XmlSrc`, `SortingOrder`, `DefaultPosition`,
`DefaultStackMode`, `MaxVisible`, `FadeInSeconds`, `FadeOutSeconds`, `Spacing`, `EdgeInset`,
`Padding`, `HoldBase`, `HoldPerChar`, `HoldMin`, `HoldMax`.

The optional trailing `configure: Action<IScreen>` runs after the text is bound, giving access to the
live toast `IScreen` (recolor, add nodes) — same shape as the modal `configure` hook.
````

- [ ] **Step 3: 自检** — 通读该节，确认 API 名（`Show` 重载、`ToastPosition.At`、`ToastStackMode`、旋钮名）与 Task 4/7 实现逐字一致。

- [ ] **Step 4: commit**

```bash
git add -A .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "docs(skill): C# skill 新增 Toast (transient tips) 节

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 收尾验证（全部 Task 完成后）

- [ ] `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)` → `read_console` 无 error。
- [ ] `run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` 全绿。
- [ ] `run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])` 全绿。
- [ ] `cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx` 干净。
- [ ] `dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Toast.ui.xml` exit 0。
- [ ] 对照 spec §13 验收标准逐条人工核对（含图文混排、换肤、路径失败退默认不抛）。
- [ ] visual QA（用户）：底部三连堆叠回收、顶部、控件定位、Sequential 排队、暂停时仍淡出。

---

## 备注：相对 spec 的两处实现细节补充

1. **CS0552 修正**：spec 原写 `implicit operator ToastPosition(IControl)` 非法（C# 禁接口转换运算符）。已改为 `UI.Toast.Show(text, IControl, ...)` 专用重载，调用 ergonomics 不变（spec §3.1/§4 已同步更正）。
2. **`Padding` 旋钮**：spec §7 仅说 “content hug 文字”。实现取 `content.sizeDelta = textNative + 2*Padding`，新增 `UI.Toast.Padding`（默认 (24,12)）—— 既让裸文字有呼吸边距，也给 pill 皮肤留边。属透明默认值，不改任何已述语义。
