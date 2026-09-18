using System;

namespace PromptUGUI.IR
{
    /// <summary>
    /// One row of <c>PromptUGUISettings.commonLibraries</c>: a common library every document
    /// implicitly imports. Semantically the <c>&lt;Import src as&gt;</c> written at the top of every
    /// entry document — except that it lands in the commons pool, where a name shared with the
    /// document is a hard error, rather than in the document's own template table.
    ///
    /// <para>A plain serializable class rather than <see cref="ImportRef"/> because Unity's
    /// serializer wants public mutable fields and a parameterless constructor; readers convert with
    /// <see cref="ToImportRef"/>. Lives in Core/IR — <c>System.Serializable</c> only, no UnityEngine —
    /// so the UIXmlLint CLI's <c>SettingsAssetReader</c> yields the very type the settings asset
    /// holds. See the 2026-09-18 commons-settings spec §4.1.</para>
    /// </summary>
    [Serializable]
    public sealed class CommonLibraryEntry
    {
        /// <summary>Resolver key — the same shape as <c>&lt;Import src&gt;</c>, never a file path.</summary>
        public string src;

        /// <summary>
        /// Optional namespace: templates are invoked as <c>&lt;ns.Name/&gt;</c>, styles as
        /// <c>class="ns:name"</c>. Blank = none.
        /// </summary>
        public string @as;

        /// <summary>Blank rows are inert: kept in the Inspector, skipped by every reader.</summary>
        public bool IsBlank => string.IsNullOrWhiteSpace(src);

        /// <summary>Whitespace trimmed on both; a blank <see cref="@as"/> is no namespace.</summary>
        public ImportRef ToImportRef() =>
            new ImportRef(src?.Trim(), string.IsNullOrWhiteSpace(@as) ? null : @as.Trim());
    }
}
