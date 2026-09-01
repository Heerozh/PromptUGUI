# `blur` / `glow` —— `<Image>` / `<Icon>` 的 sprite 级模糊与外发光（M1）

> 状态：**已与作者对齐，M1 立项**（2026-09-02 定案，见 §9）。
> 相关：`2026-09-01-graphic-reflection-design.md`（同一组叶子 Graphic 标签；§2 否决「自定义材质」的
> 理由 —— 与 `UI-LinearLightTint` / `UI-Grayscale` 抢材质槽 —— 本文用 §2 末尾的先例化解；§4.2 / §4.4
> 的排版 / 点击 / 组合表原则本文沿用）；`2026-08-31-hug-reveal-flip-checked-design.md` §3（`rotation` /
> `flip`：`BaseMeshEffect` 路径、懒挂 / 禁用不销毁）；`2026-08-23-procedural-style-design.md` 与
> `2026-08-28-inner-glow-design.md`（SDF 双层发光曲线 `PuguiApplyOuterGlow`、`ProceduralMaterialCache`
> 按参数集共享材质、`ProceduralPanel`「从内部去色」）；`2026-08-23-glass-fill-design.md`（`GlassBlur`
> 是全屏 RenderGraph Kawase，**不是**逐 sprite 工具；§9「像素风量化」留位本文沿用）；
> `2026-09-01-vertex-gradient-stops-design.md`（`GradientTint` 切条 —— 本文 §4.4 的顶点通道插值约束）。

## 0. 两个需求，一个原语

- **外发光**：选中 / 告警 / 稀有度 / 悬停暗示 —— 图标周围一圈由自身剪影向外衰减的光。`<Frame>` /
  `<Decor>` 的 `glow=` 是解析式 SDF；sprite 没有 SDF，今天做不出来。
- **模糊**：锁定 / 未揭示的物件、失焦、过渡 —— 图标自身像素糊掉。早先评估过，因「复杂」搁置。

两者在数学上是**同一次采样的两个输出**：在 sprite 周边取 N 个 tap，blur 累加 rgba 换掉本体，glow 只用
其中的 alpha 覆盖率 → 衰减曲线 → 上色 → 垫在本体下面。同一个采样核、同一套 atlas 矩形钳制、同一次
quad 外扩、同一条顶点通道、同一份 shader 骨架、同一组测试夹具。合并做比分两次做省一半，M1 两者一起落地。

当初 blur 的「复杂」来自大半径（20px+ 的头像 / 横幅做底图）：单 pass tap 数随 r² 增长，图集在高 mip
串色，Point 像素图 mip 无意义。**M1 只做小半径（≤ ~12px）**，这一档 icon 级发光与锁定态模糊完全覆盖；
大半径走 M2 的 lod / RT 路径，底座不变只换采样核（§10）。

## 1. 问题

- **控件层没有任何东西能画 sprite 的邻域。** `UI/Default` 一个 tap；`UI-LinearLightTint` /
  `UI-Grayscale` 也是逐像素。`GlassBlur` 糊的是相机图像，Screen Space Overlay 画布根本不在里面。
- **图集让「邻域」变成雷区。** sprite 只是大图上的一块矩形：光晕必然越出 sprite 边界（这是它的定义），
  quad 一外扩、uv 一外推就采到邻居；`padding: 4` 只保 4px。所以采样必须按 sprite 自己的 UV 矩形钳制
  —— 这是不可绕开的一步。不是取「真实 sprite 区域」难（那是一行），是**越界后当透明**难。
- **每实例参数无处安放。** `CanvasRenderer` 忽略 `MaterialPropertyBlock`；per-instance 材质破合批；
  `Graphic.material` 槽今天已被 `tint="linear"` 与禁用灰度以「互相覆盖的换材质」方式占用。

## 2. 否决的方案

1. **RT 两 pass（字面上的 blur）**：把 `sprite.textureRect` blit 进小 RT、Kawase 两遍、缓存、`RawImage`
   垫底。任意半径、真高斯，但：RT 生命周期 / 缓存回收、每个发光图标各自一张纹理 → **逐实例破合批**、
   编辑器与播放两套、`GlassBlur` 的 `_BlitTexture` / `_BlitScaleBias` 命名是给 RenderGraph 的还要包一层。
   留给 M2 图集内大半径这一种场景。
2. **导入期烘焙**：`.pxl` 导入器 / `SpriteAtlasSyncer` 顺手烘一张模糊 alpha 进图集。零 shader、零运行时，
   但半径定死、图集翻倍、只覆盖自家管线、不能运行时变色变宽。不排除日后作为 style 选项。
3. **独立 Graphic 垫在 Icon 下面画光晕**（只做 glow 时的最优解，不动 Icon 材质链）：blur 要**替换本体
   像素**，落点必须是 Image 自身；两者合并后此路不通。
4. **`BaseMeshEffect` + `ReserveSlot`**（reflect spec 的路线）：外扩 quad 必须在其它网格效果**之前**
   跑（`GradientTint` 切条后是多顶点网格，逐顶点外推要泛化；`RotateFlipEffect` 之后「外」的方向要
   反算），只能在 `Icon.OnAttached` 预挂一个禁用组件占第一槽 —— 每个 Icon 多一个组件，违背「不写就零
   开销」。**`UnityEngine.UI.Image` 子类在 `OnPopulateMesh` 里后处理**天然排在一切 `IMeshModifier`
   之前，不占槽、不加组件。
