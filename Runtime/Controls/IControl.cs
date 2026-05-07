using System;
using UnityEngine;

namespace PromptUGUI.Controls {
    public interface IControl : IDisposable {
        string Id { get; }
        GameObject GameObject { get; }
        RectTransform RectTransform { get; }
        bool Hidden { get; set; }
        bool Interactable { get; set; }
    }
}
