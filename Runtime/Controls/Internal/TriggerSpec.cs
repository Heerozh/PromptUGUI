using System;

namespace PromptUGUI.Controls.Internal
{
    internal enum TriggerKind { Open, Loop, Click, Manual }

    internal sealed class TriggerSpec
    {
        public TriggerKind Kind;
        public string SourceId;  // only non-null for Click with @id

        public static TriggerSpec Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return new TriggerSpec { Kind = TriggerKind.Open };
            switch (value)
            {
                case "open": return new TriggerSpec { Kind = TriggerKind.Open };
                case "loop": return new TriggerSpec { Kind = TriggerKind.Loop };
                case "manual": return new TriggerSpec { Kind = TriggerKind.Manual };
                case "click": return new TriggerSpec { Kind = TriggerKind.Click };
            }
            if (value.StartsWith("click@"))
            {
                var id = value.Substring("click@".Length);
                if (string.IsNullOrEmpty(id) || id.Contains('@'))
                    throw new ArgumentException(
                        $"Invalid trigger source id in 'on=\"{value}\"' — expected 'click@<id>' with non-empty single id");
                return new TriggerSpec { Kind = TriggerKind.Click, SourceId = id };
            }
            throw new ArgumentException(
                $"Invalid trigger 'on=\"{value}\"' — expected one of: open / loop / click / click@<id> / manual");
        }
    }
}
