using System;

namespace PromptUGUI.IR
{
    /// <summary>
    /// Identity of a <c>&lt;Template&gt;</c> after Import merging: the declaring library's
    /// <c>as=</c> namespace plus the template name. Authors write the namespaced form with a dot
    /// (<c>&lt;ui.TitledPanel/&gt;</c>) because it occupies a tag position.
    /// </summary>
    internal readonly struct TemplateKey : IEquatable<TemplateKey>
    {
        public readonly string Namespace;
        public readonly string Name;
        public TemplateKey(string ns, string name) { Namespace = ns; Name = name; }
        public bool Equals(TemplateKey o) => Namespace == o.Namespace && Name == o.Name;
        public override bool Equals(object o) => o is TemplateKey k && Equals(k);
        public override int GetHashCode() =>
            (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
        public override string ToString() =>
            Namespace == null ? Name : $"{Namespace}.{Name}";
    }

    /// <summary>
    /// Mirrors <see cref="TemplateKey"/> for <c>&lt;Style&gt;</c>. Namespaced references are
    /// written <c>class="ui:card"</c> — a colon, not the dot templates use for tags, matching
    /// the <c>Set:Name</c> form authors already write for sprite / icon references.
    /// </summary>
    internal readonly struct StyleKey : IEquatable<StyleKey>
    {
        public const char NamespaceSeparator = ':';

        public readonly string Namespace;
        public readonly string Name;
        public StyleKey(string ns, string name) { Namespace = ns; Name = name; }
        public bool Equals(StyleKey o) => Namespace == o.Namespace && Name == o.Name;
        public override bool Equals(object o) => o is StyleKey k && Equals(k);
        public override int GetHashCode() =>
            (Namespace?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0);
        public override string ToString() =>
            Namespace == null ? Name : $"{Namespace}{NamespaceSeparator}{Name}";

        /// <summary>Splits an author-written <c>class</c> entry into (namespace, name).</summary>
        public static StyleKey ParseReference(string reference)
        {
            var sep = reference.IndexOf(NamespaceSeparator);
            return sep < 0
                ? new StyleKey(null, reference)
                : new StyleKey(reference.Substring(0, sep), reference.Substring(sep + 1));
        }
    }
}
