using System;
using UnityEngine;

namespace PromptUGUI.Application
{
    // 把 RectTransform 的 OnRectTransformDimensionsChange magic method 转成 C# 回调，
    // 让 Screen 能向上层暴露纯 lambda 形态的事件订阅,业务侧不必自己写 MonoBehaviour。
    internal sealed class RectDimensionsRelay : MonoBehaviour
    {
        public Action OnDimensionsChanged;

        // Unity 在很多场景都会触发 OnRectTransformDimensionsChange(CanvasScaler.scaleFactor
        // 赋值后 Canvas RT 自动 resize、子节点布局重排级联到父等),即使 rect 实际尺寸没变也调。
        // 缓存上次尺寸比较,没变就不向订阅者派发——避免 Pixel mode 每帧反复跑 ApplyPixel。
        // 初值 NaN 保证首次回调一定触发(NaN != NaN)。
        private Vector2 _lastSize = new Vector2(float.NaN, float.NaN);

        private void OnRectTransformDimensionsChange()
        {
            var rt = (RectTransform)transform;
            var size = rt.rect.size;
            if (size == _lastSize) return;
            _lastSize = size;
            OnDimensionsChanged?.Invoke();
        }

        // Test seam: EditMode tests can't reliably get Unity to fire
        // OnRectTransformDimensionsChange on orphan / parentless GameObjects, so
        // expose a direct trigger for the guard logic unit test.
        internal void InvokeRectChangedForTests() => OnRectTransformDimensionsChange();
    }
}
