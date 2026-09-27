# MuWorld Chart Format

MuWorld uses a compact BMS subset for generated and user-edited charts.

## File Locations

- Built-in/generated charts: `NoteLane/*.bms`
- User charts: `%LOCALAPPDATA%/RhythmGame/Charts/*.bms`
- Lane-specific user charts are preferred: `easy_song_4k.bms`, `normal_song_7k.bms`
- Legacy charts without `_4k/_5k/_6k/_7k` are still loaded as fallback.

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

Legacy BMS tokens use engine-default Long/Slide durations. Newly edited charts additionally store the versioned MuWorld extension below to preserve arbitrary durations and exact timestamps.

## Validation Rules

The loader and editor collect warnings and filter unsafe notes.

- invalid token pairs are ignored with a warning
- unsupported channels are ignored
- measure must be `000` to `999`
- channel resolution must be 192 cells or less
- BPM values must be positive
- lane and slide end lane must fit the current 4K/5K/6K/7K mode
- note time and duration must be finite, and duration cannot be negative
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


## Exact user-chart extension v1

The editor retains a legacy BMS projection and appends:

```text
#MUWORLD-NOTES 1
#MWNOTES [{"Time":1.125,"Lane":0,"Type":1,"Duration":1.25,"EndLane":0}]
#MWTEMPO [{"Time":0,"Bpm":120},{"Time":32,"Bpm":150}]
```

Time and Duration are seconds, lanes are zero-based, and Type is 0=Tap, 1=Long, 2=Slide. The exact notes replace the projected channel notes in current MuWorld. The tempo map uses absolute seconds, starts at zero, and has increasing unique timestamps with positive finite BPM. Old extension-v1 files without MWTEMPO derive their map from BMS headers. The editor preserves imported tempo changes; BPM +/- changes the first segment without retiming notes. Lane-specific authored charts are not remapped or converted into other note types on playback.

Unsupported extension versions and unreadable payloads produce an explicit error and an empty playable chart, rather than a substitute pattern. The editor blocks opening unreadable charts. Empty user charts stay empty and cannot start gameplay. Saved charts are validated before replacing the original, and the previous file is retained as `.bak`. Invalid/overlapping notes block saving. Exact payloads are limited to 200000 notes and 32MiB on reading.

Older versions only understand the BMS projection and may change timestamps/durations. Restore the pre-edit `.bak` when rolling back to an old application and exact compatibility is required.
