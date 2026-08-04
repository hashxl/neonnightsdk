using System;
using System.Collections.Generic;
using ANToolkit.Controllers;
using Asuna.CharManagement;
using Asuna.Items;
using NeonNightSDK.Core;
using Spine;
using UnityEngine;

namespace NeonNightSDK.Animations
{
    public enum SexPosition
    {
        DoggyStyle,
        Handjob,
        Blowjob,
        Kneeling,
        Standing,
        LapRiding
    }

    public sealed class SexClipPair
    {
        public string ReceiverAnimation { get; }
        public string GiverAnimation { get; }

        public SexClipPair(string receiverAnimation, string giverAnimation)
        {
            ReceiverAnimation = receiverAnimation;
            GiverAnimation = giverAnimation;
        }
    }

    public sealed class OverworldSexPreset
    {
        public SexPosition Position { get; }
        public IReadOnlyList<SexClipPair> ClipPairs { get; }
        public bool ReceiverOnTop { get; set; } = true;

        public OverworldSexPreset(SexPosition position, params SexClipPair[] clipPairs)
        {
            Position = position;
            ClipPairs = clipPairs ?? new SexClipPair[0];
        }
    }

    public sealed class OverworldSexOptions
    {
        public float MaximumStartDistance { get; set; } = 8f;
        public float ApproachSpacing { get; set; } = 0.55f;
        public float ApproachTimeout { get; set; } = 20f;
        public int RigRefreshFrames { get; set; } = 2;
        public bool WalkToMeet { get; set; } = true;
        public bool StripApparel { get; set; } = true;
        public bool RedressOnStop { get; set; } = true;
        public bool DisableColliders { get; set; } = true;
        public bool RestorePositionsOnStop { get; set; } = false;
        public bool Loop { get; set; } = true;
    }

    /// <summary>
    /// Complete two-character overworld scene: approach, strip, rig refresh, alignment,
    /// native track-67 playback, sorting, synchronization and cleanup.
    /// </summary>
    public sealed class OverworldSexSceneSession : IDisposable
    {
        private enum Phase { Created, Approaching, RefreshingRigs, Playing, Stopped }

        private readonly Character _receiver;
        private readonly Character _giver;
        private readonly OverworldSexPreset _preset;
        private readonly OverworldSexOptions _options;
        private readonly string _restraintId;

        private CharacterHandler _receiverHandler;
        private CharacterHandler _giverHandler;
        private CharController _receiverController;
        private CharController _giverController;
        private OverworldAnimationTranslator _receiverTranslator;
        private OverworldAnimationTranslator _giverTranslator;
        private SexClipPair _clips;
        private TrackEntry _receiverEntry;
        private TrackEntry _giverEntry;
        private Phase _phase;
        private float _phaseStartedAt;
        private int _arrived;
        private int _refreshFrames;

        private Vector3 _receiverOriginalPosition;
        private Vector3 _giverOriginalPosition;
        private MoveDirection _receiverOriginalFacing;
        private MoveDirection _giverOriginalFacing;
        private bool _receiverCouldChangeFacing;
        private bool _giverCouldChangeFacing;
        private MoveDirection _sceneFacing;

        private List<Apparel> _receiverApparel;
        private List<Apparel> _giverApparel;
        private Collider2D[] _colliders2D;
        private bool[] _collider2DStates;
        private Collider[] _colliders3D;
        private bool[] _collider3DStates;

        private OrderRendererOnTopOfRenderer _relativeOrder;
        private bool _createdRelativeOrder;
        private Renderer _oldOrderTarget;
        private int _oldOrderOffset;
        private bool _oldOrderEveryFrame;

        public bool IsActive => _phase != Phase.Stopped;
        public bool IsPlaying => _phase == Phase.Playing;
        public SexPosition Position => _preset.Position;
        public Character Receiver => _receiver;
        public Character Giver => _giver;

