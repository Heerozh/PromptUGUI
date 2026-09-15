using System.Collections.Generic;
using TMPro;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Does at creation what <c>TextMeshProUGUI.Awake</c> would do later, so a TMP the library
    /// creates behaves the same whether or not its Awake has run yet.
    ///
    /// <para><b>Why.</b> Awake is not "at creation": the Screen tree is built under an inactive
    /// root, and a node built under a hidden parent — a ScrollList row bound before its page is
    /// shown, a <c>&lt;Show&gt;</c> block, an Add block under a hidden host — gets its Awake only
    /// when that parent is shown, after every attribute has been applied and after
    /// <c>GetNativeSize</c> measured it. Three things in that Awake matter here:</para>
    /// <list type="bullet">
    /// <item><c>m_isOrthographic = true</c>: without it TMP measures a UI text at the 3D text scale —
    /// one tenth of the truth — which is the "garbage preferredWidth" that used to freeze into a
    /// hidden page's LayoutElements.</item>
    /// <item><c>LoadFontAsset()</c>: without a font, measuring throws inside TMP
    /// (<c>MaterialReference</c> dereferences <c>fontAsset.material</c>).</item>
    /// <item><c>LoadDefaultSettings()</c>: on a component whose <c>fontSize</c> is still -99 it
    /// rewrites raycastTarget, wrapping, font size, font features and padding from
    /// <c>TMP_Settings</c> — over anything set before it. Setting the size here is what keeps
    /// that block from ever running; the other project defaults it would have applied are
    /// mirrored so the text still looks like one Unity created (kerning included).</item>
    /// </list>
    ///
    /// <para>Not mirrored: <c>raycastTarget</c> (the owner decides — spec 2026-09-15 §3),
    /// <c>autoSizeTextContainer</c> and the 100×100 <c>sizeDelta</c> default (the library owns
    /// every rect). Every TMP the library creates goes through here: <see cref="Prime"/> right after
    /// <c>AddComponent</c>, before any property the caller sets.</para>
    /// </summary>
    internal static class TmpPrimer
    {
        internal static void Prime(TMP_Text tmp)
        {
            if (tmp == null) return;

            // TextMeshProUGUI.Awake: a UI text is orthographic. This is the 10× factor.
            tmp.isOrthographic = true;

            // LoadDefaultSettings(), minus raycastTarget / container size (owned elsewhere).
            tmp.textWrappingMode = TMP_Settings.textWrappingMode;
            tmp.fontFeatures = new List<UnityEngine.TextCore.OTL_FeatureTag>(TMP_Settings.fontFeatures);
            tmp.extraPadding = TMP_Settings.enableExtraPadding;
            tmp.tintAllSprites = TMP_Settings.enableTintAllSprites;
            tmp.parseCtrlCharacters = TMP_Settings.enableParseEscapeCharacters;
            // Last of the block's values and the one that disarms it: fontSize != -99 from here on.
            tmp.fontSize = TMP_Settings.defaultFontSize;
            tmp.fontSizeMin = tmp.fontSize * TMP_Settings.defaultTextAutoSizingMinRatio;
            tmp.fontSizeMax = tmp.fontSize * TMP_Settings.defaultTextAutoSizingMaxRatio;
            tmp.isTextObjectScaleStatic = TMP_Settings.isTextObjectScaleStatic;

            // LoadFontAsset(): the font setter runs it (material, padding, sprite defaults).
            // FontApplier may replace the font a moment later; that is fine — what matters is that
            // there is never a font-less TMP to measure.
            if (tmp.font == null) tmp.font = TMP_Settings.defaultFontAsset;
        }
    }
}
