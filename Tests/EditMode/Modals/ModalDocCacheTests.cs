using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalDocCacheTests
    {
        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Inst'>
    <Image id='backdrop' anchor='stretch' color='#000000C0'/>
  </Screen>
</PromptUGUI>";

        // 共享模板：既可被模态 XML <Import>，也可先装进 commons 池
        private const string ChipXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Chip'>
    <Param name='text'/>
    <Btn text='{{text}}'/>
  </Template>
</PromptUGUI>";

        private const string ImportingXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Import src='test/Chip'/>
  <Screen name='test/Importing'>
    <Chip id='ok' text='OK'/>
  </Screen>
</PromptUGUI>";

        private const string CommonsUserXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/CommonsUser'>
    <Chip id='ok' text='OK'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src switch
            {
                "test/Inst" => Xml,
                "test/Chip" => ChipXml,
                "test/Importing" => ImportingXml,
                "test/CommonsUser" => CommonsUserXml,
                _ => null,
            });
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void EnsureLoaded_then_OpenModalScreen_twice_yields_two_distinct_screens()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();

            var (s1, k1) = UI.OpenModalScreen("test/Inst");
            var (s2, k2) = UI.OpenModalScreen("test/Inst");

            Assert.AreNotSame(s1, s2, "同一份 XML 应能实例化出两份独立 Screen");
            Assert.AreNotEqual(k1, k2, "instance key 必须唯一");
            Assert.IsNotNull(s1.RootGameObject);
            Assert.IsNotNull(s2.RootGameObject);

            UI.CloseModalScreen(k1);
            UI.CloseModalScreen(k2);
            Assert.IsNull(UI.Get(k1));
            Assert.IsNull(UI.Get(k2));
        }

        [Test]
        public void EnsureLoaded_is_idempotent()
        {
            ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult();
            Assert.DoesNotThrow(() =>
                ModalDocCache.EnsureLoaded("test/Inst").GetAwaiter().GetResult(),
                "第二次 EnsureLoaded 不应重复 LoadDocument 而抛 already loaded");
        }

        // 模态 XML 走 LoadDocumentWithCommonsAsync：<Import> 链经 SourceResolver 解析，
        // 引进来的模板在模态里可直接当标签用（旧同步 LoadDocument 只 Parse+Expand，会报 unregistered tag）
        [Test]
        public void EnsureLoaded_resolves_Import_chain_so_modal_xml_can_use_imported_templates()
        {
            ModalDocCache.EnsureLoaded("test/Importing").GetAwaiter().GetResult();

            var (screen, key) = UI.OpenModalScreen("test/Importing");
            PromptUGUI.Controls.Btn ok = null;
            Assert.DoesNotThrow(() => ok = screen.Get<PromptUGUI.Controls.Btn>("ok"),
                "<Chip> 应按 Import 进来的模板展开成 <Btn>，且调用点 id 转到模板根");
            Assert.IsNotNull(ok);
            UI.CloseModalScreen(key);
        }

        // 同一条路径也合并 commons 池：项目级共享模板（如 ModalFrame / ModalBtn）在模态皮里可用
        [Test]
        public void EnsureLoaded_merges_commons_pool_so_modal_xml_can_use_shared_templates()
        {
            UI.LoadCommonLibraryAsync("test/Chip").GetAwaiter().GetResult();
            ModalDocCache.EnsureLoaded("test/CommonsUser").GetAwaiter().GetResult();

            var (screen, key) = UI.OpenModalScreen("test/CommonsUser");
            Assert.DoesNotThrow(() => screen.Get<PromptUGUI.Controls.Btn>("ok"),
                "commons 池里的 <Chip> 应在模态 XML 里展开");
            UI.CloseModalScreen(key);
        }
    }
}
