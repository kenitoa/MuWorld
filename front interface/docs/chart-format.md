# MuWorld Chart Format

MuWorld uses a compact BMS subset for generated and user-edited charts.

## File Locations

- Built-in/generated charts: `NoteLane/*.bms`
- User charts: `%LOCALAPPDATA%/RhythmGame/Charts/*.bms`
- Lane-specific user charts are preferred: `easy_song_4k.bms`, `normal_song_7k.bms`
- Legacy charts without `_4k/_5k/_7k` are still loaded as fallback.

## Headers

```bms
#TITLE Song Title
#ARTIST Artist
#BPM 128
#BPM01 156
```

- `#BPM <value>` sets the base BPM. It must be positive.
- `#BPMxx <value>` defines a tempo token for channel `08`.
- `xx` is a two-digit hexadecimal token.

## Channels

```bms
#00011:01000001
#00012:00020000
#00108:00010000
```

- Header form: `#MMMCC:DATA`
- `MMM`: measure, `000` to `999`
- `CC`: channel
- `08`: tempo events using `#BPMxx` tokens
- `11` to `17`: note lanes 1 to 7
- `DATA`: even-length two-character token cells

## Note Tokens

- `00`: empty
- `01`: Tap
- `02`: Long, fixed default hold duration
- `03`: legacy Slide, moves to the next lane
- `31` to `37`: Slide with encoded end lane 1 to 7

Long and Slide duration are currently engine defaults. The chart editor preserves note type and slide end lane, but not arbitrary per-note duration yet.

## Validation Rules

The loader and editor collect warnings and filter unsafe notes.

- invalid token pairs are ignored with a warning
- unsupported channels are ignored
- measure must be `000` to `999`
- channel resolution must be 192 cells or less
- BPM values must be positive
- lane and slide end lane must fit the current 4K/5K/7K mode
- same-lane notes must respect the minimum tap gap
- Long/Slide notes reserve their occupied lane until their end plus a short gap
- chords cannot contain more notes than the current lane count

## Difficulty Level

The displayed level is calculated from:

- notes per second
- chord ratio
- jack ratio
- long note ratio
- slide note ratio
- hand movement

The result is clamped to `Lv.1` through `Lv.15`.
