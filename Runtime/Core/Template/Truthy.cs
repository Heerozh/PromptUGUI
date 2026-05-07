using System.Globalization;

namespace PromptUGUI.Template {
    public static class Truthy {
        public static bool Eval(string s) {
            if (string.IsNullOrEmpty(s)) return false;

            var lower = s.ToLowerInvariant();
            if (lower == "false" || lower == "null") return false;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d != 0.0;

            return true;
        }
    }
}
