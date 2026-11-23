using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// One element's parsed <c>sfx=</c> (spec 2026-11-23-ui-sfx-cues §5.1): a key for every action of the element
    /// (<see cref="Bare"/>) and keys for named actions. A key of <see cref="SfxRules.None"/> mutes. Immutable.
    /// </summary>
    public sealed class SfxSpec
    {
        /// <summary>Nothing written: every action falls through to the next element of the chain.</summary>
        public static readonly SfxSpec Empty = new SfxSpec(null, null);

        private readonly Dictionary<string, string> _actions;

        internal SfxSpec(string bare, Dictionary<string, string> actions)
        {
            Bare = bare;
            _actions = actions;
        }

        /// <summary>The key without an action — every action of the element except <c>deny</c>; null when not written.</summary>
        public string Bare { get; }

        /// <summary>The <c>action:key</c> items, in no particular order.</summary>
        public IEnumerable<KeyValuePair<string, string>> Actions
            => _actions ?? (IEnumerable<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();

        public bool IsEmpty => Bare == null && (_actions == null || _actions.Count == 0);

        /// <summary>Whether an <c>action:key</c> item names <paramref name="action"/>.</summary>
        public bool Names(string action) => _actions != null && _actions.ContainsKey(action);

        /// <summary>
        /// Whether this element decides <paramref name="action"/>: its <c>action:key</c> item first, then — for anything
        /// but <c>deny</c> — its bare key. <paramref name="key"/> is null when it decides to mute.
        /// </summary>
        public bool TryDecide(string action, out string key)
        {
            if (_actions != null && _actions.TryGetValue(action, out var named))
            {
                key = named == SfxRules.None ? null : named;
                return true;
            }
            if (Bare != null && action != SfxRules.Deny)
            {
                key = Bare == SfxRules.None ? null : Bare;
                return true;
            }
            key = null;
            return false;
        }

        /// <summary>
        /// The key <paramref name="action"/> plays over a lookup chain, nearest element first (spec §5.3): the first
        /// element that decides wins; none deciding = <paramref name="defaultKey"/>. Null = muted. Null entries are skipped.
        /// </summary>
        public static string Resolve(IEnumerable<SfxSpec> chain, string action, string defaultKey)
        {
            if (chain != null)
                foreach (var spec in chain)
                    if (spec != null && spec.TryDecide(action, out var key))
                        return key;
            return defaultKey;
        }
    }

    /// <summary>
    /// The <c>sfx=</c> value grammar, the built-in action vocabulary and the three lint rules (spec
    /// 2026-11-23-ui-sfx-cues §5, §8). Pure C#: the runtime parses through <see cref="TryParse"/> and resolves through
    /// <see cref="SfxSpec.Resolve"/>, so the CLI and a running Screen read a value the same way.
    ///
    /// <para>Lint has no warning level — every finding fails the CLI — so <see cref="ActionCode"/> and
    /// <see cref="InertCode"/> only speak where the answer is certain: values written inline on built-in nodes, never
    /// what a style broadcasts, never a template body's root (it may be a list row, or get <c>drag=</c> from its call
    /// site), and <c>lift</c> / <c>land</c> / <c>return</c> are allowed everywhere because drag and drop or reorder can
    /// be switched on by the call site, the enclosing list, or C#.</para>
    /// </summary>
    public static class SfxRules
    {
        public const string Attr = "sfx";
        public const string ValueCode = "PUI-SFX-VALUE";
        public const string ActionCode = "PUI-SFX-ACTION";
        public const string InertCode = "PUI-SFX-INERT";

        /// <summary>The reserved key that mutes.</summary>
        public const string None = "none";

        public const string Click = "click";
        public const string Checked = "checked";
        public const string Unchecked = "unchecked";
        public const string Select = "select";
        public const string Deselect = "deselect";
        public const string Expand = "expand";
        public const string Collapse = "collapse";
        public const string Flip = "flip";
        public const string Open = "open";
        public const string Close = "close";
        public const string Lift = "lift";
        public const string Land = "land";
        public const string Return = "return";

        /// <summary>The refusal feedback — never covered by a bare key or <c>none</c>, only by <c>deny:</c>.</summary>
        public const string Deny = "deny";

        private const string ScreenRootTag = "__screen_root__";

        private static readonly HashSet<string> s_vocabulary = new HashSet<string>(StringComparer.Ordinal)
        {
            Click, Checked, Unchecked, Select, Deselect, Expand, Collapse, Flip, Open, Close, Lift, Land, Return, Deny,
        };

        /// <summary>Every action a built-in element performs or is looked up for.</summary>
        public static IReadOnlyCollection<string> Vocabulary => s_vocabulary;

        /// <summary>Whether <paramref name="action"/> is one a built-in element performs or is looked up for.</summary>
        public static bool IsBuiltinAction(string action) => action != null && s_vocabulary.Contains(action);

        // What each built-in tag does itself (spec §5.2).
        private static readonly Dictionary<string, string[]> s_own = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Btn"] = new[] { Click, Deny },
            ["Toggle"] = new[] { Checked, Unchecked, Deny },
            ["Tab"] = new[] { Select, Deselect, Deny },
            ["TabMenu"] = new[] { Expand, Collapse, Deny },
            ["Dropdown"] = new[] { Expand, Collapse, Select, Deny },
            ["Popover"] = new[] { Expand, Collapse },
            ["Collapsible"] = new[] { Expand, Collapse, Deny },
            ["Carousel"] = new[] { Flip },
            [ScreenRootTag] = new[] { Open, Close },
        };

        // Looked up on behalf of the tabs inside (TabBar, TabMenu).
        private static readonly string[] s_tabGroup = { Select, Deselect, Deny };

        // Drag and drop, reorder: allowed on any element (they can be switched on at runtime).
        private static readonly HashSet<string> s_moves = new HashSet<string>(StringComparer.Ordinal) { Lift, Land, Return };

        private static readonly char[] Separators = { ' ', '\t', '\n', '\r', ',' };

        // ───── grammar ─────

        /// <summary>
        /// Parses an <c>sfx=</c> value. Null / empty / blank = <see cref="SfxSpec.Empty"/> (not written). False with an
        /// <paramref name="error"/> that reads after <c>sfx="…"</c> for anything malformed; <paramref name="spec"/> is
        /// then <see cref="SfxSpec.Empty"/>.
        /// </summary>
        public static bool TryParse(string value, out SfxSpec spec, out string error)
        {
            spec = SfxSpec.Empty;
            error = null;
            if (value == null) return true;

            var items = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (items.Length == 0) return true;

            string bare = null;
            Dictionary<string, string> actions = null;
            foreach (var item in items)
            {
                var colon = item.IndexOf(':');
                if (colon < 0)
                {
                    if (bare != null)
                    {
                        error = $"has two keys without an action ('{bare}' and '{item}'): one key may stand alone, " +
                                "the others need action:key";
                        return false;
                    }
                    bare = item;
                    continue;
                }

                var action = item.Substring(0, colon);
                var key = item.Substring(colon + 1);
                if (action.Length == 0)
                {
                    error = $"has '{item}', which names no action before ':'";
                    return false;
                }
                if (key.Length == 0)
                {
                    error = $"has '{item}', which gives no key after ':' (write {action}:none to mute it)";
                    return false;
                }
                if (key.IndexOf(':') >= 0)
                {
                    error = $"has '{item}': a key cannot contain ':'";
                    return false;
                }
                if (!IsActionName(action))
                {
                    error = $"has '{item}': '{action}' is not an action name (lower case, e.g. click, checked, expand)";
                    return false;
                }
                actions ??= new Dictionary<string, string>(StringComparer.Ordinal);
                if (actions.ContainsKey(action))
                {
                    error = $"names the action '{action}' twice";
                    return false;
                }
                actions[action] = key;
            }

            spec = new SfxSpec(bare, actions);
            return true;
        }

        private static bool IsActionName(string s)
        {
            if (s.Length == 0 || s[0] < 'a' || s[0] > 'z') return false;
            foreach (var c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    return false;
            return true;
        }

        /// <summary>The default key: <c>kind.action</c> (spec §5.2).</summary>
        public static string DefaultKey(string kind, string action) => kind + "." + action;

        // ───── what an element does ─────

        /// <summary>
        /// Whether a built-in element ever uses <paramref name="action"/>: one it performs, one it is looked up for
        /// (a tab group for its tabs, a <c>drop=</c> target for <c>land</c>), or a drag / reorder move.
        /// </summary>
        public static bool Uses(ElementNode n, string action, StyleAttributeView styles)
        {
            if (s_moves.Contains(action)) return true;
            if (s_own.TryGetValue(n.Tag, out var own) && Array.IndexOf(own, action) >= 0) return true;
            if (IsTabGroup(n.Tag) && Array.IndexOf(s_tabGroup, action) >= 0) return true;
            return action == Land && styles.Declares(n, "drop");
        }

        /// <summary>Whether a built-in element raises or is looked up for any cue at all, in some configuration.</summary>
        private static bool Sounds(ElementNode n, StyleAttributeView styles)
        {
            if (s_own.ContainsKey(n.Tag) || IsTabGroup(n.Tag)) return true;
            if (styles.Declares(n, "drag") || styles.Declares(n, "drop")) return true;
            return n.Tag == "ScrollList" && styles.Declares(n, "reorder");
        }

        private static bool IsTabGroup(string tag) => tag == "TabBar" || tag == "TabMenu";

        // ───── lint ─────

        /// <summary>
        /// A value's syntax (<see cref="ValueCode"/>) — <see cref="StyleRules"/> calls this for every value it sees,
        /// so inline values, <c>&lt;Style&gt;</c> packs and the synthetic Screen root are all covered. Null when fine.
        /// </summary>
        internal static LintIssue? CheckValue(string context, string tag, string id, string attrName, string value)
        {
            if (TryParse(value, out _, out var error)) return null;
            return new LintIssue(ValueCode, tag, id, $"{context}: {attrName}=\"{value}\" {error}");
        }

        /// <summary>The syntax of every <c>sfx</c> value in a theme's <c>&lt;Style&gt;</c> packs.</summary>
        public static IEnumerable<LintIssue> CheckStyle(StyleDef style)
        {
            var context = $"<Style name='{style.Name}'>";
            if (style.Attributes.TryGetValue(Attr, out var value) && !IsPlaceholder(value))
            {
                var issue = CheckValue(context, "Style", style.Name, Attr, value);
                if (issue.HasValue) yield return issue.Value;
            }
            if (style.VariantOverrides.TryGetValue(Attr, out var variants))
                foreach (var (variant, v) in variants)
                {
                    if (IsPlaceholder(v)) continue;
                    var issue = CheckValue(context, "Style", style.Name, Attr + "." + variant, v);
                    if (issue.HasValue) yield return issue.Value;
                }
        }

        /// <summary>
        /// <see cref="ActionCode"/> and <see cref="InertCode"/> over one tree, carrying the parent each needs. Stamps
        /// every finding with its node's source. <paramref name="isTemplateBodyRoot"/>: <paramref name="root"/> is a
        /// template's body as written (the raw pass walks bodies on their own).
        /// </summary>
        public static IEnumerable<LintIssue> CheckTree(ElementNode root, StyleAttributeView styles, bool isTemplateBodyRoot = false)
        {
            if (root == null) yield break;
            styles ??= StyleAttributeView.Empty;
            foreach (var issue in Walk(root, parent: null, isTemplateBodyRoot, styles))
                yield return issue;
        }

        private static IEnumerable<LintIssue> Walk(ElementNode n, ElementNode parent, bool isTemplateBodyRoot,
                                                   StyleAttributeView styles)
        {
            foreach (var issue in CheckNode(n, parent, isTemplateBodyRoot, styles))
                yield return issue.WithSource(n.OriginSrc, n.Line, n.InvokedAt);
            foreach (var child in n.Children)
                foreach (var issue in Walk(child, n, isTemplateBodyRoot: false, styles))
                    yield return issue;
        }

        private static IEnumerable<LintIssue> CheckNode(ElementNode n, ElementNode parent, bool isTemplateBodyRoot,
                                                        StyleAttributeView styles)
        {
            if (!n.Attributes.ContainsKey(Attr) && !n.VariantOverrides.ContainsKey(Attr)) yield break;
            // What a style broadcasts is checked for its syntax only.
            if (n.StyleAttrNames != null && n.StyleAttrNames.Contains(Attr)) yield break;
            // A template body's root may be a list row, or get drag= from its call site. An instance root carries
            // the body's value too, which is the same thing; only what its call site wrote is judged, below.
            if (isTemplateBodyRoot) yield break;
            if (n.IsTemplateInstanceRoot
                && (n.InvocationAttrNames == null || !n.InvocationAttrNames.Contains(Attr)))
                yield break;

            var builtin = BuiltinTags.IsBuiltin(n.Tag) || n.Tag == ScreenRootTag;
            if (!builtin) yield break;   // a custom control has actions the CLI cannot know

            var specs = ParsedValues(n);
            var label = $"<{n.Tag}{(string.IsNullOrEmpty(n.Id) ? "" : $" id='{n.Id}'")}>";
            var atCallSite = n.IsTemplateInstanceRoot ? " (written where the template is used)" : "";

            foreach (var (attrName, spec) in specs)
                foreach (var kv in spec.Actions)
                {
                    if (!IsBuiltinAction(kv.Key))
                        yield return new LintIssue(ActionCode, n.Tag, n.Id,
                            $"{label}: {attrName}=\"…{kv.Key}:{kv.Value}…\"{atCallSite}: '{kv.Key}' is not an action of " +
                            $"any built-in control (known: {string.Join(", ", Sorted(Vocabulary))}).");
                    else if (!Uses(n, kv.Key, styles))
                        yield return new LintIssue(ActionCode, n.Tag, n.Id,
                            $"{label}: {attrName}=\"…{kv.Key}:{kv.Value}…\"{atCallSite}: a <{DisplayTag(n.Tag)}> never " +
                            $"does '{kv.Key}', so this key is never played. {ActionsHint(n, styles)}");
                }

            if (Inert(n, parent, specs, styles))
                yield return new LintIssue(InertCode, n.Tag, n.Id, InertMessage(n, label));
        }

        private static bool Inert(ElementNode n, ElementNode parent, List<(string Attr, SfxSpec Spec)> specs,
                                  StyleAttributeView styles)
        {
            if (n.Tag == ScreenRootTag) return false;
            if (styles.IsUncertain(n)) return false;
            if (Sounds(n, styles)) return false;
            // A static child of a sortable list is one of its rows.
            if (parent != null && parent.Tag == "ScrollList" && styles.Declares(parent, "reorder")) return false;
            // lift: / land: / return: say the author means to switch drag on from C#.
            foreach (var (_, spec) in specs)
                foreach (var kv in spec.Actions)
                    if (s_moves.Contains(kv.Key)) return false;
            return true;
        }

        private static string InertMessage(ElementNode n, string label)
        {
            if (n.Tag == "Pages")
                return $"{label}: sfx= on a <Pages> is never played — a page flip is made by code (often inside a " +
                       "button's OnClick), never by a user gesture on the <Pages> itself. Put the key on the control " +
                       "that flips the page, e.g. <Btn sfx=\"pages.flip\">.";
            if (n.IsTemplateInstanceRoot)
                return $"{label}: the sfx= written where this template is used lands on its root, a <{n.Tag}>, which " +
                       "raises no sound cue, so it is never played. To set the sound of a control inside the template, " +
                       "give the template a <Param> with another name (e.g. <Param name=\"buySfx\" default=\"\"/>) and " +
                       "write sfx=\"{{buySfx}}\" on that control.";
            return $"{label}: sfx= is never played — a <{n.Tag}> raises no sound cue and is not looked up for one " +
                   "(no drag= / drop=). Put it on the control the user acts on (<Btn>, <Toggle>, <Tab>, <Dropdown>, " +
                   "<Collapsible>, <Popover>, <Carousel>, <Screen>, a drag= / drop= element, a reorder list or row).";
        }

        private static string ActionsHint(ElementNode n, StyleAttributeView styles)
        {
            var list = new List<string>();
            foreach (var action in Sorted(Vocabulary))
                if (!s_moves.Contains(action) && Uses(n, action, styles)) list.Add(action);
            return list.Count == 0
                ? "It only takes lift / land / return (drag and drop, reorder)."
                : $"It takes {string.Join(", ", list)} (and lift / land / return for drag and drop or reorder).";
        }

        private static string DisplayTag(string tag) => tag == ScreenRootTag ? "Screen" : tag;

        private static List<(string Attr, SfxSpec Spec)> ParsedValues(ElementNode n)
        {
            var result = new List<(string, SfxSpec)>();
            if (n.Attributes.TryGetValue(Attr, out var value) && !IsPlaceholder(value)
                && TryParse(value, out var spec, out _))
                result.Add((Attr, spec));
            if (n.VariantOverrides.TryGetValue(Attr, out var variants))
                foreach (var (variant, v) in variants)
                    if (!IsPlaceholder(v) && TryParse(v, out var vs, out _))
                        result.Add((Attr + "." + variant, vs));
            return result;
        }

        private static bool IsPlaceholder(string value) => value != null && value.Contains("{{");

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>(names);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
