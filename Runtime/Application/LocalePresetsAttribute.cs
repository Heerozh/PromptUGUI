using UnityEngine;

namespace PromptUGUI.Application {
    public sealed class LocalePresetsAttribute : PropertyAttribute {
        public static readonly (string code, string display)[] Defaults = {
            ("en",      "en — English"),
            ("zh-Hans", "zh-Hans — 简体中文"),
            ("zh-Hant", "zh-Hant — 繁體中文"),
            ("ja",      "ja — 日本語"),
            ("ko",      "ko — 한국어"),
            ("es",      "es — Español"),
            ("fr",      "fr — Français"),
            ("de",      "de — Deutsch"),
            ("ru",      "ru — Русский"),
            ("pt-BR",   "pt-BR — Português (Brasil)"),
        };
    }
}
