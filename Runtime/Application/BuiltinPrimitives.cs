using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Application
{
    internal static class BuiltinPrimitives
    {
        public static void Register(ControlRegistry reg)
        {
            reg.Register<Frame>("Frame", null);
            reg.Register<Image>("Image", null);
            reg.Register<Icon>("Icon", null);
            reg.Register<Text>("Text", null, defaultTextAttr: "text");
            reg.Register<VStack>("VStack", null);
            reg.Register<HStack>("HStack", null);
            reg.Register<Grid>("Grid", null);
            reg.Register<Btn>("Btn", null, defaultTextAttr: "text");
            reg.Register<Toggle>("Toggle", null, defaultTextAttr: "text");
            reg.Register<Slider>("Slider", null);
        }
    }
}
