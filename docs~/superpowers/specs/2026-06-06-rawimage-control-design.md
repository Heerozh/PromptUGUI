# `<RawImage>` 控件设计（C# 设置动态 Texture）

- **日期**: 2026-06-06
- **状态**: 设计已与用户敲定，待落 plan
- **分支**: `feat/raw-image`
- **关联**: 主 spec `2026-05-07-promptugui-description-language-design.md`（新增控件行）；复用 `2026-06-05-image-fit-cover-contain-design.md` 的 `AspectRatioFitter` 适配方案

## 1. 背景与动机

现有 `<Image>` 走 sprite / SpriteSet 通道（`UI.ResolveSprite`），面向作者预先准备好的静态资源。但有一类需求是**运行时动态加载的 `Texture`**——头像、下载的图片、截图、`RenderTexture` 等——由 C# 代码加载后直接赋到界面上，既不是 sprite 也不进 SpriteSet/atlas。

uGUI 对此有专门组件 `UnityEngine.UI.RawImage`（直接渲染任意 `Texture`，不需要 `Sprite` 包装）。本设计新增 `<RawImage>` 控件，把它接入 PromptUGUI：XML 端声明位置与外观、C# 端用一个 `Texture` 属性把动态 texture 推进去。

## 2. 设计决策（已敲定）

1. **新增 tag `<RawImage>`**，`sealed class RawImage : Control, IPointerEventSource`，底层 `GetComponent/AddComponent<UnityEngine.UI.RawImage>`。在 `BuiltinPrimitives.Register` 注册 `reg.Register<RawImage>("RawImage", null);`。
   - 控件类名 `PromptUGUI.Controls.RawImage` 与 Unity 类型 `UnityEngine.UI.RawImage` **重名**，控件文件内用别名 `using UnityRawImage = UnityEngine.UI.RawImage;`（仿 `Image.cs` 的 `using UnityImage = ...`）。`BuiltinPrimitives.cs` 不 import `UnityEngine.UI`，`RawImage` 在那里解析为控件类，无冲突。

2. **Texture 一律 C# 设置，无 XML texture/src 属性**。控件暴露公开属性：
   ```csharp
   public UnityEngine.Texture Texture { get; set; }   // 非 [UIAttr]，纯 C# API
   ```
   用法：`screen.Get<RawImage>("preview").Texture = myTexture;`（`Texture2D` / `RenderTexture` 均可）。getter→`_raw.texture`；setter→赋值 `_raw.texture` 并触发 `RecomputeAspect()`（见 §4）。

3. **沿用 `type=`，只支持 `contain` / `cover` 两个值**（与 `<Image>` 命名一致）。
   - `contain` → `AspectRatioFitter`，`FitInParent`（等比塞进父框、留边）；
   - `cover` → `AspectRatioFitter`，`EnvelopeParent`（等比撑满父框、溢出）；
   - 框 = **直接父级 rect**；裁切由作者在父级 `mask="rect"` 负责（与 Image fit 完全一致的语义）。
   - `simple` / `sliced` / `tiled` / `filled` 是 sprite 专有渲染模式，RawImage **不支持**。作者误写这些值时发一条 `Debug.LogWarning`（仿 `ImageTint` 对未知 `tint` 的处理）并回退普通模式（关掉 fitter），不静默吞掉。
   - 空 / 不写 `type` → 普通模式（不创建/关闭 fitter），texture 直接按 rect 拉伸铺满（RawImage 默认 `uvRect=(0,0,1,1)`）。

4. **镜像 `<Image>` 的外观 / 交互能力**（用户确认 parity 全留）：
   - `color`（`IsColor`）→ `_raw.color` 乘色。
   - `tint`（`multiply` / `linear`）→ 复用 `ImageTint`（需把其参数从 `UnityEngine.UI.Image` 放宽到 `Graphic`，见 §5）。
   - `mask` / `showMask` / `maskPadding` → 与 Image 同（`RectMask2D` / stencil `Mask` + `MaskPaddingParser`）。
   - `IPointerEventSource`（懒挂 `PointerEventRelay`）→ `OnPointerEnter/Exit/Down`，可作 `<Trigger>` 的 hover/press 源。
   - `GetNativeSize()` → 读 texture 像素尺寸（见 §6）。

## 3. uGUI 映射（属性表）