5. **参数全走顶点通道（uv1–uv3）**：合批最好，但 TexCoord3 全画布多一个 float4，且与 Frame / Decor
   「作者参数在材质、几何在顶点」的分工不一致。折中：**只有 sprite 相关的东西走顶点**（矩形、uv/px
   换算），作者参数走材质缓存（§4.1）。
6. **shader 关键字变体**：运行时 `new Material` 不在构建里，`shader_feature` 变体会被剥掉；
   `multi_compile` 2⁵ 个变体也没必要 —— `UI-ProceduralPanel` 全靠 `if (size <= 0.0) return`
   uniform 分支，本文同款（§5.3）。

先例：`ProceduralPanel` 是 `Graphic` 子类、自持材质（`ProceduralMaterialCache` 按参数集共享、释放进
备用栈、补间零分配），`DisabledGrayscaleController` 对它「从内部去色」而不换材质，`ColorApplier.Peek`
知道它。reflect spec §2 否决「自定义材质」的理由（抢槽）在这个先例下不成立 —— **让 Image 自己持有
材质并把 `tint="linear"` / 灰度折进同一个 shader**，槽就不再是槽，而是参数。

## 3. 语法

```xml
<!-- 资源条：选中的图标发自己的颜色（默认 glowColor = 自体模糊色） -->
<Icon name="res:gold" glow="6"/>

<!-- 告警：单色剪影光 -->
<Icon name="ui:alert" glow="8" glowColor="danger"/>

<!-- 锁定的道具：糊掉，上面再叠一把锁 -->
<Image sprite="item:blaster" blur="4"/>
<Icon name="ui:lock" anchor="center"/>

<!-- 两者同时：卡片糊 3px 并带 10px 强调色光晕 -->
<Image sprite="card:01" type="contain" blur="3" glow="10" glowColor="accent/0.6"/>

<!-- 样式包 / 主题 / Variant 照常 -->
<Style name="rare" glow="8" glowColor="gold"/>
<Theme name="matte"><Style name="rare" glow="0"/></Theme>
<Icon name="ui:trophy" glow="8" glow.portrait="0"/>
```

| 属性 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `blur` | 像素半径，`≥ 0` 浮点 | `0`（不糊） | 本体像素按半径做圆盘模糊。数字即开关；`0` / `""` = 关（Variant / 主题退回的通道） |
| `glow` | 像素半径，`≥ 0` 浮点 | `0`（无光） | 自剪影边缘向外衰减到零的距离，画在本体**下面**。与 `<Frame glow>` 同名同单位 |
| `glowColor` | 颜色 token / 字面量，**纯色** | 未写 = **自体模糊色** | 写了就是单色剪影光（alpha 是强度）；不写时光晕取图标自己在 `glow` 半径上的模糊色 —— 彩色图标发自己的颜色，与 `<Frame>`「glowColor 不写则跟随填充」同一语义 |

- **仅 `<Image>` `<Icon>`**（M1）。`<RawImage>` 随 M2 的 lod 模糊一起接入（§10）；写在其它标签的 `blur`
  → lint error `PUI-FX-TAG`。`glow` / `glowColor` 在 `<Frame>` / `<Decor>` / `<Btn>` 上已有各自的
  程序化语义，不变。
- **只对 `type="simple"`（含 `contain` / `cover`，它们也是 Simple）生效。** `sliced` / `tiled` /
  `filled` 与 `blur` / `glow` 同节点 → lint error `PUI-FX-TYPE`；作者没写 `type=` 而 sprite 带 9-slice
  边框、运行时自动判成 Sliced 的，fx 被忽略并警告一次（lint 看不到 sprite 资产）。
- 单位是**元素自身空间的设计 px**，与 `<Frame glow="12">` 同一把尺：`scale=` 的 transform 缩放会把
  光晕一起放大；`GetNativeSize()` / 排版尺寸不含光晕。
- `> 12` 的半径 → lint warning `PUI-FX-RADIUS`（M1 采样核之外会出格纹），运行时照画不钳（§9.4）。
- XSD：`Image` / `Icon` 在 `XsdGenerator` 里是手列的，各补 `blur`（`xs:string`）`glow`（`xs:string`）
  `glowColor`（`xs:string`）。

## 4. 语义

### 4.1 数据分工

| 数据 | 变化粒度 | 落点 |
|---|---|---|
| sprite 在 atlas 里的 UV 矩形 | 每 sprite | 顶点 `uv1 = (uMin, vMin, uMax, vMax)` |
| uv / 设计 px 换算 | 每实例（随绘制尺寸） | 顶点 `uv2 = (Δu/px, Δv/px, 0, 0)` |
| `blur` `glow` `glowColor`（含「自体色」标志）、`tint=linear`、禁用灰度 | 作者参数 / 状态 | 材质，`FxMaterialCache` 按参数集共享（§5.2） |
| `color=` / 状态 `*Modulate` / `GradientTint` / CanvasGroup | 既有机制 | 顶点色 / CanvasRenderer，**不动** |

