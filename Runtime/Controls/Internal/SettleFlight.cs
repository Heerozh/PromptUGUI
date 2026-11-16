using System.Collections.Generic;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The landing animations of drag-and-drop (spec 2026-11-16-drag-drop §7.6): FLIP over a structure
    /// that is already final. The element is where the layout (or its own geometry) puts it; a flight
    /// shows it travelling there from somewhere else — the point it was dropped at, the slot it was
    /// displaced from — writing only <c>anchoredPosition</c> / <c>localScale</c>, lifted while in the air.
    /// A layout rebuild from elsewhere can only make it arrive early.
    ///
    /// <para>One flight per RectTransform: a new one, or a lift of the same element, first lands the
    /// running one. Unscaled time (a pause menu still animates). Instant when the duration is 0 or the
    /// editor is not playing — EditMode tests assert the end state in the same frame.</para>
    /// </summary>
    internal static class SettleFlight
    {
        private sealed class Flight
        {
            internal MotionHandle Position;
            internal MotionHandle Scale;
            internal Vector2 To;
            internal Vector3 ScaleTo;
            internal LiftLayer.Token Layer;
        }

        private static readonly Dictionary<RectTransform, Flight> s_flights = new();

        internal static bool IsFlying(RectTransform rt) => rt != null && s_flights.ContainsKey(rt);

        /// <summary>
        /// <paramref name="rt"/> flies from <paramref name="fromAnchored"/> (and <paramref name="fromScale"/>) to where
        /// it is now. <paramref name="layer"/> is a lift it already has (handed over, lowered on landing); null
        /// raises one for the flight.
        /// </summary>
        internal static void Fly(RectTransform rt, Vector2 fromAnchored, Vector3? fromScale, float duration,
                                 LiftLayer.Token layer, int sortingOrder)
        {
            if (rt == null)
            {
                LiftLayer.Lower(layer);
                return;
            }
            Complete(rt);
            var to = rt.anchoredPosition;
            var scaleTo = rt.localScale;
            var still = (to - fromAnchored).sqrMagnitude < 0.01f
                        && (!fromScale.HasValue || (fromScale.Value - scaleTo).sqrMagnitude < 1e-6f);
            if (duration <= 0f || !UnityEngine.Application.isPlaying || still)
            {
                rt.anchoredPosition = to;
                rt.localScale = scaleTo;
                LiftLayer.Lower(layer);
                return;
            }

            var flight = new Flight
            {
                To = to,
                ScaleTo = scaleTo,
                Layer = layer ?? LiftLayer.Raise(rt, sortingOrder),
            };
            rt.anchoredPosition = fromAnchored;
            flight.Position = LMotion.Create(fromAnchored, to, duration)
                .WithEase(Ease.OutCubic)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => Landed(rt))
                .Bind(rt, static (v, r) => { if (r != null) r.anchoredPosition = v; });
            if (fromScale.HasValue)
            {
                rt.localScale = fromScale.Value;
                flight.Scale = LMotion.Create(fromScale.Value, scaleTo, duration)
                    .WithEase(Ease.OutCubic)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .Bind(rt, static (v, r) => { if (r != null) r.localScale = v; });
            }
            s_flights[rt] = flight;
        }

        /// <summary>
        /// <paramref name="rt"/> flies in from <paramref name="fromScreen"/> (a rect in screen pixels — from another
        /// Screen, scale factor or camera alike). <paramref name="morph"/> also grows / shrinks it from that rect's size.
        /// </summary>
        internal static void FlyFrom(RectTransform rt, Rect fromScreen, bool morph, float duration, int sortingOrder,
                                     LiftLayer.Token layer = null)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy)
            {
                LiftLayer.Lower(layer);
                return;
            }
            Complete(rt);
            var parent = rt.parent as RectTransform;
            if (parent == null)
            {
                LiftLayer.Lower(layer);
                return;
            }
            // The answer the layout gives AFTER whatever the drop changed.
            if (parent.GetComponent<LayoutGroup>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

            var cam = LiftLayer.CameraOf(rt);
            var nowScreen = LiftLayer.ScreenRect(rt, cam);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, fromScreen.center, cam, out var fromCenter)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, nowScreen.center, cam, out var nowCenter))
            {
                LiftLayer.Lower(layer);
                return;
            }

            var scale = rt.localScale;
            Vector3? fromScale = null;
            if (morph && nowScreen.width > 0.5f)
            {
                var ratio = Mathf.Clamp(fromScreen.width / nowScreen.width, 0.25f, 4f);
                if (Mathf.Abs(ratio - 1f) > 0.01f) fromScale = new Vector3(scale.x * ratio, scale.y * ratio, scale.z);
            }

            // anchoredPosition places the pivot; the centres line up, so take the pivot's offset from the centre
            // at each scale into account (a top-left anchored control has its pivot at a corner).
            var size = rt.rect.size;
            var pivot = rt.pivot;
            Vector2 CentreFromPivot(Vector3 s) => new((0.5f - pivot.x) * size.x * s.x, (0.5f - pivot.y) * size.y * s.y);
            var pivotNow = nowCenter - CentreFromPivot(scale);
            var pivotFrom = fromCenter - CentreFromPivot(fromScale ?? scale);
            Fly(rt, rt.anchoredPosition + (pivotFrom - pivotNow), fromScale, duration, layer, sortingOrder);
        }

        /// <summary>The running flight of <paramref name="rt"/>, if any, ends on its destination now.</summary>
        internal static void Complete(RectTransform rt)
        {
            if (rt is null || !s_flights.TryGetValue(rt, out var f)) return;
            s_flights.Remove(rt);
            if (f.Position.IsActive()) f.Position.TryCancel();
            if (f.Scale.IsActive()) f.Scale.TryCancel();
            if (rt != null)
            {
                rt.anchoredPosition = f.To;
                rt.localScale = f.ScaleTo;
            }
            LiftLayer.Lower(f.Layer);
        }

        internal static void CompleteAll()
        {
            if (s_flights.Count == 0) return;
            foreach (var rt in new List<RectTransform>(s_flights.Keys)) Complete(rt);
        }

        private static void Landed(RectTransform rt)
        {
            if (!s_flights.TryGetValue(rt, out var f)) return;
            s_flights.Remove(rt);
            LiftLayer.Lower(f.Layer);
        }
    }
}
