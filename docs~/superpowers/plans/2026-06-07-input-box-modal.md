# InputBox 内置模态 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在既有模态体系里新增第三个内置 overlay `InputBox`：一个返回用户输入文本的强模态对话框（`InputBox.Open(...) → Awaitable<string>`，确定返回文本、取消/ESC 返回 `null`）。

**Architecture:** 复用既有 `ModalRequest<TResult>` + `UI.Modal` + `ModalDocCache` 机制（队列 / ESC / sortingOrder / teardown 全部不动）。新增 `InputBoxRequest : ModalRequest<string>` + 静态 wrapper `InputBox`，与 `MessageBoxRequest` / `MessageBox` 完全平级；配套给 `InputField` 补一个公开 `TextValue` getter 以便 Bind 读回输入。内置 `.ui.xml` 走包内 Resources，可被 `InputBox.XmlSrc` 整体覆盖。

**Tech Stack:** C# (Unity 6, LangVersion 9)、uGUI、TMP_InputField、R3（Cysharp）、NUnit EditMode 测试、UnityMCP 跑测试、`dotnet format` + UIXmlLint 做 lint。

参考 spec：`docs~/superpowers/specs/2026-06-07-input-box-modal-design.md`

---

## File Structure

| 文件 | 动作 | 责任 |
|---|---|---|
| `Runtime/Controls/InputField.cs` | 修改 | 给 `TextValue` 属性补 getter（读 `_input.text`） |
| `Runtime/Application/Modals/InputBoxRequest.cs` | 新建 | `InputBoxRequest`（Bind/TryEscape）+ 静态 `InputBox`（XmlSrc/Open），与 `MessageBoxRequest.cs` 同构 |
| `Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml` | 新建 | 内置默认皮肤：backdrop + dialog(title + message + InputField + OK/Cancel) |
| `Tests/EditMode/Controls/InputFieldTests.cs` | 修改 | 2 条 getter 单测 |
| `Tests/EditMode/Modals/InputBoxTests.cs` | 新建 | Bind / 提交 / 取消 / label / message 节点等 11 条 EditMode 测试（fake-resolver 内联 XML） |
| `.claude/skills/scripting-promptugui-csharp/SKILL.md` | 修改 | overlay 计数 2→3、新增 InputBox 节、修 `NamePickerRequest` 的 `.Text`→`.TextValue` |

**约定**：每次改源码后先 `refresh_unity`，再 `read_console` 看编译错误，再 `run_tests`。EditMode 测试不存在的类型/成员会表现为**编译错误**（即本计划的 "红"），而非 NUnit 失败——这是 Unity/C# 的正常 TDD 形态。

---

## Task 1: `InputField.TextValue` getter

**Files:**
- Modify: `Runtime/Controls/InputField.cs:139-143`（`TextValue` 属性）
- Test: `Tests/EditMode/Controls/InputFieldTests.cs`（追加 2 条）

- [ ] **Step 1: 写失败测试**

在 `Tests/EditMode/Controls/InputFieldTests.cs` 的 `InputFieldTests` 类末尾（最后一个 `}` 之前）追加：

```csharp
        // --- TextValue getter ---------------------------------------------------

        [Test]
        public void TextValue_Getter_RoundtripsSetValue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var f = UI.Open("S").Get<PInputField>("f");
            f.TextValue = "abc";
            Assert.AreEqual("abc", f.TextValue);
        }

        [Test]
        public void TextValue_Getter_BeforeAttach_ReturnsNull()
        {
            // _input is null before OnAttached → getter must defend, not NRE.
            var f = new PInputField();
            Assert.IsNull(f.TextValue);
        }
```

- [ ] **Step 2: 刷新并确认红（编译错误）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `CS0154: The property or indexer 'InputField.TextValue' cannot be used in this context because it lacks the get accessor`（来自 `TextValue_Getter_RoundtripsSetValue` 和 `..._BeforeAttach_...`）。

- [ ] **Step 3: 实现 getter**

把 `Runtime/Controls/InputField.cs` 的：

```csharp
        [UIAttr("text"), Preserve]
        public string TextValue
        {
            set => _input.text = value ?? string.Empty;
        }
```

改为：

