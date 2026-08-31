using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    internal enum AnimationFamily { None, Preset, LowLevel, Text }

    /// <summary>
    /// One end of a <c>reveal</c> (spec 2026-08-31-hug-reveal-flip-checked-design §2.3): either a
    /// fixed number of pixels or <c>hug</c>, meaning "whatever the child measures at the moment the
    /// animation fires" — remeasured per fire, never cached, so new rows / a locale switch are picked
    /// up by the next expand.
    /// </summary>
    internal readonly struct RevealValue : IEquatable<RevealValue>
    {
        public readonly bool IsHug;
        public readonly float Px;

        private RevealValue(bool isHug, float px)
        {
            IsHug = isHug;
            Px = px;
        }

        public static readonly RevealValue Zero = new RevealValue(false, 0f);
        public static readonly RevealValue Hug = new RevealValue(true, 0f);

        public static RevealValue Parse(string value, string attr)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v))
                throw new ArgumentException($"<Animation {attr}=\"\">: expected a number of pixels or 'hug'");
            if (v == "hug") return Hug;
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                throw new ArgumentException(
                    $"<Animation {attr}=\"{value}\">: expected a number of pixels or 'hug'");
            if (px < 0f)
                throw new ArgumentException(
                    $"<Animation {attr}=\"{value}\">: a reveal endpoint cannot be negative");
            return new RevealValue(false, px);
        }

        public bool Equals(RevealValue o) => IsHug == o.IsHug && Px.Equals(o.Px);
        public override bool Equals(object obj) => obj is RevealValue o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(IsHug, Px);
        public override string ToString() => IsHug ? "hug" : Px.ToString(CultureInfo.InvariantCulture);
    }
    internal enum LoopMode { None, Yoyo, Restart, Count }
    internal enum EasingKind
    {
        Linear,
        InCubic, OutCubic, InOutCubic,
        InQuad, OutQuad, InOutQuad,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        OutBack, OutElastic, OutBounce
    }

    internal sealed class AnimationSpec
    {
        // Family-defining inputs (raw)
        public string TypeRaw;
        public bool HasTranslate, HasScale, HasRotate, HasFade;
        public bool HasCount, HasCharColor;

        // Family D — reveal (FND §2.3). Composes with the low-level channels above rather than
        // excluding them: "grow open while fading in" is one animation, not two.
        public bool HasReveal;
        public int RevealAxis;                                // 0 = x, 1 = y
        public RevealValue RevealFrom = RevealValue.Zero;
        public RevealValue RevealTo = RevealValue.Hug;

        // Parsed values
        public Vector2 TranslateFrom, TranslateTo;
        public Vector2 ScaleFrom, ScaleTo;
        public float RotateFrom, RotateTo;
        public float FadeFrom, FadeTo;
        public float CountFrom, CountTo;
        public string Format = "{0}";
        public Color CharColorFrom, CharColorTo;
        public float CharStaggerSec;

        // Common
        public float Duration = 0.3f;
        public float Delay;
        public EasingKind Easing = EasingKind.OutCubic;
        public LoopMode LoopMode = LoopMode.None;
        public int LoopCount;
        public string TargetId;  // null if no target=

        /// <summary>
        /// <c>reverse-on=</c> — the event that plays this animation backwards, from wherever it
        /// currently is (FND §2.4.5). Null when the author did not write one.
        /// </summary>
        public TriggerSpec ReverseOn;

        public AnimationFamily Family { get; private set; }

        private static readonly string[] ValidPresets = {
            "fadein","fadeout",
            "slidein-left","slidein-right","slidein-up","slidein-down",
            "slideout-left","slideout-right","slideout-up","slideout-down",
            "scalein","scaleout",
            "pulse","bounce","shake"
        };

        public void SetType(string v) => TypeRaw = v;
        public void SetTranslate(string v) { ParseVec2FromTo(v, out TranslateFrom, out TranslateTo); HasTranslate = true; }
        public void SetScale(string v) { ParseScaleFromTo(v, out ScaleFrom, out ScaleTo); HasScale = true; }
        public void SetRotate(string v) { ParseFloatFromTo(v, out RotateFrom, out RotateTo); HasRotate = true; }
        public void SetFade(string v) { ParseFloatFromTo(v, out FadeFrom, out FadeTo); HasFade = true; }
        public void SetCount(string v) { ParseFloatFromTo(v, out CountFrom, out CountTo); HasCount = true; }
        public void SetFormat(string v) => Format = string.IsNullOrEmpty(v) ? "{0}" : v;
        public void SetCharColor(string v) { ParseColorFromTo(v, out CharColorFrom, out CharColorTo); HasCharColor = true; }
        public void SetCharStagger(string v) => CharStaggerSec = ParseSeconds(v);
        public void SetDuration(string v) => Duration = ParseSeconds(v);
        public void SetDelay(string v) => Delay = ParseSeconds(v);
        public void SetEasing(string v) => Easing = ParseEasing(v);
        public void SetLoop(string v) => ParseLoop(v, out LoopMode, out LoopCount);
        public void SetTarget(string v) => TargetId = v?.StartsWith("@") == true ? v.Substring(1) : v;

        public void SetReveal(string v)
        {
            RevealAxis = v switch
            {
                "y" => 1,
                "x" => 0,
                _ => throw new ArgumentException(
                    $"<Animation reveal=\"{v}\">: expected 'y' (height) or 'x' (width)."),
            };
            HasReveal = true;
        }

        public void SetRevealFrom(string v) => RevealFrom = RevealValue.Parse(v, "reveal-from");
        public void SetRevealTo(string v) => RevealTo = RevealValue.Parse(v, "reveal-to");
        public void SetReverseOn(string v) => ReverseOn = TriggerSpec.ParseReverseOn(v);

        public void Validate()
        {
            bool preset = !string.IsNullOrEmpty(TypeRaw);
            bool lowLevel = HasTranslate || HasScale || HasRotate || HasFade;
            bool text = HasCount || HasCharColor;

            int families = (preset ? 1 : 0) + (lowLevel ? 1 : 0) + (text ? 1 : 0);
            if (families > 1)
                throw new ArgumentException(
                    "<Animation>: three attribute families (preset / low-level transform / text-effect) " +
                    "are mutually exclusive. Use only one.");

            // reveal composes with the low-level channels (FND §2.3) but not with the other two: a
            // preset IS a low-level bundle with fixed endpoints, and the text family drives a string.
            if (HasReveal && preset)
                throw new ArgumentException(
                    "<Animation>: reveal= and type= are mutually exclusive — a preset is a fixed bundle of " +
                    "transform channels. Spell the channels out (translate= / fade= / ...) alongside reveal.");
            if (HasReveal && text)
                throw new ArgumentException(
                    "<Animation>: reveal= and count= / char-color= are mutually exclusive — the text family " +
                    "drives a string, not a box.");
            if (HasReveal && RevealFrom.Equals(RevealTo))
                throw new ArgumentException(
                    $"<Animation reveal-from=\"{RevealFrom}\" reveal-to=\"{RevealTo}\">: the two endpoints are " +
                    "the same, so nothing would move.");

            if (ReverseOn != null)
            {
                if (LoopMode != LoopMode.None)
                    throw new ArgumentException(
                        "<Animation>: reverse-on= cannot be combined with loop= — a looping motion has no " +
                        "resting end state to reverse from.");
                if (text)
                    throw new ArgumentException(
                        "<Animation>: reverse-on= cannot be combined with count= / char-color= — a number " +
                        "counting backwards (or a per-character colour unwinding) has no stable current value.");
            }

            if (preset)
            {
                if (Array.IndexOf(ValidPresets, TypeRaw) < 0)
                    throw new ArgumentException(
                        $"<Animation type=\"{TypeRaw}\"> is not a valid preset. " +
                        "Valid: " + string.Join(", ", ValidPresets));
                Family = AnimationFamily.Preset;
            }
            // A bare reveal (no transform channel alongside) still plays through the LowLevel path —
            // the driver treats reveal as one more channel there.
            else if (lowLevel || HasReveal) Family = AnimationFamily.LowLevel;
            else if (text)
            {
                if (HasCount && HasCharColor)
                    throw new ArgumentException(
                        "<Animation>: count= and char-color= are mutually exclusive within text family.");
                Family = AnimationFamily.Text;
            }
            else Family = AnimationFamily.None;
        }

        public AnimationSnapshot Snapshot() => new AnimationSnapshot
        {
            TypeRaw = TypeRaw,
            Duration = Duration,
            Delay = Delay,
            Easing = Easing,
            LoopMode = LoopMode,
            LoopCount = LoopCount,
            TranslateFrom = TranslateFrom,
            TranslateTo = TranslateTo,
            ScaleFrom = ScaleFrom,
            ScaleTo = ScaleTo,
            RotateFrom = RotateFrom,
            RotateTo = RotateTo,
            FadeFrom = FadeFrom,
            FadeTo = FadeTo,
            CountFrom = CountFrom,
            CountTo = CountTo,
            Format = Format,
            CharColorFrom = CharColorFrom,
            CharColorTo = CharColorTo,
            CharStaggerSec = CharStaggerSec,
            TargetId = TargetId,
            HasReveal = HasReveal,
            RevealAxis = RevealAxis,
            RevealFrom = RevealFrom,
            RevealTo = RevealTo,
            ReverseOnRaw = ReverseOn?.Raw,
        };

        public struct AnimationSnapshot : IEquatable<AnimationSnapshot>
        {
            public string TypeRaw; public float Duration, Delay; public EasingKind Easing;
            public LoopMode LoopMode; public int LoopCount;
            public Vector2 TranslateFrom, TranslateTo, ScaleFrom, ScaleTo;
            public float RotateFrom, RotateTo, FadeFrom, FadeTo, CountFrom, CountTo;
            public string Format;
            public Color CharColorFrom, CharColorTo; public float CharStaggerSec;
            public string TargetId;
            public bool HasReveal; public int RevealAxis;
            public RevealValue RevealFrom, RevealTo;
            public string ReverseOnRaw;
            public bool Equals(AnimationSnapshot o) =>
                TypeRaw == o.TypeRaw && Duration == o.Duration && Delay == o.Delay && Easing == o.Easing
                && LoopMode == o.LoopMode && LoopCount == o.LoopCount
                && TranslateFrom == o.TranslateFrom && TranslateTo == o.TranslateTo
                && ScaleFrom == o.ScaleFrom && ScaleTo == o.ScaleTo
                && RotateFrom == o.RotateFrom && RotateTo == o.RotateTo
                && FadeFrom == o.FadeFrom && FadeTo == o.FadeTo
                && CountFrom == o.CountFrom && CountTo == o.CountTo
                && Format == o.Format
                && CharColorFrom == o.CharColorFrom && CharColorTo == o.CharColorTo
                && CharStaggerSec == o.CharStaggerSec
                && TargetId == o.TargetId
                && HasReveal == o.HasReveal && RevealAxis == o.RevealAxis
                && RevealFrom.Equals(o.RevealFrom) && RevealTo.Equals(o.RevealTo)
                && ReverseOnRaw == o.ReverseOnRaw;
            public override bool Equals(object obj) => obj is AnimationSnapshot s && Equals(s);
            public override int GetHashCode() => HashCode.Combine(
                TypeRaw, Duration, Easing, LoopMode,
                TranslateTo, ScaleTo, FadeTo, CountTo);
        }

        /// <summary>
        /// 播放期浅拷贝。所有字段都是值类型或不可变 string，MemberwiseClone 即等价深拷贝。
        /// Driver 在拷贝上 ExpandPreset，使作者声明的原始 spec 的 family 标志保持纯净，
        /// 这样 ReSolve（主题/语言/Variant/缩放）再次 Validate 时不会把已展开的低层标志
        /// 误判成第二个 family。见 <see cref="Validate"/> 与 AnimationDriver.Play。
        /// </summary>
        public AnimationSpec Clone() => (AnimationSpec)MemberwiseClone();

        /// <summary>
        /// 把 TypeRaw 展开成等价的低层属性 (HasTranslate/HasFade/...)。
        /// 调用后 Family 保持 Preset；Driver 用展开后的低层值生成 motion。
        /// </summary>
        public void ExpandPreset()
        {
            if (string.IsNullOrEmpty(TypeRaw)) return;
            switch (TypeRaw)
            {
                case "fadein": HasFade = true; FadeFrom = 0; FadeTo = 1; break;
                case "fadeout": HasFade = true; FadeFrom = 1; FadeTo = 0; break;
                case "slidein-left": SlideIn(new Vector2(-100, 0)); break;
                case "slidein-right": SlideIn(new Vector2(100, 0)); break;
                case "slidein-up": SlideIn(new Vector2(0, -100)); break;
                case "slidein-down": SlideIn(new Vector2(0, 100)); break;
                case "slideout-left": SlideOut(new Vector2(-100, 0)); break;
                case "slideout-right": SlideOut(new Vector2(100, 0)); break;
                case "slideout-up": SlideOut(new Vector2(0, -100)); break;
                case "slideout-down": SlideOut(new Vector2(0, 100)); break;
                case "scalein":
                    HasScale = true; ScaleFrom = new Vector2(0.8f, 0.8f); ScaleTo = Vector2.one;
                    HasFade = true; FadeFrom = 0; FadeTo = 1; break;
                case "scaleout":
                    HasScale = true; ScaleFrom = Vector2.one; ScaleTo = new Vector2(0.8f, 0.8f);
                    HasFade = true; FadeFrom = 1; FadeTo = 0; break;
                case "pulse":
                    HasScale = true; ScaleFrom = Vector2.one; ScaleTo = new Vector2(1.05f, 1.05f);
                    if (LoopMode == LoopMode.None) LoopMode = LoopMode.Yoyo; break;
                case "bounce":
                    HasScale = true; ScaleFrom = new Vector2(0.9f, 0.9f); ScaleTo = Vector2.one;
                    Easing = EasingKind.OutBack; break;
                case "shake":
                    HasTranslate = true; TranslateFrom = new Vector2(-5, 0); TranslateTo = new Vector2(5, 0);
                    Easing = EasingKind.Linear;
                    if (LoopMode == LoopMode.None) { LoopMode = LoopMode.Count; LoopCount = 4; }
                    break;
            }
        }

        private void SlideIn(Vector2 from)
        {
            HasTranslate = true; TranslateFrom = from; TranslateTo = Vector2.zero;
            HasFade = true; FadeFrom = 0; FadeTo = 1;
        }

        private void SlideOut(Vector2 to)
        {
            HasTranslate = true; TranslateFrom = Vector2.zero; TranslateTo = to;
            HasFade = true; FadeFrom = 1; FadeTo = 0;
        }

        // --- parsers ---

        private static float ParseFloat(string s)
            => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>
        /// The one duration grammar every control shares: <c>"0.3s"</c> / <c>"300ms"</c> / a bare
        /// number of seconds. Internal rather than private so <see cref="TabMenu"/>'s
        /// <c>transition</c> reads exactly the same way <c>&lt;Animation duration&gt;</c> does —
        /// a second copy would be a second grammar the day one of them grew a unit.
        /// </summary>
        /// <exception cref="System.FormatException">The numeric part is not a number.</exception>
        internal static float ParseSeconds(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            s = s.Trim();
            if (s.EndsWith("ms")) return ParseFloat(s.Substring(0, s.Length - 2)) / 1000f;
            if (s.EndsWith("s")) return ParseFloat(s.Substring(0, s.Length - 1));
            return ParseFloat(s);
        }

        private static Vector2 ParseVec2(string s)
        {
            var parts = s.Split(',');
            if (parts.Length != 2)
                throw new ArgumentException($"Expected 'x,y', got '{s}'");
            return new Vector2(ParseFloat(parts[0]), ParseFloat(parts[1]));
        }

        private static void ParseVec2FromTo(string v, out Vector2 from, out Vector2 to)
        {
            var i = v.IndexOf(':');
            if (i < 0) { from = Vector2.zero; to = ParseVec2(v); return; }
            var l = v.Substring(0, i);
            var r = v.Substring(i + 1);
            from = string.IsNullOrEmpty(l) ? Vector2.zero : ParseVec2(l);
            to = ParseVec2(r);
        }

        private static void ParseScaleFromTo(string v, out Vector2 from, out Vector2 to)
        {
            var i = v.IndexOf(':');
            string l = i >= 0 ? v.Substring(0, i) : "";
            string r = i >= 0 ? v.Substring(i + 1) : v;
            from = string.IsNullOrEmpty(l) ? Vector2.one : ParseScaleSide(l);
            to = ParseScaleSide(r);
        }

        private static Vector2 ParseScaleSide(string s)
        {
            return s.Contains(',') ? ParseVec2(s) : new Vector2(ParseFloat(s), ParseFloat(s));
        }

        private static void ParseFloatFromTo(string v, out float from, out float to)
        {
            var i = v.IndexOf(':');
            if (i < 0) { from = 0f; to = ParseFloat(v); return; }
            var l = v.Substring(0, i);
            var r = v.Substring(i + 1);
            from = string.IsNullOrEmpty(l) ? 0f : ParseFloat(l);
            to = ParseFloat(r);
        }

        private static void ParseColorFromTo(string v, out Color from, out Color to)
        {
            var parts = v.Split(':');
            if (parts.Length != 2)
                throw new System.Exception($"char-color=\"{v}\": expected 'from:to'");
            from = PromptUGUI.Application.UI.Theme.Resolve(parts[0]);
            to = PromptUGUI.Application.UI.Theme.Resolve(parts[1]);
        }

        private static EasingKind ParseEasing(string s) => (s ?? "out-cubic") switch
        {
            "linear" => EasingKind.Linear,
            "in-cubic" => EasingKind.InCubic,
            "out-cubic" => EasingKind.OutCubic,
            "in-out-cubic" => EasingKind.InOutCubic,
            "in-quad" => EasingKind.InQuad,
            "out-quad" => EasingKind.OutQuad,
            "in-out-quad" => EasingKind.InOutQuad,
            "in-quart" => EasingKind.InQuart,
            "out-quart" => EasingKind.OutQuart,
            "in-out-quart" => EasingKind.InOutQuart,
            "in-quint" => EasingKind.InQuint,
            "out-quint" => EasingKind.OutQuint,
            "in-out-quint" => EasingKind.InOutQuint,
            "out-back" => EasingKind.OutBack,
            "out-elastic" => EasingKind.OutElastic,
            "out-bounce" => EasingKind.OutBounce,
            _ => throw new ArgumentException(
                $"<Animation easing=\"{s}\"> not a recognized easing. " +
                "Valid: linear / in-cubic / out-cubic / in-out-cubic / out-back / out-elastic / out-bounce / ...")
        };

        private static void ParseLoop(string v, out LoopMode mode, out int count)
        {
            count = 0;
            switch (v)
            {
                case null: case "": mode = LoopMode.None; return;
                case "true": mode = LoopMode.Restart; return;
                case "yoyo": mode = LoopMode.Yoyo; return;
            }
            if (v.StartsWith("count:"))
            {
                mode = LoopMode.Count;
                count = int.Parse(v.Substring("count:".Length), CultureInfo.InvariantCulture);
                return;
            }
            throw new ArgumentException(
                $"<Animation loop=\"{v}\"> not valid. Use true / yoyo / count:<N>.");
        }
    }
}
