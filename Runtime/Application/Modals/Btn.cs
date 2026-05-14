using System;

namespace PromptUGUI.Application.Modals
{
    [Flags]
    public enum Btn
    {
        None   = 0,
        OK     = 1,
        Cancel = 2,
        Yes    = 4,
        No     = 8,
        Close  = 16,
    }
}
