# PromptUGUI

一个让 Unity 6+ 的 UI 可以用大模型进行开发的解决方案。

提供极其精简的 UI 描述语言 `.ui.xml` 和一个运行时解析器，翻译成uGUI结构。

比UI Toolkit更自由，更容易实现自定的样式，和GameObject体系结合更紧密。

## 功能特性

- **极简的XML描述语言，符合大模型习惯**
  - 支持热重载，改完立刻反馈
  - 支持响应式UI
  - 自动XSD Schema语法检查 + donet Lint CLI
  - 高扩展性，允许模板/Prefab模板
- **自动sprite/icon引用**
  - 可配置SpriteSet，后xml里用`<Icon name="solar:Forward" />`或`<Image sprite="ui:dialog" />`引用
  - 只打包用到的
  - 大模型极其擅长此种方式
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
  - 可继承实现高度自定义的对话框


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

Project 右键 → Create → PromptUGUI → Sprite Set，设置图标以及界面元素的图集。

图标建议使用公开图集，或起名一致，让大模型认识。比如可拖一个PNG图标集目录（比如Font Awesome）到Project，并设为SpriteSet Folder，此后Skill会自动发现你所拥有的所有图标。当然最后打包只含用到的图标/Sprite。

**推荐**使用Addressable，设置`SpriteSet.asset`和对应的SpriteAtlas的Label，如`FontAwesome`，然后用`await SpriteResolverHelpers.UseAddressableSpriteSetResolver("FontAwesome");`就可以实现按需下载和热更对应图标集，一个Label可以对应多个SpriteSet。

实际应该用比如`await SpriteResolverHelpers.UseAddressableSpriteSetResolver(new[] { "SpriteSetsA-Common", $"SpriteSetsA-{currentLang}" }, Addressables.MergeMode.Union);` 这样来只使用通用语言图集或当前语言的图集。

### 2. 设置字体和多国语言 (可选)

Project 右键 → Create → PromptUGUI → Settings，设置有哪些字体Type (`font="NormalText"`使用的就是Type名) ，以及需要哪些语言。

设置好即可，以后一键翻译会自动提取界面文本和代码中`UI.Tr()`包裹的字符串。

**建议**使用Addressable，点击菜单的`Setup Addressable for Locale ...`后，i18n目录即可移出Resources目录，放到其他目录。通过`UI.Locale.UseAddressableResolver();`后，`UI.Locale.SetToSystemDefault("en");`会自动后台下载多国语言，下完自动刷新界面。

### 3. 创建UI

Project 右键 → Create → PromptUGUI → UI XML。

让大模型按你的要求（Figma > 截图）写UI，XML的修改会自动反映在Play模式的界面上。

第一个界面大模型没有参考，选用的图素都是默认值，你需要手动修改或个别一一指示，之后会更顺利。

代码交给大模型，Skills包含了所有细节，可直接问大模型。你主要需要看顶部，了解有哪些功能。

**建议**使用Addressable，`[SerializeField] private AssetReferenceT<TextAsset> xmlSlot` 定义属性，
然后就能在Inspector中把`*.ui.xml`文件拖入`xmlSlot`，在脚本中`await UI.LoadDocumentAsync(xmlSlot);`按需下载和热更。

## 示例

```
write a normal login ui use promptugui, with Login.cs monobehavior script open it.
```

<img src="https://github.com/Heerozh/PromptUGUI/blob/media/example.png" width="394" height="497"/>

Response:

```
  Searched for 1 pattern, read 3 files, listed 6 directories (ctrl+o to expand)

● I'll explore the PromptUGUI conventions first by invoking the relevant skills.

● Skill(authoring-promptugui-xml)
  ⎿  Successfully loaded skill

● Skill(scripting-promptugui-csharp)
  ⎿  Successfully loaded skill

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

## 友情链接

[v2ex](https://www.v2ex.com/) | [linuxdo](https://linux.do)
