using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Template
{
    /// <summary>
    /// Re-derives every <c>class=</c> node in an already-expanded Screen against a different style
    /// table — what a theme switch runs (2026-08-26 spec §5).
    ///
    /// <para><b>Why this and not a second expansion.</b> Re-expanding would rebuild the IR and force
    /// <c>ScreenInstantiator</c> to rebuild GameObjects, destroying references and R3 subscriptions.
    /// Attributes, however, are already re-applied wholesale on every <c>Screen.ReSolve</c> — the
    /// same path a resize or a Variant flip takes — so re-deriving the attribute VALUES in place and
    /// letting ReSolve replay them costs one extra pass over the classed nodes and nothing else.</para>
    ///
    /// <para>Idempotent: running it twice with the same table is a no-op, which is what lets it sit
    /// unconditionally at the head of both <c>Open</c> and <c>ReSolve</c> rather than needing a
    /// theme-changed hook of its own.</para>
    /// </summary>
    internal static class ThemeStyleApplier
    {
        public static void Apply(ScreenDef def, IReadOnlyDictionary<StyleKey, StyleDef> styles)
        {
            if (def == null) return;

            ApplySubtree(def.Root, styles);
            ApplySubtree(def.FocusCursor, styles);

            // Add-block subtrees are instantiated lazily but live in the same declaration space, and
            // Screen.ReSolve walks them too — an inactive block must come back with the current skin.
            foreach (var block in def.Variants)
                foreach (var add in block.Adds)
                    foreach (var child in add.Children)
                        ApplySubtree(child, styles);

            // itemTemplate bodies too: rows bound AFTER a theme switch are instantiated from these.
            // Safe to write in place only because BuildRuntimeTemplates gave the ScreenDef its own
            // copies — the shared (possibly commons-pool) TemplateDefs are never touched.
            //
            // BodyExpanded gates it: a body kept raw (required <Param>) can still read
            // class="{{skin}}", and looking THAT up as a style name throws "unknown style '{{skin}}'".
            foreach (var tpl in def.Templates.Values)
                if (tpl.BodyExpanded)
                    ApplySubtree(tpl.Body, styles);
        }

        private static void ApplySubtree(ElementNode node, IReadOnlyDictionary<StyleKey, StyleDef> styles)
        {
            if (node == null) return;
            StyleMerger.ReMerge(node, styles);
            foreach (var child in node.Children)
                ApplySubtree(child, styles);
        }
    }
}