矩形直接取 `Image` 生成的 4 个顶点的 uv0 包围盒 —— 它就是 `DataUtility.GetOuterUV(sprite)`，且编辑器
里 Sprite Packer 未打包（`sprite.texture` 是源图）与已打包两种情况**自动都对**，不碰 packing API。
换算 = uv 包围盒尺寸 ÷ quad 尺寸。两条都不依赖 `_MainTex_TexelSize`（`GlassBlur` 注释里对自动填充
的顾虑同样适用于这里：显式算好、可测）。

Canvas 需开 `TexCoord1 | TexCoord2`（`ProceduralPanel.EnsureCanvasChannels` 同款，TexCoord1 已被程序化
面板开着，TexCoord2 新增）。

### 4.2 几何

- **外扩**：`pad = max(blur, glow)` px。`OnPopulateMesh` 先让 `Image` 照常生成 quad（`preserveAspect`
  的 letterbox、`AspectRatioFitter` 的适配结果都已在里面 —— 外扩的是**画出来的 quad**，不是 rect），
  再把四个顶点各自沿包围盒向外推 `pad`，uv0 按同一比例**线性外推**（此时 uv0 越出 sprite 矩形，是
  故意的：fragment 靠 uv1 判「矩形外 = 透明」）。`pad == 0` 不改任何顶点。
- 排版 / 点击 / 合批规则与 reflect §4.2 相同：RectTransform、anchor / margin / pivot、`LayoutElement`、
  `GetNativeSize()` 一律不变，光晕只膨胀绘制几何（`<VStack>` 的下一个兄弟会压在光晕上，作者用
  `spacing` 留空 —— 与 `<Frame glow>` 同一约定）；raycast 按 rect，光晕区不接点击；同图集 + 同参数集
  的实例仍合批。

### 4.3 采样与合成（fragment）

- **采样核**：圆盘，半径 R（`blur` 一轮用 `blur`，`glow` 一轮用 `glow`；两轮各自被 `R <= 0` 的 uniform
  分支跳过；两者同开 = 两轮）。tap 数与分布是 plan 期决定（§11.1，初值：中心 + 两环 Vogel 共 25 tap，
  近高斯权重）。tap 偏移 = 单位圆盘 × R × `uv2.xy`。
- **矩形钳制**：tap 的 uv 落在 `uv1` 矩形外 → 该 tap 贡献 (0,0,0,0)，权重照算（不是 clamp 到边，是
  当透明）。本体的单 tap 同样受此判：矩形外的外扩带上本体为透明。
- **预乘**：blur 累加 `Σw·(rgb·a)` 与 `Σw·a`，末尾反预乘 —— 否则透明像素的 RGB（常是黑 / 垃圾值）
  在边缘糊出一圈暗边。输出仍是直 alpha，blend 与 `UI/Default` 同（`SrcAlpha OneMinusSrcAlpha,
  One OneMinusSrcAlpha`，理由见 `UI-ProceduralPanel.shader` 同行注释）。
- **本体**：`image = blur > 0 ? blurred : sharp`；`tint="linear"` 时先做 Linear Light（从
  `UI-LinearLightTint` 抽到 `.cginc` 共用，§11.4）；再乘顶点色（`color=` / 渐变 / 状态调制 /
  CanvasGroup 全部在这一乘里）。
- **光晕**：由 `glow` 那轮的覆盖率 `c = Σw·a`（边缘处 ≈ 0.5、矩形内深处 → 1、R 处 → 0）得
  `g = saturate(2c)`，`alpha = g²` —— 与 `PuguiApplyOuterGlow` 的 `g²` 同形，Frame / Decor / Icon
  三处的光晕**手感一致而非逐位相同**（一个是解析距离，一个是覆盖率）。
  - `glowColor` 写了：`(glowColor.rgb, glowColor.a · g² · 顶点色.a)`；rgb **不**乘顶点色（作者指定的
    就是那个色），alpha 跟随淡出。
  - 未写（自体色）：rgb = 这一轮的反预乘平均色 × 顶点色.rgb，alpha = `g² · 顶点色.a` —— 图标被
    `color=` 染成什么色，光晕就跟着什么色。
- **合成**：`out = PuguiOver(image, glow)`（本体在上）。`_ClipRect` / `UNITY_UI_ALPHACLIP` / Stencil
  与 `UI/Default` 一致，可进 `RectMask2D` / `Mask` 层级。
- **禁用灰度**：对合成结果整体去饱和（本体与光晕一起灰，`ProceduralPanel` 把 glow 一起 `Desaturate`
  同一取舍）。

### 4.4 与其它机制的组合

