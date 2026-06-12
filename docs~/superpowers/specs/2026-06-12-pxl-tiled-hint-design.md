# `.pxl` tiled 标记 → 运行时自动 Image.Type.Tiled（pxl-tiled-hint）设计

日期：2026-06-12
分支：基于 `feat/farm-pixel-skin` 的工作（依赖该分支未提交的"默认皮肤 Sliced→Tiled + Btn/Tab `_baseType`"改动）
前置：`.pxl` 像素导入管线（Editor/Pxl）、Sync Atlases（SpriteAtlasSyncer）、SpriteSet 解析（SpriteResolverHelpers）

## 1. 目标与动机

带 border 的 sprite 在 uGUI 里有两种渲染：Sliced（边拉伸）和 Tiled（角固定 + 边/中心平铺）。
青苔、藤蔓、木纹这类**有方向性图案的边框必须 Tiled**，否则拉糊。当前的痛点：

1. 作者每个 `<Image sprite=...>` 都要手写 `type="tiled"`，忘写就默默变 Sliced（CommonControls
   实际踩坑：6 处面板框漏写）。
2. Tab / Btn 等控件根本没有 `type=` 属性，控件内部从 `border != 0 → Sliced` 盲推，默认皮肤
   只能靠硬编码特判（feat/farm-pixel-skin 这轮在 Btn / Tab 各加了一套 `_baseType` 手工补丁）。
3. "这张图设计成平铺"本质是**资产自身的属性**，应该声明在 `.pxl` 里、随 sprite 走，而不是
   散落在每个引用点。

本设计：`.pxl` section 加 `tiled: true` 指令；运行时凡解析到该 sprite（任何通道、任何控件）
自动选 `Image.Type.Tiled`；显式 `type=` 仍最高优先。

## 2. 非目标（YAGNI）

- 不做 PNG sprite 的 tiled 标记（无 sidecar 文件格式；但 §5 的 `SpriteSet.Entry.tiled`
  字段是格式无关的，未来若有 PNG sidecar 可直接喂入）。
- 不做 per-edge 平铺控制（CSS `border-image` 式逐边 repeat/stretch）——uGUI Image 单
  `type` 字段，做不到也不需要。
- 不给 Tab / Btn / Toggle 等控件新增 `type=` XML 属性。
- 不改 `<Icon>` / TMP 内联图文（始终 Simple / TMP 自管，平铺无意义）。

## 3. `.pxl` 格式：per-section `tiled:` 指令

```
[vine-frame]
border: 4,4,4,4
tiled: true
grid:
  ...
```

- 位置：section 内、`grid:` 之前，与 `border:` 同级；两者顺序不限。
- 取值：`true` / `false`（默认 false）。其他值 = 带行号的导入错误
  （`invalid tiled value '...' (expected true|false)`）。
- 重复声明 last-wins（同 `border:` 的既有规则：grid 前重复声明取最后一个）。
- **允许无 border**：整图无缝纹理（草地、水面填充）也可平铺。PxlImporter 一律
  `SpriteMeshType.FullRect`，borderless Tiled 走几何重复，合法。
- 隐式单 section 文件同样支持（指令直接放 header 之后）。
- PNG 往返（Export/Sync）：`tiled:` 是元数据，与 `border:`/`ppu:` 同等待遇——
  Sync from PNG 只重写 grid 行，`tiled:` 原样保留，无需改 PxlPngSync。

## 4. 载体：`PxlSpriteHints` 子资产

运行时无法从 `Sprite` 引用反查任何自定义元数据，所以 hints 作为 `.pxl` 导入产物
的一个子资产随资产库走：

- 新 Runtime 类型 `PromptUGUI.Application.PxlSpriteHints : ScriptableObject`，
  唯一字段 `[SerializeField] List<Sprite> tiledSprites`（**直接 Sprite 引用**，
  不存名字——避免跨集撞名 / 改名失效）。类放 Runtime asmdef（运行时要
  `Resources.LoadAll<PxlSpriteHints>` 反序列化），创建只在 Editor（importer）。
