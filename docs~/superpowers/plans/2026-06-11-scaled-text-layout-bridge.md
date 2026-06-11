# Scaled-Text Layout Bridge 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 V/HStack 直接子节点的 `<Text scale=…>` 自动获得正确布局——半密度渲染、按整行宽换行、行高随内容、占位 = 视觉（spec：`docs~/superpowers/specs/2026-06-11-scaled-text-layout-bridge-design.md`，STW-D1…D9）。

**Architecture:** 实例化期在 Text GO 外自动插一个 wrapper GO（承接 LayoutElement + 新组件 `ScaledTextLayoutBridge`，向 LayoutGroup 报告 `TMP preferred × s`）；`Control` 新增 `LayoutHost` 指针，`ApplyCommon` / `ApplyLayoutElement` / `Hidden` / `Dispose` 按它路由；内层 TMP 重置为全 stretch 基线后由**现有** `ApplyBoxPreservingCompensation` 膨胀（零改动）。

**Tech Stack:** Unity 6 uGUI（`ILayoutElement` / `LayoutRebuilder`）、TMP（`TMPro_EventManager`）、NUnit（EditMode + PlayMode，经 UnityMCP 跑）、`dotnet format` lint。

**约定（每个 Task 都适用）：**

- 测试一律走 UnityMCP：先 `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`，再 `mcp__UnityMCP__read_console(action="get", types=["error"])` 确认零编译错误，然后 `mcp__UnityMCP__run_tests(...)` 拿 `job_id` 轮询 `mcp__UnityMCP__get_test_job(job_id=...)`。
- 新建 `.cs` 文件后第一次 refresh 会生成 `.meta` sidecar——**commit 时必须一起 add**（历史 PR 评审抓过这个）。
- 全程在 `feat/scaled-text-layout-bridge` 分支上提交，不碰 main。
- 工作目录：`/workspace-PromptUGUI`（UPM 包仓库；host Unity 工程在 `C:\xsoft\PromptUGUIDev`）。

---

### Task 1: `ScaledTextLayoutBridge` 组件（纯报告逻辑）

**Files:**
- Create: `Runtime/Controls/Internal/ScaledTextLayoutBridge.cs`
- Test: `Tests/EditMode/Controls/ScaledTextLayoutBridgeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // ScaledTextLayoutBridge：挂在 wrapper GO 上的 ILayoutElement，把内层 TMP 的
    // min/preferred × s（s = 内层 localScale.x）报告给父 LayoutGroup；flexible 原样
    // 透传（权重无量纲）；layoutPriority=0 与 TMP 持平，被显式 LayoutElement(priority 1)
    // 逐属性压过。spec STW-D6。
    public class ScaledTextLayoutBridgeTests
    {
        private GameObject _canvasGo;
        private GameObject _wrapperGo;
        private RectTransform _inner;
        private TMP_Text _tmp;
        private ScaledTextLayoutBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            // TMP 量算需要 Canvas 祖先（与 Btn GetNativeSize 的既有 EditMode 测试同前提）。
            _canvasGo = new GameObject("canvas", typeof(Canvas));
            _wrapperGo = new GameObject("wrapper", typeof(RectTransform));
            _wrapperGo.transform.SetParent(_canvasGo.transform, false);
            var textGo = new GameObject("text", typeof(RectTransform));
            textGo.transform.SetParent(_wrapperGo.transform, false);
            _tmp = textGo.AddComponent<TextMeshProUGUI>();
            _tmp.text = "hello world";
            _tmp.fontSize = 12;
            _inner = (RectTransform)textGo.transform;
            _bridge = _wrapperGo.AddComponent<ScaledTextLayoutBridge>();
            _bridge.Configure(_tmp, _inner);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvasGo);

        [Test]
        public void Preferred_and_min_scale_with_inner_localScale()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            Assert.AreEqual(_tmp.preferredWidth * 0.5f, _bridge.preferredWidth, 1e-3f);
            Assert.AreEqual(_tmp.preferredHeight * 0.5f, _bridge.preferredHeight, 1e-3f);
            Assert.AreEqual(_tmp.minWidth * 0.5f, _bridge.minWidth, 1e-3f);
            Assert.AreEqual(_tmp.minHeight * 0.5f, _bridge.minHeight, 1e-3f);
        }

        [Test]
        public void Identity_scale_is_passthrough()
        {
            _inner.localScale = Vector3.one;
            Assert.AreEqual(_tmp.preferredWidth, _bridge.preferredWidth, 1e-3f);
            Assert.AreEqual(_tmp.preferredHeight, _bridge.preferredHeight, 1e-3f);
        }

        [Test]
        public void Flexible_passes_through_unscaled()
        {
            _inner.localScale = new Vector3(0.5f, 0.5f, 1f);
            Assert.AreEqual(_tmp.flexibleWidth, _bridge.flexibleWidth, 1e-6f);
            Assert.AreEqual(_tmp.flexibleHeight, _bridge.flexibleHeight, 1e-6f);
        }

        [Test]
        public void Priority_is_zero_like_tmp()
        {
            Assert.AreEqual(0, _bridge.layoutPriority);
        }

        [Test]
        public void Unconfigured_bridge_reports_zero_not_throw()
        {
            var bare = new GameObject("bare", typeof(RectTransform))
                .AddComponent<ScaledTextLayoutBridge>();
            Assert.AreEqual(0f, bare.preferredWidth);
            Assert.AreEqual(0f, bare.preferredHeight);
            Object.DestroyImmediate(bare.gameObject);
        }
    }
}
```

