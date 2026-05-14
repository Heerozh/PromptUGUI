using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    internal enum AnimationFamily { None, Preset, LowLevel, Text }
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

            if (preset)
            {
                if (Array.IndexOf(ValidPresets, TypeRaw) < 0)
                    throw new ArgumentException(
                        $"<Animation type=\"{TypeRaw}\"> is not a valid preset. " +
                        "Valid: " + string.Join(", ", ValidPresets));
                Family = AnimationFamily.Preset;
            }
            else if (lowLevel) Family = AnimationFamily.LowLevel;
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
                && TargetId == o.TargetId;
            public override bool Equals(object obj) => obj is AnimationSnapshot s && Equals(s);
            public override int GetHashCode() => HashCode.Combine(
                TypeRaw, Duration, Easing, LoopMode,
                TranslateTo, ScaleTo, FadeTo, CountTo);
        }

        // --- parsers ---

        private static float ParseFloat(string s)
            => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static float ParseSeconds(string s)
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
            var i = v.IndexOf(':');
            if (i < 0) { from = Color.white; to = ParseColor(v); return; }
            from = ParseColor(v.Substring(0, i));
            to = ParseColor(v.Substring(i + 1));
        }

        private static Color ParseColor(string s)
        {
            var parts = s.Split(',');
            if (parts.Length != 4)
                throw new ArgumentException($"Expected 'r,g,b,a', got '{s}'");
            return new Color(ParseFloat(parts[0]), ParseFloat(parts[1]),
                             ParseFloat(parts[2]), ParseFloat(parts[3]));
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
