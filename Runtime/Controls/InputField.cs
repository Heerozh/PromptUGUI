using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class InputField : ProceduralControl
    {
        private const float DefaultWidth = 160f;
        private const float MinTapHeight = 44f;

        // Breathing room between the field's frame and its text when a border sprite is
        // present. (t, r, b, l) = (7, 10, 6, 10) reproduces the historical TMP-prefab
        // geometry (Text Area sizeDelta -20,-13, anchoredPosition 0,-0.5). When there is no
        // border sprite the inset collapses to 0 so short fields don't go negative-height.
        private static readonly (float t, float r, float b, float l) DefaultPadding = (7f, 10f, 6f, 10f);

        private UnityImage _bg;

        private protected override GameObject SurfaceHost => GameObject;
        private protected override UnityEngine.UI.Selectable SurfaceSelectable => _input;
        private PuiInputField _input;
        private Internal.InputFieldNavGate _navGate;
        private TMP_Text _placeholder;
        private TMP_Text _text;
        private RectTransform _textArea;
        private RectMask2D _textAreaMask;
        private (float t, float r, float b, float l)? _paddingOverride;
        private string _fontType = "default";
        private readonly Subject<string> _changed = new();
        private readonly Subject<string> _endEdit = new();
        private readonly Subject<string> _submit = new();

        public Observable<string> OnValueChanged => _changed;

        /// <summary>
        /// The user changed the text: typing, pasting, deleting, an input method committing, the soft
        /// keyboard syncing (spec 2026-10-18 §6.7). Code writes (<see cref="TextValue"/>, an XML replay)
        /// and TMP's Escape rollback do not count; <see cref="OnValueChanged"/> still reports all of them.
        /// </summary>
        public Observable<string> OnInput => _userInput;

        private readonly Subject<string> _userInput = new();
        private bool _programmaticWrite;
        public Observable<string> OnEndEdit => _endEdit;
        public Observable<string> OnSubmit => _submit;

        public override Vector2? GetNativeSize() => new Vector2(DefaultWidth, MinTapHeight);

        public override void OnAttached()
        {
            // Root: sliced bg + TMP_InputField
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultInsetSprite(_bg);

            // Text Area: stretch to the field; the inset (offsets + mask overscan) is derived
            // from sprite/padding state in ApplyTextAreaPadding — default = historical prefab
            // geometry, sprite="" => 0, explicit padding= wins.
            _textArea = ProceduralBuilders.AddChild(RectTransform, "Text Area");
            _textArea.anchorMin = new Vector2(0f, 0f);
            _textArea.anchorMax = new Vector2(1f, 1f);
            _textAreaMask = _textArea.gameObject.AddComponent<RectMask2D>();
            ApplyTextAreaPadding();

            // PuiInputField: TMP unchanged until a <Suggestions> list is open (spec 2026-10-18 §6.6).
            var pui = GameObject.AddComponent<PuiInputField>();
            pui.Owner = this;
            _input = pui;
            _input.targetGraphic = _bg;
            _input.textViewport = _textArea;

            // Placeholder：italic + 半透明 + IgnoreLayout (默认 prefab Placeholder 节点)
            _placeholder = ProceduralBuilders.AddText(_textArea, "Placeholder");
            _placeholder.alignment = TextAlignmentOptions.TopLeft;
            _placeholder.fontStyle = FontStyles.Italic;
            _placeholder.color = ProceduralBuilders.DefaultPlaceholderColor;
            _placeholder.text = "Enter text...";
#if UNITY_2023_1_OR_NEWER
            _placeholder.textWrappingMode = TextWrappingModes.NoWrap;
#else
            _placeholder.enableWordWrapping = false;
#endif
            var phLE = _placeholder.gameObject.AddComponent<LayoutElement>();
            phLE.ignoreLayout = true;

            // Text：用户输入显示组件
            _text = ProceduralBuilders.AddText(_textArea, "Text");
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.color = ProceduralBuilders.DefaultLabelColor;
            _text.text = string.Empty;

            _input.textComponent = _text;
            _input.placeholder = _placeholder;
            _input.caretColor = ProceduralBuilders.DefaultLabelColor;
            _input.customCaretColor = false;
            _input.selectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
            _input.onValueChanged.AddListener(v =>
            {
                _changed.OnNext(v);
                // TMP rolls an Escape back after it has stopped editing, so isFocused rules that out too.
                if (_programmaticWrite || !_input.isFocused) return;
                // Armed first: a host answering OnInput with a synchronous push then opens the list.
                _suggestions?.NotifyUserInput();
                _userInput.OnNext(v);
            });
            _input.onEndEdit.AddListener(v => _endEdit.OnNext(v));
            // Gate: when Navigation is enabled and the field is not yet focused, Submit is
            // the two-level "enter edit" gesture.  TMP fires onSubmit synchronously before
            // InputFieldNavGate.OnSubmit runs, so we check isFocused here at listener time
            // to suppress the business event for that transition.
            // With a <Suggestions> list this is the typed-text submit: the field's own event first, then
            // the list's (text, -1). A chosen suggestion never comes through here (spec 2026-10-18 §6.4).
            _input.onSubmit.AddListener(v =>
            {
                if (UI.Navigation.IsEnabled && !_input.isFocused) return;
                _submit.OnNext(v);
                _suggestions?.CommitTyped(v);
            });

            // AddComponent<TMP_InputField> 在 active GO 上立刻触发 OnEnable, 那一刻
            // textComponent/placeholder/textViewport 都还是 null, RegisterDirtyVerticesCallback
            // 被跳过 → caret 顶点没人触发 redraw, 永远不显示。再触一次 OnEnable cycle 才能
            // 把 MarkGeometryAsDirty/UpdateLabel 绑到现在已经 wire 好的 textComponent 上。
            _input.enabled = false;
            _input.enabled = true;

            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;

            _navGate = GameObject.AddComponent<Internal.InputFieldNavGate>();
            _navGate.Init(_input);
        }

        private void ApplyFont()
        {
            FontApplier.Apply(_text, _fontType);
            FontApplier.Apply(_placeholder, _fontType);
        }

        // Effective Text Area inset = author padding override, else (has border sprite ?
        // DefaultPadding : 0). Recomputed after every ApplyCommon so clearing the sprite
        // (sprite="") collapses the inset regardless of attribute application order. The mask
        // overscan grows the clip outward up to the historical (8,5) but never past the inset
        // (otherwise the clip would extend beyond the field's edge).
        private void ApplyTextAreaPadding()
        {
            if (_textArea == null) return;
            var (t, r, b, l) = _paddingOverride
                ?? (_bg != null && _bg.sprite != null ? DefaultPadding : (0f, 0f, 0f, 0f));
            // The submit icon's square sits at the right end; the text stops short of it.
            var slot = PlaceSubmitIcon();
            _textArea.offsetMin = new Vector2(l, b);
            _textArea.offsetMax = new Vector2(-(r + slot), -t);
            if (_textAreaMask != null)
            {
                // RectMask2D.padding is (left, bottom, right, top); negative grows the region.
                _textAreaMask.padding = new Vector4(
                    -Mathf.Min(l, 8f), -Mathf.Min(b, 5f), -Mathf.Min(r, 8f), -Mathf.Min(t, 5f));
            }
        }

        /// <summary>
        /// Bridges the common <c>interactable</c> attribute (already applied by
        /// <see cref="Application.ControlAttributeApplier"/> via <c>ApplyCommon</c> → base
        /// <see cref="Control.Interactable"/>, CanvasGroup-backed) to the underlying
        /// <see cref="TMP_InputField"/> (itself a <c>Selectable</c>), so disabling the field
        /// also runs <c>DoStateTransition(Disabled)</c> and blocks focus/typing. Mirrors
        /// <c>Btn</c>/<c>Toggle</c>/<c>Tab</c>; runs after every <c>ApplyCommon</c>
        /// (initial apply + each Variant ReSolve), composing with the CanvasGroup behaviour.
        /// </summary>
        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _input.interactable = Interactable;
            if (_iconPress != null) _iconPress.Interactable = PeekInteractable;
            ApplyTextAreaPadding();
            _suggestions?.OnFieldAfterApply();
        }

        // ── submitIcon (spec 2026-10-18 §5.3 / §6.4) ───────────────────────────────────────

        private const float DefaultSubmitIconSize = 24f;

        private RectTransform _iconRect;
        private UnityImage _iconGlyph;
        private FocuslessPress _iconPress;
        private float _iconSize = DefaultSubmitIconSize;
        private Color _iconColor = ProceduralBuilders.DefaultGlyphColor;

        /// <summary>
        /// A submit button at the field's right end — Enter, for a pointer (a chat's "send"). Not built
        /// unless written; <c>""</c> / <c>none</c> removes it again. It is not a Selectable: pressing it
        /// leaves the field editing until the click submits.
        /// </summary>
        [UIAttr(IsSprite = true), Preserve]
        public string SubmitIcon
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value == "none")
                {
                    if (_iconRect != null) _iconRect.gameObject.SetActive(false);
                }
                else
                {
                    EnsureSubmitIcon();
                    _iconRect.gameObject.SetActive(true);
                    _iconGlyph.sprite = UI.ResolveSprite(value);
                }
                ApplyTextAreaPadding();
            }
        }

        /// <summary>The icon's side, px (default 24). The click area is a square as tall as the field.</summary>
        [UIAttr, Preserve]
        public float SubmitIconSize
        {
            set
            {
                _iconSize = value;
                if (_iconGlyph != null) _iconGlyph.rectTransform.sizeDelta = new Vector2(value, value);
            }
        }

        /// <summary>The icon's tint (default: the glyph colour, i.e. the sprite's own).</summary>
        [UIAttr(IsColor = true), Preserve]
        public string SubmitIconColor
        {
            set
            {
                _iconColor = UI.Theme.Resolve(value);
                ApplyIconColor();
            }
        }

        private void EnsureSubmitIcon()
        {
            if (_iconRect != null) return;
            _iconRect = ProceduralBuilders.AddChild(RectTransform, "SubmitIcon");
            _iconRect.anchorMin = new Vector2(1f, 0f);
            _iconRect.anchorMax = new Vector2(1f, 1f);
            _iconRect.pivot = new Vector2(1f, 0.5f);
            _iconRect.anchoredPosition = Vector2.zero;
            _iconRect.gameObject.AddComponent<HitCatcher>();
            _iconPress = _iconRect.gameObject.AddComponent<FocuslessPress>();
            _iconPress.Interactable = PeekInteractable;
            _iconPress.OnState.Subscribe(_ => ApplyIconColor());
            _iconPress.OnClicked.Subscribe(_ => SubmitFromIcon());

            _iconGlyph = ProceduralBuilders.AddImage(_iconRect, "Glyph", raycast: false);
            var glyph = _iconGlyph.rectTransform;
            glyph.anchorMin = glyph.anchorMax = new Vector2(0.5f, 0.5f);
            glyph.pivot = new Vector2(0.5f, 0.5f);
            glyph.anchoredPosition = Vector2.zero;
            glyph.sizeDelta = new Vector2(_iconSize, _iconSize);
            _iconGlyph.preserveAspect = true;
            ApplyIconColor();

            // The square follows the field's height when a layout resizes it.
            var relay = GameObject.GetComponent<RectDimensionsRelay>();
            if (relay == null) relay = GameObject.AddComponent<RectDimensionsRelay>();
            relay.OnDimensionsChanged += ApplyTextAreaPadding;
        }

        // Sizes the click square to the field's height; returns the width the text has to leave for it.
        private float PlaceSubmitIcon()
        {
            if (_iconRect == null || !_iconRect.gameObject.activeSelf) return 0f;
            var h = RectTransform.rect.height;
            var side = h > 0f ? h : MinTapHeight;
            _iconRect.sizeDelta = new Vector2(side, 0f);
            return side;
        }

        // uGUI's default ColorBlock multipliers, so the icon answers like any stock control.
        private void ApplyIconColor()
        {
            if (_iconGlyph == null) return;
            var block = ColorBlock.defaultColorBlock;
            var tint = _iconPress == null ? block.normalColor : _iconPress.Current switch
            {
                InteractState.Hover => block.highlightedColor,
                InteractState.Pressed => block.pressedColor,
                InteractState.Disabled => block.disabledColor,
                _ => block.normalColor,
            };
            _iconGlyph.color = _iconColor * tint * block.colorMultiplier;
        }

        /// <summary>The same submit as Enter (spec §6.4): a highlighted suggestion is chosen, else the text goes.</summary>
        private void SubmitFromIcon()
        {
            if (!PeekInteractable) return;
            var list = _suggestions;
            if (list != null && list.IsOpen && list.HighlightedIndex >= 0)
            {
                list.CommitChosen(list.HighlightedIndex);
                return;
            }
            var text = TextValue;
            _submit.OnNext(text);
            list?.CommitTyped(text);
            EndEditing();
        }

        private Suggestions _suggestions;

        /// <summary>Its <c>&lt;Suggestions&gt;</c> child, or null when it has none.</summary>
        public Suggestions Suggestions => _suggestions;

        /// <summary>
        /// Takes a <c>&lt;Suggestions&gt;</c> child (spec 2026-10-18 §5.1). A field takes one list; a
        /// second is parked inactive with a warning, the way a scrolling host treats a second bar.
        /// </summary>
        internal void AdoptSuggestions(Suggestions list)
        {
            if (_suggestions != null && _suggestions != list)
            {
                UILog.Warn(list,
                    $"[{PromptUGUI.Lint.SuggestionsRules.DuplicateCode}] " +
                    PromptUGUI.Lint.SuggestionsRules.DuplicateMessage(Id, list.Id));
                list.GameObject.SetActive(false);
                return;
            }
            _suggestions = list;
            list.AttachField(this);
        }

        /// <summary>
        /// In edit mode: a caret, keyboard input going in. Selected without editing (the two-level gamepad model,
        /// after a submit or an Escape) reads false.
        /// </summary>
        public bool IsEditing => _editingOverride ?? (_input != null && _input.isFocused);

        /// <summary>
        /// Puts the field into edit mode (spec 2026-10-19-input-begin-edit §4.1): selects it if it is not, opens
        /// the soft keyboard on a phone — under any navigation mode, and whether or not it is already the selected
        /// object (<c>screen.Focus</c> does nothing then). TMP carries it out in this frame's <c>LateUpdate</c>, so
        /// <see cref="IsEditing"/> reads true only after that. Safe from <see cref="OnSubmit"/>: that edit ends and
        /// this one begins in the same frame.
        /// <para>False, and nothing done, when the field is inactive, not interactable or read-only.</para>
        /// </summary>
        public bool BeginEdit()
        {
            if (_editingOverride.HasValue)
            {
                if (_editingOverride == false) SetEditingForTests(true);
                return true;
            }
            if (_input == null || _readOnly || !_input.IsActive() || !_input.IsInteractable()) return false;
            _navGate.ExpectEdit();
            _input.RequestEdit();
            return true;
        }

        private bool? _editingOverride;
        private bool _readOnly;

        /// <summary>
        /// Edit mode began (true) or ended (false), whichever way it ended — the edges the
        /// suggestion list opens and closes on (spec 2026-10-18 §6.6).
        /// </summary>
        internal event System.Action<bool> EditingChanged;

        /// <summary>
        /// Test hook: pins <see cref="IsEditing"/> (EditMode never focuses a TMP field) and raises the
        /// edge; <c>null</c> goes back to the live state.
        /// </summary>
        internal void SetEditingForTests(bool? editing)
        {
            var was = IsEditing;
            _editingOverride = editing;
            if (IsEditing != was) EditingChanged?.Invoke(IsEditing);
        }

        /// <summary>The live edge, reported by the field's TMP component.</summary>
        internal void NotifyEditingChanged(bool editing)
        {
            if (_editingOverride == null) EditingChanged?.Invoke(editing);
        }

        /// <summary>Leaves edit mode — after a suggestion is chosen, or when a layer covers the list.</summary>
        internal void EndEditing()
        {
            if (_editingOverride == true)
            {
                SetEditingForTests(false);
                return;
            }
            if (_input != null && _input.isFocused) _input.DeactivateInputField();
        }

        internal bool IsReadOnly => _readOnly;

        /// <summary>
        /// Code that disables the field also reaches the TMP component and closes the suggestion list:
        /// this path does not go through <see cref="OnAfterApply"/>.
        /// </summary>
        public override bool Interactable
        {
            get => base.Interactable;
            set
            {
                base.Interactable = value;
                if (_input != null) _input.interactable = value;
                if (_iconPress != null) _iconPress.Interactable = value;
                if (!value) _suggestions?.Close();
            }
        }

        [UIAttr("text"), Preserve]
        public string TextValue
        {
            get => _input != null ? _input.text : null;
            set
            {
                // A code write, not user input: OnInput stays quiet (no re-query for a written-back choice).
                _programmaticWrite = true;
                try
                {
                    _input.text = value ?? string.Empty;
                }
                finally
                {
                    _programmaticWrite = false;
                }
            }
        }

        internal override string PeekDefaultText() => _input != null ? _input.text : null;

        /// <summary>
        /// Puts <paramref name="text"/> in at the caret (spec 2026-10-19 §4.12): over the live selection while
        /// editing; otherwise where the last edit left the caret and selection — the end of the text if it was
        /// never edited, clamped if code shortened the text since. The caret ends after the inserted text, so
        /// inserts in a row follow each other. A code write: <see cref="OnValueChanged"/> fires,
        /// <see cref="OnInput"/> does not, and the focus is left as it is — no keyboard pops up.
        /// <para>False, and nothing inserted, when the whole text does not fit <c>characterLimit</c> (a cut
        /// could split a surrogate pair), the field is read-only, or an IME composition is in progress.
        /// Null or empty inserts nothing and is true.</para>
        /// </summary>
        public bool Insert(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (_input == null || _readOnly || _input.CompositionLength > 0) return false;

            var current = _input.text ?? string.Empty;
            int anchor, focus;
            if (IsEditing)
            {
                anchor = _input.selectionStringAnchorPosition;
                focus = _input.selectionStringFocusPosition;
            }
            else if (_input.RememberedAnchor >= 0)
            {
                anchor = _input.RememberedAnchor;
                focus = _input.RememberedFocus;
            }
            else
            {
                anchor = focus = current.Length;
            }
            var start = Mathf.Clamp(Mathf.Min(anchor, focus), 0, current.Length);
            var end = Mathf.Clamp(Mathf.Max(anchor, focus), 0, current.Length);

            // TMP does not hold a code write to the limit (its check in SetText is commented out).
            var limit = _input.characterLimit;
            if (limit > 0 && current.Length - (end - start) + text.Length > limit) return false;

            TextValue = current.Substring(0, start) + text + current.Substring(end);
            var caret = start + text.Length;
            if (IsEditing) _input.stringPosition = caret;
            _input.RememberCaret(caret, caret);
            return true;
        }

        [UIAttr, Preserve]
        public string Placeholder
        {
            set
            {
                if (_placeholder != null) _placeholder.text = value ?? string.Empty;
            }
        }

        [UIAttr, Preserve]
        public string ContentType
        {
            set
            {
                _input.contentType = value switch
                {
                    "standard" => TMP_InputField.ContentType.Standard,
                    "autocorrected" => TMP_InputField.ContentType.Autocorrected,
                    "integer-number" => TMP_InputField.ContentType.IntegerNumber,
                    "decimal-number" => TMP_InputField.ContentType.DecimalNumber,
                    "alphanumeric" => TMP_InputField.ContentType.Alphanumeric,
                    "name" => TMP_InputField.ContentType.Name,
                    "email" => TMP_InputField.ContentType.EmailAddress,
                    "password" => TMP_InputField.ContentType.Password,
                    "pin" => TMP_InputField.ContentType.Pin,
                    "custom" => TMP_InputField.ContentType.Custom,
                    _ => throw new System.ArgumentException(
                        $"InputField.contentType='{value}' invalid; expected standard|autocorrected|integer-number|decimal-number|alphanumeric|name|email|password|pin|custom"),
                };
            }
        }

        [UIAttr, Preserve]
        public string LineType
        {
            set
            {
                _input.lineType = value switch
                {
                    "single" => TMP_InputField.LineType.SingleLine,
                    "multi-newline" => TMP_InputField.LineType.MultiLineNewline,
                    "multi-submit" => TMP_InputField.LineType.MultiLineSubmit,
                    _ => throw new System.ArgumentException(
                        $"InputField.lineType='{value}' invalid; expected single|multi-newline|multi-submit"),
                };
            }
        }

        [UIAttr, Preserve]
        public int CharacterLimit
        {
            set => _input.characterLimit = value;
        }

        [UIAttr, Preserve]
        public bool ReadOnly
        {
            set
            {
                _readOnly = value;
                _input.readOnly = value;
            }
        }

        // Inner space between the field's edge and its text (T,R,B,L; "_" = 0). When unset the
        // inset auto-tracks the border: DefaultPadding with a sprite, 0 when sprite="" / "none".
        // An explicit value wins over that auto-default. Empty string clears the override.
        [UIAttr, Preserve]
        public string Padding
        {
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _paddingOverride = null;
                }
                else
                {
                    VStack.ParseTRBL(value, out var t, out var r, out var b, out var l);
                    _paddingOverride = (t, r, b, l);
                }
                ApplyTextAreaPadding();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec);
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

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        // Point size goes through TMP_InputField.pointSize; SetGlobalPointSize fans it out
        // to both the text and placeholder components (matches the default prefab's
        // GlobalPointSize), so setting it on _text alone would be overwritten by TMP.
        [UIAttr("fontSize"), Preserve]
        public float FontSize
        {
            set => _input.pointSize = value;
        }

        // The bg color lives on `color`; `textColor` is the typed-text color (_text).
        [UIAttr(IsColor = true), Preserve]
        public string TextColor
        {
            set => _text.color = UI.Theme.Resolve(value);
        }

        [UIAttr(IsColor = true), Preserve]
        public string PlaceholderColor
        {
            set => _placeholder.color = UI.Theme.Resolve(value);
        }

        // Reuses Text.ParseAlign (two independent TMP axes) and applies to both the text and
        // the placeholder so the placeholder previews where typed text will land.
        [UIAttr, Preserve]
        public string Align
        {
            set
            {
                var (h, v) = Text.ParseAlign(value);
                _text.horizontalAlignment = h;
                _text.verticalAlignment = v;
                _placeholder.horizontalAlignment = h;
                _placeholder.verticalAlignment = v;
            }
        }

        // Must flip customCaretColor on, else TMP_InputField.caretColor's getter falls back
        // to textComponent.color and the assigned value is dead.
        [UIAttr(IsColor = true), Preserve]
        public string CaretColor
        {
            set
            {
                _input.customCaretColor = true;
                _input.caretColor = UI.Theme.Resolve(value);
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string SelectionColor
        {
            set => _input.selectionColor = UI.Theme.Resolve(value);
        }

        [UIAttr, Preserve]
        public int CaretWidth
        {
            set => _input.caretWidth = value;
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            // Remove TMP UnityEvent listeners before disposing R3 subjects so that TMP
            // callbacks firing during object destruction (e.g. DeactivateInputField in
            // OnDestroy) can't call OnNext on a disposed subject.
            if (_input != null)
            {
                _input.onValueChanged.RemoveAllListeners();
                _input.onEndEdit.RemoveAllListeners();
                _input.onSubmit.RemoveAllListeners();
            }
            _changed.Dispose();
            _userInput.Dispose();
            _endEdit.Dispose();
            _submit.Dispose();
            base.Dispose();
        }
    }
}
