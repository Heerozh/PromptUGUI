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
    /// cells each under one would be a hundred draw calls. A canvas this class added stays on the
    /// GameObject <b>disabled</b> between lifts — a disabled canvas is no canvas to uGUI, its graphics batch
    /// with the parent again — because a <c>Destroy</c> is deferred to the end of the frame in play mode, and
    /// a lift right after a landing (same frame) would see its new layer destroyed under it.</para>
    ///
    /// <para>The canvas gets no <c>GraphicRaycaster</c>: uGUI collects hit-testable graphics per canvas, so
    /// the lifted subtree is invisible to the pointer and whatever lies under it can be hit.</para>
    /// </summary>
    internal static class LiftLayer
    {
        /// <summary>Tags a canvas this class added, so a later lift reuses it instead of treating it as the control's own.</summary>
        internal sealed class Mark : MonoBehaviour
        {
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
            if (canvas == null)
            {
                canvas = go.AddComponent<Canvas>();
                go.AddComponent<Mark>();
                token.Ours = true;
            }
            else if (go.GetComponent<Mark>() != null)
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
            if (token == null || token.Canvas == null) return;
            var canvas = token.Canvas;
            token.Canvas = null;
            if (token.Ours)
            {
                canvas.overrideSorting = false;
                canvas.enabled = false;
                return;
            }
            canvas.overrideSorting = token.PrevOverride;
            canvas.sortingOrder = token.PrevOrder;
            canvas.sortingLayerID = token.PrevLayer;
            canvas.additionalShaderChannels = token.PrevChannels;
        }

        /// <summary>The camera a control's canvas renders with — null for Screen Space - Overlay.</summary>
        internal static Camera CameraOf(Transform t)
        {
            if (t == null) return null;
            var root = RootOf(t);
            if (root == null) return null;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        /// <summary>The outermost canvas above <paramref name="t"/> (its Screen's) — not <c>rootCanvas</c>, which a disabled lift canvas cannot be trusted to answer.</summary>
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
