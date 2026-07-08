# PromptUGUI

[English](#promptugui) | [中文](#promptugui-中文)

A solution that enables Unity 2022.3+ / Unity 6+ UI development through LLM.

It provides an extremely concise UI description language `.ui.xml` & `.pxl` , and a runtime parser that translates it into a uGUI hierarchy + Sprite images.

More flexible than UI Toolkit, easier for custom styling, and tightly integrated with the GameObject system.

## WebGL Demo Page

This demo interface was created entirely by Code Agent, including the border images, icons, and more.

<https://heerozh.github.io/pugui/>

## Features

- **Minimal XML description language, tailored to how LLMs write**
  - Hot reload — edit, save, see the result instantly
  - Fully reactive UI support — automatically switches layout to match the screen and device
  - Pixel-art-style UI support
  - Automatic XSD schema validation + built-in syntax-check CLI
- **Sprite image description language in the X PixMap / GIMP style**
  - Another text format LLMs are already familiar with
  - No image-generation model required — a Code Agent can draw UI art and adjust its style straight from text descriptions
  - This is the way
- **Rich control set**
  - Built-in basic UI elements (Btn / Image / Frame / InputField / Toggle / Dropdown / Slider / ScrollList / Progress / TabBar, etc.)
  - Carousel card component for showing banners, announcements, and the like
  - Markdown text component with full markdown parsing (except HTML tags), including remote images
  - Layout elements (HStack / VStack / Grid, etc.)
  - Highly extensible — supports Templates / Prefab templates
- **Automatic sprite / icon resolution**
  - Configure a SpriteSet, then reference icons in XML via `<Icon name="solar:Forward" />` or `<Image sprite="ui:dialog" />`
  - Only the sprites actually used are bundled
  - Supports TMP inline text-and-image layout
  - LLMs handle this style of authoring extremely well
- **Gamepad / keyboard navigation**
  - Directional navigation (d-pad / arrow keys) is fully supported
  - with automatic focus management and visual Cursor feedback
  - auto hide Cursor when pointer/mouse is used, auto show Cursor when directional navigation is used
- **Fully automated i18n**
  - Auto-extracts UI text plus strings wrapped with `UI.Tr()` in C# code
  - Sends the strings (with surrounding context) to any OpenAI-compatible model for translation; source text may freely mix languages
  - Authors can annotate strings whose translation is inaccurate — the annotation is included as context on the next run
- **Addressables-backed on-demand download / hot update**
  - Sprite sets and locale files can be downloaded on demand by Label
  - `.ui.xml` files can be dragged into the Inspector as an Addressable Reference
- **Offset-based animation system**
  - Simple syntax, implemented on LitMotion
  - Animates relative offsets — anchor and parent/child layout don't interfere, ideal for dynamic UI
  - Triggers are extensible to Sound / Particle and beyond
- **Full-featured modal dialog system**
  - Async, blocking-style call site: `var result = await MessageBox.Open(...)`
  - Concurrent calls are queued so dialogs never collide
  - Built-in Toast popup notifications and loading-spinner modal support
  - Subclass for highly customized dialogs
- **Deep Linking** support via a professional Router system
  - Every UI should be opened / managed by the Router
  - And open UI through `appid://ui_name?param=value` links
  - Automatically handles hierarchy dependencies — navigation closes conflicting UIs so exclusive UIs never overlap, suits new-player tutorial creation
  - Also supports external link jumps, good for announcement / event jumps, equipment-info share links, etc.

## Install / Upgrade

### Claude Code

(Optional: open Unity and Unity MCP first.) In the project directory, send this prompt:

```
Install PromptUGUI into this Unity project. Please fetch it using `curl` and follow the instructions: https://github.com/Heerozh/PromptUGUI/raw/refs/heads/main/install_for_claude.md
```

Same procedure for upgrades.

### Manual install

0. Prerequisites:

Install DotNet SDK 10: https://dotnet.microsoft.com/zh-cn/download

Install NuGetForUnity: https://github.com/GlitchEnzo/NuGetForUnity

Install R3: https://github.com/Cysharp/R3

Install LitMotion: https://github.com/annulusgames/LitMotion.git

Install UniTask — **only on Unity 2022.3** (Unity 6+ uses the native `Awaitable` API and needs no extra dependency): https://github.com/Cysharp/UniTask


1. UPM
Window > Package Manager > "+" > "Add package from git URL" > Enter:

```
https://github.com/heerozh/PromptUGUI.git
```

2. Skills
Copy the three skill folders under the package's `.claude/skills/` directory into your project's agent skills directory:
Claude Code: `<project root>/.claude/skills/`
Codex: `<project root>/.agents/skills/`

> Skill files follow the open [Agent Skills](https://agentskills.io) specification, so compatible platforms (Codex / Gemini CLI / etc.) can reuse them — just drop them into that platform's skill directory.

3. AGENT.md / CLAUDE.md
Add this to your project-wide system prompt:
```
Use `PromptUGUI.Application` namespace's `UI.Tr("...")` to wrap all player-facing text for i18n.
```

## Usage

### 1. Create a SpriteSet

Project window → right-click → Create → PromptUGUI → Sprite Set. Configure the icon atlas and UI element atlas.

For icons, prefer publicly known icon packs or use consistent names so the LLM recognizes them. For example, drag a PNG icon folder (Font Awesome, etc.) into the Project, mark it as a SpriteSet Folder, and from then on the Skill will auto-discover every icon you own. Only the icons / sprites actually referenced are included in the build.

**Recommended**: use Addressables — set the `SpriteSet.asset` and its SpriteAtlas to a Label such as `FontAwesome`, then call `await SpriteResolverHelpers.UseAddressableSpriteSetResolver("FontAwesome");` to enable on-demand download and hot-update for that icon set. A single Label can map to multiple SpriteSets.

In practice you'd write something like `await SpriteResolverHelpers.UseAddressableSpriteSetResolver(new[] { "SpriteSetsA-Common", $"SpriteSetsA-{currentLang}" }, Addressables.MergeMode.Union);` so only the common atlas plus the current-language atlas are loaded.

### 2. Configure fonts and localization

Project window → right-click → Create → PromptUGUI → Settings. Configure the font Types (`font="NormalText"` refers to the Type name) and the languages you want to support.

Once configured, one-click translation will auto-extract UI text and any `UI.Tr()`-wrapped string in your code.

**Recommended**: use Addressables. Click the menu item `Setup Addressable for Locale ...` and the i18n folder can move out of Resources to any directory. After `UI.Locale.UseAddressableResolver();`, `UI.Locale.SetToSystemDefault("en");` will download the chosen language in the background and refresh the UI when it finishes.

### 3. Author a UI

Project window → right-click → Create → PromptUGUI → UI XML.

Ask the LLM to draft the UI from your spec (Figma > screenshot).

For the very first screen the LLM has no reference, so the chosen sprites are default placeholders — you'll need to nudge or correct it once. After that it converges quickly.

Hand the code to the LLM. The Skills cover every detail, so you can ask the LLM directly. You mainly need to skim the top of each Skill so you know what's available.

**Recommended**: use Addressables — declare `[SerializeField] private AssetReferenceT<TextAsset> xmlSlot;`, drag a `*.ui.xml` file into `xmlSlot` in the Inspector, then in code call `await UI.LoadDocumentAsync(xmlSlot);` for on-demand download and hot update.

### Documentation

Please refer to the [Best Practices](BEST_PRACTICES.md) document.

## Example

```
write a normal login ui use promptugui, with Login.cs monobehavior script open it.
```

<img src="https://github.com/Heerozh/PromptUGUI/blob/media/example.png" width="394" height="497"/>

Response:

```
  Searched for 1 pattern, read 3 files, listed 6 directories (ctrl+o to expand)

● I'll explore the PromptUGUI conventions first by invoking the relevant skills.

● Skill(authoring-promptugui-xml)
  ⎿  Successfully loaded skill

● Skill(scripting-promptugui-csharp)
  ⎿  Successfully loaded skill

...

● Clean compile. Let me complete the task tracking and summarize.

● Created two files:

  Assets/Resources/UI/Login.ui.xml — login screen with title/subtitle, username + password fields (password masked via
  contentType="password"), "Remember me" toggle, "Forgot password?" link, primary Login button, and a "Sign up" footer.
  Uses reference="1920x1080" with a reference.portrait="1080x1920" override so it scales sensibly on both PC and mobile.

  Assets/Scripts/Login.cs — MonoBehaviour that:
  1. Sets up UseResourcesResolver("UI"), loads Login.ui, opens the screen in Start.
  2. Tracks _username / _password / _rememberMe via R3 OnValueChanged subscriptions (InputField has setter-only
  TextValue, so caching latest values via R3 is the idiomatic way).
  3. Wires the three buttons (login / forgot / register) to handlers that pop a MessageBox for feedback. Player-facing
  strings are wrapped in UI.Tr(...) per the project's i18n convention; XML source text is auto-extracted as msgid (no
  wrapping needed there).

  To use it: drop the Login script onto an empty GameObject in your scene and hit Play.

  Validation: xmllint passes against Assets/PromptUGUI.gen.xsd; Unity recompile is clean.

✻ Worked for 8m 22s
```

XML Code:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PromptUGUI version="1">

  <Screen name="Login" reference="1920x1080" reference.portrait="1080x1920">

    <Image anchor="stretch" color="#1E2433"/>

    <Frame id="card" anchor="center" size="420x600">
      <Image anchor="stretch" color="#FFFFFF"/>

      <VStack anchor="stretch" margin="32" spacing="14">
        <Text fontSize="30" align="center" color="#111827">Welcome Back</Text>
        <Text fontSize="14" align="center" color="#6B7280">Sign in to your account</Text>

        <Frame height="16"/>

        <Text fontSize="14" color="#374151">Username</Text>
        <InputField id="username" placeholder="Enter your username"
                    width="stretch" height="40"/>

        <Frame height="4"/>

        <Text fontSize="14" color="#374151">Password</Text>
        <InputField id="password" placeholder="Enter your password"
                    contentType="password"
                    width="stretch" height="40"/>

        <HStack height="28" spacing="8">
          <Toggle id="rememberMe">Remember me</Toggle>
          <Frame width="stretch"/>
          <Btn id="forgot" color="#FFFFFF00">
            <Text anchor="stretch" align="center" fontSize="13" color="#3B82F6">Forgot password?</Text>
          </Btn>
        </HStack>

        <Frame height="8"/>

        <Btn id="login" color="#3B82F6" width="stretch" height="44">
          <Text anchor="stretch" align="center" fontSize="16" color="#FFFFFF">Login</Text>
        </Btn>

        <HStack height="24" spacing="4" childAlign="middle-center">
          <Text fontSize="13" color="#6B7280">No account?</Text>
          <Btn id="register" color="#FFFFFF00">
            <Text anchor="stretch" align="center" fontSize="13" color="#3B82F6">Sign up</Text>
          </Btn>
        </HStack>
      </VStack>
    </Frame>

  </Screen>

</PromptUGUI>

```


C# Code:

```csharp
    private async void Start()
    {
        UI.UseResourcesResolver("UI");
        await UI.LoadDocumentAsync("Login.ui");
        var screen = UI.Open("Login");

        screen.Get<InputField>("username").OnValueChanged
              .Subscribe(v => _username = v).AddTo(screen);

        screen.Get<InputField>("password").OnValueChanged
              .Subscribe(v => _password = v).AddTo(screen);

        screen.Get<Toggle>("rememberMe").OnValueChanged
              .Subscribe(v => _rememberMe = v).AddTo(screen);

        screen.Get<Btn>("login").OnClick
              .Subscribe(_ => OnLoginClicked()).AddTo(screen);

        screen.Get<Btn>("forgot").OnClick
              .Subscribe(_ => OnForgotClicked()).AddTo(screen);

        screen.Get<Btn>("register").OnClick
              .Subscribe(_ => OnRegisterClicked()).AddTo(screen);
    }

    private async void OnLoginClicked()
    {
        if (string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_password))
        {
            await MessageBox.Open(
                UI.Tr("Please enter both username and password."), MsgBtn.OK);
            return;
        }

        Debug.Log($"[Login] user={_username} remember={_rememberMe}");
        await MessageBox.Open(UI.Tr("Welcome back!"), MsgBtn.OK);
    }
```

---

# PromptUGUI (中文)

[English](#promptugui) | [中文](#promptugui-中文)

一个让 Unity 2022.3+ / Unity 6+ 的 UI 可以用大模型进行开发的解决方案。

通过描述语言 `.ui.xml` 以及 `.pxl` 和一个运行时解析器，翻译成uGUI结构 + Sprite图像。
整体架构全面且简洁，适合大模型直接书写而脱离MCP。

比UI Toolkit更自由，和GameObject体系结合更紧密。

简介视频： <https://www.bilibili.com/video/BV1zzM369Exa/>

## WebGL演示页面

此演示界面全由Code Agent编写，甚至包括边框图像，Icon等等。

<https://heerozh.github.io/pugui/>

## 功能特性

- **极简的XML描述语言，符合大模型习惯**
  - 支持热重载，改完立刻反馈
  - 全响应式UI支持，自动根据屏幕和设备切换布局
  - 像素艺术风格UI支持
  - 自动XSD Schema语法检查 + 内置语法检查 CLI
- **提供X PixMap/GIMP 风格的Sprite图像描述语言**
  - 同样是大模型熟悉的文本格式
  - 无须生图模型，可通过文字描述让Code Agent直接为UI出图，修改风格
  - This is the way
- **控件丰富**
  - 基础UI元素支持（Btn/Image/Frame/InputField/Toggle/Dropdown/Slider/ScrollList/Progress/TabBar等）
  - Carousel 轮播卡片组件，用于显示Banner公告等；
  - Markdown文本组件，支持markdown全解析(除html标签)，支持网络图片等；
  - 布局元素（HStack/VStack/Grid等）
  - 高扩展性，允许模板/Prefab模板
- **自动sprite/icon引用**
  - 可配置SpriteSet，后xml里用`<Icon name="solar:Forward" />`或`<Image sprite="ui:dialog" />`引用
  - 只打包用到的
  - 支持TMP图文混排
  - 大模型极其擅长此种方式
- **手柄/键盘导航**
  - 完整支持方向键/摇杆导航
  - 自动管理焦点和光标显示
  - 当使用鼠标/指针时自动隐藏光标，当使用方向键/摇杆时自动显示光标
- **全自动多国语言系统**
  - 自动提取界面文本，以及代码中`UI.Tr()`包裹的字符串
  - 自动携带上下文交给OpenAI兼容的模型自动翻译，原文可任意语言混写
  - 对于翻译不准确的内容，用户可在文字上添加注释，会携带到上下文
- **Addressable随需下载/热更支持**
  - Sprite集和多国语言可按Label方式随需下载
  - UI XML可以作为Addressable Reference直接拖入
- **Offset-based动画系统**
  - 语法简单，LitMotion实现
  - 基于offset，不受锚点/父子关系影响，适合动态UI
  - Trigger支持扩展为Sound/Particle等更多功能
- **完善的 Model 模态对话框系统**
  - 异步堵塞式书写方便： `var result = await MessageBox.Open(...)`
  - 多次调用通过队列依次弹出避免冲突
  - Toast小信息弹出提示系统，Loading转圈模态窗口等等内置支持
  - 可继承实现高度自定义的对话框
- **Deep Linking** ，专业的 Router 系统
  - 所有UI都应该由Router打开/管理，提供统一的导航链接
  - 然后通过 appid://ui_name?param=value 形式的链接打开对应功能模块
  - 自动处理层级依赖关系，导航时会自动关闭冲突的UI，防止独占UI重叠。新手引导更容易制作。
  - 除了内部互相跳转，也支持从外部打开，实现公告/活动跳转、装备信息分享链接、外部操作后返回游戏等场景。


## 安装/升级方法

### Claude Code

（可选：打开Unity和Unity MCP），在项目目录执行以下提示词：

```
我希望安装 PromptUGUI 到本Unity项目，请curl获取后遵循指导： https://github.com/Heerozh/PromptUGUI/raw/refs/heads/main/install_for_claude.md
```

升级同样。

### 手动安装：

0. Prerequisite:

Install DotNet SDK 10: https://dotnet.microsoft.com/zh-cn/download

Install NuGetForUnity: https://github.com/GlitchEnzo/NuGetForUnity

Install R3: https://github.com/Cysharp/R3

Install LitMotion: https://github.com/annulusgames/LitMotion.git

Install UniTask —— **仅 Unity 2022.3 需要**（Unity 6+ 用原生 `Awaitable`，无需额外依赖）: https://github.com/Cysharp/UniTask


1. UPM
Window > Package Manager > "+" > "Add package from git URL" > Enter:

```
https://github.com/heerozh/PromptUGUI.git
```

2. Skills
把包内的 `.claude/skills/` 目录下的三个 skill 目录 copy 到你项目对应agent的skills目录下
Claude Code: `<project root>/.claude/skills/`
Codex: `<project root>/.agents/skills/`

> Skill 文件遵循开放 [Agent Skills](https://agentskills.io) 规范，兼容平台（Codex / Gemini CLI 等）也可复用，只需放到对应平台的 skill 目录。

3. AGENT.md / CLAUDE.md
把以下内容写到项目全局提示词：
```
Use `PromptUGUI.Application` namespace's `UI.Tr("...")` to wrap all player-facing text for i18n.
```

## 使用方法

### 1. 创建SpriteSet

Project 右键 → Create → PromptUGUI → Sprite Set，设置图标以及界面元素的图集。让大模型知道图片放哪。

比如可拖一个PNG图标集目录（比如Font Awesome）到Project，并设为SpriteSet Folder，此后Skill会自动发现你所拥有的所有图标。当然最后打包只含用到的图标/Sprite。

### 2. 设置字体和多国语言

Project 右键 → Create → PromptUGUI → Settings，设置有哪些字体Type (`font="NormalText"`使用的就是Type名) ，以及需要哪些语言。

设置好即可，以后一键翻译会自动提取界面文本和代码中`UI.Tr()`包裹的字符串。

### 3. 创建UI

Project 右键 → Create → PromptUGUI → UI XML。

然后让大模型按你的要求（Figma > 截图）写UI。代码也交给大模型，Skills包含了所有细节，可直接问大模型。

第一个界面大模型没有参考，选用的图素都是默认值，你需要手动修改或个别一一指示，之后会更顺利。

### 文档

请参考 [最佳实践](BEST_PRACTICES.zh.md) 文档


> 演示示例请见上方英文版的 [Example](#example) 章节。

