using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 当所属 <see cref="IStateSource"/> 进入 <see cref="InteractState.Disabled"/> 时，把（剪枝后的）
    /// 子树去色：非 TMP <see cref="Graphic"/> 换共享灰度材质，<see cref="TMP_Text"/> 把 <c>color</c> 置成
    /// 其亮度灰；离开 Disabled 还原。作者未写任何 <c>disabled*</c> 时的默认禁用外观（由
    /// <see cref="DisabledGrayscaleInstaller"/> 装上）。与 <c>transition</c> 无关——靠 OnState 流驱动。
    /// </summary>
    /// <remarks>
    /// 原始材质/颜色 capture-once：re-<see cref="Configure"/>（ReSolve）不重捕，避免把"禁用中"的灰度态
    /// 误当原始态。每次访问 graphic 前判空（销毁安全，呼应 <see cref="StateTintReactor"/>）。
    /// </remarks>
    internal sealed class DisabledGrayscaleController : MonoBehaviour
    {
        private const string GrayscaleResourcePath = "PromptUGUI/Material/UI-Grayscale";
        private static Material _sharedMat;

        /// <summary>进程内共享灰度材质：从 Resources 加载 shader 后懒建一份。</summary>
        internal static Material SharedMaterial
        {
            get
            {
                if (_sharedMat == null)
                {
                    var shader = Resources.Load<Shader>(GrayscaleResourcePath);
                    if (shader != null) _sharedMat = new Material(shader) { name = "UI-Grayscale (shared)" };
                }
                return _sharedMat;
            }
        }

        private readonly struct Captured
        {
            public readonly Graphic Graphic;
            public readonly Material Material;  // 原材质（非 TMP）
            public readonly Color Color;        // 原颜色（TMP）
            public readonly bool IsTmp;
            public Captured(Graphic g, Material m, Color c, bool isTmp)
            { Graphic = g; Material = m; Color = c; IsTmp = isTmp; }
        }

        private readonly Dictionary<Graphic, Captured> _captured = new Dictionary<Graphic, Captured>();
        private IStateSource _source;
        private IDisposable _sub;
        private bool _grayed;

        public void Configure(IReadOnlyList<Graphic> graphics)
        {
            // 先捕获原始态，再订阅：订阅会同步重放当前状态，首装即 Disabled 时必须先有原始态可还原。
            foreach (var g in graphics)
            {
                if (g == null || _captured.ContainsKey(g)) continue;
                var tmp = g as TMP_Text;
                _captured[g] = tmp != null
                    ? new Captured(g, null, tmp.color, true)
                    : new Captured(g, g.material, default, false);
            }

            if (_source == null)
            {
                // includeInactive：源可能在初始隐藏的 TabBar 绑定页上（同 StateTintReactor）。
                _source = GetComponentInParent<IStateSource>(true);
                if (_source != null) _sub = _source.OnState.Subscribe(OnState);
            }
            else if (_grayed)
            {
                // re-Configure（ReSolve）时仍处于 Disabled：属性管线可能已把材质复位（如 tint= setter）。
                // 按当前 _grayed 强制重涂全部（含本次新捕获的 graphic），不走 OnState 的去抖。
                ApplyAll();
            }
            else
            {
                // 不在 Disabled 时的 re-Configure：属性管线**刚刚**写完作者声明的值，所以现在图上的
                // 就是真相 —— 应该重新捕获，而不是把旧捕获写回去。
                //
                // 写回去正是主题切换悄悄丢掉所有 label 字色的原因：capture-once 活得比它捕获的值久，
                // 于是 textColor 的 setter 刚上色就被这里覆盖回上一套皮的颜色。bg 之所以幸免，只是
                // 因为非 TMP 分支写的是 material 而不是 color。同一族缺陷：从当前声明推，别 latch。
                Recapture();
            }
        }

        private void OnState(InteractState state)
        {
            var gray = state == InteractState.Disabled;
            if (gray == _grayed) return;   // 仅在跨入/跨出 Disabled 时动手（避免 hover/press 每次重写材质）
            _grayed = gray;
            ApplyAll();
        }

        /// <summary>
        /// Refreshes every captured original from what is on screen right now. Only valid while NOT
        /// greyed — greyed pixels are this controller's own output, and capturing those would bake
        /// the grey in as the "original".
        /// </summary>
        private void Recapture()
        {
            var keys = new List<Graphic>(_captured.Keys);
            foreach (var g in keys)
            {
                if (g == null) continue;
                var tmp = g as TMP_Text;
                _captured[g] = tmp != null
                    ? new Captured(g, null, tmp.color, true)
                    : new Captured(g, g.material, default, false);
            }
        }

        private void ApplyAll()
        {
            foreach (var kv in _captured)
            {
                var c = kv.Value;
                if (c.Graphic == null) continue;   // 销毁安全
                if (c.IsTmp)
                {
                    ((TMP_Text)c.Graphic).color = _grayed ? Desaturate(c.Color) : c.Color;
                }
                else if (c.Graphic is ISelfGrayscale self)
                {
                    // A graphic that owns its material greys itself from the inside. Swapping in
                    // UI-Grayscale would throw away what that material carries — a procedural
                    // surface's shape, border, glow and glass; an FxImage's blur and glow — and its
                    // own FlushParams would write the material straight back anyway.
                    self.SetDisabledGrayscale(_grayed);
                }
                else
                {
                    c.Graphic.material = _grayed ? SharedMaterial : c.Material;
                }
            }
        }

        private static Color Desaturate(Color c)
        {
            var luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(luma, luma, luma, c.a);
        }

        private void OnDestroy()
        {
            _sub?.Dispose();
            _sub = null;
        }
    }
}
