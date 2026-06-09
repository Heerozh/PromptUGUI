using System;

namespace PromptUGUI.Application
{
    /// <summary>Router 注册 / 导航期的错误(未注册、缺失/成环 parent、tab 解析失败等)。</summary>
    public sealed class RouteException : Exception
    {
        public RouteException(string message) : base(message) { }
        public RouteException(string message, Exception inner) : base(message, inner) { }
    }
}