```csharp
        [UIAttr("text"), Preserve]
        public string TextValue
        {
            get => _input != null ? _input.text : null;
            set => _input.text = value ?? string.Empty;
        }
```

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="InputFieldTests")
```
Expected: 无编译错误；`InputFieldTests` 全绿（含新增 2 条）。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 退出码 0（若有 `Local.props` 缺失导致的 CS0246 噪音可忽略，但 whitespace/style 不应报 InputField.cs 的改动）。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Controls/InputField.cs Tests/EditMode/Controls/InputFieldTests.cs
git commit -m "$(cat <<'EOF'
feat(inputfield): TextValue 补 getter

读 _input.text（未 attach 时返回 null）。InputBox 模态 Bind 需要读回
当前输入；也修正 C# skill NamePicker 示例引用的 .Text（下一步统一改 skill）。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `InputBoxRequest` + 静态 `InputBox`

**Files:**
- Create: `Runtime/Application/Modals/InputBoxRequest.cs`
- Test: `Tests/EditMode/Modals/InputBoxTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `Tests/EditMode/Modals/InputBoxTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using TMPro;
using PBtn = PromptUGUI.Controls.Btn;
using PInputField = PromptUGUI.Controls.InputField;
using PText = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Modals
{
    public class InputBoxTests
    {
        private const string InputBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/InputBox1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x220'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Text id='message' fontSize='14'/>
        <InputField id='field' height='44'/>
        <Btn id='ok'>OK</Btn>
        <Btn id='cancel'>Cancel</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        // Same dialog but WITHOUT the message node — covers an override XML that drops it.
        private const string InputBoxNoMessageXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/InputBoxNoMsg'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <InputField id='field' height='44'/>
        <Btn id='ok'>OK</Btn>
        <Btn id='cancel'>Cancel</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>
            {
                ["test/InputBox1"] = InputBoxXml,
                ["test/InputBoxNoMsg"] = InputBoxNoMessageXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            InputBox.XmlSrc = "test/InputBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PInputField Field()
            => UI.Modal.TopScreen.Get<PInputField>("field");

        private static string Label(string id)
            => UI.Modal.TopScreen.Get<PBtn>(id).GameObject
                .GetComponentInChildren<TMP_Text>().text;

        [Test]
        public void Click_OK_returns_current_field_text()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            Field().TextValue = "Link";
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            Assert.AreEqual("Link", task.GetAwaiter().GetResult());
        }

        [Test]
        public void Click_Cancel_returns_null()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            UI.Modal.TopScreen.Get<PBtn>("cancel").SimulateClick();
            Assert.IsNull(task.GetAwaiter().GetResult());
        }

        [Test]
        public void Submit_resolves_with_submitted_text()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            Field().GameObject.GetComponent<TMP_InputField>().onSubmit.Invoke("typed");
            Assert.AreEqual("typed", task.GetAwaiter().GetResult());
        }

        [Test]
        public void Empty_input_then_OK_returns_empty_string_not_null()
        {
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?" });
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            var result = task.GetAwaiter().GetResult();
            Assert.IsNotNull(result);
            Assert.AreEqual("", result);
        }

        [Test]
        public void Initial_prefills_field()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Initial = "preset" });
            Assert.AreEqual("preset",
                Field().GameObject.GetComponent<TMP_InputField>().text);
        }

        [Test]
        public void ContentType_password_applied_to_field()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Pw", ContentType = "password" });
            Assert.AreEqual(TMP_InputField.ContentType.Password,
                Field().GameObject.GetComponent<TMP_InputField>().contentType);
        }

        [Test]
        public void Custom_ok_cancel_labels_override_default()
        {
            UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = "Name?",
                OkLabel = "Save",
                CancelLabel = "Later",
            });
            Assert.AreEqual("Save", Label("ok"));
            Assert.AreEqual("Later", Label("cancel"));
        }

        [Test]
        public void Null_message_hides_message_node()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = null });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("message").GameObject.activeSelf);
        }

        [Test]
        public void Message_present_shows_message_node_with_text()
        {
            UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = "details" });
            var msg = UI.Modal.TopScreen.Get<PText>("message");
            Assert.IsTrue(msg.GameObject.activeSelf);
            Assert.AreEqual("details", msg.TmpComponent.text);
        }

        [Test]
        public void Missing_message_node_in_xml_is_tolerated()
        {
            InputBox.XmlSrc = "test/InputBoxNoMsg";
            var task = UI.Modal.OpenAsync(new InputBoxRequest { Title = "Name?", Message = "ignored" });
            // Bind must not throw even though there is no 'message' id.
            Field().TextValue = "ok";
            UI.Modal.TopScreen.Get<PBtn>("ok").SimulateClick();
            Assert.AreEqual("ok", task.GetAwaiter().GetResult());
        }

        [Test]
        public void TryEscape_returns_null_and_true()
        {
            var req = new InputBoxRequest { Title = "Name?" };
            Assert.IsTrue(req.TryEscape(out var r));
            Assert.IsNull(r);
        }
    }
}
```

