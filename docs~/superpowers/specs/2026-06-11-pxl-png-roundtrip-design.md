# `.pxl` ↔ PNG 双向往返（Inspector 导出 + Sync from PNG 回写）设计

**日期**：2026-06-11
**状态**：设计阶段（待 review，未进入实施）
**作用域**：给 `PxlImporter` 资产加自定义 Inspector（`ScriptedImporterEditor`）：只读信息面板 + **Export PNG**（工具 1）+ **Sync from PNG**（工具 2，就地回写 grid）。打通 "LLM 起稿 `.pxl` → 美术 Aseprite/PNG 精修 → 回写 `.pxl` → LLM 继续可读可改" 的协同回路。纯 `Editor/` 改动，运行时与 `.pxl` 格式语义零变化。
**依赖**：
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md)（总纲）
- [`2026-06-11-pxl-pixel-sprite-importer-design.md`](2026-06-11-pxl-pixel-sprite-importer-design.md)（`.pxl` 格式 / PxlImporter / `.gpl` 调色板；本文沿用其全部术语与约束）

---

## 1. 背景与目标

### 1.1 问题

`.pxl` 让 LLM 能以文本生成像素 UI 小件，但与美术工具链是断开的：

- 美术无法用 Aseprite 直接编辑 `.pxl`（它不是图像文件）；
- 选中 `.pxl` 时 Inspector 显示的是生成 Texture2D 的灰色只读面板，看不出"该去改文本"，对使用者不友好；
- 美术改完 PNG 后没有回到 `.pxl` 的通道——要么 LLM 失去后续编辑能力（资产移交），要么人工重新誊写网格。

### 1.2 目标

选中 `.pxl` 资产的 Inspector 上提供：

1. **只读信息面板**：节列表（名字/尺寸/border）、引用的 `.gpl`、各节缩略预览、一行提示 "All settings live in the .pxl file"。
2. **Export PNG（工具 1）**：每节导出一张 PNG（命名约定 `<basename>.<section>.png`），美术拿去 Aseprite 精修（Aseprite 原生读 PNG、原生读写 `.gpl`，无需专门对接；`.ase` 批量转换走 Aseprite CLI，不在本设计范围内）。
3. **Sync from PNG（工具 2）**：把精修后的 PNG 像素**就地回写**进 `.pxl` 的 grid 块——`.pxl` 始终是结构与元数据的唯一事实来源，PNG 只携带像素。

### 1.3 非目标（范围外）

- PNG → 全新 `.pxl` 的"从零生成"转换器（sync 只更新已有节）。
- 节的增删/重命名经由 PNG 侧同步（仍是文本编辑操作）。
- `.ase` 二进制直接解析（一律经 PNG 中转）。
- 大图"假像素图"对齐修正（独立 milestone）。
- 自动监视 PNG 变化的 watch 式同步（手动按钮足够）。

## 2. Inspector（`PxlImporterEditor : ScriptedImporterEditor`）

- `[CustomEditor(typeof(PxlImporter))]`。importer 本身无可序列化设置 → 不画设置项，`ApplyRevertGUI` 按 ScriptedImporterEditor 契约保留。
- **信息区**（只读）：
  - 引用调色板：`@<name>` + 解析到的 `.gpl` 资产路径（可点击 ping）；纯内联模式显示 "inline palette"。
  - 节表：每节一行 = 名字（隐式单节显示文件名）、`W×H`、border（无则 "—"）、缩略预览（`AssetPreview` 或直接画 Texture2D，Point 采样）。
  - 提示行："All settings (ppu / border / palette / pixels) live in the .pxl text file."
- **按钮区**：`Export PNG...`、`Sync from PNG...`（见 §3 / §4）。资产处于导入失败态（无 Texture2D main asset）时两钮禁用，信息区显示导入错误提示。
- 数据来源：直接 `File.ReadAllText` + `PxlParser.Parse`（不依赖导入产物，失败态也能展示部分信息时再说——首版失败态只显示错误提示，YAGNI）。

## 3. 工具 1：Export PNG

- **命名约定（往返配对的契约）**：显式节 → `<basename>.<section>.png`；隐式单节 → `<basename>.png`。
- **目标目录**：`EditorUtility.SaveFolderPanel`，默认值 = 上次使用目录（`EditorPrefs`，key 按 asset GUID；纯便利项，不进 `.pxl` 文件，不进版本库）。
- **sourceFolder 防呆**：选中目录位于**任一** SpriteSet sourceFolder 之下时弹确认警告（导出的 PNG 会被同步工具当作新 sprite 来源，产生重复 key/重复打包），用户可坚持继续。
- **写出**：importer 产物 Texture2D 本就 readable → 逐节 `EncodeToPNG()` 写文件；已存在同名文件直接覆盖（导出即镜像）。多节文件一键导出全部节；不提供单节勾选（YAGNI，要单节美术删多余文件即可）。
- 导出完成后 `EditorUtility.RevealInFinder` 目标目录。

## 4. 工具 2：Sync from PNG（就地回写）

### 4.1 入口与配对

- `EditorUtility.OpenFolderPanel`（默认同上次导出目录）。按 §3 命名约定在该目录配对：每个已有节找 `<basename>.<section>.png`（隐式单节找 `<basename>.png`）。
- 配对结果三类：**匹配**（将更新）、**缺失**（该节跳过，列入摘要）、**多余 PNG**（前缀匹配 `<basename>.` 但节不存在 → warning 列出，不创建节）。
- 执行前弹**摘要确认框**：每节"更新 W×H→W'×H'"、新增颜色字符列表、跳过项；Cancel 不落盘。