| 属性          | 落到 uGUI                                | 备注                                                                 |
| ------------- | ---------------------------------------- | -------------------------------------------------------------------- |
| (C#) `Texture`| `_raw.texture`                           | 唯一图源；无 XML 对应                                                 |
| `color`       | `_raw.color`                             | 乘色；无 texture 时 RawImage 仍按 color 画纯色 quad（uGUI null→白图） |
| `type`        | `AspectRatioFitter`（仅 contain/cover）  | 见 §2.3；其余值 warn + 关 fitter                                     |
| `tint`        | `_raw.material`（经 `ImageTint`）         | `multiply`→null 材质；`linear`→LinearLightTint                       |
| `mask`        | `RectMask2D`（`rect`）/ `Mask`（`self`） | 与 Image 同                                                          |
| `showMask`    | `Mask.showMaskGraphic`                   | 与 Image 同                                                          |
| `maskPadding` | `RectMask2D.padding`                     | 与 Image 同                                                          |

> `StateReact`（来自 `Control` 基类）自动可用，无需额外处理。

## 4. `Texture` setter 与 `AspectRatioFitter` 重算时序

Image 在 `OnAfterApply` 里按 `sprite.rect` 算 `aspectRatio`，因为 sprite 在属性循环里就到位了。RawImage 的 texture **是 Open 之后才由 C# 赋值**，属性循环跑完时 texture 还是 null。所以把重算下沉到一个共享方法，由 **`Texture` setter** 与 **`type` setter** 两处触发：

```csharp
private AspectRatioFitter _fitter;
private AspectRatioFitter EnsureFitter() => _fitter ??= GameObject.AddComponent<AspectRatioFitter>();

private void RecomputeAspect()
{
    if (_fitter != null && _fitter.enabled && _raw.texture != null && _raw.texture.height > 0)
        _fitter.aspectRatio = (float)_raw.texture.width / _raw.texture.height;
}

public UnityEngine.Texture Texture
{
    get => _raw.texture;
    set { _raw.texture = value; RecomputeAspect(); }
}

[UIAttr, Preserve]
public string Type
{
    set
    {
        switch (value)
        {
            case "contain":
            case "cover":
                var f = EnsureFitter();
                f.enabled = true;
                f.aspectMode = value == "cover"
                    ? AspectRatioFitter.AspectMode.EnvelopeParent
                    : AspectRatioFitter.AspectMode.FitInParent;
                RecomputeAspect();   // 若 texture 已由 C# 设过（ReSolve 路径）则即时重算
                break;
            case null:
            case "":
                if (_fitter != null) _fitter.enabled = false;
                break;
            default:
                Debug.LogWarning($"PromptUGUI: <RawImage type=\"{value}\"> 仅支持 'contain' / 'cover'；" +
                    "simple/sliced/tiled/filled 是 <Image> 的 sprite 专有模式，已忽略。");
                if (_fitter != null) _fitter.enabled = false;
                break;
        }
    }
}
```

- **时序**：初次实例化时 texture 恒为 null（C# 只能在 Open 后 `Get<>` 拿到控件再赋值）→ `type` setter 里的 `RecomputeAspect` 是 no-op；真正的比例计算发生在之后 C# 赋 `Texture` 时。换图（再次赋 `Texture`）自动重算。
- **不需要 override `OnAfterApply`**：两个 setter 已覆盖「初次 + 换图 + ReSolve 重应用 `type`」全部路径，比 Image 更精简。
- **`AspectRatioFitter` 生命周期**：懒创建、靠 `enabled` 开关复用、绝不 `Destroy`（同 Image）。它是 `ILayoutSelfController`，父级 resize 自动重排。
- **fit 模式下 RawImage 自身 `anchor`/`size`/`margin` 被 ARF 接管失效**（同 Image §2.4）：框由父级决定。

## 5. `ImageTint` 参数放宽到 `Graphic`

当前 `ImageTint.Apply(UnityEngine.UI.Image img, string mode)` 形参是 `Image`，但实现只读写 `img.material`（`Graphic` 成员）。把签名改为 `Apply(UnityEngine.UI.Graphic img, string mode)`：

- 实现一字不改；
- `Image : MaskableGraphic : Graphic`，现有调用方（`Image`/`Btn`/`Progress`/`Tab` 等）传 `Image` 实参隐式上转，**零改动编译通过**；
- `RawImage : MaskableGraphic : Graphic`，可直接复用。
- `ImageTint.cs` 顶部补 `using UnityEngine.UI;`（取 `Graphic`）。

`internal` 类型，影响面受控。

## 6. `GetNativeSize`

```csharp
public override Vector2? GetNativeSize()
{
    if (_raw == null || _raw.texture == null) return null;
    return new Vector2(_raw.texture.width, _raw.texture.height);   // 像素 1:1，RawImage 无 PPU
}
```

- RawImage 无 `pixelsPerUnit`（那是 sprite 概念），texture 像素直接 1:1 映射 UI 单位（对齐 `RawImage.SetNativeSize()`）。
- **注意时序**：texture 是 Open 后才赋值，所以 `size="native"` 仅在「实例化前已同步赋 texture」的特殊场景才有值；常态请显式写 `size` 或用 `type="contain"/"cover"`。文档需提示。

## 7. 边界 / 非目标（YAGNI）

- **`uvRect`（子区域 / 平铺）**：RawImage 的特色能力，但非本次需求，v1 不暴露。
- **XML 端从 Resources 加载 texture 的 `src`**：texture 一律 C# 设置，不引入 texture 资源解析通道。
- **`type` 的 lint 规则**：Image 有 `PUI-IMAGE-FIT-VARIANT` / `PUI-IMAGE-FIT-GEOMETRY`。RawImage 的 `type=contain/cover` 同样不宜进变体、fit 下几何失效——但为收敛范围，v1 **暂不**为 RawImage 加这两条 lint，仅在 XML SKILL 文档提示。后续可作 parity follow-up（规则实现在 `Runtime/Core/Lint/`，到时按 tag 泛化）。
- **LayoutGroup 直接子级**：fit 模式 RawImage 直接做 `<VStack>`/`<HStack>`/`<Grid>` 子级时 ARF 与 LayoutGroup 抢布局，行为未定义；仅文档提示「套一层 `<Frame>`」（同 Image）。
- **`raycastTarget`**：保持 uGUI 默认 `true`（同 Image，无新属性暴露）。

## 8. 测试（EditMode，TDD：先红后绿）

控件行为（`PromptUGUI.Tests.EditMode`）：

1. `<RawImage>` 实例化后 GameObject 上有 `UnityEngine.UI.RawImage`。
2. `Texture` get/set：C# 赋值后 `_raw.texture` 等于所赋 texture，getter 取回同值。
3. `color="#RRGGBB"` → `_raw.color` 应用。
4. `tint="linear"` → `_raw.material` 为 LinearLightTint（非 null）；`tint="multiply"`/不写 → null。
5. `type="contain"` → 有 `AspectRatioFitter`、`enabled`、`FitInParent`；`type="cover"` → `EnvelopeParent`。
6. 赋 `Texture`（已知比例，如 4×2）后 `_fitter.aspectRatio == 2.0`；换一张（如 2×4）后重算为 0.5。
7. `type="simple"`（或其它 sprite 模式）→ 不创建/关闭 fitter（行为退化为普通），且不抛异常。
8. 不写 `type` → 无 `AspectRatioFitter`。
9. `GetNativeSize()`：无 texture→null；赋 W×H texture→`(W, H)`。
10. `mask="rect"` → 有 `RectMask2D`；`mask="self"` → 有 stencil `Mask`。

回归（确保 `ImageTint` 放宽签名不破坏现有控件）：`<Image>` / `<Btn>` 的 `tint="linear"` 测试仍绿（现有测试覆盖即可，无需新增）。

XSD（`PromptUGUI.Tests.EditorOnly`）：确认 `XsdGenerator` 自 registry 自动产出 `<RawImage>` element（新 tag 应自动出现）；若 EditorOnly 有「全 tag 枚举」类断言按需补 `StringAssert.Contains("RawImage")`。

## 9. 文档更新（同 PR）

- **主 spec** `2026-05-07-...-design.md`：控件目录加 `<RawImage>` 行（uGUI RawImage：C# 设 Texture + 等比适配 + mask）。
- **XML SKILL** `.claude/skills/authoring-promptugui-xml/SKILL.md`（英文）：
  - 内置控件目录加 `<RawImage>` 行；
  - 新增 `<RawImage>` 小节：属性表（`color`/`type=contain|cover`/`tint`/`mask`…）、「texture 由 C# 设置、无 sprite」说明、`type` 仅 contain/cover + 父级框 + 作者负责裁切、native-size 时序提示、LayoutGroup 套 Frame 提示。
- **C# SKILL** `.claude/skills/scripting-promptugui-csharp/SKILL.md`（英文）：加 `RawImage.Texture` get/set 用法（`screen.Get<RawImage>("id").Texture = tex;`）。
- **Addressables SKILL**：无 `PROMPTUGUI_HAS_ADDRESSABLES` 相关变更，不动。
