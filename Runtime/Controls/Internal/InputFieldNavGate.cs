using PromptUGUI.Application;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 两级方向导航门：导航（Directional）选中 TMP_InputField 时只聚焦不进编辑，
    /// 让方向键经 Selectable.OnMove 正常导航；Submit 才进编辑。指针选中（Pointer 模式）
    /// 保持 TMP 默认立即编辑。仅当 UI.Navigation.IsEnabled 时生效。
    /// </summary>
    internal sealed class InputFieldNavGate : MonoBehaviour,
        ISelectHandler, ISubmitHandler, IDeselectHandler
    {
        private TMP_InputField _input;
        private bool _suppressUntilDeactivated;

        internal void Init(TMP_InputField input) => _input = input;

        public void OnSelect(BaseEventData eventData)
        {
            // 导航选中：标记下一 tick 撤销 TMP 的自动激活（同帧撤销有 m_AllowInput 竞态，见计划风险点 1）。
            if (UI.Navigation.IsEnabled && UI.Navigation.IsDirectional)
                _suppressUntilDeactivated = true;
        }

        // TMP 的激活延后到 LateUpdate 才令 isFocused=true；本组件 AddComponent 晚于 TMP，
        // LateUpdate 在其后运行 → 此刻 isFocused 已 true，可安全 DeactivateInputField。
        private void LateUpdate()
        {
            if (!_suppressUntilDeactivated) return;
            if (_input != null && _input.isFocused)
            {
                _input.DeactivateInputField();
                _suppressUntilDeactivated = false;
            }
            // Pointer 模式被点中（用户改用鼠标）→ 放弃抑制，保留编辑。
            else if (!UI.Navigation.IsDirectional)
            {
                _suppressUntilDeactivated = false;
            }
        }

        // Also check in Update as a double-safety net in case LateUpdate on this component
        // runs before TMP's own LateUpdate (MonoBehaviour execution order is not guaranteed
        // unless Script Execution Order is configured).
        private void Update()
        {
            if (!_suppressUntilDeactivated) return;
            if (_input != null && _input.isFocused)
            {
                _input.DeactivateInputField();
                _suppressUntilDeactivated = false;
            }
            // Pointer 模式被点中（用户改用鼠标）→ 放弃抑制，保留编辑。
            // (Symmetric with LateUpdate's escape-hatch — T1-M2 fold-in.)
            else if (!UI.Navigation.IsDirectional)
            {
                _suppressUntilDeactivated = false;
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!UI.Navigation.IsEnabled) return;
            if (_input == null || !_input.IsInteractable()) return;
            // Enter-edit: TMP's own OnSubmit (runs first) already scheduled activation
            // (m_ShouldActivateNextUpdate). Do NOT ActivateInputField() here — on a single-line
            // *confirm* TMP has already DeactivateInputField()'d synchronously, so re-activating
            // would bounce the field back into edit instead of returning to navigation. We only
            // clear the suppress flag so the gate doesn't fight TMP's intentional activation.
            if (!_input.isFocused)
                _suppressUntilDeactivated = false;
        }

        public void OnDeselect(BaseEventData eventData) => _suppressUntilDeactivated = false;
    }
}
