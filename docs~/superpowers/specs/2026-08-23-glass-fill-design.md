# 玻璃填充：backdrop 模糊 + SDF 边缘光照 + 多块融合

**日期**：2026-08-23
**状态**：已实施（M3a + M3b + M4 一次做完，分支 `feat/glass-fill`）。实施期偏离设计的七处记在 §13，验证记录在 §14。
**作用域**：在 [procedural-style](2026-08-23-procedural-style-design.md)（PR #99）的 `ProceduralPanel` 之上增加**玻璃填充模式**：`<Frame glass="true">` 时，填充不再是纯色/渐变，而是采样一张全局模糊背景纹理，叠加边缘折射、方向光边缘高光、色散、磨砂颗粒——不用任何 sprite，形状仍是同一套 SDF（radius / pill / border / glow 原样共用）。附带（M4）同一父容器下多块玻璃的 SDF 平滑融合（`weld`）。背景纹理由 URP 注入式 render pass 供给，`PROMPTUGUI_HAS_URP` 门控；无 URP 时优雅降级为半透明程序化填充。
**关联**：形状/材质缓存/顶点-材质分工全部继承 procedural-style §12.1（参数放材质、形状放顶点——玻璃参数逐样式相同，缓存合批模型不变）；真渲染验证义务继承 §12.2（EditMode 全绿不构成能渲染证据）；纯容器 lint 名单扩展继承 §12.3；`color` 的 token/渐变/`/alpha` 语义沿用 [gradient-color](2026-06-13-gradient-color-design.md)。Addressables 的 `versionDefines` 门控是 URP 门控的先例（`PromptUGUI.Runtime.asmdef`）。

---

## 1. 背景与目标：什么样的玻璃

视觉基调定为 **Figma glass / 薄磨砂亚克力**，明确不是 iOS 26 liquid glass。两者的分水岭是折射发生的位置：

- liquid glass 显"厚"的根源：**整面透镜折射**（内部像水滴一样放大/弯曲背景）+ 宽斜面 + 弯曲高光；
- Figma glass 显"薄平"的根源：**内部零折射、完全平整**，折射只发生在边缘窄斜面（bevel）上。

因此算法一刀切开：内部只做"模糊 + 提亮提饱和 + tint"，折射/光照全部限制在 `depth` 像素宽的边缘带内。参数直接对齐 Figma glass 的心智模型（frost / depth / dispersion / light angle / light intensity），LLM 作者与人类设计师零翻译成本。

设计约束（继承 procedural-style，新增两条）：

1. 玻璃参数逐**样式**相同、逐面板尺寸无关——延续 §12.1 的材质缓存模型，`class="glass-card"` 用二十次仍是一个材质、能合批。
2. 背景供给是全局单点成本：每帧至多一次降采样+模糊链，所有玻璃共享；场上没有玻璃时零开销。

## 2. 视觉模型（fragment 合成顺序）

自下而上五层，全部在一个 pass 内：

1. **磨砂底**：按屏幕坐标采样全局模糊纹理（两档模糊 `lerp`，见 §5），然后 vibrancy：`rgb = lerp(luma, rgb, saturation)` 提饱和——这是玻璃"发亮"而不是"发灰"的关键，比折射重要。再叠 `noise` 强度的 interleaved-gradient-noise 颗粒（防 banding + 磨砂质感，Windows Acrylic 做法）。
2. **边缘折射带**：仅当 `-depth < d < 0`（d 为 SDF 距离）。法线取 SDF 屏幕空间梯度 `n = normalize(ddx(d), ddy(d))`，采样 UV 沿 n 向外偏移，偏移量按圆弧 profile 从边缘最大衰减到带内侧 0——产生"边缘轻微透镜"感。内部（`d < -depth`）严格不扰动。
3. **色散**：折射带内 RGB 三通道的偏移量乘 `1±dispersion·k`，默认 0。
4. **边缘光照**：折射带内加白 `pow(saturate(dot(n, L)), p) × lightIntensity`，L 由 `lightAngle` 决定；并叠 180° 反向的弱补光（×0.35），双侧高光像管壁截面——这就是"光打在玻璃上"的物理描边，沿 SDF 轮廓自动流动（M4 融合后自动沿融合外轮廓走）。
5. **复用层**：`color` 作为 tint 层 **source-over** 到上述结果之上（语义与非玻璃时完全一致："color 是画在底上的填充层"，只是底从透明换成了模糊背景；渐变照常）；`borderWidth`/`borderColor` 的 1px 保底描边、`glow` 外发光，与 `UI-ProceduralPanel` 同公式。形状外部（`d > 0`）只有 glow，不采样 backdrop。

