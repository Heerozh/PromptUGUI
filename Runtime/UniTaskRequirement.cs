// PromptUGUI on Unity 2022 requires UniTask. This guard turns hundreds of
// "Awaitable not found" errors into one clear message when UniTask is missing.
#if !UNITY_6000_0_OR_NEWER && !PROMPTUGUI_HAS_UNITASK
#error PromptUGUI on Unity 2022 requires UniTask (com.cysharp.unitask). Install it via : https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
#endif
