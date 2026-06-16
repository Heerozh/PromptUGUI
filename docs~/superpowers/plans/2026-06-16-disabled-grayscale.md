# 禁用态默认置灰 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Btn/Tab/Toggle 在 Disabled 状态下，未声明任何 `disabled*` 时整控件按 sprite 形状真·去色（灰度），作者零配置。

**Architecture:** 新增 `UI/Grayscale` shader（Resources，运行期 `new Material` 懒建共享实例）+ 挂在控件 root 的 `DisabledGrayscaleController`：订阅 `IStateSource.OnState`，进入 Disabled 时给（剪枝后的）子树非 TMP graphic 换灰度材质、TMP 文字 `.color` 置灰，离开还原（capture-once + 销毁判空）。不翻 `transition=None`（保留 hover/press 的 uGUI ColorTint），并把 Selectable 内置 `disabledColor` 中和为白防二次压暗。子树遍历/剪枝抽成共享 `StateSubtree`，与 `*Modulate` 同源。

**Tech Stack:** Unity 6 uGUI, C# (LangVersion 9), R3 (Observable/Subscribe), TMP_Text, Unity MCP（跑测试）, `dotnet format`（lint）。

---

## File Structure

| 文件 | 责任 | 动作 |
|---|---|---|
| `Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader` | UI 去色 shader（保留 clip/stencil/alphaclip） | 创建 |
| `Runtime/Controls/Internal/StateSubtree.cs` | 状态源子树遍历 + 剪枝（单一来源） | 创建 |
| `Runtime/Controls/Internal/DisabledGrayscaleController.cs` | root 上的逐 graphic 去色/还原组件 | 创建 |
| `Runtime/Controls/Internal/DisabledGrayscaleInstaller.cs` | 中和内置 disabledColor + 收集 graphics + 装控制器 | 创建 |
| `Runtime/Controls/Internal/StateTintInstaller.cs` | 复用 `StateSubtree.CollectBlocked`（去掉私有副本） | 修改 |
| `Runtime/Controls/Internal/StateColorSet.cs` | 加 `NoneToNull` 哨兵助手 | 修改 |
| `Runtime/Controls/Btn.cs` | `OnAfterApply`：none 归一化 + 装灰度默认 | 修改 |
| `Runtime/Controls/Tab.cs` | 同上 | 修改 |
| `Runtime/Controls/Toggle.cs` | 同上 | 修改 |
| `Tests/EditMode/Controls/DisabledGrayscaleTests.cs` | EditMode 测试（Btn/Tab/Toggle + 边界） | 创建 |
| `Tests/EditMode/Controls/StateSubtreeTests.cs` | `StateSubtree.CollectGraphics` 直接测试 | 创建 |
| `Tests/PlayMode/Controls/DisabledGrayscalePlayTests.cs` | 活 Canvas 冒烟 | 创建 |
| `.claude/skills/authoring-promptugui-xml/reference/states.md` | 文档：默认去色 + 覆盖 + `none` | 修改 |

**测试运行约定（每个 "Run" 步骤都按此操作）：**

1. `mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)`
2. `mcp__UnityMCP__read_console(action="get", types=["error"])` —— 确认无编译错误再继续。
3. `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], group_names=["<ClassName>"])` —— 返回 `job_id`。
4. 轮询 `mcp__UnityMCP__get_test_job(job_id=...)` 直到完成，读 pass/fail。

> MCP 不可用时：尝试重连或请用户重启 MCP（见 CLAUDE.md）。**不要**用 batch-mode Unity。

---

## Task 1: `UI/Grayscale` shader

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader`
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`：

```csharp
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DisabledGrayscaleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void GrayscaleShader_LoadsFromResources_WithExpectedName()
        {
            var shader = Resources.Load<Shader>("PromptUGUI/Material/UI-Grayscale");
            Assert.IsNotNull(shader, "UI-Grayscale shader must live in Resources");
            Assert.AreEqual("UI/Grayscale", shader.name);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run（按上方约定，`group_names=["DisabledGrayscaleTests"]`）。
Expected: FAIL —— `Resources.Load<Shader>` 返回 null（文件不存在）。

- [ ] **Step 3: 写 shader**

创建 `Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader`（以 Unity `UI/Default` 为骨架，保留 stencil / clip-rect / alphaclip / z-test / blend；片元做标准 UI 相乘后去色）：

```hlsl
// uGUI 去色 shader：标准 UI 渲染（sprite × 顶点色）后按亮度去饱和。
// 保留 RectMask2D 裁剪、Mask 蒙版(Stencil)、AlphaClip —— 与 UI/Default 一致，可放进被遮罩的层级。
// 作为"禁用态默认外观"由 DisabledGrayscaleController 换到 graphic 上；离开 Disabled 时换回原材质。
Shader "UI/Grayscale"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Desaturate", Range(0,1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            half _Strength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // 亮度去饱和：_Strength=1 ⇒ 全灰。
                half luma = dot(color.rgb, half3(0.299, 0.587, 0.114));
                color.rgb = lerp(color.rgb, luma.xxx, _Strength);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run（先 `refresh_unity` 让 Unity 导入并编译 shader，再 `read_console types=["error"]` 确认无 shader 编译错误，再 run_tests `group_names=["DisabledGrayscaleTests"]`）。
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add "Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader" \
        "Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader.meta" \
        Tests/EditMode/Controls/DisabledGrayscaleTests.cs \
        Tests/EditMode/Controls/DisabledGrayscaleTests.cs.meta
