using System;
using System.Collections.Generic;
using Asuna.CharManagement;
using NeonNightSDK.Core;
using NeonNightSDK.Utility;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace NeonNightSDK.Animations
{
    /// <summary>
    /// Describes the two Spine clips that form one paired animation. The clips must already
    /// exist in the live skeletons; this kit coordinates them, it does not manufacture the
    /// artwork contained in a Neon Nights sex-scene prefab.
    /// </summary>
    public sealed class SexAnimationDefinition
    {
        public string FirstAnimation { get; set; }
        public string SecondAnimation { get; set; }
        public float MaxDistance { get; set; } = 2.5f;
        public int TrackIndex { get; set; } = 99;
        public bool Loop { get; set; } = true;
        public bool LockMovement { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public float MixDuration { get; set; } = 0.15f;
        public float Alpha { get; set; } = 1f;

        /// <summary>
        /// Overworld depth is commonly used for draw ordering, so distance is XY-only by
        /// default. Disable this when Z has real spatial meaning in the calling scene.
        /// </summary>
        public bool IgnoreZ { get; set; } = true;

        public SexAnimationDefinition(string firstAnimation, string secondAnimation)
        {
            FirstAnimation = firstAnimation;
            SecondAnimation = secondAnimation;
        }
    }

    /// <summary>A running paired animation. Stop or Dispose it to release both characters.</summary>
    public sealed class SexAnimationSession : IDisposable
    {
        private readonly Character _first;
        private readonly Character _second;
        private readonly SkeletonAnimation _firstSkeleton;
        private readonly SkeletonAnimation _secondSkeleton;
        private readonly int _trackIndex;
        private readonly float _mixDuration;
        private readonly string _restraintId;
        private readonly string _firstAnimation;
        private readonly string _secondAnimation;
        private readonly bool _loop;
        private readonly float _speed;
        private readonly float _alpha;
        private TrackEntry _firstEntry;
        private TrackEntry _secondEntry;
        private bool _firstComplete;
        private bool _secondComplete;

        public bool IsPlaying { get; private set; } = true;

        internal SexAnimationSession(
            Character first,
            Character second,
            SkeletonAnimation firstSkeleton,
            SkeletonAnimation secondSkeleton,
            int trackIndex,
            float mixDuration,
            string restraintId,
            TrackEntry firstEntry,
            TrackEntry secondEntry,
            string firstAnimation,
            string secondAnimation,
            bool loop,
            float speed,
            float alpha,
            bool stopOnComplete)
        {
            _first = first;
            _second = second;
            _firstSkeleton = firstSkeleton;
            _secondSkeleton = secondSkeleton;
            _trackIndex = trackIndex;
            _mixDuration = mixDuration;
            _restraintId = restraintId;
            _firstEntry = firstEntry;
            _secondEntry = secondEntry;
            _firstAnimation = firstAnimation;
            _secondAnimation = secondAnimation;
            _loop = loop;
            _speed = speed;
            _alpha = alpha;

            if (stopOnComplete)
            {
                _firstEntry.Complete += FirstCompleted;
                _secondEntry.Complete += SecondCompleted;
            }
        }

        private void FirstCompleted(TrackEntry entry)
        {
            _firstComplete = true;
            StopWhenBothComplete();
        }

        private void SecondCompleted(TrackEntry entry)
        {
            _secondComplete = true;
            StopWhenBothComplete();
        }

        private void StopWhenBothComplete()
        {
            if (_firstComplete && _secondComplete) Stop();
        }

        /// <summary>
        /// Keeps overworld idle/walk translators from replacing the paired full-body clips.
        /// Call once per frame while the owning feature wants the scene to remain active.
        /// </summary>
        public bool Maintain()
        {
            if (!IsPlaying || _firstSkeleton == null || _secondSkeleton == null ||
                _firstSkeleton.AnimationState == null || _secondSkeleton.AnimationState == null)
                return false;

            _firstEntry = RestoreIfReplaced(_firstSkeleton, _firstEntry, _firstAnimation);
            _secondEntry = RestoreIfReplaced(_secondSkeleton, _secondEntry, _secondAnimation);
            if (_firstEntry == null || _secondEntry == null) return false;

            // Both animations form one shot. Correct meaningful drift without resetting them
            // every frame (which would make the Spine pose appear frozen).
            if (Mathf.Abs(_firstEntry.TrackTime - _secondEntry.TrackTime) > 0.05f)
                _secondEntry.TrackTime = _firstEntry.TrackTime;
            return true;
        }

        private TrackEntry RestoreIfReplaced(
            SkeletonAnimation skeleton, TrackEntry expected, string animationName)
        {
            var current = skeleton.AnimationState.GetCurrent(_trackIndex);
            if (current != null && current.Animation != null &&
                string.Equals(current.Animation.Name, animationName, StringComparison.Ordinal))
                return current;

            var clip = skeleton.Skeleton?.Data?.FindAnimation(animationName);
            if (clip == null) return null;

            var restored = skeleton.AnimationState.SetAnimation(_trackIndex, clip, _loop);
            restored.TimeScale = _speed;
            restored.MixDuration = 0f;
            restored.Alpha = _alpha;
            restored.MixBlend = MixBlend.Replace;
            restored.TrackTime = expected == null ? 0f : expected.TrackTime;
            SdkLog.Info($"SexAnimationKit: restored interrupted animation '{animationName}'.");
            return restored;
        }

        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;

            if (_firstEntry != null) _firstEntry.Complete -= FirstCompleted;
            if (_secondEntry != null) _secondEntry.Complete -= SecondCompleted;

            ClearTrack(_firstSkeleton);
            ClearTrack(_secondSkeleton);

            if (!string.IsNullOrEmpty(_restraintId))
            {
                PlayerControl.UnlockMovement(_first, _restraintId);
                PlayerControl.UnlockMovement(_second, _restraintId);
            }

            _firstEntry = null;
            _secondEntry = null;
            SdkLog.Info("SexAnimationKit: paired animation stopped.");
        }

        private void ClearTrack(SkeletonAnimation skeleton)
        {
            if (skeleton == null || skeleton.AnimationState == null) return;

            if (_mixDuration > 0f)
                skeleton.AnimationState.SetEmptyAnimation(_trackIndex, _mixDuration);
            else
                skeleton.AnimationState.ClearTrack(_trackIndex);
        }

        public void Dispose() => Stop();
    }

    /// <summary>Coordinates one Spine animation on each of two nearby live characters.</summary>
    public static class SexAnimationKit
    {
        /// <summary>
        /// Writes every distinct Spine clip currently available on a character's active rigs.
        /// One clip per line keeps Unity's log from truncating a single giant message.
        /// </summary>
        public static int LogAvailableAnimations(Character character, string label = null)
        {
            if (character == null)
            {
                SdkLog.Error("SexAnimationKit.LogAvailableAnimations: character is null.");
                return 0;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var rigCount = 0;

            foreach (var skeleton in CharacterSkeletons.GetAll(character))
            {
                if (skeleton == null || !skeleton.gameObject.activeInHierarchy ||
                    skeleton.Skeleton?.Data?.Animations == null)
                    continue;

                rigCount++;
                foreach (var animation in skeleton.Skeleton.Data.Animations)
                {
                    if (animation != null && !string.IsNullOrEmpty(animation.Name))
                        names.Add(animation.Name);
                }
            }

            var sorted = new List<string>(names);
            sorted.Sort(StringComparer.Ordinal);
            var displayLabel = string.IsNullOrEmpty(label) ? character.Name : label;

            SdkLog.Info($"SexAnimationKit: animations for '{displayLabel}' " +
                        $"({rigCount} active rig(s), {sorted.Count} distinct clip(s)):");
            foreach (var name in sorted)
                SdkLog.Info($"SexAnimationKit.Animation[{displayLabel}]: {name}");

            return sorted.Count;
        }

        /// <summary>
        /// Starts the pair and returns its session. A null return means nothing was changed.
        /// In particular, characters farther apart than MaxDistance only produce an info log.
        /// </summary>
        public static SexAnimationSession Play(
            Character first,
            Character second,
            SexAnimationDefinition definition)
        {
            SexAnimationSession session;
            return TryPlay(first, second, definition, out session) ? session : null;
        }

        public static bool TryPlay(
            Character first,
            Character second,
            SexAnimationDefinition definition,
            out SexAnimationSession session)
        {
            session = null;

            if (first == null || second == null)
            {
                SdkLog.Error("SexAnimationKit: both characters are required.");
                return false;
            }
            if (ReferenceEquals(first, second))
            {
                SdkLog.Error("SexAnimationKit: first and second must be different characters.");
                return false;
            }
            if (definition == null)
            {
                SdkLog.Error("SexAnimationKit: definition is null.");
                return false;
            }
            if (string.IsNullOrEmpty(definition.FirstAnimation) ||
                string.IsNullOrEmpty(definition.SecondAnimation))
            {
                SdkLog.Error("SexAnimationKit: both animation names are required.");
                return false;
            }
            if (definition.MaxDistance < 0f)
            {
                SdkLog.Error("SexAnimationKit: MaxDistance cannot be negative.");
                return false;
            }
            if (definition.TrackIndex < 0)
            {
                SdkLog.Error("SexAnimationKit: TrackIndex cannot be negative.");
                return false;
            }
            if (definition.Speed <= 0f)
            {
                SdkLog.Error("SexAnimationKit: Speed must be greater than zero.");
                return false;
            }

            CharacterHandler firstHandler;
            CharacterHandler secondHandler;
            SkeletonAnimation firstSkeleton;
            SkeletonAnimation secondSkeleton;
            float distance;

            if (!TryFindClosestPair(first, second, definition.IgnoreZ,
                    out firstHandler, out secondHandler,
                    out firstSkeleton, out secondSkeleton, out distance))
            {
                SdkLog.Error("SexAnimationKit: no active Spine handler pair was found for the characters.");
                return false;
            }

            // This is intentionally the only effect of the too-far path.
            if (distance > definition.MaxDistance)
            {
                SdkLog.Info($"SexAnimationKit: characters are {distance:0.###} units apart " +
                            $"(maximum {definition.MaxDistance:0.###}); animation not started.");
                return false;
            }

            var firstClip = firstSkeleton.Skeleton.Data.FindAnimation(definition.FirstAnimation);
            var secondClip = secondSkeleton.Skeleton.Data.FindAnimation(definition.SecondAnimation);
            if (firstClip == null || secondClip == null)
            {
                if (firstClip == null)
                    SdkLog.Error($"SexAnimationKit: animation '{definition.FirstAnimation}' does not exist on the first character.");
                if (secondClip == null)
                    SdkLog.Error($"SexAnimationKit: animation '{definition.SecondAnimation}' does not exist on the second character.");
                return false;
            }

            TrackEntry firstEntry = null;
            TrackEntry secondEntry = null;
            var restraintId = definition.LockMovement
                ? $"NeonNightSDK.SexAnimationKit.{Guid.NewGuid():N}"
                : null;

            try
            {
                // Both clips were validated before either state is touched. Setting both in
                // this call and resetting TrackTime gives them the same logical time origin.
                // Use the SDK's established animation pipeline instead of writing through the
                // overworld action translator. A dedicated high track is not replaced by the
                // game's idle/walk state machine.
                AnimationsKit.PlayAnimationPipeline(
                    firstSkeleton,
                    definition.TrackIndex,
                    new List<AnimationPipelineStep>
                    {
                        new AnimationPipelineStep(definition.FirstAnimation, definition.Loop)
                    });
                AnimationsKit.PlayAnimationPipeline(
                    secondSkeleton,
                    definition.TrackIndex,
                    new List<AnimationPipelineStep>
                    {
                        new AnimationPipelineStep(definition.SecondAnimation, definition.Loop)
                    });

                firstEntry = firstSkeleton.AnimationState.GetCurrent(definition.TrackIndex);
                secondEntry = secondSkeleton.AnimationState.GetCurrent(definition.TrackIndex);
                if (firstEntry == null || secondEntry == null)
                    throw new InvalidOperationException("AnimationsKit did not create both paired track entries.");

                Configure(firstEntry, definition);
                Configure(secondEntry, definition);
                firstEntry.TrackTime = 0f;
                secondEntry.TrackTime = 0f;

                if (definition.LockMovement)
                {
                    PlayerControl.LockMovement(first, restraintId);
                    PlayerControl.LockMovement(second, restraintId);
                }

                session = new SexAnimationSession(
                    first, second, firstSkeleton, secondSkeleton,
                    definition.TrackIndex, Mathf.Max(0f, definition.MixDuration), restraintId,
                    firstEntry, secondEntry,
                    definition.FirstAnimation, definition.SecondAnimation,
                    definition.Loop, definition.Speed, Mathf.Clamp01(definition.Alpha),
                    stopOnComplete: !definition.Loop);

                SdkLog.Info($"SexAnimationKit: started '{definition.FirstAnimation}' + " +
                            $"'{definition.SecondAnimation}' at distance {distance:0.###}.");
                return true;
            }
            catch (Exception ex)
            {
                if (firstEntry != null) firstSkeleton.AnimationState.ClearTrack(definition.TrackIndex);
                if (secondEntry != null) secondSkeleton.AnimationState.ClearTrack(definition.TrackIndex);
                if (!string.IsNullOrEmpty(restraintId))
                {
                    PlayerControl.UnlockMovement(first, restraintId);
                    PlayerControl.UnlockMovement(second, restraintId);
                }
                SdkLog.Error($"SexAnimationKit: failed to start paired animation: {ex}");
                return false;
            }
        }

        /// <summary>Convenience overload for a pair that uses the same clip name.</summary>
        public static SexAnimationSession Play(
            Character first,
            Character second,
            string animation,
            float maxDistance = 2.5f)
        {
            return Play(first, second,
                new SexAnimationDefinition(animation, animation) { MaxDistance = maxDistance });
        }

        private static void Configure(TrackEntry entry, SexAnimationDefinition definition)
        {
            entry.TimeScale = definition.Speed;
            entry.MixDuration = Mathf.Max(0f, definition.MixDuration);
            entry.Alpha = Mathf.Clamp01(definition.Alpha);
            entry.MixBlend = MixBlend.Replace;
        }

        private static bool TryFindClosestPair(
            Character first,
            Character second,
            bool ignoreZ,
            out CharacterHandler firstHandler,
            out CharacterHandler secondHandler,
            out SkeletonAnimation firstSkeleton,
            out SkeletonAnimation secondSkeleton,
            out float distance)
        {
            firstHandler = null;
            secondHandler = null;
            firstSkeleton = null;
            secondSkeleton = null;
            distance = float.PositiveInfinity;

            if (first.Handlers == null || second.Handlers == null) return false;

            foreach (var candidateFirst in first.Handlers)
            {
                if (candidateFirst == null || !candidateFirst.gameObject.activeInHierarchy) continue;
                var candidateFirstSkeleton = CharacterSkeletons.Get(candidateFirst);
                if (candidateFirstSkeleton == null || candidateFirstSkeleton.AnimationState == null) continue;

                foreach (var candidateSecond in second.Handlers)
                {
                    if (candidateSecond == null || !candidateSecond.gameObject.activeInHierarchy) continue;
                    var candidateSecondSkeleton = CharacterSkeletons.Get(candidateSecond);
                    if (candidateSecondSkeleton == null || candidateSecondSkeleton.AnimationState == null) continue;
                    if (ReferenceEquals(candidateFirstSkeleton, candidateSecondSkeleton)) continue;

                    var delta = candidateFirst.transform.position - candidateSecond.transform.position;
                    if (ignoreZ) delta.z = 0f;
                    var candidateDistance = delta.magnitude;
                    if (candidateDistance >= distance) continue;

                    distance = candidateDistance;
                    firstHandler = candidateFirst;
                    secondHandler = candidateSecond;
                    firstSkeleton = candidateFirstSkeleton;
                    secondSkeleton = candidateSecondSkeleton;
                }
            }

            return firstSkeleton != null && secondSkeleton != null;
        }

    }
}
