using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// 临时挂到目标 GO(TapTarget)或 mask GO(TapAnywhere)的点击转发器,步骤结束移除。
    /// 与 GO 上既有 Button 等 IPointerClickHandler 并存(uGUI 对命中 GO 上所有 handler 逐个执行)。
    /// </summary>
    internal sealed class TutorialClickRelay : MonoBehaviour, IPointerClickHandler
    {
        internal Action OnClicked;
        public void OnPointerClick(PointerEventData _) => OnClicked?.Invoke();
        internal void FireForTests() => OnClicked?.Invoke();
    }
}
