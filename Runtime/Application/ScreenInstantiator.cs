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
        public Dictionary<ElementNode, Control> NodeToControl;
    }

    public sealed class ScreenInstantiator {
        readonly ControlRegistry _registry;
        readonly VariantStore _variants;

        public ScreenInstantiator(ControlRegistry registry, VariantStore variants) {
            _registry = registry;
            _variants = variants;
        }

        public InstantiationResult Instantiate(ScreenDef def) {
            return InstantiateInto(new GameObject(def.Name, typeof(RectTransform)), def);
        }

        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def) {
            var result = new InstantiationResult {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
                NodeToControl = new Dictionary<ElementNode, Control>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false,
                                     result.Controls, result.NodeToControl);

            return result;
        }

        internal void InstantiateRecursive(ElementNode node, Transform parent,
                                           bool parentIsLayoutGroup,
                                           Dictionary<string, IControl> controls,
                                           Dictionary<ElementNode, Control> nodeMap) {
            if (parentIsLayoutGroup) {
                if (node.Attributes.ContainsKey("anchor")
                    || node.VariantOverrides.ContainsKey("anchor"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: anchor ignored inside layout group");
                if (node.Attributes.ContainsKey("margin")
                    || node.VariantOverrides.ContainsKey("margin"))
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

            if (!string.IsNullOrEmpty(node.Id))
                controls[node.Id] = control;
            nodeMap[node] = control;

            ControlAttributeApplier.Apply(node, control, entry, _variants);

            // 子节点的 id 作用域
            Dictionary<string, IControl> childScope = controls;
            if (node.IsTemplateInstanceRoot) {
                childScope = new Dictionary<string, IControl>();
                control.ReplaceScopedIds(childScope);
            }

            bool selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, childScope, nodeMap);
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
    }
}
