# Icon Assets 设计：`<Icon>` + IconSet + SpriteAtlas 后端

**日期**：2026-05-08
**状态**：设计阶段（待 review，未进入实施）
**作用域**：项目级图标系统的 XML 元素 / 运行时 Resolver / Editor 同步工具 / 与现有 hot-reload 集成的 C# API + XML 语义设计；不含实施代码或 task 拆分细节
**依赖**：基础设计 [`2026-05-07-promptugui-description-language-design.md`](2026-05-07-promptugui-description-language-design.md) §7（内置控件）/ §6（Layout/SizeSpec）/ §12（风险表）；M4 设计 [`2026-05-08-m4-import-autoimport-hotreload-xsd-design.md`](2026-05-08-m4-import-autoimport-hotreload-xsd-design.md) §6（Hot reload）/ §9（XSD）

---

## 1. 背景

像素游戏项目从 PC 横屏铺到移动竖屏的过程中，UI 上有大量小图标（设置齿轮、关闭叉、装备槽、状态、技能等）。Web 开发的常见模式是引入一个 FontAwesome 这样的"大集合"，写 XML 时按名字引用，构建产物里**只**包含真正用过的那些。

PromptUGUI 现状：

- `<Image src="..."/>` 走的是单图引用路径，每张 PNG 是独立资产；用于背景、立绘等"内容性"美术资产
- `Image.Sprite` 当前直接 `Resources.Load<Sprite>(value)`，与"运行时 content-agnostic"的库设计不一致（独立问题，不在本 spec 范围）
- 没有"项目级共享、按名引用、按需打包"的图标机制；作者只能手工管理一堆 PNG + 在 XML 里写完整 src

**本 spec 解决的问题**：

1. 作者侧：怎么"导入"一批 icon 让它们能在 XML 里按短名引用
2. 构建侧：怎么让 Unity 只把真正被 XML 引用过的 icon 打进包
3. 库侧：保留"运行时不读文件系统、用户可自定义后端"的核心契约

---

## 2. 决策一览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| ICN-D1 | XML 元素 | 新增 `<Icon>` 内置控件，不复用 `<Image>` | 语义清晰；XSD 可对 `<Icon>` 强约束 `name` pattern，对 `<Image>` 不强约束 |
| ICN-D2 | 引用 key 形态 | 单字符串 `name="ns:icon"`，冒号分隔 | 一处而非两处属性；与 FontAwesome / Tailwind 习惯一致；LLM 友好 |
| ICN-D3 | 多 set / 命名空间 | 每个 IconSet 一个独立 namespace；同 set 名禁止重复 | 便于第三方 pack 分发、独立 hot-reload、独立打包优化 |
| ICN-D4 | Resolver 注入 | `UI.IconResolver = Func<string, Sprite>`，与 `SourceResolver` 同构 | 用户可整体替换；库不绑定具体后端 |
| ICN-D5 | 默认后端 | SpriteAtlas（每 IconSet 一个） | 同步 API、UI 默认 draw-call 批处理、像素艺术友好；通过 Unity 自身资产引用追踪做"只打包用到的" |
| ICN-D6 | IconSet 资产形态 | 每个 IconSet 一个 ScriptableObject | Unity 惯用形式；便于第三方 pack 增删；独立 inspector |
| ICN-D7 | Icon 源材料 | 文件夹约定：sourceFolder 下 PNG，文件名（去扩展名）= icon 名 | 拖文件夹即"导入"；FA 体验 |
| ICN-D8 | 颜色染色 | `Image.color` multiply tint；默认 `#ffffff` | 单色 mask 与彩色 PNG 都支持；白色保留原色，非白做染色 |
| ICN-D9 | 默认尺寸 | `size="native"` 新枚举值 | 取 sprite 原生像素尺寸，符合像素艺术作者心智 |
| ICN-D10 | "只打包用到的" 实施路径 | Editor 同步工具扫 .ui.xml → 重建每 set 的 SpriteAtlas（仅含被引用 sprites） | 不需要自定义 build hook；靠 Unity 引用追踪自然成立 |
| ICN-D11 | 同步触发时机 | 手动 Tools 菜单 + IPreprocessBuildWithReport；AssetPostprocessor 默认关，EditorPrefs opt-in | 正确性靠构建前钩子；编辑期 ergonomics 留给作者选 |
| ICN-D12 | 动态 icon 名 | `name` 中 ns 必须字面量；name 部分可含 `{{...}}` 但作者必须在 IconSet.alwaysInclude 列出真实候选 | "只打包用到的"需要静态可分析；动态名给逃生口而非静默全打 |
| ICN-D13 | 运行时未知 icon | LogError + sprite=null，不抛异常 | UI 失败不该 crash 游戏；Editor + 测试可断言 |
| ICN-D14 | 同 setName 冲突 | InvalidOperationException（runtime resolver build 时） | fail loud；与 §4.2 风格一致 |
| ICN-D15 | atlas hot-reload | atlas reimport → 重建 IconResolver lookup → 触发所有 Screen RefreshIcons | sprite 引用 GUID 不变但内容变；ReSolve 复用或新增 hook |

