using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ProgressAttributeRulesTests
    {
        private static ElementNode N(params (string k, string v)[] attrs)
        {
            var n = new ElementNode("Progress") { Id = "p" };
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        [Test]
        public void Clean_Progress_No_Issues()
        {
            var n = N(("value", "0.5"), ("fill", "ui:bar"));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(n));
        }

        // ===== value range =====

        [Test]
        public void Value_Below_Zero_ValueRange_Warning()
        {
            var n = N(("value", "-0.1"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ValueRangeCode, issues[0].Code);
        }

        [Test]
        public void Value_Above_One_ValueRange_Warning()
        {
            var n = N(("value", "1.5"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ValueRangeCode, issues[0].Code);
        }

        [Test]
        public void Value_Non_Numeric_No_ValueRange_Issue()
        {
            // Dynamic binding sources (e.g. "{state.hp}") parse as non-numeric and must
            // be ignored — lint can only judge literals.
            var n = N(("value", "{state.hp}"), ("fill", "ui:bar"));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(n));
        }

        // ===== mode =====

        [Test]
        public void Mode_Bogus_ModeCode_Error()
        {
            var n = N(("mode", "radial"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ModeCode, issues[0].Code);
        }

        [Test]
        public void Mode_Scale_And_Fill_Both_OK()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("mode", "scale"), ("fill", "ui:bar"))));
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("mode", "fill"), ("fill", "ui:bar"))));
        }

        // ===== direction =====

        [Test]
        public void Direction_Bogus_DirectionCode_Error()
        {
            var n = N(("direction", "diagonal"), ("fill", "ui:bar"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.DirectionCode, issues[0].Code);
        }

        [Test]
        public void Direction_All_Four_Values_OK()
        {
            foreach (var d in new[] { "horizontal", "vertical", "reverse-horizontal", "reverse-vertical" })
                Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("direction", d), ("fill", "ui:bar"))),
                    $"direction='{d}'");
        }

        // ===== children =====

        [Test]
        public void Children_ChildrenCode_Error()
        {
            var n = N(("fill", "ui:bar"));
            n.Children.Add(new ElementNode("Image"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.ChildrenCode, issues[0].Code);
        }

        // ===== mask variant =====

        [Test]
        public void Mask_In_Variant_Override_MaskVariantCode_Error()
        {
            var n = N(("fill", "ui:bar"));
            n.VariantOverrides["mask"] =
                new List<(string Variant, string Value)> { ("mobile", "ui:pill") };
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.MaskVariantCode, issues[0].Code);
        }

        // ===== no fill =====

        [Test]
        public void Value_Set_But_No_Fill_Or_FillColor_NoFillCode_Warning()
        {
            var n = N(("value", "0.5"));
            var issues = ProgressAttributeRules.CheckProgress(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ProgressAttributeRules.NoFillCode, issues[0].Code);
        }

        [Test]
        public void Value_Plus_Fill_No_NoFill_Issue()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("value", "0.5"), ("fill", "ui:bar"))));
        }

        [Test]
        public void Value_Plus_FillColor_No_NoFill_Issue()
        {
            Assert.IsEmpty(ProgressAttributeRules.CheckProgress(N(("value", "0.5"), ("fillColor", "#f00"))));
        }
    }
}