- [ ] **Step 2: refresh + 跑测试确认编译失败（类型不存在）**

`refresh_unity` → `read_console(types=["error"])` 预期出现 `ScaledTextLayoutBridge` CS0246。

- [ ] **Step 3: 写实现**

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// V/HStack 直接子节点的 &lt;Text scale=…&gt; 自动 wrapper 上的布局桥（spec STW-D6）。
    /// 把内层 TMP 的 min/preferred × s（s = 内层 localScale.x，由 Screen.ApplyScaleToNode
    /// 写入，scale 未解析时为 1 → 自动透传）报告给父 LayoutGroup，flexible 原样透传；
    /// layoutPriority=0 与 TMP 持平，被显式 LayoutElement（priority 1）逐属性压过。
    /// 量算时序依赖 uGUI 标准四段 pass：水平 set 定下 wrapper 宽 → 内层经放宽 anchors
    /// 被动跟到 W/s → 垂直输入 calc 时这里读 tmp.preferredHeight（按需在 W/s 宽下重算）。
    /// 包装后 TMP 自己的 MarkLayoutForRebuild 在 wrapper（无 ILayoutGroup）就停了，
    /// 到不了外层 LayoutGroup —— 所以这里订阅 TMP 文本变更事件替它上报（spec STW-D7）。
    /// </summary>
    internal sealed class ScaledTextLayoutBridge : UIBehaviour, ILayoutElement
    {
        private TMP_Text _tmp;
        private RectTransform _inner;

        internal void Configure(TMP_Text tmp, RectTransform inner)
        {
            _tmp = tmp;
            _inner = inner;
        }

        private float S => _inner != null ? _inner.localScale.x : 1f;

        public float minWidth => _tmp != null ? _tmp.minWidth * S : 0f;
        public float preferredWidth => _tmp != null ? _tmp.preferredWidth * S : 0f;
        public float flexibleWidth => _tmp != null ? _tmp.flexibleWidth : -1f;
        public float minHeight => _tmp != null ? _tmp.minHeight * S : 0f;
        public float preferredHeight => _tmp != null ? _tmp.preferredHeight * S : 0f;
        public float flexibleHeight => _tmp != null ? _tmp.flexibleHeight : -1f;
        public int layoutPriority => 0;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        protected override void OnEnable()
        {
            base.OnEnable();
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            MarkParentForRebuild();
        }

        protected override void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            base.OnDisable();
        }

        private void OnTextChanged(Object obj)
        {
            if (!ReferenceEquals(obj, _tmp)) return;
            MarkParentForRebuild();
        }

        internal void MarkParentForRebuild()
        {
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
```

- [ ] **Step 4: refresh + 跑测试确认通过**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["ScaledTextLayoutBridgeTests"])` → 轮询 job → 5/5 PASS。

- [ ] **Step 5: commit（含 .meta）**

```bash
git add Runtime/Controls/Internal/ScaledTextLayoutBridge.cs* Tests/EditMode/Controls/ScaledTextLayoutBridgeTests.cs*
git commit -m "feat(scale): ScaledTextLayoutBridge — ILayoutElement reporting TMP preferred × s"
```

---

### Task 2: `Control.LayoutHost` 路由（Hidden / Dispose / ApplyCommon / ApplyLayoutElement）

**Files:**
- Modify: `Runtime/Controls/Control.cs`（`Hidden` 属性 ~line 28、`Dispose` ~line 467、`ApplyCommon` 的 `parentLg` 查询 ~line 197、`ApplyLayoutElement` 的 `parentHv` + LE 落点 ~line 315）
- Test: `Tests/EditMode/Controls/LayoutHostRoutingTests.cs`

此 Task 全部是"无 wrapper 时行为完全不变"的重构 + wrapper 模式下的新路由；wrapper 的自动创建在 Task 3 才接通，本 Task 测试手动搭 GO 结构。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    // Control.LayoutHost（spec STW-D4/D5）：默认 = 自身 RectTransform；指向 wrapper 后
    // ApplyCommon 的父级判断、LayoutElement 落点、Hidden、Dispose 都以 wrapper 为宿主。
    public class LayoutHostRoutingTests
    {
        private GameObject _vstackGo;
        private GameObject _wrapperGo;
        private GameObject _textGo;
        private PromptUGUI.Controls.Text _control;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _vstackGo = new GameObject("vstack", typeof(RectTransform),
                                       typeof(VerticalLayoutGroup));
            _wrapperGo = new GameObject("wrapper", typeof(RectTransform));
            _wrapperGo.transform.SetParent(_vstackGo.transform, false);
            _textGo = new GameObject("text", typeof(RectTransform));
            _textGo.transform.SetParent(_wrapperGo.transform, false);
            _control = new PromptUGUI.Controls.Text();
            _control.AttachTo(_textGo);
            _control.LayoutHost = (RectTransform)_wrapperGo.transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_vstackGo != null) Object.DestroyImmediate(_vstackGo);
            UI.ResetForTests();
        }

        [Test]
        public void LayoutHost_defaults_to_own_RectTransform()
        {
            var bare = new GameObject("bare", typeof(RectTransform));
            var c = new PromptUGUI.Controls.Text();
            c.AttachTo(bare);
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
            Assert.AreEqual(bare, c.HostGameObject);
            Object.DestroyImmediate(bare);
        }

        [Test]
        public void ApplyCommon_routes_LayoutElement_to_wrapper()
        {
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "LE should attach to the wrapper, not the inner GO");
            Assert.AreEqual(0f, le.preferredWidth);
            Assert.AreEqual(1f, le.flexibleWidth);
            Assert.IsNull(_textGo.GetComponent<LayoutElement>());
        }

        [Test]
        public void ApplyCommon_resets_inner_to_stretch_baseline()
        {
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var rt = _control.RectTransform;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rt.pivot);
            Assert.AreEqual(Vector2.zero, rt.sizeDelta);
            Assert.AreEqual(Vector2.zero, rt.anchoredPosition);
        }

        [Test]
        public void Explicit_height_pins_wrapper_LE_min_and_preferred()
        {
            _control.ApplyCommon(null, null, "stretch", "40", null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.AreEqual(40f, le.preferredHeight);
            Assert.AreEqual(40f, le.minHeight);
            Assert.AreEqual(0f, le.flexibleHeight);
        }

        [Test]
        public void Omitted_height_leaves_wrapper_LE_sentinel_for_bridge()
        {
            // <Text> 是 UsesIntrinsicLayoutSize 控件：省略轴留 -1 哨兵（bridge 接管）。
            _control.ApplyCommon(null, null, "stretch", null, null, null, null, true);

            var le = _wrapperGo.GetComponent<LayoutElement>();
            Assert.AreEqual(-1f, le.preferredHeight);
            Assert.AreEqual(-1f, le.minHeight);
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Hidden_toggles_wrapper_not_inner()
        {
            _control.Hidden = true;
            Assert.IsFalse(_wrapperGo.activeSelf);
            Assert.IsTrue(_textGo.activeSelf);
            Assert.IsTrue(_control.Hidden);
            _control.Hidden = false;
            Assert.IsTrue(_wrapperGo.activeSelf);
        }

        [Test]
        public void Dispose_destroys_wrapper()
        {
            _control.Dispose();
            Assert.IsTrue(_wrapperGo == null, "wrapper (host GO) should be destroyed");
        }
    }
}
```

- [ ] **Step 2: refresh + 跑测试确认失败**

`LayoutHost` setter 不存在 → CS0246/CS0200 编译错误（`read_console` 确认）。

- [ ] **Step 3: 实现 `Control.cs` 改动**

3a. 字段 + 属性（放在 `_canvasGroup` 字段之后）：

```csharp
        private RectTransform _layoutHost;

        /// <summary>
        /// LayoutGroup 量算用的宿主 RectTransform，默认 = 自身 RectTransform。
        /// V/HStack 直下声明了 scale 的 &lt;Text&gt; 由 ScreenInstantiator 指向自动插入的
        /// wrapper（spec STW-D4）：ApplyCommon 的父级判断、LayoutElement 落点、Hidden 的
        /// SetActive、Dispose 的销毁对象都以它为准；内层 RectTransform 只承载视觉与
        /// box-preserving 膨胀。
        /// </summary>
        internal RectTransform LayoutHost
        {
            get => _layoutHost != null ? _layoutHost : RectTransform;
            set => _layoutHost = value;
        }

        /// <summary>包装时 = wrapper GO（SetActive / Destroy 的作用对象），否则 = 自身 GameObject。</summary>
        internal GameObject HostGameObject
            => _layoutHost != null ? _layoutHost.gameObject : GameObject;
