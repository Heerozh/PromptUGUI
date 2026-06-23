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
        // buttons 至少 1 个（两个 request 各自保证）；itemsSource 由 request 归一化为非空 Observable。
        public static void Bind<T>(
            IScreen screen, Observable<IReadOnlyList<T>> itemsSource, Action<IControl, T> bindCard,
            string title, IReadOnlyList<(string label, string key)> buttons, Func<T, object> key,
            string xmlSrcForError, Action<T, string> onConfirm, Action onCancel) where T : class
        {
            if (buttons == null || buttons.Count == 0)        // facade 已挡空；直接 new request 的兜底（CSB-D14）
                throw new ArgumentException("CenteredSlideBox requires at least one button.", nameof(buttons));
            if (itemsSource == null)
                throw new ArgumentNullException(nameof(itemsSource));

            // —— title ——
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = title;

            // —— 取消三通道（CSB-D9）——
            screen.Get<Btn>("close").OnClick.Subscribe(_ => onCancel()).AddTo(screen);
            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => onCancel()).AddTo(screen);

            bool autoConfirm = buttons.Count == 1;                  // CSB-D16
            string soleKey = autoConfirm ? buttons[0].key : null;

            IReadOnlyList<T> latest = Array.Empty<T>();
            int idx = 0;
            var car = screen.Get<Carousel>("cards");

            // —— 探测皮肤按钮槽（CSB-D17）：必须在 BindItems 之前，.Do 首发会同步刷新按钮禁用态 ——
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
            var visibleButtons = new List<Btn>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i >= buttons.Count) { slot.GameObject.SetActive(false); continue; }
                var (label, btnKey) = buttons[i];
                if (!string.IsNullOrEmpty(label)) slot.Text = label;   // null/空 → 保留皮肤默认（button0 的 "OK"）
                visibleButtons.Add(slot);
                slot.OnClick.Subscribe(_ =>
                {
                    int cur = car.Current;
                    if (cur >= 0 && cur < latest.Count) onConfirm(latest[cur], btnKey);
                }).AddTo(screen);
            }
            void RefreshButtonsEnabled(bool on) { foreach (var b in visibleButtons) b.Interactable = on; }

            // —— carousel + 卡：.Do（上游先于下游 Rebuild）维护 latest / 重置 idx / 刷新按钮态（RI-D12/D13）；
            //    BindItems 带 key 做身份保持（RI-D9~D11）——
            var src = itemsSource.Do(list =>
            {
                latest = list ?? Array.Empty<T>();
                idx = 0;                                            // ★ 每 emit 归零，否则跨重建累加致索引错乱
                RefreshButtonsEnabled(latest.Count > 0);            // 空→disable，非空→enable（CSB-D11 反应式版）
            });
            car.BindItems(src, (IControl card, T item) =>
            {
                int i = idx++;
                bindCard?.Invoke(card, item);
                AttachCardClick(card, i, car, () => latest, onConfirm, autoConfirm, soleKey);
            }, key).AddTo(screen);
        }

        // 每张卡：透明 raycast catcher + PuiButton。click（非拖动）→ 居中导航或（仅单按钮）确认。
        private static void AttachCardClick<T>(IControl card, int i, Carousel car,
            Func<IReadOnlyList<T>> getLatest,
            Action<T, string> onConfirm, bool autoConfirm, string soleKey) where T : class
        {
            var go = card.GameObject;
            var img = go.GetComponent<UnityImage>() ?? go.AddComponent<UnityImage>();
            img.color = new UnityEngine.Color(0f, 0f, 0f, 0f);   // 透明，仅 raycast
            img.raycastTarget = true;
            // 卡 GO 每次 BindItems 重建都是全新的（旧的由 ClearCards 销毁），故无条件 AddComponent 安全。
            var btn = go.AddComponent<PuiButton>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                var items = getLatest();
                if (car.Current == i)
                {
                    // 单按钮：点居中卡=确认；多按钮：无操作（CSB-D16）。
                    if (autoConfirm && i >= 0 && i < items.Count) onConfirm(items[i], soleKey);
                }
                else car.GoTo(i, animated: true);                // 点侧卡=居中
            });
        }
    }

    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;                       // 静态（保留，向后兼容）
        public Observable<IReadOnlyList<T>> ItemsSource;     // 反应式（优先）
        public Func<T, object> Key;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;                 // 单个按钮的 label（空→皮肤默认 "OK"）
        public string XmlSrcOverride;               // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out T result) { result = null; return true; }

        public override void Bind(IScreen screen, Action<T> close)
            => CenteredSlideBoxBinder.Bind(screen,
                   ItemsSource ?? Observable.Return<IReadOnlyList<T>>(Items ?? Array.Empty<T>()),
                   BindCard, Title, new[] { (ConfirmLabel, (string)null) }, Key, XmlSrc,   // 1 个隐式按钮；key 忽略
                   onConfirm: (item, _) => close(item),
                   onCancel: () => close(null));
    }

    // 多按钮：返回 SlideSelection<T>（选中卡 + 按钮 key）。
    public sealed class CenteredSlideBoxMultiRequest<T> : ModalRequest<SlideSelection<T>> where T : class
    {
        public IReadOnlyList<T> Items;
        public Observable<IReadOnlyList<T>> ItemsSource;
        public Func<T, object> Key;
        public Action<IControl, T> BindCard;
        public string Title;
        public IReadOnlyList<(string label, string key)> Buttons;   // facade 保证非空
        public string XmlSrcOverride;

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out SlideSelection<T> result) { result = default; return true; }

        public override void Bind(IScreen screen, Action<SlideSelection<T>> close)
            => CenteredSlideBoxBinder.Bind(screen,
                   ItemsSource ?? Observable.Return<IReadOnlyList<T>>(Items ?? Array.Empty<T>()),
                   BindCard, Title, Buttons, Key, XmlSrc,
                   onConfirm: (item, key) => close(new SlideSelection<T>(item, key)),
                   onCancel: () => close(default));
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

        // 单按钮 · 静态 → 返回选中对象 / null（向后兼容）。
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

        // 单按钮 · 反应式。
        public static UnityEngine.Awaitable<T> Open<T>(
            Observable<IReadOnlyList<T>> items,
            Action<IControl, T> bind,
            string title = null,
            string confirmLabel = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            Func<T, object> key = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            return UI.Modal.OpenAsync(new CenteredSlideBoxRequest<T>
            {
                ItemsSource = items,
                Key = key,
                BindCard = bind,
                Title = title,
                ConfirmLabel = confirmLabel,
                Configure = configure,
            }, mode, ct);
        }

        // 多按钮 · 静态 → 返回 (选中对象, 按钮 key)。buttons 必填且非空。
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

        // 多按钮 · 反应式。
        public static UnityEngine.Awaitable<SlideSelection<T>> Open<T>(
            Observable<IReadOnlyList<T>> items,
            Action<IControl, T> bind,
            IEnumerable<(string label, string key)> buttons,
            string title = null,
            ModalMode mode = ModalMode.Popup,
            Action<IScreen> configure = null,
            Func<T, object> key = null,
            System.Threading.CancellationToken ct = default
        ) where T : class
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var list = new List<(string label, string key)>(
                buttons ?? throw new ArgumentNullException(nameof(buttons)));
            if (list.Count == 0)
                throw new ArgumentException("buttons must be non-empty", nameof(buttons));
            return UI.Modal.OpenAsync(new CenteredSlideBoxMultiRequest<T>
            {
                ItemsSource = items,
                Key = key,
                BindCard = bind,
                Title = title,
                Buttons = list,
                Configure = configure,
            }, mode, ct);
        }
    }
}
