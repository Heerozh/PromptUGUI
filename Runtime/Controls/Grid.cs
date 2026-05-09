using System;
using System.Globalization;
using PromptUGUI.Registry;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Grid : Control
    {
        private GridLayoutGroup _layout;

        public override void OnAttached()
        {
            _layout = GameObject.GetComponent<GridLayoutGroup>()
                      ?? GameObject.AddComponent<GridLayoutGroup>();
            _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        }

        [UIAttr]
        public int Columns
        {
            set => _layout.constraintCount = value;
        }

        [UIAttr]
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

        [UIAttr]
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

        [UIAttr]
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
