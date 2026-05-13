using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Controls
{
    public sealed class SafeArea : Control
    {
        private SafeAreaTracker _tracker;

        public override void OnAttached()
        {
            _tracker = GameObject.AddComponent<SafeAreaTracker>();
        }

        internal override void OnAfterApply()
        {
            // ApplyCommon 在初次实例化和 Variant ReSolve 时都会重写 anchorMin/Max,
            // tracker 必须立刻补一次，否则 ReSolve 后会有一帧 stretch-default 闪烁。
            if (_tracker != null) _tracker.Apply();
        }
    }
}
