using NUnit.Framework;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Layout
{
    public class MaskPaddingParserTests
    {
        [Test]
        public void Empty_ReturnsZero()
        {
            Assert.AreEqual(Vector4.zero, MaskPaddingParser.Parse(""));
            Assert.AreEqual(Vector4.zero, MaskPaddingParser.Parse(null));
        }

        [Test]
        public void OneComponent_AppliesToAllFour()
        {
            // "8" → top=8, right=8, bottom=8, left=8 → Vector4(L,B,R,T) = (8,8,8,8)
            Assert.AreEqual(new Vector4(8f, 8f, 8f, 8f), MaskPaddingParser.Parse("8"));
        }

        [Test]
        public void TwoComponents_VerticalThenHorizontal()
        {
            // "4,6" → top=bottom=4, right=left=6 → Vector4(L,B,R,T) = (6,4,6,4)
            Assert.AreEqual(new Vector4(6f, 4f, 6f, 4f), MaskPaddingParser.Parse("4,6"));
        }

        [Test]
        public void FourComponents_TRBL_FlippedToLBRT()
        {
            // Author "T,R,B,L" = "1,2,3,4" → Vector4(L,B,R,T) = (4,3,2,1)
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), MaskPaddingParser.Parse("1,2,3,4"));
        }

        [Test]
        public void UnderscorePlaceholder_BecomesZero()
        {
            // "_,16,_,_" → T=0, R=16, B=0, L=0 → Vector4(L,B,R,T) = (0,0,16,0)
            Assert.AreEqual(new Vector4(0f, 0f, 16f, 0f), MaskPaddingParser.Parse("_,16,_,_"));
        }

        [Test]
        public void NegativeValues_AreAllowed()
        {
            // InputField 的 textArea 用了负 padding(-8,-5,-8,-5)
            Assert.AreEqual(new Vector4(-5f, -5f, -8f, -8f), MaskPaddingParser.Parse("-8,-8,-5,-5"));
        }

        [Test]
        public void ThreeComponents_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => MaskPaddingParser.Parse("1,2,3"));
        }
    }
}
