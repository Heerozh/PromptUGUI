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
        }

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
                v[i] = int.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
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