- [ ] **Step 2: 刷新并确认红（编译错误）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 编译错误 `CS0246: The type or namespace name 'InputBoxRequest' could not be found` 及 `'InputBox' does not exist`。

- [ ] **Step 3: 实现 `InputBoxRequest` + `InputBox`**

新建 `Runtime/Application/Modals/InputBoxRequest.cs`：

```csharp
using System;
using R3;

namespace PromptUGUI.Application.Modals
{
    public sealed class InputBoxRequest : ModalRequest<string>
    {
        public string Title;
        public string Message;
        public string Initial;
        public string Placeholder;
        public string ContentType;
        public string OkLabel;
        public string CancelLabel;

        public override string XmlSrc => InputBox.XmlSrc;

        public override void Bind(IScreen screen, Action<string> close)
        {
            var titleCtl = screen.Get<PromptUGUI.Controls.Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            // message 节点可选（覆盖 XML 可能删掉）
            try
            {
                var msgCtl = screen.Get<PromptUGUI.Controls.Text>("message");
                if (string.IsNullOrEmpty(Message)) msgCtl.GameObject.SetActive(false);
                else msgCtl.TextValue = Message;
            }
            catch (System.Collections.Generic.KeyNotFoundException) { /* message element is optional */ }

            var field = screen.Get<PromptUGUI.Controls.InputField>("field");
            if (!string.IsNullOrEmpty(ContentType)) field.ContentType = ContentType;
            if (Placeholder != null) field.Placeholder = Placeholder;
            field.TextValue = Initial ?? "";

            // 回车 = 确定；OnSubmit 直接给出当前文本。
            field.OnSubmit.Subscribe(v => close(v)).AddTo(screen);

            var ok = screen.Get<PromptUGUI.Controls.Btn>("ok");
            if (!string.IsNullOrEmpty(OkLabel)) ok.Text = OkLabel;
            ok.OnClick.Subscribe(_ => close(field.TextValue)).AddTo(screen);

            var cancel = screen.Get<PromptUGUI.Controls.Btn>("cancel");
            if (!string.IsNullOrEmpty(CancelLabel)) cancel.Text = CancelLabel;
            cancel.OnClick.Subscribe(_ => close(null)).AddTo(screen);
        }

        public override bool TryEscape(out string result)
        {
            result = null;   // ESC → 取消
            return true;
        }
    }

    public static class InputBox
    {
        // 必须带 .ui 后缀：Unity 只剥离 .ui.xml 文件名的最后 .xml。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/InputBox.ui";

        public static UnityEngine.Awaitable<string> Open(
            string title,
            string message = null,
            string initial = null,
            string placeholder = null,
            string contentType = null,
            string okLabel = null,
            string cancelLabel = null,
            ModalMode mode = ModalMode.Popup)
            => UI.Modal.OpenAsync(new InputBoxRequest
            {
                Title = title,
                Message = message,
                Initial = initial,
                Placeholder = placeholder,
                ContentType = contentType,
                OkLabel = okLabel,
                CancelLabel = cancelLabel,
            }, mode);
    }
}
```

- [ ] **Step 4: 刷新并确认绿**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"], filter="InputBoxTests")
```
Expected: 无编译错误；`InputBoxTests` 11 条全绿。

- [ ] **Step 5: Lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
```
Expected: 退出码 0。

- [ ] **Step 6: Commit**