---

## 3. 一个完整可读的例子

### 3.1 工程目录

```
Assets/
  Art/
    Icons/
      ui/                      <- IconSet "ui" 的 sourceFolder
        settings.png
        close.png
        gear.png
      art/                     <- IconSet "art" 的 sourceFolder
        gold-coin.png
        sword.png
    IconSets/
      ui.asset                 <- IconSet ScriptableObject
      art.asset
    Generated/
      ui.spriteatlas           <- 由 Editor 同步工具维护，列入 git
      art.spriteatlas
  UI/
    screens/
      MainMenu.ui.xml
```

### 3.2 启动期注册

```csharp
UI.UseResourcesResolver("UI");
BuiltinPrimitives.Register(UI.Registry);

// 默认 helper：枚举 Resources/IconSets 下所有 IconSet → 建 lookup
IconResolverHelpers.UseSpriteAtlasIconResolver("IconSets");
```

或显式传 SO 列表（不依赖 Resources 约定）：

```csharp
IconResolverHelpers.UseSpriteAtlasIconResolver(
    new[] { uiIconSet, artIconSet });
```

### 3.3 XML 引用

```xml
<!-- MainMenu.ui.xml -->
<PromptUGUI version="1">
  <Screen name="MainMenu">
    <HStack>
      <Icon name="ui:settings"/>                <!-- 默认 size=native, color=white -->
      <Icon name="ui:close" color="#ff4444"/>   <!-- 单色 mask 染红 -->
      <Icon name="art:gold-coin" size="48"/>    <!-- 彩色 PNG，强制 48px -->
      <Btn id="ok">
        <Icon name="ui:gear" color="#fff"/>
        <Text>设置</Text>
      </Btn>
    </HStack>
  </Screen>
</PromptUGUI>
```

### 3.4 作者工作流

1. 拖一张 `volume.png` 进 `Assets/Art/Icons/ui/`
2. 在 XML 里写 `<Icon name="ui:volume"/>`
3. **Tools → PromptUGUI → Sync Icon Atlases (All Sets)**
4. `ui.spriteatlas` 自动包含 `volume`；运行时立即可见
5. 切到构建 → IPreprocessBuildWithReport 自动再跑一次同步，保证发布产物正确

---

## 4. XML 语义（`<Icon>`）

### 4.1 属性

| 属性 | 必填 | 默认 | 类型 / 取值 | 备注 |
|---|---|---|---|---|
| `name` | 是 | — | `^[\w\-]+:[\w\-]+$` | `ns:icon-name` 形式，冒号必有 |
| `color` | 否 | `#ffffff` | HTML color | 透传 `UnityImage.color` |
| `size` | 否 | `native` | `<num>` / `stretch` / `native` | 见 §5 SizeSpec 扩展 |
| `width` / `height` | 否 | (跟 `size` 同语义) | 同上 | 与 anchor=stretch 互斥规则同 §6.2 |
| `anchor` | 否 | `center` | 同 Frame | |
| `margin` | 否 | `0,0,0,0` | 同 Frame | |
| `id` | 否 | — | 同其他控件 | |
| `if` | 否 | — | truthy 表达式 | |
| `attr.<var>.*` | 否 | — | Variant 覆盖 | 全部支持，包括 `attr.dark.name="ui:moon"` |