| 组合 | 行为 |
|---|---|
| `flip` / `rotation` | Fx 在 `OnPopulateMesh` 里先做，`RotateFlipEffect` 转的是外扩后的 quad；uv1 / uv2 逐顶点常量随顶点走，圆盘核各向同性 —— 无影响 |
| `color=` 渐变（`GradientTint`） | 渐变按网格包围盒归一，包围盒已含 `pad` → 本体看到的是 `pad/H` 比例内缩后的那一段渐变。小半径可忽略；`blur="8"` 配 48px 图标能看出来。M1 接受并在 SKILL 注明（§11.3 留改进）。**切条路径必须对 uv1 / uv2 做顶点插值**（常量插值不变），守卫测试 §7 |
| 祖先 `mask="rect"` / `mask="self"` | 按顶点 / stencil 裁，光晕伸出父级的部分被裁掉 —— 正确行为 |
| 自身 `mask="rect"` | `RectMask2D` 不裁自己那层的 Graphic，光晕完整，子节点照常被裁 |
| 自身 `mask="self"` | stencil 用同一份网格 → **光晕区也成为遮罩的一部分**（且半透明光晕会触发 alpha-discard 把 stencil 写飞）。lint warning `PUI-FX-MASK` |
| `type="contain"` / `"cover"` | `AspectRatioFitter` 先定 rect，Fx 贴适配后的 quad；`cover` 溢出与光晕一起由父级 mask 裁 |
| `sliced` / `tiled` / `filled` | 不支持（§3） |
| `tint="linear"` | 折进 Fx shader，`ImageTint.Apply` 对 `FxImage` 改写参数而非换材质；视觉与旧 `UI-LinearLightTint` 逐像素等价（渲染测试） |
| 禁用（`DisabledGrayscaleController`） | `FxImage` 实现 `ISelfGrayscale`（`ProceduralPanel` 同一接口），从内部去色；离开禁用态参数复位 |
| 状态 `*Modulate` | 乘进 `Graphic.color` → 本体与自体色光晕一起变；显式 `glowColor` 不变（作者指定色） |
| CanvasGroup / `hidden` | alpha 一起淡出；隐藏一起消失 |
| `<Animation>`（transform 级） | 光晕随网格一起动，互不知晓；`blur` / `glow` **不作为动画轨道**（M1） |
| pixel-perfect | 外扩取整数 px 保持对齐；Point 纹理下 tap 采到硬像素，小半径的光晕偏硬 —— 像素风「1–2px 硬描边光」留位（§10） |
| reflect（若日后立项） | `ReflectionEffect` 镜像的是外扩后的网格，光晕一起倒影，自洽 |
| `sprite=""` / 无 sprite | 不生成 fx（`Image` 无 sprite 时画的是白 quad，糊它 / 给它发光没有意义） |

### 4.5 图集要求

采样按 sprite 矩形钳制，**不依赖** padding。但两个打包开关会破坏「矩形 = 这个 sprite」的前提：

- `enableRotation`：sprite 在图集里转了 90°，uv 轴与 sprite 轴错位 —— uGUI `Image` 本身就画倒
  （`DataUtility.GetOuterUV` 不看 `packingRotation`），`uv2` 的换算也会 x/y 对调。
- `enableTightPacking`：允许邻居钻进本 sprite 矩形的透明角落（`SpriteMeshType.Tight` 的素材，如外部
  PNG 图标集；`.pxl` 导入恒为 `FullRect` 不受影响）—— 平时看不见，模糊 alpha 后显形成一坨杂晕。

`SpriteAtlasSyncer` 今天只设 `TextureSettings`，打包设置吃 Unity 默认（两者都 `true`）。M1：
**新建 atlas 时显式设 `enableRotation = false` / `enableTightPacking = false`**；同步已有 atlas 时发现
任一为 `true` → `Debug.LogWarning` 指出改法（改打包设置要 Pack Preview，不自动改）。宿主工程里现有
`PxlIcon.spriteatlas`（1/1）由客户端仓库单独改，不在本 spec 内。

### 4.6 C# 面

`Image.Blur` / `Icon.Blur`、`Image.Glow` / `Icon.Glow`（`float`，可读写、可补间）；`GlowColor`（`string`，
`""` 退回自体色）。补间 `Glow`：每帧一次材质缓存查找（备用栈，零分配，与补间 `<Frame glow>` 同价）
**加**一次网格重建（`pad` 变了；与补间 `Rotation` 同价）。

## 5. 实现地图

### 5.1 `Controls/Internal/FxImage.cs`（新，`sealed class FxImage : UnityEngine.UI.Image`）

`<Icon>` / `<Image>` 的 `OnAttached` 一律 `GetComponent<FxImage>() ?? AddComponent<FxImage>()`；字段类型
仍是 `UnityImage`（is-a），既有代码不变。节点上若已有一个**非** `FxImage` 的 `UnityImage`（prefab 自带）：
沿用它，fx setter 警告一次「blur/glow need FxImage」（§11.6）。

```
状态：_blur, _glow, _glowColor, _glowColorExplicit, _tintLinear, _grayed
      _key (FxParams), _hasKey, _paramsDirty
HasGeometryFx => sprite != null && type == Simple && (_blur > 0 || _glow > 0)
HasMaterialFx => HasGeometryFx || _tintLinear || _grayed

OnPopulateMesh(vh):
  base.OnPopulateMesh(vh)                      // Image 的 Simple 路径：4 顶点，uv0 = outer UV
  if (!HasGeometryFx) return
  if (vh.currentVertCount != 4) { WarnOnce(...); return }   // useSpriteMesh 之类的防御
  FxMesh.Inflate(vh, pad: max(_blur, _glow))   // 静态、纯几何、可测：外推位置 + uv0，写 uv1 矩形 + uv2 换算

FlushParams(fromRebuild) / UpdateMaterial():   // 逐字沿用 ProceduralPanel 的形态
  !HasMaterialFx → 释放 key，m_Material = null（UI/Default，与今天完全一样）
  否则 BuildParams → FxMaterialCache.Acquire/Release → 写 m_Material（backing field，避免 rebuild 内重入）

setter：Blur / Glow 变化 → pad 变则 SetVerticesDirty；任何参数变 → _paramsDirty + SetMaterialDirty
      TintLinear（供 ImageTint）；SetDisabledGrayscale（ISelfGrayscale）
OnEnable / 首次设 fx → EnsureCanvasChannels(TexCoord1 | TexCoord2)
OnDestroy → 释放 key
```