- `PxlImporter`：文件中存在任何 `tiled: true` section 时，`ctx.AddObjectToAsset`
  一个 hints 子资产（命名固定 `__pxl_hints`，不参与 sprite key 空间），引用对应
  Sprite 子资产。全 false 则不生成（零成本路径）。
- `PxlImporterEditor` 只读面板：有 tiled 标记的 section 在尺寸/border 行旁追加
  "tiled" 字样。

## 5. 运行时登记表：`SpriteRenderHints`

`Runtime/Application/Internal/SpriteRenderHints.cs`，internal static：

```csharp
internal static class SpriteRenderHints
{
    static readonly HashSet<int> _tiledIds = new();   // instanceID，不 pin 资产
    public static void Register(Sprite s);             // null 安全
    public static bool IsTiled(Sprite s);              // null → false
    public static void Clear();                        // UI.ResetForTests 调用
}
```

三个填充口（覆盖全部解析通道）：

1. **`UI.ResolveSprite` 的 Resources 分支**（裸路径 + `path#name` 两个子分支）：
   在 `Resources.LoadAll<Sprite>(path)` 同处补一次
   `Resources.LoadAll<PxlSpriteHints>(path)` 并逐个 Register。Resources.LoadAll
   有 Unity 自身缓存，重复调用廉价。
2. **SpriteSet 通道**（Resources 与 Addressables 共用 `BuildLookup`）：
   `SpriteSet.Entry` 增加 `public bool tiled;`（List 序列化向后兼容，旧资产读出
   false）。`BuildLookup` 构表时对 `tiled` 条目 Register。
   Editor 侧 `SpriteAtlasSyncer`：扫 sourceFolder 时对每个 `.pxl` 加载其
   `PxlSpriteHints` 子资产，含于其中的 Sprite 对应的 entry（含裸名别名条目）烙
   `tiled = true`。Addressables 路线拿到的 Entry 引用同一批 `.pxl` 子资产 Sprite
   原件（无克隆），同一张表天然生效。
3. **内置皮肤自举**：`ProceduralBuilders.GetDefaultSprite` 首次
   `Resources.LoadAll<Sprite>` 处，同步 LoadAll hints 并 Register
   （`ResetDefaultSpriteCacheForTests` 同时让位给 `SpriteRenderHints.Clear`，
   二者都挂在 `UI.ResetForTests`）。

## 6. 消费端：统一 `DeriveType`

`ProceduralBuilders` 新增唯一推导点：

```csharp
public static UnityImage.Type DeriveType(Sprite s) =>
    s == null                       ? UnityImage.Type.Simple :
    SpriteRenderHints.IsTiled(s)    ? UnityImage.Type.Tiled  :
    s.border != Vector4.zero        ? UnityImage.Type.Sliced :
                                      UnityImage.Type.Simple;
```

改写所有手写 border 推导处（行号为当前工作树）：

| 调用点 | 现状 | 改后 |
|---|---|---|
| `ProceduralBuilders.AutoSlice` | border→Sliced/Simple | `img.type = DeriveType(img.sprite)`（null sprite 不动，维持原契约） |
| `ProceduralBuilders.ApplyDefaultSlicedSprite` | 硬编码 Tiled（本分支临时手段） | `DeriveType(s)`（pugui round 标了 tiled → 结果不变） |
| `ProceduralBuilders.ApplyDefaultInsetSprite` | 硬编码 Sliced | `DeriveType(s)`（inset 未标 → Sliced 不变） |
| `Image` auto-pick（`Image.cs:143-150`，`!_typeExplicit`） | border→Sliced/Simple | `DeriveType`；显式 `type=` 优先级不变 |
| `Btn.Sprite` setter / `ApplyStateSprite` | AutoSlice + authored override 手写 border 推导 | 均走 `DeriveType`；`_baseType` 机制保留，值来源统一 |
| `Tab.ApplyBgSprite` / `ApplySelectedSprite` | 手写 border 推导 | 同上 |
| `Progress` 轨/填充的 Simple/Sliced 选择（非 Filled 分支） | AutoSlice 镜像规则 | 已经走 `AutoSlice` 的自动受益；私有重复实现（若有）改 `DeriveType` |
| `CarouselView.cs:254` 卡片 sub-sprite | 手写 border 推导 | `DeriveType` |