```

3b. `Hidden` 改为：

```csharp
        public bool Hidden
        {
            get => !HostGameObject.activeSelf;
            set => HostGameObject.SetActive(!value);
        }
```

3c. `Dispose` 改为：

```csharp
        public virtual void Dispose()
        {
            if (GameObject == null) return;
            // 与 Screen.Close 一致：EditMode 下用 DestroyImmediate，避免 "Destroy may not be called" 警告。
            // 销毁宿主 GO（wrapper 存在时即 wrapper，内层随子级联销毁）——BindItems 重建
            // 经 Dispose 走这里，不会把 wrapper 留在 LayoutGroup 里占行高。
            if (UnityEngine.Application.isPlaying) Object.Destroy(HostGameObject);
            else Object.DestroyImmediate(HostGameObject);
        }
```

3d. `ApplyCommon` 内 `parentLg` 查询改读 `LayoutHost`：

```csharp
            var parentLg = LayoutHost.parent != null
                ? LayoutHost.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                : null;
```

3e. `ApplyCommon` 的 `parentIsAutoLayout` 分支加内层基线重置：

```csharp
            if (parentIsAutoLayout)
            {
                ApplyLayoutElement(sizeSpec, preset);
                // anchor / pivot / sizeDelta / anchoredPosition: LayoutGroup 接管几何。
                // 作者写 anchor/margin 已经被 ScreenInstantiator 警告（spec §6.5）；这里静默跳过。
                // STW-D4: wrapper 模式下内层 RT 重置为全 stretch 基线——这是 ApplyScales
                // box-preserving 膨胀的输入（"ApplyCommon 先重置、ApplyScales 再膨胀"契约；
                // wrapper 本身的几何由 LayoutGroup 驱动）。
                if (_layoutHost != null)
                {
                    RectTransform.anchorMin = Vector2.zero;
                    RectTransform.anchorMax = Vector2.one;
                    RectTransform.pivot = new Vector2(0.5f, 0.5f);
                    RectTransform.sizeDelta = Vector2.zero;
                    RectTransform.anchoredPosition = Vector2.zero;
                }
            }
