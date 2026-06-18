using System;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Application.Tutorial
{
    /// <summary>
    /// overlay 视图 + 每步状态机：WaitTarget（整屏遮罩，逐 tick 解析路径，累计超时）
    /// → Active（开洞、摆气泡手指、装推进监听、逐 tick 跟随/检活/轮询 When）
    /// → 完成（acs.SetResult）或超时（acs.SetException）。
    /// LateUpdate 调 Tick(unscaledDeltaTime)；EditMode 测试经 UI.Tutorial.TickForTests 手动驱动。
    /// </summary>
    internal sealed class TutorialOverlayView : MonoBehaviour
    {
        private const float FingerGap = 60f;        // 手指 + 间距
        private const float FingerSpriteOffset = 180f;  // pugui_caret 本身朝下 → 抵消 TutorialPlacement 的“朝上”基准

        internal SpotlightMask Mask;
        private RectTransform _overlayRect, _bubbleRootRt, _bubbleRt, _fingerRt;
        private Canvas _canvas;
        private Text _bubbleText;

        private StepConfig _cfg;
        private AwaitableCompletionSource _acs;
        private bool _stepActive, _targetLive, _untilStarted;
        private float _waited;
        private RectTransform _targetRect;
        private TutorialClickRelay _relay;

        internal void Init(Screen screen)
        {
            Mask = screen.Get<IControl>("mask").GameObject.AddComponent<SpotlightMask>();
            Mask.color = UI.Theme.Resolve(UI.Tutorial.MaskColor);
            Mask.raycastTarget = true;
            _overlayRect = screen.RootGameObject.GetComponent<RectTransform>();
            _canvas = screen.RootGameObject.GetComponent<Canvas>();
            _bubbleRootRt = screen.Get<IControl>("bubbleRoot").RectTransform;
            _bubbleRt = screen.Get<IControl>("bubble").RectTransform;
            _bubbleText = screen.Get<Text>("bubbleText");
            _fingerRt = screen.Get<IControl>("finger").RectTransform;
            _bubbleRootRt.gameObject.SetActive(false);
        }

        internal void BeginStep(StepConfig cfg, AwaitableCompletionSource acs)
        {
            _cfg = cfg; _acs = acs; _stepActive = true; _targetLive = false;
            _waited = 0f; _untilStarted = false; _targetRect = null;
            bool block = cfg.Mode == TutorialMode.Block;
            Mask.enabled = block; Mask.raycastTarget = block;
            Mask.SetHole(null);
            ApplyBubbleText(cfg.Text);
            if (cfg.Target == null) EnterActive(null);
        }

        internal void EndStep()
        {
            RemoveRelay();
            _stepActive = false; _targetLive = false; _targetRect = null;
            if (Mask != null) Mask.SetHole(null);
            if (_bubbleRootRt != null) _bubbleRootRt.gameObject.SetActive(false);
        }

        internal void Tick(float dt)
        {
            if (!_stepActive) return;
            if (!_targetLive && _cfg.Target != null)
            {
                if (UI.TryResolvePath(_cfg.Target, out _, out var rect)) EnterActive(rect);
                else
                {
                    _waited += dt;
                    if (_cfg.Timeout >= 0f && _waited > _cfg.Timeout)
                        Fail(new TimeoutException(
                            $"tutorial step target '{_cfg.Target}' not found within {_cfg.Timeout}s"));
                    return;
                }
            }
            // 解析成功的同一 tick 故意不 return,继续往下:让激活当帧就为真的 When 谓词即时推进。
            if (_targetLive)
            {
                if (_cfg.Target != null && _targetRect == null) { LeaveActive(); return; }
                if (_cfg.Target != null) UpdateVisuals();
                if (_cfg.AdvanceKind == Advance.Kind.WhenK && _cfg.Predicate != null)
                {
                    bool done;
                    try { done = _cfg.Predicate(); }
                    catch (Exception ex) { Fail(ex); return; }   // 作者谓词抛错 → 一次性 Fail,不逐帧刷日志(与 Until 对齐)
                    if (done) Complete();
                }
            }
        }

        private void EnterActive(RectTransform rect)
        {
            _targetRect = rect; _targetLive = true; _waited = 0f;
            if (_cfg.AdvanceKind == Advance.Kind.TapTargetK && rect != null)
                _relay = AttachRelay(rect.gameObject);
            else if (_cfg.AdvanceKind == Advance.Kind.TapAnywhereK)
                _relay = AttachRelay(Mask.gameObject);
            if (_cfg.AdvanceKind == Advance.Kind.UntilK && !_untilStarted)
            { _untilStarted = true; _ = AwaitCondition(); }
            if (_cfg.Target != null) UpdateVisuals(); else CenterBubble();
        }

        private void LeaveActive()
        {
            RemoveRelay(); _targetLive = false; _targetRect = null;
            if (Mask != null) Mask.SetHole(null);
        }

        private void UpdateVisuals()
        {
            var local = WorldRectToLocal(_targetRect, _overlayRect);
            var hole = new Rect(local.xMin - _cfg.Padding, local.yMin - _cfg.Padding,
                local.width + 2f * _cfg.Padding, local.height + 2f * _cfg.Padding);
            if (_cfg.Mode == TutorialMode.Block) Mask.SetHole(hole);
            if (!_bubbleRootRt.gameObject.activeSelf) return;   // 无文案 → 不摆气泡/手指
            // 气泡/手指夹进安全区(遮罩+挖洞仍走全屏 _overlayRect.rect),避开 notch / 挖孔 / Home 条。
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            var safeOverlay = ApplySafeInset(_overlayRect.rect, sf);
            var r = TutorialPlacement.Choose(safeOverlay, local, _bubbleRt.rect.size,
                FingerGap, _cfg.Place);
            _bubbleRootRt.anchoredPosition = r.BubblePos;
            _fingerRt.gameObject.SetActive(true);
            _fingerRt.anchoredPosition = r.FingerPos - r.BubblePos;
            _fingerRt.localEulerAngles = new Vector3(0f, 0f, r.FingerAngle + FingerSpriteOffset);
        }

        private void CenterBubble()
        {
            _bubbleRootRt.anchoredPosition = Vector2.zero;
            _fingerRt.gameObject.SetActive(false);
        }

        private void ApplyBubbleText(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            _bubbleRootRt.gameObject.SetActive(show);
            if (show) _bubbleText.TextValue = text;
        }

        private async Awaitable AwaitCondition()
        { try { await _cfg.Condition(); Complete(); } catch (Exception ex) { Fail(ex); } }

        private TutorialClickRelay AttachRelay(GameObject go)
        {
            var r = go.AddComponent<TutorialClickRelay>();
            r.OnClicked = Complete;
            return r;
        }

        private void RemoveRelay()
        {
            if (_relay == null) return;
            _relay.OnClicked = null;
            if (UnityEngine.Application.isPlaying) Destroy(_relay); else DestroyImmediate(_relay);
            _relay = null;
        }

        private void Complete() { if (_stepActive) { _stepActive = false; _acs.TrySetResult(); } }
        private void Fail(Exception ex) { if (_stepActive) { _stepActive = false; _acs.TrySetException(ex); } }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        // 把全屏 overlay 矩形按设备安全区(notch / 挖孔 / Home 条)内缩成气泡可落位的安全矩形。
        // 遮罩 / 挖洞仍用全屏坐标,只有气泡 + 手指被夹进这个安全矩形(见 UpdateVisuals)。
        // 复用 SafeAreaTracker 的内缩算法 + 测试注入钩子;PC(inset=0)下原样返回 full。
        internal static Rect ApplySafeInset(Rect full, float scaleFactor)
        {
            float sf = SafeAreaTracker.ScaleFactorOverride != null
                ? SafeAreaTracker.ScaleFactorOverride()
                : scaleFactor;
            var (l, r, b, t) = SafeAreaTracker.ComputeInsetsDesignPx(
                SafeAreaTracker.ResolveSafeAreaStatic(),
                SafeAreaTracker.ResolveScreenSizeStatic(),
                sf);
            return Rect.MinMaxRect(full.xMin + l, full.yMin + b, full.xMax - r, full.yMax - t);
        }

        // 每帧调用,复用同一数组避免 GC(Tick 主线程同步,无重入)。
        private static readonly Vector3[] s_corners = new Vector3[4];

        internal static Rect WorldRectToLocal(RectTransform target, RectTransform overlayRect)
        {
            target.GetWorldCorners(s_corners);
            var srcCanvas = target.GetComponentInParent<Canvas>();
            Camera srcCam = srcCanvas != null ? srcCanvas.worldCamera : null;
            var dstCanvas = overlayRect.GetComponentInParent<Canvas>();
            Camera dstCam = dstCanvas != null ? dstCanvas.worldCamera : null;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(srcCam, s_corners[i]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, sp, dstCam, out var lp);
                min = Vector2.Min(min, lp); max = Vector2.Max(max, lp);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal bool IsBlockingStep => _stepActive && _cfg.Mode == TutorialMode.Block;
        internal bool BubbleRootActiveForTests => _bubbleRootRt != null && _bubbleRootRt.gameObject.activeSelf;
        internal string BubbleTextForTests =>
            _bubbleText != null && _bubbleText.TmpComponent != null ? _bubbleText.TmpComponent.text : null;
    }
}
