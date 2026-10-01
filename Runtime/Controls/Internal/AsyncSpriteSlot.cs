using PromptUGUI.Application;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>How a host's static (non-runtime-set) resolution went.</summary>
    internal enum StaticResult
    {
        /// <summary>Resolved, or a value the static path treats as "nothing" without an error (Resources path).</summary>
        Ok,
        /// <summary><c>UI.SpriteResolver</c> is still loading: empty and silent for now.</summary>
        Deferred,
        /// <summary>Failed, and the failure was logged exactly as before runtime sets existed.</summary>
        Failed,
    }

    /// <summary>The control side of an <see cref="AsyncSpriteSlot"/> (<c>&lt;Icon&gt;</c>, <c>&lt;Image&gt;</c>).</summary>
    internal interface ISpriteSlotHost
    {
        /// <summary>The Graphic the slot writes to.</summary>
        public UnityImage SlotGraphic { get; }

        /// <summary><c>Icon</c> / <c>Image</c>: the word log lines start with.</summary>
        public string SlotTag { get; }

        /// <summary>
        /// Today's resolution of a value that names no runtime set, logs included — byte for byte what the
        /// setter did before (RuntimeSourceAttributionTests pins the at-lines).
        /// </summary>
        public StaticResult ResolveStatic(string value, out Sprite sprite);

        /// <summary>Recomputes what depends on the sprite (type, aspect, effect material) outside an apply pass.</summary>
        public void RefreshDerived();

        /// <summary>Whether the element's size comes from the sprite (spec §7.5).</summary>
        public bool SizeDependsOnSprite { get; }
    }

    /// <summary>
    /// The sprite half of <c>&lt;Icon name&gt;</c> / <c>&lt;Image sprite&gt;</c> (spec 2026-10-01-runtime-sprite-sets-design §7).
    /// Static values resolve as before. A value naming an on-demand runtime set shows the set's Loading
    /// placeholder and refreshes its own control when the sprite arrives — no ReSolve, so a code-written
    /// (runtime-owned) name is reached too. Its waiting lives in the registry's tables, never in the control's
    /// <c>Track</c> bag: ScrollList releases that bag on every row reuse (spec §7.4).
    /// </summary>
    internal sealed class AsyncSpriteSlot : IRuntimeSpriteListener
    {
        internal enum SlotState { None, Static, WaitingStaticResolver, Ready, Loading, Missing, Detached }

        private readonly Control _owner;
        private readonly ISpriteSlotHost _host;

        private string _value;
        private SlotState _state;
        private RuntimeSpriteSets.Registration _reg;
        private string _key;
        private string _waitingName;
        private bool _waitingStatic;
        private bool _hiddenByUs;
        private bool _sizeWarned;

        // Bumped by every Set / Release / Detach. A call that can run user code (a loader's synchronous part,
        // a log callback) compares it afterwards: a reentrant Set has superseded this one.
        private int _gen;

        public AsyncSpriteSlot(Control owner, ISpriteSlotHost host)
        {
            _owner = owner;
            _host = host;
        }

        public Control Owner => _owner;

        internal SlotState State => _state;

        internal string Value => _value;

        public void Set(string value)
        {
            if (value == _value && IsRuntimeState(_state)) return;
            Release();
            _value = value;
            Resolve();
            if (!_owner.InApplyPass) CheckSize();
        }

        /// <summary>The host's <c>OnAfterApply</c> calls this last: the pass's <c>SizeFromNative</c> is final.</summary>
        public void AfterPass() => CheckSize();

        private static bool IsRuntimeState(SlotState s) =>
            s == SlotState.Ready || s == SlotState.Loading || s == SlotState.Missing || s == SlotState.Detached;

        private void Resolve()
        {
            var gen = ++_gen;
            if (string.IsNullOrEmpty(_value))
            {
                _state = SlotState.None;
                Show(null, hideIfNull: false);
                return;
            }

            var colon = _value.IndexOf(':');
            if (colon > 0 && RuntimeSpriteSets.TryGet(_value.Substring(0, colon), out var reg))
            {
                ResolveRuntime(reg, _value.Substring(colon + 1), gen);
                return;
            }

            var result = _host.ResolveStatic(_value, out var sprite);
            if (gen != _gen) return;
            _state = result == StaticResult.Deferred ? SlotState.WaitingStaticResolver : SlotState.Static;
            Show(sprite, hideIfNull: false);
            if (colon <= 0) return;

            var setName = _value.Substring(0, colon);
            if (result == StaticResult.Deferred)
            {
                // XML-written values are replayed by EndSpriteResolverLoad's broadcast; only a value code wrote
                // needs the slot to come back for it (spec §7.7).
                if (!_owner.InApplyPass)
                {
                    RuntimeSpriteSets.WaitForStatic(this);
                    _waitingStatic = true;
                }
                WaitForName(setName);
            }
            else if (result == StaticResult.Failed && !UI.LoadedSpriteSetNames.Contains(setName))
            {
                // A loaded SpriteSet asset with this name: a runtime set of the same name could never register.
                WaitForName(setName);
            }
        }

        private void ResolveRuntime(RuntimeSpriteSets.Registration reg, string key, int gen)
        {
            _reg = reg;
            _key = key;
            RuntimeSpriteSets.Bind(reg, this);

            if (!reg.OnDemand)
            {
                if (reg.Entries.TryGetValue(key, out var hit))
                {
                    ShowRuntime(SlotState.Ready, hit.Sprite);
                    return;
                }
                RuntimeSpriteSets.WarnMissingOnce(reg, key, _value, _host.SlotTag, _owner);
                if (gen != _gen) return;
                ShowRuntime(SlotState.Missing, reg.Missing);
                return;
            }

            var entry = RuntimeSpriteSets.Request(reg, key, _owner);
            if (gen != _gen) return;
            switch (entry.State)
            {
                case RuntimeSpriteSets.KeyState.Ready:
                    ShowRuntime(SlotState.Ready, entry.Result.Sprite);
                    break;
                case RuntimeSpriteSets.KeyState.Missing:
                    ReportMissing(entry);
                    if (gen != _gen) return;
                    ShowRuntime(SlotState.Missing, reg.Missing);
                    break;
                default:
                    RuntimeSpriteSets.AddWaiter(entry, this);
                    ShowRuntime(SlotState.Loading, reg.Loading);
                    break;
            }
        }

        private void ReportMissing(RuntimeSpriteSets.KeyEntry entry)
        {
            // A loader exception was already reported by the registry (with this control as context).
            if (entry.Error == null)
                RuntimeSpriteSets.WarnMissingOnce(_reg, _key, _value, _host.SlotTag, _owner);
        }

        private void ShowRuntime(SlotState state, Sprite sprite)
        {
            _state = state;
            Show(sprite, hideIfNull: true);
            // Inside the host's own pass its OnAfterApply refreshes; outside it nothing else will (spec §7.3).
            if (!_owner.InApplyPass) _host.RefreshDerived();
        }

        private void Show(Sprite sprite, bool hideIfNull)
        {
            var img = _host.SlotGraphic;
            if (img == null) return;
            if (img is FxImage fx) fx.DrawNothingWhenEmpty = hideIfNull;
            else
            {
                // A pre-existing plain Image (not FxImage) cannot draw nothing: fall back to disabling it,
                // and only ever re-enable what this slot disabled.
                var hide = hideIfNull && sprite == null;
                if (hide != _hiddenByUs)
                {
                    img.enabled = !hide;
                    _hiddenByUs = hide;
                }
            }
            img.sprite = sprite;
        }

        private void WaitForName(string setName)
        {
            _waitingName = setName;
            RuntimeSpriteSets.WaitForName(setName, this);
        }

        private void Release()
        {
            _gen++;
            if (_reg != null)
            {
                RuntimeSpriteSets.Unbind(_reg, _key, this);
                _reg = null;
            }
            _key = null;
            if (_waitingName != null)
            {
                RuntimeSpriteSets.StopWaitingForName(_waitingName, this);
                _waitingName = null;
            }
            if (_waitingStatic)
            {
                RuntimeSpriteSets.StopWaitingForStatic(this);
                _waitingStatic = false;
            }
        }

        // Re-resolves the current value after the registry changed under it (not a write by the author).
        private void Reresolve()
        {
            Release();
            Resolve();
            if (!_owner.InApplyPass) CheckSize();
        }

        private void CheckSize()
        {
            if (_reg == null || !_reg.OnDemand || !_host.SizeDependsOnSprite) return;
            if (_owner.SourceNode != null ? !RuntimeSpriteSets.FirstSizeWarningFor(_owner) : _sizeWarned) return;
            _sizeWarned = true;
            UILog.Warn(_owner,
                $"{_host.SlotTag} '{_value}': its size comes from the sprite (size=\"native\", or a width / height " +
                $"left out), but runtime SpriteSet '{_reg.Name}' loads on demand — the element keeps the size it had " +
                "when its attributes were applied, it is not resized when the sprite arrives. Give it an explicit size.");
        }

        // ── IRuntimeSpriteListener ────────────────────────────────────────────────────────────

        public void OnKeySettled(RuntimeSpriteSets.Registration reg, string key, RuntimeSpriteSets.KeyEntry entry)
        {
            if (_reg != reg || _key != key || _state != SlotState.Loading) return;
            var gen = _gen;
            if (entry.State == RuntimeSpriteSets.KeyState.Ready)
            {
                ShowRuntime(SlotState.Ready, entry.Result.Sprite);
                return;
            }
            ReportMissing(entry);
            if (gen != _gen) return;
            ShowRuntime(SlotState.Missing, reg.Missing);
        }

        public void OnSetRegistered(string setName)
        {
            if (_waitingName != setName) return;
            _waitingName = null;   // the registry has already dropped the whole waiting set
            Reresolve();
        }

        public void OnSetUnregistered(RuntimeSpriteSets.Registration reg)
        {
            if (_reg != reg) return;
            _gen++;
            _reg = null;   // the registry has already cleared its tables for this set
            _key = null;
            _state = SlotState.Detached;
            // Never the set's Missing sprite: it belongs to the caller too, about to be destroyed (spec §6.6).
            Show(null, hideIfNull: true);
            if (!_owner.InApplyPass) _host.RefreshDerived();
            WaitForName(reg.Name);
        }

        public void OnStaticResolverInstalled()
        {
            _waitingStatic = false;   // the registry has already cleared its static table
            if (_state != SlotState.WaitingStaticResolver) return;
            Reresolve();
        }
    }
}