不改：`Dropdown.cs:105`（item 行有意 Simple）、mask 路径（stencil 形状用,
`AutoSlice` 改动对纯白 mask sprite 无视觉差，可随 AutoSlice 自然变化）、
`Image type="contain|cover"`（强制 Simple）。

## 7. 回收临时手段（同一交付物内）

1. `pugui.pxl`：`pugui_9slice_round` / `pugui_9slice_pressed` 标 `tiled: true`。
2. `ApplyDefaultSlicedSprite` 的硬编码 `Type.Tiled` 还原为 `DeriveType`（§6）。
3. CommonControls.ui.xml：删除 6 处 `type="tiled"`（自动推导接管）。
4. Btn / Tab 的 `_baseType` 赋值点全部改经 `DeriveType`，删除散落的三元 border 推导。
5. 既有测试随契约微调：`ApplyDefaultSlicedSprite_SetsRoundTiled` /
   `Tab_DefaultSkin_StaysTiled_AcrossSelection` 断言不变（结果仍 Tiled，来源变了）；
   作者 sprite 的 Sliced 断言不变（未标记 sprite 行为零变化）。

## 8. 错误与边界

- `tiled: 1` / `tiled: yes` 等 → 导入错误（行号），sprite 不产出（同既有 pxl 错误模型）。
- hints 子资产损坏/丢失（手删）→ 登记缺失，退化为 border 推导（Sliced）——渲染退化但不报错。
- 同一 Sprite 经多通道重复 Register → HashSet 幂等。
- 域重载/`ResetForTests` → `Clear()` 后由各通道在下次解析时重新登记（与
  `_defaultSprites` 缓存同生命周期）。
- 烫更新（pxl 重导入触发 SpriteResolverRebuilder）→ BuildLookup 重跑即重新登记；
  instanceID 在重导入后可能变化，旧 ID 残留无害（不会撞上新 sprite）。

## 9. 测试计划

EditorOnly（`PromptUGUI.Tests.EditorOnly`）：
- parser：`tiled: true/false` 解析、非法值/重复声明报错（带行号）、隐式 section 支持、
  PNG Sync 往返保留 `tiled:` 行。
- importer：标记 section → hints 子资产存在且引用正确 sprite；全 false → 无子资产。
- syncer：sourceFolder 含标记 `.pxl` → SpriteSet entry（路径 key + 裸名别名）`tiled=true`。

EditMode（`PromptUGUI.Tests.EditMode`）：
- `SpriteRenderHints` 注册/查询/Clear/幂等。
- `DeriveType` 四分支。
- `ResolveSprite` Resources 分支登记（fake hints 资产放 Tests 用 Resources）。
- `BuildLookup` 对 tiled entry 登记。
- 消费端各一条：Image auto-pick、Btn authored sprite、Tab selectedSprite、Carousel 卡片
  ——标记 sprite → Tiled，未标记带 border → Sliced（回归）。
- 内置皮肤自举：默认 Btn bg `type == Tiled` 且来源是 hints（删除硬编码后仍绿）。

## 10. SKILL 同步

- `authoring-promptugui-pxl/SKILL.md`：per-section 指令清单加 `tiled:`；craft 段补
  "平铺边设计准则"（边条带按周期设计、两端收回底色衔接四角）。
- `authoring-promptugui-xml/SKILL.md`：Image `type` 段补一句"`.pxl` 标记 `tiled: true`
  的 sprite 自动按 Tiled 渲染，显式 `type=` 可覆盖"。
- C# SKILL：免更（`SpriteRenderHints` internal，`SpriteSet.Entry.tiled` 由工具填，
  均为 transparent default）。
