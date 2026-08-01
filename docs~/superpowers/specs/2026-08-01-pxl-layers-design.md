# `.pxl` 图层（pxl-layers）设计

**日期**：2026-08-01
**状态**：设计阶段（待 review，未进入实施）
**作用域**：`.pxl` 格式新增 `layer:` 块，让作者（尤其是 LLM）分层叠加地画像素，而不是一次性写出合成结果。合并在解析期完成，**不落盘**；导入 / 打包 / 运行时消费全部零改动。
**依赖**：
- [`2026-06-11-pxl-pixel-sprite-importer-design.md`](2026-06-11-pxl-pixel-sprite-importer-design.md)（格式与 importer 基线）
- [`2026-06-11-pxl-png-roundtrip-design.md`](2026-06-11-pxl-png-roundtrip-design.md)（Export PNG / Sync from PNG）
- [`2026-06-12-pxl-tiled-hint-design.md`](2026-06-12-pxl-tiled-hint-design.md)（`tiled:` 与 per-section 指令的既有惯例）

---

## 1. 目标与动机

现在的 `.pxl` 只有一层：作者必须在脑子里把轮廓、底色、高光、阴影合成好，再一次性写出最终字符网格。代价是**每次局部修改都要在合成结果上做**——想调暗一点阴影，得逐个找出哪些格子属于阴影；想把轮廓挪一格，得同时改掉被轮廓压住的底色。对逐字符输出的 LLM，这是最容易出错的一步。

图层把这件事拆开：一层画轮廓，一层铺底色，一层点高光。改哪层只碰哪层的字符，**互不干扰**。这也让"同一底图 + 不同高光 = 多状态"这种画法在单个 section 内成立。

### 1.1 设计约束（决定了下面所有取舍）

1. **不能破坏 `palette:` 的调色板强制**。`PxlColorResolver.cs:26` 要求 palette 模式下每个 hex 命中 `.gpl`，这是 `.pxl` 最核心的项目级约束。
2. **不能引入双事实来源**。文件里不能同时存在"层"和"由层生成的合成结果"两份可编辑数据。
3. **不能扩大下游改动面**。importer / atlas syncer / 运行时 / PNG 导出都不该感知图层的存在。

## 2. 三个关键决策

### 2.1 合成语义 = 纯覆盖，不做 alpha 混合

上层非 `.` 字符直接覆盖下层字符；`.` 表示**穿透**（不绘制，显示下层），而非"最终透明"。

否决 alpha 混合的理由：`#1a1c2c` 上叠 `#ffffff80` 合出 `#8d8e96`，**几乎必然不在调色板上**——约束 1 直接失效。连带后果是合并结果的色数爆炸，`PxlPngSync.cs:141` 那条 "ran out of palette characters"（字母表仅 62 个可读字符）会真的触发。而且 SKILL.md:117 本来就写着像素画不该用半透明伪造 AA。

纯覆盖换来三个性质，后面的简化全部建立在它们之上：

- 合并结果的色集 = 各层色集的**并集**，不产生任何新颜色 → 约束 1 自动成立
- 合并可以在**字符级**完成，完全不需要颜色信息
- 合并结果的 `chars` 就是原来的 `chars`，字符不用重新分配 → diff 稳定

**推论：不需要 `layer_chars` 这个概念。** 文档级的 `chars:` 单表既是层的字符表，也是合并结果的字符表。少一个块，改一层也不会扰动 `chars:` 块。

### 2.2 合并结果不落盘

导入器直接消费合并结果，文件里只有层。理由：

- `ScriptedImporter.OnImportAsset` 里写回 `ctx.assetPath` 是 Unity 明确不支持的（导入期改源文件 → .meta 时间戳错乱 + 重入）
- 改用 `AssetPostprocessor` 可行但要幂等短路，且 **Unity 只在获得焦点时刷新**——LLM 通过 CLI 写完文件时 Unity 大概率在后台，回写不会立刻发生，下一次读回去是过期内容
- 落盘 = 违反约束 2

