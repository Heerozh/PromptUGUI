using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [CustomPropertyDrawer(typeof(LocalePresetsAttribute))]
    public sealed class LocalePresetsDrawer : PropertyDrawer
    {
        private const float DropdownWidth = 22f;
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                var contentRect = EditorGUI.PrefixLabel(position, label);
                var fieldRect = new Rect(
                    contentRect.x, contentRect.y,
                    contentRect.width - DropdownWidth - Spacing, contentRect.height);
                var dropRect = new Rect(
                    fieldRect.xMax + Spacing, contentRect.y,
                    DropdownWidth, contentRect.height);

                property.stringValue = EditorGUI.TextField(fieldRect, property.stringValue);

                if (EditorGUI.DropdownButton(dropRect, new GUIContent("▾"), FocusType.Keyboard, EditorStyles.miniButton))
                {
                    var menu = new GenericMenu();
                    var prop = property.Copy();
                    foreach (var (code, display) in LocalePresetsAttribute.Defaults)
                    {
                        var captured = code;
                        var on = prop.stringValue == captured;
                        menu.AddItem(new GUIContent(display), on, () =>
                        {
                            prop.stringValue = captured;
                            prop.serializedObject.ApplyModifiedProperties();
                        });
                    }
                    menu.DropDown(dropRect);
                }
            }
        }
    }
}
