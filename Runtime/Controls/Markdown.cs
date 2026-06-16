using System;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class Markdown : Control
    {
        private ScrollRect _scroll;
        private RectTransform _viewport;

        private IControl _renderedRoot;
        // Render-generation token: bumped on each render / dispose; async image loads drop
        // results that arrive after a newer render replaced the subtree.
        private int _renderGen;
        private bool _applied;
        private bool _dirty;
        private string _source = "";
        private MarkdownStyle _style;

        private readonly Subject<string> _linkClicked = new();
        private IDisposable _textSub;
        public Observable<string> OnLinkClicked => _linkClicked;
        public Func<string, Awaitable<Texture2D>> ImageResolver { get; set; }

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

        public MarkdownStyle Style
        {
            get => _style ??= UI.Markdown.DefaultStyle.Clone();
            set { _style = value; MarkDirty(); }
        }

        [UIAttr("text"), Preserve]
        public string Text
        {
            get => _source;
            set { var v = value ?? ""; if (v == _source) return; _source = v; MarkDirty(); }
        }

        internal override string PeekDefaultText() => _source;

        [UIAttr, Preserve]
        public string BodyFont
        {
            set
            {
                var v = string.IsNullOrEmpty(value) ? "default" : value;
                if (v == Style.BodyFont) return;
                Style.BodyFont = v;
                MarkDirty();
            }
        }

        [UIAttr, Preserve]
        public string CodeFont
        {
            set
            {
                var v = string.IsNullOrEmpty(value) ? "default" : value;
                if (v == Style.CodeFont) return;
                Style.CodeFont = v;
                MarkDirty();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string LinkColor
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == Style.LinkColor) return;
                Style.LinkColor = value;
                MarkDirty();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string BodyColor
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == Style.BodyColor) return;
                Style.BodyColor = value;
                MarkDirty();
            }
        }

        [UIAttr, Preserve]
        public float FontSize
        {
            set
            {
                if (value == Style.BodySize) return;
                Style.BodySize = value;
                MarkDirty();
            }
        }

        [UIAttr, Preserve]
        public float Spacing
        {
            set
            {
                if (value == Style.BlockSpacing) return;
                Style.BlockSpacing = value;
                MarkDirty();
            }
        }

        [UIAttr, Preserve]
        public bool Wrap
        {
            set
            {
                if (value == Style.ParagraphWrap) return;
                Style.ParagraphWrap = value;
                MarkDirty();
            }
        }

        public IDisposable BindText(Observable<string> source)
        {
            _textSub?.Dispose();
            _textSub = source.Subscribe(s => Text = s);
            return _textSub;
        }

        internal void RaiseLinkClickedForTests(string url) => _linkClicked.OnNext(url);

        private void InstallLinkClickers(IControl root)
        {
            foreach (var tmp in root.GameObject.GetComponentsInChildren<TMP_Text>(true))
            {
                var clicker = tmp.gameObject.GetComponent<MarkdownLinkClicker>()
                              ?? tmp.gameObject.AddComponent<MarkdownLinkClicker>();
                clicker.Init(tmp, url => _linkClicked.OnNext(url));
            }
        }

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
                LayoutRebuilder.ForceRebuildLayoutImmediate(_renderedRoot.RectTransform);
                return;
            }

            var result = renderer.Render(_source, Style);
            _renderedRoot = inst.InstantiateNode(result.Root, _viewport, owner);
            SetAsContent(_renderedRoot);
            InstallLinkClickers(_renderedRoot);
            if (result.Images != null)
                foreach (var req in result.Images)
                    _ = LoadImageAsync(_renderGen, req);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_renderedRoot.RectTransform);
        }

        private async Awaitable LoadImageAsync(int gen, ImageRequest req)
        {
            var resolver = ImageResolver ?? UI.Markdown.ImageResolver;
            if (resolver == null) return;   // alt placeholder stays
            Texture2D tex;
            try { tex = await resolver(req.Url); }
            catch (Exception e)
            {
                Debug.LogWarning($"<Markdown> image '{req.Url}' failed: {e.Message}");
                return;
            }
            if (gen != _renderGen || tex == null || _renderedRoot == null) return;   // stale / failed
            RawImage img;
            try { img = _renderedRoot.Get<RawImage>(req.NodeId); }
            catch { return; }
            img.Texture = tex;
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
            _textSub?.Dispose();
            _linkClicked.Dispose();
            if (_renderedRoot != null) { _renderedRoot.Dispose(); _renderedRoot = null; }
            base.Dispose();
        }
    }
}
