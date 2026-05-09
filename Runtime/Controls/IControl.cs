using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public interface IControl : IDisposable
    {
        public string Id { get; }
        public GameObject GameObject { get; }
        public RectTransform RectTransform { get; }
        public bool Hidden { get; set; }
        public bool Interactable { get; set; }

        /// <summary>
        /// 模板实例根才会有非空的字典；其他 Control 返回空只读字典。
        /// 用于 Screen.Get("a/b") 路径解析。
        /// </summary>
        public IReadOnlyDictionary<string, IControl> ScopedIds { get; }
    }
}
