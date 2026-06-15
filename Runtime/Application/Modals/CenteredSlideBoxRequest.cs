using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Application.Modals
{
    public sealed class CenteredSlideBoxRequest<T> : ModalRequest<T> where T : class
    {
        public IReadOnlyList<T> Items;
        public Action<IControl, T> BindCard;
        public string Title;
        public string ConfirmLabel;
        public string XmlSrcOverride;                       // 命名变体 facade 可传；null→静态默认

        public override string XmlSrc => XmlSrcOverride ?? CenteredSlideBox.XmlSrc;

        public override bool TryEscape(out T result) { result = null; return true; }

        public override void Bind(IScreen screen, Action<T> close)
        {
            var titleCtl = screen.Get<Text>("title");
            if (string.IsNullOrEmpty(Title)) titleCtl.GameObject.SetActive(false);
            else titleCtl.TextValue = Title;

            // 取消通道：× / 背景 / ESC → null
            screen.Get<Btn>("close").OnClick.Subscribe(_ => close(null)).AddTo(screen);
            screen.Get<PromptUGUI.Controls.Image>("backdrop")
                .OnPointerDown.Subscribe(_ => close(null)).AddTo(screen);

            // null Items 视作空（Open(null,…) 不该在 Carousel.Rebuild 里 NRE；空列表「禁确认」是 Task 4）
            Items ??= System.Array.Empty<T>();

            var car = screen.Get<Carousel>("cards");
            int idx = 0;
            car.BindItems(
                Observable.Return((IReadOnlyList<T>)Items),
                (IControl card, T item) =>
                {
                    int i = idx++;
                    BindCard?.Invoke(card, item);
                    AttachCardClick(card, i, car, close);
                }).AddTo(screen);

            var ok = screen.Get<Btn>("confirm");
            if (!string.IsNullOrEmpty(ConfirmLabel)) ok.Text = ConfirmLabel;
            ok.OnClick.Subscribe(_ =>
            {
                int cur = car.Current;
                if (cur >= 0 && cur < Items.Count) close(Items[cur]);
            }).AddTo(screen);
        }

        // 每张卡挂透明 raycast catcher + PuiButton：click(非拖动) → 居中或确认。
        // 点居中卡 = 确认；点侧卡 = GoTo 居中。拖动不被 PuiButton 处理 → 冒泡给 CarouselView。
        private void AttachCardClick(IControl card, int i, Carousel car, Action<T> close)
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
                if (car.Current == i) close(Items[i]);     // 点居中卡 = 确认
                else car.GoTo(i, animated: true);          // 点侧卡 = 居中
            });
        }
    }

    public static class CenteredSlideBox
    {
        // 必须带 .ui 后缀（Unity 只剥 .ui.xml 的最后 .xml）。可写 = 换皮入口。
        public static string XmlSrc { get; set; } = "PromptUGUI/Modals/CenteredSlideBox.ui";

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
    }
}
