# Image Tint Blend Mode 设计

**日期**: 2026-05-28
**状态**: 设计阶段（待 review，未进入实施）
**作用域**:
1. 新增 `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.shader` / `.mat`（已由作者落盘，仅 review）
2. 新增 `Runtime/Controls/Internal/ImageTint.cs`（material 加载 / 缓存 / 应用 helper）
3. 各 Image-backed 控件加 `[UIAttr] Tint` setter：`Image` / `Icon` / `Btn` / `Toggle` / `Slider` / `Dropdown` / `ScrollList` / `InputField` / `Progress`
4. `authoring-promptugui-xml/SKILL.md` 列入新 attr + 取值表
5. 主 spec `2026-05-07-promptugui-description-language-design.md` §5/§6 控件表小注脚指本文

**依赖**: 无（独立扩展；不需要 R3、不依赖 Theme / Variant 改动）

---

## 1. 背景

控件的 `color="..."` 是给 `UnityEngine.UI.Image.color` / `TMP_Text.color` 写值，最终走 Unity UI 默认 shader（`UI/Default`）的 **multiply blend**：

```
fragment.rgb = sprite.rgb * tintColor.rgb
fragment.a   = sprite.a   * tintColor.a
```

multiply 的语义是「sprite 决定形状和明度，tint 染色」。但像素风游戏里有一种常见 sprite：**美术把灰度图当 base**（128 灰 = 中性），希望 tint 能往两端拉 —— 深色区域要能被 tint 拉到接近黑，亮色区域要能被 tint 拉到接近白。multiply 永远只会让 sprite 变暗，做不到这一点。

`Photoshop` 的 **Linear Light** 混合模式公式：

```
result = base + 2 * blend - 1
```

正好满足：blend = 128 灰时 `result = base`（中性），blend 越深 result 越偏 base 下方、blend 越亮 result 越偏 base 上方。把 sprite 当 blend、tint 当 base，就实现了「128 灰中性、tint 决定基色」的染色。

