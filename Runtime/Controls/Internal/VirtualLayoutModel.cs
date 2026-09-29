namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Heights and states of a virtual list's items (spec 2026-09-29-scrolllist-virtualization §5.1) — plain
    /// arithmetic, no Unity types. Every item occupies <c>extent = height + spacing</c> except a collapsed one
    /// (a hidden or <c>flow="false"</c> row the VerticalLayoutGroup leaves out of <c>rectChildren</c>), which
    /// occupies nothing; the last visible item's spacing is not part of the total. That is exactly how the
    /// group lays out, so the <c>Leading</c> / <c>Trailing</c> extents built from here line the realized rows up
    /// with the items they stand for.
    /// <para>Spacing and padding are handed in at every sync (<see cref="SetMetrics"/>): the list's layout group
    /// is rewritten on every ReSolve in attribute order, so the model never keeps its own idea of them.</para>
    /// </summary>
    internal sealed class VirtualLayoutModel
    {
        private enum State : byte
        {
            Unknown,     // never measured: uses the estimate
            Stale,       // measured before an invalidation: keeps its own old value as its estimate
            Measured,
            Collapsed,   // no height, no spacing
        }

        private float[] _height = new float[16];
        private State[] _state = new State[16];
        private float[] _spareHeight = new float[16];
        private State[] _spareState = new State[16];
        private int _count;

        private double _measuredSum;
        private int _measuredCount;
        private float _fallback;
        private bool _hasFallback;

        private float _spacing;
        private float _padTop;
        private float _padBottom;

        // _prefix[i] = Σ_{j<i} extent_j; rebuilt on demand, O(N) — microseconds for a thousand items.
        private double[] _prefix = new double[17];
        private int _visibleCount;
        private bool _dirty = true;

        public int Count => _count;

        public bool HasEstimate => _measuredCount > 0 || _hasFallback;

        /// <summary>Mean height of the measured items; the pre-invalidation mean while none is measured.</summary>
        public float Estimate =>
            _measuredCount > 0 ? (float)(_measuredSum / _measuredCount) : _hasFallback ? _fallback : 0f;

        public void SetMetrics(float spacing, float padTop, float padBottom)
        {
            if (spacing == _spacing && padTop == _padTop && padBottom == _padBottom) return;
            _spacing = spacing;
            _padTop = padTop;
            _padBottom = padBottom;
            _dirty = true;
        }

        /// <summary><paramref name="count"/> unknown items; the fallback estimate is forgotten too.</summary>
        public void Reset(int count)
        {
            EnsureCapacity(ref _height, ref _state, count);
            for (var i = 0; i < count; i++)
            {
                _height[i] = 0f;
                _state[i] = State.Unknown;
            }
            _count = count;
            _measuredSum = 0;
            _measuredCount = 0;
            _hasFallback = false;
            _dirty = true;
        }

        /// <summary>
        /// Items move with a push: new item j takes the height and state of old item
        /// <c>newToOld[j]</c>, or starts unknown when that is −1.
        /// </summary>
        public void Remap(int[] newToOld, int newCount)
        {
            EnsureCapacity(ref _spareHeight, ref _spareState, newCount);
            _measuredSum = 0;
            _measuredCount = 0;
            for (var j = 0; j < newCount; j++)
            {
                var from = newToOld[j];
                if (from >= 0 && from < _count)
                {
                    _spareHeight[j] = _height[from];
                    _spareState[j] = _state[from];
                    if (_state[from] == State.Measured)
                    {
                        _measuredSum += _height[from];
                        _measuredCount++;
                    }
                }
                else
                {
                    _spareHeight[j] = 0f;
                    _spareState[j] = State.Unknown;
                }
            }
            (_height, _spareHeight) = (_spareHeight, _height);
            (_state, _spareState) = (_spareState, _state);
            _count = newCount;
            _dirty = true;
        }

        public void SetMeasured(int i, float height)
        {
            if (_state[i] == State.Measured)
            {
                _measuredSum -= _height[i];
                _measuredCount--;
            }
            _height[i] = height;
            _state[i] = State.Measured;
            _measuredSum += height;
            _measuredCount++;
            _dirty = true;
        }

        public void SetCollapsed(int i)
        {
            if (_state[i] == State.Collapsed) return;
            if (_state[i] == State.Measured)
            {
                _measuredSum -= _height[i];
                _measuredCount--;
            }
            _height[i] = 0f;
            _state[i] = State.Collapsed;
            _dirty = true;
        }

        /// <summary>
        /// Every measurement goes stale — the width changed, or a ReSolve may have changed a font. Each item
        /// keeps its old value as its own estimate; the current mean becomes the fallback until something is
        /// measured again.
        /// </summary>
        public void InvalidateAll()
        {
            if (_measuredCount > 0)
            {
                _fallback = (float)(_measuredSum / _measuredCount);
                _hasFallback = true;
            }
            for (var i = 0; i < _count; i++)
                if (_state[i] == State.Measured) _state[i] = State.Stale;
            _measuredSum = 0;
            _measuredCount = 0;
            _dirty = true;
        }

        public bool IsMeasured(int i) => _state[i] == State.Measured;

        public bool IsCollapsed(int i) => _state[i] == State.Collapsed;

        public float HeightOf(int i)
        {
            switch (_state[i])
            {
                case State.Measured:
                case State.Stale:
                    return _height[i];
                case State.Collapsed:
                    return 0f;
                default:
                    return Estimate;
            }
        }

        /// <summary>Distance from the content's top edge to item <paramref name="i"/>'s top edge.</summary>
        public float OffsetOf(int i)
        {
            EnsurePrefix();
            return (float)(_padTop + _prefix[i]);
        }

        public float Total
        {
            get
            {
                EnsurePrefix();
                var sum = _prefix[_count];
                if (_visibleCount > 0) sum -= _spacing;   // the last visible item carries no trailing gap
                return (float)(_padTop + sum + _padBottom);
            }
        }

        /// <summary>What the items before <paramref name="first"/> occupy, their spacing included.</summary>
        public float ExtentBefore(int first)
        {
            EnsurePrefix();
            return (float)_prefix[first];
        }

        /// <summary>What the items after <paramref name="last"/> occupy, their spacing included.</summary>
        public float ExtentAfter(int last)
        {
            EnsurePrefix();
            return (float)(_prefix[_count] - _prefix[last + 1]);
        }

        /// <summary>
        /// The items that intersect <c>(top, bottom)</c> — touching does not count. There is always at least one
        /// while the model is not empty (the nearest item when the range falls in padding, in a gap or past the
        /// end), which keeps <c>Leading + Trailing</c> in step with the group's <c>(n − 1) · spacing</c>.
        /// </summary>
        public bool TryWindow(float top, float bottom, out int first, out int last)
        {
            first = last = -1;
            if (_count == 0) return false;
            EnsurePrefix();

            // first = the smallest i whose bottom edge is below top.
            int lo = 0, hi = _count - 1;
            first = _count - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (_padTop + _prefix[mid] + HeightOf(mid) > top) { first = mid; hi = mid - 1; }
                else lo = mid + 1;
            }

            // last = the largest i whose top edge is above bottom.
            lo = 0;
            hi = _count - 1;
            last = 0;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (_padTop + _prefix[mid] < bottom) { last = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            if (first > last) first = last;
            return true;
        }

        /// <summary>The item whose extent holds <paramref name="y"/>, clamped to the first / last item; −1 if empty.</summary>
        public int IndexAt(float y)
        {
            if (_count == 0) return -1;
            EnsurePrefix();
            int lo = 0, hi = _count - 1, found = 0;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (_padTop + _prefix[mid] <= y) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        private void EnsurePrefix()
        {
            if (!_dirty) return;
            if (_prefix.Length < _count + 1) _prefix = new double[System.Math.Max(_count + 1, _prefix.Length * 2)];
            _prefix[0] = 0;
            _visibleCount = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_state[i] == State.Collapsed)
                {
                    _prefix[i + 1] = _prefix[i];
                    continue;
                }
                _prefix[i + 1] = _prefix[i] + HeightOf(i) + _spacing;
                _visibleCount++;
            }
            _dirty = false;
        }

        private static void EnsureCapacity(ref float[] heights, ref State[] states, int count)
        {
            if (heights.Length >= count) return;
            var size = heights.Length;
            while (size < count) size *= 2;
            heights = new float[size];
            states = new State[size];
        }
    }
}
