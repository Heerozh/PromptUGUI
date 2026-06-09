using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Application
{
    internal static class BuiltinPrimitives
    {
        public static void Register(ControlRegistry reg)
        {
            reg.Register<Frame>("Frame", null);
            reg.Register<SafeArea>("SafeArea", null);
            reg.Register<Trigger>("Trigger", null);
            reg.Register<Show>("Show", null);
            reg.Register<Animation>("Animation", null);
            reg.Register<Image>("Image", null);
            reg.Register<RawImage>("RawImage", null);
            reg.Register<Icon>("Icon", null);
            reg.Register<Text>("Text", null, defaultTextAttr: "text");
            reg.Register<VStack>("VStack", null);
            reg.Register<HStack>("HStack", null);
            reg.Register<Grid>("Grid", null);
            reg.Register<Btn>("Btn", null, defaultTextAttr: "text");
            reg.Register<Toggle>("Toggle", null, defaultTextAttr: "text", runtimeStateAttr: "isOn");
            reg.Register<Tab>("Tab", null, defaultTextAttr: "text", runtimeStateAttr: "isOn");
            reg.Register<TabBar>("TabBar", null);
            reg.Register<Slider>("Slider", null, runtimeStateAttr: "value");
            reg.Register<Progress>("Progress", null, runtimeStateAttr: "value");
            reg.Register<Dropdown>("Dropdown", null, runtimeStateAttr: "value");
            reg.Register<ScrollList>("ScrollList", null);
            reg.Register<InputField>("InputField", null, defaultTextAttr: "text");
            reg.Register<Carousel>("Carousel", null, runtimeStateAttr: "current");
            reg.Register<Markdown>("Markdown", null, defaultTextAttr: "text");
        }
    }
}