保底描边存在的理由：低对比背景下第 4 层的物理高光会消失，UI 的边界不能跟着消失——两层各有职责，作者可独立开关。

## 3. 作者面

### 3.1 属性表（全部在 `<Frame>` 上，`[UIAttr]`，Variant / ReSolve / `<Style>`/class 自动生效）

| 属性 | 语法 | 默认 | 说明 |
|---|---|---|---|
| `glass` | bool | `false` | 玻璃模式开关。`false` 时下列参数全部无效（lint warn，§8）。 |
| `frost` | float 0–1 | `0.5` | 模糊量，两档模糊纹理间插值。0 = 最轻磨砂（不是"完全清晰"，见 §9）。 |
| `depth` | float px | `4` | 边缘折射带宽度（玻璃"厚度"）。0 = 无折射也无边缘光照（光照画在斜面上）。 |
| `dispersion` | float 0–1 | `0` | 色散强度。 |
| `lightAngle` | float 度 | `0` | 光源方向：0 = 正上方，顺时针增。 |
| `lightIntensity` | float 0–1 | `0.6` | 边缘高光强度，0 关闭光照层。 |
| `saturation` | float | `1.15` | backdrop 饱和度乘子（vibrancy）。 |
| `noise` | float 0–1 | `0.02` | 磨砂颗粒强度。 |

复用不新增：`color`（tint 层，token/渐变/`/alpha` 照常）、`radius`（形状）、`borderWidth`/`borderColor`（保底描边）、`glow`/`glowColor`（外发光）。典型用法通过 Style 复用：

```xml
<Style name="glass-card" glass="true" radius="16" frost="0.6"
       color="white/0.06" borderColor="white/0.25" borderWidth="1"/>

<Frame class="glass-card" anchor="top-stretch" height="220" margin="16,16,_,16">...</Frame>
```

### 3.2 backdrop 语义（写进 SKILL 的硬规则）

玻璃采样的背景 = **capture 相机渲完（含后处理）那一刻的画面**：游戏世界 + 该相机名下所有 Screen Space-Camera canvas。**Overlay canvas 里的内容不在其中**——uGUI 没有 grabpass，同一 overlay canvas 内玻璃永远看不到兄弟元素。

推荐结构因此非常简单：**玻璃 Screen 用默认的 Overlay 模式（什么都不用改）；希望被玻璃模糊的底层 UI 用 `CanvasMode.Camera`。** 对最常见的"玻璃资源条浮在游戏世界上"场景，默认配置天然正确。

**反馈回环警告**：Camera 模式 Screen 里写玻璃 → 该 canvas 在 capture **之前**渲染，玻璃采到的是含自己上一帧像素的纹理，多帧后糊成一团。运行时检测到 glass panel 所在 canvas 为 ScreenSpaceCamera/World 且 render camera == capture 相机时，一次性 `Debug.LogWarning`（不硬禁——多相机结构下可以是合法配置）。

同理，**玻璃看不到玻璃**：两块玻璃上下叠放时，上层采样的 backdrop 不含下层玻璃的绘制结果（同一张 capture）。叠放需求用 M4 融合或避免重叠；多 pass 逐层 capture 记 YAGNI（§9）。

## 4. 渲染实现

### 4.1 组件与材质：扩展而非新建

不新增组件。`ProceduralPanel` 增加玻璃参数组；`PanelParams` 扩展 `GlassEnabled + Frost/Depth/Dispersion/LightAngle/LightIntensity/Saturation/Noise` 字段（非玻璃面板全零，现有 key 等价性不受影响）；`ProceduralMaterialCache.Configure` 按 `GlassEnabled` 选 shader（缓存两个 Shader 引用）。收益：

- Frame 的 setter 接线、lazy 挂载、缓存/引用计数/spare 复用、`TexCoord1` 通道逻辑零重复；
- Variant 切 `glass.low="false"`（低端机档位关玻璃）= 换一个材质 key，走现有 ReSolve，不脏顶点不重建；
- 同 style 的玻璃面板照旧共用材质合批。

### 4.2 Shader：`UI-GlassPanel.shader`

独立 shader 文件（不做 multi_compile 变体——两种填充的 fragment 差异太大，合并只会让两边都难读）。`sdRoundBox` / `over` 提取到同目录 `UI-PanelSDF.cginc`，两个 shader include（.cginc 编译期打入，无 Resources 加载问题）。

