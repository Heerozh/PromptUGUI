using PromptUGUI.Application;
using PromptUGUI.IR;

namespace PromptUGUI.Variants
{
    /// <summary>
    /// 在解析任意属性时调用：先看 VariantOverrides 中按声明顺序"最后一个激活"的变体，
    /// 找到则返回其值；否则回退到基础 Attributes；都没有则返回 null。
    /// 这是 spec §8.3 的 last-active-wins 规则。
    /// </summary>
    public static class VariantResolver
    {
        public static string ResolveAttribute(
            ElementNode node, string attrName, VariantStore store)
        {

            if (node.VariantOverrides.TryGetValue(attrName, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (store.IsActive(list[i].Variant))
                        return list[i].Value;
                }
            }
            return node.Attributes.TryGetValue(attrName, out var v) ? v : null;
        }
    }
}