```bash
git add Runtime/Application/Modals/InputBoxRequest.cs Tests/EditMode/Modals/InputBoxTests.cs
git commit -m "$(cat <<'EOF'
feat(modal): InputBoxRequest + 静态 InputBox

第三个内置 modal overlay：InputBox.Open(title, message, initial,
placeholder, contentType, okLabel, cancelLabel, mode) → Awaitable<string>。
确定/回车返回文本，取消/ESC 返回 null（区分于空串）。复用既有
ModalRequest<T> + UI.Modal，不碰队列/ESC/sortingOrder。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: 内置 `InputBox.ui.xml`

**Files:**
- Create: `Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml`

> 本任务产出一份静态资源（无 NUnit 单测——Bind 契约已由 Task 2 的 fake-resolver 测试覆盖同一套 id）。验证靠 UIXmlLint（结构）+ Screen-name byte-equal 校验 + 后续 visual QA。

- [ ] **Step 1: 创建内置 XML**

新建 `Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml`，与 `MessageBox.ui.xml` 同皮肤：

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/InputBox.ui" reference="1920x1080" reference.portrait="1080x1920" >
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

- [ ] **Step 2: 校验 `<Screen name>` 与默认 `InputBox.XmlSrc` byte-equal**

```bash
grep -q 'name="PromptUGUI/Modals/InputBox.ui"' Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml && echo "screen-name OK"
```
Expected: 打印 `screen-name OK`（与 `InputBox.XmlSrc` 默认值逐字相等——`ModalDocCache.EnsureLoaded` 的硬约束）。

- [ ] **Step 3: UIXmlLint**

```bash
dotnet run --project .lint/UIXmlLint -- Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml
```
Expected: 退出码 0，无 error（layout-group 子节点上没有非法 anchor/margin）。

- [ ] **Step 4: 刷新让 Unity 导入资源（生成 .meta）**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
```
Expected: 无 error（资源导入成功）。

- [ ] **Step 5: Commit**

```bash
git add Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml Runtime/Resources/PromptUGUI/Modals/InputBox.ui.xml.meta
git commit -m "$(cat <<'EOF'
feat(modal): 内置 InputBox.ui.xml 默认皮肤

backdrop + dialog(title + message + InputField + OK/Cancel)，复用
MessageBox 的九宫格皮肤；Screen name byte-equal InputBox.XmlSrc 默认值。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: C# SKILL.md 更新

**Files:**
- Modify: `.claude/skills/scripting-promptugui-csharp/SKILL.md`

- [ ] **Step 1: overlay 计数 2→3 + 索引行**

把 `## Modal dialogs` 段开头（`SKILL.md:524-528` 附近）的：

```
builtin overlays: a `MessageBox` dialog and a `Loading` spinner.
```

改为：

```
builtin overlays: a `MessageBox` dialog, an `InputBox` text prompt, and a `Loading`
spinner.
```

并在 `MODAL` 速查表区块（`SKILL.md:501-517` 附近）`MessageBox.Open(...)` 行之后补一行：

```
               var s = await InputBox.Open(title, message, initial, placeholder,
                                           contentType, okLabel, cancelLabel, mode)
                       // ↑ confirm → text ("" if empty); cancel/ESC → null
```

- [ ] **Step 2: Quick usage 补 InputBox 例子**

在 `### Quick usage` 代码块（`SKILL.md:532-547`）末尾、闭合 ``` 之前追加：

```csharp

// InputBox: text prompt. Returns the text on confirm/Enter, null on cancel/ESC.
// "" (empty) is distinct from null — empty submit vs cancelled.
string name = await InputBox.Open(UI.Tr("Your name?"), placeholder: UI.Tr("e.g. Link"));
if (name != null) game.PlayerName = name;

// password prompt with a sub-message line
string pw = await InputBox.Open(UI.Tr("Enter password"),
    message: UI.Tr("at least 8 chars"), contentType: "password");
```

- [ ] **Step 3: API surface 补 InputBox 块**

在 `### API surface` 代码块里，`MessageBox` 块之后、`[Flags] public enum MsgBtn ...` 之前插入：

```csharp
public static class InputBox {
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/InputBox.ui";

    // confirm/Enter → entered text ("" if empty); cancel/ESC → null
    public static Awaitable<string> Open(
        string title,
        string message     = null,   // optional line under the title
        string initial     = null,   // prefill text
        string placeholder = null,
        string contentType = null,   // InputField.contentType, e.g. "password" / "email"
        string okLabel     = null,
        string cancelLabel = null,
        ModalMode mode     = ModalMode.Popup);
}
```

- [ ] **Step 4: 修 `NamePickerRequest` 示例 `.Text` → `.TextValue`**

把 `### Custom modal types` 里（`SKILL.md:643` 附近）：

```csharp
        screen.Get<Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").Text)).AddTo(screen);
```

改为：

```csharp
        screen.Get<Btn>("ok").OnClick.Subscribe(_ =>
            close(screen.Get<InputField>("input").TextValue)).AddTo(screen);
```

并在该示例下方补一句注解：

```markdown
> `InputField.TextValue` is the read/write current-text property (the same one bound by
> the XML `text=` attribute). For the common "prompt the user for a string" case, prefer
> the builtin `InputBox.Open(...)` over hand-rolling a `ModalRequest<string>`.
```

- [ ] **Step 5: Commit**

