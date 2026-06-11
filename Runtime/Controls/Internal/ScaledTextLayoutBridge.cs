using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// V/HStack 直接子节点的 &lt;Text scale=…&gt; 自动 wrapper 上的布局桥（spec STW-D6）。
    /// 把内层 TMP 的 min/preferred × s（s = 内层 localScale.x，由 Screen.ApplyScaleToNode
    /// 写入，scale 未解析时为 1 → 自动透传）报告给父 LayoutGroup，flexible 原样透传；
    /// layoutPriority=0 与 TMP 持平，被显式 LayoutElement（priority 1）逐属性压过。
    /// 量算时序依赖 uGUI 标准四段 pass：水平 set 定下 wrapper 宽 → 内层经放宽 anchors
    /// 被动跟到 W/s → 垂直输入 calc 时这里读 tmp.preferredHeight（按需在 W/s 宽下重算）。
    /// 包装后 TMP 自己的 MarkLayoutForRebuild 在 wrapper（无 ILayoutGroup）就停了，
    /// 到不了外层 LayoutGroup —— 所以这里订阅 TMP 文本变更事件替它上报（spec STW-D7）。
    /// </summary>
    internal sealed class ScaledTextLayoutBridge : UIBehaviour, ILayoutElement
    {
        private TMP_Text _tmp;
        private RectTransform _inner;

        internal void Configure(TMP_Text tmp, RectTransform inner)
        {
            _tmp = tmp;
            _inner = inner;
        }

        // scale 是 uniform（Screen 只写 (v,v,1)），读 .x 代表两轴均正确。
        private float S => _inner != null ? _inner.localScale.x : 1f;

        public float minWidth => _tmp != null ? _tmp.minWidth * S : 0f;
        public float preferredWidth => _tmp != null ? _tmp.preferredWidth * S : 0f;
        public float flexibleWidth => _tmp != null ? _tmp.flexibleWidth : -1f;
        // max 的"无上限"哨兵是负数——不能乘 S（-1×0.5=-0.5；S→0 时更会翻成"硬上限 0"）。
        public float maxWidth { get { if (_tmp == null) return -1f; var m = _tmp.maxWidth; return m < 0f ? m : m * S; } }
        public float minHeight => _tmp != null ? _tmp.minHeight * S : 0f;
        public float preferredHeight => _tmp != null ? _tmp.preferredHeight * S : 0f;
        public float flexibleHeight => _tmp != null ? _tmp.flexibleHeight : -1f;
        public float maxHeight { get { if (_tmp == null) return -1f; var m = _tmp.maxHeight; return m < 0f ? m : m * S; } }
        public int layoutPriority => 0;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        protected override void OnEnable()
        {
            base.OnEnable();
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            MarkParentForRebuild();
        }

        protected override void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            base.OnDisable();
        }

        private void OnTextChanged(Object obj)
        {
            if (!ReferenceEquals(obj, _tmp)) return;
            MarkParentForRebuild();
        }

        // wrapper 自身没有 ILayoutGroup，LayoutRebuilder.MarkLayoutForRebuild 会向上
        // walk 直到找到真正的 LayoutGroup——所以标记 wrapper 本身即可传播到外层组。
        internal void MarkParentForRebuild()
        {
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
