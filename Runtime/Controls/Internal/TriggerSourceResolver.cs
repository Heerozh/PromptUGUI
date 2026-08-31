using System;
using System.Collections.Generic;

namespace PromptUGUI.Controls.Internal
{
    internal static class TriggerSourceResolver
    {
        /// <summary>
        /// Resolves an <c>@id</c> reference lexically, nearest scope first (spec
        /// 2026-08-31-hug-reveal-flip-checked-design §4.3): the trigger's own scope, then each
        /// enclosing control's scope walking outward — which is where a Template instance's shared
        /// id table lives, so two invocations of one template never see each other's ids — and
        /// finally the Screen's top-level ids.
        ///
        /// <para>This is what lets a trigger point at a SIBLING (<c>&lt;Toggle id='hdr'/&gt;</c> next
        /// to a <c>&lt;Show on="checked@hdr"&gt;</c>), which the old subtree-only lookup could not
        /// express at all. Every reference that resolved before still resolves to the same control:
        /// the subtree is still consulted first.</para>
        /// </summary>
        public static IControl ResolveId(Trigger trigger, string id, string onLabel)
        {
            if (trigger.ScopedIds.TryGetValue(id, out var found)) return found;

            for (var scope = trigger.Parent; scope != null; scope = scope.Parent)
                if (scope.ScopedIds.TryGetValue(id, out found)) return found;

            var screen = PromptUGUI.Application.UI.OwnerScreenOf(trigger);
            if (screen != null && screen.TryGet(id, out found)) return found;

            throw new InvalidOperationException(
                $"<Trigger on=\"{onLabel}@{id}\"> in '{trigger.Id ?? trigger.GameObject.name}': id '{id}' not " +
                "found in the trigger's subtree, its enclosing template instance, or screen " +
                $"'{screen?.Name ?? "?"}'.");
        }

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
                // The subtree walk stays first (it reaches a Btn at any depth, which the scope
                // tables do not), then the lexical lookup takes over for siblings / screen ids.
                if (found.Count > 0) return found[0];
                var ctrl = ResolveId(trigger, sourceId, "click");
                return ctrl as Btn ?? throw new InvalidOperationException(
                    $"<Trigger on=\"click@{sourceId}\">: id '{sourceId}' is a " +
                    $"{ctrl.GetType().Name}, not a <Btn>. click requires a <Btn>.");
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
                var ctrl = ResolveId(trigger, sourceId, "hover-enter/hover-exit/press");
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

        /// <summary>
        /// 为 state-* 触发器查找作为状态源的 <see cref="IStateSource"/>。
        /// 与 click / hover / press 向下搜子树相反，state-* 默认向 <b>上</b> 找最近的 IStateSource 祖先
        /// （把 Trigger 当作"贴在 Btn / Tab / Toggle 上的反应器"）。
        /// </summary>
        /// <param name="trigger">触发器控件</param>
        /// <param name="sourceId">空 → 走 GameObject 树向上找最近的 IStateSource；非空 → 走 ScopedIds 精确查找 + 类型校验</param>
        public static IStateSource FindStateSource(Trigger trigger, string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                // includeInactive: the source ancestor exists regardless of whether its subtree is
                // currently shown — a state-* Show/Trigger on a TabBar-bound page that isn't the
                // initially-selected tab is SetActive(false) at Open, and the default active-only
                // walk would otherwise miss the Btn/Tab/Toggle ancestor and throw.
                var ancestor = trigger.GameObject.GetComponentInParent<IStateSource>(true);
                if (ancestor == null)
                    throw new InvalidOperationException(
                        $"<Trigger on=\"state-...\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                        "no <Btn>/<Tab>/<Toggle> ancestor found. Place it inside one, or use state-...@<id>.");
                return ancestor;
            }

            var ctrl = ResolveId(trigger, sourceId, "state-...");
            var src = ctrl.GameObject.GetComponent<IStateSource>();
            if (src == null)
                throw new InvalidOperationException(
                    $"<Trigger on=\"state-...@{sourceId}\">: id '{sourceId}' is a " +
                    $"{ctrl.GetType().Name}, not a state source. state-* triggers require a <Btn>/<Tab>/<Toggle>.");
            return src;
        }

        /// <summary>
        /// Finds the <see cref="TabMenu"/> an <c>expand</c> / <c>collapse</c> trigger listens to.
        /// Resolves <b>upward</b>, like <see cref="FindStateSource"/> and for the same reason: the
        /// natural place for one is on a row inside the menu it belongs to.
        /// </summary>
        /// <param name="trigger">触发器控件</param>
        /// <param name="sourceId">空 → 沿 GameObject 树向上找最近的 TabMenu；非空 → 走 ScopedIds 精确查找 + 类型校验</param>
        public static TabMenu FindTabMenu(Trigger trigger, string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                // includeInactive: a collapsed menu's popup — where these triggers live — is
                // switched off, and the default active-only walk would never reach the marker.
                var marker = trigger.GameObject.GetComponentInParent<TabMenuMarker>(true);
                if (marker == null || marker.Owner == null)
                    throw new InvalidOperationException(
                        $"<Trigger on=\"expand\"/\"collapse\"> in '{trigger.Id ?? trigger.GameObject.name}': " +
                        "no <TabMenu> ancestor found. Place it inside one, or use expand@<id>.");
                return marker.Owner;
            }

            var ctrl = ResolveId(trigger, sourceId, "expand/collapse");
            return ctrl as TabMenu ?? throw new InvalidOperationException(
                $"<Trigger on=\"expand@{sourceId}\">: id '{sourceId}' is a " +
                $"{ctrl.GetType().Name}, not a <TabMenu>. expand / collapse require a <TabMenu>.");
        }
    }
}