```

3f. `ApplyLayoutElement` 内 `parentHv` 查询与 LE 落点改用 `LayoutHost`（共三处）：

```csharp
            var parentHv = LayoutHost.parent != null
                ? LayoutHost.parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>()
                : null;
```

```csharp
            var le = LayoutHost.gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
```

```csharp
            le ??= LayoutHost.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
```

- [ ] **Step 4: refresh + 跑新测试 + 全量 EditMode 回归（无 wrapper 路径不得变化）**

```
run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["LayoutHostRoutingTests"])   → 7/7 PASS
run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])                                           → 全绿（基线 1522+，无回归）
```

- [ ] **Step 5: commit**

```bash
git add Runtime/Controls/Control.cs Tests/EditMode/Controls/LayoutHostRoutingTests.cs*
git commit -m "feat(scale): Control.LayoutHost — route LayoutGroup sizing/Hidden/Dispose to wrapper host"
```

---

### Task 3: 实例化期自动包装 + `InstantiateNode` 宿主查找修复

**Files:**
- Modify: `Runtime/Application/ScreenInstantiator.cs`（`InstantiateRecursive` 的 `control.AttachTo(go)` 之后 ~line 233；`InstantiateNode` 的 rootControl 查找 ~line 55）
- Test: `Tests/EditMode/Application/ScaledTextWrapperTests.cs`（新建）

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    // V/HStack 直下声明 scale 的 <Text> 在实例化期自动插 wrapper + 布局桥（spec STW-D8）。
    // 条件矩阵 + 动态子树（BindItems 与静态树共用 InstantiateRecursive，零特判）。
    public class ScaledTextWrapperTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen OpenScreen(string xml)
        {
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        private static Control GetControl(IScreen screen, string id)
            => (Control)screen.Get(id);

        [Test]
        public void Text_with_scale_in_VStack_gets_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost, "wrapper expected");
            Assert.AreEqual("t [scale-host]", c.LayoutHost.gameObject.name);
            // 层级：VStack → wrapper → text
            Assert.IsNotNull(c.LayoutHost.parent
                .GetComponent<UnityEngine.UI.VerticalLayoutGroup>());
            Assert.AreEqual(c.LayoutHost, c.RectTransform.parent);
            Assert.IsNotNull(c.LayoutHost.GetComponent<ScaledTextLayoutBridge>());
        }

        [Test]
        public void Text_without_scale_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
            Assert.IsNotNull(c.RectTransform.parent
                .GetComponent<UnityEngine.UI.VerticalLayoutGroup>());
        }

        [Test]
        public void Text_with_scale_in_Frame_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame size='200x100'>
      <Text id='t' anchor='stretch' margin='0' scale='0.5'>hello</Text>
    </Frame>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
        }

        [Test]
        public void Text_with_scale_in_Grid_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Grid anchor='top-left' size='300x100' columns='3' cellSize='100x100'>
      <Text id='t' scale='0.5'>hello</Text>
    </Grid>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost,
                "Grid is excluded (STW-D2) — cellSize is the declared box");
        }

        [Test]
        public void Text_with_variant_only_scale_gets_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale.mobile='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost,
                "variant 运行期才激活而 GO 永不重建 → 创建期必须备好 wrapper");
        }

        [Test]
        public void Btn_with_scale_in_VStack_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Btn id='b' size='100x44' scale='0.5'>ok</Btn>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "b");
            Assert.AreEqual(c.RectTransform, c.LayoutHost, "non-Text 控件不在范围（spec §1.3）");
        }

        [Test]
        public void BindItems_text_root_card_gets_wrapper_and_slot_resolves()
        {
            // 模板根 = <Text scale>：ScrollList Content 是 VerticalLayoutGroup → 包装；
            // 同时回归 InstantiateNode 的 rootControl 查找（必须按 HostGameObject 匹配，
            // 否则 BindItems 拿不到 slot）。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Text width='stretch' wrap='true' scale='0.5'>x</Text>
  </Template>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row' size='200x300'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            IControl captured = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl slot, string s) => captured = slot);
            Assert.IsNotNull(captured, "BindItems should instantiate one slot");
            var c = (Control)captured;
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost);
            Assert.AreEqual(0.5f, c.RectTransform.localScale.x, 1e-5f);
        }
    }
}
```

