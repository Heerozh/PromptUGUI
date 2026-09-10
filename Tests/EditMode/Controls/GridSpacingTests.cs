using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.UI;
using UGrid = PromptUGUI.Controls.Grid;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Pins the WRITTEN ORDER of the two-part <c>&lt;Grid spacing="V,H"&gt;</c> form (SGS-D1).
    /// The order is vertical-first, matching the two-part <c>padding</c> ("V,H") and the four-part
    /// <c>margin</c> ("T,R,B,L") — every multi-part value in this library leads with the vertical
    /// axis. It was previously untested and the skill doc claimed "H,V", so these tests exist to
    /// stop a future reader from "fixing" the code to match a wrong doc.
    /// </summary>
    public class GridSpacingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static GridLayoutGroup OpenGrid(string spacing)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + $"<Grid id='g' anchor='top-left' width='200' height='200' columns='2' cellSize='40x40' spacing='{spacing}'>"
                    + "<Frame/><Frame/></Grid></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<UGrid>("g").GameObject.GetComponent<GridLayoutGroup>();
        }

        [Test]
        public void Two_part_spacing_is_vertical_then_horizontal()
        {
            var layout = OpenGrid("10,20");

            // GridLayoutGroup.spacing is a Vector2 of (x = horizontal, y = vertical).
            Assert.AreEqual(20f, layout.spacing.x, 0.001f, "parts[1] is the HORIZONTAL gap");
            Assert.AreEqual(10f, layout.spacing.y, 0.001f, "parts[0] is the VERTICAL gap");
        }

        [Test]
        public void Single_value_spacing_applies_to_both_axes()
        {
            var layout = OpenGrid("6");

            Assert.AreEqual(6f, layout.spacing.x, 0.001f);
            Assert.AreEqual(6f, layout.spacing.y, 0.001f);
        }
    }
}
