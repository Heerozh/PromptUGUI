using System;

namespace PromptUGUI.Application
{
    /// <summary>导航被 UI.Router.AddGuard 注册的 guard 拒绝。</summary>
    public sealed class NavigationRejectedException : Exception
    {
        public string RouteName { get; }
        public NavigationRejectedException(string routeName)
            : base($"navigation to '{routeName}' rejected by guard")
            => RouteName = routeName;
    }
}
