using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// What <see cref="FontFloorRules"/> checks against: the floor, the target screens, and the
    /// size a <c>&lt;Text&gt;</c> without <c>fontSize</c> renders at. Built by the front end — the
    /// Editor menu from <c>PromptUGUISettings</c> + <c>TMP_Settings</c>, the CLI from the settings
    /// asset and its flags. See the 2026-10-12 font-floor spec §5.7.
    /// </summary>
    public sealed class FontFloorPolicy
    {
        /// <summary>The smallest readable text, in dp. 0 or less turns the rule off.</summary>
        public float Floor;

        /// <summary>
        /// The rows as written; blank and malformed rows are skipped here (<see cref="LintRun"/>
        /// reports the malformed ones once per run).
        /// </summary>
        public IReadOnlyList<FontFloorTarget> Targets;

        /// <summary><c>TMP_Settings.defaultFontSize</c> — TMP ships 36.</summary>
        public float TextDefaultSize = 36f;
    }
}
