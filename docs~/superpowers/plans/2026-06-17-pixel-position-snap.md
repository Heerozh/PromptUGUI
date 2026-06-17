# 像素位置吸附 (Pixel Position Snap) 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pixel 模式下自动把 TMP 文本的渲染原点吸附到设备整数像素网格，补上 `Canvas.pixelPerfect` 漏掉的 TMP 字形对齐，修复像素字/中心锚点文字随屏幕尺寸时糊时清。

**Architecture:** 新增内部组件 `PixelSnap : UIBehaviour`，由 `Screen` 在 Pixel 模式下挂到每个 `TMP_Text` 上；组件在 `LateUpdate` 用经实测验证的「屏幕空间往返」公式把**对齐感知参考点**吸到整数设备像素（幂等、运行期自门控于 `canvas.pixelPerfect`）。静态文本在 `Open` 挂载，动态文本（BindItems/Markdown）在 `RegisterDynamicSubtree` 挂载。

**Tech Stack:** Unity 6 uGUI + TextMeshPro (`TMPro.TMP_Text`)；测试 NUnit（`PromptUGUI.Tests.EditMode` / `.PlayMode`）；经 UnityMCP 跑测试 + `dotnet format` lint。

**关键已验证事实（宿主 Unity 实测）：**
- `Canvas.pixelPerfect` 已把 Image/RawImage 的位置+尺寸吸到整数设备像素（`30.9→31`），但 **TMP 绕过该路径**，故只需补 TMP 文本（spec PPS-D1）。
- Overlay 画布下 `canvas.transform.localScale = (sf,sf,sf)`，**世界坐标 == 屏幕设备像素**。
- 吸附公式（已实测：`screen (21.90,12.30) → (22,12)`，二次 pass delta `=0` 幂等）：
  `WorldToScreenPoint(ref) → round → ScreenPointToWorldPointInRectangle(canvasRect) → rt.position += (snapWorld - refWorld)`。

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Runtime/Controls/Internal/PixelSnap.cs` | 吸附组件 + 两个可测纯静态助手（`ReferencePoint` / `SnapToPixelGrid`） | 新建 |
| `Runtime/Application/Screen.cs` | 缓存 `_isPixelMode`；`AttachPixelSnaps`；在 `Open` 与 `RegisterDynamicSubtree` 调用 | 改 |
| `Tests/EditMode/Application/PixelSnapTests.cs` | 助手数学 + 组件 + Screen 挂载（静态/动态）EditMode 测试 | 新建 |
| `Tests/PlayMode/PixelSnapPlayTests.cs` | 真实 play loop 下中心锚点文本落格回归 | 新建 |
| `scripting-promptugui-csharp/SKILL.md` | CanvasConfigurator/Pixel 一节加一句（吸附 + opt-out） | 改 |
| `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md` | 补节引用本设计（PPS-D1…D7） | 改 |

> `PixelSnap` 是内部组件，**不是 XML builtin tag → 不需要同步 `Runtime/Core/Lint/BuiltinTags.cs`**（spec §5.1）。
> 新建 `.cs` 文件需先 `refresh_unity` 生成 `.meta`，提交时连 `.meta` 一起 `git add`。

---

## Task 1: `ReferencePoint` 对齐感知参考点（纯静态助手）

**Files:**
- Create: `Runtime/Controls/Internal/PixelSnap.cs`
- Test: `Tests/EditMode/Application/PixelSnapTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Application/PixelSnapTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class PixelSnapTests
    {
        // ---- Task 1: ReferencePoint ----
        [Test]
        public void ReferencePoint_LeftTop_ReturnsMinXMaxY()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Top);
            Assert.AreEqual(new Vector2(0f, 16f), p);
        }

        [Test]
        public void ReferencePoint_CenterMiddle_ReturnsCenter()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Middle);
            Assert.AreEqual(new Vector2(5f, 8f), p);
        }

        [Test]
        public void ReferencePoint_RightBottom_ReturnsMaxXMinY()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Right, VerticalAlignmentOptions.Bottom);
            Assert.AreEqual(new Vector2(10f, 0f), p);
        }

        [Test]
        public void ReferencePoint_Justified_TreatedAsLeft()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Justified, VerticalAlignmentOptions.Top);
            Assert.AreEqual(0f, p.x);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败（`PixelSnap` 不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `PixelSnap could not be found`。