代价是作者读文件时看不到合成后的样子。补偿全部放进 `PxlPreview` CLI（§6）：`--layers` 逐层出图，`--emit-flat` 把合成网格打到 stdout。作者的迭代循环本来就要求渲染并**看图**，这条路不额外增加负担。

### 2.3 沿用 `.pxl` 扩展名，不新增 `.lpxl`

- **sprite key 会撞车**：key 由 `Path.GetFileNameWithoutExtension` 派生（`PxlImporter.cs:67`），`ok.pxl` 与 `ok.lpxl` 产生同一个 `Buttons/ok`
- **原地演进**：把现有扁平文件改造成图层版是纯文本追加，文件名 / key / 所有 `sprite="ui:Buttons/ok"` 引用全不动；双扩展名要改名，过渡期两文件并存还会撞 key
- **图层是可选特性不是新格式**：`border:` / `tiled:` / `palette:` 都是可选的，没人因为用了 9-slice 就发明 `.9pxl`
- 单扩展名还允许**同文件混用**：复杂的 `[normal]` 用图层画，简单的 `[disabled]` 直接扁平写

## 3. 格式

### 3.1 语法

```
palette: @ui
chars:
  K: night
  M: steel
  H: cloud

[normal]
border: 3,3,3,3
grid:              ← 匿名底层，语法一个字不用改
  KKKKKK
  KMMMMK
  KMMMMK
  KKKKKK
layer: highlight   ← 直接往上追加
  ......
  .HHH..
  ......
  ......
```

合成结果（解析期算出，不落盘）：

```
  KKKKKK
  KHHHMK    ← H 覆盖 M
  KMMMMK
  KKKKKK
```

### 3.2 规则

- **`grid:` 就是匿名底层。** 从扁平演进到图层是纯追加，`grid:` 不用改名。
- **`grid:` 若存在，必须是本 section 的第一个像素块。** `layer:` 出现在 `grid:` 之前是错误——否则"grid 是底层"的心智模型会崩。
- **section 可以只有 `layer:` 没有 `grid:`**（纯图层文件），至少要有一个像素块。
- **叠放顺序 = 声明顺序，先声明在下。**
- **层名必填**，charset 同 section（`[A-Za-z0-9_-]+`）；同 section 内不得重名，跨 section 可以重名（`[normal]` 和 `[pressed]` 各有一个 `base` 很自然）。匿名底层（来自 `grid:`）在诊断信息里显示为 `(grid)`。
- **所有层必须同宽同高**。宽高由第一个像素块的第一行确定；同层内行宽不齐沿用既有错误，跨层高度不齐是新错误。
- **`layer:` 头的识别优先级与 `[section]` 头相同**——在 `inGrid` 状态下也能打断当前块。所以层与层之间既可以紧挨着写，也可以隔空行（空行仍然结束当前块，行为不变）。
- **`border:` / `tiled:` 必须在第一个像素块之前**（既有约束从"grid 之前"泛化为"任何像素块之前"）。
- **`.` = 穿透，不是擦除。**

### 3.3 emergent：`transparent` 字符 = 橡皮擦

`chars:` 一直支持 `X: transparent`（`PxlColorResolver.cs:22`）。在纯覆盖的字符级合并下，`X` 是非 `.` 字符，因此**会覆盖下层**，结果是透明——天然就是"在上层挖洞"的橡皮擦，不需要任何新语法。

单层文件里 `X: transparent` 与 `.` 视觉等价，行为与现状一致。

### 3.4 `:` 收编为保留字符

`layer:` 头与 grid 行存在理论歧义：一行恰好拼出 `layer: base` 会被误判成层头。`l/a/y/e/r` 都是合法 chars key，但 `:` 一旦封死就完全拼不出来。两处同时改：

- `PxlChars.Alphabet`（`PxlChars.cs:13`）删掉 `:`，与 `.` / `#` / `[` / `]` 并列为保留字符
- `PxlParser` 的 chars 条目处理显式拒绝 `:` 作为 key：
  `':' cannot be a chars key (reserved: a grid row could then parse as a 'layer:' header)`

