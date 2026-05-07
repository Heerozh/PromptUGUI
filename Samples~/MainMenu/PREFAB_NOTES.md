# Manual Prefab Setup

The MainMenu sample needs two prefabs that must be created by hand in the Unity Editor (binary asset files cannot be templated). After importing the sample via Package Manager, follow the steps below.

## PrimaryButton.prefab

Place at: `Assets/Samples/PromptUGUI/<version>/Main Menu Demo/Resources/UI/PrimaryButton.prefab`

Structure:

```
PrimaryButton (GameObject)
  RectTransform: width 240, height 64
  Image (Source Image: any 9-slice sprite, e.g. Unity's UISprite, color #3B82F6)
  Button (Target Graphic: the Image above)
  └── Label (child GameObject)
       RectTransform: anchor stretch-stretch, offsets 0,0,0,0
       TextMeshProUGUI: text "Button", fontSize 24, alignment Center, color white
```

## DangerButton.prefab

Place at: `Assets/Samples/PromptUGUI/<version>/Main Menu Demo/Resources/UI/DangerButton.prefab`

Same structure as PrimaryButton.prefab but with the Image color set to `#DC2626`.

## Wiring up MainMenuRunner

Once the prefabs exist:

1. Create an empty GameObject named `Runner` in the demo scene.
2. Add `MainMenuRunner` (from this sample) as a component.
3. Drag `MainMenu.ui.xml` into the `_xml` field.
4. Drag the two prefabs into `_primaryButtonPrefab` / `_dangerButtonPrefab`.
5. Press Play; the menu appears and clicks log to the Console.