`FxMesh`（同文件或 `FxMesh.cs`，`internal static`）：输入 4 顶点，输出外扩 + uv1 / uv2；`BuildMeshForTests`
同款暴露给测试。

### 5.2 `Controls/Internal/FxMaterialCache.cs`（新）

`FxParams`（`Blur`, `Glow`, `GlowColor`, `GlowSelf`, `TintLinear`, `Desaturate`；`IEquatable`，hash 同
`PanelParams` 写法）+ `Acquire` / `Release` / 备用栈 / `Clear`（测试）。shader 常量 `_Blur` `_Glow`
`_GlowColor` `_GlowSelf` `_TintLinear` `_Desaturate`。结构照抄 `ProceduralMaterialCache`；是否抽公共
泛型是 plan 期决定（§11.2）。

### 5.3 `Runtime/Resources/PromptUGUI/Material/UI-ImageFx.shader`（新）

骨架克隆 `UI-Grayscale.shader`（Properties 的 Stencil / `_ColorMask` / AlphaClip、Tags、Blend、
`UNITY_UI_CLIP_RECT` / `UNITY_UI_ALPHACLIP` 两个 `multi_compile_local`）；`#include "UI-PanelSDF.cginc"`
取 `PuguiOver`；Linear Light 抽到 `UI-ImageTint.cginc` 与 `UI-LinearLightTint.shader` 共用。顶点：
uv0 / uv1 / uv2 / color / worldPosition 透传。fragment 按 §4.3；全部 uniform 分支，无关键字。

### 5.4 其余

| 层 | 文件 | 改动 |
|---|---|---|
| 控件 | `Image.cs` / `Icon.cs` | `OnAttached` 换 `FxImage`；`[UIAttr, Preserve] Blur` / `Glow`（`ProceduralValueParser.Pixels`）/ `GlowColor`（`UI.Theme.Resolve`，纯色）；`OnAfterApply` 自动判 Sliced 后若有 fx → 警告一次 |
| tint | `Internal/ImageTint.cs` | `img is FxImage fx` → `fx.TintLinear = mode == "linear"`；否则旧路径 |
| 灰度 | `Internal/DisabledGrayscaleController.cs` + 新 `ISelfGrayscale` | `is ProceduralPanel` 类型判断改为接口判断；`ProceduralPanel` / `FxImage` 各实现 |
| 颜色回读 | `Internal/ColorApplier.Peek` | 无改动 —— `FxImage` 的颜色仍在 `Graphic.color` |
| lint | `Core/Lint/ImageFxRules.cs`（新，纯 C#）+ `Core/Lint/StyleRules.cs` | §6；`IRWalker` 登记（`Icon` 分支新增）；`PUI-FX-TYPE` 兼作运行时警告（`ScreenInstantiator`，`ImageFitRules.CheckVariant` 同款双落点）；`StyleRules.PixelAttrs` 加 `blur` |
| XSD | `Editor/XsdGenerator.cs` | `Image` / `Icon` 手列表各加三项 |
| 图集 | `Editor/SpriteAtlasSyncer.cs` | §4.5：新建设 packing settings；同步时检查告警 |
| 注册 / i18n 扫描 | 无 | 非 sprite、非文本属性；`glowColor` 走既有颜色属性路径（`IsColor = true`） |

## 6. lint

| 规则 | 遍 | 级别 | 触发 |
|---|---|---|---|
| `PUI-FX-TAG` | raw | error | `blur` 写在 `<Image>` / `<Icon>` 以外的标签（含 `<RawImage>`，消息注明 M2） |
| （既有）`PUI-PROCEDURAL-VALUE` | raw + Style | error | `blur` / `glow` 非数字或为负 —— `StyleRules.PixelAttrs` 已对 `glow` 做通用像素值校验（任何标签、含 `<Style>`），`blur` 加进同一数组即可，**不另设 `PUI-FX-VALUE`**。`glowColor` 的格式由既有 `PUI-COLOR-LITERAL-INVALID` 管 |
| `PUI-FX-TYPE` | expanded（class 合并后判） | error（运行时 warning） | `blur` / `glow` 与 `type="sliced"` / `"tiled"` / `"filled"` 同节点 |
| `PUI-FX-ATTR` | expanded | warning | 写了 `glowColor` 但最终没有 `glow`（什么都不会画） |
| `PUI-FX-MASK` | expanded | warning | `blur` / `glow` 与 `mask="self"` 同节点（§4.4） |
| `PUI-FX-RADIUS` | expanded | warning | `blur` 或 `glow` `> 12`：M1 采样核之外会出格纹；大半径等 M2 |

## 7. 测试（Red 先行）

