using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Template;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Rules for theme-scoped <c>&lt;Style&gt;</c> packs (2026-08-26 spec §6.1, §7). Both catch
    /// mistakes whose only runtime symptom is "some attribute silently keeps the previous theme's
    /// value", which is close to undebuggable from the outside — the reason the spec makes them
    /// static constraints rather than documentation.
    /// </summary>
    /// <remarks>
    /// Internal, unlike the other rule classes: <see cref="StyleKey"/> is internal, and widening it
    /// to the public API just to type one lint parameter would be the tail wagging the dog. The only
    /// consumers are <see cref="DocumentLinter"/> and the tests, both of which see internals.
    /// </remarks>
    internal static class ThemeStyleRules
    {
        public const string ShapeCode = "PUI-THEME-STYLE-SHAPE";
        public const string OnInvocationCode = "PUI-THEME-STYLE-ON-INVOCATION";
        public const string NoBaselineCode = "PUI-THEME-STYLE-NO-BASELINE";

        /// <summary>
        /// §4.2. Expansion resolves <c>class=</c> against the GLOBAL style pool; the theme layer only
        /// re-derives values afterwards. A style that exists solely inside a <c>&lt;Theme&gt;</c> is
        /// therefore unreferenceable — <c>class="pixel-only"</c> throws "unknown style" at expansion,
        /// with a message that cannot mention the theme it was actually written in.
        ///
        /// <para>Reported even when nothing references it, because both readings are worth knowing:
        /// unreferenced means dead, referenced means broken. The fix is the same either way and is
        /// the discipline §6.1 rests on — declare the global <c>&lt;Style&gt;</c> as the baseline and
        /// let each theme override values on top of it.</para>
        /// </summary>
        public static IEnumerable<LintIssue> CheckBaselines(
            IReadOnlyDictionary<StyleKey, StyleDef> globalStyles,
            IReadOnlyDictionary<string, ThemeBlock> themes)
        {
            if (themes == null) yield break;

            var reported = new HashSet<string>();
            foreach (var themeName in Sorted(themes.Keys))
            {
                foreach (var styleName in Sorted(themes[themeName].Styles.Keys))
                {
                    if (globalStyles != null
                        && globalStyles.ContainsKey(StyleKey.ParseReference(styleName)))
                        continue;
                    if (!reported.Add(styleName)) continue;

                    yield return new LintIssue(
                        NoBaselineCode, "Theme", styleName,
                        $"<Theme name='{themeName}'> declares <Style name='{styleName}'> but there is " +
                        "no global <Style> of that name. class= resolves against the global pool at " +
                        "expansion, so this pack can never be reached. Declare the global " +
                        $"<Style name='{styleName}'> with the full attribute set and let each theme " +
                        "override the few values that differ.");
                }
            }
        }

        /// <summary>
        /// §6.1. Every theme must resolve the same attribute-NAME set for a given style, because
        /// <c>ControlAttributeApplier</c> only replays attributes that resolve to a value: a name
        /// present under theme A and absent under theme B is never reset when you switch to B, so the
        /// control silently keeps A's value.
        ///
        /// <para>Only compared ACROSS themes. The global <c>&lt;Style&gt;</c> is the implicit root of
        /// every chain, so each theme's set already contains it; and with fewer than two themes there
        /// is no switch to go wrong.</para>
        ///
        /// <para>One family is exempt — see <see cref="ShapeOnlyProcedural"/>.</para>
        /// </summary>
        public static IEnumerable<LintIssue> CheckShape(
            IReadOnlyDictionary<StyleKey, StyleDef> globalStyles,
            IReadOnlyDictionary<string, ThemeBlock> themes)
        {
            if (themes == null || themes.Count < 2) yield break;

            var touched = new HashSet<string>();
            foreach (var theme in themes.Values)
                foreach (var name in theme.Styles.Keys)
                    touched.Add(name);
            if (touched.Count == 0) yield break;

            // theme name -> its resolved table, computed once each.
            var resolved = new Dictionary<string, IReadOnlyDictionary<StyleKey, StyleDef>>(themes.Count);
            foreach (var themeName in themes.Keys)
                resolved[themeName] = ThemeStyleResolver.Resolve(globalStyles, themes, themeName);

            foreach (var styleName in Sorted(touched))
            {
                var key = StyleKey.ParseReference(styleName);

                // Decided across ALL themes before any pair is compared — deliberately not inside
                // the loop below. That loop measures everything against the first theme in sort
                // order, so a pairwise test would clear 'plain vs full' and 'plain vs partial'
                // independently and never notice that 'full' and 'partial' disagree.
                var exemptProcedural = ProceduralSetIsAllOrNothing(resolved, key);

                string referenceTheme = null;
                HashSet<string> referenceNames = null;

                foreach (var themeName in Sorted(resolved.Keys))
                {
                    var names = NamesOf(resolved[themeName], key);
                    if (exemptProcedural) names.ExceptWith(ShapeOnlyProcedural);
                    if (referenceNames == null)
                    {
                        referenceTheme = themeName;
                        referenceNames = names;
                        continue;
                    }
                    if (referenceNames.SetEquals(names)) continue;

                    var onlyA = Missing(referenceNames, names);
                    var onlyB = Missing(names, referenceNames);
                    yield return new LintIssue(
                        ShapeCode, "Theme", styleName,
                        $"<Style name='{styleName}'> resolves to different attributes under " +
                        $"'{referenceTheme}' and '{themeName}': " +
                        Describe(referenceTheme, onlyA) + Describe(themeName, onlyB) +
                        "An attribute one theme sets and the other does not is never reset on a " +
                        "switch — the control keeps the old value. Give it a baseline in the global " +
                        $"<Style name='{styleName}'>, or declare it in every theme.");
                    break;   // one report per style name is enough to act on
                }
            }
        }

        /// <summary>
        /// §7. A theme-scoped style on a Template INVOCATION does not follow the theme. Half of the
        /// pack becomes <c>&lt;Param&gt;</c> values, which <c>Substitution</c> bakes into the body's
        /// attribute strings at expansion; the other half is merged onto the instance root, and the
        /// invocation node itself no longer exists afterwards, so there is nothing left to re-derive
        /// from. The author gets a skin that silently refuses to change.
        ///
        /// <para>The fix is to move the <c>class=</c> down onto a node inside the template body,
        /// where it is an ordinary node attribute and re-merges like any other.</para>
        /// </summary>
        public static IEnumerable<LintIssue> CheckInvocations(
            ElementNode root,
            IReadOnlyCollection<string> templateTags,
            IReadOnlyCollection<string> themeStyleNames)
        {
            if (root == null || templateTags == null || templateTags.Count == 0
                || themeStyleNames == null || themeStyleNames.Count == 0)
                yield break;

            var tags = new HashSet<string>(templateTags);
            var themed = new HashSet<string>(themeStyleNames);
            foreach (var issue in Walk(root, tags, themed)) yield return issue;
        }

        private static IEnumerable<LintIssue> Walk(
            ElementNode node, HashSet<string> tags, HashSet<string> themed)
        {
            var tag = new TemplateKey(node.Namespace, node.Tag).ToString();
            if (tags.Contains(tag)
                && node.Attributes.TryGetValue(StyleMerger.ClassAttr, out var classValue)
                && classValue != null)
            {
                foreach (var reference in classValue.Split(
                             new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    // Compared as written: a theme names its override the same way class= names
                    // the reference, namespace included.
                    if (!themed.Contains(reference)) continue;

                    yield return new LintIssue(
                        OnInvocationCode, node.Tag, node.Id,
                        $"<{tag}> is a Template invocation, and class=\"{reference}\" names a style " +
                        "some <Theme> overrides — but a pack applied here is baked in at expansion " +
                        "and will NOT follow a theme switch. Move class= onto a node inside " +
                        $"<Template name='{node.Tag}'>, or use a style no theme overrides.");
                    break;
                }
            }

            foreach (var child in node.Children)
                foreach (var issue in Walk(child, tags, themed))
                    yield return issue;
        }

        /// <summary>
        /// The attributes that describe a procedural SHAPE, and nothing else. Derived from
        /// <see cref="ProceduralAttrNames"/> rather than hand-listed, so a new procedural attribute
        /// lands here the moment it is wired up.
        ///
        /// <para><b>Why these are exempt from §6.1.</b> The residue this rule guards against needs an
        /// attribute that STICKS when a theme stops setting it. These do the opposite:
        /// <c>ProceduralSurface</c> recomputes the mode every pass from "was the setter called at
        /// all" (procedural-surface spec §8), so a theme that simply omits them turns the surface off
        /// and hands the control back to its Image, sprite and alpha included. The twin rule on the
        /// variant side has said so since procedural surfaces shipped —
        /// <c>VariantBaseRules.proceduralSelfHeals</c>.</para>
        ///
        /// <para>And §6.1's usual advice is actively harmful here: presence, not value, is what
        /// attaches the surface, so writing <c>radius=""</c> as a "baseline" retires the sprite the
        /// other theme needs — which <c>PUI-PROC-SPRITE-CONFLICT</c> then reports, correctly.</para>
        ///
        /// <para><b>Two deliberate omissions.</b> <c>weld</c> never crosses into a control
        /// (procedural-surface §13.2), the same reason <c>ProceduralSurfaceRules</c> skips it.
        /// <c>color</c> is left out even though <c>VariantBaseRules</c> counts it as procedural: on an
        /// Image-backed control it is an ordinary tint, and the path that would make it self-heal —
        /// <c>Restore()</c> putting the retired alpha back — is itself the defect the 2026-08-27 spec
        /// §5 fixes. A theme that stops setting <c>color</c> is a real residue.</para>
        ///
        /// <para><b>Known under-report:</b> this rule is style-level and runs before expansion, so it
        /// cannot see which tag the class lands on. <c>&lt;Frame&gt;</c> attaches its panel directly
        /// and never reconciles the mode per pass, so it does NOT self-heal — a themed shape on a
        /// Frame goes unreported. Accepted: <c>VariantBaseRules</c> can exclude Frame only because it
        /// has a node to look at, and a false positive here would wall off correct XML behind the
        /// CLI's non-zero exit. See the 2026-08-27 spec §3.3.</para>
        /// </summary>
        private static readonly HashSet<string> ShapeOnlyProcedural = BuildShapeOnlyProcedural();

        private static HashSet<string> BuildShapeOnlyProcedural()
        {
            var set = new HashSet<string>();
            foreach (var name in ProceduralAttrNames.NeedsPanel)
                if (name != "weld") set.Add(name);
            foreach (var name in ProceduralAttrNames.InnerLayerRadius)
                set.Add(name);
            return set;
        }

        /// <summary>
        /// Whether every theme's procedural set for this style is either EMPTY or the same one —
        /// which is what "the surface toggles wholesale" means, and the only shape that self-heals.
        ///
        /// <para>A theme holding HALF the set pins the mode on, so the half it omits really is never
        /// reset; that style goes back to being reported name-for-name. Requiring an empty side too
        /// is what separates "one skin has a shape, the other has none" from "two skins disagree
        /// about the shape they both draw".</para>
        /// </summary>
        private static bool ProceduralSetIsAllOrNothing(
            Dictionary<string, IReadOnlyDictionary<StyleKey, StyleDef>> resolved, StyleKey key)
        {
            var distinct = new HashSet<string>(System.StringComparer.Ordinal);
            var sawEmpty = false;

            foreach (var table in resolved.Values)
            {
                var shape = new List<string>();
                foreach (var name in NamesOf(table, key))
                    if (ShapeOnlyProcedural.Contains(name)) shape.Add(name);
                shape.Sort(System.StringComparer.Ordinal);

                var canonical = string.Join(",", shape);
                if (canonical.Length == 0) sawEmpty = true;
                distinct.Add(canonical);
            }

            return sawEmpty && distinct.Count <= 2;
        }

        private static HashSet<string> NamesOf(
            IReadOnlyDictionary<StyleKey, StyleDef> table, StyleKey key)
        {
            var names = new HashSet<string>();
            if (table != null && table.TryGetValue(key, out var style))
                foreach (var name in style.DeclaredNames) names.Add(name);
            return names;
        }

        private static List<string> Missing(HashSet<string> from, HashSet<string> other)
        {
            var only = new List<string>();
            foreach (var name in from)
                if (!other.Contains(name)) only.Add(name);
            only.Sort(System.StringComparer.Ordinal);
            return only;
        }

        private static string Describe(string themeName, List<string> only) =>
            only.Count == 0 ? "" : $"only '{themeName}' sets {string.Join(", ", only)}. ";

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>(names);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }
    }
}
