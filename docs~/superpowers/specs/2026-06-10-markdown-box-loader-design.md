# MarkdownBox 延迟内容加载 + 默认链接分发

**日期**:2026-06-10
**状态**:设计阶段(待 review,未进入实施)
**作用域**:扩展 `MarkdownBox`(同分支 PR #64):(1) 延迟内容加载——先开窗显示占位 Loading,内容到达后热替换,关窗自动取消加载;(2) `OpenUrl` 裸 GET 便捷重载;(3) `UI.Markdown.HandleLink` 默认链接分发(Router scheme 联动),并成为 `MarkdownBoxRequest` 的默认链接行为。
**关联**:叠加在 [`2026-06-10-markdown-box-modal-design.md`](2026-06-10-markdown-box-modal-design.md) 之上(同一 feature 分支/同一 PR);Router 联动建立在 [`2026-06-09-router-navigation-design.md`](2026-06-09-router-navigation-design.md) 的 `UI.Router.Navigate(url)` / `Router.Scheme` 之上,不改 Router。公开 C# API 新增,`scripting-promptugui-csharp` 必须更新(见 §8);XML skill 不动。

---

## 1. 背景与目标

公告/邮件的 markdown 源文通常在服务器上。浏览器式体验是:**立即开窗显示 Loading → 下载完成热替换 → 用户关窗则取消下载**。这套生命周期(configure 时序、销毁守卫、取消方向)每个接 CDN 的游戏都要写、且容易写错,应进库。同时,markdown 里的链接目前默认裸 `Application.OpenURL`——对配置了 `Router.Scheme` 的游戏,`appid://` 深链会被错误地丢给系统浏览器,需要一个正确的默认分发。

明确不做(上轮讨论已定):

- `.md` 后缀自动嵌套打开 → **不做默认**(启发式易误判:签名 URL 带 query、API 路径无后缀;且隐含裸 GET 绕过鉴权)。游戏一行自定义:`onLinkClicked: url => url.EndsWith(".md") ? MarkdownBox.OpenUrl(url) : UI.Markdown.HandleLink(url)`。
- `OpenUrl` 不加自定义 headers/鉴权参数——需要鉴权的内容走 `Open(loader)`,用游戏自己的网络栈。
- 错误展示不参数化——loader 内自行 catch 并返回错误 markdown 即可定制。

## 2. `MarkdownBoxRequest` 扩展(生命周期核心)

```csharp
public sealed class MarkdownBoxRequest : ModalRequest<bool>
{
    public string Text;                                      // 既有
    public Func<CancellationToken, Awaitable<string>> Loader; // 新:非 null 时忽略 Text
    public string LoadingText = "*Loading…*";                // 新:占位 markdown
    // Title / OnLinkClicked / XmlSrc / TryEscape 不变
}
```

`Bind` 中 `Loader != null` 时:

1. `md.Text = LoadingText`(占位即一段普通 markdown);
2. 建 `CancellationTokenSource`,`Disposable.Create(cts.Cancel).AddTo(screen)` —— **关窗(×/backdrop/ESC/外部 ct,任何通道)→ Screen Dispose → 自动取消加载**。Queued 模式排队时 Bind 尚未发生 → 真正上屏才开始加载,时序天然正确;
3. fire-and-forget `FillAsync(md, Loader, cts.Token)`:
   - 成功 → `md.Text = 结果`(热替换,`Markdown.Text` setter 自带重渲染);
   - `OperationCanceledException` → 静默返回(关窗正常路径,**不得再触碰已销毁的控件**);
   - 其他异常 → `Debug.LogWarning` + `md.Text = "**Failed to load.**\n\n" + ex.Message`(若此刻未取消)。

`Loader` 与 `Text` 同时设置时 Loader 胜出(Text 被忽略),文档注明;不抛错。

## 3. 静态门面新重载

```csharp
public static async Awaitable Open(
    Func<CancellationToken, Awaitable<string>> loader,
    string title = null,
    Action<string> onLinkClicked = null,
    string loadingText = null,                // null → 默认 "*Loading…*"
    ModalMode mode = ModalMode.Popup,
    Action<IScreen> configure = null,
    CancellationToken ct = default);

public static Awaitable OpenUrl(
    string url, /* 同上,无 loader 参 */)
    => Open(ct2 => FetchAsync(url, ct2), ...);
```

`FetchAsync`(private,`MarkdownBoxRequest.cs` 内)镜像 `UI.Markdown.LoadWebTextureAsync` 的既有模式(`UnityWebRequest.Get` + `AwaitableCompletionSource` + `op.completed`,WebGL 安全、无线程),加取消:

```csharp
using var req = UnityWebRequest.Get(url);
var op = req.SendWebRequest();
var acs = new AwaitableCompletionSource<bool>();
op.completed += _ => acs.TrySetResult(true);
using var reg = ct.Register(() => req.Abort());
if (!op.isDone) await acs.Awaitable;
ct.ThrowIfCancellationRequested();
if (req.result != UnityWebRequest.Result.Success)
    throw new InvalidOperationException($"{url}: {req.error}");
return req.downloadHandler.text;
```

不做内容缓存(公告/邮件每次开窗重新拉是合理默认;要缓存的游戏在自己 loader 里做)。

## 4. `UI.Markdown.HandleLink(string url)`(默认链接分发)

放在 `UI.Markdown`(决策点 a):它是 markdown 子系统的链接政策,独立 `<Markdown>` 页面订阅 `OnLinkClicked` 时同样可用,与 `DefaultStyle` / `ImageResolver` 同层级。

```csharp
public static void HandleLink(string url)
{
    var scheme = UI.Router.Scheme;
    if (!string.IsNullOrEmpty(scheme) &&
        url.StartsWith(scheme + "://", StringComparison.Ordinal))
        _ = NavigateLogged(url);          // try/catch + Debug.LogError,仿 HotReload 模式
    else
        UnityEngine.Application.OpenURL(url);
}
```

- `Router.Scheme` 未设置 → 一切走 `OpenURL`(Router 未启用,库无意见)。
- scheme 命中但路由解析失败(`RouteException` 等)→ LogError,不回落 OpenURL(深链交给系统浏览器只会更糟)。
- 测试钩子:`internal static Action<string> OpenUrlHookForTests`(null → 真 `Application.OpenURL`),`ResetForTestsInternal` 清空。

## 5. `MarkdownBoxRequest` 默认链接行为变更(决策点 b)

`Bind` 中链接订阅的 else 分支由 `Application.OpenURL(url)` 改为 `UI.Markdown.HandleLink(url)`。

**行为变化标注**:对已配置 `Router.Scheme` 的游戏,MarkdownBox 里的 `appid://` 链接从"丢给系统浏览器(坏)"变为"走 Router 导航(对)"。这是修正而非破坏;未配置 Scheme 的游戏行为完全不变。传了 `onLinkClicked` 的调用方不受影响(仍完全接管)。

## 6. 与现有体系的交互

- `ModalDocCache` / pump / dialog 栈:零改动。
- `configure` 钩子:与 Loader 正交——configure 在 Bind 后照常触发,可改尺寸/拿控件。
- 外部 `ct:` 取消模态 → 关窗 → Screen Dispose → loader 的 cts 一并取消,两个取消方向自动复合。
- ReSolve(横竖屏翻转):`Markdown.Text` 是运行时赋值(builtin XML 无 `text=` 属性),变体翻转不重置内容——已有 `Variant_flip_keeps_text_and_hidden_title` 测试钉住,对 Loader 路径同样成立。

## 7. 测试(EditMode,仿既有模式)

1. **同步 loader 热替换**:loader 返回 `AwaitableHelpers.Completed("# done")` → `md.Text == "# done"`。
2. **占位可见**:loader 用 `AwaitableCompletionSource<string>` 悬置 → 开窗后 `md.Text == "*Loading…*"`;`SetResult` 后 → 热替换。
3. **自定义 LoadingText** 生效。
4. **关窗取消**:悬置 loader 捕获传入的 ct;点 × 关窗 → `ct.IsCancellationRequested == true`;之后再 `SetResult` → 不抛、不触碰已销毁控件(测试无异常即过)。
5. **loader 抛异常** → `LogAssert.Expect(LogType.Warning, ...)`,`md.Text` 以 `**Failed to load.**` 开头。
6. **Loader 胜出**:`Text` 与 `Loader` 同时设置 → 显示 loader 结果。
7. **HandleLink scheme 命中**:`Router.Scheme = "app"` + 注册假 Page 路由,`HandleLink("app://p1")` → 路由激活(断言方式仿 Router 既有 EditMode 测试)。
8. **HandleLink 未命中/未启用**:`OpenUrlHookForTests` 捕获 → 普通 https、以及 Scheme 为 null 时的任意 url,都落到 hook。
9. **MarkdownBox 默认走 HandleLink**:开 MarkdownBox(无 onLinkClicked),`RaiseLinkClickedForTests("https://x")` → `OpenUrlHookForTests` 捕获到。
10. `OpenUrl` 的真实网络不测;其取消/错误路径由 §7.4/7.5 的 loader 层测试覆盖(FetchAsync 视为薄胶水)。

## 8. SKILL 更新

`scripting-promptugui-csharp/SKILL.md`:

- MODAL cheatsheet:`MarkdownBox.Open(loader, ...)` / `OpenUrl(url, ...)` 行 + "关窗自动取消加载"注释;
- `### Quick usage`:OpenUrl 一例 + 自带鉴权的 Open(loader) 一例;
- `### API surface`:MarkdownBox 块补两个重载与 `LoadingText` 语义;
- MARKDOWN 区(或 cheatsheet MARKDOWN 行):`UI.Markdown.HandleLink` 条目(scheme→Router,否则 OpenURL;默认链接行为变更说明);
- `.md` 嵌套的"游戏自己做"一行示例。

`authoring-promptugui-xml`:不动。