- [ ] **Step 3: 写最小实现**

新建 `Runtime/Controls/Internal/PixelSnap.cs`（先只放 `ReferencePoint`，组件骨架下个 task 补全）：

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    // 把 TMP 文本的「对齐感知参考点」吸到整数设备像素网格——补 Canvas.pixelPerfect
    // 漏掉的 TMP 字形对齐。见 spec 2026-06-17-pixel-position-snap (PPS-D1…D7)。
    [DisallowMultipleComponent]
    internal sealed class PixelSnap : UIBehaviour
    {
        // 参考点 = TMP 把文本块相对 rect 摆放所依据的边/中心（PPS-D4）。
        internal static Vector2 ReferencePoint(
            Rect rect, HorizontalAlignmentOptions h, VerticalAlignmentOptions v)
        {
            float x = h == HorizontalAlignmentOptions.Center ? rect.center.x
                    : h == HorizontalAlignmentOptions.Right ? rect.xMax
                    : rect.xMin;   // Left / Justified / Flush / Geometry
            float y = v == VerticalAlignmentOptions.Middle ? rect.center.y
                    : v == VerticalAlignmentOptions.Bottom ? rect.yMin
                    : rect.yMax;   // Top / Baseline / Capline / Geometry
            return new Vector2(x, y);
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)   # 轮询到完成
```
Expected: 4 个 ReferencePoint 测试 PASS，0 fail。

- [ ] **Step 5: 提交**

```bash
mcp__UnityMCP__refresh_unity   # 确保 .meta 已生成
git add Runtime/Controls/Internal/PixelSnap.cs Runtime/Controls/Internal/PixelSnap.cs.meta Tests/EditMode/Application/PixelSnapTests.cs Tests/EditMode/Application/PixelSnapTests.cs.meta
git commit -m "$(cat <<'EOF'
feat: PixelSnap.ReferencePoint 对齐感知参考点助手

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `SnapToPixelGrid` 吸附公式（纯静态助手）

**Files:**
- Modify: `Runtime/Controls/Internal/PixelSnap.cs`
- Test: `Tests/EditMode/Application/PixelSnapTests.cs`

- [ ] **Step 1: 写失败测试**

在 `PixelSnapTests` 类追加（锚点在屏幕左下角 → 屏幕坐标 = anchoredPos × scaleFactor，与编辑器视口尺寸无关，故断言确定）：

```csharp
        // ---- Task 2: SnapToPixelGrid ----
        private static (GameObject root, RectTransform rt, Canvas canvas) MakeRT(
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("c");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            var child = new GameObject("ch");
            child.transform.SetParent(go.transform, false);
            var rt = child.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            Canvas.ForceUpdateCanvases();
            return (go, rt, canvas);
        }

        [Test]
        public void SnapToPixelGrid_FractionalScreenPos_SnapsToIntegerScreen()
        {
            var (root, rt, canvas) = MakeRT(new Vector2(7.3f, 4.1f), new Vector2(10f, 16f));
            // localRef = rect 左下 (0,0) → 屏幕 (21.9,12.3) → 期望吸到 (22,12)
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);
            Canvas.ForceUpdateCanvases();
            var after = RectTransformUtility.WorldToScreenPoint(
                null, rt.TransformPoint((Vector3)rt.rect.min));
            Assert.AreEqual(22f, after.x, 0.01f);
            Assert.AreEqual(12f, after.y, 0.01f);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void SnapToPixelGrid_AlreadyAligned_Idempotent()
        {
            var (root, rt, canvas) = MakeRT(new Vector2(7.3f, 4.1f), new Vector2(10f, 16f));
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);
            Canvas.ForceUpdateCanvases();
            var pos1 = rt.position;
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);   // 第二次
            Assert.AreEqual(pos1.x, rt.position.x, 1e-4f);
            Assert.AreEqual(pos1.y, rt.position.y, 1e-4f);
            Object.DestroyImmediate(root);
        }
```

- [ ] **Step 2: 跑测试确认失败（`SnapToPixelGrid` 不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `SnapToPixelGrid` 未定义。

- [ ] **Step 3: 写最小实现**

在 `PixelSnap` 类内追加（公式已实测验证）：

```csharp
        // 把 rt 平移，使 localRef（局部空间）落在整数设备像素上。模式无关：overlay 用
        // null 相机、camera 模式用 worldCamera。已实测幂等（PPS-D3）。
        internal static void SnapToPixelGrid(RectTransform rt, Canvas canvas, Vector2 localRef)
        {
            var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            var refWorld = rt.TransformPoint((Vector3)localRef);
            var before = RectTransformUtility.WorldToScreenPoint(cam, refWorld);
            var snap = new Vector2(Mathf.Round(before.x), Mathf.Round(before.y));
            if ((before - snap).sqrMagnitude < 1e-4f) return;          // 已对齐
            var canvasRect = (RectTransform)canvas.transform;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    canvasRect, snap, cam, out var snapWorld))
                rt.position += (snapWorld - refWorld);
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: Task1+Task2 全部 PASS（6 个）。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Internal/PixelSnap.cs Tests/EditMode/Application/PixelSnapTests.cs
git commit -m "$(cat <<'EOF'
feat: PixelSnap.SnapToPixelGrid 屏幕空间往返吸附公式

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `PixelSnap` 组件（LateUpdate + 门控 + TMP 接线）

**Files:**
- Modify: `Runtime/Controls/Internal/PixelSnap.cs`
- Test: `Tests/EditMode/Application/PixelSnapTests.cs`

- [ ] **Step 1: 写失败测试**

追加（用真实 `TextMeshProUGUI`；无字体环境下 rect/对齐仍可用，吸附只读几何）：

```csharp
        // ---- Task 3: 组件 Snap() ----
        private static (GameObject root, TextMeshProUGUI tmp, PixelSnap snap) MakeText(
            Vector2 anchoredPos, bool pixelPerfect)
        {
            var go = new GameObject("c");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            canvas.pixelPerfect = pixelPerfect;
            var t = new GameObject("txt");
            t.transform.SetParent(go.transform, false);
            var tmp = t.AddComponent<TextMeshProUGUI>();   // 默认 TopLeft 对齐
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(10f, 16f);
            rt.anchoredPosition = anchoredPos;
            var snap = t.AddComponent<PixelSnap>();
            Canvas.ForceUpdateCanvases();
            return (go, tmp, snap);
        }

        [Test]
        public void Snap_PixelPerfectOn_SnapsLeftTopReferenceToInteger()
        {
            var (root, tmp, snap) = MakeText(new Vector2(7.3f, 4.1f), pixelPerfect: true);
            snap.Snap();
            Canvas.ForceUpdateCanvases();
            // TopLeft → 参考点 (xMin, yMax) = (0,16) → 屏幕 (21.9, (4.1+16)*3=60.3) → (22,60)
            var after = RectTransformUtility.WorldToScreenPoint(
                null, tmp.rectTransform.TransformPoint((Vector3)tmp.rectTransform.rect.min
                       + Vector3.up * tmp.rectTransform.rect.height));
            Assert.AreEqual(22f, after.x, 0.01f);
            Assert.AreEqual(60f, after.y, 0.01f);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Snap_PixelPerfectOff_NoOp()
        {
            var (root, tmp, snap) = MakeText(new Vector2(7.3f, 4.1f), pixelPerfect: false);
            var before = tmp.rectTransform.position;
            snap.Snap();
            Assert.AreEqual(before, tmp.rectTransform.position);   // 门控：不动
            Object.DestroyImmediate(root);
        }
```

- [ ] **Step 2: 跑测试确认失败（`Snap()` 不存在）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `Snap` 未定义。

- [ ] **Step 3: 写最小实现**

在 `PixelSnap` 类加字段、生命周期与 `Snap()`（放在静态助手之上）：

```csharp
        private RectTransform _rt;
        private TMP_Text _text;
        private Canvas _canvas;

        protected override void Awake()
        {
            _rt = (RectTransform)transform;
            _text = GetComponent<TMP_Text>();
        }

        // 父链变化 → 下次 Snap 重新解析所属 Canvas。
        protected override void OnCanvasHierarchyChanged() => _canvas = null;
        protected override void OnTransformParentChanged() => _canvas = null;

        private void LateUpdate() => Snap();

        // 运行期自门控于 canvas.pixelPerfect（关掉它 = 同时关吸附，复用既有 opt-out，PPS-D7）。
        internal void Snap()
        {
            if (_rt == null) _rt = (RectTransform)transform;
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>(true);
            if (_text == null || _canvas == null
                || !_canvas.pixelPerfect
                || _canvas.renderMode == RenderMode.WorldSpace)
                return;

            var localRef = ReferencePoint(
                _rt.rect, _text.horizontalAlignment, _text.verticalAlignment);
            SnapToPixelGrid(_rt, _canvas, localRef);
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: Task1-3 全 PASS（8 个）。若 `TextMeshProUGUI` 因测试环境缺 TMP Essentials 报 material 警告但测试仍 PASS 即可（吸附只读几何）。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Internal/PixelSnap.cs Tests/EditMode/Application/PixelSnapTests.cs
git commit -m "$(cat <<'EOF'
feat: PixelSnap 组件 (LateUpdate + pixelPerfect 门控 + TMP 接线)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Screen 在 Pixel 模式下挂载静态文本

**Files:**
- Modify: `Runtime/Application/Screen.cs`（字段 ~30；`ApplyCanvasScaler` ~191；`Open` 168 后；新方法）
- Test: `Tests/EditMode/Application/PixelSnapTests.cs`

- [ ] **Step 1: 写失败测试**

追加（`OpenScreen` 风格；锚定全屏的 `<Text>`）：

```csharp
        // ---- Task 4: Screen 静态挂载 ----
        private static PromptUGUI.Application.Screen OpenPixel(string body)
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // /1920x1080 → factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>" + body + @"</Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        [Test]
        public void Open_PixelMode_AttachesPixelSnapToText()
        {
            var screen = OpenPixel("<Text id='t'>hi</Text>");
            var tmp = screen.RootGameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(tmp);
            Assert.IsNotNull(tmp.GetComponent<PixelSnap>(), "pixel 模式应挂 PixelSnap");
        }

        [Test]
        public void Open_AutoMode_DoesNotAttachPixelSnap()
        {
            UI.CanvasSizeOverride = () => new Vector2(1920f, 1080f);
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Text id='t'>hi</Text></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = (PromptUGUI.Application.Screen)UI.Open("S");
            var tmp = screen.RootGameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNull(tmp.GetComponent<PixelSnap>(), "auto 模式不应挂");
        }
```

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: `Open_PixelMode_AttachesPixelSnapToText` FAIL（未挂载）。

- [ ] **Step 3: 改 `Screen.cs`**

(a) 在字段区（约 line 30，`_canvasFactor` 附近）加：
```csharp
        private bool _isPixelMode;
```

(b) 在 `ApplyCanvasScaler`，`var mode = ResolveScaleMode();`（约 line 191）下一行加：
```csharp
            _isPixelMode = mode == ScaleMode.Pixel;
```

(c) 在 `Open` 的 `ApplyScales();`（line 168）之后加：
```csharp
            AttachPixelSnaps(root);
```

(d) 在 `ApplyScales` 方法附近新增：
```csharp
        // Pixel 模式下给子树里每个 TMP 文本挂 PixelSnap——Canvas.pixelPerfect 不吸 TMP 字形，
        // 这里把文本渲染原点吸到设备整数像素。幂等（已挂则跳过）；Auto 模式 no-op。
        // 见 spec 2026-06-17-pixel-position-snap (PPS-D1/D2)。
        private void AttachPixelSnaps(GameObject subtreeRoot)
        {
            if (!_isPixelMode || subtreeRoot == null) return;
            var texts = subtreeRoot.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true);
            foreach (var t in texts)
                if (t.GetComponent<Controls.Internal.PixelSnap>() == null)
                    t.gameObject.AddComponent<Controls.Internal.PixelSnap>();
        }
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: Task1-4 全 PASS（10 个）。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/PixelSnapTests.cs
git commit -m "$(cat <<'EOF'
feat: Screen 在 Pixel 模式下给静态 TMP 文本挂 PixelSnap

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: 动态子树（BindItems/Markdown）挂载

**Files:**
- Modify: `Runtime/Application/Screen.cs`（`RegisterDynamicSubtree` 顶部，约 line 377）
- Test: `Tests/EditMode/Application/PixelSnapTests.cs`

- [ ] **Step 1: 写失败测试**

追加（BindItems 模式，参照 `DynamicSubtreeScaleTests`）：

```csharp
        // ---- Task 5: 动态子树挂载 ----
        [Test]
        public void BindItems_PixelMode_DynamicTextGetsPixelSnap()
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame width='200' height='50'><Text id='label'>x</Text></Frame></Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<PromptUGUI.Controls.ScrollList>("list");
            PromptUGUI.Controls.IControl captured = null;
            list.BindItems(
                R3.Observable.Return<System.Collections.Generic.IReadOnlyList<string>>(new[] { "a" }),
                (PromptUGUI.Controls.IControl slot, string s) => captured = slot);
            var label = captured.Get<PromptUGUI.Controls.Text>("label");
            Assert.IsNotNull(label.GameObject.GetComponent<PixelSnap>(),
                "动态实例化的文本也应挂 PixelSnap");
        }
```

> 注：`IControl` / `Text` / `ScrollList` 在 `PromptUGUI.Controls`；`Observable` 在 `R3`。文件顶部已有 `using TMPro; using UnityEngine; using PromptUGUI.Controls.Internal;`——本测试用全限定名引用，避免改 using 块。

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: `BindItems_PixelMode_DynamicTextGetsPixelSnap` FAIL（动态子树未挂）。

- [ ] **Step 3: 改 `Screen.cs`**

在 `RegisterDynamicSubtree(Control root, ...)` 方法体**最开头**（`PruneDeadDynamicSubtrees();` 之前，约 line 379）加一行——必须在 `if (!hasScale) return;` 早返回之前，使无 scale 的动态文本也被挂：

```csharp
            AttachPixelSnaps(root.GameObject);
```

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["PixelSnapTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: Task1-5 全 PASS（11 个）。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Application/Screen.cs Tests/EditMode/Application/PixelSnapTests.cs
git commit -m "$(cat <<'EOF'
feat: 动态子树 (BindItems/Markdown) 文本也挂 PixelSnap

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: PlayMode 回归——真实 loop 下中心锚点文本落格

**Files:**
- Create: `Tests/PlayMode/PixelSnapPlayTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/PlayMode/PixelSnapPlayTests.cs`（真实 play loop 自动跑 `LateUpdate`）：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class PixelSnapPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator CenterAnchoredText_SnapsToIntegerDevicePixel_InPlayLoop()
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Text id='t' anchor='center' width='100' height='20'>hi</Text>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var tmp = screen.Get<Text>("t").TmpComponent;
            var rt = tmp.rectTransform;

            // 强制一个分数设备位置：anchoredPosition x 落在半像素（×3 后 .5）
            rt.anchoredPosition = new Vector2(7.5f / 3f, 0f); // 期望参考点 x 原 7.5 → 吸到 8 或 7
            yield return null;   // LateUpdate 跑 Snap
            yield return null;

            var localRef = PromptUGUI.Controls.Internal.PixelSnap.ReferencePoint(
                rt.rect, tmp.horizontalAlignment, tmp.verticalAlignment);
            var screenPt = RectTransformUtility.WorldToScreenPoint(
                null, rt.TransformPoint((Vector3)localRef));
            Assert.AreEqual(Mathf.Round(screenPt.x), screenPt.x, 0.02f, "x 应落整数设备像素");
            Assert.AreEqual(Mathf.Round(screenPt.y), screenPt.y, 0.02f, "y 应落整数设备像素");
        }
    }
}
```

> `Text.TmpComponent`（internal `TMP_Text` getter）已存在（`Runtime/Controls/Text.cs:16`）。`anchor='center'` 让文本中心锚点对屏幕中心——与用户报告的失效场景同构。

- [ ] **Step 2: 跑测试确认失败**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["PixelSnapPlayTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: 若 Task4 的挂载在 PlayMode 也生效，此测试可能直接 PASS（说明吸附在真实 loop 工作）。若 FAIL，按 systematic-debugging 排查（多为 `LateUpdate` 时序——`yield return null` 加一帧再断言）。

- [ ] **Step 3: 无需新实现**（功能已由 Task 1-5 提供）。若 Step 2 因时序 FAIL：在断言前多 `yield return null` 一帧，或确认 PixelSnap 在 inactive GO 上不跑、显示后跑。

- [ ] **Step 4: 跑测试确认通过**

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["PixelSnapPlayTests"])
mcp__UnityMCP__get_test_job(job_id=...)
```
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
mcp__UnityMCP__refresh_unity
git add Tests/PlayMode/PixelSnapPlayTests.cs Tests/PlayMode/PixelSnapPlayTests.cs.meta
git commit -m "$(cat <<'EOF'
test: PlayMode 回归——中心锚点文本在真实 loop 下落格

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: 文档同步 + 全量回归 + lint

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`

- [ ] **Step 1: C# SKILL 加一句**

在 `scripting-promptugui-csharp/SKILL.md` 的 `UI.CanvasConfigurator` / Pixel 模式相关段落，追加（英文，SKILL 用英文）：

```markdown
> In Pixel mode (`scale-mode="pixel"`) the library auto-attaches a `PixelSnap` to
> every TMP text so glyph origins land on whole device pixels (Canvas.pixelPerfect
> does not pixel-adjust TMP). To opt out for a screen that needs smooth tweens,
> disable `pixelPerfect` on that Canvas inside `UI.CanvasConfigurator` — that also
> disables the text snap.
```

- [ ] **Step 2: master spec 补引用**

在 `2026-05-07-promptugui-description-language-design.md` 的 scale-mode 章节末尾加一句：

```markdown
Pixel-mode TMP text is additionally pixel-snapped at render time — see
`2026-06-17-pixel-position-snap-design.md` (decisions PPS-D1…D7).
```

- [ ] **Step 3: 全量三套 + lint**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
mcp__UnityMCP__get_test_job(job_id=...)
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])
mcp__UnityMCP__get_test_job(job_id=...)
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])
mcp__UnityMCP__get_test_job(job_id=...)
```
然后 lint（从仓库根）：
```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 三套全绿（EditMode 基线 +11、PlayMode +1）、lint 零改动。**勿只看 group——按 [[feedback_verify_full_suites_not_groups]] 跑整套断言全绿。**

