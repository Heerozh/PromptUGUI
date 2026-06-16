using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Application.Modals
{
    // 共享 Bind 逻辑：单/多按钮 request 都委托这里。onConfirm/onCancel 抽掉结果类型差异。
    internal static class CenteredSlideBoxBinder
    {
        // buttons 至少 1 个（两个 request 各自保证）。
        public static void Bind<T>(
            IScreen screen, IReadOnlyList<T> items, Action<IControl, T> bindCard, string title,
            IReadOnlyList<(string label, string key)> buttons, string xmlSrcForError,
            Action<T, string> onConfirm, Action onCancel) where T : class
        {
            if (buttons == null || buttons.Count == 0)        // facade 已挡空；这是直接 new request 的兜底（CSB-D14）
                throw new ArgumentException("CenteredSlideBox requires at least one button.", nameof(buttons));

            // —— title ——
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = title;

            // —— 取消三通道（CSB-D9）——
            screen.Get<Btn>("close").OnClick.Subscribe(_ => onCancel()).AddTo(screen);
            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => onCancel()).AddTo(screen);

            items ??= Array.Empty<T>();
            bool autoConfirm = buttons.Count == 1;                  // CSB-D16
            string soleKey = autoConfirm ? buttons[0].key : null;

            // —— carousel + 卡 ——
            var car = screen.Get<Carousel>("cards");
            int idx = 0;
            car.BindItems(
                Observable.Return(items),
                (IControl card, T item) =>
                {
                    int i = idx++;
                    bindCard?.Invoke(card, item);
                    AttachCardClick(card, i, car, items, onConfirm, autoConfirm, soleKey);
                }).AddTo(screen);

            // —— 探测皮肤按钮槽（CSB-D17）——
            var slots = new List<Btn>();
            for (int i = 0; ; i++)
            {
                try { slots.Add(screen.Get<Btn>($"button{i}")); }
                catch (KeyNotFoundException) { break; }
            }
            if (buttons.Count > slots.Count)
                throw new InvalidOperationException(
                    $"CenteredSlideBox skin '{xmlSrcForError}' provides {slots.Count} button slot(s) but " +
                    $"{buttons.Count} buttons were passed; override XmlSrc with more 'button{{i}}' slots.");

            // —— 映射 buttons[i] → slot i，隐藏多余槽 ——
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i >= buttons.Count) { slot.GameObject.SetActive(false); continue; }
                var (label, key) = buttons[i];
                if (!string.IsNullOrEmpty(label)) slot.Text = label;   // null/空 → 保留皮肤默认（button0 的 "OK"）
                if (items.Count == 0) slot.Interactable = false;       // CSB-D11
                slot.OnClick.Subscribe(_ =>
                {
                    int cur = car.Current;
                    if (cur >= 0 && cur < items.Count) onConfirm(items[cur], key);
                }).AddTo(screen);
            }
        }

        // 每张卡：透明 raycast catcher + PuiButton。click（非拖动）→ 居中导航或（仅单按钮）确认。
        private static void AttachCardClick<T>(IControl card, int i, Carousel car, IReadOnlyList<T> items,
            Action<T, string> onConfirm, bool autoConfirm, string soleKey) where T : class
        {
            var go = card.GameObject;
            var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
            img.color = new UnityEngine.Color(0f, 0f, 0f, 0f);   // 透明，仅 raycast
            img.raycastTarget = true;
            // 卡 GO 每次 BindItems 重建都是全新的（旧的由 ClearCards 销毁），故无条件 AddComponent 安全、不重复挂。
            var btn = go.AddComponent<PuiButton>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (car.Current == i) { if (autoConfirm) onConfirm(items[i], soleKey); }   // 单按钮：点居中卡=确认；多按钮：无操作
                else car.GoTo(i, animated: true);                                          // 点侧卡=居中
            });
        }
    }

    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;                 // 单个按钮的 label（空→皮肤默认 "OK"）
        public string XmlSrcOverride;               // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out T result) { result = null; return true; }

        public override void Bind(IScreen screen, Action<T> close)
            => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title,
                   new[] { (ConfirmLabel, (string)null) }, XmlSrc,     // 1 个隐式按钮；key 忽略
                   onConfirm: (item, _) => close(item),
                   onCancel: () => close(null));
    }

    // 多按钮：返回 SlideSelection<T>（选中卡 + 按钮 key）。
    public sealed class CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public IReadOnlyList<(string label, string key)> Buttons;   // facade 保证非空
        public string XmlSrcOverride;

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out SlideSelection<T> result) { result = default; return true; }

        public override void Bind(IScreen screen, Action<SlideSelection<T>> close)
            => CenteredSlideBoxBinder.Bind(screen, Items, BindCard, Title, Buttons, XmlSrc,
                   onConfirm: (item, key) => close(new SlideSelection<T>(item, key)),
                   onCancel: () => close(default));
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        // 单按钮 → 返回选中对象 / null（向后兼容，非 async 直传）。
        public static UnityEngine.Awaitable<T> Open<T>(
            IReadOnlyList<T> items,
            Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
            => UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);

        // 多按钮 → 返回 (选中对象, 按钮 key)。buttons 必填且非空。
        public static UnityEngine.Awaitable<SlideSelection<T>> Open<T>(
            IReadOnlyList<T> items,
            Action<IControl, T> bind,
            IEnumerable<(string label, string key)> buttons,
            string title = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            var list = new List<(string label, string key)>(
                buttons ?? throw new ArgumentNullException(nameof(buttons)));
            if (list.Count == 0)
                throw new ArgumentException("buttons must be non-empty", nameof(buttons));
            return UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<T>
            {
                Items = items,
                BindCard = bind,
                Title = title,
                Buttons = list,
                Configure = configure,
            }, mode, ct);
        }
    }
}
