# `.pxl` 像素网格文本资产（LLM 生成 UI 小件）设计

**日期**：2026-06-11
**状态**：设计阶段（待 review，未进入实施）
**作用域**：新增 `.pxl` 文本格式 + Editor `ScriptedImporter`，让 LLM 以"调色板 + 字符网格"文本直接产出 Unity Sprite 资产（含 9-slice border / PPU），落进现有 SpriteSet → Sync Atlases 管线；调色板用社区标准 GIMP `.gpl` 做项目级共享。覆盖 UI chrome 小件（9-slice 边框、按钮皮肤、图标、小装饰，经验上限 ≤48×48）。
**依赖**：
- [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §7.6（`<Icon>` / SpriteSet）
- `Editor/SpriteAtlasSyncer.cs`（发现/打包/key 派生）、`Runtime/Application/SpriteSet.cs`（entries 消费端）

---

## 1. 背景与目标

### 1.1 问题

PromptUGUI 已让 LLM 通过 `.ui.xml` 生成界面结构，但界面的**图像资产**（9-slice 边框、按钮皮肤、图标）仍需用户手工绘制——这是"LLM 生成完整 UI"链路上最后一段断点。

### 1.2 为什么不是 SVG → Sprite

- **表示错配**：SVG 是连续坐标 + 抗锯齿光栅化；像素艺术是离散网格 + 限色调色板 + 逐像素控制。SVG 光栅化再降采样/量化得到的是"缩小的平面矢量图"，做不出 1px 勾线、selective outline、手工 AA。
- **精度错配**：LLM 写 SVG 控制得了拓扑，控制不了最终光栅化到 24×24 时每个像素的明暗——像素画的质量恰恰全在这一层。
- **工具链负担**：`com.unity.vectorgraphics` 维护停滞，否则需引外部 rasterizer。

### 1.3 为什么是网格文本

像素画天生是小型离散数据：24×24 的 9-slice 边框只有 576 格、<10 色——正是 LLM 文本输出能精确控制的形式，且**文本即所见**，LLM 能看着自己的输出自我修订。网格语法沿用 XPM 惯用法（单字符=色板项、`.`=透明、一行字符串=一行像素）——LLM 训练数据里大量存在；XPM 本身因 C 语法噪音且缺 9-slice/多 sprite/PPU 元数据而不直接采用。调研确认（2026-06）不存在满足此场景的社区标准格式；调色板层面则有标准：GIMP Palette（`.gpl`），Aseprite 原生读写、Lospec 全站提供下载，直接采用。

### 1.4 目标

LLM 写下一个 `.pxl` 文件放进 SpriteSet sourceFolder → Unity 自动导入为 Sprite（point filter、带 border）→ 现有 Sync Atlases / `<Icon>` / `<Image sprite=>` 零改动消费。错误（语法、行宽、越板色）在 import 时以行号报出，LLM 据此修订。

---

## 2. `.pxl` 文件格式

```
# 注释仅支持整行（trim 后以 # 开头）；行尾注释不可用
# palette: 可选；引用项目级 main.gpl。省略 = 纯内联模式
palette: @main
# ppu: 可选；缺省 100（Unity TextureImporter 惯例值）
ppu: 16
chars:
  .: transparent
  K: dark-blue
  W: #f4f4f4

[normal]
# border: 可选，9-slice（L,B,R,T 同 Unity Sprite border 序）；每节独立；须在 grid 前
border: 4,4,4,4
grid:
  ..KKKKKK..
  .KWWWWWWK.
  .KW....WK.
  ..KKKKKK..

[pressed]
border: 4,4,4,4
grid:
  ..KKKKKK..
  .KGGGGGGK.
  ...
```

规则：

- **网格**：单字符 = `chars` 中一项；`.` 固定为透明（保留字符）；每行宽度必须一致（不一致 = import error 报行号）；各节尺寸可不同。grid 行首尾空白一律 trim（缩进随意，空格不是合法网格字符）；空行结束 grid 块。
- **分节**：`[节名]` 起一节，节名限 `[A-Za-z0-9_-]+`。**单 sprite 文件可整体省略 `[节名]`**（隐式单节）。
- **key 派生**（icon 名 / atlas key）：
  - 单节文件：`相对 sourceFolder 路径去扩展名`——与现有 PNG 规则一致（`Buttons/ok.pxl` → `Buttons/ok`，不冲突时另补裸名别名 `ok`）。
  - 多节文件：`路径/节名`（`Buttons/ok.pxl` 的 `[pressed]` → `Buttons/ok/pressed`，裸名别名 `pressed` 按既有唯一性规则）。
- **颜色**：`chars` 值三种写法——`transparent`、`.gpl` 颜色名（仅 `@palette` 模式）、`#RRGGBB` / `#RRGGBBAA`。`@palette` 模式下 hex 写法须精确命中色板某项（忽略 alpha 通道比较 RGB），否则 import error（"越板色"），把全项目风格一致性从 LLM 自觉变成管线强制。
- **多节共享**：`palette` / `ppu` / `chars` 是文件级头部，所有节共享——按钮三态放同一文件、共享调色板，LLM 一屏看全整套皮肤，是风格一致性的主要收益点。

## 3. 调色板：`.gpl`（GIMP Palette）

- 标准文本格式：`GIMP Palette` 头 + 每行 `R G B<TAB>名字`。美术可从 Lospec 直接下载落盘，Aseprite 同步编辑。
- `palette: @main` 按**文件名去扩展名**全项目 `FindAssets` 查找 `main.gpl`；找不到或重名 = import error。
- `PxlImporter` 通过 `ctx.DependsOnSourceAsset(gplPath)` 注册依赖：**改色板自动重导入所有引用它的 `.pxl`**，全项目换色一次完成。
- `.gpl` 本身不需要成为自定义资产类型——importer 直接读文件文本；无 `.gpl` ScriptedImporter（Unity 默认把它当 DefaultAsset，无碍）。
- 色名解析：忽略大小写与空格/连字符差异（`Dark Blue` ≡ `dark-blue`）；无名条目只能被 hex 命中。

## 4. PxlImporter（`Editor/` asmdef，ScriptedImporter）

- `[ScriptedImporter(1, "pxl")]`。解析 → 每节构建 `Texture2D`（`FilterMode.Point`、RGBA32、不压缩、`alphaIsTransparency`）→ `Sprite.Create(tex, rect, pivot:center, ppu, extrude:0, FullRect, border)` 挂为 sub-asset。
- **资产结构**：每节一张独立 Texture2D（节间尺寸可异，不拼 sheet）。单节文件 main asset = 该 Texture2D；多节文件 main asset = 首节 Texture2D，其余节的 Texture2D + Sprite 全部 `ctx.AddObjectToAsset`。这样 `FindAssets("t:Texture2D", folders)` 必然可发现（main asset 类型）。
- **解析器**与 importer 同置 `Editor/`（纯 C# 内部类，便于 EditorOnly 单测直测）；错误用 `ctx.LogImportError` 携带行号 + 期望/实际描述，LLM 读 Console/lint 输出可直接定位修订。
- 导入产物随 Library 进 Player 构建（同 Aseprite importer 模式），**运行时零新增代码**。

## 5. 现有管线对接（改动面）

| 处 | 改动 |
|---|---|
| `SpriteAtlasSyncer` key 派生 | "去 `.png`" 泛化为"去扩展名"；`.pxl` 多节文件按 sub-asset Sprite 枚举，key 加 `/节名` 段 |
| `SpriteAtlasSyncer` 收集 | 既有 `FindAssets("t:Texture2D", folders)` 天然命中 `.pxl` main asset；多节需补 `LoadAllAssetsAtPath` 取全部 Sprite sub-assets |
| `ResetTextureImportSettings` 等 TextureImporter-only 操作 | 已有 importer 类型 guard（MF-D1b 同款，`is not TextureImporter` 跳过）——预计零改动，补测试钉死 |
| `SpriteSet.entries` / `<Icon>` / `<Image>` / 9-slice 消费端 | 零改动（只认 Sprite 引用） |
| `SpriteAtlasAutoSync` | `.pxl` 重导入产生的 Sprite 变化应触发既有 auto-sync 路径；验证 + 必要时把 `.pxl` 扩展名加进监听 |

## 6. 测试

- **解析器**（EditorOnly，直测内部类）：格式全集（单/多节、palette 引用/内联/混合、border、ppu 缺省）+ 错误路径（行宽不齐、未知字符、越板 hex、重复节名、palette 缺失/重名、保留字符 `.` 被重定义）。
- **Importer 集成**（EditorOnly，temp 文件夹 + AssetDatabase）：资产结构（main/sub）、Sprite border/PPU 正确、`.gpl` 修改触发依赖重导入。
- **Syncer**（EditorOnly）：`.pxl` 单/多节 key 派生、与 PNG 混放同一 sourceFolder、`ResetTextureImportSettings` 跳过 `.pxl`。

## 7. 文档（SKILL）

- 新增 LLM authoring skill：`.claude/skills/authoring-promptugui-pxl/SKILL.md`——格式语法 + 像素画作画守则（1px 勾线、限色、9-slice 角区设计、按钮三态明度阶梯等）+ `.gpl` 调色板约定。
- `authoring-promptugui-xml/reference/icons.md` 加指针（`.pxl` 也是 SpriteSet 的合法来源）。

## 8. 范围外（本次不做）

- **大图对齐修正工具**（生图模型输出的"假像素图"网格检测 + 主导色重采样 + 调色板量化）——独立 milestone，与本设计仅共享"落盘进 sourceFolder"约定。
- PNG 导出逃生口（右键 Export as PNG，供美术接手精修）。
- 动画 / 多帧 / sprite sheet。
- SVG 路线（已否决，见 §1.2）。
- 运行时动态解析 `.pxl`（importer 是 Editor-only，运行时只见成品 Sprite）。