现有 `.pxl` 只用字母，`:` 在字母表里排在第 63 位之后，改动零风险。

## 4. IR 与解析器

### 4.1 新增 `PxlLayer`，`PxlSection.Rows` 降级为合成缓存

```csharp
internal sealed class PxlLayer
{
    public string Name;                        // null = 来自 grid:（匿名底层）
    public readonly List<string> Rows = new(); // top-down，已 trim
    public int StartLine, EndLine;             // 源文本 1-based 行区间
}

internal sealed class PxlSection
{
    public string Name;
    public Vector4 Border;
    public bool Tiled;
    public int Width, Height;
    public readonly List<PxlLayer> Layers = new(); // 底 → 顶，至少一个
    public readonly List<string> Rows = new();     // 合成结果，FinishSection 时算出
    public int GridStartLine, GridEndLine;         // = Layers[0] 的行区间（sync 用）
}
```

**`Rows` 保持为普通 `List<string>`，由 `FinishSection` 填成合成结果。** 这是整个设计改动面最小的支点：

| 消费点 | 改动 |
|---|---|
| `PxlImporter.BuildTexture`（`PxlImporter.cs:109`） | 零 |
| `PxlPngExporter.EncodeSection` | 零（走 BuildTexture） |
| `PxlPngSync` 的 `colorToChar` 反查 | 零 |
| `.lint/PxlPreview/Renderer.cs:96` | 零（默认仍渲合成图） |
| 现有 `PxlParserTests` / `PxlFromPngTests` 断言 | 零（扁平文件 `Layers.Count == 1`，`Rows` 逐字节不变） |

扁平文件在新模型里就是 `Layers.Count == 1`，**不存在"有没有图层"的分支判断**——只有一条合并路径，N=1 是恒等。

### 4.2 `PxlFlattener`（新文件，`Editor/Pxl/PxlFlattener.cs`）

```csharp
internal static class PxlFlattener
{
    // 底 → 顶逐层覆盖；上层 '.' 保留下层字符。
    public static List<string> Flatten(IReadOnlyList<PxlLayer> layers, int width, int height);
}
```

纯字符串操作，**不引用任何 Unity 类型**。加入 `.lint/PxlPreview` 的共享编译列表（与 `PxlParser` / `GplPalette` / `PxlColorResolver` 同批），CLI 与 Unity 共用同一份合并逻辑。注意 CLAUDE.md 那条约束：这批共享文件只能依赖 `Color32` / `Vector4`——字符级合并连这两个都不需要，天然满足。

### 4.3 解析器状态机改动（`PxlParser.Parse`）

匹配优先级，从高到低：

1. `[section]` 头（现状，`PxlParser.cs:73`）
2. **`layer:` 头（新增）**
3. **`grid:` 头（从原先的末位提到这里）**
4. `inGrid` → `ValidateRow`
5. `inChars` → chars 条目
6. `palette:` / `ppu:` / `chars:` / `border:` / `tiled:`

层名正则 `^[A-Za-z0-9_-]+$`。

**两个块头都必须越过 `inGrid`**：否则紧跟在上一块像素行之后的 `layer:` / `grid:` 会被当成（宽度不齐的）像素行吞掉。这条歧义的另一半由 §3.4 封死——`:` 保留后，合法像素行拼不出任何以 `xxx:` 收尾的块头。其余指令（`border:` 等）不提优先级：它们前面本来就要求空行，既有行为不变。

`ValidateRow` 把行追加进 **当前层**而非 section，`section.Width` 仍由第一行确定（保持既有语义，跨层同样成立）。

`grid:` 与 `layer:` 各自 `EnsureSection` 后创建新层；`border:` / `tiled:` 的前置检查从 `section.Rows.Count > 0` 改为 `section.Layers.Count > 0`。

