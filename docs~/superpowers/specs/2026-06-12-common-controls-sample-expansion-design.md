# CommonControls 示例扩充 + 新手引导演示 — 设计稿

日期：2026-06-12
范围：仅 `Samples~/CommonControls/`（示例内容），不改 Runtime / Editor 代码，不需要更新 SKILL。

## 目标

1. 补齐所有内置控件的示例覆盖：当前 CommonControls 只演示 InputField / Toggle / Slider / Dropdown / ScrollList；MainMenu 覆盖了 Btn / Icon 基础用法。其余内置控件（Grid、TabBar/Tab、Progress、Carousel、Markdown、RawImage、SafeArea、Trigger/Animation/Show、Frame 实质用法）以及非控件功能（内置模态 MessageBox / InputBox / MarkdownBox / Loading、Toast）均无示例。
2. 新增 UI.Tutorial 新手引导演示：几步引导要求用户实际操作几个控件，跨页演示路径定位与目标等待。

## 文件变更

| 文件 | 动作 |
|---|---|
| `Samples~/CommonControls/Resources/UI/Settings.ui.xml` | 改名为 `CommonControls.ui.xml`，Screen 名改为 `CommonControls`，内容重组为 TabBar 分页 |
| `Samples~/CommonControls/CommonControlsRunner.cs` | 重写：分页绑定逻辑 + 新手引导脚本 |

## 页面结构

整体包在 `<SafeArea>` 中（顺便演示）。顶部固定一行：标题 + 常驻「新手引导」Btn（id `tutorialBtn`）。其下 `<TabBar>` 切 4 页：

### ① 表单输入（Tab id `tabForm`）

现有内容保留：`InputField`（id `username`）、`Toggle`（id `muteAudio`）、`Slider`（id `masterVol`）、`Dropdown`（id `quality`）。本页是引导前半段的目标页。

### ② 展示反馈（Tab id `tabDisplay`）

- `Progress` + 两个小 Btn（+10 / −10）驱动数值
- `RawImage`：C# 生成渐变 `Texture2D` 喂入（`Icon` 不在本 sample 重复演示——它依赖 SpriteSet 资源，MainMenu sample 已完整覆盖）
- `Markdown`：一小段富文本（宿主未装 Markdig 时控件自动降级为纯文本，示例不做特殊处理）
- `Animation`/`Trigger`：点 Btn 触发 `<Animation on="click@...">` 让某元素弹跳
- Btn 状态可视化：`hoverColor` / `pressedColor` + `<Show on="state-hover">` 角标

### ③ 列表轮播（Tab id `tabList`）

- `ScrollList`（现有 OptionRow Template 保留）
- `Grid`：一组色块
- `Carousel`：3 张卡（itemTemplate + BindItems）、autoplay、dots

### ④ 模态提示（Tab id `tabModal`）

5 个 Btn 分别调用：

- `MessageBox.Open` → 结果 Toast 回显
- `InputBox.Open` → 输入内容 Toast 回显（cancel 时回显「已取消」）
- `MarkdownBox.Open`
- `Loading.Show` 2 秒后自动关
- `UI.Toast.Show`（id `toastBtn`，也是引导最后一个目标）

## 新手引导

点「新手引导」Btn 触发 `UI.Tutorial.Run("common-controls-intro", ...)`，**不注册 UseProgressStore**——每次从头跑，可反复体验。步骤：

1. `Step("CommonControls/tabForm")` — 先点 Tab 切到表单页（非激活页的控件路径解析不到，必须先把目标页带出来）
2. `Step("CommonControls/username", advance: Advance.When(输入非空))` — 要求输入
3. `Step("CommonControls/muteAudio")` — 勾选 Toggle
4. `Step("CommonControls/masterVol", advance: Advance.When(值变化))` — 拖动 Slider
5. `Step("CommonControls/tabModal")` — 切到模态页
6. `Step("CommonControls/toastBtn")` — 点按钮弹 Toast
7. `Step(null, advance: Advance.TapAnywhere)` — caption-only 结束页

引导进行中点「新手引导」按钮需防重入：`UI.Tutorial.IsActive` 时直接 return（嵌套 Run 会抛 InvalidOperationException）。

## 验证

1. `dotnet run --project .lint/UIXmlLint -- Samples~/CommonControls/Resources/UI/CommonControls.ui.xml` 零 error
2. Runner.cs 编译验证：临时放入宿主工程编译检查（或宿主已导入 sample 则同步副本），通过 UnityMCP `refresh_unity` + `read_console` 确认无编译错误后还原
3. 视觉 / 交互 QA：用户在宿主工程导入 sample 实际运行

## 非目标

- 不演示 i18n / Variant / Router / .pxl（各自已有文档，塞进来会让示例失焦）
- 不演示 Tutorial 断点续（UseProgressStore）——保持 demo 可反复触发
- 不改 package.json 之外的包元数据（若 sample displayName/description 需要微调则顺带改）
