using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Lifts a control above every Screen without moving it out of its own (spec 2026-11-16-drag-drop
    /// §7.2): a nested <see cref="Canvas"/> with <c>overrideSorting</c> on the control itself — the
    /// <c>PopupCore</c> trick. It draws at <c>UI.Drag.SortingOrder</c> over every root canvas, and uGUI's
    /// stencil and <c>RectMask2D</c> lookups both stop at an overriding canvas, so no ancestor mask (a list's
    /// viewport) cuts it. The control keeps its parent, its geometry, its ids and its Screen.
    ///
    /// <para>Never resident while idle: every enabled canvas is a batch root of its own, and a hundred bag
    /// cells each under one would be a hundred draw calls. Lowering takes a canvas this class added
    /// <b>off</b> — never merely disables it. A disabled nested canvas keeps its whole subtree out of every
    /// batch (the renderers stay bound to it natively, <c>absoluteDepth</c> -1) while <c>Graphic.canvas</c>
    /// already names the parent, and destroying it later does not bring them back. Nor can it stay enabled with
    /// <c>overrideSorting</c> off: Unity drops that write while the GameObject is inactive (a source the drop's
    /// answer hid).</para>
    ///
    /// <para>In play mode the take-off is a <c>Destroy</c>, which always lands before the frame renders: lowering
    /// is reached from <c>OnDestroy</c> (a source destroyed mid-drag) and from structure changes inside
    /// activation callbacks, where Unity refuses a <c>DestroyImmediate</c>. Until then the canvas sorts with its
    /// parent, and a lift in the same frame replaces it at once (<see cref="Raise"/>).</para>
    ///
    /// <para>The canvas gets no <c>GraphicRaycaster</c>: uGUI collects hit-testable graphics per canvas, so
    /// the lifted subtree is invisible to the pointer and whatever lies under it can be hit.</para>
    /// </summary>
    internal static class LiftLayer
    {
        /// <summary>
        /// Tags a canvas this class added. <see cref="Doomed"/>: lowered this frame, destroyed at its end.
        /// </summary>
        internal sealed class Mark : MonoBehaviour
        {
            internal bool Doomed;
        }

        /// <summary>
        /// Gives a control's own canvas its <c>overrideSorting</c> back once the GameObject is active again — Unity
        /// dropped the write while it was inactive. Spent after one go.
        /// </summary>
        [ExecuteAlways]
        internal sealed class RestoreOnEnable : MonoBehaviour
        {
            internal Canvas Canvas;
            internal bool Override;

            private void OnEnable()
            {
                if (Canvas == null) return;
                Canvas.overrideSorting = Override;
                Canvas = null;
                // An activation: Destroy is allowed here, DestroyImmediate is not. In edit mode it stays, spent.
                if (UnityEngine.Application.isPlaying) Destroy(this);
            }
        }

        /// <summary>What <see cref="Raise"/> did, so <see cref="Lower"/> can undo exactly that.</summary>
        internal sealed class Token
        {
            internal Canvas Canvas;
            internal bool Ours;
            internal bool PrevOverride;
            internal int PrevOrder;
            internal int PrevLayer;
            internal AdditionalCanvasShaderChannels PrevChannels;
        }

        internal static Token Raise(RectTransform host, int sortingOrder)
        {
            if (host == null) return null;
            var go = host.gameObject;
            var parentCanvas = NearestEnabledCanvas(host.parent);
            var root = RootOf(host);

            var token = new Token();
            // Explicit null checks, not `??`: in the Editor a missing component is a fake null that `??` keeps.
            var canvas = go.GetComponent<Canvas>();
            var mark = go.GetComponent<Mark>();
            if (canvas != null && mark != null && mark.Doomed)
            {
                // Lowered earlier this frame and on its way out: its Destroy would take this lift with it.
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(mark);
                canvas = null;
                mark = null;
            }
            if (canvas == null)
            {
                canvas = go.AddComponent<Canvas>();
                go.AddComponent<Mark>();
                token.Ours = true;
            }
            else if (mark != null)
            {
                token.Ours = true;
            }
            else
            {
                token.PrevOverride = canvas.overrideSorting;
                token.PrevOrder = canvas.sortingOrder;
                token.PrevLayer = canvas.sortingLayerID;
                token.PrevChannels = canvas.additionalShaderChannels;
            }
            token.Canvas = canvas;
            canvas.enabled = true;

            // A procedural panel only opens its shader channels on its NEAREST canvas: without the parent's
            // TexCoord1 / TexCoord2 the panel reads zeros and collapses the moment it is lifted.
            if (parentCanvas != null) canvas.additionalShaderChannels |= parentCanvas.additionalShaderChannels;
            if (root != null)
            {
                canvas.vertexColorAlwaysGammaSpace = root.vertexColorAlwaysGammaSpace;
                canvas.sortingLayerID = root.sortingLayerID;
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            return token;
        }

        internal static void Lower(Token token)
        {
            if (token == null) return;
            var canvas = token.Canvas;
            token.Canvas = null;
            if (canvas == null) return;   // went with its GameObject
            if (token.Ours)
            {
                TakeOff(canvas);
                return;
            }
            canvas.sortingOrder = token.PrevOrder;
            canvas.sortingLayerID = token.PrevLayer;
            canvas.additionalShaderChannels = token.PrevChannels;
            canvas.overrideSorting = token.PrevOverride;
            if (canvas.overrideSorting != token.PrevOverride)
            {
                // Inactive (the drop's answer hid it): Unity dropped the write — it is made when it shows again.
                var restore = canvas.GetComponent<RestoreOnEnable>();
                if (restore == null) restore = canvas.gameObject.AddComponent<RestoreOnEnable>();
                restore.Canvas = canvas;
                restore.Override = token.PrevOverride;
            }
        }

        /// <summary>Removes a canvas this class added (see the class remarks for why never "disabled").</summary>
        private static void TakeOff(Canvas canvas)
        {
            var go = canvas.gameObject;
            var mark = go.GetComponent<Mark>();
            if (!UnityEngine.Application.isPlaying)
            {
                Object.DestroyImmediate(canvas);
                if (mark != null) Object.DestroyImmediate(mark);
                return;
            }
            canvas.overrideSorting = false;   // sorts with its parent until it goes (dropped while inactive — gone before it shows)
            if (mark != null)
            {
                mark.Doomed = true;
                Object.Destroy(mark);
            }
            Object.Destroy(canvas);
        }

        /// <summary>The camera a control's canvas renders with — null for Screen Space - Overlay.</summary>
        internal static Camera CameraOf(Transform t)
        {
            if (t == null) return null;
            var root = RootOf(t);
            if (root == null) return null;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        /// <summary>The outermost canvas above <paramref name="t"/> — its Screen's.</summary>
        internal static Canvas RootOf(Transform t)
        {
            Canvas root = null;
            for (var p = t; p != null; p = p.parent)
            {
                var c = p.GetComponent<Canvas>();
                if (c != null) root = c;
            }
            return root;
        }

        private static Canvas NearestEnabledCanvas(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                var c = p.GetComponent<Canvas>();
                if (c != null && c.enabled) return c;
            }
            return null;
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>A RectTransform's bounds in screen pixels.</summary>
        internal static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            rt.GetWorldCorners(s_corners);
            var min = RectTransformUtility.WorldToScreenPoint(cam, s_corners[0]);
            var max = min;
            for (var i = 1; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, s_corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