`FinishSection` 依次做：层高一致性校验 → `Flatten` 填 `Rows` → 既有 border 越界校验（此时 `Width`/`Height` 已定）。

### 4.4 错误清单（全部带行号）

| 错误 | 触发 |
|---|---|
| `grid: must come before any layer: block (grid is the bottom layer)` | `grid:` 出现在同 section 的 `layer:` 之后（解析器顺序扫描，检测点必然落在 `grid:` 行上） |
| `duplicate layer name 'X' in section '[s]'` | 同 section 层重名 |
| `layer 'X' must be named ([A-Za-z0-9_-]+)` | `layer:` 后缺名或名非法 |
| `layer 'X' has N rows but layer 'Y' has M (all layers in a section must be the same size)` | 跨层高度不一致 |
| `row width N != first row width M` | 行宽不齐（既有消息，跨层同样适用） |
| `':' cannot be a chars key (reserved: ...)` | `:` 作 chars key（§3.4） |
| `border must come before grid:/layer:` | 既有消息泛化 |
| `section '[s]' has no grid: or layer: block` | 既有 "has no grid" 消息泛化 |

## 5. PNG 往返：图层 section 拒绝 Sync

**从合成后的 PNG 反推不出图层**——这是信息丢失，不是实现难度问题。所以：

- **Export PNG：零改动。** 导出的是合成图（走 `Rows`），文件名契约不变。
- **Sync from PNG：`Layers.Count > 1` 的 section 跳过。** `SyncPlan` 新增 `List<string> LayeredSections`，与 `MissingSections` 并列。

选择"跳过"而非"整体报错"，是因为一个文件里可以混着扁平 section 和图层 section——扁平的那些应该照常 sync。`PxlPngSync.BuildPlan` 在按 section 循环时，判定优先于 PNG 匹配（图层 section 即使有对应 PNG 也不参与）。

`PxlImporterEditor.SyncFromPng` 的 UI 配套：

- summary 每条图层 section 输出 `skipped (has layers): [normal]`
- **补一个分支**：`Updates.Count == 0 && LayeredSections.Count > 0` 时，弹的对话框不能再是误导性的 "No matching PNGs found"，改为说明这些 section 由图层驱动、PNG 无法反推
- 只读信息面板每个 section 的尺寸/border 行后追加层数（如 `3 layers`），与既有 `tiled` 字样同一位置

`PxlFromPng` / `CreatePxlFromPngMenu` 生成的永远是单层文件，不受影响。

## 6. PxlPreview CLI

默认行为完全不变（渲合成图）。新增两项，它们是"不落盘"决策的补偿，**不是可选的锦上添花**——没有它们，作者看不到自己刚画的那一层，图层带来的正交性收益就拿不到。

| 选项 | 行为 |
|---|---|
| `--layers` | 每个 section 一行，行内横向排列 `[层1][层2]…[合成]`；层标签 `section/layer`，合成结果沿用原标签加 ` flat`。（当前布局是所有 section 横向并排——`--layers` 下改为每 section 一行，否则宽度失控。单层节退化为一格，不重复画） |
| `--emit-flat` | 把各 section 的合成网格按可直接粘回 `.pxl` 的形式打到 stdout（含 `[section]` 头），**不写文件**。补上文本级逐字符自检那条路 |

其余配套：

- **橡皮擦标记**：§3.3 的透明字符在自己的层里 alpha=0，若照常"不画"，橡皮擦层看起来和空层完全一样——恰好击穿 `--layers` 的用途。层单元格里把这类格子画成洋红块（沿用 guide 色约定）；合成图里不画（那里它就是普通透明）
- stdout 的 section 摘要行（`Program.cs:200`）追加层数，与既有 `border` / `tiled` 字样同格式
- **全透明层 warning**：某层所有格子都是 `.` → stderr 打 warning，**exit code 不变**（多半是忘了画，但也可能是有意占位）。只在 CLI 做，不进 Unity Console，避免污染导入日志
- `--help` 与 `.lint/PxlPreview/README.md` 同步

