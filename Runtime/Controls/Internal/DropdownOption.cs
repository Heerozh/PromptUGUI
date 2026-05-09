using UnityEngine;

namespace PromptUGUI.Controls
{
    public readonly struct DropdownOption
    {
        public readonly string Text;
        public readonly Sprite Icon;
        public DropdownOption(string text, Sprite icon = null)
        {
            Text = text;
            Icon = icon;
        }
    }
}
