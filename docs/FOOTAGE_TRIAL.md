# Footage trial — the `anya-*` set

Branch `trial/anya-footage`, off `development` at `08c5d89`. Everything on
`development` is untouched; this branch exists to be judged and then either
merged or deleted.

## What changed

Three files, plus three new clips:

| | |
|---|---|
| `Assets/StreamingAssets/anya-*.mp4` | the new clips, re-encoded (below) |
| `Assets/StreamingAssets/coral-ar.json` | `magnifier_clips` points at them |
| `FullscreenMagnifier.footageWorldWidthStart` | `0.0096` → `0.0068`, in the field default **and** `CoralAR.unity` |

Nothing in the magnifier's logic moved. `VideoMagnifierSource` reads clip names
from config and sizes its RenderTextures from the clips themselves, so new
footage is a config change plus one hand-derived constant.

## Where it came from

`micro-scale/anya-footage/anya-footage/` — one Faviid corallite shot three ways
in the **same framing**:

| source | slot |
|---|---|
| `healthy-favia-loop.mp4` | `alive` |
| `transparent-favia-loop.mp4` | `fluorescent` |
| `bleached-favia-loop.mov` | `dead` |

**The `transparent` filename is a trap.** That clip is vivid green GFP glow — it
is the fluorescent state on look, whatever it is called. The name presumably
refers to the tissue going transparent, but nothing about it reads as
transparency on screen. It feeds the `fluorescent` slot.

### Why this set is shaped better than `*-microscale`

The old three are three *different* corals at three different magnifications, so
a crossfade between them is a dissolve between subjects. These three are one
polyp, same frame, same scale — so the crossfade transforms the polyp **in
place**, tentacle for tentacle. That is a materially stronger reading of the
same cue, and it is the thing worth judging on device.

## The re-encode

Supplied as 10–15 Mbps, ~30 MB each, one of them HEVC in a `.mov`, all three
carrying AAC audio. `VideoMagnifierSource` keeps **all three decoding at once,
forever** — deliberate, so no transition ever stalls on a `Prepare()`. Three
1080p/15 Mbps streams is a very different thermal ask than the three
720×1280/~2 Mbps ones it was tuned against, and a trial that throttles tells you
nothing about the footage.

So they were re-encoded to sit in the same decode budget as the set they replace,
and **nothing else was touched** — native resolution, native framing, native
frame rate all preserved, so what you are judging is the footage:

```sh
ffmpeg -i IN -an \
  -c:v libx264 -profile:v high -pix_fmt yuv420p \
  -crf 21 -maxrate 5M -bufsize 10M \
  -preset slow -movflags +faststart OUT
```

| | current set | supplied | after re-encode |
|---|---|---|---|
| bitrate | 1.3 – 3.3 Mbps | 10.8 – 15.0 Mbps | **2.3 – 4.0 Mbps** |
| total | 14 MB | 90 MB | **19 MB** |
| codec | H.264 High | H.264 ×2, HEVC ×1 | H.264 High ×3 |
| audio | none | AAC ×3 | none |

Audio is stripped rather than muted: `audioOutputMode` is already `None`, so it
was pure payload. The soundscape is Ableton's job.

## The emergence scale — the constant that had to move

`footageWorldWidthStart` is a property of **the clips**, not of the coral, and
`MAGNIFIER.md` §the-loupe warns that re-framing changes it and nothing in code
will notice. It did change:

```
  cup = 4.22 mm object pitch × 1.37 AlignScale = 5.78 mm world

  *-microscale   portrait 720×1280   corallite fills ~0.60 of width → 5.78/0.60 = 0.0096
  anya-*         landscape 1920×1080 corallite fills ~0.85 of width → 5.78/0.85 = 0.0068
```

The anya set is a much tighter macro crop — one corallite nearly edge to edge,
rather than one among several. Left at `0.0096` its polyps emerge about **1.4×
oversized** and the lens stops reading as resting on the surface at 1×.

`0.85` is eyeballed off a gridded frame, exactly as the `0.60` was. **This is the
first number to re-judge on device**, and it is the only tuning value on this
branch. It lives in two places — the field default and the serialized value in
`CoralAR.unity` — and the scene wins at runtime.

## Known consequence, not yet judged: the takeover crop

The old clips are **portrait** (aspect 0.5625), close to a phone screen (~0.462).
These are **landscape** (1.778). `MagnifierFullscreen.shader` cover-fits, so at
full takeover:

```
  visible width = _ScreenAspect / _FootageAspect = 0.462 / 1.778 ≈ 0.26
```

You will see roughly the **middle quarter** of the frame's width. The corallite
spans ~0.85 of that width, so the takeover will hold the mouth and inner
tentacles and lose the outer tentacle ring — which is much of what makes the
healthy and bleached frames read as different.

This was left alone on purpose. Cropping to portrait at encode time cuts the same
tentacles, only blind and irreversibly; better to look at it and then decide. The
loupe beat is unaffected — it samples triplanar across the coral and corrects for
aspect, so it sees the whole frame either way.

If it reads badly, the options in rough order of cost:

1. Re-encode with a portrait centre-crop — cheap, cuts the tentacle ring.
2. Ask for a portrait regrade of the same source, if the original is higher
   resolution than 1080p and can afford the crop.
3. Let the takeover letterbox rather than cover-fit — a shader change, and it
   would put bars on the exhibition's most committed moment.

## A/B-ing it

The `*-microscale` clips are still in `StreamingAssets` beside the new ones, so
in the Editor the comparison is a `coral-ar.json` edit plus the constant:

```jsonc
// anya set
"alive": "anya-healthy.mp4", "fluorescent": "anya-fluorescent.mp4", "dead": "anya-bleached.mp4"
// footageWorldWidthStart = 0.0068

// original set
"alive": "healthy-microscale.mp4", "fluorescent": "fluorescent-microscale.mp4", "dead": "bleached-microscale.mp4"
// footageWorldWidthStart = 0.0096
```

**Swap both together.** The names and the constant are one decision; changing the
clips and leaving the constant is the failure `MAGNIFIER.md` warns about, and it
looks like a footage problem rather than a tuning one.

On device this does not work — `StreamingAssets` is inside the bundle and
read-only — so an iPad A/B means two builds.

If this set is adopted, **delete the losing three clips**: carrying both costs
14 MB of bundle for nothing, and LFS keeps them recoverable from history.

## Reverting

`git switch development`. Nothing on that branch was modified.
