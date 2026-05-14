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

        /// <summary>
        /// 在 trigger 子树里查找 IPointerEventSource (Btn 或 Image) 用作 hover/press 事件源。
        /// </summary>
        /// <param name="trigger">触发器控件</param>
        /// <param name="sourceId">非空 → 走 ScopedIds 精确查找 + 类型校验；空 → 子树里 unique source</param>
        public static IPointerEventSource FindPointerSource(Trigger trigger, string sourceId)
        {
            if (!string.IsNullOrEmpty(sourceId))
            {
                if (!trigger.ScopedIds.TryGetValue(sourceId, out var ctrl))
                    throw new InvalidOperationException(
                        $"<Trigger on=\"...@{sourceId}\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                        $"id '{sourceId}' not found in trigger subtree scope");
                return ctrl as IPointerEventSource ?? throw new InvalidOperationException(
                    $"<Trigger on=\"...@{sourceId}\">: id '{sourceId}' is a " +
                    $"{ctrl.GetType().Name}, not supported as pointer event source. Use <Btn> or <Image>.");
            }

            var found = new List<IPointerEventSource>();
            CollectPointerSources(trigger, found);
            if (found.Count == 0)
                throw new InvalidOperationException(
                    $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
                    "no <Btn> or <Image> found in subtree. Add one or use ...@<id>.");
            if (found.Count > 1)
                throw new InvalidOperationException(
                    $"<Trigger> in '{trigger.Id ?? trigger.GameObject.name}': " +
                    $"ambiguous — found {found.Count} pointer-event-source descendants. " +
                    "Use on=\"...@<id>\" to disambiguate.");
            return found[0];
        }

        private static void CollectPointerSources(Control c, List<IPointerEventSource> outList)
        {
            foreach (var child in c.Children)
            {
                if (child is IPointerEventSource src)
                {
                    outList.Add(src);
                    // Source nodes (Btn / Image) are leaves for traversal — same rule as CollectBtns.
                }
                else if (child is Control childCtrl)
                    CollectPointerSources(childCtrl, outList);
            }
        }
    }
}
