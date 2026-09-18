using System.Collections.Generic;

namespace PromptUGUI.Application
{
    internal sealed class DepGraph
    {
        /// <summary>
        /// Loaded common libraries: src → the namespace it was loaded under (null = bare names).
        /// Remembered here so <c>ReloadCommonLibraryAsync</c> re-installs a library exactly as it
        /// was loaded — the M4 v1 limitation of reloading under bare names is gone.
        /// </summary>
        public readonly Dictionary<string, string> CommonsSources = new();
        public readonly Dictionary<string, HashSet<string>> SrcToDeps = new();
        public readonly Dictionary<string, ScreenDep> ScreenDeps = new();

        public sealed class ScreenDep
        {
            public string EntrySrc;
            public HashSet<string> AllDeps;
        }

        public void Clear()
        {
            CommonsSources.Clear();
            SrcToDeps.Clear();
            ScreenDeps.Clear();
        }

        public bool IsCommons(string src) => CommonsSources.ContainsKey(src);

        public IEnumerable<string> ScreensDependingOn(string src)
        {
            foreach (var kv in ScreenDeps)
                if (kv.Value.AllDeps.Contains(src))
                    yield return kv.Key;
        }
    }
}
