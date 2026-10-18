namespace PromptUGUI.Controls
{
    /// <summary>
    /// One submit of an <c>&lt;InputField&gt;</c> that has a <c>&lt;Suggestions&gt;</c> list (spec
    /// 2026-10-18 §6.4): either a suggestion was chosen, or the typed text was submitted.
    /// </summary>
    public readonly struct SuggestionSubmit
    {
        public SuggestionSubmit(string text, int chosenIndex)
        {
            Text = text;
            ChosenIndex = chosenIndex;
        }

        /// <summary>The field's text at the moment of the submit. Choosing a suggestion does not change it.</summary>
        public string Text { get; }

        /// <summary>The chosen item's index in the last push; −1 when the typed text was submitted.</summary>
        public int ChosenIndex { get; }

        public bool IsChosen => ChosenIndex >= 0;

        public override string ToString() => IsChosen ? $"chose #{ChosenIndex} ('{Text}')" : $"typed '{Text}'";
    }
}
