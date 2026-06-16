using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 装"禁用态默认去色"：中和 Selectable 内置 disabledColor（保留 transition=ColorTint 的 hover/press，
    /// 但不让内置 disabledColor 在灰度下二次压暗+半透）、按 <see cref="StateSubtree"/> 收集子树 graphic、
    /// 装/复用 root 上的 <see cref="DisabledGrayscaleController"/>。仅在作者未声明任何 <c>disabled*</c>
    /// 时由 Btn/Tab/Toggle 调用。幂等（ReSolve 复用同一控制器）。
    /// </summary>
    internal static class DisabledGrayscaleInstaller
    {
        internal static void Install(GameObject root, Selectable selectable, IReadOnlyList<IControl> children)
        {
            var colors = selectable.colors;
            colors.disabledColor = Color.white;
            selectable.colors = colors;

            var graphics = StateSubtree.CollectGraphics(root, children);
            var controller = root.GetComponent<DisabledGrayscaleController>()
                             ?? root.AddComponent<DisabledGrayscaleController>();
            controller.Configure(graphics);
        }
    }
}
