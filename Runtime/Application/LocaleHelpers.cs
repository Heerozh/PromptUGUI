using UnityEngine;

namespace PromptUGUI.Application
{
    public static class LocaleHelpers
    {
        public static string MapSystemLanguage(SystemLanguage lang) =>
            lang switch
            {
                SystemLanguage.ChineseSimplified => "zh-Hans",
                SystemLanguage.ChineseTraditional => "zh-Hant",
                SystemLanguage.Chinese => "zh-Hans",
                SystemLanguage.English => "en",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.French => "fr",
                SystemLanguage.German => "de",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Russian => "ru",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Italian => "it",
                _ => null,
            };
    }
}
