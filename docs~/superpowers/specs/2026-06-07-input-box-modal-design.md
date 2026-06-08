# 内置 InputBox 模态设计

**日期**：2026-06-07
**状态**：设计阶段（待 review，未进入实施）
**作用域**：在既有 `PromptUGUI.Application.Modals` 模态体系里新增**第三个内置 overlay**——`InputBox`：一个返回用户输入文本的强模态对话框。包含 (1) 静态便捷 wrapper `InputBox.Open(...)` → `Awaitable<string>`；(2) 内置子类 `InputBoxRequest : ModalRequest<string>`；(3) 内置 `.ui.xml` 模板（title + 可选 message + `<InputField>` + OK/Cancel）；(4) 给 `InputField` 补一个公开的 `TextValue` getter（当前只有 setter），以便 Bind 时读取最终文本。
**依赖**：[`2026-05-14-messagebox-modal-design.md`](2026-05-14-messagebox-modal-design.md)（通用模态系统 + `ModalRequest<T>` + `UI.Modal` + `ModalDocCache` + ESC/sortingOrder/队列，已实现并合并）；`Runtime/Application/Modals/MessageBoxRequest.cs`（结构原型）；`Runtime/Controls/InputField.cs`（被复用的输入控件）；`Runtime/Application/Modals/ModalMode.cs`

---

## 1. 背景与目标

模态系统在 MessageBox PR 里已建好——队列、ESC 监听、sortingOrder 叠加、`ModalDocCache` 加载分流、teardown 取消都是现成的。`ModalRequest<TResult>` 从 day 1 就是开放扩展点，`MessageBox` / `Loading` 只是其上的两个内置实例。

当前缺口：**没有内置的「让用户输入一段文本」对话框**。这是仅次于 MessageBox 的高频需求：

```csharp
string name = await InputBox.Open(UI.Tr("Your name?"), placeholder: UI.Tr("e.g. Link"));
if (name != null) game.PlayerName = name;
```

调用方今天能自己写 `ModalRequest<string>` 子类做到（C# skill 里的 `NamePickerRequest` 示例正是如此），但：

1. 每个项目都要重抄一遍 Bind + 一份 XML；
2. skill 示例引用了 `screen.Get<InputField>("input").Text`，而 `InputField` **没有这个 getter**——示例无法编译。说明「读回输入值」这一步缺一个公开入口。

目标：把这套封装成开箱即用的内置 overlay，与 `MessageBox.Open` 对称。

### 设计原则

1. **复用既有模态机制**：InputBox 不碰 `UI.Modal` / `ModalDocCache` / ESC / sortingOrder 任何一行——它只是又一个 `ModalRequest<TResult>` 子类 + 静态 wrapper，与 `MessageBoxRequest` 完全平级。
2. **结果类型安全且语义清晰**：`TResult = string`。确定 → 返回输入文本（可能是空串 `""`）；取消 / ESC → 返回 `null`。用 `null` vs `""` 区分「取消」和「提交了空内容」。
3. **caller 端 API 最小化**：常见路径一行 `await InputBox.Open(title)`；高级需求（password 输入、自定义按钮文案、初始值、placeholder）走可选具名参数。
4. **内置 + 可覆盖**：XML 是包内 Resources 资源，`InputBox.XmlSrc` 可写覆盖，与 MessageBox 一致。

### 非目标

- 不做内置必填 / 格式校验（调用方自己判 `null` / 空 / 正则）。需要时调用方写自定义 `ModalRequest<T>`。
- 不透传 `InputField` 的全部属性（`characterLimit` / `lineType` / `align` / 颜色…）。v1 只暴露 `contentType`（password 等高频场景）；其余通过覆盖 XML 表达。
- 不做多字段表单（一个对话框多个输入框）。那是独立特性，调用方走自定义 `ModalRequest<T>`。
- 不改动 ESC / sortingOrder / 队列 / teardown 任何既有行为。

---

## 2. C# API

命名空间沿用 `PromptUGUI.Application.Modals`。无新增枚举（结果是 `string`，不需要 `MsgBtn` 那样的标志枚举）。

### 2.1 静态便捷 wrapper `InputBox`

