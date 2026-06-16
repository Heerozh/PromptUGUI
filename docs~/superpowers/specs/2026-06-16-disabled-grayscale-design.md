# 禁用态默认置灰（Disabled-state default grayscale）

**日期**：2026-06-16
**状态**：设计阶段（待 review，未进入实施）
**作用域**：给 `<Btn>` / `<Tab>` / `<Toggle>` 在 **Disabled 状态**补一个**像样的默认外观**——整控件（背景 + 文字 + 图标）真·去色（灰度）。**不新增任何作者可写属性 / 关键字**：灰度是静默的内置默认，作者已有的 `disabledColor` / `disabledModulate` / `disabledSprite` 覆盖它，`disabledModulate="none"` 显式关掉。新增的全是库内部件（一个 UI shader + 一份 material + 一个内部组件）。`authoring-promptugui-xml`（`reference/states.md`）必须更新。
**关联**：建立在 [`2026-06-02-state-color-absolute-modulate-design.md`](2026-06-02-state-color-absolute-modulate-design.md)（`*Color` 绝对 / `*Modulate` 相对乘法 + `StateTintInstaller` 子树扇出/剪枝）与 [`2026-05-30-clickable-state-visuals-design.md`](2026-05-30-clickable-state-visuals-design.md)（`IStateSource` / `StateBroadcaster` / `InteractState`）之上。换材质范式复用 `Internal/ImageTint.cs` + `Resources/PromptUGUI/Material/UI-LinearLightTint.shader`。不改模态、不改其它控件。

---

## 1. 背景与目标

禁用一个 `<Btn>` 目前**几乎没有可见表现**：

- `Btn.Interactable = false` 只设 `CanvasGroup.interactable/blocksRaycasts`（基类，**不动 alpha**）+ `PuiButton.interactable = false`。
- 唯一的禁用视觉来自 uGUI `Button` 内置 ColorTint —— `disabledColor ≈ 灰(0.78)+50% alpha`，且**只作用在 `targetGraphic`（背景）上，不碰 Label/图标**，所以文字始终亮着、"读起来不像禁用"。
- 更糟：只要该控件设了任何 `*Color` / `*Modulate` / `pressedSprite` / `disabledSprite`，`transition` 就被翻成 `None`，此时若没写 `disabledColor` / `disabledModulate`，禁用态**完全无表现**（`StateTintReactor` 对 Disabled 回落到 base 色）。

**目标**：禁用 Btn/Tab/Toggle 时，**整个控件按其 sprite 形状去色（灰度）**，作者零配置即获得统一、明确的禁用观感；现有的 `disabled*` 属性继续作为"覆盖默认"的手段。

**为什么必须是 shader/去色，而不是顶点色乘法**：脱色 = `gray = dot(rgb, 亮度权重)`，是**跨通道**运算；`*Modulate` 的顶点色乘法每通道独立，数学上算不出灰度（只能整体染色/压暗）。所以去色与现有的颜色乘法是两种本质不同的机制。

**非目标**：
- 不新增作者可写属性或颜色关键字（`disabledModulate="monochrome"`、新 `disabledGray` 均被否，见 DG-D1）。
- 不做 rect/全屏后处理（GrabPass / RenderTexture，见 DG-D2）。
- 不把 hover/press 的状态视觉一并接管——本次只新增 Disabled 的默认（见 DG-D3）。
- 不覆盖 `Slider` / `Dropdown` / `InputField`（无 `*Modulate` 表面；如需另开，见 DG-D5）。
- 不处理 TMP 渐变文字 / TMP 自定义描边材质的去色（罕见，已知限制，见 DG-D7）。

## 2. 行为（作者视角）

| 作者写法（Btn/Tab/Toggle） | 禁用时的表现 |
|---|---|
| 三个 `disabled*` 都不写（绝大多数按钮） | **整控件自动去色**（新默认） |
| `disabledColor=<色>` | 用作者指定的禁用底色，**不**去色 |
| `disabledModulate=<色>` | 用作者指定的禁用乘法，**不**去色 |
| `disabledSprite=<图>`（仅 Btn） | 用作者的禁用图，**不**去色 |
| `disabledModulate="none"` | 显式关闭：禁用态无任何效果（不去色、不乘色） |

去色随 `InteractState.Disabled` 进入而生效、离开（回到 Normal/Hover/Pressed/Selected）而还原。扇出范围与剪枝**完全等同 `*Modulate`**：跳过 `stateReact="false"` 子树、跳过嵌套 `IStateSource`（内层 Btn/Tab/Toggle 自管其图形）。作者**不需要**逐个设置子 Image，框架自动遍历子树。

