using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Fit-mode lint rules for <c>&lt;Image type="cover"/"contain"&gt;</c>.
    /// FIT-VARIANT is shared by <c>IRWalker</c> (CLI) + <c>ScreenInstantiator</c> (runtime warning),
    /// like the mask rules — a fit value in a variant adds an AspectRatioFitter that can't be torn
    /// down when the variant turns off (ControlAttributeApplier skips null-resolving setters).
    /// FIT-GEOMETRY is CLI-only (a static authoring nit with no runtime effect), like PUI-MARGIN-INERT-SIDE.
    /// Single source of truth shared with the runtime warning path.
    /// </summary>
    public static class ImageFitRules
    {
        public const string VariantCode = "PUI-IMAGE-FIT-VARIANT";
        public const string GeometryCode = "PUI-IMAGE-FIT-GEOMETRY";

        private static readonly string[] GeometryAttrs = { "anchor", "size", "width", "height", "margin" };

        private static bool IsFit(string v) => v == "cover" || v == "contain";

        /// <summary>Runtime + CLI: a fit value (cover/contain) inside a type.&lt;variant&gt; override.</summary>
        public static IEnumerable<LintIssue> CheckVariant(ElementNode n)
        {
            if (n.VariantOverrides.TryGetValue("type", out var overrides))
            {
                foreach (var (variant, value) in overrides)
                {
                    if (IsFit(value))
                    {
                        yield return new LintIssue(
                            VariantCode, n.Tag, n.Id,
                            $"<Image id='{n.Id}'>: type=\"{value}\" in a variant override (type.{variant}) is not " +
                            "supported in v1. Switching to/from a fit mode adds/removes an AspectRatioFitter, which " +
                            "can't be torn down when the variant turns off. Use a fixed base type=, or split into " +
                            "per-orientation Screens / <Add into=...>.");
                        yield break; // one issue per Image
                    }
                }
            }
        }

        /// <summary>CLI-only: own anchor/size/width/height/margin under a fit mode (overridden by ARF).</summary>
        public static IEnumerable<LintIssue> CheckGeometry(ElementNode n)
        {
            if (!n.Attributes.TryGetValue("type", out var type) || !IsFit(type))
                yield break;

            var offenders = new List<string>();
            foreach (var attr in GeometryAttrs)
                if (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr))
                    offenders.Add(attr);

            if (offenders.Count > 0)
                yield return new LintIssue(
                    GeometryCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: {string.Join(", ", offenders)} on a type=\"{type}\" Image " +
                    "have no effect — AspectRatioFitter sizes the Image to its PARENT, overriding the Image's own " +
                    "anchor/size/width/height/margin. Put the size on the parent container instead.");
        }
    }
}