- `FxMeshTests`（EditMode，纯几何，`FxMesh.Inflate` 直接喂 4 顶点）：`pad = 6` 后四角各外移 6；uv0
  外推量 = `6 × Δuv/px`；uv1 = 原 uv0 包围盒；uv2 = 包围盒尺寸 ÷ quad 尺寸；`pad = 0` 逐位不变；
  letterbox 的 quad（比 rect 窄）以 quad 为准；非 4 顶点早退。
- `FxImageTests`（EditMode）：`<Icon glow="6">` 落到 `FxImage`；无 fx 时 `material == null`（`UI/Default`）；
  同参数两个 Icon 共享同一 `Material`，不同 sprite 也共享；`glow.portrait="0"` Variant 往返后材质回
  `null`、无新组件；补间 `Glow` 100 帧 `Material` 实例数不增（备用栈）；`tint="linear"` 不换材质而是
  参数；`SetDisabledGrayscale` 不换材质、离开后参数复位；`GetComponents<IMeshModifier>()` 数量不变；
  RectTransform / `GetNativeSize()` 不变；9-slice sprite 未写 `type=` → fx 忽略 + `LogAssert` 一次警告。
- `ImageFxRulesTests`：§6 五条各正反例（含 `{{}}` 跳过、class 合并后才判的 `PUI-FX-TYPE`）；`StyleRulesTests`
  补 `blur` 的非数字 / 负值走 `PUI-PROCEDURAL-VALUE`。
- `DisabledGrayscaleControllerTests` 补：`ISelfGrayscale` 分派对 `FxImage` 生效。
- **守卫**：`GradientTint` 带色标切条 + `glow` → 切出的顶点 uv1 / uv2 与原顶点相同（常量插值）。
- **`ImageFxRenderTests`（EditMode，`Camera.Render()` 到 RT，`DecorRenderTests` 同款夹具，PNG dump）**。
  测试内用 `Texture2D` 手搓一张**双 sprite 图集**（左：透明底白色圆盘；右：纯红实心块；两块紧邻，
  `Sprite.Create` 各取矩形）—— 这是矩形钳制唯一的真实回归防线：
  1. **夹具自检永远第一条**：无 fx 时圆盘内亮、圆盘外暗、红块处红。
  2. glow：圆盘剪影外 2px 处亮度 > 阈值，沿法线向外单调递减，`R` 之外 = 背景；`glow="0"` 时同一像素
     = 背景（**阈值用关掉功能的那一版验过**）。
  3. **不串色**：圆盘 sprite 开 `glow="8"` / `blur="8"`，其矩形右缘外 1–8px 的像素 R 通道不高于 G/B
     （没有红块漏进来）；红块 sprite 开 fx，其矩形左缘外没有白色。
  4. blur：圆盘边缘像素亮度介于内外之间，`blur` 越大过渡带越宽。
  5. 预乘：白圆盘（透明区 RGB 预置为黑）`blur="6"` 后边缘半透明像素的 rgb ≈ 白，无暗环。
  6. `glowColor="#ff0000"` 光晕为红；未写时白圆盘光晕为白、`color="#00ff00"` 时为绿（自体色跟随顶点色）。
  7. 禁用灰度 + 彩色 sprite + `glow`：光晕像素 R≈G≈B。
  8. `tint="linear"` 中灰测试图：`FxImage` 与旧 `UI-LinearLightTint` 逐像素差 ≤ 1/255。
  9. 祖先 `mask="rect"` 裁掉伸出的光晕。
  linear 色彩空间工程用 `GetPixels32` 读原始字节（`CornerTreatmentRenderTests` 同）。
- `CanvasRebuildTests` 扩展：带 fx 的 Icon `Canvas.ForceUpdateCanvases()` 不抛、`CanvasRenderer` 有网格。
- `SpriteAtlasSyncerTests`：新建 atlas `enableRotation == false && enableTightPacking == false`；
  对手工开着的 atlas 同步产生 warning。
- `XsdGeneratorTests`：三个属性名 substring。

## 8. SKILL 更新（同一 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：`<Image>` / `<Icon>` 属性表各加三行；紧接 **Rotation & flip**
  新增 **Blur & glow** 小节（示例、自体色 vs `glowColor`、只支持 simple、不占排版留 `spacing`、
  `mask="self"` 注意、渐变被 pad 内缩的说明、>12px 是 M2）；Quick reference 加一行
  `FX blur="4" glow="8" glowColor="accent"`；lint 码表加五条（`PUI-PROCEDURAL-VALUE` 一行补 `blur`）。
- `authoring-promptugui-xml/reference/icons.md`：图集打包要求（rotation / tight 关，Syncer 会告警）。
- `scripting-promptugui-csharp/SKILL.md`：`Icon.Blur` / `Glow` 可补间，一句话挂在 `Rotation` 旁。
- `authoring-promptugui-pxl/SKILL.md`：一句 —— 光晕 / 模糊由 `glow=` / `blur=` 生成，**不要**画进 `.pxl`。
- `.lint/UIXmlLint/README.md`：规则列表。
- CLAUDE.md 路由表：无新 `reference/*.md`，不加行。

## 9. 已定的决策（2026-09-02 与作者对齐）

1. **落点 = `FxImage` 子类**（`OnPopulateMesh` 后处理 + 自持材质缓存），不走 `BaseMeshEffect` +
   `ReserveSlot`。
