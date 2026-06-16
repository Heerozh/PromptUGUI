namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// 多按钮 <c>CenteredSlideBox.Open</c> 的返回值：选中的卡 + 点击的按钮 key。
    /// 取消（×/背景/ESC）→ <c>default</c>（Item=null, Button=null）。
    /// <c>Cancelled</c> 只看 <c>Button == null</c>（单按钮内部确认路径会出现 Item 有值、Button=null）。
    /// </summary>
    public readonly struct SlideSelection<T> where T : class
    {
        public readonly T Item;         // 选中的对象；取消时 null
        public readonly string Button;  // 点的按钮 key；取消时 null

        public SlideSelection(T item, string button) { Item = item; Button = button; }

        public bool Cancelled => Button == null;
        public void Deconstruct(out T item, out string button) { item = Item; button = Button; }
    }
}
