using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class HStack : Control, Internal.IHugContent
    {
        private HorizontalLayoutGroup _layout;

        // See VStack: the group already publishes this node's content size to a parent group.
        protected internal override bool SelfReportsContentSize => true;

        float Internal.IHugContent.ContentSize(int axis)
            => axis == 0 ? _layout.preferredWidth : _layout.preferredHeight;

        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<HorizontalLayoutGroup>()
                      ?? GameObject.AddComponent<HorizontalLayoutGroup>();
            // 同 VStack：spec §6.5 路由前提。
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
            // 默认垂直居中：子节点矮于 HStack 时贴中线。左对齐保留"自左向右排"语义。
            _layout.childAlignment = TextAnchor.MiddleLeft;
        }

        [UIAttr, Preserve]
        public string ChildAlign
        {
            set => _layout.childAlignment = VStack.ParseChildAlign(value);
        }

        [UIAttr, Preserve]
        public float Spacing
        {
            set => _layout.spacing = value;
        }

        [UIAttr, Preserve]
        public string Padding
        {
            set
            {
                VStack.ParseTRBL(value, out var t, out var r, out var b, out var l);
                _layout.padding = new RectOffset(l, r, t, b);
            }
        }
    }
}