- 顶点输入与 `UI-ProceduralPanel` 完全一致（uv0 局部坐标 / uv1 半尺寸 / 顶点色 = Graphic tint），继承 `_ClipRect` / stencil / AlphaClip 段。
- backdrop 采样坐标从 `SV_POSITION`（`VPOS` 语义或 `ComputeScreenPos`）取屏幕 UV——不需要新顶点数据。
- 全局纹理 `_PUGUI_GlassBackdropA` / `_PUGUI_GlassBackdropB`（两档模糊），`Shader.SetGlobalTexture` 设置，不进材质参数——不破坏材质缓存 key 模型。
- `#pragma multi_compile_local _ PUGUI_GLASS_FALLBACK`：降级 keyword（§6），backdrop 不可用时跳过采样、tint 层直接当半透明填充画——等价于非玻璃面板的视觉。

### 4.3 backdrop 供给：`GlassBackdropSystem`（URP，注入式）

`Runtime/Application/Glass/`，整体 `#if PROMPTUGUI_HAS_URP`。**不要求用户改 URP Renderer asset**——用运行时注入代替 ScriptableRendererFeature 配置：

- **计数驱动**：`ProceduralPanel` 在玻璃参数生效/失效与 `OnEnable`/`OnDisable` 时增减静态计数；计数 0→1 订阅 `RenderPipelineManager.beginCameraRendering`，1→0 退订并释放 RT。场上没有玻璃 = 系统完全不存在。
- **注入**：回调中对目标相机 `EnqueuePass` 一个 `ScriptableRenderPass`（`RenderPassEvent.AfterRenderingPostProcessing`）。目标相机默认 `Camera.main`，`UI.Glass.Camera` 属性可覆盖（分屏/多相机结构）；相机缺失时本帧跳过 + 一次性 warn。
- **Pass 内容**：camera color → Blitter 降采样至 1/4 分辨率 → dual Kawase 2 迭代 → 写入持久 RTHandle **A** → 再 2 迭代 → 持久 RTHandle **B** → `SetGlobalTexture` 两张。频域上 A≈轻磨砂、B≈重磨砂，`frost` 在 shader 里 lerp 两档得连续调节。RT 格式 `B10G11R11_UFloatPack32`（不足则 RGBA8），屏幕尺寸变化时重建。
- **双路径**：Unity 6 / URP 17 走 RenderGraph（`RecordRenderGraph`，blur 输出 blit 进 imported 持久 RTHandle——RG 临时纹理活不过当帧，而 overlay UI 在 RG 之外渲染）；宿主开 Compatibility Mode 时走传统 `Execute`。两条路径共享 blur 逻辑，各自只有十几行胶水。
- 预算：1/4 分辨率 dual Kawase 4 次 blit，移动端 < 0.3ms；WebGL2 全兼容；无线程、无 `Task`（项目铁律不涉及——全部在 render pass 内）。

### 4.4 门控

`PromptUGUI.Runtime.asmdef` 增 versionDefine：`com.unity.render-pipelines.universal` ≥ `17.0.0` → `PROMPTUGUI_HAS_URP`；references 增 `Unity.RenderPipelines.Universal.Runtime`（asmdef 未解析引用自动忽略，Addressables 同款）。**v1 只支持 Unity 6 + URP 17**：Unity 2022（URP 14，无 RenderGraph、Blitter API 有差异）下 define 不满足 → 玻璃走降级链，视觉为半透明面板，功能不炸。已讨论确认 2022 今后也不会实现。

## 5. 公共 API：`UI.Glass`

```csharp
public static class UI {
    public static class Glass {
        /// 全局开关（画质选项）。false 时所有玻璃面板走降级填充，backdrop 系统整体休眠。
        public static bool Enabled { get; set; } = true;
        /// backdrop capture 相机。null（默认）= Camera.main。
        public static Camera Camera { get; set; }
    }
}
```

→ 触发 C# skill 更新（公共 API 面变化，见 §11）。

## 6. 降级链（全部收敛到同一个 fallback 视觉）

按优先级判定，任一命中 → 面板材质启用 `PUGUI_GLASS_FALLBACK`（tint 当半透明填充，形状/描边/发光照常）：

1. 无 `PROMPTUGUI_HAS_URP`（URP 包未装 / 版本不足）——编译期整个 backdrop 系统不存在。
2. URP 包在但当前激活管线不是 URP（`GraphicsSettings.currentRenderPipeline` 检查）——一次性 warn。
3. `UI.Glass.Enabled == false`——静默（这是正常画质档位操作）。
4. capture 相机缺失——一次性 warn。

fallback 切换 = 材质 keyword 翻转，实现上进 `PanelParams` key（一个 bool），复用缓存机制；不重建 GO、不脏顶点。

## 7. M4：多块玻璃融合（`weld`）

顶部主资源条 + 尾部次级小块这类场景，两块玻璃用描边分割难看；SDF smooth-min 让它们像一块连续玻璃，交界处用厚度台阶而非线条区分。

### 7.1 作者面

