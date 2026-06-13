using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Flags a gradient value (contains ',') on any <c>*Modulate</c> attribute. Modulate is a
    /// per-state colour MULTIPLIER fanned out to the subtree; it is solid-only by design (spec §6),
    /// and the runtime <see cref="PromptUGUI.Application.UI.Theme.Resolve"/> hard-throws on a
    /// gradient there. This surfaces the same mistake statically. CLI-only (dispatched from
    /// <c>IRWalker</c>), mirroring <see cref="ColorLiteralRules"/>.
    /// </summary>
    public static class GradientModulateRules
    {
        public const string GradientModulateCode = "PUI-GRADIENT-MODULATE";

        public static IEnumerable<LintIssue> Check(ElementNode node)
        {
            foreach (var kv in node.Attributes)
            {
                if (!kv.Key.EndsWith("Modulate", System.StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(kv.Value) || kv.Value.IndexOf(',') < 0) continue;
                yield return new LintIssue(GradientModulateCode, node.Tag, node.Id,
                    $"<{node.Tag} id='{node.Id}'>: '{kv.Key}' is a colour multiplier — " +
                    "gradients are not supported on *Modulate (use a solid colour/token).");
            }
        }
    }
}