        internal OverworldSexSceneSession(
            Character receiver,
            Character giver,
            OverworldSexPreset preset,
            OverworldSexOptions options)
        {
            _receiver = receiver;
            _giver = giver;
            _preset = preset;
            _options = options;
            _restraintId = $"NeonNightSDK.OverworldSex.{Guid.NewGuid():N}";
        }

        internal bool Start()
        {
            if (!ResolveHandlers() || !ResolveTranslatorsAndClips()) return false;

            var delta = _giverHandler.transform.position - _receiverHandler.transform.position;
            delta.z = 0f;
            if (_options.MaximumStartDistance >= 0f &&
                delta.magnitude > _options.MaximumStartDistance)
            {
                SdkLog.Info($"OverworldSexSceneKit: pair is {delta.magnitude:0.###} units " +
                            $"apart (maximum {_options.MaximumStartDistance:0.###}).");
                return false;
            }

            CaptureOriginalState();
            _sceneFacing = delta.x > 0f ? MoveDirection.Left : MoveDirection.Right;

            if (_options.WalkToMeet && delta.magnitude > Mathf.Max(0.1f, _options.ApproachSpacing))
            {
                BeginApproach();
            }
            else
            {
                BeginInteraction();
            }

            return true;
        }

        internal void Tick()
        {
            if (_phase == Phase.Stopped) return;

            if (_receiverHandler == null || _giverHandler == null ||
                !_receiverHandler.gameObject.activeInHierarchy ||
                !_giverHandler.gameObject.activeInHierarchy)
            {
                Stop();
                return;
            }

            if (_phase == Phase.Approaching)
            {
                if (_options.ApproachTimeout > 0f &&
                    Time.time - _phaseStartedAt > _options.ApproachTimeout)
                {
                    SdkLog.Error("OverworldSexSceneKit: approach timed out.");
                    Stop();
                }
                return;
            }

            MaintainAlignment();
            if (_phase == Phase.RefreshingRigs && _refreshFrames-- <= 0)
                StartAnimationsAfterRefresh();
        }

        private bool ResolveHandlers()
        {
            _receiverHandler = FindActiveHandler(_receiver);
            _giverHandler = FindActiveHandler(_giver);
            _receiverController = _receiverHandler == null ? null : _receiverHandler.Controller;
            _giverController = _giverHandler == null ? null : _giverHandler.Controller;
            if (_receiverHandler != null && _giverHandler != null &&
                _receiverController != null && _giverController != null)
                return true;

            SdkLog.Error("OverworldSexSceneKit: both characters need active overworld handlers/controllers.");
            return false;
        }

