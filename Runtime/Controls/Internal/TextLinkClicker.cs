using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The pointer side of the TMP <c>&lt;link="id"&gt;</c> tags in one <see cref="Text"/> (spec
    /// 2026-09-30-text-link-click). Nothing is cached: a click looks its link up in the text's layout as
    /// it is when the click lands, so a row a virtual list recycled for another message, or a text
    /// rewritten since, answers for what is on screen now.
    ///
    /// <para>Also the text's raycast filter: unless the author asked for the whole rect
    /// (<c>raycastTarget="true"</c>), only a point over a link hits the text and the rest of it stays
    /// click-through, as a <c>&lt;Text&gt;</c> is by default (TL-D4).</para>
    /// </summary>
    internal sealed class TextLinkClicker : MonoBehaviour, IPointerClickHandler, ICanvasRaycastFilter
    {
        private readonly List<CanvasGroup> _groups = new();
        private TMP_Text _tmp;
        private Func<bool> _wholeRect;
        private Action<string> _onLink;

        internal void Init(TMP_Text tmp, Func<bool> wholeRect, Action<string> onLink)
        {
            _tmp = tmp;
            _wholeRect = wholeRect;
            _onLink = onLink;
        }

        // Graphic.Raycast asks this for the text's own Graphic and for every Graphic under it: TMP's
        // sub-meshes (fallback-font glyphs, sprites) want the same answer, and so does anything written
        // inside the <Text> — like a Mask or a CanvasGroup, the filter covers the subtree.
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
            => _wholeRect() || LinkAt(sp, eventCamera) >= 0;

        public void OnPointerClick(PointerEventData e)
        {
            var link = e.button == PointerEventData.InputButton.Left && GroupsAllowInteraction()
                ? LinkAt(e.position, e.pressEventCamera)
                : -1;
            if (link >= 0)
            {
                _onLink(_tmp.textInfo.linkInfo[link].GetLinkID());
                return;
            }
            // Not a link click. uGUI gave it to the nearest click handler — this one — so without
            // handing it on, a linked text inside a <Btn> would swallow the button's clicks (TL-D6).
            var parent = transform.parent;
            if (parent != null)
                ExecuteEvents.ExecuteHierarchy(parent.gameObject, e, ExecuteEvents.pointerClickHandler);
        }

        private int LinkAt(Vector2 screenPoint, Camera eventCamera)
            => _tmp != null && _tmp.textInfo.linkCount > 0
                ? TMP_TextUtilities.FindIntersectingLink(_tmp, screenPoint, eventCamera)
                : -1;

        // Selectable.ParentGroupAllowsInteraction: interactable="false" on the text itself (its own
        // CanvasGroup) or on any ancestor mutes the links, up to a group that ignores its parents.
        private bool GroupsAllowInteraction()
        {
            for (var t = transform; t != null; t = t.parent)
            {
                t.GetComponents(_groups);
                foreach (var g in _groups)
                {
                    if (g.enabled && !g.interactable) return false;
                    if (g.ignoreParentGroups) return true;
                }
            }
            return true;
        }
    }
}