```bash
git add .claude/skills/scripting-promptugui-csharp/SKILL.md
git commit -m "$(cat <<'EOF'
docs(skill): InputBox 内置模态 + InputField.TextValue getter

C# skill：overlay 计数 2→3、新增 InputBox Quick usage + API surface，
修正 NamePicker 示例 .Text→.TextValue（旧示例引用了不存在的 getter）。

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: 全量回归 + 收尾

**Files:** 无（验证 + 收尾提交）

- [ ] **Step 1: 全量 EditMode**

```
mcp__UnityMCP__refresh_unity(compile="request", mode="force", scope="all", wait_for_ready=true)
mcp__UnityMCP__read_console(action="get", types=["error"])
mcp__UnityMCP__run_tests(mode="EditMode", assembly_names=["PromptUGUI.Tests.EditMode"])
```
Expected: 无编译错误；EditMode 全绿（既有 + 新增 13 条），无回归。

- [ ] **Step 2: 全量 lint**

```bash
cd .lint && dotnet format --verify-no-changes --severity warn PromptUGUI.Lint.slnx
dotnet run --project UIXmlLint -- ../Runtime/Resources/
```
Expected: `dotnet format` 退出码 0；UIXmlLint 整个 Resources 目录退出码 0。

- [ ] **Step 3: （可选）PlayMode 冒烟**

模态机制本身已被既有 MessageBox PlayMode 测试覆盖、InputBox 无新机制，仅在改动接触了共享路径时跑：

```
mcp__UnityMCP__run_tests(mode="PlayMode", assembly_names=["PromptUGUI.Tests.PlayMode"], filter="Modal")
```
Expected: 既有 Modal PlayMode 测试无回归。

- [ ] **Step 4: 人工 visual QA（交给 user）**

在 host 工程跑一句 `await InputBox.Open("Your name?", placeholder: "e.g. Link")`，确认：弹窗居中、输入框可聚焦输入、回车=OK、ESC/Cancel 返回 null、password 走掩码。默认皮肤丑可由 user 覆盖 `InputBox.XmlSrc`。

- [ ] **Step 5: 推分支 + 开 PR（待 user 确认后）**

```bash
git push -u origin feat/input-box-modal
gh pr create --title "feat(modal): 内置 InputBox 文本输入模态" --body "..."
```
（DO NOT 合并 main；PR 描述含 spec/plan 链接 + 验收点。）

---

## Self-Review

**Spec coverage（逐节核对 spec → task）：**
- spec §2.1 `InputBox.Open` 7 参数 + XmlSrc → Task 2 Step 3 ✓
- spec §2.2 `InputBoxRequest`（字段 + Bind + TryEscape→null/true）→ Task 2 Step 3；TryEscape 测试 Task 2 Step 1 ✓
- spec §3 内置 XML（id: title/message/field/ok/cancel，Screen name byte-equal）→ Task 3 ✓
- spec §4 Bind（getter 读 OK、OnSubmit→close、message 可选 catch）→ Task 2 Step 3 + 测试 `Submit_*` / `Missing_message_node_*` ✓
- spec §5 `InputField.TextValue` getter（`_input!=null` 防御）→ Task 1 ✓
- spec §6 边界（空串≠null、message 隐藏/缺失、label 覆盖、ContentType）→ Task 2 测试 `Empty_input_*` / `Null_message_*` / `Custom_*` / `ContentType_*` ✓
- spec §7 测试（EditMode 11 + getter 2）→ Task 1 + Task 2 ✓
- spec §8 SKILL（计数 + InputBox 节 + 修 `.Text`）→ Task 4 ✓

**Placeholder 扫描：** 无 TBD/TODO；每个代码步骤都给了完整代码。Task 5 Step 5 的 PR body `"..."` 是占位但属交付动作（待 user 确认后填），非实现缺口。

**类型一致性：** `TextValue`（get/set）在 Task 1 定义、Task 2 Bind（`field.TextValue` 读写）+ Task 1/2 测试一致；`InputBox.XmlSrc` 默认 `"PromptUGUI/Modals/InputBox.ui"` 在 Task 2 Step 3 与 Task 3 Screen name 一致；`InputBoxRequest` 字段名（Title/Message/Initial/Placeholder/ContentType/OkLabel/CancelLabel）在 Step 3 定义与测试构造一致；`Btn.Text` / `Text.TmpComponent` / `Btn.SimulateClick` / `InputField.OnSubmit` 均为既有 API（已核对源码）。
