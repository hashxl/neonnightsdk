using System;
using System.Collections.Generic;
using System.IO;
using Modding;
using NeonNightSDK.Core;
using NeonNightSDK.Utility;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace NeonNightSDK.Animations
{
    // One step of a PlayAnimationPipeline. AnimationName can be ANY animation that already
    // exists in the skeleton (the game's own "actions/faint/faint_fall",
    // "general/idle/idle", ...) — it does not have to be one you registered yourself.
    //
    // Loop only really matters on the LAST step (or on the return-to-idle step): Spine's
    // AddAnimation queues each step to start once the PREVIOUS one's own duration has elapsed,
    // even if that previous step has Loop=true — so a looping step in the MIDDLE of a pipeline
    // still plays only one cycle before the next one starts. Only a step with nothing queued
    // after it actually loops forever.
    [Serializable]
    public class AnimationPipelineStep
    {
        public string AnimationName;
        public bool Loop;

        // How long (seconds) this step plays before the next one in the queue (or the return
        // step) takes over. null = Spine's default behaviour: switch when the native clip
        // completes one cycle, via TrackEntry.TrackComplete.
        //
        // Setting this is what actually controls the switch. Under the hood it becomes the
        // "delay" passed to the NEXT step's AddAnimation — confirmed by reflection over
        // spine-csharp.dll (Spine.AnimationState.Update): the switch to the following
        // TrackEntry happens when the current entry's trackLast reaches next.delay, not when
        // the native clip truly ends. So you can cut a clip shorter than it is (0.5s out of a
        // 2s clip) or stretch a Loop=true step beyond a single cycle (5s of
        // "general/idle/idle_drunk" before moving on).
        public float? DurationOverride;

        public AnimationPipelineStep(string animationName, bool loop = false, float? durationOverride = null)
        {
            AnimationName = animationName;
            Loop = loop;
            DurationOverride = durationOverride;
        }
    }

    // JSON schema for one keyframe. `angle` is the FINAL desired rotation in the bone's own
    // local space (what you would read off a reference pose) — NOT the raw value Spine's
    // RotateTimeline stores internally, which is a delta from BoneData.Rotation.
    // RegisterBoneRotationAnimation does that subtraction for you, so nobody has to think
    // about setup-pose-relative math by hand.
    [Serializable]
    public class AnimationKeyframeDto
    {
        public float time;
        public float angle;
    }

    [Serializable]
    public class BoneTrackDto
    {
        public string bone;
        public List<AnimationKeyframeDto> keyframes;
    }

    [Serializable]
    public class AnimationDto
    {
        public string name;
        public List<BoneTrackDto> tracks;
    }

    // Simple animations from code or JSON, with no need for the original .spine project —
    // same premise as ClothingKit: Animation and Timeline are just data, so they can be
    // injected straight into a live SkeletonData at runtime. Signatures confirmed by
    // reflection over this project's spine-csharp.dll before any of this was written:
    //   Animation(string name, ExposedList<Timeline> timelines, float duration)
    //   RotateTimeline(int frameCount, int bezierCount, int boneIndex)
    //   CurveTimeline1.SetFrame(int frame, float time, float value)
    //   CurveTimeline.SetLinear(int frame)
    //
    // Scope: bone rotation only (RotateTimeline). One bone rotating as a whole (hand, forearm,
    // head) already covers waving, raising a hand, shaking the head. It does NOT cover
    // per-frame attachment swapping (flipbook) or per-vertex mesh deformation — that still
    // requires the original .spine project's bone weights.
    public static class AnimationsKit
    {
        // Builds and registers a new Animation from one or more tracks (a bone plus its
        // rotation keyframes). No-ops (returns false) if an Animation with this name already
        // exists — safe to call on every scene load, same as ClothingKit.
        public static bool RegisterBoneRotationAnimation(SkeletonData skeletonData, string animationName, List<BoneTrackDto> tracks)
        {
            if (skeletonData == null || string.IsNullOrEmpty(animationName)) return false;
            if (skeletonData.FindAnimation(animationName) != null) return false;
            if (tracks == null || tracks.Count == 0) return false;

            var timelines = new ExposedList<Timeline>();
            var duration = 0f;

            foreach (var track in tracks)
            {
                var boneData = skeletonData.FindBone(track.bone);
                if (boneData == null)
                {
                    Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimation: bone '{track.bone}' not found, skipping track.");
                    continue;
                }
                if (track.keyframes == null || track.keyframes.Count == 0) continue;

                var timeline = new RotateTimeline(track.keyframes.Count, 0, boneData.Index);
                for (var i = 0; i < track.keyframes.Count; i++)
                {
                    var kf = track.keyframes[i];
                    // RotateTimeline stores a DELTA relative to the bone's setup rotation, not
                    // an absolute angle — hence the subtraction.
                    timeline.SetFrame(i, kf.time, kf.angle - boneData.Rotation);
                    timeline.SetLinear(i);
                    duration = Mathf.Max(duration, kf.time);
                }
                timelines.Add(timeline);
            }

            if (timelines.Count == 0)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimation: no valid tracks, '{animationName}' was not registered.");
                return false;
            }

            var animation = new Animation(animationName, timelines, duration);
            skeletonData.Animations.Add(animation);
            Debug.Log($"[NeonNightSDK.Animations] Registered animation '{animationName}' ({timelines.Count} track(s), duration={duration:0.###}s).");
            return true;
        }

        // Reads a JSON shaped like { "name": "...", "tracks": [ { "bone": "...",
        // "keyframes": [ {"time":0,"angle":0}, ... ] } ] } and registers it via
        // RegisterBoneRotationAnimation. The path is relative to the mod folder, same
        // convention as manifest.SpriteResolver everywhere else in NeonNightSDK.
        public static bool RegisterBoneRotationAnimationFromJson(SkeletonData skeletonData, ModManifest manifest, string relativePath)
        {
            if (manifest == null)
            {
                Debug.LogError("[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: manifest is null.");
                return false;
            }

            var fullPath = Path.Combine(manifest.ModPath, relativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: file not found at '{fullPath}'.");
                return false;
            }

            AnimationDto dto;
            try
            {
                dto = JsonUtility.FromJson<AnimationDto>(File.ReadAllText(fullPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: failed to read '{fullPath}': {ex}");
                return false;
            }

            if (dto == null || string.IsNullOrEmpty(dto.name) || dto.tracks == null)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: invalid JSON in '{fullPath}'.");
                return false;
            }

            return RegisterBoneRotationAnimation(skeletonData, dto.name, dto.tracks);
        }

        // Convenience wrapper: same pattern as ClothingKit — walks the Character's Handlers and
        // registers on each SkeletonData. Call it on every scene load.
        public static void RegisterBoneRotationAnimationForCharacter(Asuna.CharManagement.Character character, string animationName, List<BoneTrackDto> tracks)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                RegisterBoneRotationAnimation(skeletonAnim.Skeleton.Data, animationName, tracks);
            }
        }

        // Plays a sequence of animations that ALREADY EXIST in the game (or ones you registered
        // via RegisterBoneRotationAnimation — same thing, it's all by name), one after another,
        // on one AnimationState track. Animation-mod style (SKSE/FNIS): idle -> [pipeline] ->
        // something new, and at the end either return to where it was or go to a fixed state.
        //
        // What plays AFTER the pipeline (optional, pick one):
        //   - returnToAnimationName: always returns to that specific animation (e.g. always
        //     "general/idle/idle" — useful when the pipeline is a one-off event like falling
        //     down and getting back up).
        //   - returnToPreviousAnimation=true: remembers what was playing BEFORE the pipeline
        //     (via AnimationState.GetCurrent) and returns to it — useful when you don't know in
        //     advance whether the character was idle, walking, etc.
        // With neither, the pipeline simply stops on the last step (frozen if Loop=false,
        // repeating forever if Loop=true) — the "from now on she crawls, full stop" case.
        //
        // Signatures confirmed by reflection over spine-csharp.dll:
        //   AnimationState.SetAnimation(int track, string name, bool loop) -> TrackEntry
        //   AnimationState.AddAnimation(int track, string name, bool loop, float delay) -> TrackEntry
        //   AnimationState.GetCurrent(int track) -> TrackEntry (Animation, Loop)
        //
        // lockMovementFor: if passed, locks the character's movement input (through
        // Core.PlayerControl, which uses the same restraint system the game itself uses for
        // things like automatic pathfinding — confirmed in the log as "CharController: Removing
        // Input Restraint: PMA_PathfindingMoveTo") as soon as the pipeline starts, and unlocks
        // automatically when the LAST entry in the queue (the return step, or the final step if
        // there is no return) actually begins playing — via Spine's TrackEntry.Start, which
        // fires exactly once every earlier entry in the queue has finished. Without this the
        // character keeps walking around on top of the animation (e.g. falling over and still
        // being able to stroll away).
        public static void PlayAnimationPipeline(
            SkeletonAnimation skeletonAnim,
            int trackIndex,
            List<AnimationPipelineStep> steps,
            string returnToAnimationName = null,
            bool returnToPreviousAnimation = false,
            Asuna.CharManagement.Character lockMovementFor = null)
        {
            if (skeletonAnim == null || skeletonAnim.AnimationState == null)
            {
                Debug.LogError("[NeonNightSDK.Animations] PlayAnimationPipeline: skeletonAnim/AnimationState is null.");
                return;
            }
            if (steps == null || steps.Count == 0) return;

            // Spine's AnimationState.SetAnimation/AddAnimation throw ArgumentException for an
            // unknown animation name instead of no-oping — and since that call happens partway
            // through building the queue (and before the lockMovement restraint is added), an
            // uncaught throw here doesn't just skip this pipeline: it propagates out of whatever
            // OnSceneLoaded/OnFrame handler called us, potentially skipping every mod callback
            // still queued after it (clothing re-apply, shop/vending setup, etc.). Validate every
            // name against the skeleton up front so a missing/renamed animation just gets skipped
            // and logged, never crashes the caller.
            var skeletonData = skeletonAnim.Skeleton.Data;
            var validSteps = new List<AnimationPipelineStep>(steps.Count);
            foreach (var step in steps)
            {
                if (skeletonData.FindAnimation(step.AnimationName) != null)
                {
                    validSteps.Add(step);
                }
                else
                {
                    Debug.LogError($"[NeonNightSDK.Animations] PlayAnimationPipeline: animation '{step.AnimationName}' does not exist in this skeleton, skipping step.");
                }
            }

            if (!string.IsNullOrEmpty(returnToAnimationName) && skeletonData.FindAnimation(returnToAnimationName) == null)
            {
                Debug.LogError($"[NeonNightSDK.Animations] PlayAnimationPipeline: returnToAnimationName '{returnToAnimationName}' does not exist in this skeleton, ignoring the return.");
                returnToAnimationName = null;
            }

            if (validSteps.Count == 0 && string.IsNullOrEmpty(returnToAnimationName) && !returnToPreviousAnimation)
            {
                Debug.LogError("[NeonNightSDK.Animations] PlayAnimationPipeline: no valid steps and no return defined, aborting.");
                return;
            }

            var state = skeletonAnim.AnimationState;

            string previousName = null;
            var previousLoop = false;
            if (returnToPreviousAnimation)
            {
                var currentEntry = state.GetCurrent(trackIndex);
                if (currentEntry != null)
                {
                    previousName = currentEntry.Animation.Name;
                    previousLoop = currentEntry.Loop;
                }
            }

            TrackEntry lastEntry;
            var startIndex = 0;
            // Delay for the NEXT AddAnimation — taken from the DurationOverride of the step that
            // was just queued (null = 0f = let Spine work it out from the clip's native duration,
            // the original behaviour, unchanged). See DurationOverride on AnimationPipelineStep.
            var nextDelay = 0f;
            if (validSteps.Count > 0)
            {
                lastEntry = state.SetAnimation(trackIndex, validSteps[0].AnimationName, validSteps[0].Loop);
                nextDelay = validSteps[0].DurationOverride ?? 0f;
                startIndex = 1;
            }
            else if (!string.IsNullOrEmpty(returnToAnimationName))
            {
                lastEntry = state.SetAnimation(trackIndex, returnToAnimationName, true);
                returnToAnimationName = null; // already played as the only entry, don't queue it again below
            }
            else
            {
                lastEntry = state.SetAnimation(trackIndex, previousName, previousLoop);
            }

            for (var i = startIndex; i < validSteps.Count; i++)
            {
                lastEntry = state.AddAnimation(trackIndex, validSteps[i].AnimationName, validSteps[i].Loop, nextDelay);
                nextDelay = validSteps[i].DurationOverride ?? 0f;
            }

            if (!string.IsNullOrEmpty(returnToAnimationName))
            {
                lastEntry = state.AddAnimation(trackIndex, returnToAnimationName, true, nextDelay);
            }
            else if (returnToPreviousAnimation && previousName != null)
            {
                lastEntry = state.AddAnimation(trackIndex, previousName, previousLoop, nextDelay);
            }

            if (lockMovementFor != null)
            {
                var restraintId = $"NeonNightSDK.AnimationPipeline.track{trackIndex}";
                PlayerControl.LockMovement(lockMovementFor, restraintId);
                lastEntry.Start += _ => PlayerControl.UnlockMovement(lockMovementFor, restraintId);
            }

            Debug.Log($"[NeonNightSDK.Animations] PlayAnimationPipeline: {validSteps.Count}/{steps.Count} valid step(s) on track {trackIndex}" +
                      (returnToAnimationName != null ? $", returns to '{returnToAnimationName}'" :
                       returnToPreviousAnimation ? $", returns to previous ('{previousName ?? "none"}')" : ", no return") +
                      (lockMovementFor != null ? ", movement locked until the end" : "") + ".");
        }

        // Convenience wrapper: same pattern as the rest of the kit — walks the Character's
        // Handlers and calls PlayAnimationPipeline on each SkeletonAnimation. lockMovement=true
        // locks the character's input for the whole pipeline (see PlayAnimationPipeline).
        public static void PlayAnimationPipelineForCharacter(
            Asuna.CharManagement.Character character,
            int trackIndex,
            List<AnimationPipelineStep> steps,
            string returnToAnimationName = null,
            bool returnToPreviousAnimation = false,
            bool lockMovement = false)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                PlayAnimationPipeline(skeletonAnim, trackIndex, steps, returnToAnimationName, returnToPreviousAnimation,
                    lockMovement ? character : null);
            }
        }
    }
}
