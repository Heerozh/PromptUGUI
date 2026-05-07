using System.Collections.Generic;
using System.Reflection;
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
            return InstantiateInto(new GameObject(def.Name, typeof(RectTransform)), def);
        }

        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def) {
            var result = new InstantiationResult {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false, result.Controls);

            return result;
        }

        void InstantiateRecursive(ElementNode node, Transform parent,
                                  bool parentIsLayoutGroup,
                                  Dictionary<string, IControl> controls) {
            if (parentIsLayoutGroup) {
                if (node.Attributes.ContainsKey("anchor"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: anchor ignored inside layout group");
                if (node.Attributes.ContainsKey("margin"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: margin ignored inside layout group");
            }

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
            if (entry.Prefab != null)
                BindFields(control, go);
            control.AttachTo(go);

            // 该节点的 id 入"当前作用域"——可能是 Screen 顶层，也可能是某个外层模板实例的 ScopedIds
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

            // 子节点的 id 作用域：若本节点是模板实例根，切换到本 Control 的 ScopedIds
            Dictionary<string, IControl> childScope = controls;
            if (node.IsTemplateInstanceRoot) {
                childScope = new Dictionary<string, IControl>();
                control.ReplaceScopedIds(childScope);
            }

            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, childScope);
        }

        static void BindFields(Control control, GameObject prefabRoot) {
            var t = control.GetType();
            foreach (var f in t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var bind = f.GetCustomAttribute<BindAttribute>();
                if (bind == null) continue;

                string childName = bind.ChildName ?? StripUnderscore(f.Name);
                var childTransform = FindChildByName(prefabRoot.transform, childName);
                if (childTransform == null) {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' not found");
                    continue;
                }

                var component = childTransform.GetComponent(f.FieldType);
                if (component == null) {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' " +
                        $"has no {f.FieldType.Name}");
                    continue;
                }

                f.SetValue(control, component);
            }
        }

        static string StripUnderscore(string name) =>
            name.StartsWith("_") ? char.ToUpperInvariant(name[1]) + name.Substring(2) : name;

        static Transform FindChildByName(Transform parent, string name) {
            for (int i = 0; i < parent.childCount; i++) {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
            }
            return null;
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
