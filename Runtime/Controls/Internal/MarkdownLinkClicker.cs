using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Added to each rendered TMP_Text; on click resolves the intersecting TMP &lt;link&gt;
    /// and reports its id (the URL) to the owning Markdown control.</summary>
    internal sealed class MarkdownLinkClicker : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text _tmp;
        private Action<string> _onLink;

        public void Init(TMP_Text tmp, Action<string> onLink)
        {
            _tmp = tmp;
            _onLink = onLink;
            _tmp.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_tmp == null) return;
            int idx = TMP_TextUtilities.FindIntersectingLink(_tmp, e.position, e.pressEventCamera);
            if (idx < 0) return;
            _onLink?.Invoke(_tmp.textInfo.linkInfo[idx].GetLinkID());
        }
    }
}
