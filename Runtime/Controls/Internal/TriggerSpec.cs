using System;

namespace PromptUGUI.Controls.Internal
{
    internal enum TriggerKind
    {
        Open, Loop, Click, Manual, HoverEnter, HoverExit, Press,
        StateNormal, StateHover, StatePressed, StateDisabled,
    }

    internal sealed class TriggerSpec
    {
        public TriggerKind Kind;
        public string SourceId;  // non-null for Click / HoverEnter / HoverExit / Press / state-* with @id

        private static readonly (string prefix, TriggerKind kind)[] s_prefixedKinds = {
            ("click@",          TriggerKind.Click),
            ("hover-enter@",    TriggerKind.HoverEnter),
            ("hover-exit@",     TriggerKind.HoverExit),
            ("press@",          TriggerKind.Press),
            ("state-normal@",   TriggerKind.StateNormal),
            ("state-hover@",    TriggerKind.StateHover),
            ("state-pressed@",  TriggerKind.StatePressed),
            ("state-disabled@", TriggerKind.StateDisabled),
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
                case "state-disabled": return new TriggerSpec { Kind = TriggerKind.StateDisabled };
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
                "state-normal / state-hover / state-pressed / state-disabled (each also with @<id>) / manual");
        }
    }
}