2. **`glowColor` 未写 = 自体模糊色**（bloom 观感），与 Frame「跟随填充」同调；写了才是单色剪影光。
3. **M1 = `<Icon>` + `<Image>`**。`<RawImage>` 随 M2；`<Btn sprite=>` 底图 glow 另立 spec。
4. **`> 12px` 只 lint warning，不钳制**；运行时照画。
5. 属性名 `blur` / `glow` / `glowColor`，px、数字即开关；只支持 `type=simple`；矩形外一律透明
   （`clamp` 边缘模式留 M2）；`tint="linear"` 与禁用灰度折进 Fx shader；图集卫生进 Syncer；
   `mask="self"` 同节点 warning；`<Animation>` 轨道与状态驱动发光不进 M1。

## 10. 非目标（M2 及以后，留扩展位）

- **大半径 blur**：非图集纹理 + mip 走 `tex2Dlod(log2 R)` 路径（`<RawImage>` / 移出图集的头像横幅，
  矩形退化为 [0,1]，精确又便宜）；非要糊图集里的图再上 RT 缓存（§2.1）。底座（外扩、uv1 / uv2、材质
  缓存、合成）不变，只换 `blur` 那轮的采样核。
- **`edge="clamp"`**：全出血照片模糊时边缘不淡出而是延展。与大半径同期。
- **`<RawImage>` 接入**（`FxRawImage`，`uvRect` 即矩形）。
- **`<Btn sprite=>` 底图 glow**：今天撞 `PUI-PROC-SPRITE-CONFLICT`；把 `_bg` 换成 `FxImage` 并按皮肤
  类型路由 `glow`，牵涉状态系统，独立 spec。
- **像素风**：硬描边光（`glowStyle="hard"`，4/8 tap 取 max）、量化模糊（glass §9 留位）。
- **动画 / 状态**：`<Animation blur= glow=>` 轨道；`hoverGlow` 之类状态驱动。
- **烘焙式光晕**作为 style 选项（§2.2）。

## 11. 开放问题（留给 plan / 实现期）

1. 采样核细节：25 tap 两环 Vogel vs 5×5 去角；bilinear 纹理是否用半纹素偏移白嫖 2×2；权重形状。
   以渲染测试 2 / 4 的单调性与无格纹为准。
2. `FxMaterialCache` 与 `ProceduralMaterialCache` 是否抽公共泛型（两份 ~80 行的复制 vs 一次动到玻璃
   面板的重构）。
3. `GradientTint` 归一被 `pad` 内缩：接受，或让 `GradientTint` 向 `FxImage` 询问内容包围盒（一个
   `internal` 接口，两行）。先接受，看真实用例。
4. Linear Light 抽 `.cginc` 后 `UI-LinearLightTint.shader` 是否直接 include（同一实现）—— 应当。
5. 预乘输出与 HDR 显示（`One OneMinusSrcAlpha` alpha 通道）—— 沿用面板 shader 的处理。
6. prefab 自带 plain `Image` 的节点：警告即可，还是运行时替换组件（要迁移 sprite / color / type /
   引用）。倾向警告。
7. `PUI-FX-TYPE` 对 class 合并前的 raw 遍是否也判（`type=` 常来自 `<Style>`）。倾向只在 expanded 判。

## 12. 成本

| | 开销 |
|---|---|
| 不写 `blur` / `glow` 的文档 | **零** —— `FxImage` 无 fx 时 `material = null`、不改顶点、不挂组件；只是组件类型换成子类 |
| 一个有 fx 的 Graphic | quad 外扩 `pad` 的透明过绘带；fragment 每轮 ~25 tap（两者同开 ~50）；材质按参数集共享，不同 sprite 同参数仍合批；不同参数集之间是一次 batch break（与 Frame 样式不同即分批同一规则） |
| 补间 `Glow` / `Blur` | 每帧一次材质缓存查找（零分配）+ 一次网格重建 |
| Canvas | `TexCoord2` 新开：画布内每个顶点多一个 float4（`TexCoord1` 已被程序化面板开着） |
| 图集 | 关 rotation / tight 后打包略松，`padding` 不变 |

## 13. 实施记录（M1，2026-09-02 完成）

分支 `feat/image-fx-blur-glow`，11 个提交（Task 0–11）。全量：EditMode + EditorOnly **3787 绿**、
PlayMode **200 绿**、`dotnet format --verify-no-changes` 干净、`UIXmlLint` 退出码 0。

### 与设计的偏差

1. **`PUI-FX-VALUE` 并入既有 `PUI-PROCEDURAL-VALUE`**（写 spec 时已改口，见 §6）：`StyleRules.PixelAttrs`
   本就对 `glow` 做非负像素值校验、且覆盖 `<Style>` 内部，`blur` 加进同一数组即可。规则从六条变五条。
2. **`FxMaterialCache` 采用复制而非抽公共泛型**（§11.2）：与 `ProceduralMaterialCache` /
   `DecorMaterialCache` 并列的第三份 ~80 行结构。抽泛型要同时动到玻璃面板的双备用栈，不值得夹带进本期。