作者已经写好 `UI-LinearLightTint.shader`（gamma 空间算，保住 "128 = 中性" 的直觉），下一步要给 XML 作者一个声明式开关，不暴露 Unity material 资源路径。

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| TINT-D1 | 暴露形态 | 独立 enum attr `tint="multiply\|linear"`，跟 `color` 解耦 | 跟现有 `color` / `sprite` 单一职责 attr 风格一致；XSD 能枚举合法值；以后扩 enum（grayscale / outline）不改语法 |
| TINT-D2 | 默认值 | `multiply` —— 不写 `tint` 等于 `tint="multiply"`，运行时 `image.material = null` | 不破坏现有 `.ui.xml`；null 让 Graphic 走 `defaultGraphicMaterial`（UI/Default），跟手写 `tint="multiply"` 完全一样 |
| TINT-D3 | 哪些控件支持 | 所有 Image-backed 控件：`Image` / `Icon` / `Btn` / `Toggle` / `Slider` / `Dropdown` / `ScrollList` / `InputField` / `Progress` | `tint` 通过换 Image.material 实现，覆盖范围必须跟「持有 Image 的控件」一致 |
| TINT-D4 | `Text` (TMP) 是否支持 | 否 | TMP shader stack（`TextMeshPro/Distance Field` 系列）独立于 uGUI Image shader；换 `UI-LinearLightTint` 到 TMP 上不工作。CLAUDE.md 也提醒过 "Text 用 TMP 不走 Image" |
| TINT-D5 | `Progress` 的三个 Image 怎么切 | `tint` 一次切到 `_fill` + `_bg` + `_frame` 三个 | 用户视角是 "Progress 整体走 linear blend"，每个图层单独切 tint 是 YAGNI；维度爆炸（per-layer × tint mode）也跟 D1 简洁取向冲突 |
| TINT-D6 | `Progress._maskGraphic` 是否跟切 | 否 | mask graphic 是隐藏 helper（`showMaskGraphic = false`），用户看不到；material 跟它无关 |
| TINT-D7 | 未识别 tint 值 | `Debug.LogWarning` + 回退 multiply（material = null） | 跟现有 `Type` setter 的 fallback 风格一致（`Image.cs:53` 未匹配走 Simple）；不抛异常 |
| TINT-D8 | material 资源加载 | `Resources.Load<Material>("PromptUGUI/Material/UI-LinearLightTint")`，静态缓存一份 | 资源在 `Runtime/Resources/PromptUGUI/Material/`，Player 自动包含；首次 lazy load |
| TINT-D9 | 共享 vs 实例化 material | 共享（写入 `image.material = sharedMat`，**不读** `image.material` getter） | Unity Image 的 `.material` setter 不会触发实例化；只有 `.material` getter 才会。整个进程一份 LinearLightTint material，零 GC 压力 |
| TINT-D10 | Variant 覆盖 | 自动支持（走 `attr.var` 现有机制） | `tint` 就是普通 `[UIAttr]` setter，`Screen.ReSolve` 会重设；setter 内部用 `??` cache 不重复 load |
| TINT-D11 | XSD 是否枚举合法值 | 否（v1 跟 `Type` 一样 declare 成 `xs:string`） | 现有 XSD 生成器没有 enum 表达；扩枚举时再统一抽 `[UIAttr(Enum=...)]` flag。本次不顺便做 |
| TINT-D12 | Lint 是否检查非法值 | 否（v1 不加 lint 规则） | XSD 不约束 + runtime warning 已经够。等 D11 实现 enum 表达再统一做 lint |
| TINT-D13 | `tint` 与 R3 binding | 不支持（与 `color` 的 `{name}` binding 形态无关） | tint 是 material 切换，不是连续值；运行时切 material 走 Variant 覆盖路径已够 |
| TINT-D14 | `tint` 作用到 Animation `char-color` 之类的颜色动画 | 不影响 | char-color 是改 `TMP.color`，TMP 不走 Image material（D4） |
| TINT-D15 | SKILL 更新范围 | 只动 `authoring-promptugui-xml/SKILL.md`；C# SKILL 不动 | `tint` 只是新 XML attr，没新增 public C# API（`ImageTint` 是 internal） |

---

## 3. XML 语法

每个 Image-backed 控件多一个属性：

| 控件 | `tint` 作用图层 |
|---|---|
| `<Image>` | 自身 Image |
| `<Icon>` | 自身 Image |
| `<Btn>` | 背景 Image |
| `<Toggle>` | 背景 Image |
| `<Slider>` | 背景 Image |
| `<Dropdown>` | 背景 Image |
| `<ScrollList>` | 背景 Image |
| `<InputField>` | 背景 Image |
| `<Progress>` | `_fill` + `_bg` + `_frame` 三个 Image |

合法值：

| 值 | 行为 |
|---|---|
| `multiply` | (默认) `image.material = null`，走 Unity `UI/Default`，sprite 跟 tint 相乘 |
| `linear` | `image.material = UI-LinearLightTint`，sprite 当 blend、tint 当 base 做 Linear Light（128 灰中性） |
| 其他 | `Debug.LogWarning` + 回退 multiply |

```xml
<!-- 灰度 sprite 做染色 base -->
<Image src="card-grayscale" color="#FF8040" tint="linear" />

<!-- 默认 multiply 跟之前完全一样 -->
<Image src="card-color" color="#888888" />

<!-- Theme token + linear -->
<Btn label="Accent" color="<Theme>/<Accent>" tint="linear" />

<!-- Progress 三层一起 linear -->
<Progress value="0.6" color="#80FF80" bgColor="#404040" tint="linear" />

<!-- Variant 切换 tint 也 OK（走现有 attr.var 机制）-->
<Image src="card" color="#FFFFFF" tint="multiply" />
<Variant when="@hilight=on">
  <Image id="..." tint.var="linear" />
</Variant>
```

---

## 4. C# 实现

