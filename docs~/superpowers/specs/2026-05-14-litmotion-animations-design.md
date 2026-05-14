# `<Trigger>` / `<Animation>` 设计（LitMotion 驱动）

**日期**：2026-05-14
**状态**：设计阶段（待 review，未进入实施）
**作用域**：新增 `<Trigger>` 基类控件 + `<Animation>` 子类控件。`<Trigger>` 负责解析 `on=` DSL、订阅事件源、暴露 `OnFire` 流；`<Animation>` 在 `OnFire` 时驱动 LitMotion 动画（transform 偏移 / 缩放 / 旋转 / fade / 文本计数 / 逐字色波）。LitMotion 升为硬依赖。
**依赖**：
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §5（内置原语）、§6.2（anchor stretch 结构约束）、§7.6（`[UIAttr]` 反射）
- [`2026-05-13-safearea-builtin-design.md`](2026-05-13-safearea-builtin-design.md) §SA-D5（`Control.OnAfterApply` 钩子）
- `Runtime/Application/ScreenInstantiator.cs`（实例化时机 + parent 关系）
- 外部：[LitMotion](https://github.com/annulusgames/LitMotion) `LMotion` API + `LitMotion.Extensions` 的 TMP / TextMeshPro 绑定

---

## 1. 背景与目标

PromptUGUI 当前没有任何动画基础设施。像素风游戏的常见 UI 行为——菜单入场淡入、重要元素 idle pulse、按钮点击 bounce 反馈、分数 popup 滚动数字、文字色波——目前必须由调用方写 C# + 手挂 LitMotion 来实现。这破坏了"UI 行为在 `.ui.xml` 里声明"的语言定位。

调研结果：
- LitMotion 已经在 `install_for_claude.md` 里被列为推荐依赖，README 也提示安装；但 `package.json` 没声明依赖，asmdef 没引用——本次改成硬依赖。
- 现有 15 个 built-in 控件全部继承 `Control`，通过 `BuiltinPrimitives.Register` 注册。`<SafeArea>` 已经引入 `Control.OnAfterApply` 钩子（每次 `ApplyCommon` 之后调），本设计复用同一钩子做 trigger 订阅初始化。
- `Btn.OnClick` 已经是 R3 `Observable<Unit>`；trigger 的 `on="click"` 解析直接订阅这个流。
- `Text` 内部组件是 TMP_Text（`Runtime/Controls/Text.cs:14`），可直接喂 LitMotion 的 `BindToText` / `BindToTMPCharColor`。

### 1.1 用例

**A. 入场过渡（Open 时自动播一次）**
```xml
<Animation type="fadein" duration="0.3s">
  <Frame><Text>Welcome</Text></Frame>
</Animation>
```

**C. 元素反馈（事件驱动）**
```xml
<Animation type="bounce" on="click" duration="0.2s">
  <Btn id="ok">OK</Btn>
</Animation>
```

**D. Idle loop**
```xml
<Animation type="pulse" on="loop" duration="0.8s">
  <Icon name="warn"/>
</Animation>
```

**文本计数（BindToText）**
```xml
<Animation count="0:100000" format="{0:N0}" duration="2s" easing="out-cubic">
  <Text>0</Text>
</Animation>
```

**逐字色波（BindToTMPCharColor）**
```xml
<Animation char-color="1,1,1,1:1,0.8,0.2,1" char-stagger="0.05s" duration="0.4s">
  <Text>VICTORY</Text>
</Animation>
```

**命名 hook（C# 端订阅，XML 端只声明触发条件）**
```xml
<Trigger id="bonus" on="click@bonus-btn">
  <Frame><Btn id="bonus-btn">领取</Btn></Frame>
</Trigger>
```
```csharp
screen.Get<Trigger>("bonus").OnFire.Subscribe(_ => GameLogic.AwardBonus());
```

### 1.2 目标

1. `<Trigger>` 作为 reusable base 控件，解析统一的 `on=` DSL，暴露 `OnFire`。可独立使用（"命名事件 hook"），也可被未来扩展（`<Sound>` / `<Haptic>` / `<FX>`）继承。
2. `<Animation>` 在 `OnFire` 时驱动 LitMotion，覆盖三族效果：preset（fadein/pulse/bounce/...）、低层 transform（translate/scale/rotate/fade）、文本效果（count/char-color）。
3. 偏移模型：`<Animation>` 自动插入 inner offset RectTransform；外层 base 位置永远由布局系统决定不动，内层被 LitMotion 驱动 → 原坐标和动画偏移完全解耦。
4. 生命周期清晰：`on="open"` 一次性（变体切换不重播）；in-flight motion 在 Variant ReSolve 时不被打断（除非控制属性变了）；Close 时取消 MotionHandle。
5. 全部行为 XML 可声明，C# 端只需要 `OnFire.Subscribe(...)` / `Fire()` 这两个 API。

### 1.3 显式不做

- ❌ **B. 离场动画**：Close 反向播放需要改 `UI.Close` 加 await 等待动画结束，独立 PR
- ❌ **Stagger 糖**：v1 不做 `<Animation stagger="...">` 自动 per-child wrapping；用户写多个 sibling `<Animation>` 显式 `delay=`
- ❌ **ScriptableObject 引用**：`type="@key"` 在解析期被识别但运行时报 "not implemented in v1"；保留 sugar 入口但不实现
- ❌ **AnimationCurve 自定义曲线**：只支持 named easing（LitMotion 内置枚举）
- ❌ **跨 wrapper 的 trigger source selector**：`on="click@id"` 的 id 严格在 Animation 子树 scope 内查找
- ❌ **per-char position / scale wave**：`char-color` 是 v1 唯一的逐字效果；其他逐字效果（jelly / wave-translate）v2 再加
- ❌ **`<Sound>` / `<Haptic>` / `<FX>` 实现**：本 spec 只设计 `<Trigger>` 基类形态以保留继承空间，不在 v1 实现这些子类
- ❌ **Editor inspector 调参**：动画完全靠 XML 属性驱动，不暴露 Inspector 字段

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| ANIM-D1 | LitMotion 依赖形态 | 硬依赖。`package.json` 加 `"com.annulusgames.lit-motion"` 到 dependencies；asmdef 直接 reference `LitMotion` / `LitMotion.Extensions` | 用户明确要求硬依赖。版本 define 不需要；`<Animation>` 无 `#if` 包裹 |
| ANIM-D2 | `<Trigger>` 作为基类 vs 仅 C# abstract base | 同时是 XML 标签 + C# 基类 | 独立可用（命名 hook 模式：XML 声明 trigger，C# 订阅 OnFire），也是 `<Animation>` / 未来 `<Sound>` 的继承基。可注册到 ControlRegistry |
| ANIM-D3 | 子类都是 wrapper 形态（包子树） | 是 | wrapper 划定"事件源 scope" — `on="click"` 在子树内找 Btn。`<Sound>` 未来即使不需要"包"子树，仍走 wrapper 保持一致性。代价：每个 trigger 多一个空 GameObject，可忽略 |
| ANIM-D4 | `on=` DSL 语法 | `open` (默认) / `loop` / `click` / `click@id` / `manual` | 五个值覆盖三类用例 + 命名 hook + 主动 fire。Variant 不允许改 `on=`（parse error） |
| ANIM-D5 | `on="click"` 多 Btn 行为 | parse error，强制 `@id` | 沉默选第一个 Btn 会让作者误以为绑对了；显式 error 更安全 |
| ANIM-D6 | `on="click@id"` id scope | Animation 子树（descendants）scope | wrapper 边界 = trigger source 边界，模型简单 |
| ANIM-D7 | `<Animation>` 内部加 offset proxy 层 | 是 | 外层布局定位、内层动画驱动 → 永不打架，不需要"读 base 加 offset"每帧合成 |
| ANIM-D8 | offset proxy 创建时机 | `Animation.OnAttached()` 创建 child GO，并通过新增的 `Control.ChildHostTransform` 钩子让 ScreenInstantiator 把子节点 parent 到 proxy 而不是外层 | 解决"子节点该 parent 到谁"的问题，让其他控件零侵入 |
| ANIM-D9 | `Control.ChildHostTransform` 钩子 | **新增** virtual property，默认返回 `RectTransform`；`Animation` override 返回 `_offsetProxy` | 跟 `OnAfterApply` 同精神：让特殊容器扩展 ScreenInstantiator 的 parent 决策 |
| ANIM-D10 | XML 三族属性互斥 | preset (`type`) vs low-level transform (`translate/scale/rotate/fade`) vs text-effect (`count/format` 或 `char-color/char-stagger`) — 三族两两互斥；同族内可任意组合 | preset 是 sugar，要微调就全切低层；transform 和 text-effect 是不同目标，混用语义模糊 |
| ANIM-D11 | preset 清单 | `fadein/fadeout/slidein-{left,right,up,down}/slideout-{...}/scalein/scaleout/pulse/bounce/shake` | 覆盖 95% UI 动画需求；命名跟主流 (Animate.css / DOTween) 对齐降低学习成本 |
| ANIM-D12 | easing 默认值 | `out-cubic` | UI 反馈最常用、视觉自然；DOTween 默认也是 OutCubic |
| ANIM-D13 | duration 默认值 | `0.3s` | 凭经验值；像素风游戏短反馈常用 0.15-0.3，长入场 0.3-0.5 |
| ANIM-D14 | `loop=` 语法 | `true` / `yoyo` / `count:N` | 跟 LitMotion `WithLoops` + `LoopType` 直接映射 |
| ANIM-D15 | `on="loop"` 和 `loop=` 关系 | `on="loop"` 隐含 `loop="yoyo"` + Open 时自动 fire；两者同时写以 `loop=` 显式值为准 | "loop" 这个 trigger 类型本质就是"open + 自动循环"，sugar |
| ANIM-D16 | text-effect 的 target= | text 族支持 `target="@id"` 在 **screen 全局 scope** 内找 Text；wrapper 子树有唯一 Text 时可省略；transform 族禁止写 `target=`（target 永远是自己 offset proxy） | transform 必须包子树（变换继承）；text 效果可远程作用（一个 Text 上叠加多种 effect 不需要嵌套包多层） |
| ANIM-D17 | `<Animation>` 多 Text 无 `target=` 行为 | parse error | 跟 `on="click"` 多 Btn 一致 |
| ANIM-D18 | `Text` 控件暴露 `internal TMP_Text TmpComponent` | 是 | Animation 喂 LitMotion BindToText / BindToTMPCharColor 需要拿到底层 TMP_Text；Text 当前只有私有 `_tmp`，加 internal getter |
| ANIM-D19 | `on="open"` 在 Variant ReSolve 时是否重播 | 否，一次性 | 维持"variant 不重建 GameObject、不重启效果"的现有约定。如果作者需要 variant 切换时动画，那是另一种 trigger（譬如 `on="variant:X"`），v2 再说 |
| ANIM-D20 | Variant ReSolve 时 in-flight motion 处理 | 控制属性（`type`/`duration`/`easing`/`loop`/低层 from-to）变了 → cancel 当前 motion + 下次 fire 用新参；属性未变 → motion 不打断 | 减少视觉抖动；变了才重启是直觉行为 |
| ANIM-D21 | `Animation.Fire()` 可在任何 `on=` 模式下被调用 | 是 | `on="manual"` 是默认不自动 fire，但 `Fire()` 不限制——作者要从 C# 强制重播 `on="click"` 的动画也能用 |
| ANIM-D22 | MotionHandle 生命周期 | Animation 持有 `MotionHandle _current`（多 motion 时是 `MotionHandle[]`）。每次 Fire 之前 `TryCancel` 旧 handle；Dispose 时 cancel | 避免泄漏 + 重复触发时旧 motion 被新 motion 覆盖 |
| ANIM-D23 | `char-color` 默认 char range | 全部字符 | TMP 渲染后 `textInfo.characterCount` 获取；char-stagger=0 时所有字符同时；>0 时第 i 个字符 delay = i * stagger |
| ANIM-D24 | `target="@key"` SO 引用入口 | 解析期识别 `@` 前缀，运行时抛 `NotImplementedException("ScriptableObject motion references not implemented in v1")` | 保留语法入口，不实现；未来加 MotionResolver 时只换 setter 实现 |
| ANIM-D25 | trigger 订阅时机 | `OnAfterApply` 第一次被调用时（即首次 ApplyCommon 之后）。后续 ApplyCommon 不重订阅 | 此时所有 wrapper 子树已实例化、Btn 等事件源已 ready |
| ANIM-D26 | Add 块内的 trigger | trigger 在所属 Add 块首次激活时（即 GameObject 首次实例化）订阅；之后 SetActive 切换不影响订阅 | Strategy C 的自然延续 |
| ANIM-D27 | text 族 + Add 块的 target 找不到 | parse error 留到 Apply 时——找不到 Text 抛 RuntimeException with 节点定位 | parse-time 无法静态校验 `target="@id"` 是否解析得到（id 在 Add 块里时尤其难）；运行时报错+source location 是 OK 的妥协 |
| ANIM-D28 | XSD 生成器同步 | 反射驱动；新增 `<Trigger>` / `<Animation>` 自动出现。需要确认 `[UIAttr]` 的复杂 syntax（`translate="x1,y1:x2,y2"`）在 XSD 里作为 `xs:string` 处理，由运行时解析 | M4 已经把 `[UIAttr]` string 默认映射成 xs:string；不需额外改 |
| ANIM-D29 | 测试组织 | EditMode：parse + 结构 + 不需要 LitMotion 实跑的逻辑；PlayMode：LitMotion 实跑 + duration 跳时间步验最终值 | 跟现有控件测试拆法一致 |
| ANIM-D30 | SKILL.md 更新 | `authoring-promptugui-xml/SKILL.md` 加 Trigger/Animation 章节；`scripting-promptugui-csharp/SKILL.md` 加 `OnFire` / `Fire()` 用法 | CLAUDE.md 触发条件：新增 XML 元素 + 公共 C# API → 强制 SKILL 更新 |

---

## 3. 改动面

### 3.1 包元数据

**`package.json`：**
```json
{
  "dependencies": {
    "com.unity.ugui": "2.0.0",
    "com.annulusgames.lit-motion": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion"
  }
}
```

**`Runtime/PromptUGUI.Runtime.asmdef`：** 加 `LitMotion` 和 `LitMotion.Extensions` 到 `references`（具体 assembly name 在实施时根据 LitMotion 包确认；若 TMP 绑定在主包则只需 `LitMotion`）。

### 3.2 新文件 `Runtime/Controls/Trigger.cs`

```csharp
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;

namespace PromptUGUI.Controls
{
    public class Trigger : Control
    {
        private readonly Subject<Unit> _fire = new();
        public Observable<Unit> OnFire => _fire;

        private TriggerSpec _spec;
        private System.IDisposable _sourceSub;
        private bool _subscribed;

        [UIAttr]
        public string On { set => _spec = TriggerSpec.Parse(value); }

        internal override void OnAfterApply()
        {
            if (_subscribed) return;
            _subscribed = true;
            InitTriggerSubscription();
        }

        protected virtual void InitTriggerSubscription()
        {
            switch (_spec?.Kind)
            {
                case TriggerKind.Open:   Fire(); break;
                case TriggerKind.Loop:   Fire(); break;
                case TriggerKind.Click:  SubscribeClick(); break;
                case TriggerKind.Manual: /* no auto subscribe */ break;
                case null:               Fire(); break;  // 缺省 = Open
            }
        }

        public void Fire()
        {
            OnTriggerFired();
            _fire.OnNext(Unit.Default);
        }

        protected virtual void OnTriggerFired() { }

        private void SubscribeClick()
        {
            // 在本 Trigger 子树（descendants of this.RectTransform）内查找：
            //   SourceId == null → 唯一 Btn；多个或零个 → InvalidOperationException
            //   SourceId != null → ScopedIds[SourceId]，必须是 Btn；找不到或类型不对 → 异常
            var btn = TriggerSourceResolver.FindBtn(this, _spec.SourceId);
            _sourceSub = btn.OnClick.Subscribe(_ => Fire());
        }

        public override void Dispose()
        {
            _sourceSub?.Dispose();
            _fire.Dispose();
            base.Dispose();
        }
    }
}
```

### 3.3 新文件 `Runtime/Controls/Animation.cs`

```csharp
using LitMotion;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Animation : Trigger
    {
        private RectTransform _offsetProxy;
        private CanvasGroup _cg;
        private MotionHandle[] _current;

        private AnimationSpec _spec;            // 三族属性集成后的解析结果
        private AnimationSpec.Snapshot _lastApplied;  // Variant ReSolve dirty check 基线

        [UIAttr] public string Type      { set => _spec.SetType(value); }
        [UIAttr] public string Translate { set => _spec.SetTranslate(value); }
        [UIAttr] public string Scale     { set => _spec.SetScale(value); }
        [UIAttr] public string Rotate    { set => _spec.SetRotate(value); }
        [UIAttr] public string Fade      { set => _spec.SetFade(value); }
        [UIAttr] public string Count     { set => _spec.SetCount(value); }
        [UIAttr] public string Format    { set => _spec.SetFormat(value); }
        [UIAttr("char-color")]   public string CharColor   { set => _spec.SetCharColor(value); }
        [UIAttr("char-stagger")] public string CharStagger { set => _spec.SetCharStagger(value); }
        [UIAttr] public string Duration  { set => _spec.SetDuration(value); }
        [UIAttr] public string Delay     { set => _spec.SetDelay(value); }
        [UIAttr] public string Easing    { set => _spec.SetEasing(value); }
        [UIAttr] public string Loop      { set => _spec.SetLoop(value); }
        [UIAttr] public string Target    { set => _spec.SetTarget(value); }

        protected override Transform ChildHostTransform => _offsetProxy;  // ANIM-D9

        public override void OnAttached()
        {
            var go = new GameObject("_offsetProxy", typeof(RectTransform));
            go.transform.SetParent(RectTransform, worldPositionStays: false);
            _offsetProxy = (RectTransform)go.transform;
            _offsetProxy.anchorMin = Vector2.zero;
            _offsetProxy.anchorMax = Vector2.one;
            _offsetProxy.offsetMin = Vector2.zero;
            _offsetProxy.offsetMax = Vector2.zero;
            _offsetProxy.pivot = new Vector2(0.5f, 0.5f);
            _cg = GameObject.AddComponent<CanvasGroup>();  // 替换 Control 基类的 lazy CanvasGroup
        }

        internal override void OnAfterApply()
        {
            _spec.Validate();          // 三族互斥 / target 范围 / count vs char-color 互斥等
            // 控制属性变化检测：把 _spec 跟 _lastApplied 比对，变了就 cancel _current
            if (_spec.HasControlChanges(_lastApplied))
                CancelCurrent();
            _lastApplied = _spec.Snapshot();

            base.OnAfterApply();  // 进入 Trigger 订阅 / Open 时立即 Fire
        }

        protected override void OnTriggerFired()
        {
            CancelCurrent();
            _current = AnimationDriver.Play(_spec, _offsetProxy, _cg, ResolveTextTarget());
        }

        private TMP_Text ResolveTextTarget()
        {
            if (_spec.Kind is not TextFamily) return null;
            if (_spec.Target?.StartsWith("@") == true)
            {
                // text 族 target= 走 screen 全局 scope；UI.OwnerScreenOf 是 internal API（同程序集）
                var screen = UI.OwnerScreenOf(this)
                    ?? throw new System.InvalidOperationException(
                        $"<Animation target=\"{_spec.Target}\"> couldn't find owner Screen");
                return screen.Get<Text>(_spec.Target.Substring(1)).TmpComponent;
            }
            return AnimationTargetResolver.FindTextInSubtree(this);  // unique or error
        }

        // ... cancel / dispose
    }
}
```

`AnimationDriver` 是个静态辅助类，把 `AnimationSpec` → 一组 `LMotion.Create(...).BindTo...(...)`，返回 `MotionHandle[]`。这一层把 LitMotion 调用集中在一个文件，便于测试和未来替换 backend。

### 3.4 新文件 `Runtime/Controls/Internal/TriggerSpec.cs`

```csharp
namespace PromptUGUI.Controls.Internal
{
    internal enum TriggerKind { Open, Loop, Click, Manual }

    internal sealed class TriggerSpec
    {
        public TriggerKind Kind;
        public string SourceId;  // 仅 Click + 有 @id 时非空

        public static TriggerSpec Parse(string value)
        {
            // null / "" / "open" → Open
            // "loop"            → Loop
            // "click"           → Click, SourceId=null
            // "click@<id>"      → Click, SourceId="<id>"
            // "manual"          → Manual
            // 其他              → ParseException with source location
        }
    }
}
```

### 3.5 新文件 `Runtime/Controls/Internal/AnimationSpec.cs` + `AnimationDriver.cs`

- `AnimationSpec`：纯 POCO，收集所有 Animation 属性的 raw string，`Validate()` 检查三族互斥、target 合法性、preset 名合法性等。`Snapshot()` 返回结构体用于 ReSolve 时的 dirty check。
- `AnimationDriver`：纯静态，输入 `AnimationSpec` + target RectTransform + CanvasGroup + (optional) TMP_Text，返回 `MotionHandle[]`。LitMotion 的所有 API 调用集中在这。

preset → 低层属性的映射表：

| preset | 等价低层 |
|---|---|
| `fadein`           | `fade="0:1"` |
| `fadeout`          | `fade="1:0"` |
| `slidein-left`     | `translate="-100,0:0,0" fade="0:1"` |
| `slidein-right`    | `translate="100,0:0,0" fade="0:1"` |
| `slidein-up`       | `translate="0,-100:0,0" fade="0:1"` |
| `slidein-down`     | `translate="0,100:0,0" fade="0:1"` |
| `slideout-{...}`   | 上面四条反向 |
| `scalein`          | `scale="0.8:1" fade="0:1"` |
| `scaleout`         | `scale="1:0.8" fade="1:0"` |
| `pulse`            | `scale="1:1.05"` + `loop=yoyo` 隐含 |
| `bounce`           | `scale="0.9:1"` + easing=`out-back` 隐含 |
| `shake`            | `translate="-5,0:5,0"` + `loop=count:4` + easing=`linear` 隐含 |

preset 的"100px 偏移量"是默认值；作者可以同时写 `type="slidein-left" duration="..."` 但**不能**通过 preset 同时写 translate 覆盖距离（互斥），要调整距离就完全切低层 syntax。

### 3.6 改动 `Runtime/Controls/Control.cs`

加一个 virtual `ChildHostTransform`：

```csharp
public abstract class Control : IControl
{
    // ...
    protected internal virtual Transform ChildHostTransform => RectTransform;
    // ScreenInstantiator 实例化子节点时调它作为 parent
}
```

### 3.7 改动 `Runtime/Application/ScreenInstantiator.cs`

`InstantiateRecursive` 在递归子节点时，把 parent 从 `parent` 改为 `controlForParent?.ChildHostTransform ?? parent`。具体在第 193 行附近的 `SetParent` 调用上游。

### 3.8 改动 `Runtime/Controls/Text.cs`

加一个 `internal TMP_Text TmpComponent => _tmp;` getter，让 Animation 拿到底层 TMP_Text 喂 LitMotion BindToText / BindToTMPCharColor。

### 3.9 改动 `Runtime/Application/BuiltinPrimitives.cs`

```csharp
reg.Register<Trigger>("Trigger", null);
reg.Register<Animation>("Animation", null);
```

### 3.10 改动 `.claude/skills/authoring-promptugui-xml/SKILL.md`

新增"Triggers and Animations"章节：
- `<Trigger>` 标签 + `on=` DSL 完整语法表
- `<Animation>` 三族属性表 + preset 清单
- target= 规则（transform 族禁用 vs text 族允许 screen-scope）
- parse-time 错误清单（多 Btn 无 `@id` / 多 Text 无 `target=` / preset+低层混用 / `on=` 不合法值）

### 3.11 改动 `.claude/skills/scripting-promptugui-csharp/SKILL.md`

加 `Trigger.OnFire` / `Animation.Fire()` 用法示例。强调"XML 管什么时候，C# 管做什么"的分工模式。

---

## 4. 测试范围

### 4.1 EditMode（不需要 LitMotion 实跑）

`Tests/EditMode/Controls/TriggerTests.cs`：
- `on=` 解析：所有合法值 + 非法值（如 `"hover"`）抛 ParseException
- `on="click"` 子树无 Btn → parse-time error
- `on="click"` 子树多 Btn 无 `@id` → parse-time error
- `on="click@nonexistent"` → parse-time / instantiate-time error（带 source location）
- 独立 `<Trigger id="x" on="manual">` → 实例化成功，C# `Get<Trigger>("x").OnFire` 可订阅
- `Fire()` 触发 OnFire stream
- Variant 不允许改 `on=`（parse error 复现）

`Tests/EditMode/Controls/AnimationTests.cs`：
- 三族属性互斥：写 `type` + `translate` 同时 → ParseException
- 写 `count` + `char-color` 同时 → ParseException
- preset 不合法名（如 `type="explodeIn"`）→ ParseException
- 低层 syntax：`translate="0,-50:0,0"` 解析成 from/to Vector2
- `scale="0.5:1"` 单值 → (0.5,0.5,0.5):(1,1,1)
- `loop="count:3"` 解析成 LoopType.Restart with 3 loops
- Inner offset proxy GameObject 结构（`_offsetProxy` 存在、anchor stretch、子节点 parent 到它）
- `Animation` + `target="@id"` 跨 wrapper 找 Text 解析（Add 块除外的简单情况）
- Variant ReSolve 时控制属性变化检测：duration 从 0.3 → 0.5 → `_current` 被 cancel
- Variant ReSolve 时属性未变 → motion 不被 cancel

### 4.2 PlayMode（需要 LitMotion 实跑）

`Tests/PlayMode/Controls/AnimationPlayTests.cs`：
- `type="fadein"` Open 自动 fire；等 duration 后 CanvasGroup.alpha == 1
- `type="pulse" on="loop"` 启动后持续 fire（采样多帧验 scale 在 1.0 和 1.05 之间）
- `type="bounce" on="click"`：模拟点击 Btn → fire 一次；duration 后回到初始 scale
- `count="0:1000" format="{0:N0}"` 2s 后 Text.text == "1,000"（依赖 culture，spec 里用 InvariantCulture）
- `char-color="1,1,1,1:1,0,0,1" char-stagger="0.1s"` 验第 0 个字符在 t=duration 时 = 红，第 1 个字符在 t=duration+0.1 时 = 红
- `Fire()` C# 主动触发 `on="manual"` 的 Animation
- Close → MotionHandle 被 cancel（无后台残留）

### 4.3 XSD 生成器

`Tests/EditorOnly/XsdGeneratorTests.cs` 现有反射驱动；只需补 substring assert 验 `<Trigger>` / `<Animation>` 出现在生成的 schema 里，且 `on` / `type` / `translate` 等属性在 attribute 列表。

---

## 5. 风险 / 开放问题

1. **LitMotion asmdef 名字**：实施前需要 clone LitMotion 包确认正确的 assembly references（`LitMotion` vs `LitMotion.Runtime` vs `LitMotion.Animation`，以及 TMP 绑定在哪个分包）。如果分包结构跟预期不符可能要分两步：先核 assembly 名，再写 asmdef 改动。
2. **`Animation` inner offset proxy 跟 LayoutGroup 子节点的交互**：如果 `<Animation>` 是 VStack 的子节点，外层走 LayoutElement 通道（不受 LayoutGroup 直接设置 anchor）。`_offsetProxy` 在外层内部是 stretch 子，理论上 OK 但需 PlayMode 测试覆盖："VStack 里放 Animation 包 Btn"完整路径验视觉对齐。
3. **`char-color` per-char delay 实现**：LitMotion 的 `BindToTMPCharColor` 是单 char 绑定，N 个字符需要 spawn N 个 MotionHandle。如果 Text 字符在动画过程中变化（i18n 切换 / 数字滚动），char index 失效。v1 假设动画期间 Text 不变；运行时如果发现 textInfo.characterCount 在 fire 时 vs. update 时不一致，跳过本帧更新（不抛错），SKILL.md 标 caveat。
4. **`Animation` 嵌套自身（offset proxy 嵌套）**：两层 `<Animation>` 嵌套 → 两个 offset proxy。布局上 OK（每层独立 stretch）；动画驱动两个不同 proxy，互不打架。无需特殊处理，但应在 EditMode 测试覆盖。
5. **`Trigger` 子类的 `[UIAttr]` 继承**：`Animation : Trigger`，Trigger 的 `On` 属性需要被 `Animation` 实例上反射到。当前 `ControlRegistry` 反射逻辑遍历 type hierarchy 应该已 OK，但实施时验一遍。
6. **CanvasGroup 跟 Control 基类的 lazy CanvasGroup 冲突**：Control 基类 `CanvasGroup` getter 是 lazy add。`Animation.OnAttached` 主动加一个 CanvasGroup，跟基类 lazy 拿到的是同一 component（GetComponent 返回已存在的）。OK 但要注释解释。

---

## 6. 实施顺序（写 plan 时细化）

1. 包依赖（package.json + asmdef）+ 编译通
2. `Control.ChildHostTransform` 钩子 + `ScreenInstantiator` 改动 + 现有控件零回归测试
3. `Trigger` 基类（含 TriggerSpec parse + OnFire stream + `on="manual"` / `on="open"` 路径，**不含 click**）+ EditMode 测试
4. `on="click" / click@id` 子树查找 + EditMode 测试
5. `Animation` skeleton（offset proxy + `type="fadein"` 一个 preset 走通 PlayMode）
6. 低层 transform 属性（translate/scale/rotate/fade）+ AnimationDriver + EditMode parse 测试
7. 完整 preset 表 + EditMode 测试
8. text 族（count / char-color）+ Text.TmpComponent 暴露 + PlayMode 测试
9. Variant ReSolve dirty check + EditMode 测试
10. BuiltinPrimitives 注册 + XSD 生成器集成测试
11. SKILL.md 更新（XML + C# 两份）
12. CLAUDE.md 不需要改（不引入新跨包约定）

每步独立测试通过再进下一步；步 3-9 都独立可 ship。