git commit -m "feat: UI/Grayscale shader（禁用态去色，Resources）"
```

> `.meta` sidecar 由 Unity refresh 生成，务必一并 `git add`（见 markdown-control 评审教训）。

---

## Task 2: 共享子树遍历 `StateSubtree`

把 `StateTintInstaller` 的"遍历 + 剪枝"抽成单一来源，供灰度安装器复用。

**Files:**
- Create: `Runtime/Controls/Internal/StateSubtree.cs`
- Modify: `Runtime/Controls/Internal/StateTintInstaller.cs`
- Test: `Tests/EditMode/Controls/StateSubtreeTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/EditMode/Controls/StateSubtreeTests.cs`：

```csharp
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class StateSubtreeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Btn BuildBtn(string body)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b'>{body}</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        [Test]
        public void CollectGraphics_IncludesBgAndLabel_ExcludesStateReactFalseAndNestedSource()
        {
            var btn = BuildBtn("<Image id='keep' stateReact='false'/><Btn id='inner'>x</Btn><Text id='t'>hi</Text>");
            var graphics = StateSubtree.CollectGraphics(btn.GameObject, btn.Children);

            // bg (root Image) + the 'hi' Text 都在；'keep'（stateReact=false）与内层 Btn 的图形不在。
            var keep = btn.Get<Image>("keep").GameObject.GetComponent<Graphic>();
            var innerBg = btn.Get<Btn>("inner").GameObject.GetComponent<Graphic>();
            var rootBg = btn.GameObject.GetComponent<Graphic>();

            Assert.Contains(rootBg, graphics, "root bg 应在内");
            Assert.IsFalse(graphics.Contains(keep), "stateReact='false' 子树应被剪掉");
            Assert.IsFalse(graphics.Contains(innerBg), "嵌套 IStateSource 应被剪掉");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run（`group_names=["StateSubtreeTests"]`）。
Expected: 编译失败 —— `StateSubtree` 类型不存在。

- [ ] **Step 3: 创建 `StateSubtree`**

创建 `Runtime/Controls/Internal/StateSubtree.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 状态源（Btn/Tab/Toggle）子树的 Graphic 收集 + 剪枝规则的单一来源：跳过 <c>stateReact="false"</c>
    /// 子树与嵌套 <see cref="IStateSource"/> 子树（它们自管图形）。被 <see cref="StateTintInstaller"/>
    /// 的 <c>*Modulate</c> 扇出与 <see cref="DisabledGrayscaleInstaller"/> 的去色共用。
    /// </summary>
    internal static class StateSubtree
    {
        /// <summary>收集 root 子树内未被剪枝的 Graphic（含 targetGraphic 自身）。</summary>
        internal static List<Graphic> CollectGraphics(GameObject root, IReadOnlyList<IControl> children)
        {
            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            var result = new List<Graphic>();
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
                if (!blocked.Contains(g.gameObject))
                    result.Add(g);
            return result;
        }

        /// <summary>把 <c>stateReact="false"</c> 节点与嵌套 <see cref="IStateSource"/> 节点（连同其子树
        /// 全部 Graphic）加入 blocked 集。从 <see cref="StateTintInstaller"/> 迁来，逻辑不变。</summary>
        internal static void CollectBlocked(Control control, HashSet<GameObject> blocked)
        {
            if (control == null) return;
            var optedOut = !control.StateReact;
            var nestedSource = control.GameObject != null
                               && control.GameObject.GetComponent<IStateSource>() != null;
            if (optedOut || nestedSource)
            {
                if (control.GameObject != null)
                {
                    foreach (var g in control.GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
                        blocked.Add(g.gameObject);
                    blocked.Add(control.GameObject);
                }
                return;
            }

            foreach (var child in control.Children)
                CollectBlocked(child as Control, blocked);
        }
    }
}
```

- [ ] **Step 4: 改 `StateTintInstaller` 复用它**

在 `Runtime/Controls/Internal/StateTintInstaller.cs`：删掉私有 `CollectBlocked` 方法（整段移除），把 `Install` 内的

```csharp
            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);
```

改为

```csharp
            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                StateSubtree.CollectBlocked(child as Control, blocked);
```

（其余逻辑——`transition=None`、`isTarget` 判定、绝对色/选中基色只落 target——保持不变。`using System.Collections.Generic;` 仍需要。）

- [ ] **Step 5: 跑测试确认通过 + 无回归**

Run 三组：`group_names=["StateSubtreeTests"]`（新测试 PASS）、`group_names=["BtnStateTests"]`、`group_names=["ToggleStateTests","TabStateTests"]`（既有剪枝测试无回归）。
Expected: 全 PASS。

- [ ] **Step 6: 提交**

```bash
git add Runtime/Controls/Internal/StateSubtree.cs Runtime/Controls/Internal/StateSubtree.cs.meta \
        Runtime/Controls/Internal/StateTintInstaller.cs \
        Tests/EditMode/Controls/StateSubtreeTests.cs Tests/EditMode/Controls/StateSubtreeTests.cs.meta
git commit -m "refactor: 抽出 StateSubtree（状态源子树遍历/剪枝单一来源）"
```

---

## Task 3: 灰度控制器 + 安装器 + 接入 Btn（核心默认行为）

**Files:**
- Create: `Runtime/Controls/Internal/DisabledGrayscaleController.cs`
- Create: `Runtime/Controls/Internal/DisabledGrayscaleInstaller.cs`
- Modify: `Runtime/Controls/Internal/StateColorSet.cs`
- Modify: `Runtime/Controls/Btn.cs:98-110`（`OnAfterApply`）
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`

- [ ] **Step 1: 写失败测试**

往 `DisabledGrayscaleTests.cs` 追加（类内加 helper + 3 个 Btn 核心测试）：

```csharp
        // SelectionState 序号镜像（测试程序集无法命名 protected 嵌套类型）。
        private const int Normal = 0;
        private const int Disabled = 4;

        private static Btn BuildBtn(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b' {attrs}>Hi</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        private static UnityEngine.UI.Image BgOf(Btn b) => b.GameObject.GetComponent<UnityEngine.UI.Image>();
        private static TMPro.TMP_Text LabelOf(Btn b) => b.GameObject.GetComponentInChildren<TMPro.TMP_Text>();
        private static PromptUGUI.Controls.Internal.PuiButton PuiOf(Btn b)
            => b.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiButton>();

        private static Color Gray(Color c)
        {
            var l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(l, l, l, c.a);
        }

        private static void AssertColorEq(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), "a");
        }

        [Test]
        public void PlainBtn_Disabled_DesaturatesBgAndLabel_RevertsOnNormal()
        {
            var btn = BuildBtn();
            var bg = BgOf(btn);
            var label = LabelOf(btn);
            var labelBase = label.color;
            var pui = PuiOf(btn);

            pui.SimulateState(Disabled);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "bg 应换成灰度材质");
            AssertColorEq(Gray(labelBase), label.color);

            pui.SimulateState(Normal);
            Assert.AreEqual(bg.defaultMaterial, bg.material, "还原回默认材质");
            AssertColorEq(labelBase, label.color);
        }

        [Test]
        public void PlainBtn_DefaultGrayscale_KeepsColorTintTransition()
        {
            var btn = BuildBtn();
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, PuiOf(btn).transition,
                "灰度默认不得翻 transition=None（hover/press 反馈保留）");
        }

        [Test]
        public void InteractableFalse_AppliesGrayscaleImmediately()
        {
            var btn = BuildBtn("interactable='false'");
            Assert.AreEqual("UI/Grayscale", BgOf(btn).material.shader.name,
                "interactable='false' 首装即处于 Disabled，订阅重放应立即去色");
        }
