using System.Collections.Generic;
using System.Reflection;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application
{
    public sealed class InstantiationResult
    {
        public GameObject Root;
        public Dictionary<string, IControl> Controls;
        public Dictionary<ElementNode, Control> NodeToControl;
        // DFS 后序排列的待 Apply 节点，仅在 InstantiateInto(deferApply:true) 时填充。
        public List<ElementNode> ApplyOrder;
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
                if (kv.Value.HostGameObject == rootGo) { rootControl = kv.Value; break; }
            if (rootControl == null) return null;

            // 无论节点是否标了 IsTemplateInstanceRoot，对外把整个 scope 接到根；
            // 让 caller (ScrollList BindItems 回调) 能 root.Get<T>("id") 命中子节点。
            // 若根本身的 id 出现在 scope 中（与自己同名场景），不影响——ScopedIds 是 IControl 的查询面。
            rootControl.ReplaceScopedIds(scope);
            // 动态子树登记到 owner Screen：scale（尤其 Nx / <r>r 依赖 canvasFactor）由
            // Screen.ApplyScales 统一应用，并参与 resize / Variant ReSolve 重算。
            // owner 为 null（裸用 instantiator 的调用方）时跳过，scale 不生效。
            owner?.RegisterDynamicSubtree(rootControl, nodeMap);
            return rootControl;
        }

        /// <param name="deferApply">
        /// true：递归里不就地 Apply 属性，而是把节点按 DFS 后序收进
        /// <see cref="InstantiationResult.ApplyOrder"/>，由调用方在 SetActive(true) 之后统一
        /// Apply —— 这样 ApplyCommon / GetNativeSize 的 TMP 文本测量发生在组件 Awake 之后。
        /// </param>
        public InstantiationResult InstantiateInto(GameObject root, ScreenDef def,
                                                   bool deferApply = false)
        {
            var result = new InstantiationResult
            {
                Root = root,
                Controls = new Dictionary<string, IControl>(),
                NodeToControl = new Dictionary<ElementNode, Control>(),
                ApplyOrder = new List<ElementNode>(),
            };

            foreach (var childNode in def.Root.Children)
                InstantiateRecursive(childNode, result.Root.transform,
                                     parentIsLayoutGroup: false,
                                     result.Controls, result.NodeToControl,
                                     applyOrder: deferApply ? result.ApplyOrder : null);

            return result;
        }

        internal List<GameObject> ApplyAddBlock(VariantBlock block, InstantiationResult result,
                                                List<ElementNode> applyOrder = null)
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
                                         result.Controls, result.NodeToControl,
                                         applyOrder: applyOrder);
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
                                           Control parentControl = null,
                                           List<ElementNode> applyOrder = null)
        {
            if (parentIsLayoutGroup)
            {
                foreach (var issue in LayoutGroupChildRules.CheckChild(node))
                    Debug.LogWarning(issue.Message);
            }
            else
            {
                // 运行时父级已确凿（按组件判断）：非 layout-group 下的 'flow' 是 inert 属性。
                foreach (var issue in LayoutGroupChildRules.CheckNonLayoutChild(node))
                    Debug.LogWarning(issue.Message);
            }

            // Per-tag self-checks (mirror of IRWalker dispatch; runtime warns)
            if (node.Tag == "Frame")
                foreach (var issue in MaskAttributeRules.CheckFrame(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "Image")
            {
                foreach (var issue in MaskAttributeRules.CheckImage(node))
                    Debug.LogWarning(issue.Message);
                // FIT-VARIANT only — FIT-GEOMETRY is CLI-only (inert, zero runtime cost).
                foreach (var issue in ImageFitRules.CheckVariant(node))
                    Debug.LogWarning(issue.Message);
            }
            else if (node.Tag == "Progress")
                foreach (var issue in ProgressAttributeRules.CheckProgress(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "TabBar")
                foreach (var issue in TabRules.CheckTabBar(node))
                    Debug.LogWarning(issue.Message);
            else if (node.Tag == "Carousel")
                foreach (var issue in PromptUGUI.Lint.CarouselRules.CheckCarousel(node))
                    Debug.LogWarning(issue.Message);

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
            // STW-D8: V/HStack 直下声明了 scale 的 <Text> → 插 wrapper + 布局桥，让
            // LayoutGroup 量到 "TMP preferred × s"（半密度渲染 + 整行宽换行 + 行高随内容）。
            // 条件 3 看 base 或任意 variant 覆盖——variant 运行期才激活而 GO 永不重建，
            // 创建期必须备好；scale 未解析时桥 ×1 透传（≡ 裸 TMP）。Grid 不在内
            // （GetComponent<HorizontalOrVerticalLayoutGroup> 对 GridLayoutGroup 返回 null）。
            // flow="false"（且没有 variant 能翻回流内）的 Text 不被 LayoutGroup 量算，
            // wrapper 反而会变成一个没人定位的中间层（自由定位写的是内层 RT）—— 跳过。
            if (control is Text textControl
                && parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>() != null
                && !LayoutGroupChildRules.AlwaysOutOfFlow(node)
                && (node.Attributes.ContainsKey("scale")
                    || node.VariantOverrides.ContainsKey("scale")))
            {
                var wrapperGo = new GameObject(
                    (node.Id ?? node.Tag) + " [scale-host]", typeof(RectTransform));
                var wrapperRt = (RectTransform)wrapperGo.transform;
                wrapperRt.SetParent(parent, worldPositionStays: false);
                // go 此前是 parent 的末位 child；移入 wrapper 后 wrapper 顶上同一末位，
                // 兄弟顺序不变（ApplyAddBlock 的 SetSiblingIndex 流程因此无需感知 wrapper）。
                go.transform.SetParent(wrapperRt, worldPositionStays: false);
                wrapperGo.AddComponent<ScaledTextLayoutBridge>()
                         .Configure(textControl.TmpComponent, control.RectTransform);
                control.LayoutHost = wrapperRt;
            }
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

            var selfIsLayoutGroup = node.Tag is "VStack" or "HStack" or "Grid" or "TabBar" or "Carousel";
            foreach (var c in node.Children)
            {
                if (node.Tag == "Carousel")
                    foreach (var issue in PromptUGUI.Lint.CarouselRules.CheckCard(node, c))
                        Debug.LogWarning(issue.Message);
                InstantiateRecursive(c, control.ChildHostTransform, selfIsLayoutGroup, childScope, nodeMap,
                                     parentControl: control, applyOrder: applyOrder);
            }

            // Apply 放在子树递归之后：OnAfterApply（如 Trigger.SubscribeClick）可以安全访问
            // 已完全实例化的子节点（通过 ScopedIds / GetComponentsInChildren 等）。
            // applyOrder != null：延迟 Apply —— 调用方会先 SetActive(true) 让组件 Awake，
            // 再按这里收集的 DFS 后序统一 Apply（GetNativeSize 的 TMP 测量需要 Awake）。
            if (applyOrder != null)
                applyOrder.Add(node);
            else
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