        private bool ResolveTranslatorsAndClips()
        {
            _receiverTranslator = _receiverHandler.GetComponentInChildren<OverworldAnimationTranslator>();
            _giverTranslator = _giverHandler.GetComponentInChildren<OverworldAnimationTranslator>();
            if (_receiverTranslator == null || _giverTranslator == null)
            {
                SdkLog.Error("OverworldSexSceneKit: an overworld animation translator is missing.");
                return false;
            }

            foreach (var candidate in _preset.ClipPairs)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.ReceiverAnimation) ||
                    string.IsNullOrEmpty(candidate.GiverAnimation)) continue;
                // HasAnimation uses the game's silent TryGetAnimation lookup. GetAnimation
                // logs a red "not found" error, which is inappropriate while probing presets.
                if (!_receiverTranslator.HasAnimation(candidate.ReceiverAnimation) ||
                    !_giverTranslator.HasAnimation(candidate.GiverAnimation)) continue;
                _clips = candidate;
                return true;
            }

            SdkLog.Error($"OverworldSexSceneKit: no compatible {_preset.Position} clip pair " +
                         $"exists on '{_receiver.Name}' (receiver) and '{_giver.Name}' (giver).");
            return false;
        }

        private void BeginApproach()
        {
            _phase = Phase.Approaching;
            _phaseStartedAt = Time.time;
            _arrived = 0;

            var receiverPosition = _receiverHandler.transform.position;
            var giverPosition = _giverHandler.transform.position;
            var midpoint = (receiverPosition + giverPosition) * 0.5f;
            var horizontal = giverPosition.x >= receiverPosition.x ? Vector3.right : Vector3.left;
            var halfGap = Mathf.Max(0.1f, _options.ApproachSpacing) * 0.5f;
            var receiverDestination = midpoint - horizontal * halfGap;
            var giverDestination = midpoint + horizontal * halfGap;
            receiverDestination.z = receiverPosition.z;
            giverDestination.z = giverPosition.z;

            _receiverController.MoveTo(ParticipantArrived, receiverDestination);
            _giverController.MoveTo(ParticipantArrived, giverDestination);
            SdkLog.Info($"OverworldSexSceneKit: {_receiver.Name} and {_giver.Name} are approaching.");
        }

        private void ParticipantArrived()
        {
            if (_phase != Phase.Approaching) return;
            _arrived++;
            if (_arrived >= 2) BeginInteraction();
        }

        private void BeginInteraction()
        {
            _receiverController.SkipMoveTo();
            _giverController.SkipMoveTo();
            _receiverController.FacingDirection = _sceneFacing;
            _giverController.FacingDirection = _sceneFacing;
            _receiverController.CanChangeFacingDirection = false;
            _giverController.CanChangeFacingDirection = false;
            _receiverController.AddInputRestraint(_restraintId);
            _giverController.AddInputRestraint(_restraintId);

            if (_options.DisableColliders) DisableColliders();
            AlignToReceiver();

            if (_options.StripApparel)
            {
                _receiverApparel = new List<Apparel>(_receiver.EquippedItems.GetAll<Apparel>());
                _giverApparel = new List<Apparel>(_giver.EquippedItems.GetAll<Apparel>());
                _receiver.StripEquipment<Apparel>(cacheItem: true);
                _giver.StripEquipment<Apparel>(cacheItem: true);
                _receiverTranslator = null;
                _giverTranslator = null;
                _refreshFrames = Math.Max(0, _options.RigRefreshFrames);
                _phase = Phase.RefreshingRigs;
            }
            else
            {
                StartAnimationsAfterRefresh();
            }
        }

        private void StartAnimationsAfterRefresh()
        {
            if (!ResolveTranslatorsAndClips()) { Stop(); return; }
            if (!SetupRelativeRenderOrder()) { Stop(); return; }

            AlignToReceiver();
            _giverEntry = _giverTranslator.PlayAnimation(_clips.GiverAnimation);
            _receiverEntry = _receiverTranslator.PlayAnimation(_clips.ReceiverAnimation);
            if (_giverEntry == null || _receiverEntry == null) { Stop(); return; }

            _giverEntry.Loop = _options.Loop;
            _receiverEntry.Loop = _options.Loop;
            _giverEntry.TrackTime = 0f;
            _receiverEntry.TrackTime = 0f;
            _phase = Phase.Playing;
            SdkLog.Info($"OverworldSexSceneKit: started {_preset.Position}: " +
                        $"'{_clips.ReceiverAnimation}' + '{_clips.GiverAnimation}'.");
        }

        private void MaintainAlignment()
        {
            AlignToReceiver();
            _receiverController.FacingDirection = _sceneFacing;
            _giverController.FacingDirection = _sceneFacing;
            if (_phase == Phase.Playing && _receiverEntry != null && _giverEntry != null &&
                Mathf.Abs(_receiverEntry.TrackTime - _giverEntry.TrackTime) > 0.05f)
                _giverEntry.TrackTime = _receiverEntry.TrackTime;
        }

        private void AlignToReceiver() =>
            _giverHandler.transform.position = _receiverHandler.transform.position;

        private void CaptureOriginalState()
        {
            _receiverOriginalPosition = _receiverHandler.transform.position;
            _giverOriginalPosition = _giverHandler.transform.position;
            _receiverOriginalFacing = _receiverController.FacingDirection;
            _giverOriginalFacing = _giverController.FacingDirection;
            _receiverCouldChangeFacing = _receiverController.CanChangeFacingDirection;
            _giverCouldChangeFacing = _giverController.CanChangeFacingDirection;
        }

        private bool SetupRelativeRenderOrder()
        {
            var frontTranslator = _preset.ReceiverOnTop ? _receiverTranslator : _giverTranslator;
            var backTranslator = _preset.ReceiverOnTop ? _giverTranslator : _receiverTranslator;
            var frontRenderer = frontTranslator.GetComponent<Renderer>();
            var backRenderer = backTranslator.GetComponent<Renderer>();
            if (frontRenderer == null || backRenderer == null)
            {
                SdkLog.Error("OverworldSexSceneKit: paired Spine renderers are missing.");
                return false;
            }

            _relativeOrder = frontRenderer.GetComponent<OrderRendererOnTopOfRenderer>();
            _createdRelativeOrder = _relativeOrder == null;
            if (_createdRelativeOrder)
                _relativeOrder = frontRenderer.gameObject.AddComponent<OrderRendererOnTopOfRenderer>();
            else
            {
                _oldOrderTarget = _relativeOrder.Target;
                _oldOrderOffset = _relativeOrder.Offset;
                _oldOrderEveryFrame = _relativeOrder.EveryFrame;
            }

            _relativeOrder.Target = backRenderer;
            _relativeOrder.Offset = 1;
            _relativeOrder.EveryFrame = true;
            _relativeOrder.enabled = true;
            return true;
        }

        private void DisableColliders()
        {
            var twoD = new List<Collider2D>();
            twoD.AddRange(_receiverHandler.GetComponentsInChildren<Collider2D>(true));
            twoD.AddRange(_giverHandler.GetComponentsInChildren<Collider2D>(true));
            _colliders2D = twoD.ToArray();
            _collider2DStates = new bool[_colliders2D.Length];
            for (var i = 0; i < _colliders2D.Length; i++)
            {
                if (_colliders2D[i] == null) continue;
                _collider2DStates[i] = _colliders2D[i].enabled;
                _colliders2D[i].enabled = false;
            }

            var threeD = new List<Collider>();
            threeD.AddRange(_receiverHandler.GetComponentsInChildren<Collider>(true));
            threeD.AddRange(_giverHandler.GetComponentsInChildren<Collider>(true));
            _colliders3D = threeD.ToArray();
            _collider3DStates = new bool[_colliders3D.Length];
            for (var i = 0; i < _colliders3D.Length; i++)
            {
                if (_colliders3D[i] == null) continue;
                _collider3DStates[i] = _colliders3D[i].enabled;
                _colliders3D[i].enabled = false;
            }
        }

        public void Stop()
        {
            if (_phase == Phase.Stopped) return;
            _phase = Phase.Stopped;

            _receiverController?.SkipMoveTo();
            _giverController?.SkipMoveTo();
            _receiverTranslator?.StopAnimation(_receiverEntry);
            _giverTranslator?.StopAnimation(_giverEntry);
            RestoreRelativeOrder();
            RestoreColliders();

            RestoreController(_receiverController, _receiverOriginalFacing, _receiverCouldChangeFacing);
            RestoreController(_giverController, _giverOriginalFacing, _giverCouldChangeFacing);

            if (_options.RestorePositionsOnStop)
            {
                if (_receiverHandler != null) _receiverHandler.transform.position = _receiverOriginalPosition;
                if (_giverHandler != null) _giverHandler.transform.position = _giverOriginalPosition;
            }

            if (_options.RedressOnStop)
            {
                RestoreApparel(_receiver, _receiverApparel);
                RestoreApparel(_giver, _giverApparel);
            }

            OverworldSexSceneKit.Release(this);
            SdkLog.Info($"OverworldSexSceneKit: stopped {_preset.Position}.");
        }

        private void RestoreController(CharController controller, MoveDirection facing, bool canFace)
        {
            if (controller == null) return;
            controller.RemoveInputRestraint(_restraintId);
            controller.CanChangeFacingDirection = canFace;
            controller.FacingDirection = facing;
        }

        private void RestoreRelativeOrder()
        {
            if (_relativeOrder == null) return;
            if (_createdRelativeOrder) UnityEngine.Object.Destroy(_relativeOrder);
            else
            {
                _relativeOrder.Target = _oldOrderTarget;
                _relativeOrder.Offset = _oldOrderOffset;
                _relativeOrder.EveryFrame = _oldOrderEveryFrame;
            }
        }

        private void RestoreColliders()
        {
            if (_colliders2D != null && _collider2DStates != null)
                for (var i = 0; i < _colliders2D.Length && i < _collider2DStates.Length; i++)
                    if (_colliders2D[i] != null) _colliders2D[i].enabled = _collider2DStates[i];
            if (_colliders3D != null && _collider3DStates != null)
                for (var i = 0; i < _colliders3D.Length && i < _collider3DStates.Length; i++)
                    if (_colliders3D[i] != null) _colliders3D[i].enabled = _collider3DStates[i];
        }

        private static void RestoreApparel(Character character, List<Apparel> apparel)
        {
            if (character == null || apparel == null) return;
            foreach (var item in apparel)
                if (item != null && !item.IsEquipped) character.EquipItem(item);
        }

        private static CharacterHandler FindActiveHandler(Character character)
        {
            if (character?.Handlers == null) return null;
            foreach (var handler in character.Handlers)
                if (handler != null && handler.gameObject.activeInHierarchy) return handler;
            return null;
        }

        public void Dispose() => Stop();
    }

    public static class OverworldSexSceneKit
    {
        private static readonly List<OverworldSexSceneSession> Sessions =
            new List<OverworldSexSceneSession>();
        private static readonly Dictionary<SexPosition, OverworldSexPreset> Presets =
            new Dictionary<SexPosition, OverworldSexPreset>();
        private static bool _installed;

        static OverworldSexSceneKit()
        {
            RegisterDefaults();
        }

        public static OverworldSexSceneSession Play(
            Character receiver,
            Character giver,
            SexPosition position,
            OverworldSexOptions options = null)
        {
            if (receiver == null || giver == null || ReferenceEquals(receiver, giver))
            {
                SdkLog.Error("OverworldSexSceneKit.Play: two different characters are required.");
                return null;
            }
            if (!Presets.TryGetValue(position, out var preset))
            {
                SdkLog.Error($"OverworldSexSceneKit.Play: no preset registered for {position}.");
                return null;
            }

            foreach (var active in Sessions)
            {
                if (!active.IsActive) continue;
                if (ReferenceEquals(active.Receiver, receiver) || ReferenceEquals(active.Giver, receiver) ||
                    ReferenceEquals(active.Receiver, giver) || ReferenceEquals(active.Giver, giver))
                {
                    SdkLog.Error("OverworldSexSceneKit.Play: one of the characters is already " +
                                 "participating in another active session.");
                    return null;
                }
            }

            EnsureInstalled();
            var session = new OverworldSexSceneSession(
                receiver, giver, preset, options ?? new OverworldSexOptions());
            if (!session.Start()) return null;
            Sessions.Add(session);
            return session;
        }

        public static OverworldSexSceneSession DoggyStyle(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.DoggyStyle, options);

        public static OverworldSexSceneSession Handjob(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.Handjob, options);

        public static OverworldSexSceneSession Blowjob(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.Blowjob, options);

        public static OverworldSexSceneSession Kneeling(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.Kneeling, options);

        public static OverworldSexSceneSession Standing(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.Standing, options);

        public static OverworldSexSceneSession LapRiding(
            Character receiver, Character giver, OverworldSexOptions options = null) =>
            Play(receiver, giver, SexPosition.LapRiding, options);

        public static void RegisterPreset(OverworldSexPreset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            Presets[preset.Position] = preset;
        }

        public static OverworldSexPreset GetPreset(SexPosition position) =>
            Presets.TryGetValue(position, out var preset) ? preset : null;

        public static bool CanPlay(Character receiver, Character giver, SexPosition position)
        {
            if (receiver == null || giver == null || ReferenceEquals(receiver, giver) ||
                !Presets.TryGetValue(position, out var preset)) return false;

            var receiverTranslator = FindActiveTranslator(receiver);
            var giverTranslator = FindActiveTranslator(giver);
            if (receiverTranslator == null || giverTranslator == null) return false;

            foreach (var pair in preset.ClipPairs)
            {
                if (pair == null) continue;
                if (receiverTranslator.HasAnimation(pair.ReceiverAnimation) &&
                    giverTranslator.HasAnimation(pair.GiverAnimation))
                    return true;
            }
            return false;
        }

        public static void StopAll()
        {
            var snapshot = Sessions.ToArray();
            foreach (var session in snapshot) session.Stop();
        }

        internal static void Release(OverworldSexSceneSession session) => Sessions.Remove(session);

        private static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            SdkEvents.OnUpdate += Tick;
            SdkEvents.OnSceneLoaded += _ => StopAll();
        }

        private static void Tick()
        {
            var snapshot = Sessions.ToArray();
            foreach (var session in snapshot) session.Tick();
        }

        private static OverworldAnimationTranslator FindActiveTranslator(Character character)
        {
            if (character?.Handlers == null) return null;
            foreach (var handler in character.Handlers)
                if (handler != null && handler.gameObject.activeInHierarchy)
                    return handler.GetComponentInChildren<OverworldAnimationTranslator>();
            return null;
        }

        private static SexClipPair Pair(string receiver, string giver) =>
            new SexClipPair("actions/lewd/sex/" + receiver, "actions/lewd/sex/" + giver);

        private static void RegisterDefaults()
        {
            RegisterPreset(new OverworldSexPreset(SexPosition.DoggyStyle,
                Pair("doggystyle_standing_receive_a", "doggystyle_standing_give"),
                Pair("doggystyle_standing_receive_b", "doggystyle_standing_give")));

            RegisterPreset(new OverworldSexPreset(SexPosition.Handjob,
                Pair("handjob_sitting_receive", "handjob_sitting_give"),
                Pair("sitting_kneeling_idle", "handjob_sitting_give")));

            RegisterPreset(new OverworldSexPreset(SexPosition.Blowjob,
                Pair("blowjob_sitting_receive", "blowjob_sitting_give"),
                Pair("sitting_kneeling_idle", "blowjob_sitting_give")));

            RegisterPreset(new OverworldSexPreset(SexPosition.Kneeling,
                // Customer has no kneeling "give" clip. Its standing blowjob-receive
                // pose is the closest native counterpart for Zoey's kneeling scene.
                Pair("kneeling_sex_3", "blowjob_standing_receive"),
                Pair("kneeling_sex_1", "kneeling_sex_1"),
                Pair("kneeling_sex_2", "kneeling_sex_2"),
                Pair("kneeling_sex_3", "kneeling_sex_3")));

            RegisterPreset(new OverworldSexPreset(SexPosition.Standing,
                Pair("hump_receive_standing", "hump_give_standing"),
                Pair("doggystyle_standing_receive_a", "doggystyle_standing_give")));

            RegisterPreset(new OverworldSexPreset(SexPosition.LapRiding,
                // Customer has no lap-riding "give" clip. Reuse its seated receiving
                // body pose so both native rigs remain aligned at the shared origin.
                Pair("lap_riding_receive", "blowjob_sitting_receive"),
                Pair("lap_riding_receive2", "blowjob_sitting_receive"),
                Pair("lap_riding_receive", "lap_riding_give"),
                Pair("lap_riding_receive2", "lap_riding_give")));
        }
    }
}
