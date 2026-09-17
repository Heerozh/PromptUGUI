using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 状态源（Btn/Tab/Toggle）子树的 Graphic 收集 + 剪枝规则的单一来源：跳过 <c>stateReact="false"</c>
    /// 子树与嵌套 <see cref="IStateSource"/> 子树（它们自管图形），也跳过 TMP 的子网格（见
    /// <see cref="IsTmpOwned"/>）。被 <see cref="StateTintInstaller"/> 的 <c>*Modulate</c> 扇出与
    /// <see cref="DisabledGrayscaleInstaller"/> 的去色共用。
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
                if (!blocked.Contains(g.gameObject) && !IsTmpOwned(g))
                    result.Add(g);
            return result;
        }

        /// <summary>
        /// TMP 自己生成、自己管理的 Graphic：<see cref="TMPro.TMP_SubMeshUI"/>——一段文字里用了回落字体 /
        /// 内联 sprite 时，TMP 在 <c>TMP_Text</c> 下建的子网格。它的顶点色来自父文本的生成（父 <c>color</c>
        /// 一改整段重生成，子网格跟着变），材质是 TMP 按引用计数管的 fallback material（父文本 disable 时
        /// 释放、归零即销毁）。**不能当普通 Graphic 对待**：读它的 <c>material</c> 会当场
        /// <c>new Material(m_sharedMaterial)</c> 克隆一份并把共享材质换成克隆（TMP 的 getter 语义）——
        /// 在父文本被藏起之后（Tab 切页）那份共享材质已被 TMP 销毁，这一读就是
        /// "The object of type 'Material' has been destroyed … Parameter name: source"；就算没死，克隆也让
        /// 每个子网格各拿一份实例、打破 TMP 的材质共享。往它身上写 <c>material</c>（灰度去色）更是把 SDF 字形
        /// 换成 UI-Grayscale shader——回落字形变白方块。所以颜色 / 材质一律只作用于父 TMP_Text。
        /// </summary>
        internal static bool IsTmpOwned(Graphic g) => g is TMPro.TMP_SubMeshUI;

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
