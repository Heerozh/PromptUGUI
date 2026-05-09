using System.Collections.Generic;

namespace PromptUGUI.Application
{
    internal sealed class DepGraph
    {
        public readonly HashSet<string> CommonsSources = new();
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

        public bool IsCommons(string src) => CommonsSources.Contains(src);

        public IEnumerable<string> ScreensDependingOn(string src)
        {
            foreach (var kv in ScreenDeps)
                if (kv.Value.AllDeps.Contains(src))
                    yield return kv.Key;
        }
    }
}