```xml
<Frame weld="10" frost="0.5" lightAngle="-30">          <!-- 组容器：weld + 组级玻璃参数 -->
  <Frame glass="true" anchor="top-stretch" height="64" radius="0,0,16,16" depth="6" color="white/0.06"/>
  <Frame glass="true" anchor="top-right" size="180,40" radius="12" depth="3" color="#39f/0.15"/>
</Frame>
```

- `weld`（px）写在父 Frame：smin 的焊接半径，控制交界圆角。写了 weld 的 Frame 自身不得 `glass="true"`（**lint error**，见 §15.1——运行时容忍并按承载者处理）——它是承载者不是形状。
- **组级参数**（物理上必须一致）写在父上：`frost` / `lightAngle` / `lightIntensity` / `saturation` / `noise`；**逐块参数**写在子上：`radius` / `depth` / `color`（tint 逐块）/ `borderWidth` / `borderColor`。子上写组级参数 → lint warn + 忽略。
- 参与者 = **直接子级**中 `glass="true"` 的 Frame，上限 8 块（超出为 **lint error**，见 §15.1——运行时超额成员自己画，不阻断开屏）。子 Frame 照常参与布局、放内容、被 `Get<T>` 找到——只是玻璃视觉由组统一画。

### 7.2 实现

- 父 Frame 挂 `GlassGroupPanel`（独立 `MaskableGraphic`）：几何为覆盖所有成员 union AABB + glow 外扩的一个 quad；成员的 `ProceduralPanel` 进 suppressed 模式（`OnPopulateMesh` 空输出，参数仍被 setter 收集）。
- Shader `UI-GlassGroup.shader`：uniform 数组（`float4 _Rects[8]`（中心+半尺寸，父局部空间）、`float4 _Radii[8]`、`float4 _Tints[8]`、`float _Depths[8]`、`int _Count`），fragment 对成员 SDF 做 polynomial smin（k = weld）；per-pixel 有效 depth 按各 SDF 的 `exp(-dᵢ/k)` 权重混合 → 交界处自然形成 bevel 台阶；厚块向薄块方向叠一条短距离衰减的接触阴影强化层级。边缘光照/折射/frost 用组级参数作用在融合后的 SDF 上——高光自动沿融合外轮廓流动，交界内部无任何分割线。
- **材质逐组实例、不进缓存**（uniform 含成员 rect，逐组唯一且逐帧可变——组是少数，可接受）。布局变化（成员 `OnRectTransformDimensionsChange` / 父尺寸变）→ 只重传 uniform 向量，union AABB 变了才 `SetVerticesDirty`——延续 §12.1"改参数不脏顶点"哲学。
- 降级链对组同样生效（组 shader 的 fallback keyword：各成员按自己 tint 画融合形状的半透明填充）。

## 8. Lint / XSD

- `PureContainerVisualAttrRules`：玻璃全部新属性（`glass` / `frost` / `depth` / `dispersion` / `lightAngle` / `lightIntensity` / `saturation` / `noise` / `weld`）加入 VStack / HStack / Grid / SafeArea 的报告名单（§12.3 拆分后的第二类）。
- 新增 `GlassRules`（Core/Lint，纯 C#，CLI 共享）：
  - `PUI-GLASS-PARAM-NO-GLASS`：写了玻璃参数但无 `glass="true"`（含"组容器有 weld 但子级参数错位"：子上写组级参数、父上写逐块参数）。
  - `PUI-GLASS-WELD-SELF`：`weld` 与 `glass="true"` 同节点。
  - `PUI-GLASS-WELD-COUNT`：weld 容器下玻璃子级 0/1 块（无意义）或 >8 块。
- 数值语法错误（frost 超 0–1、负 depth 等）走 `[UIAttr]` setter 的 ParseException（`ParsePixels` 同款），CLI 侧由共享解析函数覆盖。
- XSD：Frame 新属性；substring 断言。

## 9. 不做的事（YAGNI 记录，留扩展位）

- **frost=0 的"全清玻璃"**——需全分辨率 capture copy，成本翻倍；最低档即 1/4 分辨率轻磨砂。真实需求出现再加第三档。
- **玻璃看到玻璃**（逐层多 pass capture）——见 §3.2，叠放场景先用融合/避让。
- **Unity 2022 / URP 14 完整支持**——降级不炸即可；Blitter/RTHandle 差异等有真实需求再做。
- **BiRP / HDRP**——URP-only。
- **像素风量化玻璃**（模糊半径按像素格量化 + point 采样马赛克）——procedural-style 时说"留玻璃 spec 一并考虑"，考虑结果：先出平滑版看实际游戏里效果，违和再加 `pixelated` 开关。
- **乘法染色 tint**（透射模型 `backdrop × tint`）——`color` 的 source-over 语义与非玻璃一致性优先；`tintBlend` 扩展位。
- **cornerSmoothing / 切角**——沿用 procedural-style §7 的留位。
- **组跨层级成员**（非直接子级参与融合）——直接子级已覆盖目标场景，跨级收集 rect 的坐标换算与失效追踪不值当。

