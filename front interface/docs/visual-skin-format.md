# Visual Skin Format

MuWorld loads optional visual skins from either:

- `Skins/Visual/<skin-name>/`
- `Songs/Skins/Visual/<skin-name>/`

Set `"VisualSkin": "<skin-name>"` in `%LOCALAPPDATA%/RhythmGame/user_settings.json`.
If a file is missing, the built-in renderer is used as fallback.

## Optional Images

Supported image extensions are `.png`, `.jpg`, `.jpeg`, and `.bmp`.

- `note_body.png`: tap note body.
- `long_tail.png`: long note tail/body.
- `slide_arrow.png`: slide direction marker.
- `hit_burst.png`: short effect shown after a note is hit.
- `miss_effect.png`: effect shown when a note is missed.

## Optional `skin.json`

Colors use `#RRGGBB` or `#AARRGGBB`.

```json
{
  "laneColors": ["#ff5050", "#50d250", "#5078ff", "#ffd232", "#ff78d2", "#50dce6", "#d28cff"],
  "lanePressedTint": "#80d2ff",
  "laneHoldTint": "#ffd966",
  "laneSeparator": "#6f8fcb",
  "hitLine": "#ffd45a",
  "hitGlow": "#00aaff",
  "hitGlowBottom": "#ffe65a",
  "keyTop": "#373c46",
  "keyBottom": "#232832",
  "keyPressedTop": "#d0d8e2",
  "keyPressedBottom": "#aab4c3"
}
```

## BGA

Song sidecar JSON can set `"bga": "background.png"`. The image is drawn behind the playfield with a dark overlay. Unsupported video paths are ignored; if no image BGA is present, gameplay uses the built-in audio-position reactive background.


## Validation and lifetime

Skin names must be a single directory name: rooted paths, separators, `.` and `..` are rejected. Invalid names use the default skin. Manifest size is limited to 256KiB; images to 16MiB and 4096 pixels per side. A bad asset falls back independently and logs its failure. Replacing a skin and closing the game dispose the loaded bitmaps.

BGA supports PNG, JPG, JPEG and BMP images up to the same image limits. Unsupported video paths are shown as `IMAGE BGA ONLY` in library status. Missing and failed assets use the built-in background.
