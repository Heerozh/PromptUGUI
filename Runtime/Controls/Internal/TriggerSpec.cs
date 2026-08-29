using System;

namespace PromptUGUI.Controls.Internal
{
    internal enum TriggerKind
    {
        Open, Loop, Click, Manual, HoverEnter, HoverExit, Press,
        StateNormal, StateHover, StatePressed, StateSelected, StateDisabled,
        Expand, Collapse,
    }

    internal sealed class TriggerSpec
    {
        public TriggerKind Kind;
        public string SourceId;  // non-null for Click / HoverEnter / HoverExit / Press / state-* / expand / collapse with @id

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
        };

        public static TriggerSpec Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open };
            switch (value)
            {
                case "open": return new TriggerSpec { Kind = TriggerKind.Open };
                case "loop": return new TriggerSpec { Kind = TriggerKind.Loop };
                case "manual": return new TriggerSpec { Kind = TriggerKind.Manual };
                case "click": return new TriggerSpec { Kind = TriggerKind.Click };
                case "hover-enter": return new TriggerSpec { Kind = TriggerKind.HoverEnter };
                case "hover-exit": return new TriggerSpec { Kind = TriggerKind.HoverExit };
                case "press": return new TriggerSpec { Kind = TriggerKind.Press };
                case "state-normal": return new TriggerSpec { Kind = TriggerKind.StateNormal };
                case "state-hover": return new TriggerSpec { Kind = TriggerKind.StateHover };
                case "state-pressed": return new TriggerSpec { Kind = TriggerKind.StatePressed };
                case "state-selected": return new TriggerSpec { Kind = TriggerKind.StateSelected };
                case "state-disabled": return new TriggerSpec { Kind = TriggerKind.StateDisabled };
                case "expand": return new TriggerSpec { Kind = TriggerKind.Expand };
                case "collapse": return new TriggerSpec { Kind = TriggerKind.Collapse };
            }
            foreach (var (prefix, kind) in s_prefixedKinds)
            {
                if (value.StartsWith(prefix))
                {
                    var id = value.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(id) || id.Contains('@'))
                        throw new ArgumentException(
                            $"Invalid trigger source id in 'on=\"{value}\"' — expected '<prefix>@<id>' with non-empty single id");
                    return new TriggerSpec { Kind = kind, SourceId = id };
                }
            }
            throw new ArgumentException(
                $"Invalid trigger 'on=\"{value}\"' — expected one of: open / loop / click / click@<id> / " +
                "hover-enter / hover-enter@<id> / hover-exit / hover-exit@<id> / press / press@<id> / " +
                "state-normal / state-hover / state-pressed / state-selected / state-disabled (each also with @<id>) / " +
                "expand / expand@<id> / collapse / collapse@<id> / manual");
        }
    }
}