```

- [ ] **Step 2: 跑测试确认失败**

Run（`group_names=["DisabledGrayscaleTests"]`）。
Expected: 编译失败 —— `DisabledGrayscaleInstaller` 等类型不存在。

- [ ] **Step 3: 加 `StateColorSet.NoneToNull`**

在 `Runtime/Controls/Internal/StateColorSet.cs` 类内（任意位置，例如 `ResolveModulates` 之后）加：

```csharp
        /// <summary>把禁用槽的 <c>none</c> 哨兵归一化为 null（不进颜色管线，避免 <see cref="UI.Theme.Resolve"/>
        /// 对非颜色值抛异常）。仅用于 <c>disabledModulate</c>："none" ⇒ 显式关闭禁用态视觉。</summary>
        internal static string NoneToNull(string v)
            => string.Equals(v, "none", System.StringComparison.OrdinalIgnoreCase) ? null : v;
```

- [ ] **Step 4: 创建 `DisabledGrayscaleController`**

创建 `Runtime/Controls/Internal/DisabledGrayscaleController.cs`：

```csharp
using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 当所属 <see cref="IStateSource"/> 进入 <see cref="InteractState.Disabled"/> 时，把（剪枝后的）
    /// 子树去色：非 TMP <see cref="Graphic"/> 换共享灰度材质，<see cref="TMP_Text"/> 把 <c>color</c> 置成
    /// 其亮度灰；离开 Disabled 还原。作者未写任何 <c>disabled*</c> 时的默认禁用外观（由
    /// <see cref="DisabledGrayscaleInstaller"/> 装上）。与 <c>transition</c> 无关——靠 OnState 流驱动。
    /// </summary>
    /// <remarks>
    /// 原始材质/颜色 capture-once：re-<see cref="Configure"/>（ReSolve）不重捕，避免把"禁用中"的灰度态
    /// 误当原始态。每次访问 graphic 前判空（销毁安全，呼应 <see cref="StateTintReactor"/>）。
    /// </remarks>
    internal sealed class DisabledGrayscaleController : MonoBehaviour
    {
        private const string GrayscaleResourcePath = "PromptUGUI/Material/UI-Grayscale";
        private static Material _sharedMat;

        /// <summary>进程内共享灰度材质：从 Resources 加载 shader 后懒建一份。</summary>
        internal static Material SharedMaterial
        {
            get
            {
                if (_sharedMat == null)
                {
                    var shader = Resources.Load<Shader>(GrayscaleResourcePath);
                    if (shader != null) _sharedMat = new Material(shader) { name = "UI-Grayscale (shared)" };
                }
                return _sharedMat;
            }
        }

        private readonly struct Captured
        {
            public readonly Graphic Graphic;
            public readonly Material Material;  // 原材质（非 TMP）
            public readonly Color Color;        // 原颜色（TMP）
            public readonly bool IsTmp;
            public Captured(Graphic g, Material m, Color c, bool isTmp)
            { Graphic = g; Material = m; Color = c; IsTmp = isTmp; }
        }

        private readonly Dictionary<Graphic, Captured> _captured = new();
        private IStateSource _source;
        private IDisposable _sub;
        private bool _grayed;

        public void Configure(IReadOnlyList<Graphic> graphics)
        {
            // 先捕获原始态，再订阅：订阅会同步重放当前状态，首装即 Disabled 时必须先有原始态可还原。
            foreach (var g in graphics)
            {
                if (g == null || _captured.ContainsKey(g)) continue;
                var tmp = g as TMP_Text;
                _captured[g] = tmp != null
                    ? new Captured(g, null, tmp.color, true)
                    : new Captured(g, g.material, default, false);
            }

            if (_source == null)
            {
                // includeInactive：源可能在初始隐藏的 TabBar 绑定页上（同 StateTintReactor）。
                _source = GetComponentInParent<IStateSource>(true);
                if (_source != null) _sub = _source.OnState.Subscribe(OnState);
            }
            else
            {
                // re-Configure（ReSolve）：属性管线可能已把材质复位（如 tint= setter）。按当前 _grayed
                // 强制重涂全部（含本次新捕获的 graphic），不走 OnState 的去抖。
                ApplyAll();
            }
        }

        private void OnState(InteractState state)
        {
            var gray = state == InteractState.Disabled;
            if (gray == _grayed) return;   // 仅在跨入/跨出 Disabled 时动手（避免 hover/press 每次重写材质）
            _grayed = gray;
            ApplyAll();
        }

        private void ApplyAll()
        {
            foreach (var kv in _captured)
            {
                var c = kv.Value;
                if (c.Graphic == null) continue;   // 销毁安全
                if (c.IsTmp)
                    ((TMP_Text)c.Graphic).color = _grayed ? Desaturate(c.Color) : c.Color;
                else
                    c.Graphic.material = _grayed ? SharedMaterial : c.Material;
            }
        }

        private static Color Desaturate(Color c)
        {
            var luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(luma, luma, luma, c.a);
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _sub = null;
        }
    }
}
```

- [ ] **Step 5: 创建 `DisabledGrayscaleInstaller`**

创建 `Runtime/Controls/Internal/DisabledGrayscaleInstaller.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 装"禁用态默认去色"：中和 Selectable 内置 disabledColor（保留 transition=ColorTint 的 hover/press，
    /// 但不让内置 disabledColor 在灰度下二次压暗+半透）、按 <see cref="StateSubtree"/> 收集子树 graphic、
    /// 装/复用 root 上的 <see cref="DisabledGrayscaleController"/>。仅在作者未声明任何 <c>disabled*</c>
    /// 时由 Btn/Tab/Toggle 调用。幂等（ReSolve 复用同一控制器）。
    /// </summary>
    internal static class DisabledGrayscaleInstaller
    {
        public static void Install(GameObject root, Selectable selectable, IReadOnlyList<IControl> children)
        {
            var colors = selectable.colors;
            colors.disabledColor = Color.white;
            selectable.colors = colors;

            var graphics = StateSubtree.CollectGraphics(root, children);
            var controller = root.GetComponent<DisabledGrayscaleController>()
                             ?? root.AddComponent<DisabledGrayscaleController>();
            controller.Configure(graphics);
        }
    }
}
```

- [ ] **Step 6: 接入 `Btn.OnAfterApply`**

在 `Runtime/Controls/Btn.cs` 的 `OnAfterApply`：

把
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, null, _disabledModulate);
```
改为
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, null, StateColorSet.NoneToNull(_disabledModulate));
```

并在方法**末尾**（`if (_pressedSprite != null || _disabledSprite != null) ...` 之后）追加：

```csharp
            // 默认禁用外观：作者未声明任何 disabled* 时整控件去色。与 transition 无关（ColorTint/None 皆可）。
            if (string.IsNullOrWhiteSpace(_disabledColor)
                && string.IsNullOrWhiteSpace(_disabledModulate)
                && _disabledSprite == null)
                DisabledGrayscaleInstaller.Install(GameObject, _btn, Children);