**触发判定**（grayscale 默认生效当且仅当作者未声明任何显式禁用视觉）：

```
wantGrayscale =
    isNullOrWhitespace(disabledColor)
 && !isColorModulate(disabledModulate)     // 非空且非 "none" 才算作者指定的颜色乘法
 && disabledSprite == null                 // 仅 Btn；Tab/Toggle 省略此项
 && !equalsIgnoreCase(disabledModulate, "none")
```

- `disabledModulate` 空 → 去色（前提是 color/sprite 也没写）。
- `disabledModulate="none"` → 不去色、也不乘色（显式关）。
- `disabledModulate="#888"` → 走现有乘法路径，不去色。
- `disabledColor` 或（Btn）`disabledSprite` 任一被写 → 不去色。

**`none` 的解析**：现有 `StateColorSet.ResolveModulates` 会把 `disabledModulate` 丢给 `UI.Theme.Resolve`，而 `"none"` 不是合法颜色会抛异常。因此在各控件 `OnAfterApply` 把传入 `ResolveModulates` 的 disabled 槽做归一化——`"none"` → `null`（等同空），使其既不进颜色管线、又被上面的判定识别为"显式关"。

## 3. 内部实现

### 3.1 UI 灰度 shader + material（新文件）

- `Runtime/Resources/PromptUGUI/Material/UI-Grayscale.shader`，`Shader "UI/Grayscale"`。
- 以 Unity `UI/Default`（`UI-LinearLightTint.shader` 本身也派生自它）为骨架，**保留** Stencil 块、`UNITY_UI_CLIP_RECT`（`_ClipRect` + `UnityGet2DClipping`）、`UNITY_UI_ALPHACLIP`、`unity_GUIZTestMode`、Blend/ColorMask —— 这样 RectMask2D 裁剪、Mask 蒙版、alpha clip 全部自动继承（与 LinearLightTint 同款，已在 WebGL 验证）。
- 片元：标准 UI 相乘后去色——

  ```hlsl
  half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
  half luma   = dot(color.rgb, half3(0.299, 0.587, 0.114));   // 亮度权重
  color.rgb   = lerp(color.rgb, luma.xxx, _Strength);          // _Strength=1 ⇒ 全灰
  ```

  暴露 `_Strength ("Desaturate", Range(0,1)) = 1`，留作日后微调（例如"灰 + 略压暗"），默认 `_Strength=1` 即全灰。
- **不单独提供 `.mat` 资源**：shader 放 Resources（`Resources.Load<Shader>("PromptUGUI/Material/UI-Grayscale")`，不被构建剥离），运行期 `new Material(shader)` 懒建一份进程内共享实例（比 `ImageTint` 加载 `.mat` 更省事、免去手写 `.mat` YAML 的 shader GUID 引用）。shader 的 `.meta` sidecar 一并提交。

### 3.2 共享子树遍历（重构）

`StateTintInstaller` 里"遍历 root 子树 + 剪枝（`stateReact="false"` / 嵌套 `IStateSource`）+ 识别 `targetGraphic`"的逻辑抽成一个共享 helper（如 `StateSubtree.CollectGraphics(root, children, out target)`），供 `StateTintInstaller` 与新的灰度安装器共用，剪枝规则**单一来源**（呼应仓库"规则只写一处"的约定）。`StateTintInstaller` 行为不变（仍含 `transition=None`、绝对色/选中基色只落 target 等逻辑）。

### 3.3 灰度控制器（新组件）

`Runtime/Controls/Internal/DisabledGrayscaleController.cs : MonoBehaviour`（root 上挂一个，**非**每 graphic 一个，见 DG-D6）：

- `Configure(IReadOnlyList<Graphic> graphics)`：刷新受控 graphic 列表；对**首次见到**的 graphic **捕获原始态**（`Dictionary<Graphic, Captured>`，capture-once）：
  - Image/RawImage 等非 TMP `Graphic` → 记 `material`（可能是 `null` / `UI-LinearLightTint`）。
  - `TMP_Text` → 记 `color`。
  - 顺序关键：**先捕获、再订阅**（订阅 `IStateSource.OnState` 会同步重放当前状态；若控件首装即处于 Disabled，重放必须看到已捕获的原始态，否则会把灰度态当原始态捕获——与 `StateTintReactor.Configure` 防的是同一类 bug）。
