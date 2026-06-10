# MarkdownBox 内置模态:富文本公告/邮件显示框

**日期**:2026-06-10
**状态**:设计阶段(待 review,未进入实施)
**作用域**:新增第 4 个内置模态 `MarkdownBox`,配合 `<Markdown>` 控件做富文本只读显示(公告、邮件、更新日志等)。复用 `ModalRequest<T>` / `UI.Modal.OpenAsync` 体系,不改动现有模态语义。
**关联**:建立在 [`2026-06-09-markdown-control-design.md`](2026-06-09-markdown-control-design.md) 的 `<Markdown>` 控件与 [`2026-06-07-input-box-modal-design.md`](2026-06-07-input-box-modal-design.md) 的模态骨架之上。无作者可写的 XML 元素/属性变更,`authoring-promptugui-xml` 不动;公开 C# API 新增,`scripting-promptugui-csharp` 必须更新(见 §7)。

---

## 1. 背景与目标

`MessageBox` 是按钮驱动的决策框;公告/邮件类内容是**只读浏览**,不需要任何按钮,但需要大面积、可滚动、支持富文本(标题、链接、图片、图文混排)。`<Markdown>` 控件已具备渲染能力(自带 ScrollRect、`OnLinkClicked`、`ImageResolver` 全局兜底),缺一个开箱即用的模态壳。

与 `MessageBox` 的差异(需求原文):

1. **没有任何 OK/Cancel 按钮,也不可设置按钮。**
2. **关闭方式**:
   - (a) 点击右上角的 ✕(始终存在,浮在 Markdown 文本区之上、与之重叠);
   - (b) 点击暗色 backdrop 直接关闭;
   - (c) ESC(设计补充:点背景都能关,ESC 行为应一致)。
3. **标题可设置**:设置了则 Markdown 控件下移;未设置则该空间留给 Markdown。✕ 不受标题影响。

## 2. XML 骨架

`Runtime/Resources/PromptUGUI/Modals/MarkdownBox.ui.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">
  <Screen name="PromptUGUI/Modals/MarkdownBox.ui" reference="1920x1080" reference.portrait="1080x1920">
    <Image id="backdrop" anchor="stretch" color="#000000FE"/>

    <Image id="dialog" sprite="PromptUGUI/Defaults/pugui.png#pugui_9slice_round"
           anchor="stretch" margin="480,160" margin.portrait="80,240">
      <VStack anchor="stretch" margin="24" spacing="12">
        <Text id="title" fontSize="24" height="40"/>
        <Markdown id="markdown" width="stretch" height="stretch"/>
      </VStack>
      <Btn id="close" anchor="top-right" size="36x36" margin="12" sprite="">✕</Btn>
    </Image>
  </Screen>
</PromptUGUI>
```

要点:

- **尺寸自适应**:不学 MessageBox 的固定 `size="640x300"`,dialog 用 `anchor="stretch"` + margin(横屏左右 480/上下 160;竖屏 Variant 翻转为 80/240)。公告内容量大,自适应两种朝向比固定尺寸合理;个别调用想改尺寸用 `configure` 钩子。
- **标题重排**:`title` 在 VStack 内,未设置时 `SetActive(false)` → VStack 自动重排,`markdown`(stretch)占满整个内容区。与 `MessageBoxRequest` 现有的隐藏模式一致。
- **✕ 浮层**:`close` 按钮是 `dialog` 的直接子节点(不在 VStack 内),`anchor="top-right"` 浮在内容之上 —— 始终存在、与 Markdown 区重叠、不随标题移动。透明背景(`sprite=""`)+ "✕" 文字。

## 3. C# API

`Runtime/Application/Modals/MarkdownBoxRequest.cs`:

```csharp
public sealed class MarkdownBoxRequest : ModalRequest<bool>
{
    public string Text;                       // markdown 源文
    public string Title;                      // null/空 → 隐藏标题行
    public Action<string> OnLinkClicked;      // null → 默认 Application.OpenURL
    public override string XmlSrc => MarkdownBox.XmlSrc;
    public override void Bind(IScreen screen, Action<bool> close) { ... }
    public override bool TryEscape(out bool result) { result = true; return true; }
}

public static class MarkdownBox
{
    public static string XmlSrc { get; set; } = "PromptUGUI/Modals/MarkdownBox.ui";

    public static async Awaitable Open(
        string markdown, string title = null,
        Action<string> onLinkClicked = null,
        ModalMode mode = ModalMode.Popup,
        Action<IScreen> configure = null,
        CancellationToken ct = default)
        => await UI.Modal.OpenAsync(new MarkdownBoxRequest { ... }, mode, ct);
}
```

- **返回非泛型 `Awaitable`**:无按钮 ⇒ 没有有意义的结果值,关闭即完成。`ct` 取消 → `OperationCanceledException`,同现有模态。内部 `ModalRequest<bool>` 仅为复用泛型管线,恒以 `true` 关闭。
- **`Bind` 行为**:
  - `markdown.Text = Text ?? ""`;
  - `Title` 为 null/空 → `title.GameObject.SetActive(false)`,否则赋 `TextValue`;
  - `close` Btn `OnClick` → `close(true)`;
  - `backdrop`(`Controls.Image`)`OnPointerDown` → `close(true)`;
  - `markdown.OnLinkClicked` → `OnLinkClicked ?? (url => Application.OpenURL(url))`。订阅均 `.AddTo(screen)`。
- **ESC**:`TryEscape` 恒返回 true —— 点背景都能关,ESC 行为一致。
- **链接默认行为**:公告/邮件里的链接最常见诉求是打开网页,默认 `Application.OpenURL`;调用方传 `onLinkClicked` 则完全接管(不叠加默认行为)。
- **图片**:走 `<Markdown>` 控件现有的 `UI.Markdown.ImageResolver` 全局兜底,不加参数。

## 4. 与现有体系的交互

- `ModalDocCache` / `MaterializePump` / dialog 栈 / `ModalEscapeListener`:全部复用,零改动。
- `Configure` 钩子:基类 `ModalRequest<T>.Configure` 已有,`Open` 透传 —— 调用方可借此改尺寸、改样式、拿 `Markdown` 控件订阅更多事件。
- `UI.Router` Prompt 呈现:不在本次范围;`MarkdownBox` 与 `MessageBox` 一样可被调用方在 `onEnter` 里就地 `await`。

## 5. 非目标(v1)

- 不提供按钮/底部操作区(需求 1 明确排除;要按钮请用 `MessageBox` 或自定义模态)。
- 不提供"从 src 加载 markdown 文件"的便捷参数 —— 调用方自行取得字符串后传入。
- 不做打开/关闭转场动画。

## 6. 测试

EditMode(仿 `InputBoxModalTests` 模式,fake resolver + `ResetForTests`):

1. `Open` 后栈顶 Screen 存在,`markdown` 控件 `Text` 等于传入源文;
2. `title=null` → title GameObject inactive;`title="公告"` → active 且文本正确;
3. 点 `close` Btn → Awaitable 完成、Screen 销毁;
4. `backdrop.OnPointerDown` 触发 → 同上关闭;
5. ESC(`TryEscape`)→ 关闭;
6. `onLinkClicked` 传入自定义委托 → 链接事件路由到它(用 `Markdown.RaiseLinkClickedForTests`);
7. `ct` 取消 → `OperationCanceledException`。

## 7. SKILL 更新

- `scripting-promptugui-csharp/SKILL.md`:内置模态一节新增 `MarkdownBox.Open` 条目(签名、关闭三通道、链接默认 OpenURL、`configure` 可改尺寸)。
- `authoring-promptugui-xml`:不动(内置模态 XML 非作者可写面)。
