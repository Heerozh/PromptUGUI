using System;
using System.Collections.Generic;
using System.Linq;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Dropdown : ProceduralControl
    {
        private UnityImage _bg;

        private protected override GameObject SurfaceHost => GameObject;
        private protected override UnityEngine.UI.Selectable SurfaceSelectable => _tmp;
        private UnityImage _templateBg;
        // 内部图层。TMP_Dropdown 每次展开都从 template 子树克隆选项行，所以改 _itemBg /
        // _itemCheckmark / _itemLabel 会作用于之后所有选项；已展开的那批实例不受影响。
        private UnityImage _arrow;
        private UnityImage _itemBg;
        private UnityImage _itemCheckmark;
        private TMP_Text _itemLabel;
        private UnityImage _scrollbarBg;
        private UnityImage _scrollbarHandle;
        private RectTransform _popupViewport;
        private bool _popupMaskExplicit;
        private TMP_Dropdown _tmp;
        private string _fontType = "default";
        private readonly Subject<int> _selected = new();

        // DSS-D2: Dropdown 不读 caption 来算 native（caption 会随用户选项变化，UX 会跳）。
        // 固定默认覆盖"作者忘写 size"的可见性问题；显式 size 仍胜出。
        private const float MinTapHeight = 44f;
        private const float DefaultDropdownWidth = 160f;

        public override Vector2? GetNativeSize()
            => new Vector2(DefaultDropdownWidth, MinTapHeight);

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _tmp = GameObject.AddComponent<TMP_Dropdown>();
            _tmp.targetGraphic = _bg;

            // Caption (the always-visible label on the closed dropdown button).
            var label = ProceduralBuilders.AddText(RectTransform, "Label");
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(10f, 6f);
            label.rectTransform.offsetMax = new Vector2(-25f, -7f);
            _tmp.captionText = label;

            // Caret on the right edge.
            _arrow = ProceduralBuilders.AddImage(RectTransform, "Arrow", raycast: false);
            var arrow = _arrow;
            arrow.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(arrow, ProceduralBuilders.SpriteCaret);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(20f, 20f);
            arrow.rectTransform.anchoredPosition = new Vector2(-15f, 0f);

            // Template (popup root, anchored to dropdown's bottom edge so it grows downward).
            var template = ProceduralBuilders.AddChild(RectTransform, "Template");
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, 150f);
            template.anchoredPosition = new Vector2(0f, 2f);
            template.gameObject.SetActive(false);
            _templateBg = template.gameObject.AddComponent<UnityImage>();
            _templateBg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_templateBg);
            var templateScroll = template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;

            // Viewport: popup viewport mask 三态 + 默认 pugui_9slice_round，spec §2.3。
            // CRITICAL: alpha 必须为 1。alpha=0.01 会触发 UI/Default shader 的 alpha-discard，
            // 把 stencil 写飞 (4af322b 之前的 bug)。
            _popupViewport = ProceduralBuilders.AddChild(template, "Viewport");
            _popupViewport.anchorMin = new Vector2(0f, 0f);
            _popupViewport.anchorMax = new Vector2(1f, 1f);
            _popupViewport.pivot = new Vector2(0f, 1f);
            _popupViewport.offsetMin = Vector2.zero;
            _popupViewport.offsetMax = Vector2.zero;
            _popupViewport.sizeDelta = new Vector2(-18f, 0f);  // 留 18px 给 Vertical Scrollbar
            ProceduralBuilders.ApplyViewportMask(_popupViewport, null, ProceduralBuilders.SpriteRoundedRect);

            // Content (top-anchored; height grows to fit items via TMP_Dropdown's runtime sizing).
            var content = ProceduralBuilders.AddChild(_popupViewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 28f);
            content.anchoredPosition = Vector2.zero;

            // Item template (cloned per option; fixed height + horizontal stretch).
            const float itemHeight = 20f;
            var item = ProceduralBuilders.AddChild(content, "Item");
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(0f, itemHeight);

            // Item Background: 独立子节点，simple + 无 sprite + #F5F5F5 (highlighted-tinted 色带)
            var itemBgRt = ProceduralBuilders.AddChild(item, "Item Background");
            _itemBg = itemBgRt.gameObject.AddComponent<UnityImage>();
            var itemBg = _itemBg;
            itemBg.type = UnityImage.Type.Simple;
            itemBg.sprite = null;
            itemBg.color = new UnityEngine.Color(0.961f, 0.961f, 0.961f, 1f);
            var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            itemToggle.targetGraphic = itemBg;

            // Item checkmark anchored on the left side of the item.
            _itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
            var itemCheckmark = _itemCheckmark;
            itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
            itemCheckmark.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            itemCheckmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
            itemCheckmark.rectTransform.anchoredPosition = new Vector2(10f, 0f);
            itemToggle.graphic = itemCheckmark;

            // Item label fills the rest of the item.
            _itemLabel = ProceduralBuilders.AddText(item, "Item Label");
            var itemLabel = _itemLabel;
            itemLabel.alignment = TextAlignmentOptions.Left;
            itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            itemLabel.rectTransform.offsetMin = new Vector2(20f, 1.5f);
            itemLabel.rectTransform.offsetMax = new Vector2(-10f, -1.5f);

            // Scrollbar Vertical (default prefab 在 Template 内有这个子树)
            var scrollbarRt = ProceduralBuilders.AddChild(template, "Scrollbar");
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 1f);
            scrollbarRt.sizeDelta = new Vector2(20f, 0f);
            scrollbarRt.anchoredPosition = Vector2.zero;
            _scrollbarBg = scrollbarRt.gameObject.AddComponent<UnityImage>();
            var scrollbarBg = _scrollbarBg;
            scrollbarBg.color = ProceduralBuilders.DefaultControlBgColor; // white
            ProceduralBuilders.ApplyDefaultInsetSprite(scrollbarBg);
            var scrollbar = scrollbarRt.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.value = 0f;
            scrollbar.size = 0.2f;

            var slidingArea = ProceduralBuilders.AddChild(scrollbarRt, "Sliding Area");
            slidingArea.sizeDelta = new Vector2(-20f, -20f);

            _scrollbarHandle = ProceduralBuilders.AddImage(slidingArea, "Handle");
            var sbHandle = _scrollbarHandle;
            sbHandle.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultSlicedSprite(sbHandle);
            sbHandle.rectTransform.anchorMin = new Vector2(0f, 0f);
            sbHandle.rectTransform.anchorMax = new Vector2(1f, 0.2f);
            sbHandle.rectTransform.sizeDelta = new Vector2(20f, 20f);
            sbHandle.rectTransform.anchoredPosition = Vector2.zero;
            scrollbar.targetGraphic = sbHandle;
            scrollbar.handleRect = sbHandle.rectTransform;

            templateScroll.verticalScrollbar = scrollbar;
            templateScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            templateScroll.verticalScrollbarSpacing = -3f;

            templateScroll.viewport = _popupViewport;
            templateScroll.content = content;

            _tmp.template = template;
            _tmp.itemText = itemLabel;

            _tmp.onValueChanged.AddListener(i => _selected.OnNext(i));
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_tmp == null) return;
            FontApplier.Apply(_tmp.captionText, _fontType);
            FontApplier.Apply(_tmp.itemText, _fontType);
        }

        [UIAttr, Preserve]
        public int Value
        {
            get => _tmp.value;
            set => _tmp.value = value;
        }

        internal override string PeekRuntimeState() =>
            _tmp != null ? _tmp.value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec.Top, spec.Bottom);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string PopupSprite
        {
            set
            {
                _templateBg.sprite = UI.ResolveSprite(value);
                ProceduralBuilders.AutoSlice(_templateBg);
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string PopupColor
        {
            set => Internal.ColorApplier.Apply(_templateBg, UI.Theme.ResolveSpec(value));
        }

        [UIAttr(IsSprite = true), Preserve]
        public string PopupMask
        {
            set
            {
                _popupMaskExplicit = true;
                ProceduralBuilders.ApplyViewportMask(
                    _popupViewport, value, ProceduralBuilders.SpriteRoundedRect);
            }
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            // popupMask 未显式写时跟随 popup bg sprite：有图→圆角 stencil，popupSprite=""→直角 RectMask2D
            // （对齐 ScrollList.mask / InputField 的 mask-tracks-border 先例；显式 popupMask= 一旦写过即跳过）。
            if (!_popupMaskExplicit)
                ProceduralBuilders.ApplyViewportMask(
                    _popupViewport, _templateBg != null && _templateBg.sprite != null ? null : "",
                    ProceduralBuilders.SpriteRoundedRect);
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        /// <summary>Caption text color (theme token / hex / CSS / gradient / <c>/alpha</c>);
        /// distinct from the background <c>color</c>. Empty = default ink.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => LabelColorApplier.Apply(_tmp.captionText, value);
        }

        // 内部图层：与 <Progress> 同一套命名规约 —— 每层一对 `<layer>` (sprite) + `<layer>Color`。
        // 弹窗内的三层（item 高亮带 / item 对勾 / item 文字）改的是 TMP_Dropdown 的 template，
        // 之后每次展开克隆出来的选项行都继承；已展开的那批实例不受影响。

        /// <summary>下拉箭头的 sprite。<c>""</c> = 隐藏箭头（无图的 Image 会画成实心方块，故直接关组件）。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Arrow
        {
            set
            {
                var sprite = UI.ResolveSprite(value);
                _arrow.sprite = sprite;
                // 箭头是字形不是底板：没有图就没有意义，留着会变成一个实心方块。
                _arrow.enabled = sprite != null;
            }
        }

        /// <summary>下拉箭头颜色；支持 token / <c>/alpha</c> / 渐变。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ArrowColor
        {
            set => Internal.ColorApplier.Apply(_arrow, UI.Theme.ResolveSpec(value));
        }

        /// <summary>
        /// 选项行的高亮底色（hover / 选中时由 uGUI Toggle 的 ColorTint 乘上去）。
        /// 不设时是硬编码的浅灰 <c>#F5F5F5</c> —— 深色主题下必须显式改。
        /// </summary>
        [UIAttr(IsColor = true), Preserve]
        public string ItemColor
        {
            set => Internal.ColorApplier.Apply(_itemBg, UI.Theme.ResolveSpec(value));
        }

        /// <summary>选中项左侧对勾的 sprite。<c>""</c> = 无图。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Checkmark
        {
            set
            {
                var sprite = UI.ResolveSprite(value);
                _itemCheckmark.sprite = sprite;
                _itemCheckmark.enabled = sprite != null;
            }
        }

        /// <summary>选中项对勾的颜色。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string CheckmarkColor
        {
            set => Internal.ColorApplier.Apply(_itemCheckmark, UI.Theme.ResolveSpec(value));
        }

        /// <summary>选项行文字色（区别于 <c>textColor</c> —— 那是收起状态的 caption）。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ItemTextColor
        {
            set => LabelColorApplier.Apply(_itemLabel, value);
        }

        /// <summary>弹窗滚动条轨道的 sprite。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Scrollbar
        {
            set => _scrollbarBg.sprite = UI.ResolveSprite(value);
        }

        /// <summary>弹窗滚动条轨道的颜色。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ScrollbarColor
        {
            set => Internal.ColorApplier.Apply(_scrollbarBg, UI.Theme.ResolveSpec(value));
        }

        /// <summary>弹窗滚动条滑块的 sprite。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string ScrollbarHandle
        {
            set => _scrollbarHandle.sprite = UI.ResolveSprite(value);
        }

        /// <summary>弹窗滚动条滑块的颜色。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ScrollbarHandleColor
        {
            set => Internal.ColorApplier.Apply(_scrollbarHandle, UI.Theme.ResolveSpec(value));
        }

        public Observable<int> OnSelected => _selected;

        public IDisposable BindOptions(Observable<IEnumerable<string>> source) =>
            source.Subscribe(seq => SetOptions(seq.Select(s => new DropdownOption(s)).ToList()));

        public IDisposable BindOptions(Observable<IEnumerable<DropdownOption>> source) =>
            source.Subscribe(seq => SetOptions(seq.ToList()));

        private void SetOptions(List<DropdownOption> opts)
        {
            var wasOpen = _tmp.IsExpanded;
            if (wasOpen) _tmp.Hide();

            _tmp.options.Clear();
            foreach (var o in opts)
            {
                var od = new TMP_Dropdown.OptionData { text = o.Text ?? "", image = o.Icon };
                _tmp.options.Add(od);
            }
            _tmp.RefreshShownValue();

            if (wasOpen) _tmp.Show();
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _selected.Dispose();
            base.Dispose();
        }
    }
}