- [ ] **Step 2: refresh + 跑测试确认失败**

`run_tests(..., group_names=["ScaledTextWrapperTests"])` → 包装类测试 FAIL（`LayoutHost == RectTransform`，无 wrapper）。负例（no_wrapper 三个）此时已 PASS 是正常的。

- [ ] **Step 3: 实现**

3a. `ScreenInstantiator.InstantiateRecursive`，在 `control.AttachTo(go);` 与 `parentControl?.AddChild(control);` 之间插入：

```csharp
            control.AttachTo(go);
            // STW-D8: V/HStack 直下声明了 scale 的 <Text> → 插 wrapper + 布局桥，让
            // LayoutGroup 量到 "TMP preferred × s"（半密度渲染 + 整行宽换行 + 行高随内容）。
            // 条件 3 看 base 或任意 variant 覆盖——variant 运行期才激活而 GO 永不重建，
            // 创建期必须备好；scale 未解析时桥 ×1 透传（≡ 裸 TMP）。Grid 不在内
            // （GetComponent<HorizontalOrVerticalLayoutGroup> 对 GridLayoutGroup 返回 null）。
            if (control is Text textControl
                && parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>() != null
                && (node.Attributes.ContainsKey("scale")
                    || node.VariantOverrides.ContainsKey("scale")))
            {
                var wrapperGo = new GameObject(
                    (node.Id ?? node.Tag) + " [scale-host]", typeof(RectTransform));
                var wrapperRt = (RectTransform)wrapperGo.transform;
                wrapperRt.SetParent(parent, worldPositionStays: false);
                // go 此前是 parent 的末位 child；移入 wrapper 后 wrapper 顶上同一末位，
                // 兄弟顺序不变（ApplyAddBlock 的 SetSiblingIndex 流程因此无需感知 wrapper）。
                go.transform.SetParent(wrapperRt, worldPositionStays: false);
                wrapperGo.AddComponent<ScaledTextLayoutBridge>()
                         .Configure(textControl.TmpComponent, control.RectTransform);
                control.LayoutHost = wrapperRt;
            }
            parentControl?.AddChild(control);
```

（文件头需补 `using PromptUGUI.Controls.Internal;`，`Text` 类型已有 `PromptUGUI.Controls` 可见性。）

