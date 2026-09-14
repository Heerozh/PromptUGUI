using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <c>PromptUGUISettings.Instance</c> 只扫一次（<c>Resources.FindObjectsOfTypeAll</c> 是全内存对象扫描，
    /// 宿主星图加载后每次 ~0.4 ms，而每个 Text 的字体应用都要拿一次 settings——一张面板 243 次，
    /// 占打开耗时的 28%）；缓存的实例被销毁 / 卸载（Unity 假 null）或显式 <c>ResetInstanceCache</c> 后重扫。
    /// </summary>
    public class PromptUGUISettingsInstanceTests
    {
        private PromptUGUISettings _a;
        private int _scans;

        [SetUp]
        public void SetUp()
        {
            PromptUGUISettings.ResetInstanceCache();
            _a = ScriptableObject.CreateInstance<PromptUGUISettings>();
            _scans = 0;
            PromptUGUISettings.FinderForTests = () => { _scans++; return new[] { _a }; };
        }

        [TearDown]
        public void TearDown()
        {
            PromptUGUISettings.FinderForTests = null;
            PromptUGUISettings.ResetInstanceCache();
            if (_a != null) Object.DestroyImmediate(_a);
        }

        [Test]
        public void Instance_scans_once_and_reuses()
        {
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            Assert.AreEqual(1, _scans, "三次访问只该扫一次");
        }

        [Test]
        public void Reset_forces_a_rescan()
        {
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            PromptUGUISettings.ResetInstanceCache();
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            Assert.AreEqual(2, _scans);
        }

        [Test]
        public void Destroyed_instance_is_rescanned()
        {
            Assert.AreSame(_a, PromptUGUISettings.Instance);
            Object.DestroyImmediate(_a);
            var b = ScriptableObject.CreateInstance<PromptUGUISettings>();
            try
            {
                PromptUGUISettings.FinderForTests = () => { _scans++; return new[] { b }; };
                Assert.AreSame(b, PromptUGUISettings.Instance, "缓存的实例成了假 null，必须重扫");
                Assert.AreEqual(2, _scans);
            }
            finally
            {
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Missing_settings_is_not_cached_as_null()
        {
            PromptUGUISettings.FinderForTests = () => { _scans++; return new PromptUGUISettings[0]; };
            Assert.IsNull(PromptUGUISettings.Instance);
            Assert.IsNull(PromptUGUISettings.Instance);
            Assert.AreEqual(2, _scans, "没找到不做负缓存：资产晚点才加载的场合下次要能找到");
        }
    }
}