### 4.2 IR 与解析

- 不引入新 IR 节点类型；`<Icon>` 解析为通用 `ElementNode { Tag="Icon", Attrs={...} }`
- `UIDocumentParser` 不需要专门分支
- Variant / Template 实参 / `if` 自动支持，因为它们作用于 attr 字符串
- 经 `TemplateExpander.Expand` 后形态不变（Icon 不是 Template）

### 4.3 解析期错误（ParseException）

| 触发 | 信息 |
|---|---|
| 缺 `name` | `Icon: 'name' is required` |
| `name` 不匹配 `ns:icon` 模式 | `Icon: 'name' must be 'set:icon' (got '{value}')` |
| 在 stretched 轴上设了显式 size | 沿用基础 spec §6.2 现有规则 |

---

## 5. SizeSpec 扩展：`native` 值

基础 spec §6 SizeSpec 当前两种形态：数值（像素）和 `stretch`。本 spec 新增第三种：`native`。

### 5.1 语义

- `<Icon size="native">` → Layout 阶段读 `UnityImage.sprite.rect.size` 写入 `RectTransform.sizeDelta`
- 是 `<Icon>` 的默认值
- **本 spec 范围内仅 `<Icon>` 接受 `native`**；其他控件出现 `size="native"` / `width="native"` / `height="native"` → ParseException `"native size only allowed on <Icon>"`
- 与 `stretch` 互斥（同轴只能选一种）；与显式数值同样互斥

### 5.2 IR / Parser 影响

- `SizeSpec` 加 `Native` 枚举值
- `<Icon>` 在解析期把缺省 `size` 视作 `native`（其他控件不变）
- Layout 阶段 native size 解算需要 sprite 已经设好；执行顺序：`OnAttached` → set `Name`（触发 sprite 赋值）→ Layout

---

## 6. Resolver + 默认 SpriteAtlas 实现

### 6.1 委托

```csharp
namespace PromptUGUI.Application {
    public static partial class UI {
        public static Func<string, Sprite> IconResolver { get; set; }
    }
}
```

- 接收完整 key（带冒号）；resolver 自决定怎么解析
- null 表示未注册 → runtime LogError
- 非 null 但返回 null → runtime LogError，sprite=null

### 6.2 IconSet ScriptableObject

```csharp
namespace PromptUGUI.Application {
    [CreateAssetMenu(menuName = "PromptUGUI/Icon Set")]
    public sealed class IconSet : ScriptableObject {
        [SerializeField] string setName;             // ns，匹配 [\w\-]+
        [SerializeField] DefaultAsset sourceFolder;  // Editor-only
        [SerializeField] SpriteAtlas atlas;          // Editor 工具维护
        [SerializeField] List<string> alwaysInclude; // 动态名逃生口（§7.6）

        public string SetName => setName;
        public SpriteAtlas Atlas => atlas;
    }
}
```

- `sourceFolder` 是 `DefaultAsset`（Unity 文件夹的 SO 表示），运行时不读
- `atlas` 字段的存在让 Unity 把 atlas 当成 IconSet SO 的依赖；IconSet SO 被 Resources 或场景引用 → atlas 进 build → atlas 内的 sprites 进 build。**核心闭环**
- **首次同步时 `atlas == null`** → 同步工具自动在 `Path.GetDirectoryName(IconSet asset path)` 下创建 `<setName>.spriteatlas`，并通过 SerializedObject 写回 `atlas` 字段。这意味着：作者只需创建 IconSet SO + 拖一个 sourceFolder，第一次 Sync 之后 atlas 字段会被自动填上

### 6.3 默认 helper

```csharp
public static class IconResolverHelpers {
    public static void UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets");
    public static void UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets);
}
```

实现要点：

- 一次性 `BuildLookup(sets)` → `Dictionary<string, Sprite>`（key=`set:icon`），O(1) 查询
- 同 `setName` 冲突 → `InvalidOperationException`
- atlas 为空（无引用）的 set → 不报错，map 不填条目
- `Atlas.GetSprites()` 返回 sprite name 带 `(Clone)` 后缀，去掉

