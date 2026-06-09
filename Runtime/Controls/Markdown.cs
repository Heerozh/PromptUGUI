using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Markdown : Control
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;

        public override void OnAttached()
        {
            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = GameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.viewport = _viewport;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;
        }

        protected internal override Transform ChildHostTransform => _viewport;
    }
}