## 10. 测试（Red 先行；§12.2 教训全程适用）

EditMode：

1. **参数接线**：8 个玻璃属性 setter → `PanelParams` 断言；范围/格式错误 ParseException；`glass="false"` 时 key 的 glass 段全零（缓存不分裂）。
2. **材质缓存**：同 style 玻璃面板共享材质；glass on/off Variant 往返换 key 不 Destroy；fallback keyword 进 key。
3. **降级链**：`UI.Glass.Enabled=false` → 面板 fallback 生效（材质 keyword 断言）；`#if !PROMPTUGUI_HAS_URP` 分支编译（CI 无 URP 变体如可行）。
4. **融合（M4）**：成员收集（直接子级 / glass=true 过滤 / 上限）；父子参数错位 lint；suppressed 成员空 mesh；组 uniform 数组值断言（rect 换算到父局部空间）；union AABB 几何。
5. **lint / XSD**：§8 全部规则用例；substring 断言。

**真渲染验证（强制，EditMode 绿不算数）**：

6. `CanvasRebuildTests` 扩展：玻璃面板 + 组面板各一条 `Canvas.ForceUpdateCanvases()` 强制 rebuild 不抛异常、CanvasRenderer 有网格。
7. 离屏渲染 PNG 肉眼校验（procedural-style §12.4 流程）：花纹背景上渲玻璃面板，逐项确认——模糊生效（对比无玻璃）、frost 两档差异、边缘折射带只在边缘、lightAngle 转 90° 高光跟着转、dispersion 边缘彩边、tint 渐变方向、fallback 视觉 == 非玻璃半透明面板；M4：两块融合无内部分割线、depth 台阶可见、weld 0/10/24 对比。

PlayMode（宿主 URP 激活时）：开一个玻璃 Screen 跑一帧，断言 `Shader.GetGlobalTexture("_PUGUI_GlassBackdropA")` 非空；关掉所有玻璃 Screen 后系统退订（计数归零观察口）。

## 11. SKILL 更新（同 PR，英文）

- `authoring-promptugui-xml/SKILL.md`：`<Frame>` 属性表加玻璃行 + 指向新 deep-dive 的 stub；主表同步。
- **新建 `authoring-promptugui-xml/reference/glass.md`**（参数多、backdrop 语义有坑，符合 per-feature deep-dive 模式）：全参数表、backdrop 语义与推荐 canvas 结构（§3.2 三条规则）、反馈回环警告、降级链视觉、weld 组的父/子参数分工、与 Style/Variant 的组合示例。
- `scripting-promptugui-csharp/SKILL.md`：`UI.Glass.Enabled` / `UI.Glass.Camera`（公共 API 面新增）。
- CLAUDE.md 的 SKILL 触发路由表加一行：玻璃相关改动 → `reference/glass.md`。

## 12. 里程碑拆分

| | 内容 | 依赖 |
|---|---|---|
| **M3a 玻璃面板** | §3 属性 + §4.1/4.2（shader、参数、缓存扩展、fallback keyword）+ 降级链 + lint | procedural-style（已合并） |
| **M3b backdrop 供给** | §4.3/4.4（GlassBackdropSystem、URP 门控）+ §5 `UI.Glass` | M3a（fallback 让 M3a 可独立验收） |
| **M4 融合** | §7 全部 | M3a + M3b |

M3a 先行的意义：不碰渲染管线就能把属性、缓存、降级、lint 全部红测落地；M3b 合入前 M3a 的玻璃面板以 fallback 视觉工作，宿主无 URP 的用户永远停在这个形态。

（实施时 M3a/M3b/M4 一次做完，分支 `feat/glass-fill`。）

## 13. 实施记录：与本设计的偏离

### 13.1 降级不是 shader keyword，是一个全局标量

设计 §6 说 fallback 进 `PanelParams` key、靠 keyword 翻转。实施改成全局 `_PUGUI_GlassBackdropAvailable`
（`Shader.SetGlobalFloat`），原因是原方案把成本算反了：keyword 进 key 意味着**画质开关一翻，场上每一个
玻璃面板都要重新算 key、重新 Acquire、重新赋 material**——而换 material 就是一次 canvas 材质重建。
一次 `SetGlobalFloat` 免费触达全部面板；它喂的分支对所有 fragment 取值相同，GPU 不会发散。
`UI.Glass.Enabled` 因此可以随便在运行时翻，零材质churn。

