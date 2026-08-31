using System;

namespace PromptUGUI.Controls.Internal
{
    internal enum TriggerKind
    {
        Open, Loop, Click, Manual, HoverEnter, HoverExit, Press,
        StateNormal, StateHover, StatePressed, StateSelected, StateDisabled,
        Expand, Collapse,
        Checked, Unchecked,
    }

    internal sealed class TriggerSpec
    {
        public TriggerKind Kind;
        public string SourceId;  // non-null for Click / HoverEnter / HoverExit / Press / state-* / expand / collapse with @id

        /// <summary>The literal <c>on=</c> / <c>reverse-on=</c> value, kept for error messages and
        /// for the ReSolve snapshot (two specs are the same iff they were written the same).</summary>
        public string Raw;

        private static readonly (string prefix, TriggerKind kind)[] s_prefixedKinds = {
            ("click@",          TriggerKind.Click),
            ("hover-enter@",    TriggerKind.HoverEnter),
            ("hover-exit@",     TriggerKind.HoverExit),
            ("press@",          TriggerKind.Press),
            ("state-normal@",   TriggerKind.StateNormal),
            ("state-hover@",    TriggerKind.StateHover),
            ("state-pressed@",  TriggerKind.StatePressed),
            ("state-selected@", TriggerKind.StateSelected),
            ("state-disabled@", TriggerKind.StateDisabled),
            // Not "open@" / "close@": on="open" already means "the Screen opened".
            ("expand@",         TriggerKind.Expand),
            ("collapse@",       TriggerKind.Collapse),
            ("checked@",        TriggerKind.Checked),
            ("unchecked@",      TriggerKind.Unchecked),
        };

        /// <summary>
        /// <c>reverse-on=</c> (spec 2026-08-31-hug-reveal-flip-checked-design §2.3): the same event
        /// grammar as <c>on=</c>, minus the two that describe a beginning rather than an event.
        /// </summary>
        public static TriggerSpec ParseReverseOn(string value)
        {
            var spec = Parse(value);
            if (spec.Kind == TriggerKind.Open || spec.Kind == TriggerKind.Loop)
                throw new ArgumentException(
                    $"<Animation reverse-on=\"{value}\">: cannot be 'open' or 'loop' — reverse-on names the " +
                    "event that plays the animation backwards, and a Screen opens (or a loop starts) only " +
                    "once, forwards. Use a real event: click / state-* / checked / collapse / manual (each " +
                    "also with @<id>).");
            return spec;
        }

        public static TriggerSpec Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open, Raw = value };
            switch (value)
            {
                case "open": return new TriggerSpec { Kind = TriggerKind.Open, Raw = value };
                case "loop": return new TriggerSpec { Kind = TriggerKind.Loop, Raw = value };
                case "manual": return new TriggerSpec { Kind = TriggerKind.Manual, Raw = value };
                case "click": return new TriggerSpec { Kind = TriggerKind.Click, Raw = value };
                case "hover-enter": return new TriggerSpec { Kind = TriggerKind.HoverEnter, Raw = value };
                case "hover-exit": return new TriggerSpec { Kind = TriggerKind.HoverExit, Raw = value };
                case "press": return new TriggerSpec { Kind = TriggerKind.Press, Raw = value };
                case "state-normal": return new TriggerSpec { Kind = TriggerKind.StateNormal, Raw = value };
                case "state-hover": return new TriggerSpec { Kind = TriggerKind.StateHover, Raw = value };
                case "state-pressed": return new TriggerSpec { Kind = TriggerKind.StatePressed, Raw = value };
                case "state-selected": return new TriggerSpec { Kind = TriggerKind.StateSelected, Raw = value };
                case "state-disabled": return new TriggerSpec { Kind = TriggerKind.StateDisabled, Raw = value };
                case "expand": return new TriggerSpec { Kind = TriggerKind.Expand, Raw = value };
                case "collapse": return new TriggerSpec { Kind = TriggerKind.Collapse, Raw = value };
                case "checked": return new TriggerSpec { Kind = TriggerKind.Checked, Raw = value };
                case "unchecked": return new TriggerSpec { Kind = TriggerKind.Unchecked, Raw = value };
            }
            foreach (var (prefix, kind) in s_prefixedKinds)
            {
                if (value.StartsWith(prefix))
                {
                    var id = value.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(id) || id.Contains('@'))
                        throw new ArgumentException(
                            $"Invalid trigger source id in 'on=\"{value}\"' — expected '<prefix>@<id>' with non-empty single id");
                    return new TriggerSpec { Kind = kind, SourceId = id, Raw = value };
                }
            }
            throw new ArgumentException(
                $"Invalid trigger 'on=\"{value}\"' — expected one of: open / loop / click / click@<id> / " +
                "hover-enter / hover-enter@<id> / hover-exit / hover-exit@<id> / press / press@<id> / " +
                "state-normal / state-hover / state-pressed / state-selected / state-disabled (each also with @<id>) / " +
                "expand / expand@<id> / collapse / collapse@<id> / " +
                "checked / checked@<id> / unchecked / unchecked@<id> / manual");
        }
    }
}
