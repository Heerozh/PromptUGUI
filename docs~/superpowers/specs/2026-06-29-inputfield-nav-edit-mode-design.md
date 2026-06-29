# InputField 方向导航：两级编辑模式（设计）

承接 `2026-06-29-gamepad-keyboard-navigation-design.md`（方向导航主特性，已合并 main / PR #91）。
本文是其聚焦后续：让 `<InputField>` 在手柄/键盘方向导航下行为正确。

## 1. 问题

`<InputField>` 底层是 `TMP_InputField`（`Runtime/Controls/InputField.cs:57`，本身是 `Selectable`）。
一旦它被**激活进入编辑态**（caret 闪烁），就独占键盘：方向键被 `OnUpdateSelected`
拿去移动文本 caret，EventSystem 的 move 事件不再驱动导航 → 焦点卡在输入框上、移不出去。

桌面平台上 Unity 默认**导航选中输入框即自动激活编辑态**（`shouldActivateOnSelect`，
按**平台**而非输入设备门控）。所以：

- PC 上键盘方向键选中输入框 → 立刻进编辑 → 方向键被吃；
- **PC 上插手柄一样撞这堵墙**（平台门控，与设备无关）；
- 真机主机平台才默认正确（主机上 `shouldActivateOnSelect=false`，选中只聚焦不编辑）。

结论：库自己统一接管激活时机，而不是依赖平台默认。

## 2. 设计：两级模型（导航 ↔ 编辑）

| 级别 | 进入 | 方向输入 | 退出 |
|---|---|---|---|
| **导航级**（默认） | — | 在控件间移动焦点，**含移上/移出输入框**，焦点光标自由走 | — |
| **编辑级** | 聚焦的输入框上按 **Submit**（手柄 A / 键盘 Enter） | 移动文本 caret | **Cancel**（手柄 B / 键盘 Esc）；单行框 Submit 确认即退出 |

机理：导航选中输入框时**只聚焦不激活**（字段显示 Selected/Focused 视觉，作为普通
Selectable 参与 `OnMove` 导航）；Submit 才 `ActivateInputField()` 进编辑。

## 3. 范围与门控

- **仅当 `UI.UseGamepadNavigation()` 已启用时生效**；纯指针项目零影响。
- **指针点击输入框仍立即进编辑**（鼠标 UX 不变）——区分"指针选中"与"导航选中"。
- 启用导航后**所有平台一致**（接管桌面默认，使 PC+键盘 / PC+手柄 / 主机 都走两级模型）。
- "导航选中"判定 = 选中发生时 `UI.Navigation.IsDirectional`（Directional 模式）。
  Pointer 模式下的选中（鼠标点）→ 保持默认激活。

## 4. 退出与方向细节

- **单行框**：Submit（Enter/A）确认并退出编辑回导航级。
- **多行框**（`lineType=multi-newline`）：Enter 是插入换行，不能用来退出 → 用 Esc/Cancel 退出；
  编辑态 Up/Down 移动 caret（多行有行间意义，**不**挪用）。
- **可选增强（单行框）**：编辑态按 Up/Down（单行无 caret 意义）→ 退出编辑并向上/下导航，
  少按一次 Esc。多行框不启用此增强。
- **Tab**：保留经典逐框跳转（导航级），不受影响。

## 5. 边界

- `readOnly` / `password` / 数字等 contentType：同一模型（仍可进编辑态输入）。
- `interactable=false`：本就不可聚焦（`TMP_InputField.interactable=false`），不参与。
- **编辑中切到鼠标**（Mode 翻 Pointer）：保持编辑态（指针仍可编辑），不强制退出。
- **焦点光标**：编辑态时输入框仍是选中项，光标默认仍显示在其旁（同时有 caret）。
  v1 接受双指示；可选：编辑态隐藏导航光标以减干扰（标记为可选增强，非 v1 必须）。

## 6. 机制决策（实现期定）

抑制"导航选中即激活"两条候选路线，实现期用 TDD 验证择优：

- **A. 伴随组件**：在 InputField 根 GO 挂一个 `MonoBehaviour`（`ISelectHandler`/`ISubmitHandler`），
  `OnSelect` 时若 `IsEnabled && IsDirectional` → `DeactivateInputField()`（撤销 TMP 同帧激活，
  或下帧守卫去激活）；`OnSubmit` 未编辑 → `ActivateInputField()`。
  风险：TMP 的 `ActivateInputField` 延迟到 LateUpdate，同帧 Deactivate 的竞态需实测。
- **B. 子类 `PuiInputField : TMP_InputField`**：override `OnSelect`，导航选中时只做 Selectable
  高亮、跳过 `ActivateInputField()`；override/补 `OnSubmit` 进编辑。
  风险：复刻 `Selectable.OnSelect` 行为的版本脆弱性；但无同帧竞态、更稳。

倾向 B（无竞态），但以实测为准。无论哪条：**仅 `#if ENABLE_INPUT_SYSTEM` 不需要**
（用的是 EventSystem/Selectable + TMP，不碰 InputSystem 类型）；门控走 `UI.Navigation.IsEnabled/IsDirectional`。

## 7. 公共 API / 属性

- v1 **不新增 XML 属性**，行为在导航启用后自动生效（橱窗 sample 已调 `UseGamepadNavigation`）。
- 备选（暂不做）：`editOnFocus="true"` 让单个字段保留"选中即编辑"。记为未来选项。

## 8. 测试（TDD 红先行）

- **EditMode**（直接调接口，不依赖输入模拟）：
  1. 启用导航 + `Mode=Directional`，对 InputField 触发"导航选中"→ 断言 `_input.isFocused == false`（未进编辑）。
  2. 同上后触发 Submit → 断言 `isFocused == true`（进编辑）。
  3. `Mode=Pointer` 触发选中（指针）→ 断言 `isFocused == true`（指针仍立即编辑）。
  4. 未启用导航 → 默认行为不变（回归）。
  5. 编辑态触发 Cancel/Esc → 断言退出编辑（`isFocused == false`）。
- **PlayMode**（可选 smoke，InputTestFixture）：聚焦未编辑的输入框按方向键 → 选区移到相邻控件
  （证明 `OnMove` 导航生效、字段没吃方向键）。

## 9. SKILL 影响

- **C# SKILL**（`scripting-promptugui-csharp`）：在导航小节补"InputField 两级编辑模式"——导航选中只聚焦、
  Submit 进编辑、Cancel/Esc 退出；指针点击仍直接编辑。
- **XML SKILL**：无新属性则仅在 `reference/navigation.md` 的 InputField 相关处补一句行为说明。