- 订阅 `GetComponentInParent<IStateSource>(true).OnState`（`includeInactive:true`——源可能在初始隐藏的 TabBar 绑定页上，同 `StateTintReactor`）。
- 回调：`state == Disabled` → 对每个受控 graphic 施加灰度；否则 → 还原。
  - 施加：非 TMP → `graphic.material = 灰度材质`（进程内共享单例，从 Resources 加载 shader 后 `new Material` 懒建）；TMP → `tmp.color = 该色的亮度灰`。
  - 还原：恢复捕获的原始 `material` / `color`。
- **销毁安全**：每次访问 graphic 前判空（`if (g)`）——Carousel 指示点等会在 ReSolve 中重建带受控 graphic 的对象（同 `StateTintReactor` 的 `if (g)` 兜底）。
- capture-once + 每次 `Configure` 重新收集列表：ReSolve 时新出现的 graphic（如绑定内容）补捕获，已捕获的保留首次原始态——避免"禁用中 ReSolve 把灰度态误捕为原始态"（无 `tint=` 的 graphic 在 ReSolve 不会被 applier 复位）。

### 3.4 中和 Unity 自带 disabledColor

灰度保留 `transition = ColorTint`（见 DG-D3），故 Unity 仍会在 Disabled 时把 bg 顶点色染成内置 `disabledColor`（灰 0.5α），叠在灰度 shader 上 → 过暗 + 半透。安装灰度时把该 `Selectable.colors.disabledColor = Color.white`（其余 normal/highlighted/pressed/selected 不动），让 Unity 的禁用=无变化、灰度独占禁用观感、hover/press 反馈照旧。幂等设置，不还原（Variant 在默认灰度↔显式禁用色之间来回切是罕见边缘，且一旦切到显式色 `StateTintInstaller` 会把 transition 翻 None 使此设置失效——既有限制，本次不处理）。

### 3.5 安装点

`Btn` / `Tab` / `Toggle` 的 `OnAfterApply` 末尾，在现有 `StateTintInstaller.Install(...)` 之后：按 §2 判定 `wantGrayscale`；为真则 `DisabledGrayscaleInstaller.Install(GameObject, _btn, Children)`（内部完成 §3.4 中和 + §3.2 收集 + 装 §3.3 控制器）。三个控件共用同一安装器与判定 helper。

## 4. 测试（TDD，先红后绿，EditMode 优先，经 Unity MCP 跑）

EditMode（加在 state-visuals 测试同处，`UI.ResetForTests()` setUp/tearDown）：

1. **默认生效**：纯 `<Btn text="X">`，`Interactable=false`（或 `interactable="false"`）→ bg `Image.material.shader.name == "UI/Grayscale"`；label `TMP.color` == 原色亮度灰。
2. **还原**：`Interactable=true` → bg `material` 回到原始（`null`）；label color 回原色。
3. **`disabledColor` 覆盖**：`<Btn disabledColor="#800000">` 禁用 → bg material **不是**灰度材质；走颜色路径（transition==None）。
4. **`disabledModulate=<色>` 覆盖**：同上，不去色。
5. **`disabledSprite` 覆盖（Btn）**：`<Btn disabledSprite="...">` 禁用 → 无灰度材质（overrideSprite 路径自管）。
6. **`none` 显式关**：`<Btn disabledModulate="none">` 禁用 → 不去色、bg material `null`、label color 不变、**不抛异常**（none 未当颜色解析）、transition 仍为 ColorTint。
7. **剪枝 `stateReact="false"`**：`<Btn><Image stateReact="false"/></Btn>` 禁用 → 该子 Image material 不变。
8. **剪枝嵌套源**：外层 Btn 内嵌 Btn，外层禁用 → 内层 Btn 的图形不被外层去色。
9. **capture-once 跨 ReSolve**：禁用 → 触发 ReSolve（Variant/Theme）→ 再启用 → material 正确回到原始（不卡在灰度）。
10. **hover/press 保留**：纯 Btn（默认灰度）→ `_btn.transition == ColorTint`（未被翻 None）。
11. **Tab/Toggle**：禁用 `<Tab>` / `<Toggle>` → bg 灰度材质；启用还原。
12. **资源加载**：`Resources.Load<Shader>("PromptUGUI/Material/UI-Grayscale")` 非空；运行期共享灰度 material 非空、其 `shader.name == "UI/Grayscale"`。

PlayMode（最小冒烟，遵循"全套验证"约定）：活 Canvas + EventSystem 下，禁用一个按钮 → bg material 为灰度材质、无报错；启用还原。