```csharp
public static class InputBox {
    // 与 MessageBox 一致：必须带 .ui 后缀（Unity 只剥离 .ui.xml 的最后 .xml）。
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/InputBox.ui";

    public static UnityEngine.Awaitable<string> Open(
        string title,
        string message     = null,   // 可选：title 下方一行说明文字；null/空 → 隐藏该节点
        string initial     = null,   // 输入框初始文本
        string placeholder = null,   // 空时的占位提示
        string contentType = null,   // 透传 InputField.contentType，如 "password" / "email"
        string okLabel     = null,   // 覆盖 OK 按钮文案（默认走 XML 内 "OK" + .po 翻译）
        string cancelLabel = null,   // 覆盖 Cancel 按钮文案
        ModalMode mode     = ModalMode.Popup);
}
```

`title` 是唯一必填项（输入框的提示语通常就是它）。其余全可选具名参数。

### 2.2 内置 `InputBoxRequest`

```csharp
public sealed class InputBoxRequest : ModalRequest<string> {
    public string Title;
    public string Message;
    public string Initial;
    public string Placeholder;
    public string ContentType;
    public string OkLabel;
    public string CancelLabel;

    public override string XmlSrc => InputBox.XmlSrc;
    public override void Bind(IScreen screen, Action<string> close);   // §4
    public override bool TryEscape(out string result) {                // ESC → 取消
        result = null;
        return true;
    }
}
```

ESC 始终映射为「取消」（返回 `null`），与「输入框对话框总能被取消」的直觉一致——这点与 MessageBox 不同（MessageBox 仅 OK 时 ESC 不响应，因为没有 cancel 语义键）。

### 2.3 调用例子

```csharp
// 最简
string name = await InputBox.Open(UI.Tr("Your name?"));
if (name != null) game.PlayerName = name;   // null = 取消/ESC；"" = 提交了空串

// password + 初始值 + placeholder
string pw = await InputBox.Open(
    UI.Tr("Enter password"),
    contentType: "password",
    placeholder: UI.Tr("at least 8 chars"));

// 自定义按钮文案 + 排队
string code = await InputBox.Open(
    UI.Tr("Redeem code"), message: UI.Tr("Paste your gift code below."),
    okLabel: UI.Tr("Redeem"), cancelLabel: UI.Tr("Later"),
    mode: ModalMode.Queued);
```

---

## 3. 内置 XML 模板

文件位置：`Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml`。结构沿用 MessageBox 的 dialog 皮肤（同一个九宫格 sprite + backdrop），把按钮行换成 title + message + InputField：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/InputBox.ui" reference="1920x1080" reference.portrait="1080x1920">
    <Image id="backdrop" anchor="stretch" color="#000000FE"/>

    <Image id="dialog" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"
           anchor="center" size="640x280">
      <VStack anchor="stretch" margin="24" spacing="12">
        <Text id="title" fontSize="24" height="40"/>
        <Text id="message" width="stretch" height="stretch" fontSize="18"/>
        <InputField id="field" height="44"/>
        <HStack height="44" spacing="8">
          <Btn id="ok">OK</Btn>
          <Btn id="cancel">Cancel</Btn>
        </HStack>
      </VStack>
    </Image>
  </Screen>