### 6.4 Icon Control

```csharp
namespace PromptUGUI.Controls {
    public sealed class Icon : Control {
        UnityImage _img;

        public override void OnAttached() {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
            _img.preserveAspect = true;
            _img.raycastTarget = false;
        }

        [UIAttr] public string Name { /* see §6.5 */ set; }
        [UIAttr] public string Color { set; }
    }
}
```

### 6.5 运行时错误策略

| 情形 | 行为 |
|---|---|
| `IconResolver == null` | LogError，sprite=null |
| Resolver 返回 null | LogError，sprite=null |
| Variant 切换到不存在的 name | 同上；不影响其他控件 ReSolve |

不抛异常的理由：UI 失败不该 crash 游戏；Editor + CI 通过 LogAssert 捕获。

---

## 7. Editor 同步工具

### 7.1 入口

```
PromptUGUI / Sync Icon Atlases (All Sets)
PromptUGUI / Sync Icon Atlases (Selected Set)   // 选中 IconSet.asset 时
PromptUGUI / Auto-sync on Save (toggle)         // EditorPrefs 开关
```

主体方法 `IconAtlasSyncer.SyncAll(IEnumerable<IconSet>)`。

### 7.2 同步算法

1. 扫所有 .ui.xml → 收集 `(setName, iconName)` 集合（合并 IconSet.alwaysInclude）
2. 对每个 IconSet：
   - `EnumeratePngs(sourceFolder)` → `Dictionary<name, Sprite>`
   - `needed = refs[setName]`
   - `sprites = needed ∩ available`
   - `missing = needed - available` → LogWarning
   - `UpdateAtlas(set.atlas, sprites)`：sprite-level packables，对比相等则 no-op
3. `AssetDatabase.SaveAssets()`

### 7.3 ScanXmlReferences

- `AssetDatabase.FindAssets("t:TextAsset")` 过滤 `.ui.xml` 后缀
- 每个文件 `UIDocumentParser.Parse`（**不**走 TemplateExpander）→ 递归找 Tag=="Icon" 节点
- 每个 `name` 属性按字面解析；`{{...}}` 在 ns 部分 → LogWarning + 跳过；在 name 部分 → LogWarning（提示 alwaysInclude）+ 跳过
- 解析失败的 .ui.xml → LogWarning + 跳过该文件继续

### 7.4 EnumeratePngs

- `Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories)`
- 文件名（不含扩展名）= icon 名
- 重名 → LogWarning + 先到先得
- 非 Sprite TextureType 的 PNG → 自动改 importer 设 Sprite + reimport（首次同步行为）

### 7.5 UpdateAtlas

- 若 IconSet `atlas` 字段为 null → 创建 `SpriteAtlas` 资产到 IconSet 同目录，回填字段（见 §6.2）
- `SpriteAtlas.GetPackables()` 取当前；与 desired sprite 数组对比
- 相等 → 直接返回（避免无变化打 dirty 进 git）
- 不等 → `Remove(current) + Add(desired) + EditorUtility.SetDirty + SpriteAtlasUtility.PackAtlases`
- packables 用 sprite-level，**不**用 folder-level（folder 会拉所有 PNG）

### 7.6 动态 icon 名逃生口

`IconSet.alwaysInclude: List<string>` 字段，作者手工列出"运行时计算名"的 icon。同步时并入 needed 集合。

**作者规约**（写进 SKILL.md）：

- 静态 icon → XML 字面量，自动追踪
- 变体决定的 icon → 同样字面量但通过 `attr.var.name="ui:foo"` 表达；扫描器看得到
- 真·运行时计算名 → 加到 `alwaysInclude`

### 7.7 触发时机

| 触发 | 默认 | 行为 |
|---|---|---|
| Tools 菜单（手动） | 启用 | 即时同步 |
| `IPreprocessBuildWithReport` | 启用 | 构建前必跑，保证发布正确 |
| AssetPostprocessor on .ui.xml save | **关闭**，EditorPrefs `PromptUGUI.IconAtlas.AutoSyncOnSave` 开 | 大项目慢；保存延迟会感受到；opt-in |