- [ ] **Step 4: 提交**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md
git commit -m "$(cat <<'EOF'
docs: pixel position snap 同步 C# SKILL + master spec

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review（写完已检查）

- **Spec 覆盖**：PPS-D1（只补 TMP，Task4 的 GetComponentsInChildren<TMP_Text>）、D2（Screen 挂载 Pixel 门控 Task4 + 动态 Task5）、D3（LateUpdate+公式 Task2/3）、D4（ReferencePoint Task1）、D5（与 scale 正交——`SnapToPixelGrid` 读最终 transform，无需专门 task，PlayMode 隐式覆盖）、D6（行为变更——文档 Task7）、D7（pixelPerfect 门控 Task3）。✅ 全覆盖。
- **占位扫描**：无 TBD/TODO；每个代码步含完整代码。✅
- **类型一致**：`ReferencePoint(Rect, HorizontalAlignmentOptions, VerticalAlignmentOptions)`、`SnapToPixelGrid(RectTransform, Canvas, Vector2)`、`Snap()`、`AttachPixelSnaps(GameObject)`、`_isPixelMode`——跨 task 命名一致。✅
- **风险**：①PlayMode `LateUpdate` 时序（Task6 Step3 已给排查）；②`TextMeshProUGUI` 无字体环境警告（吸附只读几何，不影响断言）；③居中/右对齐 + 奇内容宽 ≤0.5px 残差（spec §3.3 已文档化，非 v1）。
