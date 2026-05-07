using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests {
    public class PlayModeSmokeTests {
        [UnityTest]
        public IEnumerator PlayMode_assembly_loads() {
            yield return null;
            Assert.Pass();
        }
    }
}
