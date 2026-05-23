using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class PromptUGUIDocumentHostTests
    {
        private const string Xml = @"<?xml version='1.0'?><PromptUGUI version='1'>
            <Screen name='Preview' reference='1080x1920'>
              <VStack id='root'>
                <Text id='label'>Hello</Text>
              </VStack>
            </Screen>
          </PromptUGUI>";

        [SetUp] public void Setup() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TextAsset MakeAsset(string xml, string name)
        {
            var a = new TextAsset(xml);
            a.name = name;
            return a;
        }

        [Test]
        public void Refresh_reparents_screen_root_under_host()
        {
            var hostGO = new GameObject("Host");
            try
            {
                var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
                host.xmlAsset = MakeAsset(Xml, "Preview");
                host.Refresh();

                Assert.AreEqual(1, hostGO.transform.childCount,
                    "host should adopt exactly one UI root child");
                var root = hostGO.transform.GetChild(0).gameObject;
                Assert.AreEqual("Preview", root.name);
                Assert.IsNotNull(root.GetComponent<Canvas>(),
                    "spawned UI root must carry the Screen's Canvas");
            }
            finally { Object.DestroyImmediate(hostGO); }
        }

        [Test]
        public void Refresh_replaces_previous_root_when_called_twice()
        {
            var hostGO = new GameObject("Host");
            try
            {
                var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
                host.xmlAsset = MakeAsset(Xml, "Preview");
                host.Refresh();
                host.Refresh();

                Assert.AreEqual(1, hostGO.transform.childCount,
                    "second Refresh must replace, not stack");
            }
            finally { Object.DestroyImmediate(hostGO); }
        }

        [Test]
        public void Refresh_marks_spawned_root_as_DontSave()
        {
            var hostGO = new GameObject("Host");
            try
            {
                var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
                host.xmlAsset = MakeAsset(Xml, "Preview");
                host.Refresh();

                var root = hostGO.transform.GetChild(0).gameObject;
                var flags = root.hideFlags;
                Assert.IsTrue((flags & HideFlags.DontSaveInEditor) != 0,
                    "spawned root must not leak into saved Scene");
                Assert.IsTrue((flags & HideFlags.DontSaveInBuild) != 0,
                    "spawned root must not leak into Player build");
            }
            finally { Object.DestroyImmediate(hostGO); }
        }

        [Test]
        public void Destroying_host_unloads_doc_so_it_can_be_reloaded()
        {
            var hostGO = new GameObject("Host");
            var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
            host.xmlAsset = MakeAsset(Xml, "Preview");
            host.Refresh();

            Object.DestroyImmediate(hostGO);

            Assert.DoesNotThrow(() => UI.LoadDocument("Preview", Xml),
                "after host destruction the screen must be unregistered " +
                "so a fresh LoadDocument does not collide");
        }

        [Test]
        public void Refresh_picks_first_screen_when_ScreenName_empty()
        {
            const string twoScreens = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='A'><Frame id='r'/></Screen>
                <Screen name='B'><Frame id='r'/></Screen>
              </PromptUGUI>";

            var hostGO = new GameObject("Host");
            try
            {
                var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
                host.xmlAsset = MakeAsset(twoScreens, "Multi");
                host.screenName = "";
                host.Refresh();

                Assert.AreEqual("A", hostGO.transform.GetChild(0).name);
            }
            finally { Object.DestroyImmediate(hostGO); }
        }

        [Test]
        public void Refresh_picks_named_screen_when_ScreenName_set()
        {
            const string twoScreens = @"<?xml version='1.0'?><PromptUGUI version='1'>
                <Screen name='A'><Frame id='r'/></Screen>
                <Screen name='B'><Frame id='r'/></Screen>
              </PromptUGUI>";

            var hostGO = new GameObject("Host");
            try
            {
                var host = hostGO.AddComponent<PromptUGUIDocumentHost>();
                host.xmlAsset = MakeAsset(twoScreens, "Multi");
                host.screenName = "B";
                host.Refresh();

                Assert.AreEqual("B", hostGO.transform.GetChild(0).name);
            }
            finally { Object.DestroyImmediate(hostGO); }
        }
    }
}
