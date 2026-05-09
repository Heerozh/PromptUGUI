using System.Collections.Generic;
using System.Reflection;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application
{
    public sealed class InstantiationResult
    {
        public GameObject Root;
        public Dictionary<string, IControl> Controls;
        public Dictionary<ElementNode, Control> NodeToControl;
    }

    public sealed class ScreenInstantiator
    {
        private readonly ControlRegistry _registry;
        private readonly VariantStore _variants;

        public ScreenInstantiator(ControlRegistry registry, VariantStore variants)
        {
            _registry = registry;
            _variants = variants;
        }

        public InstantiationResult Instantiate(ScreenDef def)
        {
            return InstantiateInto(new GameObject(def.Name, typeof(RectTransform)), def);
        }

        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def)
        {
            var result = new InstantiationResult
            {
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

        internal List<GameObject> ApplyAddBlock(VariantBlock block, InstantiationResult result)
        {
            var roots = new List<GameObject>();
            foreach (var add in block.Adds)
            {
                var parent = ResolveAddTarget(result.Root, result.Controls, add.IntoPath);
                var parentIsLayoutGroup = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

                // 实例化前：记下当前 child 数；新增 N 个 child 此时都被追加到末尾
                int prevCount = parent.childCount;
                foreach (var child in add.Children)
                    InstantiateRecursive(child, parent, parentIsLayoutGroup,
                                         result.Controls, result.NodeToControl);
                int addedN = parent.childCount - prevCount;

                // 计算目标基准索引（at='end' 时等于 prevCount，保持新增项原位在末尾）
                int targetBase;
                if (add.At == "start") targetBase = 0;
                else if (add.At == "end") targetBase = prevCount;
                else if (int.TryParse(add.At, out var k))
                {
                    if (k < 0) k = 0;
                    if (k > prevCount) k = prevCount;  // OOB clamp
                    targetBase = k;
                }
                else
                {
                    throw new System.InvalidOperationException(
                        $"<Add at='{add.At}'>: must be 'start' / 'end' / integer index " +
                        $"(values out of range are clamped: <0 -> 0, >prevCount -> end)");
                }

                // 把刚加进来的 N 个 child 从末尾移到 targetBase..targetBase+N-1
                if (targetBase != prevCount)
                {
                    for (var i = 0; i < addedN; i++)
                    {
                        var c = parent.GetChild(prevCount + i);  // 它们仍在末尾
                        c.SetSiblingIndex(targetBase + i);
                    }
                }

                for (var i = 0; i < addedN; i++)
                    roots.Add(parent.GetChild(targetBase + i).gameObject);
            }
            return roots;
        }

        private static Transform ResolveAddTarget(GameObject screenRoot,
                                          IReadOnlyDictionary<string, IControl> controls,
                                          string intoPath)
        {
            if (intoPath == "@root") return screenRoot.transform;
            if (intoPath.StartsWith("#"))
            {
                var path = intoPath.Substring(1);
                if (string.IsNullOrEmpty(path))
                    throw new System.InvalidOperationException(
                        $"<Add into='{intoPath}'>: id is empty after '#'");

                // 与 Screen.Get(idPath) 同义：首段查 top-level controls，后续段下钻 ScopedIds
                var segs = path.Split('/');
                if (!controls.TryGetValue(segs[0], out var current))
                    throw new System.InvalidOperationException(
                        $"<Add into='{intoPath}'>: id '{segs[0]}' not found in screen");
                for (var i = 1; i < segs.Length; i++)
                {
                    if (!current.ScopedIds.TryGetValue(segs[i], out var next))
                        throw new System.InvalidOperationException(
                            $"<Add into='{intoPath}'>: '{segs[i]}' not found under " +
                            $"'{string.Join("/", segs, 0, i)}'");
                    current = next;
                }
                return current.GameObject.transform;
            }
            throw new System.InvalidOperationException(
                $"<Add into='{intoPath}'>: must be '@root' or '#id' / '#id/path/...'");
        }

        internal void InstantiateRecursive(ElementNode node, Transform parent,
                                           bool parentIsLayoutGroup,
                                           Dictionary<string, IControl> controls,
                                           Dictionary<ElementNode, Control> nodeMap)
        {
            if (parentIsLayoutGroup)
            {
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

            if (entry.Prefab != null)
            {
                go = Object.Instantiate(entry.Prefab, parent);
                control = (Control)System.Activator.CreateInstance(entry.ControlType);
            }
            else
            {
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
            if (node.IsTemplateInstanceRoot)
            {
                childScope = new Dictionary<string, IControl>();
                control.ReplaceScopedIds(childScope);
            }

            var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, go.transform, selfIsLayoutGroup, childScope, nodeMap);
        }

        private static void BindFields(Control control, GameObject prefabRoot)
        {
            var t = control.GetType();
            foreach (var f in t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var bind = f.GetCustomAttribute<BindAttribute>();
                if (bind == null) continue;

                var childName = bind.ChildName ?? StripUnderscore(f.Name);
                var childTransform = FindChildByName(prefabRoot.transform, childName);
                if (childTransform == null)
                {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' not found");
                    continue;
                }

                var component = childTransform.GetComponent(f.FieldType);
                if (component == null)
                {
                    Debug.LogWarning(
                        $"[Bind] {t.Name}.{f.Name}: child '{childName}' " +
                        $"has no {f.FieldType.Name}");
                    continue;
                }

                f.SetValue(control, component);
            }
        }

        private static string StripUnderscore(string name) =>
            name.StartsWith("_") ? char.ToUpperInvariant(name[1]) + name.Substring(2) : name;

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
            }
            return null;
        }
    }
}
