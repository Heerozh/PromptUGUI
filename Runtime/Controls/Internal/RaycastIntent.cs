using PromptUGUI.Application;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Decides a Graphic's <c>raycastTarget</c> from two inputs that arrive in no particular order:
    /// what the author wrote (<c>raycastTarget=</c>, replayed on every ReSolve) and whether anyone
    /// subscribed to the control's pointer streams (a <c>&lt;Trigger on="hover-enter@…"&gt;</c> or
    /// a C# <c>OnPointerDown.Subscribe</c>, both of which go through the control's relay).
    ///
    /// <para>The author wins. A subscriber on a Graphic the author marked click-through is a
    /// contradiction — the stream can never fire — so it is reported once, instead of the
    /// subscription silently flipping the hit test back on (the failure PE-D12 could only document).
    /// With nothing authored, a subscriber is the intent: the stream is what the author asked for,
    /// and a stream nobody can hit is the same silent failure from the other side.</para>
    ///
    /// <para>Owned by <c>Image</c> / <c>RawImage</c>, which default their Graphic to
    /// click-through (2026-09-15 spec §3) — uGUI's own default is true, which is how every
    /// decorative picture in a list used to sit in the raycast list.</para>
    /// </summary>
    internal sealed class RaycastIntent
    {
        private readonly Graphic _graphic;
        private readonly Control _owner;
        private bool? _authored;
        private bool _wanted;
        private bool _warned;

        internal RaycastIntent(Graphic graphic, Control owner)
        {
            _graphic = graphic;
            _owner = owner;
            _graphic.raycastTarget = false;
        }

        /// <summary>
        /// Start of an attribute pass: forget what the author wrote last time. A setter that does
        /// not run this pass means the attribute is gone (a variant-only declaration whose variant
        /// just left), and that must read as "unauthored", not as the stale value.
        /// </summary>
        internal void BeginPass()
        {
            _authored = null;
        }

        /// <summary>The <c>raycastTarget=</c> attribute; replayed on every ReSolve.</summary>
        internal void SetAuthored(bool value)
        {
            _authored = value;
            Apply();
        }

        /// <summary>End of the pass: settle the value from whatever the pass declared.</summary>
        internal void EndPass() => Apply();

        /// <summary>A pointer subscriber exists. Sticky: a subscription is never un-wanted.</summary>
        internal void Want()
        {
            if (_wanted) return;
            _wanted = true;
            Apply();
        }

        private void Apply()
        {
            _graphic.raycastTarget = _authored ?? _wanted;
            if (_authored == false && _wanted && !_warned)
            {
                _warned = true;
                UILog.Warn(_owner,
                    $"<{_owner.GetType().Name} raycastTarget=\"false\"> is used as a pointer event source " +
                    "(a hover / press trigger or an OnPointer* subscription), so its pointer events can " +
                    "never arrive. Drop raycastTarget=\"false\", or pick another source.");
            }
        }
    }
}
