using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Gives a control a procedural backing layer: a stretched <see cref="ProceduralPanel"/> child
    /// under the control's own content, with the control's primary <c>Image</c> standing down while
    /// it draws.
    ///
    /// <para><b>Why a child and not the Image itself.</b> <c>Graphic</c> is
    /// <c>[DisallowMultipleComponent]</c>, so a panel cannot share a GameObject with the Image;
    /// swapping one for the other would mean <c>Destroy</c> + <c>AddComponent</c> at runtime, which
    /// is exactly what <c>PUI-MASK-VARIANT</c> refuses — it would take the control's
    /// <c>targetGraphic</c>, its state reactor's captured base colour and any stencil with it. The
    /// shape is not new either: <c>GlassGroupPanel.Attach</c> already ships this exact arrangement
    /// (own GameObject, stretched, <c>raycastTarget=false</c>, sibling index 0) for weld.</para>
    ///
    /// <para><b>Nothing is ever destroyed.</b> The node is created on first demand and afterwards only
    /// toggled — the Add-block Strategy C rule — so a Variant flipping procedural mode on and off
    /// round-trips exactly, and the retired Image comes back with the sprite it had.</para>
    ///
    /// <para><b>Mode is recomputed every pass, never latched.</b> A variant-only attribute
    /// (<c>radius.mobile="8"</c> with no base) means the mode genuinely turns off again, and
    /// <c>ControlAttributeApplier</c> signals that by <em>not</em> calling the setter. So
    /// <see cref="BeginPass"/> clears the flag, each setter re-declares, and
    /// <see cref="Reconcile"/> acts on what is true now. Same defect shape as
    /// <c>Btn.ReconcileTransition</c> and <c>StateTintReactor</c>'s base colour.</para>
    /// </summary>
    internal sealed class ProceduralSurface
    {
        /// <summary>Follows the shipped <c>__FocusCursor</c> convention for library-owned nodes.</summary>
        internal const string NodeName = "__Surface";

        private readonly GameObject _host;
        private readonly Selectable _selectable;

        private GameObject _node;
        private ProceduralPanel _panel;
        private UnityImage _hostImage;
        private Graphic _originalTarget;

        private Sprite _retiredSprite;
        private UnityImage.Type _retiredType;
        private Color _retiredColor;
        private bool _retired;

        private bool _declaredThisPass;
        private bool _active;

        /// <param name="host">
        /// The GameObject whose <c>Image</c> is the control's primary surface — <c>Btn</c>'s own
        /// node, <c>Toggle</c>'s <c>Background</c> child, and so on. The panel becomes a child of it,
        /// so it covers exactly what the Image covered.
        /// </param>
        /// <param name="selectable">
        /// The control's <c>Selectable</c>, if it has one: its <c>targetGraphic</c> has to follow
        /// whichever Graphic is currently visible, or the state colours drive a hidden layer.
        /// </param>
        internal ProceduralSurface(GameObject host, Selectable selectable)
        {
            _host = host;
            _selectable = selectable;
            _hostImage = host != null ? host.GetComponent<UnityImage>() : null;
            _originalTarget = selectable != null ? selectable.targetGraphic : null;
        }

        internal bool IsActive => _active;
        internal ProceduralPanel Panel => _panel;

        internal void BeginPass()
        {
            _declaredThisPass = false;
            _hasFill = false;
        }

        /// <summary>
        /// Routes one procedural attribute to the panel, creating it on first use. Calling this at
        /// all is what declares procedural mode for this pass.
        /// </summary>
        internal void Declare(System.Action<ProceduralPanel> write)
        {
            _declaredThisPass = true;
            write(EnsurePanel());
        }

        /// <summary>
        /// The fill, which is <c>color=</c> in both modes (spec §7) and therefore not a declaration
        /// of procedural mode by itself — on an Image-backed control <c>color</c> is an ordinary
        /// tint. Remembered either way so a later mode flip can hand it to the right layer.
        /// </summary>
        internal void SetFill(Color top, Color bottom)
        {
            _fillTop = top;
            _fillBottom = bottom;
            _hasFill = true;
            if (_panel != null) _panel.SetFill(top, bottom);
        }

        private Color _fillTop;
        private Color _fillBottom;
        private bool _hasFill;

        /// <summary>Applies whatever the pass declared. Idempotent; safe to call every ReSolve.</summary>
        internal void Reconcile()
        {
            var on = _declaredThisPass;
            if (on) EnsurePanel();

            if (_node != null && _node.activeSelf != on) _node.SetActive(on);
            _active = on;

            if (on)
            {
                // The panel is the only thing drawing, so it inherits the control's colour — with no
                // explicit color= that is the control's built-in default, which is why
                // `<Btn radius="8">` is a rounded button rather than an invisible one. Read through
                // the captured value once retired: the live Image's alpha is zero by then.
                if (_hasFill) _panel.SetFill(_fillTop, _fillBottom);
                else if (_hostImage != null)
                {
                    var c = _retired ? _retiredColor : _hostImage.color;
                    _panel.SetFill(c, c);
                }
                Retire();
            }
            else
            {
                Restore();
            }

            if (_selectable != null)
            {
                var target = on && _panel != null ? (Graphic)_panel : _originalTarget;
                if (_selectable.targetGraphic != target) _selectable.targetGraphic = target;
            }
        }

        /// <summary>
        /// Stands the Image down without destroying it: sprite cleared (spec §7 — a bitmap under an
        /// SDF face is a mess, and the one cleared here is the control's own default, not an author
        /// declaration) and alpha zeroed.
        ///
        /// <para>Zeroed, NOT disabled. uGUI only raycasts against enabled Graphics, and the panel is
        /// deliberately <c>raycastTarget=false</c> — disabling the Image would leave the control with
        /// no hit target at all and silently stop it responding to clicks.</para>
        ///
        /// <para>Captured once but re-applied every pass: the control's own <c>color=</c> setter runs
        /// before <see cref="Reconcile"/> and would otherwise put the alpha straight back.</para>
        /// </summary>
        private void Retire()
        {
            if (_hostImage == null) return;
            if (!_retired)
            {
                _retiredSprite = _hostImage.sprite;
                _retiredType = _hostImage.type;
                _retiredColor = _hostImage.color;
                _retired = true;
            }
            _hostImage.sprite = null;
            var c = _hostImage.color;
            _hostImage.color = new Color(c.r, c.g, c.b, 0f);
        }

        private void Restore()
        {
            if (!_retired || _hostImage == null) return;
            _retired = false;
            _hostImage.sprite = _retiredSprite;
            _hostImage.type = _retiredType;
            // Alpha only: the rgb may legitimately have moved on (a theme switch, a Variant) while
            // the surface was drawing, and that newer colour is the right one to come back to.
            var c = _hostImage.color;
            _hostImage.color = new Color(c.r, c.g, c.b, _retiredColor.a);
        }

        private ProceduralPanel EnsurePanel()
        {
            if (_panel != null) return _panel;

            _node = new GameObject(NodeName, typeof(RectTransform)) { layer = _host.layer };
            var rt = (RectTransform)_node.transform;
            rt.SetParent(_host.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            // Under the control's own content (label, checkmark, arrow) and under author children.
            rt.SetSiblingIndex(0);

            _panel = _node.AddComponent<ProceduralPanel>();
            return _panel;
        }
    }
}
