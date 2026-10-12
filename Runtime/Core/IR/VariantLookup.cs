using System;

namespace PromptUGUI.IR
{
    /// <summary>
    /// spec §8.3 的 last-active-wins：按声明顺序取「最后一个激活」的变体覆盖，没有则回退基础值，都没有返回 null。
    /// 纯 C#、只问一个 <c>isActive</c> 谓词 —— 运行时 <c>VariantResolver</c> 把 <c>VariantStore.IsActive</c> 传进来，
    /// lint 传它为某个目标屏幕假设的激活集（字号下限规则）。一份实现，两条路径。
    /// </summary>
    public static class VariantLookup
    {
        public static string Resolve(ElementNode node, string attrName, Func<string, bool> isActive)
            => Resolve(node, attrName, isActive, out _);

        /// <param name="variant">命中的变体名；取的是基础值（或什么都没取到）时为 null。</param>
        public static string Resolve(ElementNode node, string attrName, Func<string, bool> isActive,
                                     out string variant)
        {
            variant = null;
            if (node.VariantOverrides.TryGetValue(attrName, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (!isActive(list[i].Variant)) continue;
                    variant = list[i].Variant;
                    return list[i].Value;
                }
            }
            return node.Attributes.TryGetValue(attrName, out var v) ? v : null;
        }
    }
}
