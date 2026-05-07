using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application {
    public sealed class InstantiationResult {
        public GameObject Root;
        public Dictionary<string, IControl> Controls;
    }

    public sealed class ScreenInstantiator {
        readonly ControlRegistry _registry;

        public ScreenInstantiator(ControlRegistry registry) {
            _registry = registry;
        }

        public InstantiationResult Instantiate(ScreenDef def) {
            var result = new InstantiationResult {
                Root = new GameObject(def.Name, typeof(RectTransform)),
                Controls = new Dictionary<string, IControl>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform, result.Controls);

            return result;
        }

        void InstantiateRecursive(ElementNode node, Transform parent,
                                  Dictionary<string, IControl> controls) {
            var entry = _registry.Resolve(node.Tag);

            GameObject go;
            Control control;

            if (entry.Prefab != null) {
                go = Object.Instantiate(entry.Prefab, parent);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            } else {
                go = new GameObject(node.Id ?? node.Tag, typeof(RectTransform));
                go.transform.SetParent(parent, worldPositionStays: false);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            }

            if (!string.IsNullOrEmpty(node.Id))
                go.name = node.Id;

            control.Id = node.Id;
            control.AttachTo(go);

            if (!string.IsNullOrEmpty(node.Id))
                controls[node.Id] = control;

            // 应用控件特定属性
            foreach (var kv in node.Attributes) {
                if (IsCommonAttribute(kv.Key)) continue;
                if (entry.Meta.HasAttribute(kv.Key))
                    entry.Meta.Apply(control, kv.Key, kv.Value);
            }

            // 文本简写
            if (!string.IsNullOrEmpty(node.TextContent) && entry.DefaultTextAttr != null)
                entry.Meta.Apply(control, entry.DefaultTextAttr, node.TextContent);

            // 应用通用属性
            node.Attributes.TryGetValue("anchor", out var anchor);
            node.Attributes.TryGetValue("size",   out var size);
            node.Attributes.TryGetValue("width",  out var width);
            node.Attributes.TryGetValue("height", out var height);
            node.Attributes.TryGetValue("margin", out var margin);
            node.Attributes.TryGetValue("pivot",  out var pivot);
            node.Attributes.TryGetValue("hidden", out var hiddenStr);
            node.Attributes.TryGetValue("interactable", out var interactableStr);
            bool hidden       = hiddenStr == "true";
            bool interactable = interactableStr != "false";

            control.ApplyCommon(anchor, size, width, height, margin, pivot, hidden, interactable);

            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, controls);
        }

        static bool IsCommonAttribute(string name) {
            switch (name) {
                case "anchor":
                case "size":
                case "width":
                case "height":
                case "margin":
                case "pivot":
                case "hidden":
                case "interactable":
                    return true;
            }
            return false;
        }
    }
}
