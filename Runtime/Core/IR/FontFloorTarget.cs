using System;

namespace PromptUGUI.IR
{
    /// <summary>
    /// One screen the font-floor lint (<c>PUI-FONT-FLOOR</c>) checks every <c>&lt;Screen&gt;</c>
    /// against — a row of <c>PromptUGUISettings.fontFloorTargets</c>. Pure C#
    /// (<see cref="SerializableAttribute"/>) so the settings asset and the UIXmlLint CLI share the
    /// type. See the 2026-10-12 font-floor spec §5.1.
    /// </summary>
    [Serializable]
    public sealed class FontFloorTarget
    {
        /// <summary>What a finding calls this screen ("iPhone SE"); blank → the <see cref="screen"/> text.</summary>
        public string name;

        /// <summary>
        /// <c>WxH[@D]</c>: the canvas in physical pixels, at the OS scale (pixels per dp) — e.g.
        /// <c>750x1334@2x</c> for an iPhone SE held upright. The aspect picks the orientation.
        /// </summary>
        public string screen;

        /// <summary>Optional: extra variants active on this screen, space- or comma-separated ("mobile tv").</summary>
        public string variants;

        public bool IsBlank => string.IsNullOrWhiteSpace(screen);

        /// <summary>The name a finding uses.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(name) ? screen?.Trim() : name.Trim();
    }
}