```

- [ ] **Step 7: 跑测试确认通过**

Run（`refresh_unity` → `read_console types=["error"]` → run_tests `group_names=["DisabledGrayscaleTests"]`）。
Expected: 三个新测试 + Task 1 的 shader 测试全 PASS。

- [ ] **Step 8: 提交**

```bash
git add Runtime/Controls/Internal/DisabledGrayscaleController.cs Runtime/Controls/Internal/DisabledGrayscaleController.cs.meta \
        Runtime/Controls/Internal/DisabledGrayscaleInstaller.cs Runtime/Controls/Internal/DisabledGrayscaleInstaller.cs.meta \
        Runtime/Controls/Internal/StateColorSet.cs Runtime/Controls/Btn.cs \
        Tests/EditMode/Controls/DisabledGrayscaleTests.cs
git commit -m "feat: Btn 禁用态默认去色（DisabledGrayscaleController/Installer）"
```

---

## Task 4: Btn 边界 —— 覆盖 / `none` / 剪枝

**Files:**
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`
- （仅测试，实现已在 Task 3 完成；红→绿验证判定与剪枝正确）

- [ ] **Step 1: 写测试**

往 `DisabledGrayscaleTests.cs` 追加：

```csharp
        private static Btn BuildBtnXml(string attrs, string body)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Btn id='b' {attrs}>{body}</Btn></Screen></PromptUGUI>");
            return UI.Open("S").Get<Btn>("b");
        }

        [Test]
        public void DisabledColor_Authored_SuppressesGrayscale()
        {
            var btn = BuildBtn("disabledColor='#800000'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "写了 disabledColor 不应装灰度控制器");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "走颜色路径，bg 不换灰度材质");
        }

        [Test]
        public void DisabledModulateColor_Authored_SuppressesGrayscale()
        {
            var btn = BuildBtn("disabledModulate='#888888'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material);
        }

        [Test]
        public void DisabledSprite_Authored_SuppressesGrayscale()
        {
            var stub = UnityEngine.Sprite.Create(UnityEngine.Texture2D.whiteTexture,
                new UnityEngine.Rect(0, 0, 1, 1), UnityEngine.Vector2.zero);
            UI.SpriteResolver = _ => stub;
            var btn = BuildBtn("disabledSprite='ui:x'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "disabledSprite 走 overrideSprite，不换灰度材质");
        }

        [Test]
        public void DisabledModulateNone_OptsOut_NoGrayscale_NoColor_NoThrow()
        {
            var btn = BuildBtn("disabledModulate='none'");
            Assert.IsNull(btn.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>(),
                "none = 显式关，不装灰度控制器");
            Assert.AreEqual(UnityEngine.UI.Selectable.Transition.ColorTint, PuiOf(btn).transition,
                "none 不应触发颜色路径（transition 仍 ColorTint）");
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(BgOf(btn).defaultMaterial, BgOf(btn).material, "none：禁用态无任何表现");
        }

        [Test]
        public void StateReactFalse_Child_NotDesaturated()
        {
            var btn = BuildBtnXml("", "<Image id='keep' color='#FF0000' stateReact='false'/>");
            var keep = btn.Get<Image>("keep").GameObject.GetComponent<UnityEngine.UI.Image>();
            var before = keep.material;
            PuiOf(btn).SimulateState(Disabled);
            Assert.AreEqual(before, keep.material, "stateReact='false' 子节点禁用时不换材质");
        }

        [Test]
        public void NestedBtn_IsBoundary_InnerNotDesaturatedByOuter()
        {
            var outer = BuildBtnXml("", "<Btn id='inner'>x</Btn>");
            var inner = outer.Get<Btn>("inner");
            var innerBg = inner.GameObject.GetComponent<UnityEngine.UI.Image>();
            var before = innerBg.material;
            PuiOf(outer).SimulateState(Disabled);
            Assert.AreEqual(before, innerBg.material, "嵌套 Btn 图形不被外层去色");
        }
```

