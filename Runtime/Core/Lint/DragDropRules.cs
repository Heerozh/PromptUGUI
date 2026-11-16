using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// The value grammar of the five drag-and-drop attributes (spec 2026-11-16-drag-drop §5.1) and the
    /// lint codes that report them. Pure C#: the runtime setters parse through these same functions, so
    /// the CLI and a running Screen accept and refuse exactly the same spellings.
    /// </summary>
    public static class DragDropRules
    {
        public const string ValueCode = "PUI-DRAG-VALUE";
        public const string HandleCode = "PUI-DRAG-HANDLE-ID";
        public const string ReorderCode = "PUI-DRAG-REORDER";
        public const string VirtualCode = "PUI-DRAG-VIRTUAL";
        public const string CarouselCardCode = "PUI-DRAG-CAROUSEL-CARD";
        public const string NoTargetCode = "PUI-DROP-NO-SOURCE";

        /// <summary>The attributes every element takes, and a template invocation carries onto its root.</summary>
        public static readonly string[] Attributes = { "drag", "drop", "dragHold", "dragHandle", "dragDuration" };

        /// <summary>
        /// Whether <paramref name="n"/> writes <paramref name="attr"/> at all — base or any variant.
        /// Declared, not resolved: a source switched on only in portrait still has to exist from the start,
        /// because the hooks inside it resolve while the tree is being built.
        /// </summary>
        public static bool Declares(ElementNode n, string attr)
            => n != null && (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr));

        /// <summary>
        /// <c>drag=</c>: a kind name, or <c>none</c> / empty for off (<paramref name="kind"/> null). False
        /// with an <paramref name="error"/> for anything else — <c>true</c> / <c>false</c> included: the
        /// attribute names what is being carried, it is not a switch.
        /// </summary>
        public static bool TryParseKind(string value, out string kind, out string error)
        {
            kind = null;
            error = null;
            if (value == null) return true;
            var v = value.Trim();
            if (v.Length == 0 || v == "none") return true;
            if (v == "true" || v == "false")
            {
                error = "takes the kind of thing being dragged (drag=\"unit\"), not a switch — \"none\" turns it off";
                return false;
            }
            if (!IsKind(v))
            {
                error = "is not a kind name — letters, digits, '_' and '-', starting with a letter or '_'";
                return false;
            }
            kind = v;
            return true;
        }

        /// <summary>
        /// <c>drop=</c>: kind names separated by spaces or commas, <c>*</c> for any kind, or <c>none</c> /
        /// empty for off (<paramref name="kinds"/> null and <paramref name="any"/> false).
        /// </summary>
        public static bool TryParseDropKinds(string value, out string[] kinds, out bool any, out string error)
        {
            kinds = null;
            any = false;
            error = null;
            if (value == null) return true;
            var v = value.Trim();
            if (v.Length == 0 || v == "none") return true;
            var list = new List<string>();
            foreach (var part in v.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "*")
                {
                    any = true;
                    continue;
                }
                if (!IsKind(part) || part == "true" || part == "false" || part == "none")
                {
                    error = $"'{part}' is not a kind name — list kinds (drop=\"unit item\"), or '*' for any";
                    return false;
                }
                if (!list.Contains(part)) list.Add(part);
            }
            kinds = list.Count > 0 ? list.ToArray() : null;
            return true;
        }

        /// <summary><c>dragHold</c> (<c>auto</c> or a duration) / <c>dragDuration</c> (a duration).</summary>
        public static bool IsDuration(string value, bool allowAuto) => ScrollListRules.IsDuration(value, allowAuto);

        internal static bool IsKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var c0 = s[0];
            if (!(char.IsLetter(c0) || c0 == '_')) return false;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }
    }
}
