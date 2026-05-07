using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls {
    public interface IControl : IDisposable {
        string Id { get; }
        GameObject GameObject { get; }
        RectTransform RectTransform { get; }
        bool Hidden { get; set; }
        bool Interactable { get; set; }

        /// <summary>
        /// 模板实例根才会有非空的字典；其他 Control 返回空只读字典。
        /// 用于 Screen.Get("a/b") 路径解析。
        /// </summary>
        IReadOnlyDictionary<string, IControl> ScopedIds { get; }
    }
}
