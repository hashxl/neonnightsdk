using System;

namespace NeonNightSDK.Core
{
    // Handle for a scheduled task. Keep the reference if you'll ever want to cancel it,
    // pause it, or tie its lifetime to something.
    public sealed class ScheduledTask
    {
        internal Action Action;
        internal Func<bool> Condition;      // != null => a When() task: waits on the condition
        internal float Interval;            // <= 0 => runs every frame
        internal float Accumulator;
        internal int RemainingRuns;         // -1 => infinite
        internal bool UnscaledTime;
        internal bool CatchUp;
        internal float TimeoutRemaining;    // <= 0 => no timeout (When only)
        internal bool DiesOnSceneChange;
        internal object Owner;              // owning ModContext, for bulk cancellation
        internal string Name;

        // Becomes true once the task has finished (ran all its repetitions) or was cancelled.
        // A finished task is removed from the Scheduler's list at the end of the tick.
        public bool IsDone { get; internal set; }

        // Pauses the clock without losing accumulated progress — Resume picks up where it
        // left off.
        public bool IsPaused { get; set; }

        public void Cancel() => IsDone = true;

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;

        // Cancels automatically when the scene changes. Use it for anything that only makes
        // sense inside the current scene (an NPC from that map, a contextual HUD) — avoids
        // the classic zombie callback poking at an object that no longer exists.
        public ScheduledTask CancelOnSceneChange()
        {
            DiesOnSceneChange = true;
            return this;
        }

        // Name for logs/diagnostics only, so failures read clearly.
        public ScheduledTask Named(string name)
        {
            Name = name;
            return this;
        }

        internal string Describe() => string.IsNullOrEmpty(Name) ? SdkLog.Describe(Action) : Name;
    }
}
