# Aseprite test fixtures

Place the following `.aseprite` files here to enable the conditional-compiled
Aseprite tests in `SpriteAtlasSyncerTests`:

- `single_animated.aseprite` — 1 frame, Aseprite Import Mode = AnimatedSprite (default).
  Main asset is `t:Sprite`, no Texture2D main asset.
- `single_sheet.aseprite` — 1 frame, Aseprite Import Mode = SpriteSheet.
  Main asset is `t:Texture2D` with one Sprite sub-asset.
- `multi.aseprite` — 3+ frames. Used to verify the SpriteSet single-sprite
  contract logs an error and skips the file.

Tests gated by `Assume.That(File.Exists(...))` are skipped when fixtures are absent.

Requires the `com.unity.2d.aseprite` package (≥ 1.0) to be installed in the
host Unity project. When installed, the `PROMPTUGUI_HAS_ASEPRITE` compile
define activates and the Aseprite-branch code in `SpriteAtlasSyncer.cs`
participates in compilation.