### 13.2 不检测 Compatibility Mode

设计 §4.3/§6 要求检测 URP 的 Compatibility Mode 并 warn。实际上 `RenderGraphSettings` 在 Unity 6000.4+
已标 `[Obsolete("These settings are not used")]`，探测它只会在宿主工程里刷两条 CS0618。而这个分支的降级
本来就是对的：Compatibility Mode 下 `RecordRenderGraph` 根本不会被调用 → 没人发布 backdrop → 面板保持
fallback。于是直接删掉检测，把理由写进代码注释。

### 13.3 材质参数改为「每次 canvas 重建解算一次」

设计没提这件事，实施时发现必须做：一次实例化会连写十几个视觉属性（玻璃把 Frame 的视觉属性从 6 个推到
15 个），而原来的 `ApplyParams` 是**每个 setter 立刻解算一次材质** —— 一个玻璃面板开屏要走十几次
「算 key → 查字典 → Acquire → Release」。改成 setter 只打脏标记（`MarkDirty`），`FlushParams` 在
`Frame.OnAfterApply` 与 `UpdateMaterial` 各收口一次。

顺带修掉一个隐藏的顺序陷阱：`ControlAttributeApplier` 遍历的是 **HashSet**，属性应用顺序不确定，
`glass` 完全可能排在 `frost` 后面。参数集中在 flush 时读取之后，写在前写在后都一样。

### 13.4 `GlassGroupPanel` 挂在子 GameObject 上，不在 weld 容器上

`Graphic` 带 `[DisallowMultipleComponent]`。weld 容器本身已经需要一个 `ProceduralPanel` 承载组级参数，
再 `AddComponent<GlassGroupPanel>()` 会**静默返回 null**，然后容器的 `frost=` setter 抛 NRE。
改为 `GlassGroupPanel.Attach(container)` 建一个撑满容器的子物体，`SetSiblingIndex(0)` 保证融合后的
玻璃画在所有子内容之后面。成员 rect 也随之改用 `TransformPoint`/`InverseTransformPoint` 换算，
不再手算 localPosition —— 容器、块、组子物体三方的 pivot 不一定一致。

### 13.5 融合组里的描边 / 发光是组级的

设计 §7.1 把 `borderWidth` / `borderColor` 列为逐块参数。实际上在一片融合玻璃里，逐块描边画出来的
**恰好就是 weld 要消灭的那条分割线**。改为描边与发光跟随融合后的外轮廓、由容器持有；`radius` /
`depth` / `color` 保持逐块。lint 相应地把容器上的 `color` / `radius` / `depth` 一起报出来。

### 13.6 真渲染验证放在 EditMode，不是 PlayMode

设计 §10 把逐像素验证放 PlayMode。实测 PlayMode 测试在**失焦的 Editor 下不推进帧**，会挂死而不是失败
（`PlayerSettings.runInBackground` 已经是 true 也一样）。而 `Camera.Render()` 在 EditMode 下能完整同步
驱动 URP —— `beginCameraRendering` 正常触发、RenderGraph 正常录制、像素能回读。于是验证改到 EditMode：
确定性、不依赖窗口焦点、CI 也能跑。为此加了一个测试缝 `GlassRuntime.RenderOutsidePlayModeForTests`
（默认 false，`ResetForTests` 复位）来绕过「只在 Play 模式采集」的门。

顺带确认设计 §9 里「玻璃不在编辑器里预览」这条保持成立：那个门只对测试打开。

### 13.7 模糊链用三张常驻 RT

设计 §4.3 写的是「两张常驻 + RenderGraph 临时纹理」。实施用三张常驻（A 轻 / B 重 / scratch）：
RenderGraph 的临时纹理活不过当帧，而 Overlay canvas 在图之外渲染；三张 1/4 分辨率 RT 在 1080p 下
合计约 1.2MB，换掉对 `TextureDesc` API 形状的依赖是划算的。链路是 3 次 blit：
`camera color →(降采样+模糊) A →(模糊) scratch →(重模糊) B`。

## 14. 验证记录

- EditMode 2201 / EditorOnly 308（既有全部 + 新增 54：玻璃参数 30、weld 组 18、lint 30、渲染 6）全通过。
- `dotnet format --verify-no-changes --severity warn` 干净；UIXmlLint CLI 编译并运行新的
  `GlassAttrParser` / `GlassRules`（7 条规则在 CLI 侧实测报出）。
