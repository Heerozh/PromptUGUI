using System;
using System.Globalization;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class VStack : Control
    {
        private VerticalLayoutGroup _layout;

        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<VerticalLayoutGroup>()
                      ?? GameObject.AddComponent<VerticalLayoutGroup>();
            // spec §6.5: 子节点 size/width/height 走 LayoutElement.preferredX + flexibleX=0。
            // childControl* 必须 true，LayoutElement 才生效；forceExpand* 必须 false，
            // 否则剩余空间会被均摊到固定尺寸子节点上把它撑大。
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
            // 默认水平居中：子节点窄于 VStack 时贴中线（pixel-art icon+label widget 的常见诉求）。
            // 顶部对齐保留"自顶向下堆叠"语义；要居中可写 childAlign="center"。
            _layout.childAlignment = TextAnchor.UpperCenter;
        }

        [UIAttr]
        public string ChildAlign
        {
            set => _layout.childAlignment = ParseChildAlign(value);
        }

        internal static TextAnchor ParseChildAlign(string s) => s switch
        {
            "upper-left" => TextAnchor.UpperLeft,
            "upper-center" => TextAnchor.UpperCenter,
            "upper-right" => TextAnchor.UpperRight,
            "middle-left" => TextAnchor.MiddleLeft,
            "middle-center" or "center" => TextAnchor.MiddleCenter,
            "middle-right" => TextAnchor.MiddleRight,
            "lower-left" => TextAnchor.LowerLeft,
            "lower-center" => TextAnchor.LowerCenter,
            "lower-right" => TextAnchor.LowerRight,
            _ => throw new ArgumentException(
                $"childAlign '{s}' must be 'upper|middle|lower-left|center|right' (or 'center' alias for middle-center)"),
        };

        [UIAttr]
        public float Spacing
        {
            set => _layout.spacing = value;
        }

        [UIAttr]
        public string Padding
        {
            set
            {
                ParseTRBL(value, out var t, out var r, out var b, out var l);
                _layout.padding = new RectOffset(l, r, t, b);
                // RectOffset 顺序: left, right, top, bottom
            }
        }

        internal static void ParseTRBL(string s, out int t, out int r, out int b, out int l)
        {
            t = r = b = l = 0;
            if (string.IsNullOrEmpty(s)) return;
            var parts = s.Split(',');
            var v = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                v[i] = (p == "_" || p == "") ? 0
                    : int.Parse(p, CultureInfo.InvariantCulture);
            }
            switch (parts.Length)
            {
                case 1: t = r = b = l = v[0]; return;
                case 2: t = b = v[0]; r = l = v[1]; return;
                case 4: t = v[0]; r = v[1]; b = v[2]; l = v[3]; return;
                default: throw new ArgumentException($"padding '{s}' must be 1/2/4 ints");
            }
        }
    }
}
