using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;

namespace PromptUGUI.Controls
{
    public sealed class SafeArea : Control
    {
        private SafeAreaTracker _tracker;

        // SafeArea 拒绝 anchor / size / width / height / margin / pivot 属性
        // (SA-D7),但默认 anchor 必须是 stretch — 旧模型靠 tracker 写
        // safe-area 分数 anchor,新模型 tracker 不再写,所以 SafeArea 必须自己
        // 声明"我永远 stretch 到 parent",否则 Control 基类返回 top-left → 宽高 0。
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