- **离屏渲染肉眼校验**（512×512，背景是本库自己画的高对比彩块 + 白十字）：
  - 模糊真实生效，`frost` 0 / 0.5 / 1 三档差异清晰可辨；
  - 形状、圆角、AA、边缘打光、1px 保底描边都正确，panel 外部干净（SDF 没有画满整个 quad）；
  - `weld` 0 / 14 / 30 三档：0 时两块各自独立、中间一条明显断缝；30 时融成一整片连续 L 形，
    交界处是平滑凹角，**没有任何分割线**；14 介于两者之间。融合按设计工作。
  - tint 叠加、fallback（关掉 `UI.Glass.Enabled` 后中心回到纯黑）均符合预期。

## 15. 代码审查修复（2026-08-24）

多智能体审查（`5af1619..HEAD`，high 档）报出 15 项，13 项确认。全部已修，测试从 2208 涨到 2251。
下面按"为什么原来是错的"分组，附带两处补记的 §13 遗漏。

### 15.1 补记：两处错误契约在实施时降级为 lint（原 §13 漏记）

本 spec §7.1 把 **weld 与 glass 同节点**、**成员超过 8 块** 定为 parse error，实施时都改成了
lint + 运行时容忍（超额成员自己画）。改得对——parse error 会让一个能跑的布局直接开不了屏，
而这两种写法都有确定且无害的运行时行为——但 §7.1 正文一直停留在旧契约，照它写测试会得到
相反的期望。正文已随本次修复更新。

### 15.2 ReSolve 是前序遍历，weld 成员资格滞后一整轮（最严重）

`Screen.ReSolve` 遍历 `_nodeMap` 的**插入序**，也就是父先子后；而 Screen 首次构建走的是后序
`ApplyOrder`。`Frame.OnAfterApply` 的注释假设两条路径同序，于是：Variant 把某个子级的 `glass`
翻转时，容器的 `SyncMembers` **已经**按旧值同步完了。后果是被取消玻璃的块继续以融合玻璃渲染
（而它自己的属性说它是普通 Frame），或新玻璃块当轮不融合、留下 weld 本该消灭的那条缝。

修法：`ProceduralPanel.SetGlass` 主动通知所在（或所在父级下的）`GlassGroupPanel` 重扫成员
（`RequestMemberRescan`）。已是成员时组已知；首次变玻璃时到父级子物体里找组——`Attach` 就放在那儿。
不改 ReSolve 的遍历序：那是全局行为，为一个控件改它风险远大于收益。

### 15.3 weld 容器被无条件 suppress，渲染结果依赖历史

`SyncMembers` 一进门就 `_container?.SetSuppressed(true)`，与是否真的在融合无关。于是一个组只要
存在过，容器自身的描边/发光就**永久**消失；更糟的是取消 weld 的两条路径终态相反——直接 setter
走 `ReleaseMembers` 解除抑制，而 Variant 走 `OnAfterApply → SyncMembers` 又抑制回去。同样的解算
属性、不同的历史、不同的画面。既有测试恰好只走了 setter 那条路径，把 bug 盖住了。

修法：`SetSuppressed(active)`，`active` 就是"真的在融合"。

### 15.4 backdrop 停产后冻结成一张死图

可用标记由采集 pass 锁存 true，而清除只发生在"采集被停"或"相机为 null"两条路径上。相机对象
还在、只是被禁用（过场、加载界面）时，`beginCameraRendering` 仍为其他相机触发但在
`camera != target` 处提前返回——没有任何人清除标记，所有玻璃永远采样最后那一帧，
`UI.Glass.IsActive` 还报 true。

修法用**看结果新鲜度**而不是逐个枚举原因：`GlassRuntime` 记录最后发布帧号，挂
`Canvas.willRenderCanvases`（它每帧都跑，即使一台相机都没渲染——这恰恰是全相机禁用时唯一能观测
到的钩子）做 2 帧陈旧判定。一个看门狗同时覆盖相机禁用/销毁、管线切换、Compatibility Mode。
另加一条即时路径：目标相机 `!isActiveAndEnabled` 时当场清标记，不必等看门狗。

### 15.5 lint 看不见 `<Style>`/`class` 带来的 glass（把正确布局判成硬错误）

`GlassRules` 只读节点自身的 `Attributes`/`VariantOverrides`，而 `glass="true"` 经 `<Style>` 到达
是本库自己的样例和 glass.md 都在推荐的写法。于是 `<Frame weld>` 下两个 `class="card"` 子级被数成
0 个玻璃子级 → `PUI-GLASS-WELD-MEMBERS` 硬错误，CLI 非零退出——而 CLAUDE.md 要求每次编辑
`.ui.xml` 后都跑 CLI。假阳性比它要防的静默属性更贵。

