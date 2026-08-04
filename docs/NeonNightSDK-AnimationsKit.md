# NeonNightSDK — AnimationsKit

## Overview

Two separate capabilities, both injecting data straight into a live `SkeletonData` at runtime.
The premise is the same one `ClothingKit` relies on: `Animation` and `Timeline` are **just
data**, so they can be built in code without the original `.spine` project.

1. **Create a new animation from scratch** — rotate a bone over time (wave, raise a hand, shake
   the head), defined in code or in a JSON file.
2. **Chain animations that already exist in the game** into a pipeline — play one after
   another, then return to the previous state or to a fixed one. Skyrim animation-mod style
   (SKSE/FNIS): idle → pipeline → something new → back to idle, or stay in the new state.

All code lives in [`Animations/AnimationsKit.cs`](../Animations/AnimationsKit.cs).

## How it works

### Bone rotation is stored as a delta, not an absolute angle

Spine's `RotateTimeline` does not store the angle you see in an editor. It stores a **delta
relative to the bone's setup-pose rotation** (`BoneData.Rotation`).

`RegisterBoneRotationAnimation` takes the **final desired angle** in the bone's own local space
— the value you would read off a reference pose — and performs the subtraction
(`angle - boneData.Rotation`) itself. You never have to think about setup-pose-relative math.

### How a pipeline actually advances

`PlayAnimationPipeline` uses Spine's own queueing, not a timer or coroutine of ours:
`AnimationState.SetAnimation` for the first step, `AnimationState.AddAnimation` for the rest.

The critical detail, confirmed by reflection over `spine-csharp.dll`
(`Spine.AnimationState.Update`): the switch to the next `TrackEntry` happens when the current
entry's elapsed time reaches the `delay` that was passed to the **next** step's
`AddAnimation` — not when the clip "actually ends". The clip's native duration is only used
when `delay <= 0`, in which case Spine computes it via `TrackEntry.TrackComplete`.

That is why `DurationOverride` lives on the step you want to shorten or stretch: internally it
becomes the `delay` of the step that follows it.

### Why `Loop = true` mid-pipeline does not do what it looks like

A step with `Loop = true` that is **not the last one** plays exactly **one cycle** before the
next step begins. `AddAnimation` computes its delay from one cycle of the previous step, even
though that step would loop forever if left alone.

`Loop` only truly matters on the **last** step, or on the return step
(`returnToAnimationName` / `returnToPreviousAnimation`, both always queued with loop enabled).

### An invalid animation name no longer crashes the caller

Spine's `SetAnimation`/`AddAnimation` throw `ArgumentException` for an unknown animation name
rather than no-oping. Because that used to happen partway through building the queue, an
uncaught throw did not merely skip that pipeline: it propagated out of whatever
`OnFrame`/`OnSceneLoaded` handler called it, potentially skipping every mod callback still
queued behind it.

`PlayAnimationPipeline` now validates every name with `skeletonData.FindAnimation(...)`
**before** touching `AnimationState`. An invalid step is dropped and logged; an invalid
`returnToAnimationName` is ignored the same way. It only aborts outright — playing nothing,
locking nothing — when zero valid steps and no return remain.

### Movement locking

By default the character keeps responding to movement input **on top of** the animation. If the
pipeline is a fall, the player can still walk around while the falling and lying animations
play.

`lockMovement` uses the game's own input restraint system through
[`Core.PlayerControl`](NeonNightSDK-Core.md) (`CharController.AddInputRestraint` /
`RemoveInputRestraint` — visible in `Player.log` as
`"CharController: Removing Input Restraint: PMA_PathfindingMoveTo"`, the pathfinding AI's
restraint id).

The restraint is added when the pipeline starts and removed automatically when the **last**
queued entry (the return step, or the final step if there is no return) actually begins
playing — via `Spine.TrackEntry.Start`, which only fires once everything before it in the queue
has finished.

