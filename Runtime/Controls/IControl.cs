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
        /// What this control stands for in a drag and drop (spec 2026-11-16-drag-drop §6) — the item a <c>drag=</c>
        /// source carries, the slot a <c>drop=</c> target is. Written from C#, typically in a bind callback.
        /// </summary>
        public object DragData { get => null; set { } }

        /// <summary>
        /// As a <c>drop=</c> target: whether it takes the drag, on top of the kind match. Asked once per drag, when
        /// it is lifted; null = yes.
        /// </summary>
        public System.Func<DragInfo, bool> CanDrop { get => null; set { } }

        /// <summary>
        /// 模板实例根才会有非空的字典；其他 Control 返回空只读字典。
        /// 用于 Screen.Get("a/b") 路径解析。
        /// </summary>
        public IReadOnlyDictionary<string, IControl> ScopedIds { get; }

        /// <summary>路径查询：a/b/c 沿 ScopedIds 下钻；与 IScreen.Get 对齐。</summary>
        public T Get<T>(string idPath) where T : class, IControl;
        public IControl Get(string idPath);
    }
}
