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
    /// 子树去色：非 TMP <see cref="Graphic"/> 换共享灰度材质，<see cref="TMP_Text"/> 在生成网格时把字形
    /// 顶点色换成亮度灰（<see cref="TMP_Text.OnPreRenderText"/>）；离开 Disabled 还原。作者未写任何
    /// <c>disabled*</c> 时的默认禁用外观（由 <see cref="DisabledGrayscaleInstaller"/> 装上）。与
    /// <c>transition</c> 无关——靠 OnState 流驱动。
    /// </summary>
    /// <remarks>
    /// <para>TMP 的灰只落在顶点上，<b>从不写 <c>tmp.color</c></b>：那是文字自己的颜色，属于它的
    /// <c>color=</c> / textColor / Variant / 主题 / 代码。灰度曾经把它 capture 一次、离开禁用时写回 ——
    /// 于是代码写的字色禁用再启用一次就没了，禁用期间写的颜色也直接亮着显示。顶点钩子每次重新生成都
    /// 从当前颜色出发，谁写都不会被覆盖。</para>
    /// <para>非 TMP 的原始材质 capture-once：re-<see cref="Configure"/>（ReSolve）不在禁用中时重新
    /// 捕获，禁用中不重捕，避免把灰度态误当原始态。每次访问 graphic 前判空（销毁安全，呼应
    /// <see cref="StateTintReactor"/>）。</para>
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

        // 非 TMP graphic → 原材质
        private readonly Dictionary<Graphic, Material> _captured = new Dictionary<Graphic, Material>();
        // 已挂上顶点钩子的 TMP 文本
        private readonly HashSet<TMP_Text> _texts = new HashSet<TMP_Text>();
        private IStateSource _source;
        private IDisposable _sub;
        private bool _grayed;

        public void Configure(IReadOnlyList<Graphic> graphics)
        {
            // 先捕获原始态 / 挂钩子，再订阅：订阅会同步重放当前状态，首装即 Disabled 时必须先有原始态可还原。
            foreach (var g in graphics)
            {
                if (g == null) continue;
                if (g is TMP_Text tmp)
                {
                    if (_texts.Add(tmp)) tmp.OnPreRenderText += GreyVertices;
                    continue;
                }
                if (!_captured.ContainsKey(g)) _captured[g] = g.material;
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
                // 就是真相 —— 应该重新捕获，而不是把旧捕获写回去。同一族缺陷：从当前声明推，别 latch。
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
        /// Refreshes every captured original material from what is on screen right now. Only valid
        /// while NOT greyed — greyed pixels are this controller's own output, and capturing those
        /// would bake the grey in as the "original".
        /// </summary>
        private void Recapture()
        {
            var keys = new List<Graphic>(_captured.Keys);
            foreach (var g in keys)
                if (g != null) _captured[g] = g.material;
        }

        private void ApplyAll()
        {
            foreach (var tmp in _texts)
            {
                if (tmp == null) continue;   // 销毁安全
                // 下一次画布重建时重新生成网格，GreyVertices 按当前 _grayed 上灰或不上。
                tmp.havePropertiesChanged = true;
                tmp.SetVerticesDirty();
            }

            foreach (var kv in _captured)
            {
                var g = kv.Key;
                if (g == null) continue;   // 销毁安全
                if (g is ISelfGrayscale self)
                {
                    // A graphic that owns its material greys itself from the inside. Swapping in
                    // UI-Grayscale would throw away what that material carries — a procedural
                    // surface's shape, border, glow and glass; an FxImage's blur and glow — and its
                    // own FlushParams would write the material straight back anyway.
                    self.SetDisabledGrayscale(_grayed);
                }
                else
                {
                    g.material = _grayed ? SharedMaterial : kv.Value;
                }
            }
        }

        /// <summary>
        /// TMP 每次生成网格、上传前调用：禁用中把全部字形顶点色（含回落字体 / 内联 sprite 的子网格）
        /// 换成亮度灰。富文本 <c>&lt;color&gt;</c> 与顶点渐变一并变灰。
        /// </summary>
        private void GreyVertices(TMP_TextInfo info)
        {
            if (!_grayed || info == null) return;
            for (var m = 0; m < info.materialCount && m < info.meshInfo.Length; m++)
            {
                var colors = info.meshInfo[m].colors32;
                if (colors == null) continue;
                var count = Mathf.Min(info.meshInfo[m].vertexCount, colors.Length);
                for (var i = 0; i < count; i++) colors[i] = Desaturate(colors[i]);
            }
        }

        /// <summary>Luminance grey of a vertex colour, alpha kept.</summary>
        internal static Color32 Desaturate(Color32 c)
        {
            var luma = (byte)Mathf.RoundToInt(c.r * 0.299f + c.g * 0.587f + c.b * 0.114f);
            return new Color32(luma, luma, luma, c.a);
        }

        private void OnDestroy()
        {
            foreach (var tmp in _texts)
                if (tmp != null) tmp.OnPreRenderText -= GreyVertices;
            _texts.Clear();
            _sub?.Dispose();
            _sub = null;
        }
    }
}