If the pipeline ends on a looping animation with no return (for example "she now crawls from
here on"), movement is released at that moment — which is correct, since that new state
presumably has its own way of responding to input.

## Architecture

| Type | Role |
|---|---|
| `AnimationsKit` | Static entry point; all methods below |
| `BoneTrackDto` | One bone plus its rotation keyframes |
| `AnimationKeyframeDto` | `time` (seconds) and `angle` (final local-space angle) |
| `AnimationDto` | JSON root: `name` plus `tracks` |
| `AnimationPipelineStep` | One step: `AnimationName`, `Loop`, `DurationOverride` |

Underlying `spine-csharp.dll` signatures, all confirmed by reflection before any code was
written:

```
Animation(string name, ExposedList<Timeline> timelines, float duration)
RotateTimeline(int frameCount, int bezierCount, int boneIndex)
CurveTimeline1.SetFrame(int frame, float time, float value)
CurveTimeline.SetLinear(int frame)
AnimationState.SetAnimation(int track, string name, bool loop) -> TrackEntry
AnimationState.AddAnimation(int track, string name, bool loop, float delay) -> TrackEntry
AnimationState.GetCurrent(int track) -> TrackEntry
```

### API reference

| Method | Use |
|---|---|
| `RegisterBoneRotationAnimation(skeletonData, name, tracks)` | Creates a new bone-rotation `Animation` from a list of `BoneTrackDto`. Idempotent. |
| `RegisterBoneRotationAnimationFromJson(skeletonData, manifest, relativePath)` | Same, reading from a `.json` in the mod's folder. |
| `RegisterBoneRotationAnimationForCharacter(character, name, tracks)` | Wrapper: registers on every `SkeletonAnimation` under the character's `Handlers`. |
| `PlayAnimationPipeline(skeletonAnim, track, steps, returnToAnimationName?, returnToPreviousAnimation?, lockMovementFor?)` | Plays a sequence of animations and optionally returns to the previous or a fixed state. |
| `PlayAnimationPipelineForCharacter(character, track, steps, ..., lockMovement?)` | Same, wrapped over `Handlers`. |
| `AnimationPipelineStep(name, loop?, durationOverride?)` | One step. `durationOverride` in seconds. |

## Getting started

### 1. Create an animation

```csharp
using NeonNightSDK.Animations;

var tracks = new List<BoneTrackDto>
{
    new BoneTrackDto
    {
        bone = "Fhand",
        keyframes = new List<AnimationKeyframeDto>
        {
            new AnimationKeyframeDto { time = 0.0f, angle = 0f },
            new AnimationKeyframeDto { time = 0.5f, angle = 90f },
            new AnimationKeyframeDto { time = 1.0f, angle = 0f },
        }
    }
};

AnimationsKit.RegisterBoneRotationAnimation(skeletonData, "modded/raise_hand", tracks);
```

Idempotent: it no-ops (returns `false`) when an `Animation` with that name already exists, so
it is safe to call on every scene load.

### 2. Play it

Ordinary Spine `AnimationState`:

```csharp
skeletonAnim.AnimationState.SetAnimation(0, "modded/raise_hand", false);
```

### 3. Or define it in JSON

```json
{
  "name": "modded/raise_hand",
  "tracks": [
    {
      "bone": "Fhand",
      "keyframes": [
        { "time": 0.0, "angle": 0 },
        { "time": 0.5, "angle": 90 },
        { "time": 1.0, "angle": 0 }
      ]
    }
  ]
}
```

```csharp
AnimationsKit.RegisterBoneRotationAnimationFromJson(
    skeletonData, manifest, "Assets\\Animations\\raise_hand.json");
```

`relativePath` is relative to the mod folder (`manifest.ModPath`). It does not go through
`SpriteResolver` because it is not an image — it is read directly with `File.ReadAllText`.

A single JSON may contain **several tracks** (several bones), which is how you animate a whole
arm (shoulder + elbow + hand) instead of just the wrist.

### 4. Find existing bone and animation names

Bone names: any name used as `bone=` by the **"View parts of this skin"** button in the local
Spine web calibrator.

Animation names: the calibrator does not list animations by default (only skins and slots), but
they can be extracted headless from the same real skeleton, without launching the game —
`skeletonData.animations.forEach(a => ...)` in the `dump.html` next to the calibrator. That is
how `actions/faint/faint_fall` (1.33s), `actions/faint/faint_lying` (3s) and
`general/idle/idle` (3s) were confirmed for this document.

> **Note:** that calibrator is a local tool, not part of this repository, and other modders do
> not have it. Packaging an in-game dump command (`skins`, `bones`, `animations`) into the SDK
> is tracked as a future improvement.

## Examples

### Fall down, lie there, get back to idle

```csharp
using NeonNightSDK.Animations;

AnimationsKit.PlayAnimationPipeline(skeletonAnim, 0, new List<AnimationPipelineStep>
{
    new AnimationPipelineStep("actions/faint/faint_fall"),
    new AnimationPipelineStep("actions/faint/faint_lying"),
}, returnToAnimationName: "general/idle/idle");
```

### What happens at the end of a pipeline

Pick **one** of these, or neither:

| Parameter | Behaviour |
|---|---|
| `returnToAnimationName = "general/idle/idle"` | Always returns to that specific animation, whatever was playing before. Good for one-off events (falling, tripping). |
| `returnToPreviousAnimation = true` | Remembers what was playing **before** the pipeline (via `AnimationState.GetCurrent`) and returns to it. Good when you do not know in advance whether the character was idle, walking, etc. |
| neither | Stops on the last step — frozen with `Loop = false`, repeating forever with `Loop = true`. This is the "from now on she crawls, full stop" case. |

### Custom step duration

```csharp
new AnimationPipelineStep("general/idle/idle_drunk", loop: true, durationOverride: 5f)
```

Cuts a clip shorter than its native length, or stretches a `Loop = true` step across more than
one cycle before moving on.

### Convenience wrapper, with movement locked

```csharp
AnimationsKit.PlayAnimationPipelineForCharacter(
    zoey, 0, steps, returnToAnimationName: "general/idle/idle", lockMovement: true);
```

Same pattern as the rest of the SDK: walks `character.Handlers` and calls
`PlayAnimationPipeline` on each `SkeletonAnimation`.

## Limitations

- **`RotateTimeline` only.** A whole bone rotating about its own pivot. That already covers any
  gesture built from rigid rotation: hand, forearm, head, finger.
- **No translation or scale.** `TranslateTimeline` / `ScaleTimeline` follow the same idea but
  are not implemented yet.
- **No per-frame attachment swapping** (`AttachmentTimeline`, flipbook style).
- **No vertex mesh deformation** (`DeformTimeline`). That requires the vertex weights authored
  in the original `.spine` project and cannot be synthesized at runtime.
- **Linear interpolation only.** Every keyframe is registered with `SetLinear`; no Bezier
  easing.
- **`durationOverride` is ignored on the very last entry.** With nothing queued after it there
  is no next `TrackEntry` to attach the delay to.
- **An invalid animation name is a logged error, not a crash** — but the step simply will not
  play, so check `Debug.LogError` in the console.
- **Registration is per `SkeletonData`.** Each `SkeletonAnimation` a character owns has its own
  data, which is why the `...ForCharacter` wrappers exist.

## Best practices

- Prefix your animation names (`modded/...`) so they cannot collide with the game's own.
- Register on every scene load. The methods are idempotent, so this costs nothing and survives
  skeleton reloads.
- Put `durationOverride` on the step you want to shorten or stretch, not on the one after it.
- Only set `Loop = true` on the final step or the return step.
- Use `lockMovement: true` for any pipeline where the character is not in control of herself
  (falling, being restrained, unconscious), or the player will walk around mid-animation.
- Verify names against the skeleton before shipping — a typo is silent apart from the log line.

## References

- Code: [`Animations/AnimationsKit.cs`](../Animations/AnimationsKit.cs)
- Input restraints: [NeonNightSDK Core](NeonNightSDK-Core.md) (`PlayerControl`)
- Clothing, which shares the "inject data into live SkeletonData" premise:
  `neonnightsdk/Clothing/ClothingKit.cs`
- Real usage: `testmod-master/Needs/SleepRobberyService.cs`, `testmod-master/TestMod.cs`
- Spine types: `Spine.Animation`, `Spine.RotateTimeline`, `Spine.AnimationState`,
  `Spine.TrackEntry` (`spine-csharp.dll`)

## Updates

- **v0.2.0** — Movement locking now goes through `Core.PlayerControl` instead of a private
  duplicate of the restraint loop. Document restructured and translated to English.
- **v0.1.0** — `RegisterBoneRotationAnimation` (+ JSON and per-character variants),
  `PlayAnimationPipeline` with `returnTo` options, `DurationOverride` and up-front name
  validation.