3b. `InstantiateNode` 的 rootControl 查找改按宿主 GO 匹配：

```csharp
            var rootGo = parent.GetChild(prevChildCount).gameObject;
            Control rootControl = null;
            foreach (var kv in nodeMap)
                if (kv.Value.HostGameObject == rootGo) { rootControl = kv.Value; break; }
```

- [ ] **Step 4: refresh + 跑新测试 + 全量 EditMode 回归**

```
run_tests(..., group_names=["ScaledTextWrapperTests"])  → 7/7 PASS
run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])  → 全绿
```

注意全量回归里重点看既有 `DynamicSubtreeScaleTests` / `ScaleAttributeTests` / Markdown 套件——它们覆盖 `InstantiateNode` 与 scale 的既有契约。

- [ ] **Step 5: commit**

```bash
git add Runtime/Application/ScreenInstantiator.cs Tests/EditMode/Application/ScaledTextWrapperTests.cs*
git commit -m "feat(scale): auto-wrap <Text scale> under V/HStack with layout-bridge host"
```

---

### Task 4: 端到端几何 + ReSolve 幂等 + scale 变更脏标

**Files:**
- Modify: `Runtime/Application/Screen.cs`（`ApplyScaleToNode` ~line 274：包一层加 MarkLayoutForRebuild）
- Test: `Tests/EditMode/Application/ScaledTextWrapperTests.cs`（追加）

- [ ] **Step 1: 追加失败测试**

```csharp
        [Test]
        public void Inner_text_inflated_by_box_preserving_compensation()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            // stretch 基线 span 1 → /0.5 = 2，关于中心放宽 → [-0.5, 1.5]，两轴同。
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.y, 1e-5f);
            Assert.AreEqual(0f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(0f, rt.sizeDelta.y, 1e-4f);
        }

        [Test]
        public void Hidden_attr_deactivates_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale='0.5' hidden='true'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.IsFalse(c.LayoutHost.gameObject.activeSelf,
                "hidden 必须作用在 wrapper，否则空 wrapper 仍占行高");
            Assert.IsTrue(c.GameObject.activeSelf);
        }

        [Test]
        public void Variant_flip_resets_and_reapplies_idempotently()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale='0.5' scale.mobile=''>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);

            UI.Variants.Set("mobile", true);   // scale 清空 → 恒等 + stretch 基线
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-5f);

            UI.Variants.Set("mobile", false);  // 回到 0.5 + 膨胀
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);

            // 显式双跑 ReSolve：补偿不得跨次累积（幂等）。
            screen.ReSolve();
            screen.ReSolve();
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Relative_scale_in_wrapper_recomputes_with_factor()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5r'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            // round(3×0.5)=2 → localScale 2/3；膨胀 span = 1/(2/3) = 1.5 → [-0.25, 1.25]。
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-5f);

            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // factor 2
            screen.ReSolve();
            // round(2×0.5)=1 → localScale 0.5 → [-0.5, 1.5]。
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
        }
```

- [ ] **Step 2: refresh + 跑测试**

预期：前两个（几何/hidden）在 Task 2/3 完成后可能已 PASS——它们是端到端验收，留作回归锚点；variant / factor 两个验证 ReSolve 链路，若 FAIL 按失败信息修。**至少跑一次确认 4 个全绿或定位出真实缺口后再进 Step 3。**

- [ ] **Step 3: `Screen.ApplyScaleToNode` 加 wrapper 脏标（无论 Step 2 是否全绿都要做——EditMode 断不到 rebuild 队列，这是 PlayMode 撑高的依赖）**

把现有方法改名为 `ApplyScaleToNodeCore`（签名与方法体不动），再加包装方法：

```csharp
        private void ApplyScaleToNode(ElementNode node, Control control, bool dynamicBaseline)
        {
            ApplyScaleToNodeCore(node, control, dynamicBaseline);
            // STW-D7(2): wrapper 模式下 scale 变更（Variant / resize 重算 Nx、<r>r）后内层
            // localScale 已变，但 TMP 文本没变 → TEXT_CHANGED 不会响——这里替它把父
            // LayoutGroup 标脏，让 bridge 的 ×s 新值参与下一次布局 pass。
            if (control._layoutHostForScaleDirty != null)
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(control._layoutHostForScaleDirty);
        }
```

`Control` 侧不再加新成员——直接复用 `LayoutHost`，但 `LayoutHost` 的 getter 在无 wrapper 时返回自身 RT，无法区分。改为在 `Control` 上加一个只读判别（**实际实现放 Task 2 已加的字段上，这里只读它**）：

