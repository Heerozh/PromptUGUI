using System;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A state change from code that lands on its end state at once — <c>Tab.SetIsOn(on, animate: false)</c>,
    /// <c>Toggle.SetIsOn</c>, <c>TabBar.ClearSelection(animate: false)</c>. While <see cref="Run"/> is on the
    /// stack, a <c>checked</c> / <c>unchecked</c> <c>&lt;Animation&gt;</c> establishes its end state the way a
    /// Screen's opening does (FND-D10) instead of playing, and the controls' own state fades (a Toggle's check,
    /// a state colour) snap. Scoped to the call, not to a control: the tab a group turns off on the way runs
    /// inside the same call and lands too.
    /// </summary>
    internal static class InstantState
    {
        // Single-threaded UI: a depth, so a nested call (a bind callback selecting from inside one) is fine.
        private static int s_depth;

        public static bool Active => s_depth > 0;

        public static void Run(Action change)
        {
            s_depth++;
            try { change(); }
            finally { s_depth--; }
        }
    }
}
