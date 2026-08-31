using System.Collections.Generic;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The accordion: within one Screen, opening a <see cref="Collapsible"/> closes the others that
    /// share its <c>group</c> (spec 2026-08-31-collapsible-design §4.6).
    ///
    /// <para>Shaped like <see cref="ToggleGroupRegistry"/> — screen-scoped, name-keyed, torn down
    /// with the Screen — but it cannot reuse it: uGUI's <c>ToggleGroup</c> only coordinates
    /// <c>Toggle</c> components, and a Collapsible is not one. The rule is also deliberately
    /// looser than a ToggleGroup's: <b>all closed is a legal state</b>, because folding the open
    /// panel away is a thing a reader wants to do.</para>
    /// </summary>
    internal sealed class CollapsibleGroupRegistry
    {
        private readonly Dictionary<string, List<Collapsible>> _groups = new();

        public void Add(string group, Collapsible member)
        {
            if (string.IsNullOrEmpty(group) || member == null) return;
            if (!_groups.TryGetValue(group, out var list))
                _groups[group] = list = new List<Collapsible>();
            if (!list.Contains(member)) list.Add(member);
        }

        public void Remove(string group, Collapsible member)
        {
            if (string.IsNullOrEmpty(group) || member == null) return;
            if (_groups.TryGetValue(group, out var list)) list.Remove(member);
        }

        /// <summary>Closes every other open member of <paramref name="group"/>.</summary>
        public void NotifyExpanding(string group, Collapsible opening)
        {
            if (string.IsNullOrEmpty(group)) return;
            if (!_groups.TryGetValue(group, out var list)) return;
            for (var i = 0; i < list.Count; i++)
            {
                var other = list[i];
                if (other == null || ReferenceEquals(other, opening)) continue;
                if (other.IsExpanded) other.Collapse();
            }
        }

        /// <summary>
        /// The member that keeps its open state when several were authored open — document order,
        /// which is instantiation order (spec §4.6). Returns null when the group has none open.
        /// </summary>
        public Collapsible FirstExpanded(string group)
        {
            if (string.IsNullOrEmpty(group)) return null;
            if (!_groups.TryGetValue(group, out var list)) return null;
            for (var i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].IsExpanded) return list[i];
            return null;
        }

        /// <summary>Every group name that currently has members, for the open-time adjudication.</summary>
        public IEnumerable<string> Names => _groups.Keys;

        public IReadOnlyList<Collapsible> Members(string group)
            => _groups.TryGetValue(group, out var list) ? list : System.Array.Empty<Collapsible>();

        public void Clear() => _groups.Clear();
    }
}
