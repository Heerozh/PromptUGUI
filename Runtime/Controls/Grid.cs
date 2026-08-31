using System;
using System.Globalization;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Grid : Control, Internal.IHugContent
    {
        private GridLayoutGroup _layout;

        // See VStack. GridLayoutGroup derives its preferred size from cellSize + the row count
        // implied by `columns`, so a hug axis here is a constant for a given child count.
        protected internal override bool SelfReportsContentSize => true;

        float Internal.IHugContent.ContentSize(int axis)
            => axis == 0 ? _layout.preferredWidth : _layout.preferredHeight;

        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<GridLayoutGroup>()
                      ?? GameObject.AddComponent<GridLayoutGroup>();
            _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        }

        [UIAttr, Preserve]
        public int Columns
        {
            set => _layout.constraintCount = value;
        }

        [UIAttr, Preserve]
        public string CellSize
        {
            set
            {
                var x = value.IndexOf('x');
                var w = float.Parse(value.Substring(0, x), CultureInfo.InvariantCulture);
                var h = float.Parse(value.Substring(x + 1), CultureInfo.InvariantCulture);
                _layout.cellSize = new Vector2(w, h);
            }
        }

        [UIAttr, Preserve]
        public string Spacing
        {
            set
            {
                var parts = value.Split(',');
                if (parts.Length == 1)
                {
                    var s = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    _layout.spacing = new Vector2(s, s);
                }
                else
                {
                    var v = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    var h = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    _layout.spacing = new Vector2(h, v);  // GridLayoutGroup.spacing is (x,y) = (horizontal, vertical)
                }
            }
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