### 7.8 与 M4 hot-reload 的交互

- 改 .ui.xml → 现有 hot-reload 机制重建 Screen → `<Icon>` 重新查 IconResolver。零新增。
- 改 IconSet 引用的 atlas 内容（同步刚跑完）→ atlas reimport → IconResolver 缓存的 Sprite 引用 GUID 仍有效但内容变 → 需要：
  - `UI.HotReload.NotifyAssetChanged` 增加 IconSet / SpriteAtlas 路径分支
  - 触发 `UseSpriteAtlasIconResolver` 重建 lookup
  - 让所有 Screen 走一次 ReSolve（或新增 `Screen.RefreshIcons`，只重设 Icon sprite，不重做 attribute apply）
- 优先复用 ReSolve 路径，简单；性能不够再拆 RefreshIcons

### 7.9 实现位置

```
Editor/
  IconAtlasSyncer.cs        # 算法主体
  IconAtlasMenu.cs          # [MenuItem]
  IconAtlasBuildHook.cs     # IPreprocessBuildWithReport
  IconAtlasAutoSync.cs      # AssetPostprocessor，受 EditorPrefs 控制
  IconSetEditor.cs          # 自定义 inspector：PNG 数量 / atlas 状态 / Sync Now 按钮
```

---

## 8. 错误矩阵

| 阶段 | 触发 | 行为 |
|---|---|---|
| Parse | 缺 `name` | ParseException |
| Parse | `name` 不匹配 `ns:icon` | ParseException |
| Editor sync | XML 引用 → sourceFolder 无对应 PNG | LogWarning，atlas 跳过 |
| Editor sync | 两个 IconSet 同 `setName` | LogError，整体同步中止 |
| Editor sync | sourceFolder 内重名 PNG（不同子目录） | LogWarning，先到先得 |
| Editor sync | sourceFolder=null / 非文件夹 | LogError，跳过该 set |
| Editor sync | 解析某 .ui.xml 失败 | LogWarning，跳过该文件 |
| Resolver build | 同 setName 多个 SO | InvalidOperationException |
| Runtime | `IconResolver == null` | LogError，sprite=null |
| Runtime | resolver 返回 null | LogError，sprite=null |
| Runtime | Variant 切到不存在 name | 同上；不影响其他控件 |
| Hot-reload | atlas 内容变 | 重建 lookup → ReSolve |

---

## 9. 测试计划

### 9.1 EditMode（PromptUGUI.Tests.EditMode）

**`IconParserTests.cs`**

- 各种 `name` 形式：合法 / 缺 `:` / 空 ns / 空 name / 缺属性
- Template 内 + 实参替换
- Variant `attr.var.name`、`attr.var.color` 下 ReSolve
- `<Icon if="...">` truthy/falsy

**`IconResolverTests.cs`**

- BuildLookup 正确建表
- 同 setName 冲突抛异常
- 不同 set 同 icon 名不冲突
- atlas=null / 空 atlas 不报错
- `IconResolver == null` 路径 LogError + sprite=null

均按现有测试规约：`UI.ResetForTests()` + `BuiltinPrimitives.Register(UI.Registry)`。

### 9.2 EditorOnly（PromptUGUI.Tests.EditorOnly）

**`IconAtlasSyncerTests.cs`**

- ScanXmlReferences：fake 几个 .ui.xml 验证收集
- `{{...}}` 在 ns / name 部分 warn + 跳过
- EnumeratePngs：递归 / 重名 / 非 Sprite type 跳过
- UpdateAtlas：增 / 删 / 不变（no-op 不打 dirty）
- 缺失 sprite warn 但同步成功
- alwaysInclude 并入 needed
- sourceFolder=null / 非文件夹的错误路径

### 9.3 PlayMode（PromptUGUI.Tests.PlayMode）

**`IconRuntimeTests.cs`**

- 完整流水线：手工建 IconSet + 真 SpriteAtlas → UseSpriteAtlasIconResolver → 加载 Screen → 断言 sprite 非 null、color 正确
- Variant 切换 icon name → ReSolve sprite 变化
- Variant 切到不存在 → 该 Icon sprite=null，其他控件正常
- Hot-reload：模拟 NotifyAssetChanged(atlasPath) → lookup 重建

