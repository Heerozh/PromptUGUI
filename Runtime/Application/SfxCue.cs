using PromptUGUI.Controls;

namespace PromptUGUI.Application
{
    /// <summary>
    /// One UI sound cue (spec 2026-11-23-ui-sfx-cues §6.1): a real user action — or a Screen / Modal opening or starting to
    /// close — and the key the game should play for it. The library plays nothing; see <see cref="UI.Sfx.OnCue"/>.
    /// </summary>
    public readonly struct SfxCue
    {
        internal SfxCue(string kind, string action, string key, string defaultKey,
                        IControl source, IControl target, IScreen screen)
        {
            Kind = kind;
            Action = action;
            Key = key;
            DefaultKey = defaultKey;
            Source = source;
            Target = target;
            Screen = screen;
        }

        /// <summary>What raised it: <c>btn</c>, <c>toggle</c>, <c>tab</c>, <c>tabmenu</c>, <c>dropdown</c>, <c>popover</c>,
        /// <c>collapsible</c>, <c>carousel</c>, <c>screen</c>, <c>modal</c>, <c>drag</c>, <c>reorder</c> — or a custom control's
        /// tag in lower case.</summary>
        public string Kind { get; }

        /// <summary><c>click</c>, <c>checked</c>, <c>unchecked</c>, <c>select</c>, <c>deselect</c>, <c>expand</c>, <c>collapse</c>,
        /// <c>flip</c>, <c>open</c>, <c>close</c>, <c>lift</c>, <c>land</c>, <c>return</c>, <c>deny</c> — or a custom one.</summary>
        public string Action { get; }

        /// <summary>The key to play: the one <c>sfx=</c> decided, else <see cref="DefaultKey"/>. Never <c>none</c> — a muted
        /// action raises no cue.</summary>
        public string Key { get; }

        /// <summary><c>Kind + "." + Action</c>: the fallback for a custom key the game has not mapped.</summary>
        public string DefaultKey { get; }

        /// <summary>The control acted on (the Btn, the Toggle, the Tab picked, the dragged control, the list row…); null for a
        /// <c>screen</c> / <c>modal</c> cue.</summary>
        public IControl Source { get; }

        /// <summary><c>drag.land</c>: the drop target it went into; otherwise null.</summary>
        public IControl Target { get; }

        /// <summary>The Screen the cue belongs to — the Screen itself for <c>screen</c> / <c>modal</c>, the target's for
        /// <c>drag.land</c>. Null for a control outside any Screen.</summary>
        public IScreen Screen { get; }

        public override string ToString() => $"{Kind}.{Action} → {Key}";
    }
}
