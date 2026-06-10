using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Router
{
    public class RouterNavigateTests
    {
        private static string Xml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
  <Btn id='ok'>OK</Btn>
</Screen></PromptUGUI>";

        // MessageBox.Bind requires text/title/ok/cancel/yes/no/close controls.
        private const string MboxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='mbox'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Text id='text'  fontSize='14'/>
        <Btn  id='ok'>OK</Btn>
        <Btn  id='cancel'>Cancel</Btn>
        <Btn  id='yes'>Yes</Btn>
        <Btn  id='no'>No</Btn>
        <Btn  id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = Xml("home"),
                ["profile"] = Xml("profile"),
                ["mbox"] = MboxXml,
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("profile", "profile", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Navigate_ParsesNameAndQuery()
        {
            string seen = null;
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home"), ["profile"] = Xml("profile") };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("profile", "profile", parent: "home", onEnter: (s, q) => seen = q["uid"]);

            UI.Router.Navigate("appid://profile?uid=123").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "profile" }, UI.Router.Chain.ToList());
            Assert.AreEqual("123", seen);
        }

        [Test]
        public void Navigate_SlashInName_NotSplitAsHierarchy()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string> { ["home"] = Xml("home"), ["f"] = Xml("f") };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("home/friend", "f", parent: "home");

            UI.Router.Navigate("appid://home/friend").GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "home", "home/friend" }, UI.Router.Chain.ToList());
        }

        [Test]
        public void Navigate_SchemeMismatch_Throws()
        {
            UI.Router.Scheme = "appid";
            Assert.ThrowsAsync<RouteException>(
                async () => await UI.Router.Navigate("other://profile"));
        }

        [Test]
        public void Navigate_NoSchemeConfigured_AcceptsAny()
        {
            UI.Router.Navigate("whatever://home").GetAwaiter().GetResult();
            Assert.AreEqual("home", UI.Router.Current);
        }

        [Test]
        public void Reconcile_ClosesAdHocModalFirst()
        {
            MessageBox.XmlSrc = "mbox";
            UI.Router.Open("home").GetAwaiter().GetResult();
            _ = MessageBox.Open("hi");
            Assert.IsTrue(UI.Modal.IsAnyOpen);
            UI.Router.Open("profile").GetAwaiter().GetResult();
            Assert.IsFalse(UI.Modal.IsAnyOpen);
            CollectionAssert.AreEqual(new[] { "home", "profile" }, UI.Router.Chain.ToList());
        }
    }
}