> 说明：`Image`、`Color`、`Selectable` 等类型用全限定名以免与文件顶部 `using` 冲突；如该测试文件已 `using UnityEngine;`/`UnityEngine.UI;` 可酌情简化。

- [ ] **Step 2: 跑测试确认通过**

Run（`group_names=["DisabledGrayscaleTests"]`）。
Expected: 全 PASS（实现已在 Task 3）。若某条失败，按 systematic-debugging 定位判定/剪枝逻辑，勿改测试将就。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Controls/DisabledGrayscaleTests.cs
git commit -m "test: Btn 禁用去色边界（覆盖/none/剪枝）"
```

---

## Task 5: Btn capture-once 跨 ReSolve

**Files:**
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`

- [ ] **Step 1: 写测试**

追加：

```csharp
        [Test]
        public void CaptureOnce_DisabledThenReSolve_ThenEnable_RevertsToDefault()
        {
            var btn = BuildBtn("interactable='false'");  // 持久禁用
            var bg = BgOf(btn);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "前置：已去色");

            UI.Variants.Set("dark", true);   // ReSolve（OnAfterApply 重跑）
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "ReSolve 中仍禁用 → 维持去色");

            btn.Interactable = true;          // 重新启用
            Assert.AreEqual(bg.defaultMaterial, bg.material,
                "capture-once：还原回原始默认材质，而非卡在灰度");
        }

        [Test]
        public void CaptureOnce_WithTintLinear_RevertsToAuthoredMaterial()
        {
            var btn = BuildBtn("interactable='false' tint='linear'");
            var bg = BgOf(btn);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "禁用 → 去色（覆盖 linear）");

            UI.Variants.Set("dark", true);    // tint setter 重跑把材质复位成 linear，灰度须重新盖回
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name, "ReSolve 后仍禁用 → 重新去色");

            btn.Interactable = true;
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name,
                "还原回作者材质（linear），而非默认或灰度");
        }
```