## 5. 文档更新（同 PR）

`reference/states.md`：

- 新增"默认禁用外观"说明：Btn/Tab/Toggle 禁用时，**未声明任何 `disabled*` 则整控件按 sprite 形状去色（真灰度，shader 实现，区别于 `*Modulate` 的颜色乘法）**；扇出/剪枝同 `*Modulate`。
- 覆盖规则：写任一 `disabledColor` / `disabledModulate` / `disabledSprite` 即取代灰度默认。
- 退出口：`disabledModulate="none"` = 禁用态无表现。
- 改写现有"`interactable="false"` … `disabledColor`/`disabledModulate` apply"一段，补上"未写时默认走灰度"。
- 主文档 `SKILL.md` 若有 disabled 行为的 stub/速查，补一句"默认灰度"指针。

（无新增属性 → 无 XSD 变更；`none` 是字符串属性的取值，不动 schema。无新增 lint 规则。）

## 6. 决策记录

- **DG-D1 灰度是静默默认，不是属性/关键字**。否决 `disabledModulate="monochrome"`（颜色槽塞非颜色关键字、走的也不是乘法，是 code smell）与新增 `disabledGray`（加剧 `disabledColor`/`disabledModulate`/`disabledSprite` 的 disabled* 膨胀）。用户要的是"一个好默认"，不是"一个调灰度的旋钮"——做成默认即零 API 表面，三个旧属性反而升格为"覆盖默认"语义、更自洽。
- **DG-D2 逐 graphic 换材质，不做 rect/后处理**。uGUI 每个 Graphic 各自一次 draw、各带各的 material，父材质不向下级联，**结构上没有轻量的"子树后处理"**；真要做只有 GrabPass（仅 Built-in RP、重、不能合批）或 RenderTexture 重渲染（重、依赖管线）。且矩形叠加会把按钮**透明区下透出的背景一起去色**（圆角四角=四块灰背景），语义是错的。逐 graphic 按 sprite alpha 形状去色更正确，且作者零配置（框架自动遍历，同 `*Modulate` 已有扇出）。
- **DG-D3 保留 `transition=ColorTint`，灰度走独立 `OnState` pass**。不接管 hover/press——保住 Unity 默认的悬停/按下反馈；灰度只在 Disabled 叠加，并把 Unity 内置 `disabledColor` 中和为白防二次压暗+半透。备选"默认也给 hover/press 一份 modulate 让 reactor 全接管"被否：改动面大、会微调现有 hover/press 观感、与本次诉求无关。
- **DG-D4 退出口 `disabledModulate="none"`；任一 `disabled*` 显式值覆盖默认**。默认从"无"变"灰"后需要一个显式关；复用现有属性的哨兵取值，而非新增属性/全局开关（YAGNI）。
- **DG-D5 作用域 Btn/Tab/Toggle**。`*Modulate` 家族所在、共享同一安装路径，天然一致。Slider/Dropdown/InputField 无 `*Modulate` 表面，不在本次；如需另开 spec。
- **DG-D6 单 root 控制器，非每 graphic 一个组件**。默认无处不在，单组件 + 单订阅比逐 graphic 装 `StateTintReactor` 更省；capture-once 字典 + 销毁判空兜底动态/重建。
- **DG-D7 TMP 文字走 `.color` 置灰**。TMP 用自己的 SDF shader，不能套 UI 灰度材质；而 Label 是单色，"去色"=把该色变成其亮度灰，一行搞定。TMP 逐顶点渐变文字、TMP 自定义描边材质不被单独去色——罕见，已知限制。

## 7. 风险与开放问题

- **行为变更**：现有所有未写 `disabled*` 的按钮，禁用观感从"几乎无"变"整体灰"。预期且正是诉求；需更新/补充断言旧行为的测试（若有）。
- **性能**：禁用 graphic 换材质破合批——仅禁用时、数量通常很少，可接受。
- **构建剥离**：shader/material 走 Resources，不被 IL2CPP/WebGL 剥离（LinearLightTint 同款已验证）。
- **色彩空间**：亮度权重 `(0.299,0.587,0.114)` 在采样后的 rgb 上直接算；Linear 色彩空间下感知灰度略有偏差，但对"禁用"提示足够，实施时可按需细化（`_Strength` 与权重均在 shader 内，便于调）。
- **Variant 边缘**：在"默认灰度 ↔ 显式 disabledColor"之间来回切时 `transition` 不回切（既有 `StateTintInstaller` 限制），罕见，本次不处理。
