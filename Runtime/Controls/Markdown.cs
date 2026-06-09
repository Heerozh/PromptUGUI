using System;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Markdown : Control
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;

        // --- Task 4 fields ---
        private IControl _renderedRoot;
        // Render-generation token: bumped on each render / dispose; read by async image loading
        // (a later task) to drop results that arrive after a newer render replaced the subtree.
        private int _renderGen;
        private bool _applied;
        private bool _dirty;
        private string _source = "";
        private MarkdownStyle _style;

        public override void OnAttached()
        {
            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = GameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.viewport = _viewport;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;
        }

        protected internal override Transform ChildHostTransform => _viewport;

        // --- Task 4 members ---

        public MarkdownStyle Style
        {
            get => _style ??= UI.Markdown.DefaultStyle.Clone();
            set { _style = value; MarkDirty(); }
        }

        [UIAttr("text"), Preserve]
        public string Text
        {
            get => _source;
            set { _source = value ?? ""; MarkDirty(); }
        }

        internal override string PeekDefaultText() => _source;

        [UIAttr, Preserve]
        public string BodyFont { set { Style.BodyFont = string.IsNullOrEmpty(value) ? "default" : value; MarkDirty(); } }

        [UIAttr, Preserve]
        public string CodeFont { set { Style.CodeFont = string.IsNullOrEmpty(value) ? "default" : value; MarkDirty(); } }

        [UIAttr(IsColor = true), Preserve]
        public string LinkColor { set { if (!string.IsNullOrEmpty(value)) Style.LinkColor = value; MarkDirty(); } }

        [UIAttr, Preserve]
        public float Spacing { set { Style.BlockSpacing = value; MarkDirty(); } }

        [UIAttr, Preserve]
        public bool Wrap { set { Style.ParagraphWrap = value; MarkDirty(); } }

        internal override void OnAfterApply()
        {
            _applied = true;
            if (_dirty) Render();
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (_applied) Render();
        }

        private void Render()
        {
            _dirty = false;
            _renderGen++;
            if (_renderedRoot != null) { _renderedRoot.Dispose(); _renderedRoot = null; }
            if (string.IsNullOrEmpty(_source)) return;

            var inst = UI.GetInstantiator();
            var owner = UI.OwnerScreenOf(this);
            var renderer = UI.Markdown.Renderer;

            if (renderer == null)
            {
                Debug.LogWarning("<Markdown> needs Markdig. Install it (NuGetForUnity / DLL); the editor " +
                    "auto-defines PROMPTUGUI_HAS_MARKDIG when found. Showing raw text.");
                var raw = new ElementNode("Text");
                raw.Attributes["wrap"] = "true";
                raw.Attributes["anchor"] = "top-stretch";
                raw.Attributes["tr"] = "false";
                raw.TextContent = _source;
                _renderedRoot = inst.InstantiateNode(raw, _viewport, owner);
                SetAsContent(_renderedRoot);
                return;
            }

            var result = renderer.Render(_source, Style);
            _renderedRoot = inst.InstantiateNode(result.Root, _viewport, owner);
            SetAsContent(_renderedRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_renderedRoot.RectTransform);
        }

        private void SetAsContent(IControl root)
        {
            _scroll.content = root.RectTransform;
            var csf = root.GameObject.GetComponent<ContentSizeFitter>()
                      ?? root.GameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.verticalNormalizedPosition = 1f;
        }

        public override void Dispose()
        {
            _renderGen++;
            if (_renderedRoot != null) { _renderedRoot.Dispose(); _renderedRoot = null; }
            base.Dispose();
        }
    }
}
