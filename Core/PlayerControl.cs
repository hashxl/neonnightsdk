using Asuna.CharManagement;

namespace NeonNightSDK.Core
{
    // Input restraints: the game's own mechanism for "the player can't walk right now".
    //
    // This exact loop (walk the Character's Handlers, grab each Controller, add/remove a
    // named restraint) was already written twice by hand — privately inside AnimationsKit and
    // again in TestMod's SleepRobberyService — and the modal window in HudKit needs it a
    // third time. Centralized here.
    //
    // The restraint is KEYED BY STRING and it stacks: the game itself uses ids like
    // "PMA_PathfindingMoveTo" and "LevelTransition". Always pass an id unique to your feature
    // and always remove the same id, otherwise you either free the player while another
    // system still wants them locked, or leave them frozen forever.
    public static class PlayerControl
    {
        // Locks/unlocks movement for a specific character.
        public static void SetMovementRestraint(Character character, string restraintId, bool locked)
        {
            if (character?.Handlers == null || string.IsNullOrEmpty(restraintId)) return;

            foreach (var handler in character.Handlers)
            {
                var controller = handler == null ? null : handler.Controller;
                if (controller == null) continue;

                if (locked) controller.AddInputRestraint(restraintId);
                else controller.RemoveInputRestraint(restraintId);
            }
        }

        public static void LockMovement(Character character, string restraintId) =>
            SetMovementRestraint(character, restraintId, true);

        public static void UnlockMovement(Character character, string restraintId) =>
            SetMovementRestraint(character, restraintId, false);

        // Same thing for whoever the player currently is. Returns false when there's no
        // player right now (menu, mid-loading) so the caller can decide whether that matters.
        public static bool LockPlayer(string restraintId)
        {
            var player = PlayerRef.Current;
            if (player == null) return false;

            SetMovementRestraint(player, restraintId, true);
            return true;
        }

        public static bool UnlockPlayer(string restraintId)
        {
            var player = PlayerRef.Current;
            if (player == null) return false;

            SetMovementRestraint(player, restraintId, false);
            return true;
        }
    }
}
