using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 状态源（Btn/Tab/Toggle）子树的 Graphic 收集 + 剪枝规则的单一来源：跳过 <c>stateReact="false"</c>
    /// 子树与嵌套 <see cref="IStateSource"/> 子树（它们自管图形）。被 <see cref="StateTintInstaller"/>
    /// 的 <c>*Modulate</c> 扇出与 <see cref="DisabledGrayscaleInstaller"/> 的去色共用。
    /// </summary>
    internal static class StateSubtree
    {
        /// <summary>收集 root 子树内未被剪枝的 Graphic（含 targetGraphic 自身）。</summary>
        internal static List<Graphic> CollectGraphics(GameObject root, IReadOnlyList<IControl> children)
        {
            var blocked = new HashSet<GameObject>();
            foreach (var child in children)
                CollectBlocked(child as Control, blocked);

            var result = new List<Graphic>();
            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
                if (!blocked.Contains(g.gameObject))
                    result.Add(g);
            return result;
        }

        /// <summary>把 <c>stateReact="false"</c> 节点与嵌套 <see cref="IStateSource"/> 节点（连同其子树
        /// 全部 Graphic）加入 blocked 集。从 <see cref="StateTintInstaller"/> 迁来，逻辑不变。</summary>
        internal static void CollectBlocked(Control control, HashSet<GameObject> blocked)
        {
            if (control == null) return;
            var optedOut = !control.StateReact;
            var nestedSource = control.GameObject != null
                               && control.GameObject.GetComponent<IStateSource>() != null;
            if (optedOut || nestedSource)
            {
                if (control.GameObject != null)
                {
                    foreach (var g in control.GameObject.GetComponentsInChildren<Graphic>(includeInactive: true))
                        blocked.Add(g.gameObject);
                    blocked.Add(control.GameObject);
                }
                return;
            }

            foreach (var child in control.Children)
                CollectBlocked(child as Control, blocked);
        }
    }
}