### 4.1 `Runtime/Controls/Internal/ImageTint.cs`（新文件）

```csharp
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Switches an <see cref="UnityImage"/> between Unity's default multiply tint
    /// (material = null) and PromptUGUI's Linear Light tint material. Material asset
    /// is shared process-wide and lazy-loaded from Resources on first use.
    /// </summary>
    internal static class ImageTint
    {
        private const string LinearLightTintResourcePath = "PromptUGUI/Material/UI-LinearLightTint";
        private static Material _linearLightTint;

        public static void Apply(UnityImage img, string mode)
        {
            if (img == null) return;
            switch (mode)
            {
                case null:
                case "":
                case "multiply":
                    img.material = null;
                    break;
                case "linear":
                    img.material = _linearLightTint ??=
                        Resources.Load<Material>(LinearLightTintResourcePath);
                    break;
                default:
                    Debug.LogWarning(
                        $"PromptUGUI: tint=\"{mode}\" is not a recognized value " +
                        "(expected: multiply, linear). Falling back to multiply.");
                    img.material = null;
                    break;
            }
        }
    }
}
```

### 4.2 控件 setter 模板

每个控件加一个：

```csharp
[UIAttr, Preserve]
public string Tint
{
    set => ImageTint.Apply(_img, value);   // 字段名按控件实际
}
```

例外是 `Progress`，要走三个 Image，且要处理 "_bg / _frame 后激活" 的时序问题：

```csharp
private string _pendingTint;   // 记下值，激活时补写

[UIAttr, Preserve]
public string Tint
{
    set
    {
        _pendingTint = value;
        ImageTint.Apply(_fill, value);
        if (_bg != null)    ImageTint.Apply(_bg, value);
        if (_frame != null) ImageTint.Apply(_frame, value);
    }
}
```

`_bg` / `_frame` 是按需激活的（spec §6 activation table）：当 `Bg=` / `BgColor=` / `Frame=` / `FrameColor=` setter 第一次创建它们时，要补一句 `ImageTint.Apply(_bg, _pendingTint)`。`_fill` 总是非 null（PB-D7）。

### 4.3 控件 image 字段对照

| 控件 | image 字段 |
|---|---|
| `Image` | `_img` |
| `Icon` | `_img` |
| `Btn` | `_bg` |
| `Toggle` | `_bg` |
| `Slider` | `_bg` |
| `Dropdown` | `_bg` |
| `ScrollList` | `_bg` |
| `InputField` | `_bg` |
| `Progress` | `_fill` + `_bg` + `_frame`（三个） |

---

## 5. 测试

EditMode（`PromptUGUI.Tests.EditMode`）：

```csharp
public class ImageTintTests
{
    // 1. 不写 tint：material is null (default)
    // 2. tint="multiply" 显式：material is null
    // 3. tint="linear"：material is UI-LinearLightTint Resources asset，且 shader name == "UI/LinearLightTint"
    // 4. tint="unknown"：material is null + LogWarning fired
    // 5. tint="linear" → tint="multiply"：material 回到 null
    // 6. Btn / Toggle 至少一个 _bg 路径走通
    // 7. Progress：tint="linear" 后 _fill / _bg / _frame 三个 material 都换了
    // 8. Variant override：<Variant when="...">tint.var="linear"</Variant> 切活后 ReSolve material 切换
    // 9. 共享：连续两个 <Image tint="linear"> 拿到同一个 Material 引用（不是 instance）
}
```

测试构造的 XML 用 `UI.LoadDocument(label, raw)` 同步路径，跟现有 `ImageControlTests` / `BtnControlTests` 同模式。

Material 加载：测试运行时 Unity 编辑器 Resources.Load 应能找到 `Runtime/Resources/PromptUGUI/Material/UI-LinearLightTint.mat`；如果 Resources 路径有空，回头改成 `UnityEditor.AssetDatabase.LoadAssetAtPath` 在 EditMode 测试里直接拿引用对比。