```csharp
        /// <summary>wrapper 存在时返回它（scale 变更脏标用），否则 null。仅 Screen 读。</summary>
        internal RectTransform _layoutHostForScaleDirty => _layoutHost;
```

（命名按 `_lastAppliedDefaultText` 等既有 internal-field 风格；它是表达式体属性，放 `LayoutHost` 属性声明之后。）

- [ ] **Step 4: refresh + 全量 EditMode 回归**

`run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → 全绿。

- [ ] **Step 5: commit**

```bash
git add Runtime/Application/Screen.cs Runtime/Controls/Control.cs Tests/EditMode/Application/ScaledTextWrapperTests.cs
git commit -m "feat(scale): mark wrapper layout dirty on scale change; e2e geometry + ReSolve idempotency tests"
```

---

### Task 5: PlayMode 撑高验证（真实布局 pass）

**Files:**
- Create: `Tests/PlayMode/Controls/ScaledTextLayoutPlayTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    // 真实布局 pass 下的验收（spec §1.2 语义 2/3）：行高 = TMP 换行高 × s；
    // 运行时改文本后行高跟随（bridge 的 TEXT_CHANGED 脏标传播）。
    public class ScaledTextLayoutPlayTests
    {
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' fontSize='24' scale='0.5'>hello world hello world hello world hello world hello world</Text>
    </VStack>
  </Screen>
</PromptUGUI>";

        [UnityTest]
        public IEnumerator Row_height_matches_scaled_preferred_and_grows_with_text()
        {
            UI.ResetForTests();
            UI.LoadDocument("test", Xml);
            var screen = UI.Open("S");
            yield return null;                 // 一帧：Canvas + 布局 pass
            Canvas.ForceUpdateCanvases();

            var text = screen.Get<Text>("t");
            var control = (Control)(IControl)text;
            var wrapper = control.LayoutHost;
            var tmp = text.TmpComponent;

            // 内层 rect 宽 = wrapper 宽 × 2（anchors 放宽 1/0.5）→ TMP 按整行宽换行。
            Assert.AreEqual(wrapper.rect.width * 2f, control.RectTransform.rect.width, 1f);
            // 行高 = TMP 换行后 preferredHeight × 0.5（容差 1px）。
            var rowHeight = wrapper.rect.height;
            Assert.AreEqual(tmp.preferredHeight * 0.5f, rowHeight, 1f);
            Assert.Greater(rowHeight, 1f);

            // 动态改文本 → TEXT_CHANGED → bridge 标脏 → 下一帧行高增长。
            text.TextValue = string.Concat(
                System.Linq.Enumerable.Repeat("hello world ", 40));
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(wrapper.rect.height, rowHeight,
                "longer text must grow the row height");
            Assert.AreEqual(tmp.preferredHeight * 0.5f, wrapper.rect.height, 1f);
        }
    }
}
```

- [ ] **Step 2: refresh + 跑 PlayMode 测试**

`run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["ScaledTextLayoutPlayTests"])` → 1/1 PASS。失败时优先排查：(a) bridge 的 `OnEnable` 是否在 wrapper `AddComponent` 时已跑（`Configure` 在 AddComponent 之后——首帧靠 `UI.Open` 的整树布局，不依赖订阅）；(b) `TMPro_EventManager.TEXT_CHANGED_EVENT` 过滤是否用 `ReferenceEquals`。

- [ ] **Step 3: commit（含 .meta）**

```bash
git add Tests/PlayMode/Controls/ScaledTextLayoutPlayTests.cs*
git commit -m "test(scale): PlayMode — wrapper row height matches TMP preferred × s and tracks text changes"
```

---

### Task 6: 文档同步（XML SKILL + master spec）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/SKILL.md`（"Where to put scale" 列表，~line 930）
- Modify: `docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md`（scale 形态清单，line 239 之后）

- [ ] **Step 1: SKILL.md 改写 LayoutGroup-skip caveat**

把现有这条：

```markdown
- **On a direct child of `<VStack>` / `<HStack>` / `<Grid>`, box-preserving is skipped** (the LayoutGroup owns the child's geometry). `localScale` still applies, but the group measures with the _unscaled_ `RT.rect`, so a `scale="0.5"` child still reserves its full unscaled slot (the "small text gap" footgun). Wrap in a `<Frame size="..." scale="0.5">` if you want the group to see the intended size.
```

替换为：