### 9.4 XSD 测试更新

`XsdGeneratorTests.cs` 增量：

- `StringAssert.Contains("Icon", xsd)`
- `StringAssert.Contains("name", xsd)`
- 含 `pattern` 的 simple type 验证 `ns:icon` 形式

---

## 10. 与现有文档的同步（PR 内必须完成）

### 10.1 SKILL.md（`.claude/skills/authoring-promptugui-xml/SKILL.md`）

- 新增 `<Icon>` 章节：语法、属性表、`ns:name` 规则
- 元素索引表加 `Icon` 行
- 新增 "Dynamic icon names" 小节：`{{...}}` 限制 + alwaysInclude 逃生口
- Variant 章节示例补一条 `attr.dark.name="ui:moon"`

### 10.2 Master spec（`2026-05-07-promptugui-description-language-design.md`）

- §7 内置控件章节：加 `<Icon>` 作为第 8 个（在 `<Btn>` 之后），含错误规则
- 新增章节"Icon assets"（暂定 §11）：IconSet SO、SpriteAtlas 后端、IconResolver 委托、Editor 同步语义
- §6 SizeSpec：增 `size="native"` 默认值规则（Icon 默认）
- §12 风险表：动态 icon 名限制、Atlas 4096 上限、AssetPostprocessor 自动同步成本

### 10.3 XSD 生成器（`Editor/XsdGenerator.cs`）

- `Icon` 元素加入主元素 union
- `name` 属性 simpleType 加 pattern `^[\w\-]+:[\w\-]+$`
- 沿用现有 `[UIAttr]` 反射机制；pattern 需扩 `[UIAttr]`，加可选 `Pattern` 字段

---

## 11. 不在范围内（Out of scope）

- **Image.Sprite 的 Resources.Load 耦合**：另一个清理任务，不在本 spec
- **Icon 状态切换 / 动画**：`<Btn>` 的按下变色等通过现有 Variant 表达；icon 序列帧不做
- **运行时下载 icon / Addressables 后端**：通过自定义 IconResolver 实现，本 spec 只给默认 Atlas 实现
- **多 atlas-per-set**（拆分突破 4k 上限）：后续优化项
- **TMP SDF font icon**：不进默认实现；如需可由用户写自定义 IconResolver

---

## 12. 风险

| 风险 | 缓解 |
|---|---|
| SpriteAtlas 4096 上限 | 文档化；超量时 LogWarning；后续可加 split 策略 |
| `PackAtlases` 同步阻塞主线程 | 手动 + 构建前为主；自动同步 opt-in |
| `name="ui:{{x}}"` 动态名漏打包 | `alwaysInclude` 字段 + SKILL.md 警告 |
| 第一次同步把 PNG importer 改成 Sprite type | Editor-only 操作；记录在 SKILL；首次同步前提示 |
| atlas dirty 进 git 噪音 | `PackablesEqual` 提前返回避免无变化 SetDirty |
| EditMode 测试无法用 SpriteAtlas API | EditorOnly asmdef 跑 sync 测试；EditMode 用 mock IconSet（atlas=null + 直接塞 lookup） |
| Hot-reload 触发 ReSolve 性能 | 复用现有 ReSolve；性能不够再拆 RefreshIcons |
| AssetDatabase.FindAssets 在大项目慢 | 默认不开 AssetPostprocessor；构建前一次性可接受 |

---

## 13. 实施顺序建议

仅给 writing-plans 排序提示，不是 plan：

1. IR/Parser：`<Icon>` 解析 + 错误（EditMode 测试驱动）
2. SizeSpec：`native` 值
3. `IconSet` SO + `Icon` Control + 默认 IconResolver（不依赖 Editor）
4. Editor 同步工具（先手动菜单，再 build hook，最后 AssetPostprocessor）
5. XSD 生成器扩展
6. Hot-reload 接入（atlas → ReSolve）
7. SKILL.md / master spec 同步
8. PlayMode 端到端测试
