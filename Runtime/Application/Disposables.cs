using System;

namespace PromptUGUI.Application
{
    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, Screen screen) where T : IDisposable
        {
            screen.Track(disposable);
            return disposable;
        }

        public static T AddTo<T>(this T disposable, IScreen screen) where T : IDisposable
        {
            ((Screen)screen).Track(disposable);
            return disposable;
        }
    }
}
