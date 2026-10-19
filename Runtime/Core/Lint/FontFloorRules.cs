using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>PUI-FONT-FLOOR</c>: text that renders below the project's floor on one of its target
    /// screens (2026-10-12 font-floor spec). A <c>fontSize</c> is in canvas units; on a device it
    /// becomes <c>fontSize × canvas factor × Π ancestor localScale ÷ OS scale</c> dp, and the canvas
    /// factor differs per device — which is why a text that reads fine on the author's machine can be
    /// unreadable on someone else's, and why nothing short of evaluating each target catches it.
    ///
    /// <para>Walks the EXPANDED tree only: the raw view has neither a template invocation's real
    /// ancestors nor the <c>fontSize</c> a <c>class=</c> brings from an import (spec §3-E). Per
    /// <c>&lt;Screen&gt;</c> × target it rebuilds what the runtime builds under that target's
    /// variants — active <c>&lt;Add&gt;</c> blocks hung where <c>ScreenInstantiator</c> would hang
    /// them, <c>itemTemplate</c> rows under their list — with the runtime's own formulas
    /// (<see cref="CanvasScaleMath"/>, <see cref="ScaleSyntax"/>, <see cref="VariantLookup"/>).
    /// One finding per node: the worst target leads, with the fix, and the others follow.</para>
    /// </summary>
    public static class FontFloorRules
    {
        public const string Code = "PUI-FONT-FLOOR";

        /// <summary>A <c>fontFloor</c> attribute that is not a non-negative number.</summary>
        public const string ValueCode = "PUI-FONT-FLOOR-VALUE";

        private const float Epsilon = 1e-4f;

        /// <summary>Where a tag's text size lives (spec §5.5).</summary>
        internal sealed class TextTag
        {
            /// <summary>The size the control builds with when <c>fontSize</c> is not written; null = TMP's default.</summary>
            public float? Default;

            /// <summary>Only carries text once it is given some (<c>text</c>, text content, or a <c>fontSize</c>).</summary>
            public bool OnlyWithText;

            /// <summary>No <c>fontSize</c> attribute at all — the size is the default, always.</summary>
            public bool Fixed;

            /// <summary>
            /// Only carries text while this boolean attribute is <c>true</c> — a
            /// <c>&lt;Cooldown&gt;</c>'s label exists for its <c>countdown</c>, whatever its fontSize says.
            /// </summary>
            public string OnlyWhenTrue;
        }

        /// <summary>
        /// The text-bearing built-ins and the size each builds with — mirrors the controls' own
        /// constants (<c>Btn.cs</c> / <c>Tab.cs</c> 24, <c>ProceduralBuilders.AddText</c> 14,
        /// <c>Collapsible.DefaultFontSize</c>, <c>TabMenu.DefaultFontSize</c>,
        /// <c>MarkdownStyle.BodySize</c>); <c>FontFloorDefaultsTests</c> pins them against the real
        /// controls.
        /// </summary>
        internal static readonly Dictionary<string, TextTag> TextTags = new Dictionary<string, TextTag>
        {
            ["Text"] = new TextTag { Default = null },
            ["Btn"] = new TextTag { Default = 24f, OnlyWithText = true },
            ["Tab"] = new TextTag { Default = 24f, OnlyWithText = true },
            ["Toggle"] = new TextTag { Default = 14f, OnlyWithText = true },
            ["InputField"] = new TextTag { Default = 14f },
            ["Collapsible"] = new TextTag { Default = 14f, OnlyWithText = true },
            ["TabMenu"] = new TextTag { Default = 24f },
            ["Dropdown"] = new TextTag { Default = 14f, Fixed = true },
            ["Markdown"] = new TextTag { Default = 16f },
            ["Cooldown"] = new TextTag { Default = 24f, OnlyWhenTrue = "countdown" },
        };

        /// <summary>The tags that build rows from an <c>itemTemplate</c> (<c>TemplateFactoryResolver</c>'s callers).</summary>
        private static readonly HashSet<string> RowHosts = new HashSet<string> { "ScrollList", "TabBar", "Carousel", "Suggestions" };

        /// <summary>
        /// One finding per text node that renders below <see cref="FontFloorPolicy.Floor"/> on at
        /// least one target, attributed to where the node was written. Nothing when the policy is
        /// null, off, or has no usable target.
        /// </summary>
        public static IEnumerable<LintIssue> Check(UIDocument expanded, FontFloorPolicy policy)
        {
            if (expanded == null || policy == null || policy.Floor <= 0f || policy.Targets == null)
                yield break;
            var targets = ResolveTargets(policy.Targets);
            if (targets.Count == 0) yield break;

            foreach (var screen in expanded.Screens)
            {
                if (screen.CanvasMode == CanvasMode.World) continue;

                var visits = new Dictionary<VisitKey, Visit>();
                var order = new List<Visit>();
                foreach (var target in targets)
                    new Walker(screen, target, policy, visits, order).Run();

                foreach (var visit in order)
                    if (visit.Below.Count > 0)
                        yield return visit.ToIssue();
            }
        }

        private static List<Target> ResolveTargets(IReadOnlyList<FontFloorTarget> rows)
        {
            var targets = new List<Target>();
            foreach (var row in rows)
            {
                if (row == null || row.IsBlank || FontFloorTargetSyntax.Validate(row) != null) continue;
                FontFloorTargetSyntax.TryParse(row.screen, out var screen, out _);
                var active = new HashSet<string> { screen.Orientation };
                foreach (var v in FontFloorTargetSyntax.SplitVariants(row.variants)) active.Add(v);
                targets.Add(new Target
                {
                    Name = row.DisplayName,
                    ScreenText = row.screen.Trim(),
                    Screen = screen,
                    Active = active,
                });
            }
            return targets;
        }

        private sealed class Target
        {
            public string Name;
            public string ScreenText;
            public FontFloorScreen Screen;
            public HashSet<string> Active;
        }

        /// <summary>One target's reading of one node.</summary>
        private sealed class Reading
        {
            public string TargetName;
            public string ScreenText;
            public string Orientation;
            public string CanvasDesc;
            public string SizeDesc;
            public float Dp;
            public float PerUnit;          // dp per canvas unit at this node
            public float ScaleChain;       // Π localScale, root to node
            public float OwnLocalScale;
            public float Floor;            // the floor this reading is judged against
            public string FloorSource;     // where a node-level floor came from; null = the project's
        }

        /// <summary>
        /// The floor in force at a node: the project's, or the nearest <c>fontFloor</c> on the node or
        /// an ancestor (2026-10-12 font-floor spec §14). <see cref="Owner"/> null = the project's.
        /// </summary>
        private sealed class Floor
        {
            public float Value;
            public ElementNode Owner;
            public string Attribute;       // "fontFloor" / "fontFloor.mobile"
            public string Class;           // the class= that brought it, if a pack did

            /// <summary>How a finding names it, seen from <paramref name="at"/>.</summary>
            public string Describe(ElementNode at)
            {
                if (Owner == null) return null;
                var owner = ReferenceEquals(Owner, at) ? "this node" : Label(Owner);
                var via = Class == null ? "" : $" via class=\"{Class}\"";
                return $"{Attribute}=\"{Num(Value)}\" on {owner}{via}";
            }
        }

        /// <summary>A node as reached through a particular chain of <c>itemTemplate</c> hosts.</summary>
        private readonly struct VisitKey : IEquatable<VisitKey>
        {
            private readonly ElementNode _node;
            private readonly ElementNode[] _hosts;

            public VisitKey(ElementNode node, ElementNode[] hosts)
            {
                _node = node;
                _hosts = hosts;
            }

            public bool Equals(VisitKey other)
            {
                if (!ReferenceEquals(_node, other._node) || _hosts.Length != other._hosts.Length) return false;
                for (var i = 0; i < _hosts.Length; i++)
                    if (!ReferenceEquals(_hosts[i], other._hosts[i])) return false;
                return true;
            }

            public override bool Equals(object obj) => obj is VisitKey other && Equals(other);

            public override int GetHashCode()
            {
                var h = RuntimeHelpers.GetHashCode(_node);
                foreach (var host in _hosts) h = h * 31 + RuntimeHelpers.GetHashCode(host);
                return h;
            }
        }

        private sealed class Visit
        {
            public ElementNode Node;
            public bool Fixed;
            public readonly List<Reading> Below = new List<Reading>();

            public LintIssue ToIssue()
            {
                // The largest shortfall leads: with one floor that is the smallest dp, but a node-level
                // fontFloor can differ per target (a variant), and the target furthest below ITS floor
                // is the one whose fix satisfies them all.
                Below.Sort((a, b) => (a.Dp / a.Floor).CompareTo(b.Dp / b.Floor));
                var worst = Below[0];
                var floor = worst.Floor;

                var scale = Math.Abs(worst.ScaleChain - 1f) > Epsilon ? $", scale ×{Num(worst.ScaleChain)}" : "";
                // A --target row is named after its screen; say the screen once.
                var screen = worst.TargetName == worst.ScreenText ? "" : worst.ScreenText + " ";
                var source = worst.FloorSource == null ? "" : $" ({worst.FloorSource})";
                var message =
                    $"{Label(Node)}: {worst.SizeDesc} renders at {Dp(worst.Dp)}dp on '{worst.TargetName}' " +
                    $"({screen}{worst.Orientation}; {worst.CanvasDesc}{scale}) — below the {Num(floor)}dp floor{source}. ";

                if (Fixed)
                {
                    var needed = Math.Ceiling(worst.OwnLocalScale * floor / worst.Dp * 100f - Epsilon) / 100.0;
                    message += "The control has no fontSize attribute. " +
                               $"Needs scale ≥ {needed.ToString("0.##", CultureInfo.InvariantCulture)} on this node.";
                }
                else
                {
                    var needed = Math.Ceiling(floor / worst.PerUnit - Epsilon);
                    message += $"Needs fontSize ≥ {needed.ToString("0", CultureInfo.InvariantCulture)}.";
                }

                if (Below.Count > 1)
                {
                    var others = new List<string>();
                    for (var i = 1; i < Below.Count; i++)
                        others.Add($"'{Below[i].TargetName}' ({Dp(Below[i].Dp)}dp)");
                    message += $" Also below on {string.Join(", ", others)}.";
                }

                return new LintIssue(Code, Node.Tag, Node.Id, message, Node.OriginSrc, Node.Line, Node.InvokedAt);
            }
        }

        /// <summary>One <c>&lt;Screen&gt;</c> under one target: the tree the runtime builds there.</summary>
        private sealed class Walker
        {
            private readonly ScreenDef _screen;
            private readonly Target _target;
            private readonly FontFloorPolicy _policy;
            private readonly Dictionary<VisitKey, Visit> _visits;
            private readonly List<Visit> _order;
            private readonly Func<string, bool> _isActive;
            private readonly Dictionary<ElementNode, List<ElementNode>> _adds =
                new Dictionary<ElementNode, List<ElementNode>>();
            private readonly HashSet<ElementNode> _rowsInProgress = new HashSet<ElementNode>();
            private float _factor;
            private string _canvasDesc;

            public Walker(ScreenDef screen, Target target, FontFloorPolicy policy,
                          Dictionary<VisitKey, Visit> visits, List<Visit> order)
            {
                _screen = screen;
                _target = target;
                _policy = policy;
                _visits = visits;
                _order = order;
                _isActive = target.Active.Contains;
            }

            public void Run()
            {
                ResolveCanvasFactor();
                CollectAdds();
                VisitNode(_screen.Root, _factor, 1f, new ElementNode[0], new Floor { Value = _policy.Floor });
            }

            // Screen.ApplyCanvasScaler for this target. A Screen that does not say scale-mode is
            // judged as auto: UI.DefaultScaleMode is a runtime switch the lint cannot see (spec §5.4).
            private void ResolveCanvasFactor()
            {
                var root = _screen.Root;
                var pixel = VariantLookup.Resolve(root, "scale-mode", _isActive) == "pixel";
                var referenceRaw = VariantLookup.Resolve(root, "reference", _isActive)?.Trim();
                (float W, float H)? reference = null;
                try
                {
                    reference = ReferenceSyntax.Parse(referenceRaw, $"<Screen name='{_screen.Name}' reference>");
                }
                catch (ParseException)
                {
                    // The parser already rejected it; nothing sensible to measure against.
                }

                var screen = _target.Screen;
                if (reference == null)
                {
                    _factor = 1f;
                    _canvasDesc = "canvas ×1, no reference";
                }
                else if (pixel)
                {
                    _factor = CanvasScaleMath.PixelFactor(screen.Width, screen.Height,
                                                          reference.Value.W, reference.Value.H);
                    _canvasDesc = $"pixel canvas ×{Num(_factor)} from reference {referenceRaw}";
                }
                else
                {
                    _factor = CanvasScaleMath.ExpandFactor(screen.Width, screen.Height,
                                                           reference.Value.W, reference.Value.H);
                    _canvasDesc = $"canvas ×{Num(_factor)} from reference {referenceRaw}";
                }
            }

            // The <Add> blocks of the variants this target activates, keyed by the node they hang under.
            private void CollectAdds()
            {
                foreach (var block in _screen.Variants)
                {
                    if (!_isActive(block.When)) continue;
                    foreach (var add in block.Adds)
                    {
                        var parent = FindAddTarget(_screen.Root, add.IntoPath);
                        if (parent == null) continue;   // the runtime throws there; not this rule's to say
                        if (!_adds.TryGetValue(parent, out var list)) _adds[parent] = list = new List<ElementNode>();
                        list.AddRange(add.Children);
                    }
                }
            }

            private void VisitNode(ElementNode node, float parentPx, float parentChain, ElementNode[] hosts,
                                   Floor parentFloor)
            {
                var local = ScaleSyntax.LocalScale(VariantLookup.Resolve(node, "scale", _isActive), _factor);
                var px = parentPx * local;            // physical px per canvas unit
                var chain = parentChain * local;
                var floor = OwnFloor(node) ?? parentFloor;

                Inspect(node, px, chain, local, hosts, floor);

                foreach (var child in node.Children)
                    VisitNode(child, px, chain, hosts, floor);

                if (_adds.TryGetValue(node, out var added))
                    foreach (var child in added)
                        VisitNode(child, px, chain, hosts, floor);

                if (RowHosts.Contains(node.Tag))
                {
                    var name = VariantLookup.Resolve(node, "itemTemplate", _isActive);
                    if (!string.IsNullOrEmpty(name)
                        && _screen.Templates.TryGetValue(name, out var tpl)
                        && tpl.BodyExpanded && tpl.Body != null
                        && _rowsInProgress.Add(tpl.Body))
                    {
                        var rowHosts = new ElementNode[hosts.Length + 1];
                        hosts.CopyTo(rowHosts, 0);
                        rowHosts[hosts.Length] = node;
                        VisitNode(tpl.Body, px, chain, rowHosts, floor);
                        _rowsInProgress.Remove(tpl.Body);
                    }
                }
            }

            // This node's own fontFloor under the target's variants; null when it has none (or one that
            // is not a size — PUI-FONT-FLOOR-VALUE says so), so the ancestor's stays in force.
            private Floor OwnFloor(ElementNode node)
            {
                var raw = VariantLookup.Resolve(node, FloorAttribute, _isActive, out var variant);
                if (!TryParseFloor(raw, out var value)) return null;
                string cls = null;
                if (node.StyleAttrNames != null && node.StyleAttrNames.Contains(FloorAttribute))
                    node.Attributes.TryGetValue("class", out cls);
                return new Floor
                {
                    Value = value,
                    Owner = node,
                    Attribute = variant == null ? FloorAttribute : FloorAttribute + "." + variant,
                    Class = cls,
                };
            }

            private void Inspect(ElementNode node, float px, float chain, float local, ElementNode[] hosts,
                                 Floor floor)
            {
                if (floor.Value <= 0f) return;   // fontFloor="0": the subtree is exempt
                if (node.Namespace != null || !TextTags.TryGetValue(node.Tag, out var tag)) return;
                if (tag.OnlyWhenTrue != null
                    && VariantLookup.Resolve(node, tag.OnlyWhenTrue, _isActive) != "true")
                    return;

                string sizeRaw = null, variant = null;
                if (!tag.Fixed) sizeRaw = VariantLookup.Resolve(node, "fontSize", _isActive, out variant);
                var declared = !string.IsNullOrEmpty(sizeRaw);

                if (tag.OnlyWithText && !declared
                    && string.IsNullOrEmpty(VariantLookup.Resolve(node, "text", _isActive))
                    && string.IsNullOrEmpty(node.TextContent))
                    return;

                float size;
                string sizeDesc;
                if (tag.Fixed)
                {
                    size = tag.Default.Value;
                    sizeDesc = $"fixed fontSize {Num(size)}";
                }
                else if (declared)
                {
                    if (!float.TryParse(sizeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out size)
                        || size <= 0f)
                        return;   // unsubstituted {{…}} / garbage: another rule's business
                    sizeDesc = $"{(variant == null ? "fontSize" : "fontSize." + variant)} {Num(size)}";
                    if (node.StyleAttrNames != null && node.StyleAttrNames.Contains("fontSize")
                        && node.Attributes.TryGetValue("class", out var cls))
                        sizeDesc += $" (class=\"{cls}\")";
                }
                else
                {
                    size = tag.Default ?? _policy.TextDefaultSize;
                    sizeDesc = $"default fontSize {Num(size)}";
                }

                var perUnit = px / _target.Screen.Density;
                var dp = size * perUnit;

                var key = new VisitKey(node, hosts);
                if (!_visits.TryGetValue(key, out var visit))
                {
                    visit = new Visit { Node = node, Fixed = tag.Fixed };
                    _visits[key] = visit;
                    _order.Add(visit);
                }
                if (dp >= floor.Value - Epsilon) return;

                visit.Below.Add(new Reading
                {
                    TargetName = _target.Name,
                    ScreenText = _target.ScreenText,
                    Orientation = _target.Screen.Orientation,
                    CanvasDesc = _canvasDesc,
                    SizeDesc = sizeDesc,
                    Dp = dp,
                    PerUnit = perUnit,
                    ScaleChain = chain,
                    OwnLocalScale = local,
                    Floor = floor.Value,
                    FloorSource = floor.Describe(node),
                });
            }
        }

        // ---- the node-level override -------------------------------------------------------------

        /// <summary>
        /// The node-level floor: a dp value for this node and its subtree, nearest one wins, <c>0</c>
        /// exempts. Lint-only — the runtime skips an attribute no control declares.
        /// </summary>
        public const string FloorAttribute = "fontFloor";

        private static bool TryParseFloor(string raw, out float value)
        {
            value = 0f;
            return !string.IsNullOrEmpty(raw)
                   && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && value >= 0f;
        }

        /// <summary>
        /// <see cref="ValueCode"/>: a <c>fontFloor</c> (base or variant) that is not a non-negative
        /// number. Pure syntax, so it runs on every pass whether or not a floor is configured. An empty
        /// variant value is the usual "clear under this variant"; a <c>{{param}}</c> is judged once
        /// substituted.
        /// </summary>
        public static IEnumerable<LintIssue> CheckValue(ElementNode node)
        {
            if (node.Attributes.TryGetValue(FloorAttribute, out var baseValue) && IsBadFloor(baseValue, allowEmpty: false))
                yield return BadFloor(node, FloorAttribute, baseValue);
            if (node.VariantOverrides.TryGetValue(FloorAttribute, out var list))
                foreach (var (variant, value) in list)
                    if (IsBadFloor(value, allowEmpty: true))
                        yield return BadFloor(node, FloorAttribute + "." + variant, value);
        }

        private static bool IsBadFloor(string value, bool allowEmpty)
        {
            if (string.IsNullOrEmpty(value)) return !allowEmpty;
            if (value.Contains("{{")) return false;
            return !TryParseFloor(value, out _);
        }

        private static LintIssue BadFloor(ElementNode node, string attribute, string value) =>
            new LintIssue(ValueCode, node.Tag, node.Id,
                $"{Label(node)}: {attribute}=\"{value}\" is not a size — write the floor in dp as a non-negative " +
                "number (0 exempts the subtree from PUI-FONT-FLOOR).");

        /// <summary>
        /// <c>ScreenInstantiator.ResolveAddTarget</c> on the IR: <c>@root</c>, or <c>#a/b/c</c> whose
        /// first segment is an id in the Screen's scope and every further one an id in the scope of
        /// the node before — a template instance's whole scope, or a plain node's direct children.
        /// Null when the path leads nowhere.
        /// </summary>
        internal static ElementNode FindAddTarget(ElementNode root, string intoPath)
        {
            if (intoPath == "@root") return root;
            if (string.IsNullOrEmpty(intoPath) || intoPath[0] != '#' || intoPath.Length == 1) return null;

            var segments = intoPath.Substring(1).Split('/');
            var current = FindInScope(root, segments[0]);
            for (var i = 1; i < segments.Length && current != null; i++)
                current = current.IsTemplateInstanceRoot
                    ? FindInScope(current, segments[i])
                    : FindChild(current, segments[i]);
            return current;
        }

        // An id in the scope rooted at scopeRoot: every descendant, except that a template instance
        // root keeps its own descendants to itself (its own id still belongs out here).
        private static ElementNode FindInScope(ElementNode scopeRoot, string id)
        {
            foreach (var child in scopeRoot.Children)
            {
                if (child.Id == id) return child;
                if (child.IsTemplateInstanceRoot) continue;
                var hit = FindInScope(child, id);
                if (hit != null) return hit;
            }
            return null;
        }

        private static ElementNode FindChild(ElementNode parent, string id)
        {
            foreach (var child in parent.Children)
                if (child.Id == id) return child;
            return null;
        }

        private static string Label(ElementNode node) =>
            string.IsNullOrEmpty(node.Id) ? $"<{node.Tag}>" : $"<{node.Tag} id='{node.Id}'>";

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Dp(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
