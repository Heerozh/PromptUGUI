// Editor/LinearTintSolverWindow.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// 反解 <c>UI/LinearLightTint</c> shader 的小工具：给定主色（tint，从工程里的
    /// &lt;Theme&gt; 颜色中选）和想要在屏幕上看到的目标色，算出应该画进 sprite 的像素颜色，
    /// 使得该 sprite 被 tint 后正好得到目标色。
    ///
    /// shader 的正向混合定义在 sRGB 显示空间： result = saturate(tint + 2*sprite - 1)。
    /// 反解即 sprite = (result - tint + 1) / 2。Unity 的 LDR ColorField / TryParseHtmlString
    /// 给到的 Color 分量本就是 sRGB-normalized，正是该混合所处的空间，所以不需要 gamma 换算。
    /// </summary>
    internal sealed class LinearTintSolverWindow : EditorWindow
    {
        private struct ThemeColor
        {
            public string Label;   // "themeName / token"
            public Color Color;
        }

        private List<ThemeColor> _themeColors = new();
        private string[] _labels = Array.Empty<string>();

        [SerializeField] private int _selected;
        [SerializeField] private Color _target = Color.white;

        [MenuItem("Tools/PromptUGUI/Linear Tint Solver")]
        public static void Open()
        {
            var w = GetWindow<LinearTintSolverWindow>("Linear Tint Solver");
            w.minSize = new Vector2(360, 320);
            w.Rescan();
            w.Show();
        }

        private void OnEnable() => Rescan();

        private void Rescan()
        {
            _themeColors = ScanThemeColors();
            _labels = _themeColors.Select(t => t.Label).ToArray();
            if (_selected >= _labels.Length) _selected = 0;
        }

        // 复用 StringExtractor.ScanAllXml 的工程扫描套路：t:TextAsset → .ui.xml →
        // UIDocumentParser → doc.Themes。Edit mode 下即可工作，不需要 Play。
        // 只列每个 <Theme> 自己声明的颜色（不展开 base= 继承）。
        private static List<ThemeColor> ScanThemeColors()
        {
            var result = new List<ThemeColor>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml")) continue;
                if (path.StartsWith("Packages/")) continue;

                UIDocument doc;
                try { doc = UIDocumentParser.Parse(File.ReadAllText(path)); }
                catch (ParseException) { continue; }

                foreach (var theme in doc.Themes)
                {
                    foreach (var ce in theme.Colors)
                    {
                        if (!ColorUtility.TryParseHtmlString(ce.Value, out var c)) continue;
                        result.Add(new ThemeColor
                        {
                            Label = $"{theme.Name} / {ce.Name}",
                            Color = c,
                        });
                    }
                }
            }
            return result;
        }

        // 反解：sprite = (result - tint + 1) / 2，逐通道。raw 为未 clamp 的结果，
        // outOfGamut 标记任一通道落在 [0,1] 之外（即该目标色用此主色无法精确还原）。
        private static Color SolveSprite(Color tint, Color target, out Color clamped, out bool outOfGamut)
        {
            float Solve(float a, float b) => (b - a + 1f) / 2f;
            var raw = new Color(Solve(tint.r, target.r), Solve(tint.g, target.g), Solve(tint.b, target.b), 1f);
            outOfGamut = raw.r < 0f || raw.r > 1f || raw.g < 0f || raw.g > 1f || raw.b < 0f || raw.b > 1f;
            clamped = new Color(Mathf.Clamp01(raw.r), Mathf.Clamp01(raw.g), Mathf.Clamp01(raw.b), 1f);
            return raw;
        }

        // 正向：result = saturate(tint + 2*sprite - 1)，用 clamp 后的 sprite 回算，
        // 让 out-of-gamut 时的实际偏差可见。
        private static Color ForwardTint(Color tint, Color sprite)
        {
            float F(float t, float s) => Mathf.Clamp01(t + 2f * s - 1f);
            return new Color(F(tint.r, sprite.r), F(tint.g, sprite.g), F(tint.b, sprite.b), 1f);
        }

        private static void DrawSwatchRow(string label, Color c)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150));
                var rect = GUILayoutUtility.GetRect(40f, EditorGUIUtility.singleLineHeight,
                    GUILayout.Width(40f));
                EditorGUI.DrawRect(rect, c);
                EditorGUILayout.LabelField(
                    $"#{ColorUtility.ToHtmlStringRGB(c)}    " +
                    $"{Mathf.RoundToInt(c.r * 255f)}, {Mathf.RoundToInt(c.g * 255f)}, " +
                    $"{Mathf.RoundToInt(c.b * 255f)}");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Tint Color (Vertex Color)", GUILayout.Width(150));
                if (_labels.Length == 0)
                    EditorGUILayout.LabelField("(No Theme Colors Found)", EditorStyles.miniLabel);
                else
                    _selected = EditorGUILayout.Popup(_selected, _labels);
                if (GUILayout.Button("↻", GUILayout.Width(26))) Rescan();
            }

            if (_labels.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No <Theme> colors found in the project.\nDefine " +
                    "<Theme name=\"...\"><Color name=\"...\" value=\"#RRGGBB\"/></Theme> " +
                    "in a .ui.xml file, then click ↻ to refresh.",
                    MessageType.Info);
                return;
            }

            var tint = _themeColors[_selected].Color;
            DrawSwatchRow("Tint Color (Vertex Color)", tint);

            EditorGUILayout.Space();
            _target = EditorGUILayout.ColorField("Target Color", _target);

            EditorGUILayout.Space();
            SolveSprite(tint, _target, out var sprite, out var outOfGamut);

            EditorGUILayout.LabelField("Output sprite pixel color", EditorStyles.boldLabel);
            DrawSwatchRow("sprite", sprite);

            var hex = "#" + ColorUtility.ToHtmlStringRGB(sprite);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("", GUILayout.Width(90));
                EditorGUILayout.SelectableLabel(hex,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Copy hex", GUILayout.Width(90)))
                    EditorGUIUtility.systemCopyBuffer = hex;
            }

            EditorGUILayout.LabelField("Use this color to draw sprite, when tinted with the");
            EditorGUILayout.LabelField("above vertex color, it will produce the target color on screen.");

            if (outOfGamut)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Target color is outside the reachable gamut for this tint and was " +
                    "clamped — it cannot be reproduced exactly. The \"Actual result\" " +
                    "swatch below is what the clamped sprite produces after tinting.",
                    MessageType.Warning);
                DrawSwatchRow("Actual result", ForwardTint(tint, sprite));
            }
        }
    }
}