3. **`ImageFxRules` 提前到 Task 4 建文件**（只放 `FxTags` / `SupportedProceduralAttrs` 两个常量，
   规则本体仍在 Task 6）：`ProceduralAttrNamesTests.OnlyFrame_HasThePanelRequiringAttributes` 这条守卫
   在 Task 4 就变红了 —— 它要求「任何接受 panel-requiring 属性的控件都必须登记」。这正是它存在的意义，
   处理方式沿用 `<Decor>` 的先例：`PureContainerVisualAttrRules` 对 `<Image>` / `<Icon>` 豁免 glow 对。
   同时 `PureContainerVisualAttrRulesTests` 里那条"报全部而非只报第一个"的用例原本用 `radius + glow`
   举例，`glow` 在这两个标签上合法之后换成了 `radius + borderWidth`。
4. **采样核权重是均匀圆盘（全 1），不是高斯**。先按 spec 的 `exp(-2r²)` 实现，PNG 目检发现光晕在
   离边缘 R/2 处就消失 —— 作者写 `glow="8"` 得到的是一圈 4px 硬边。高斯把权重堆在核心，覆盖率衰减
   远快于"到 R 归零"。改均匀圆盘后覆盖率即"圆盘落进剪影的面积占比"，恰在 d=R 归零，与
   `PuguiApplyOuterGlow` 的 `g²` 同族（d=0.5R 时本式 0.15 / 参考式 0.25）。代价是模糊比高斯略平，
   ≤12px 这一档看不出来。
5. **shader 增加"退化矩形回退"**：`uv1` 的矩形无效（宽或高 ≤ 0）时整段 fx 跳过、按 `UI/Default` 画。
   设计里没有这条。没有它的话，任何没经过 `FxMesh` 的网格（Sliced / Tiled / sprite mesh，或画布没开
   TEXCOORD1/2）都会因为"所有 tap 都在矩形外"而被采成全透明 —— sprite 直接消失。C# 侧也做了对应
   收窄（`BuildParams` 在 `HasGeometryFx == false` 时把两个半径清零，材质不进缓存）。
6. **`ImageFxApplier` 是新增的第三个 applier**（设计里只提了 setter 直连）：`<Image>` / `<Icon>` 的
   三个 setter 完全同形，且都要处理"节点上是 prefab 自带的 plain Image"这一路，与
   `RotateFlipApplier` / `ImageTint` 同一惯例。
7. **`GradientTint` 内缩（§11.3）实测不显**：`MeshSlicer.Lerp` 已经带 uv0–uv3，守卫测试一写即绿；
   渐变按含 `pad` 的包围盒归一这件事本身仍在，但在 ≤12px 半径下肉眼不可辨，M1 保持接受。

### 最终采样核与渲染阈值

- 核：中心 1 tap + 24 点 Vogel 圆盘（黄金角 2.39996，`r_i = √((i+0.5)/24)`），权重全 1，共 25 tap。
  两轮（blur / glow）各自被 `if (R <= 0)` 的 uniform 分支跳过。
- 光晕曲线：`falloff = saturate(2·coverage)²`。剪影边缘 coverage≈0.5 → 满强度；d=R → 0。
- `ImageFxRenderTests` 用测试内手搓的 **双 sprite 图集**（左 32×32 透明底白圆盘、右 32×32 纯红），
  9 条用例。关键阈值都在"功能关掉的那一版"上反向验过：`glow='0'` 时同一像素为背景。
  探针按**半径相对**取样（`R/2`），不用固定像素距离 —— 首版写死 3px 时 `blur='4'`（= 2 texel）
  自然测不出来，那是测试的尺度错误不是 shader 的。

### 宿主工程真实图集目检（Task 11）

在 ssw_re_client 里用 `Solar96Bold.spriteatlas` 的真实打包 sprite（在 512×512 图集页里占
`(264, 176, 84, 84)`）离屏渲染四个 `<Icon>`：无 fx / `glow=8` / `glow=8 glowColor=#3ba7ff` / `blur=6`。
水平剖面（穿过图标中心向右）：

```
plain     …26:0.65 28:0.08 30:0.08 …44:0.08     ← 剪影边缘后直接是背景
glow-self …26:0.77 28:0.36 30:0.25 32:0.13 34:0.08 …44:0.08   ← 单调衰减到背景，中间无异物
```

即真实图集页上邻居没有漏进来。两点观感记录：

- **自体色光晕会填满内部镂空**（与 Photoshop 的 Outer Glow 同性质：光晕画在本体之下，本体透明处
  就透出光）。该图标的箭头是白色实心不受影响，但"镂空即图形"的图标配自体色会糊掉内部细节 —— 写
  显式 `glowColor` 立刻分得开。局部核无法区分"形状外"与"内部孔洞"，这是技术本身的性质，非缺陷。
- 半透明边缘像素会与身后的光晕合成而变亮（`plain` 0.73 → `glow-self` 1.00），也是正确的 source-over。

### 客户端仓库待办（不在本 PR 内）

`ssw_re_client` 的 `PxlIcon.spriteatlas` 目前 `enableRotation=1 / enableTightPacking=1`（当前
`spriteCount=0`，尚未 Pack Preview）。要在像素图标上用 blur/glow，需在 SpriteAtlas inspector 里关掉
两项再 Pack Preview —— Syncer 现在会对已有 atlas 告警但不自动改。`Solar96Bold` 已是 0/0，无需处理。
