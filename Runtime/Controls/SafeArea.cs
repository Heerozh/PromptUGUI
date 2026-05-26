using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;

namespace PromptUGUI.Controls
{
    public sealed class SafeArea : Control
    {
        private SafeAreaTracker _tracker;

        // SafeArea 默认 anchor 必须是 stretch — 旧模型靠 tracker 写
        // safe-area 分数 anchor,新模型 tracker 在 stretch 框架下用 offsetMin/Max
        // 表达 safe-area,所以 SafeArea 必须自己声明"永远 stretch 到 parent",
        // 否则 Control 基类返回 top-left → ApplyCommon 写出 sizeDelta=(0,0) →
        // Inspector 里宽高全 0。SA-D7 仍拒绝 anchor / size / width / height /
        // pivot 等几何 override,但 margin 由 v2 absorb 语义接管(参考 OnAfterApply)。
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Stretch, AnchorHorizontal.Stretch);

        public override void OnAttached()
        {
            _tracker = GameObject.AddComponent<SafeAreaTracker>();
        }

        internal override void OnAfterApply()
        {
            // ApplyCommon 在初次实例化和 Variant ReSolve 时都会把 anchor 写回 stretch、
            // 把 offsetMin/Max 写成纯 design margin。tracker 在这里 snapshot 设计 margin，
            // 再用 device safe-area inset 做 per-edge max-blend 写回 offsets。
            if (_tracker == null) return;
            _tracker.CaptureDesignMargin(RectTransform);
            _tracker.Apply();
        }
    }
}
