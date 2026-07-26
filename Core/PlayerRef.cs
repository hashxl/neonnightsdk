using Asuna.CharManagement;
using UnityEngine;

namespace NeonNightSDK.Core
{
    // The RIGHT way to get the player. There are two traps here; both already cost real
    // debugging time and were documented in loose comments inside TestMod
    // (VendingMachineService and TestMod.OnSceneLoaded) — now they're fixed in code:
    //
    // 1. AMBIGUITY: `Character.Get("Zoey")` is NOT reliable. The game registers more than
    //    one Character with the same display name (e.g. "Char_NPC_Zoey", a cameo clone with
    //    IsPlayer == false), and the name lookup can resolve to that NPC instead of the live
    //    player. The reliable path is always to start from the player's CharacterHandler.
    //
    // 2. UNITY'S FAKE NULL: `CharacterHandler.Player?.Character` (C# null-propagation) does
    //    NOT respect UnityEngine.Object's overloaded == operator, so an ALREADY DESTROYED
    //    object sails through the `?.` as if it were alive and only blows up later, in a
    //    MissingReferenceException far from the cause. Here the check is a real `== null`,
    //    which triggers Unity's overload and detects the destroyed object.
    //
    // Performance bonus: CharacterHandler.Player is a PROPERTY that runs
    // Entity.GetPlayer<PlayerController>() + GetComponent on every access. TestMod called
    // that up to 3x per frame (NeedsService, SleepRobberyService, SleepFatigueService). Here
    // the result is cached per frame, so N calls in the same frame cost one.
    public static class PlayerRef
    {
        private static CharacterHandler _cachedHandler;
        private static Character _cachedCharacter;
        private static int _cachedFrame = -1;

        // The player's Character, or null if it doesn't exist yet (menu, mid-loading, ...).
        public static Character Current
        {
            get
            {
                Resolve();
                return _cachedCharacter;
            }
        }

        // The player's CharacterHandler — the way to reach the Controller (input restraints),
        // the transform (position) and the SkeletonAnimations.
        public static CharacterHandler Handler
        {
            get
            {
                Resolve();
                return _cachedHandler;
            }
        }

        public static bool IsAvailable => Current != null;

        // Invalidates the cache immediately. Only needed if you swapped the player WITHIN the
        // same frame (rare) — the cache already expires on its own every frame.
        public static void Invalidate() => _cachedFrame = -1;

        private static void Resolve()
        {
            if (_cachedFrame == Time.frameCount) return;
            _cachedFrame = Time.frameCount;

            var handler = CharacterHandler.Player;
            // `== null` (not `?.`) on purpose: it triggers UnityEngine.Object's overload and
            // treats a destroyed object as null. See the header comment.
            if (handler == null)
            {
                _cachedHandler = null;
                _cachedCharacter = null;
                return;
            }

            _cachedHandler = handler;

            var character = handler.Character;
            _cachedCharacter = character == null ? null : character;
        }
    }
}
