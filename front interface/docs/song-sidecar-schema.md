# Song Sidecar JSON Schema

Sidecar files live next to the audio file with the same base name:

```text
Songs/InGameBGM/Original/example.wav
Songs/InGameBGM/Original/example.json
```

Supported fields:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `title` | string | no | Display title. Falls back to the audio file name. |
| `artist` | string | no | Display artist. Falls back to `Unknown Artist`. |
| `bpm` | number/string | no | Base BPM shown in Song Select and used as metadata. |
| `durationSeconds` | number/string | no | Song duration override, useful for non-WAV files. |
| `previewStart` | number/string | no | Song Select preview start time in seconds. |
| `previewEnd` | number/string | no | Song Select preview end time in seconds. |
| `genre` | string | no | Free-form genre filter/search metadata. |
| `source` | string | no | Source or pack name. |
| `bga` | string | no | Relative or absolute BGA path. Stored for later BGA support. |
| `cover` | string | no | Relative or absolute cover image path. PNG, JPG, BMP, and GIF are handled by WinForms image loading. |

Example:

```json
{
  "title": "Akina Drift",
  "artist": "MuWorld",
  "bpm": 128,
  "durationSeconds": 142.5,
  "previewStart": 32,
  "previewEnd": 47,
  "genre": "Drift",
  "source": "Original",
  "cover": "covers/akina-drift.png",
  "bga": "bga/akina-drift.mp4"
}
```

Song IDs are not plain file names. The runtime combines the normalized file name with a stable hash of the full path so files with the same base name but different paths or extensions do not collide.
