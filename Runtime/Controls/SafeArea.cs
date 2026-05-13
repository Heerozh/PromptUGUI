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
    }
}
