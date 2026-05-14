using System;
using System.Collections.Generic;
using TMPro;

namespace PromptUGUI.Controls.Internal
{
    internal static class AnimationTargetResolver
    {
        /// <summary>
        /// Wrapper 子树内查找唯一 Text；多个 → 报错（要求 target= 指定）；零个 → 报错。
        /// </summary>
        public static TMP_Text FindTextInSubtree(Control wrapper)
        {
            var found = new List<Text>();
            Collect(wrapper, found);
            if (found.Count == 0)
                throw new InvalidOperationException(
                    "<Animation count=... or char-color=...>: no <Text> in subtree. " +
                    "Add a Text child or use target=\"@id\".");
            if (found.Count > 1)
                throw new InvalidOperationException(
                    $"<Animation>: ambiguous — {found.Count} Text descendants. Use target=\"@id\".");
            return found[0].TmpComponent;
        }

        private static void Collect(Control c, List<Text> outList)
        {
            foreach (var child in c.Children)
            {
                if (child is Text t) outList.Add(t);
                else if (child is Control cc) Collect(cc, outList);
            }
        }
    }
}
