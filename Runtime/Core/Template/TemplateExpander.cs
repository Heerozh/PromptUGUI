using PromptUGUI.IR;

namespace PromptUGUI.Template {
    public static class TemplateExpander {
        public static UIDocument Expand(UIDocument doc) {
            var result = new UIDocument { Version = doc.Version };
            // Templates 不参与下游 instantiation，但保留以便诊断
            foreach (var kv in doc.Templates)
                result.Templates[kv.Key] = kv.Value;

            foreach (var s in doc.Screens) {
                var newRoot = CloneNode(s.Root);
                result.Screens.Add(new ScreenDef(s.Name, newRoot));
            }
            return result;
        }

        static ElementNode CloneNode(ElementNode src) {
            var dst = new ElementNode(src.Tag) {
                Id = src.Id,
                TextContent = src.TextContent,
                IsTemplateInstanceRoot = src.IsTemplateInstanceRoot,
            };
            foreach (var kv in src.Attributes)
                dst.Attributes[kv.Key] = kv.Value;
            foreach (var c in src.Children)
                dst.Children.Add(CloneNode(c));
            return dst;
        }
    }
}