## 7. 版本与重导入

`[ScriptedImporter(1, "pxl")]` → `2`。旧文件的解析结果逐字节不变（`Layers.Count == 1`，`Rows` 相同），理论上无需重导入；bump 只是为了让 parser 改动确定性地触达所有已导入资产。

## 8. 测试计划

EditorOnly（`PromptUGUI.Tests.EditorOnly`，`Tests/EditMode/Editor/Pxl/`）：

**解析 + 合并**
- `grid:` + `layer:` → `Layers.Count == 2`，顺序为底→顶
- 纯 `layer:`（无 `grid:`）合法
- 上层非 `.` 覆盖下层；`.` 穿透
- `X: transparent` 在上层 → 擦除下层（合成结果该格为 `X`，解析出的颜色 a==0）
- 三层及以上的叠加顺序正确
- 层间隔空行仍能解析
- **回归**：现有扁平文件 `Layers.Count == 1` 且 `Rows` 与改动前逐字节相同（现有 `PxlParserTests` 全绿即达成）

**错误路径**（逐条断言行号 + 消息）
- `layer:` 在 `grid:` 之前
- 同 section 层重名 / 跨 section 同名合法
- 层名缺失或非法字符
- 跨层高度不一致
- 跨层行宽不一致
- `:` 作 chars key
- `border:` 在层之后
- section 无任何像素块

**下游**
- `PxlImporter`：图层文件产出的 Texture 像素 == 合成结果（含擦除格为透明）
- `PxlPngSync.BuildPlan`：图层 section 进 `LayeredSections` 且不进 `Updates`；同文件的扁平 section 正常产出 update
- `PxlPngExporter`：图层 section 导出的 PNG == 合成图

CLI（`.lint/PxlPreview`）不在 Unity 测试范围，手动验证：对一个三层文件跑 `--layers` / `--emit-flat`，确认逐层出图正确、`--emit-flat` 输出可粘回并得到同样的合成结果。

## 9. SKILL 同步

`authoring-promptugui-pxl/SKILL.md` 新增 "Layers" 一节（CLAUDE.md 要求同 PR 内完成，英文）：

- 语法与叠放顺序；`grid:` = 匿名底层，追加即可演进
- 纯覆盖语义：`.` = 穿透而非透明；`X: transparent` = 橡皮擦
- 合成结果**不落盘**——文件里读到的永远是层，要看结果就渲染
- `--layers` / `--emit-flat` 进"Self-verify before reporting done"清单：图层文件必须逐层看过
- Sync from PNG 对图层 section 不可用（Export 仍可用）
- **何时该分层**：轮廓 / 底色 / 高光阴影分开，或某一层要反复迭代。**何时不该**：≤16×16 的简单图标直接扁平写更省事，分层反而增加对照成本
- 错误表补 §4.4 的新条目

其余 SKILL 免更：`.ui.xml` 侧对 sprite 的引用方式零变化，公开 C# API 零变化。

## 10. 范围外

- **跨 section 复用层**（`use: normal.base`，多状态共享底图）。这是重复量最大的地方（`pugui.pxl` 现在每个状态整块重抄），但引入"层引用"这个新维度——命名空间、循环引用检测、尺寸校验时机全要重新设计。留给下一个 milestone。
- **层偏移**（`offset: x,y`，小层叠在大层上）。要一并处理越界裁剪，且与"层引用"是同一批需求，一起做更合算。
- **alpha 混合 / 混合模式**（§2.1 已否决）。语法上保留扩展位：将来若确有需要，可在层头加 `layer: shade blend=alpha`，届时需一并回答"混合结果如何回到调色板"（`PxlQuantizer` 最近色量化）与"Gamma 还是 Linear 空间"。
- **遮挡检测 warning**（某层的非 `.` 像素被上层全部盖住）。检测便宜但易误报（作者可能有意留备用层），先不做。
- **层的临时禁用**（`layer: X disabled`）。等实际迭代中确认需要再加。