- [ ] **Step 2: 跑测试确认通过**

Run（`group_names=["DisabledGrayscaleTests"]`）。
Expected: PASS。`CaptureOnce_WithTintLinear...` 专门验证 §3.3 的"re-Configure 强制 ApplyAll 重新盖回 + 还原到作者材质"。

- [ ] **Step 3: 提交**

```bash
git add Tests/EditMode/Controls/DisabledGrayscaleTests.cs
git commit -m "test: Btn 禁用去色 capture-once 跨 ReSolve"
```

---

## Task 6: 接入 Tab

**Files:**
- Modify: `Runtime/Controls/Tab.cs:339-353`（`OnAfterApply`）
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`

- [ ] **Step 1: 写失败测试**

追加（Tab 在 `<TabBar>` 下，路径 `bar/t`；其底图在 Tab 自身 GameObject 上）：

```csharp
        private static Tab BuildTab(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><TabBar id='bar'><Tab id='t' {attrs}>Edit</Tab></TabBar></Screen></PromptUGUI>");
            return UI.Open("S").Get<Tab>("bar/t");
        }

        // Tab/Toggle 的状态源是 PuiToggle。
        private static PromptUGUI.Controls.Internal.PuiToggle PuiToggleOn(MonoBehaviour ctrlGo)
            => ctrlGo.GetComponent<PromptUGUI.Controls.Internal.PuiToggle>();

        [Test]
        public void Tab_Disabled_DesaturatesBg_RevertsOnNormal()
        {
            var tab = BuildTab();
            var bg = tab.GameObject.GetComponent<UnityEngine.UI.Image>();
            var pui = tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiToggle>();

            pui.SimulateState(Disabled);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            pui.SimulateState(Normal);
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }

        [Test]
        public void Tab_DisabledModulateNone_OptsOut()
        {
            var tab = BuildTab("disabledModulate='none'");
            Assert.IsNull(tab.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
        }
```

> 若 `PuiToggle.SimulateState(int)` 不存在（仅 PuiButton 有），改用 `tab.Interactable = false;` 触发 Disabled、`tab.Interactable = true;` 还原，并相应断言（见 Step 2 备注）。

- [ ] **Step 2: 跑测试确认失败**

Run（`group_names=["DisabledGrayscaleTests"]`）。
Expected: 两个 Tab 测试 FAIL（Tab 尚未接入）。

> 备注：先确认 `PuiToggle` 是否有 `SimulateState` 测试钩子——
> `mcp__UnityMCP__find_in_file` 或 `grep -n "SimulateState\|InitStateBroadcast" Runtime/Controls/Internal/PuiToggle.cs`。
> 无则用 `Interactable` 真切换驱动状态（`Interactable=false` → PuiToggle.interactable=false → DoStateTransition(Disabled)）。

- [ ] **Step 3: 接入 `Tab.OnAfterApply`**

在 `Runtime/Controls/Tab.cs` 的 `OnAfterApply`：

把
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
```
改为
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, _selectedModulate, StateColorSet.NoneToNull(_disabledModulate));
```

并在 `ApplySelectedSprite();` 之后追加：

```csharp
            if (string.IsNullOrWhiteSpace(_disabledColor) && string.IsNullOrWhiteSpace(_disabledModulate))
                DisabledGrayscaleInstaller.Install(GameObject, _toggle, Children);
```

（`using PromptUGUI.Controls.Internal;` 已在文件顶部；如无则加。）

- [ ] **Step 4: 跑测试确认通过**

Run（`group_names=["DisabledGrayscaleTests"]` + 回归 `group_names=["TabStateTests"]`）。
Expected: 全 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Tab.cs Tests/EditMode/Controls/DisabledGrayscaleTests.cs
git commit -m "feat: Tab 禁用态默认去色"
```