```markdown
- **`<Text>` as a direct child of `<VStack>` / `<HStack>` is auto-bridged** — the library inserts an invisible layout host (`"<id> [scale-host]"` in the Hierarchy) so the group measures the text's *visual* size (`TMP preferred × scale`): `width="stretch"` / fixed width works, the text wraps against its full inflated width, and the row height grows with the wrapped content (auto-height multiline keeps working, now at density — chat-message body text is the canonical use). Omitted axes stay TMP-driven; explicit `width=` / `height=` still pin the axis. Works for all three forms (`N` / `Nx` / `<r>r`) and recomputes on resize / Variant flips. C#-visible side effect: `Get<Text>(id).RectTransform.parent` is the auto host, not the stack.
- **On a direct child of `<Grid>`, or any non-`<Text>` control in a layout group, box-preserving is skipped** (the LayoutGroup owns the child's geometry). `localScale` still applies, but the group measures with the _unscaled_ `RT.rect`, so a `scale="0.5"` child still reserves its full unscaled slot (the "small text gap" footgun). Wrap in a `<Frame size="..." scale="0.5">` if you want the group to see the intended size.
```

- [ ] **Step 2: master spec 加一条形态清单 bullet（line 239 的 `<r>r` 条目之后）**

```markdown
- V/HStack 直下声明了 scale 的 `<Text>` 自动桥接（实例化期 wrapper + `ILayoutElement` 报告 `TMP preferred × s`）：占位 = 视觉、按整行宽换行、行高随内容；三种形态（`N` / `Nx` / `<r>r`）一致，resize / Variant 重算。其余控件与 Grid 子节点维持 LayoutGroup-skip（footgun 文档化）。详见 [`2026-06-11-scaled-text-layout-bridge-design.md`](2026-06-11-scaled-text-layout-bridge-design.md)。
```

- [ ] **Step 3: commit**

```bash
git add .claude/skills/authoring-promptugui-xml/SKILL.md "docs~/superpowers/specs/2026-05-07-promptugui-description-language-design.md"
git commit -m "docs(scale): XML SKILL + master spec — <Text scale> auto-bridge in V/HStack"
```

---

### Task 7: 全量回归 + lint + 收尾

- [ ] **Step 1: 三套测试全量（UnityMCP）**

```
refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
read_console(action="get", types=["error"])                                     → 0 错误
run_tests(mode="EditMode",  assembly_names=["PromptUGUI.Tests.EditMode"])       → 全绿
run_tests(mode="EditMode",  assembly_names=["PromptUGUI.Tests.EditorOnly"])     → 全绿
run_tests(mode="PlayMode",  assembly_names=["PromptUGUI.Tests.PlayMode"])       → 全绿
```

- [ ] **Step 2: lint**

```bash
cd /workspace-PromptUGUI/.lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format style PromptUGUI.Lint.slnx
dotnet format analyzers PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```

预期 exit 0。（**禁止** `dotnet format analyzers --severity info`——CLAUDE.md 列了五条会炸 Unity 编译的 info 级 fixer。）有改动则单独 commit `style: dotnet format`。

- [ ] **Step 3: 漏网 .meta 检查 + 最终提交**

```bash
git status --porcelain        # 不应有未跟踪的 .meta / .cs
git log --oneline main..HEAD  # 预期 6-7 个 commit
```

- [ ] **Step 4: 按 superpowers:finishing-a-development-branch 收尾**（开 PR；PR 描述明示 STW-D9 行为变更：现存 V/HStack 直下 scaled Text 的渲染从"半尺寸 + 整槽占位"变为"占位 = 视觉"）

---

## 自审记录

- **Spec 覆盖**：STW-D1/D8 → Task 3；D2（Grid 排除）→ Task 3 测试；D3（永不销毁/透传）→ Task 4 variant 测试；D4/D5（LayoutHost 路由 + hidden/Dispose）→ Task 2；D6（桥契约）→ Task 1；D7（脏标两通道）→ Task 1（TEXT_CHANGED）+ Task 4 Step 3（scale 变更）+ Task 5（端到端）；D9 + 文档 → Task 6/7。spec §4 边界中"显式 height 钉轴"在 Task 2、"BindItems 动态"在 Task 3、"hidden"在 Task 2+4 各有测试。
- **Spec 修正**：spec §2 条件 1 写了"含子类"、§6 测试 1 写了"Text 子类"——`Text` 是 `sealed`，无子类。已随本计划提交修正 spec（删去子类字样）。
- **类型一致性**：`LayoutHost` / `HostGameObject` / `_layoutHostForScaleDirty`（Task 2 定义、Task 3/4 使用）、`Configure(TMP_Text, RectTransform)`（Task 1 定义、Task 3 调用）、`ApplyScaleToNodeCore` 改名只在 Task 4。
