using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Lifecycle
{
    public class StackChildWarningTests
    {

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Anchor_on_VStack_child_logs_warning()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex("anchor.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("d", @"<PromptUGUI version='1'>
                <Screen name='S'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='child' anchor='top-left'/>
                    </VStack>
                </Screen></PromptUGUI>");
            UI.Open("S");

            yield return null;
            UI.Close("S");
        }

        [UnityTest]
        public IEnumerator Margin_on_HStack_child_logs_warning()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex("margin.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("d2", @"<PromptUGUI version='1'>
                <Screen name='S2'>
                    <HStack id='h' anchor='center' size='200x100'>
                        <Image id='child' margin='8'/>
                    </HStack>
                </Screen></PromptUGUI>");
            UI.Open("S2");

            yield return null;
            UI.Close("S2");
        }

        [UnityTest]
        public IEnumerator No_warning_when_only_size_specified_inside_stack()
        {
            UI.LoadDocument("d3", @"<PromptUGUI version='1'>
                <Screen name='S3'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='child' size='100x40'/>
                    </VStack>
                </Screen></PromptUGUI>");
            UI.Open("S3");
            yield return null;
            UI.Close("S3");
        }

        [UnityTest]
        public IEnumerator Variant_anchor_on_VStack_child_logs_warning()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex("anchor.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("vw1", @"<PromptUGUI version='1'>
                <Screen name='VW1'>
                    <VStack id='v' anchor='center' size='200x200'>
                        <Image id='c' anchor.mobile='top-left'/>
                    </VStack>
                </Screen></PromptUGUI>");
            UI.Open("VW1");

            yield return null;
            UI.Close("VW1");
        }

        [UnityTest]
        public IEnumerator Variant_margin_on_HStack_child_logs_warning()
        {
            LogAssert.Expect(LogType.Warning,
                new Regex("margin.*ignored.*inside.*layout group", RegexOptions.IgnoreCase));

            UI.LoadDocument("vw2", @"<PromptUGUI version='1'>
                <Screen name='VW2'>
                    <HStack id='h' anchor='center' size='200x100'>
                        <Image id='c' margin.mobile='8'/>
                    </HStack>
                </Screen></PromptUGUI>");
            UI.Open("VW2");

            yield return null;
            UI.Close("VW2");
        }
    }
}
