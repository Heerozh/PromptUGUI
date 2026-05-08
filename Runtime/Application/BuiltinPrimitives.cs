using PromptUGUI.Controls;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class BuiltinPrimitives {
        public static void Register(ControlRegistry reg) {
            reg.Register<Frame>("Frame", null);
            reg.Register<Image>("Image", null);
            reg.Register<Icon>("Icon", null);
            reg.Register<Text>("Text", null, defaultTextAttr: "text");
            reg.Register<VStack>("VStack", null);
            reg.Register<HStack>("HStack", null);
            reg.Register<Grid>("Grid", null);
            reg.Register<Btn>("Btn", null);
        }
    }
}