修法：新增 `StyleAttributeView`（`Core/Lint/`），按 `StyleMerger` 的同一套优先级（inline > 右
class > 左 class，按属性名原子）解析"这个节点最终会有哪些属性"，`IRWalker` 从 `doc.Styles` 建好
传下去。**解析不了的一律沉默**：class 指向本文件没声明的样式（十有八九来自 import，单文件 CLI
永远看不到）或值里还有 `{{param}}` 时，结构性规则整条跳过而不是猜——与 `StyleRules` 刻意不做
"未知 class 名"检查同一个道理。

### 15.6 shader 法线取自光栅空间导数（跨平台翻转 + 分支内导数）

`float2 grad = float2(ddx(d), ddy(d))` 有两个问题：`lightAngle` 是**画布空间**概念（0 = 界面正
上方），而光栅 Y 轴朝向逐平台不同（D3D/Metal 向下、GL/GLES/WebGL 向上），于是高光和折射方向在
GL 目标上整个上下翻转——同一份 XML 在 WebGL 构建里长得不一样且不报错，而 WebGL 是本项目明示的
支持目标；此外该语句位于逐像素的 `inside > 0` 分支内，非均匀控制流里的导数是未定义行为。

修法：`PuguiSdNormal` 改成圆角矩形 SDF 的**解析**法线（直接对 `PuguiSdRoundBox` 求导），画布空间、
零额外 SDF 求值、不依赖任何平台约定。融合组更省：把各成员的解析法线按 pass-2 **已经算好**的那组
权重混合即可——这正是 smin 对梯度做的事，高光照旧沿融合外轮廓平滑流动。屏幕导数只留下取
`length()` 换算"一屏幕像素等于多少画布单位"，长度与轴向无关，且已提到分支外。

补了一条真渲染回归测试 `EdgeHighlight_LandsOnTheSideTheLightComesFrom`：`lightAngle` 0 与 180
各渲一帧、比上下半幅亮度差。既有渲染测试用了 `lightAngle` 却从不断言高光落在哪侧——正是这个
缺口让平台翻转能一路绿灯。

### 15.7 其余（同批修掉）

| 问题 | 修法 |
|---|---|
| `<Add at='start'>` 把 `GlassWeld` 子物体挤离 sibling 0，融合玻璃盖住新增内容 | `SyncMembers` 每次重申 index 0，不再假设 |
| 已销毁成员触发 `MissingReferenceException`，`ParseException` 包装后杀死整轮 ReSolve | `previous` 循环 + `Frame.OnAfterApply` 全改 Unity `== null` 判断；`ProceduralPanel.OnDestroy` 主动退组 |
| `frost="NaN"` / `depth="Infinity"` 穿过校验直达 shader（`v < min \|\| v > max` 对 NaN 恒 false） | `GlassAttrParser` / `RadiusParser` / `Frame.ParsePixels` 三处补有限性检查 |
| 成员上写 `borderWidth`/`glow` 被静默吃掉且无 lint | 并入 `GroupAttrs`，按 `PUI-GLASS-WELD-PARAM-PLACEMENT` 报 |
| XSD 生成器漏了全部 9 个玻璃属性（Frame 是手写列表、非反射） | 补齐，并在注释里点明"Frame 加属性必须同步这里" |
| 五个 warn-once 静态不复位，关域重载时二次 Play 起诊断全哑 | `GlassRuntime.ResetAll` 统一清，挂 `[RuntimeInitializeOnLoadMethod]` + `ResetForTests` |
| 成员纯位移（尺寸不变）不触发重打包，融合形状留在旧位置 | `GlassGroupPanel.LateUpdate` 比对已打包的 rect，变了才 dirty |
| glass.md 说单成员 weld 是良性的，lint 却报硬错误；四个错误码任何文档都查不到 | glass.md 更正 + 新增 Lint codes 一节 |

### 15.8 验证

- EditMode 2251 / 2251 通过（原 2208 + 新增 43）。新增测试文件：`GlassWeldLifecycleTests`
  （6 条：Variant 双向翻转成员、weld 取消、销毁成员、Add 块索引）、`GlassRulesStyleAwareTests`
  （11 条：class 带 glass 的各条路径 + 无法解析时沉默）、`NumericGuardTests`（12 条）、
  `GlassBackdropWatchdogTests`（4 条），外加 `GlassRenderTests` 的高光方向回归。
- `dotnet format --verify-no-changes --severity warn` 干净；UIXmlLint CLI 重新编译，对
  `Samples~/ProceduralStyle/Resources/UI/` 四个文件零 issue。
- 离屏渲染肉眼复核：单面板亮边仍在左上（`lightAngle='-35'`）、圆角与 1px 描边正常、面板外干净；
  weld 组仍是连续 L 形、交界无分割线、厚度台阶可辨。解析法线没有改变观感。
