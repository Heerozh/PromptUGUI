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

        /// <summary>
        /// 单节点子树实例化（用于 ScrollList 这类需要按数据动态实例化模板的控件）。
        /// 节点内的 id 写入新建的局部 scope；该 scope 同时挂在返回的根 IControl 的 ScopedIds，
        /// 让调用方能用 root.Get&lt;T&gt;("childId") 访问子节点。不污染 Screen._byId。
        /// </summary>
        public IControl InstantiateNode(ElementNode node, RectTransform parent, Screen owner)
        {
            _ = owner; // 保留参数：未来可注入 owner-scoped lookups
            var scope = new Dictionary<string, IControl>();
            var nodeMap = new Dictionary<ElementNode, Control>();
            var parentIsLayoutGroup = parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

            int prevChildCount = parent.childCount;
            InstantiateRecursive(node, parent, parentIsLayoutGroup, scope, nodeMap);

            // InstantiateRecursive 把子树根追加到 parent 末尾；取它对应的 Control。
            if (parent.childCount <= prevChildCount) return null;
            var rootGo = parent.GetChild(prevChildCount).gameObject;
            Control rootControl = null;
            foreach (var kv in nodeMap)
                if (kv.Value.GameObject == rootGo) { rootControl = kv.Value; break; }
            if (rootControl == null) return null;

            // 无论节点是否标了 IsTemplateInstanceRoot，对外把整个 scope 接到根；
            // 让 caller (ScrollList BindItems 回调) 能 root.Get<T>("id") 命中子节点。
            // 若根本身的 id 出现在 scope 中（与自己同名场景），不影响——ScopedIds 是 IControl 的查询面。
            rootControl.ReplaceScopedIds(scope);
            return rootControl;
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
                                           Dictionary<ElementNode, Control> nodeMap,
                                           Control parentControl = null)
        {
            if (parentIsLayoutGroup)
            {
                if (node.Attributes.ContainsKey("anchor")
                    || node.VariantOverrides.ContainsKey("anchor"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: 'anchor' is ignored because the parent is a layout group (VStack/HStack/Grid), which positions children automatically. " +
                        $"Fix: remove the 'anchor' attribute and use 'size' / 'width' / 'height' to control this child's size; " +
                        $"or, if you need anchor-based positioning, move this element out of the layout group (e.g. into a <Frame>).");
                if (node.Attributes.ContainsKey("margin")
                    || node.VariantOverrides.ContainsKey("margin"))
                    Debug.LogWarning(
                        $"<{node.Tag} id='{node.Id}'>: 'margin' is ignored because the parent is a layout group (VStack/HStack/Grid), which spaces children automatically. " +
                        $"Fix: remove the 'margin' attribute and use the parent stack's 'padding' / 'spacing' for gaps; " +
                        $"or, if you need margin-based offsets, move this element out of the layout group (e.g. into a <Frame>).");
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
            parentControl?.AddChild(control);

            if (!string.IsNullOrEmpty(node.Id))
            {
                controls[node.Id] = control;
                parentControl?.AddScopedId(node.Id, control);
            }
            nodeMap[node] = control;

            // 子节点的 id 作用域（在递归之前建好，让子节点能把自身 id 注入正确的 scope）
            Dictionary<string, IControl> childScope = controls;
            if (node.IsTemplateInstanceRoot)
            {
                childScope = new Dictionary<string, IControl>();
                control.ReplaceScopedIds(childScope);
            }

            var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid";
            foreach (var c in node.Children)
                InstantiateRecursive(c, control.ChildHostTransform, selfIsLayoutGroup, childScope, nodeMap,
                                     parentControl: control);

            // Apply 放在子树递归之后：OnAfterApply（如 Trigger.SubscribeClick）可以安全访问
            // 已完全实例化的子节点（通过 ScopedIds / GetComponentsInChildren 等）。
            ControlAttributeApplier.Apply(node, control, entry, _variants);
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
