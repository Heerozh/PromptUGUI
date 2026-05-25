using System;

namespace PromptUGUI.Registry
{
    /// <summary>
    /// Mono.Linker / IL2CPP managed stripping 按 type name "PreserveAttribute" 精确匹配
    /// (不论命名空间, 但**继承不算** — 子类必须自己叫 PreserveAttribute), 挂着它的成员会
    /// 被保留 metadata。这避免了 Medium+ stripping 把 setter-only / 无外部直接调用方的
    /// [UIAttr] property 整个 PropertyInfo 剥离, 导致 ControlMeta.Build 反射
    /// GetProperties() 漏掉 → attribute 静默失效。所有内置 Control 的 [UIAttr] / [Bind]
    /// 都成对挂 [Preserve]; 用户自定义 Control 也应如此。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
    public sealed class PreserveAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UIAttrAttribute : Attribute
    {
        public string Name { get; }
        /// <summary>
        /// Optional XSD pattern (regex) for value validation. Uses XSD pattern syntax —
        /// implicitly anchored to the entire value, so do NOT include `^` or `$`
        /// (they're treated as literal characters). Prefer ASCII char classes
        /// (`[A-Za-z0-9_-]`) over `\w` to match runtime parser behavior.
        /// </summary>
        public string Pattern { get; set; }

        public UIAttrAttribute(string name = null) { Name = name; }
    }
}
