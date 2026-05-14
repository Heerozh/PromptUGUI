using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class Animation : Trigger
    {
        private RectTransform _offsetProxy;

        protected internal override Transform ChildHostTransform => _offsetProxy;

        public override void OnAttached()
        {
            var go = new GameObject("_offsetProxy", typeof(RectTransform));
            go.transform.SetParent(RectTransform, worldPositionStays: false);
            _offsetProxy = (RectTransform)go.transform;
            _offsetProxy.anchorMin = Vector2.zero;
            _offsetProxy.anchorMax = Vector2.one;
            _offsetProxy.offsetMin = Vector2.zero;
            _offsetProxy.offsetMax = Vector2.zero;
            _offsetProxy.pivot = new Vector2(0.5f, 0.5f);
        }

        // Animation-specific [UIAttr]s and OnTriggerFired added in Task 8.
    }
}
