using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.Modals
{
    /// <summary>
    /// <c>MessageBox.Open(icon:)</c> writes <c>Icon.Name</c> from code in <c>Bind</c>; an
    /// orientation flip (a Variant ReSolve) used to replay the XML's <c>name=</c> over it (spec
    /// 2026-09-17-common-attr-runtime-state-design §1).
    /// </summary>
    public class MessageBoxIconRuntimeStateTests : ModalTestFixture
    {
        private Sprite _a, _b;

        public override void SetUp()
        {
            base.SetUp();
            _a = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            _b = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            UI.SpriteResolver = key => key == "ui:a" ? _a : key == "ui:b" ? _b : null;
            Files["test/Box1"] = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Box1'>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Icon id='icon' name='ui:a' size='24x24'/>
        <Text id='title' fontSize='20'/>
        <Text id='text'  fontSize='14'/>
        <Btn  id='ok'>OK</Btn>
        <Btn  id='cancel'>Cancel</Btn>
        <Btn  id='yes'>Yes</Btn>
        <Btn  id='no'>No</Btn>
        <Btn  id='close'>Close</Btn>
      </VStack>
    </Frame>
  </Screen>
</PromptUGUI>";
        }

        [Test]
        public void MessageBox_icon_survives_orientation_flip()
        {
            var task = MessageBox.Open("hello", MsgBtn.OK, icon: "ui:b");
            var icon = UI.Modal.TopScreen.Get<PromptUGUI.Controls.Icon>("icon").GameObject.GetComponent<UnityImage>();
            Assume.That(icon.sprite, Is.SameAs(_b), "guard: Bind wrote the requested icon");

            UI.Variants.Set("portrait", true);

            Assert.AreSame(_b, icon.sprite, "the code-written icon must not revert to the XML default");
            UI.Modal.TopScreen.Get<PromptUGUI.Controls.Btn>("ok").SimulateClick();
            Assert.AreEqual(MsgBtn.OK, task.GetAwaiter().GetResult());
        }
    }
}