</PromptUGUI>
```

- `<Screen name>` 与 `InputBox.XmlSrc` 默认值**逐字节相等**——`ModalDocCache.EnsureLoaded` 的硬约束（见 MessageBox spec §6.2）。
- `id` 契约（Bind 依赖）：`title` / `message` / `field` / `ok` / `cancel`。`message` 节点 message 为空时 `SetActive(false)`，VStack 自动 collapse。
- 按钮英文写死作 msgid 兜底，`.po` 翻译 "OK" / "Cancel"。
- 颜色 / size / 字号是粗糙默认——pixel-art 项目大概率覆盖 XML。与 MessageBox 同样的「内置 + 可覆盖」定位。

---

## 4. `InputBoxRequest.Bind` 实现

```csharp
public override void Bind(IScreen screen, Action<string> close) {
    var titleCtl = screen.Get<Controls.Text>("title");
    if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
    else titleCtl.TextValue = Title;

    // message 节点可选（XML 覆盖时可能删掉）→ 容忍 KeyNotFoundException
    try {
        var msgCtl = screen.Get<Controls.Text>("message");
        if (string.IsNullOrEmpty(Message)) msgCtl.GameObject.SetActive(false);
        else msgCtl.TextValue = Message;
    } catch (System.Collections.Generic.KeyNotFoundException) { }

    var field = screen.Get<Controls.InputField>("field");
    if (!string.IsNullOrEmpty(ContentType)) field.ContentType = ContentType;
    if (Placeholder != null) field.Placeholder = Placeholder;
    field.TextValue = Initial ?? "";

    // 回车 = 确定。OnSubmit 直接给出当前文本。
    field.OnSubmit.Subscribe(v => close(v)).AddTo(screen);

    var ok = screen.Get<Controls.Btn>("ok");
    if (!string.IsNullOrEmpty(OkLabel)) ok.Text = OkLabel;
    ok.OnClick.Subscribe(_ => close(field.TextValue)).AddTo(screen);

    var cancel = screen.Get<Controls.Btn>("cancel");
    if (!string.IsNullOrEmpty(CancelLabel)) cancel.Text = CancelLabel;
    cancel.OnClick.Subscribe(_ => close(null)).AddTo(screen);
}
```

关键点：
- **确定时通过 `field.TextValue`（getter）读最终文本**——这是本特性需要给 `InputField` 补 getter 的唯一动因（§5）。
- `close` 多次调用由模态系统幂等保证（MessageBox spec §4.4：`_currentResolved` flag）——回车后又点 OK 不会二次 resolve。
- `message` 节点缺失被容忍，与 MessageBox 对 `icon` 节点的处理一致。

---

## 5. `InputField` 公开 `TextValue` getter

当前 `InputField.TextValue` 是只写属性（`[UIAttr("text")]`）：

```csharp
[UIAttr("text"), Preserve]
public string TextValue { set => _input.text = value ?? string.Empty; }
```

改为读写：

```csharp
[UIAttr("text"), Preserve]
public string TextValue {
    get => _input != null ? _input.text : null;
    set => _input.text = value ?? string.Empty;
}
```

理由与取舍：
- **复用 `TextValue` 而非新增 `Value` / `Text`**：`TextValue` 已是该类（及 `Text` 控件）的既有「当前文本」属性名，加 getter 最一致，不引入第二个名字。
- **修正既有 skill 不实之处**：C# skill 的 `NamePickerRequest` 示例引用 `.Text`（不存在）→ 改为 `.TextValue`。
- `_input` 为 null 时返回 `null`（防御 OnAttached 之前的访问），与既有 `PeekDefaultText()` 的 null 处理一致。`PeekDefaultText()` 是内部 ReSolve 用途，保持不动。
- `[UIAttr]` / `[Preserve]` 不变——加 getter 不影响属性应用或反射裁剪。

---

## 6. 边界情况

| 情况 | 行为 |
|---|---|
| `title` 传 `null`/空 | title 节点 `SetActive(false)`（虽是必填参数，仍防御） |
| `message` 为空 | message 节点 `SetActive(false)`；覆盖 XML 删了 message 节点也不报错（catch KeyNotFound） |
| 用户输入空串后点 OK | 返回 `""`（**非** `null`）——与「取消」区分 |
| 回车后又点 OK | 第二次 `close` 被模态系统幂等忽略 |
| `contentType` 传非法值 | `InputField.ContentType` setter 抛 `ArgumentException`（既有行为）→ 走模态 Bind 异常路径（MessageBox spec §4.4：close Screen + SetException + pump 下一个） |
| 覆盖 XML 缺 `field` id | `screen.Get<InputField>("field")` 抛 KeyNotFound → 同上异常路径 |
| Locale / Variant 切换 | InputBox 是普通 Screen，ReSolve 原地生效。输入框文本不被 ReSolve 重置——但**不是**靠 `RuntimeStateAttr`：`InputField` 没有注册 runtimeStateAttr（见 `BuiltinPrimitives.cs`，对比 Toggle/Tab/Slider/Dropdown 都注册了）。真实原因是内置 `InputBox.ui.xml` 的 `<InputField id="field">` 上**没有 `text=` 属性**，ReSolve 因此根本不调 `TextValue` setter。⚠️ 反过来：若用户覆盖 XML 时给 field 写了字面 `text="…"`，开着对话框切 Locale 会清掉已输入内容——v1 不处理，作者覆盖 XML 时勿在 field 上写 `text=`。 |
| ESC | `TryEscape` 返回 `null`（取消），仅顶层 modal 响应（既有 `ModalEscapeListener`） |

无新增模态基础设施，故 ESC / sortingOrder / 队列 / teardown / hot-reload 全部沿用 MessageBox 已验证的路径，本特性不重测。

---

## 7. 测试策略

测试集中在 EditMode（Bind 逻辑 + getter），模态机制本身已被 MessageBox 测试覆盖，不重复。

### 7.1 EditMode（`PromptUGUI.Tests.EditMode`，新增 `Tests/EditMode/Modals/InputBoxTests.cs`）

沿用 `ModalTestFixture` / fake-resolver 模式（把 `InputBox.XmlSrc` 指向测试 XML，或直接用内置 Resources 路径）：

```csharp
[Test] public void Open_then_clickOk_returns_current_text();
[Test] public void Open_then_clickCancel_returns_null();
[Test] public void Submit_via_OnSubmit_resolves_with_text();   // 模拟回车
[Test] public void Empty_input_then_ok_returns_empty_string_not_null();
[Test] public void Initial_text_prefills_field();
[Test] public void ContentType_password_applied_to_field();
[Test] public void Custom_ok_cancel_labels_override_default();
[Test] public void Null_message_hides_message_node();
[Test] public void TryEscape_returns_null_and_true();
[Test] public void Missing_message_node_in_xml_is_tolerated();   // 覆盖 XML 删 message
```

模拟点击/提交：`Controls.Btn` 已有 internal `SimulateClick()`（MessageBox 测试用过）；`InputField` 提交走 `OnSubmit`——测试期可对 internal `_input` 或新增一个 internal `SimulateSubmit(string)` 辅助（实现细节留 plan 决定，优先不加新 API：可直接设 `TextValue` 后触 OnSubmit 的既有路径）。

### 7.2 InputField getter 单测（`Tests/EditMode`，并入既有 `InputFieldTests.cs`）

```csharp
[Test] public void TextValue_getter_roundtrips_set_value();
[Test] public void TextValue_getter_before_attach_returns_null();   // 防御 _input==null
```

### 7.3 不测的

- PlayMode 端到端弹窗 + 真实键盘回车——模态弹出/ESC/sortingOrder 已由 MessageBox PlayMode 测试覆盖，InputBox 无新机制。
- 视觉样式（默认皮肤好不好看）——靠用户覆盖 XML，做 visual QA。

---

## 8. SKILL.md 影响

按 `CLAUDE.md` trigger 规则：

- 新公开 C# API（`InputBox` 静态类 + `InputBoxRequest` + `InputField.TextValue` getter）→ **`scripting-promptugui-csharp/SKILL.md` 必须更新**：
  - "Modal dialogs" 节的 intro 从「two builtin overlays」改为「three builtin overlays」，新增 InputBox 的 Quick usage + API surface。
  - 修正 `NamePickerRequest` 示例 `.Text` → `.TextValue`。
- 内置 `InputBox.ui.xml` 全部用已有 built-in tag（`<Image>` / `<VStack>` / `<HStack>` / `<Text>` / `<InputField>` / `<Btn>`）→ 无新 XML tag/attribute → **`authoring-promptugui-xml/SKILL.md` 不需要更新**。
- 不涉及 `PROMPTUGUI_HAS_ADDRESSABLES` → **addressables skill 不需要更新**。
- 无新 XML tag → **无 XSD / `BuiltinTags.cs` 改动**。

---

## 9. 实施顺序（plan 阶段细化）

1. **`InputField.TextValue` getter** + 2 条 getter 单测（红 → 绿）。
2. **`InputBoxRequest`**（Bind + TryEscape）+ 内置 `InputBox.ui.xml`。
3. **`InputBox` 静态 wrapper**（`Open` + `XmlSrc`）。
4. **EditMode `InputBoxTests`**——10 条用例红 → 绿。
5. **C# SKILL.md 更新**（overlay 计数 + InputBox 节 + 修 `.Text` → `.TextValue`）。

每步跑 lint（`dotnet format --verify-no-changes --severity warn`）+ UnityMCP 编译检查 + 对应单元测试。

---

## 10. 验收标准

- `await InputBox.Open("Your name?")` 弹出输入对话框，输入 "Link" 点 OK → task 返回 `"Link"`。
- 点 Cancel / 按 ESC → 返回 `null`。
- 不输入直接点 OK → 返回 `""`（非 `null`）。
- `contentType: "password"` 时输入框走密码掩码。
- `okLabel` / `cancelLabel` 覆盖按钮文案；`initial` 预填；`placeholder` 显示占位。
- `InputBox.XmlSrc = "MyUI/Modals/Foo.ui"` 能整体替换皮肤（`<Screen name>` 须 byte-equal）。
- EditMode 测试全绿；`dotnet format --verify-no-changes --severity warn` 干净。
