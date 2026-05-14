using System;
using System.Collections.Generic;

namespace PromptUGUI.Controls.Internal
{
    internal static class TriggerSourceResolver
    {
        /// <summary>
        /// 在 trigger 子树里查找一个 Btn 作为点击事件源。
        /// 必须在子树完全实例化之后调用（即 ControlAttributeApplier.Apply 已放到子树递归之后）。
        /// </summary>
        /// <param name="trigger">触发器控件</param>
        /// <param name="sourceId">非空 → 按 id（GameObject name）精确查找；空 → 子树里 unique Btn</param>
        public static Btn FindBtn(Trigger trigger, string sourceId)
        {
            var found = new List<Btn>();
            CollectBtns(trigger, sourceId, found);

            if (!string.IsNullOrEmpty(sourceId))
            {
                if (found.Count == 0)
                    throw new InvalidOperationException(
                        $"<Trigger on=\"click@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                        $"id '{sourceId}' not found in trigger subtree");
                return found[0];
            }

            if (found.Count == 0)
                throw new InvalidOperationException(
                    $"<Trigger on=\"click\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                    "no Btn found in subtree. Add a Btn or use on=\"manual\".");
            if (found.Count > 1)
                throw new InvalidOperationException(
                    $"<Trigger on=\"click\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                    $"ambiguous — found {found.Count} Btn descendants. " +
                    "Use on=\"click@<id>\" to disambiguate.");
            return found[0];
        }

        private static void CollectBtns(Control c, string idFilter, List<Btn> outList)
        {
            foreach (var child in c.Children)
            {
                if (child is Btn b)
                {
                    if (string.IsNullOrEmpty(idFilter) || b.Id == idFilter)
                        outList.Add(b);
                    // Btns are leaves — do not descend into their children
                }
                else if (child is Control childCtrl)
                    CollectBtns(childCtrl, idFilter, outList);
            }
        }
    }
}
