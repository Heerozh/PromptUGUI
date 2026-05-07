using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Grid = PromptUGUI.Controls.Grid;

namespace PromptUGUI.Tests.Controls {
    public class GridTests {
        [UnityTest]
        public IEnumerator Adds_GridLayoutGroup_with_fixed_column_count() {
            var g = new Grid();
            var go = new GameObject("grid", typeof(RectTransform));
            g.AttachTo(go);
            g.Columns = 6;
            var lg = go.GetComponent<GridLayoutGroup>();
            Assert.IsNotNull(lg);
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, lg.constraint);
            Assert.AreEqual(6, lg.constraintCount);
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CellSize_writes_WxH() {
            var g = new Grid();
            var go = new GameObject("grid", typeof(RectTransform));
            g.AttachTo(go);
            g.CellSize = "64x64";
            var lg = go.GetComponent<GridLayoutGroup>();
            Assert.AreEqual(new Vector2(64, 64), lg.cellSize);
            Object.Destroy(go);
            yield return null;
        }
    }
}