---

## 6. SKILL 更新

### 6.1 `authoring-promptugui-xml/SKILL.md`

- Built-in controls 表里 `<Image>` / `<Icon>` / `<Btn>` / `<Toggle>` / `<Slider>` / `<Dropdown>` / `<ScrollList>` / `<InputField>` / `<Progress>` 行 attribute 列追加 `tint="multiply|linear"`。
- 新增 "Tint blend modes" 小节（紧跟 color tokens 或 sprite 段后），含：
  - multiply (default) / linear 各自语义一行；
  - "灰度 sprite + linear" 一个典型用例；
  - "Text/TMP 不支持 tint" 提醒；
  - "Progress 三层一起切" 提醒。

### 6.2 主 spec `2026-05-07-promptugui-description-language-design.md`

§5 / §6 控件表里 Image-backed 行的 attributes 一栏追加 `tint`，并加注脚指向本文。

### 6.3 C# SKILL `scripting-promptugui-csharp/SKILL.md`

**不更新**。`ImageTint` 是 internal helper，没 public C# API 暴露。

---

## 7. Out of Scope

- Text/TMP 上的 tint 模式（D4：shader stack 不同；未来若做要单写 TMP material 设计）
- 用户注入自定 material（路线 B：`material="..."`；等真有需求扩 `tint` enum 或开独立 attr）
- per-layer tint on Progress（`tint.fill="linear" tint.bg="multiply"`；D5：YAGNI）
- R3 binding tint（D13：material 切换不是连续值）
- XSD enum 表达 / Lint 规则（D11 / D12：等 enum 抽象化再统一做）
- `<Image tint="linear">` 跟 mask 的交互：mask 走 stencil pass，tint 是 fragment shader，无冲突，不用特别处理

---

## 8. 风险与回滚

| 风险 | 缓解 |
|---|---|
| `Resources.Load` 路径写错 → `tint="linear"` 时 material 为 null → 视觉跟 multiply 一样，看似没生效 | Test 4（"linear" 后 shader name == "UI/LinearLightTint"）会立刻抓住路径错 |
| 用户在 `image.material` getter 上踩坑导致 material 实例化 → 内存泄漏 | `ImageTint.Apply` 只走 setter，没读 getter；helper 是 internal，第三方代码无法误用 |
| Linear shader 在 Linear color space 项目里没还原好 gamma | 已经在 shader 里写 `#ifdef UNITY_COLORSPACE_GAMMA` 分支处理，spec §1 解释了为什么；shader 注释也讲了 |
| 用户在 Variant 反复切 `tint` → material 频繁换 | 共享 material 切换是廉价的（指针级），不是 instance 创建；放心 |
| `Progress` 三 Image 中只有 `_fill` 写了 tint setter 后 `_bg` / `_frame` 后激活 → 后激活那两个仍是 default material | `Progress` 持 `_pendingTint`（§4.2），`_bg` / `_frame` 激活点（`Bg=` / `BgColor=` / `Frame=` / `FrameColor=` setter 第一次新建 Image 那行）补一句 `ImageTint.Apply(_bg, _pendingTint)`。Test 7 + 一个 "tint 早于 bg 激活" 用例覆盖 |

---

## 9. 与现有系统的交互

- **Color tokens (PR #31)**：`color="<Theme>/<Accent>"` 跟 `tint="linear"` 正交。token 解析改 `Image.color`，tint 切 `image.material`，互不干扰。
- **Variants**：`tint` 是普通 `[UIAttr]`，自动支持 `attr.var` 覆盖；`Screen.ReSolve` 路径无需改动。
- **Hot reload**：`tint` 值变化触发 attr 重设，`ImageTint.Apply` 内部 `??` cache 不重复 Resources.Load。
- **Addressables**：`UI-LinearLightTint.mat` 用 Resources 加载，不走 Addressables。即使项目用 Addressables loader，material 资源也是 Player ship 的 Resources，跟用户的 Addressables 配置无关。
