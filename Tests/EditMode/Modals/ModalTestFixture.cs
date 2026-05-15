using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public abstract class ModalTestFixture
    {
        protected Dictionary<string, string> Files;

        protected const string MinimalMboxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Box1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='400x200'>
      <VStack anchor='stretch' margin='16' spacing='8'>
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

        [SetUp]
        public virtual void SetUp()
        {
            UI.ResetForTests();
            Files = new Dictionary<string, string> { ["test/Box1"] = MinimalMboxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(Files.TryGetValue(src, out var v) ? v : null);
            MessageBox.XmlSrc = "test/Box1";   // uncommented in Task 11
        }

        [TearDown]
        public virtual void TearDown() => UI.ResetForTests();
    }
}