---

## Task 7: 接入 Toggle

**Files:**
- Modify: `Runtime/Controls/Toggle.cs:203-214`（`OnAfterApply`）
- Test: `Tests/EditMode/Controls/DisabledGrayscaleTests.cs`

- [ ] **Step 1: 写失败测试**

追加（Toggle 底图在子节点 `Background` 上，见 ImageTintTests）：

```csharp
        private static Toggle BuildToggle(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><Toggle id='t' {attrs}>On</Toggle></Screen></PromptUGUI>");
            return UI.Open("S").Get<Toggle>("t");
        }

        [Test]
        public void Toggle_Disabled_DesaturatesBg_RevertsOnEnable()
        {
            var tog = BuildToggle();
            var bg = tog.GameObject.transform.Find("Background").GetComponent<UnityEngine.UI.Image>();

            tog.Interactable = false;
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            tog.Interactable = true;
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }

        [Test]
        public void Toggle_DisabledModulateNone_OptsOut()
        {
            var tog = BuildToggle("disabledModulate='none'");
            Assert.IsNull(tog.GameObject.GetComponent<PromptUGUI.Controls.Internal.DisabledGrayscaleController>());
        }
```

- [ ] **Step 2: 跑测试确认失败**

Run（`group_names=["DisabledGrayscaleTests"]`）。
Expected: 两个 Toggle 测试 FAIL。

- [ ] **Step 3: 接入 `Toggle.OnAfterApply`**

在 `Runtime/Controls/Toggle.cs` 的 `OnAfterApply`：

把
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, _selectedModulate, _disabledModulate);
```
改为
```csharp
            var mod = StateColorSet.ResolveModulates(_hoverModulate, _pressedModulate, _selectedModulate, StateColorSet.NoneToNull(_disabledModulate));
```

并在 `_bgReactor = StateTintInstaller.Install(...);` 之后追加：

```csharp
            if (string.IsNullOrWhiteSpace(_disabledColor) && string.IsNullOrWhiteSpace(_disabledModulate))
                DisabledGrayscaleInstaller.Install(GameObject, _toggle, Children);
```

- [ ] **Step 4: 跑测试确认通过**

Run（`group_names=["DisabledGrayscaleTests"]` + 回归 `group_names=["ToggleStateTests","PuiToggleTests"]`）。
Expected: 全 PASS。

- [ ] **Step 5: 提交**

```bash
git add Runtime/Controls/Toggle.cs Tests/EditMode/Controls/DisabledGrayscaleTests.cs
git commit -m "feat: Toggle 禁用态默认去色"
```

---

## Task 8: PlayMode 冒烟

**Files:**
- Create: `Tests/PlayMode/Controls/DisabledGrayscalePlayTests.cs`

- [ ] **Step 1: 写测试**

创建 `Tests/PlayMode/Controls/DisabledGrayscalePlayTests.cs`（活 Canvas + EventSystem 下禁用按钮 → bg 灰度材质、无报错）：

```csharp
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class DisabledGrayscalePlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator DisabledBtn_InLiveCanvas_UsesGrayscaleMaterial()
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                "<Screen name='S'><Btn id='b' interactable='false'>Hi</Btn></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;  // 走一帧，确保布局/状态稳定

            var bg = screen.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name);

            screen.Get<Btn>("b").Interactable = true;
            yield return null;
            Assert.AreEqual(bg.defaultMaterial, bg.material);
        }
    }
}
```

> 若 PlayMode 测试程序集对某 `using`/类型不可见，对照同目录现有 PlayMode 测试（如 `Tests/PlayMode/Controls/` 下既有文件）的引用与命名空间补齐。

- [ ] **Step 2: 跑 PlayMode 测试**

Run: `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], group_names=["DisabledGrayscalePlayTests"])` → 轮询 `get_test_job`。
Expected: PASS。

> PlayMode runner 在本环境偶发退化/卡住（见 memory）：若 "failed to initialize" 或挂起，先 `read_console`、确认场景已保存、必要时请用户重启 Unity，再重试。

- [ ] **Step 3: 提交**

```bash
git add Tests/PlayMode/Controls/DisabledGrayscalePlayTests.cs Tests/PlayMode/Controls/DisabledGrayscalePlayTests.cs.meta
git commit -m "test: PlayMode 禁用去色冒烟"
```

---

## Task 9: 文档（states.md）

**Files:**
- Modify: `.claude/skills/authoring-promptugui-xml/reference/states.md`
- Modify（如有禁用 stub）: `.claude/skills/authoring-promptugui-xml/SKILL.md`

- [ ] **Step 1: 改 states.md**

在 `reference/states.md` 关于 `interactable="false"` / disabled 的段落（含"`disabledColor` / `disabledModulate` apply"那句，见现文件第 31 行附近）后，新增"默认禁用外观"说明：

> **默认禁用外观（灰度）。** 当 `<Btn>` / `<Tab>` / `<Toggle>` 进入 Disabled 状态且作者**未声明任何** `disabledColor` / `disabledModulate` / `disabledSprite` 时，整个控件（背景 + 文字 + 图标）**按其 sprite 形状真·去色（灰度）** —— 这是 shader 实现的去饱和，区别于 `*Modulate` 的颜色乘法（乘法算不出灰度）。扇出范围与剪枝同 `*Modulate`（跳过 `stateReact="false"` 子树与嵌套 Btn/Tab/Toggle）。作者无需逐个设置子节点。
>
> - **覆盖**：写任一 `disabledColor` / `disabledModulate`（颜色）/ `disabledSprite`（仅 Btn）即取代灰度默认，走各自路径。
> - **关闭**：`disabledModulate="none"` ⇒ 禁用态无任何视觉变化。
>
> 灰度不接管 hover/press —— 二者仍走 uGUI 内置 ColorTint，灰度只在 Disabled 叠加。

- [ ] **Step 2: 同步 SKILL.md（如需要）**

检查 `.claude/skills/authoring-promptugui-xml/SKILL.md` 主文档是否有 disabled / states 的速查行或 stub 指针；若提到禁用行为，补一句"默认灰度（详见 reference/states.md）"。无则跳过。

- [ ] **Step 3: 提交**

```bash
git add .claude/skills/authoring-promptugui-xml/reference/states.md \
        .claude/skills/authoring-promptugui-xml/SKILL.md
