using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    // 把 RectTransform 的 OnRectTransformDimensionsChange magic method 转成 C# 回调，
    // 让 Screen 能向上层暴露纯 lambda 形态的事件订阅,业务侧不必自己写 MonoBehaviour。
    internal sealed class RectDimensionsRelay : MonoBehaviour
    {
        public Action OnDimensionsChanged;

        private void OnRectTransformDimensionsChange()
        {
            OnDimensionsChanged?.Invoke();
        }
    }
}