### 4.2 像素 → 字符（颜色映射）

1. 全透明像素（alpha == 0）→ `.`。
2. 其余像素按 RGBA 精确匹配现有 `chars:` 条目（经 `PxlColorResolver` 同一套解析得到的 char→Color32 反查表）；**多个 char 解析为同一颜色时取文件中先声明者**（稳定、可预期）。
3. 未匹配的新颜色：
   - **palette 模式**：RGB 必须命中 `.gpl`（alpha 自由，沿用 import 的越板规则）——否则**报错中止**（不落盘），错误列出色值 + 首次出现的 `节名(x,y)` 坐标，提示"加进 .gpl 或回 Aseprite 改掉"。命中者新增 chars 条目，值写色名（命中的条目有名）或 `#RRGGBB[AA]`（无名条目 / 带 alpha）。
   - **内联模式**：直接新增 `#RRGGBB[AA]` 条目。
   - 新字符从固定字母表顺序取未占用者：`A-Z` → `a-z` → `0-9` → 其余可打印 ASCII，**排除** `.` `#` `[` `]` 空白及已占用字符。字母表耗尽（颜色数 > ~80）→ 报错"not limited-palette pixel art; quantize first"（天然挡住误拿大图来 sync）。
4. 新增的 chars 条目**追加**到现有 `chars:` 块末尾（保持已有条目顺序与注释不动）。

### 4.3 文本手术（不重新生成）

- `PxlParser` 内部扩展：`PxlSection` 记录 grid 块源码行区间（首行/末行，1-based），`PxlDocument` 记录 `chars:` 块末行。**仅 internal 字段，格式语义零变化。**
- 回写 = 按行区间替换各匹配节的 grid 行（grid 行统一写两空格缩进——解析端本就 trim，缩进只是排版；与既有示例一致）+ 在 chars 块末尾插入新条目行。header、注释、节顺序、未匹配节全部逐字节保留。
- **尺寸允许变化**（grid 行数/宽度随 PNG），但变化后违反 border 约束（L+R > 新宽 或 B+T > 新高）→ **报错中止**，提示先改该节 `border:` 行——不静默改元数据。
- 落盘后 `AssetDatabase.ImportAsset(pxlPath, ForceUpdate)` → PxlImporter 全套校验兜底（理论上 sync 产物必合法；若 import 报错说明 sync 有 bug，错误自然可见）。

### 4.4 不变量（测试钉死）

- **往返幂等**：Export → 不改 PNG → Sync → `.pxl` 文本逐字节不变。
- **像素保真**：Sync 后重导入的各节 Texture2D 像素 == 源 PNG 像素。
- 字符分配确定性：同一输入多次 sync 产生相同新字符分配。

## 5. 代码组织

| 文件 | 职责 |
|---|---|
| `Editor/Pxl/PxlImporterEditor.cs` | Inspector UI：信息面板 + 两按钮 + 各类面板/确认框（薄壳，无业务逻辑） |
| `Editor/Pxl/PxlPngExporter.cs` | 工具 1 核心：节→PNG 字节/文件名（纯逻辑可直测，文件落盘分离） |
| `Editor/Pxl/PxlPngSync.cs` | 工具 2 核心：配对计划（`SyncPlan`：匹配/缺失/多余/新颜色/错误）+ 文本手术；不弹 UI，输入输出皆数据 |
| `Editor/Pxl/PxlParser.cs`（改） | `PxlSection` 加 grid 行区间、`PxlDocument` 加 chars 块末行（internal） |

EditMode/EditorOnly 测试直测 `PxlPngExporter` / `PxlPngSync`（核心路径不依赖面板 UI）；Inspector 仅含取景与按钮分发，不强求 UI 自动化测试。

## 6. 测试

- **Exporter**：命名约定（显式/隐式节）、PNG 字节解码回来像素一致、sourceFolder 检测谓词。
- **Sync 配对**：匹配/缺失/多余三类齐全的目录；隐式单节配对。
- **颜色映射**：复用现有 chars（含 RGBA 变体与同色多 char 取先声明者）、palette 模式越板报错（带坐标）、内联模式新增 hex、色名 vs hex 写法选择、字母表分配顺序与耗尽报错、全透明→`.`。
- **文本手术**：注释/header/未匹配节逐字节保留、chars 追加位置、grid 缩进、尺寸变化、border 越界报错、CRLF 文件（解析端已容忍，回写统一 `\n`——与既有文件写出行为一致即可，测试钉住选择）。
- **不变量**：§4.4 三条。
- **Inspector**：不做 UI 自动化；导入失败态按钮禁用逻辑若可剥离为谓词则直测。

## 7. 文档

- `authoring-promptugui-pxl/SKILL.md` 加一小节："Round-trip with art tools"——导出/回写按钮、命名约定、`.pxl` 是元数据唯一事实来源、节增删仍走文本。（authoring 语义无变化，主 catalog 不动。）

## 8. 范围外

见 §1.3；另：`Sync from PNG` 的撤销依赖 git（文本文件改动，diff 可读），不做编辑器内 Undo。