git commit -m "docs: states.md 禁用态默认去色"
```

---

## Task 10: 全量验证 + lint

**Files:** 无（验证 + 收尾）

- [ ] **Step 1: 跑全部 EditMode**

Run: `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])` → 轮询。
Expected: 全 PASS（含既有 1870+ 条）。若有因"默认禁用外观变了"而失败的旧测试（断言禁用旧观感的，应极少），按 systematic-debugging 确认是预期行为变更后再更新该测试——**不要**为凑绿而弱化断言。

- [ ] **Step 2: 跑 EditorOnly + PlayMode 全量**

Run:
- `mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditorOnly"])`
- `mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"])`

Expected: 全 PASS。

> 改了默认皮肤渲染/材质相关行为时，**全跑** PlayMode（含 `ImageTests` 等），不要只跑 targeted group——用户曾抓到漏跑的 PlayMode 断言（见 memory `feedback_verify_full_suites_not_groups`）。

- [ ] **Step 3: lint**

```bash
cd .lint && dotnet restore PromptUGUI.Lint.slnx
dotnet format whitespace PromptUGUI.Lint.slnx
dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 无变更 / 无 warning。有则修。
（无新增/编辑 `.ui.xml`，跳过 UIXmlLint。）

- [ ] **Step 4: 收尾提交（若 lint 有改动）**

```bash
git add -A && git commit -m "style: lint 禁用去色"
```

---

## Self-Review（写计划后自检）

**1. Spec 覆盖：**
- §3.1 shader → Task 1 ✓
- §3.2 共享遍历 → Task 2 ✓
- §3.3 控制器（capture-once / 销毁安全 / 先捕获后订阅 / re-Configure 强制重涂）→ Task 3 Step 4 + Task 5 ✓
- §3.4 中和 disabledColor → Task 3 Step 5（Installer）✓
- §3.5 安装点（Btn/Tab/Toggle）→ Task 3/6/7 ✓
- §2 判定 + none 归一化 → Task 3（Btn）/6（Tab）/7（Toggle）+ Task 4 none 测试 ✓
- §4 测试 1–12：1/2 默认+还原(T3)、3 disabledColor(T4)、4 modulate(T4)、5 disabledSprite(T4)、6 none(T4)、7 stateReact(T4)、8 嵌套源(T4)、9 capture-once(T5)、10 transition 保留(T3)、11 Tab/Toggle(T6/T7)、12 资源加载(T1) ✓；PlayMode 冒烟(T8) ✓
- §5 文档 → Task 9 ✓

**2. Placeholder 扫描：** 无 TBD/TODO；每个改动步骤都给了完整代码与具体命令。✓

**3. 类型/命名一致性：** `DisabledGrayscaleController` / `DisabledGrayscaleInstaller.Install` / `StateSubtree.CollectGraphics` / `StateSubtree.CollectBlocked` / `StateColorSet.NoneToNull` / `DisabledGrayscaleController.SharedMaterial` 在定义与调用处一致；shader 名 `"UI/Grayscale"` 在 shader、material 加载、断言三处一致；Resources 路径 `"PromptUGUI/Material/UI-Grayscale"` 一致。✓

**4. 已知风险（实施时留意）：**
- Tab/Toggle 的 `PuiToggle` 可能无 `SimulateState` 钩子 → Task 6 Step 2 已给 `Interactable` 真切换的替代路径。
- 设 `selectable.colors` 会触发一次 `DoStateTransition(current, instant)`；不影响断言（不验证 bg 顶点色）。
- 现有断言"禁用旧观感"的测试若被本变更影响 → Task 10 Step 1 已交代按预期行为更新。
