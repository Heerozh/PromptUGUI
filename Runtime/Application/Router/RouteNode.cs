using System;

namespace PromptUGUI.Application
{
    /// <summary>screen-backed 节点的呈现方式。Tab/Prompt 由 MapTab/MapPrompt 表达,不在此枚举。</summary>
    public enum RoutePresent { Page, Modal }

    /// <summary>Prompt 节点的支撑 handler:跑内置对话框 + 处理结果。ct 取消 = 被导航走。</summary>
    public delegate UnityEngine.Awaitable RoutePromptRun(
        RouteQuery query, System.Threading.CancellationToken ct);

    internal enum RouteKind { Page, Modal, Tab, Prompt }

    /// <summary>一个路由节点的注册记录(运行时,非 XML IR)。</summary>
    internal sealed class RouteNode
    {
        public string Name;                      // 稳定不透明 ID,全局唯一
        public string Parent;                    // null = 根
        public RouteKind Kind;
        public string Src;                       // Page/Modal:.ui.xml 的 src key
        public string Screen;                    // Page/Modal:screen 名;null → 激活时按"单 Screen"解析
        public string TabId;                     // Tab:宿主 screen 内 <Tab> 控件的 id 路径
        public RoutePromptRun Run;               // Prompt
        public Action<IScreen, RouteQuery> OnEnter;  // Page/Modal/Tab
    }
}
