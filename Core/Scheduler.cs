using System;
using System.Collections.Generic;

namespace NeonNightSDK.Core
{
    // Time-based scheduler. Replaces the hand-rolled Time.deltaTime accumulators scattered
    // across every mod (_decayTimers in NeedsService, _posLogTimer in TestMod, the timer in
    // SleepFatigueService...), each one reimplementing "add deltaTime, compare to the
    // interval, subtract, don't forget to reset".
    //
    //   Scheduler.Every(1f, () => Tick());
    //   Scheduler.After(4.33f, () => Rob(zoey));
    //   Scheduler.When(() => PlayerRef.IsAvailable, () => Setup(PlayerRef.Current));
    //
    // Every callback runs inside its own try/catch: a task that blows up is cancelled and
    // logged, and other mods' tasks keep running.
    //
    // On the game's own Timer (ANToolkit.Utility.Timer.Simple): it exists and works, but it's
    // coroutine-based and bound to a GameObject, with no handle to cancel/pause and no tie to
    // scene changes. This Scheduler adds cancellation, pausing, per-mod scoping (ModContext)
    // and automatic death on scene change — which is what's actually missing in practice.
    public static class Scheduler
    {
        // Ceiling on "catch-up" runs within a single tick when catchUp is enabled. Without it,
        // coming back from a 60s freeze with an Every(0.1f) would fire 600 callbacks in one
        // frame and freeze the game again — a rather unpleasant domino effect.
        private const int MaxCatchUpRuns = 100;

        private static readonly List<ScheduledTask> Tasks = new List<ScheduledTask>();
        private static readonly List<ScheduledTask> Pending = new List<ScheduledTask>();
        private static readonly Predicate<ScheduledTask> DonePredicate = t => t.IsDone;
        private static bool _ticking;

        public static int ActiveCount => Tasks.Count + Pending.Count;

        // Runs once, on the next tick. Useful to defer work until after the scene has
        // finished assembling everything.
        public static ScheduledTask NextFrame(Action action) =>
            Add(new ScheduledTask { Action = action, Interval = 0f, RemainingRuns = 1 });

        // Runs once, `seconds` from now.
        public static ScheduledTask After(float seconds, Action action, bool unscaledTime = false) =>
            Add(new ScheduledTask
            {
                Action = action,
                Interval = Math.Max(0f, seconds),
                RemainingRuns = 1,
                UnscaledTime = unscaledTime
            });

        // Runs forever, every `seconds`. Keep the handle if you might want to cancel it.
        //
        // fireImmediately: fires on the next tick instead of waiting out the first interval.
        // catchUp: if the game stalls/pauses for longer than the interval, run the callback
        //   once PER missed interval instead of just once (the default). Turn it on when the
        //   callback represents accumulation — hunger decay, interest, time passing. Leave it
        //   off when it does something expensive or visible (spawning, playing a sound),
        //   otherwise it turns into a burst.
        public static ScheduledTask Every(float seconds, Action action, bool unscaledTime = false,
            bool fireImmediately = false, bool catchUp = false)
        {
            var interval = Math.Max(0f, seconds);
            return Add(new ScheduledTask
            {
                Action = action,
                Interval = interval,
                RemainingRuns = -1,
                UnscaledTime = unscaledTime,
                CatchUp = catchUp,
                Accumulator = fireImmediately ? interval : 0f
            });
        }

        // Runs `times` times, every `seconds`, then finishes on its own.
        public static ScheduledTask Repeat(float seconds, int times, Action action, bool unscaledTime = false)
        {
            if (times <= 0)
            {
                SdkLog.Warn($"Scheduler.Repeat: times={times} makes no sense, nothing was scheduled.");
                return Add(new ScheduledTask { Action = action, IsDone = true });
            }

            return Add(new ScheduledTask
            {
                Action = action,
                Interval = Math.Max(0f, seconds),
                RemainingRuns = times,
                UnscaledTime = unscaledTime
            });
        }

        // Waits for `condition` to become true, then runs `action` ONCE.
        //
        // This is the clean way to express "only do this once the player exists", with no
        // _spawned flag and no manual per-frame check:
        //   Scheduler.When(() => PlayerRef.IsAvailable, () => ApplyClothing(PlayerRef.Current));
        //
        // timeoutSeconds > 0 gives up (with a log warning) if the condition never becomes
        // true — always set a timeout for something that may legitimately never happen,
        // otherwise the task sits there being evaluated forever.
        public static ScheduledTask When(Func<bool> condition, Action action, float timeoutSeconds = 0f,
            bool unscaledTime = true)
        {
            if (condition == null)
            {
                SdkLog.Error("Scheduler.When: condition is null, nothing was scheduled.");
                return Add(new ScheduledTask { Action = action, IsDone = true });
            }

            return Add(new ScheduledTask
            {
                Action = action,
                Condition = condition,
                TimeoutRemaining = Math.Max(0f, timeoutSeconds),
                UnscaledTime = unscaledTime
            });
        }

        // Cancels everything. Only the SDK shutdown uses this — from inside a mod, cancel your
        // own handles (or use ModContext.Dispose, which cancels only yours).
        public static void CancelAll()
        {
            foreach (var task in Tasks) task.IsDone = true;
            foreach (var task in Pending) task.IsDone = true;

            if (_ticking) return;
            Tasks.Clear();
            Pending.Clear();
        }

        internal static ScheduledTask Add(ScheduledTask task)
        {
            if (task.Action == null)
            {
                SdkLog.Error("Scheduler: something tried to schedule a task with a null action, ignoring.");
                task.IsDone = true;
                return task;
            }

            // Scheduling from INSIDE a callback is normal (an After that schedules the next
            // step). It goes to the pending list so the Tick loop's list isn't mutated
            // mid-iteration.
            if (_ticking) Pending.Add(task);
            else Tasks.Add(task);

            return task;
        }

        internal static void CancelOwnedBy(object owner)
        {
            if (owner == null) return;

            foreach (var task in Tasks)
                if (ReferenceEquals(task.Owner, owner)) task.IsDone = true;

            foreach (var task in Pending)
                if (ReferenceEquals(task.Owner, owner)) task.IsDone = true;
        }

        internal static void OnSceneChanged()
        {
            foreach (var task in Tasks)
                if (task.DiesOnSceneChange) task.IsDone = true;

            foreach (var task in Pending)
                if (task.DiesOnSceneChange) task.IsDone = true;
        }

        internal static void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (Tasks.Count == 0 && Pending.Count == 0) return;

            _ticking = true;
            // Index instead of foreach: a callback may schedule/cancel tasks. The count itself
            // doesn't change here (new ones go to Pending), but iterating by index makes that
            // explicit and immune to InvalidOperationException.
            for (var i = 0; i < Tasks.Count; i++)
            {
                var task = Tasks[i];
                if (task.IsDone || task.IsPaused) continue;

                TickTask(task, task.UnscaledTime ? unscaledDeltaTime : deltaTime);
            }
            _ticking = false;

            if (Pending.Count > 0)
            {
                Tasks.AddRange(Pending);
                Pending.Clear();
            }

            Tasks.RemoveAll(DonePredicate);
        }

        private static void TickTask(ScheduledTask task, float deltaTime)
        {
            if (task.Condition != null)
            {
                TickConditionTask(task, deltaTime);
                return;
            }

            // Zero interval = every frame, no accumulator.
            if (task.Interval <= 0f)
            {
                RunOnce(task);
                return;
            }

            task.Accumulator += deltaTime;
            if (task.Accumulator < task.Interval) return;

            if (!task.CatchUp)
            {
                // Drop the missed intervals but preserve phase (% instead of = 0) so an
                // Every(1f) doesn't drift by a few milliseconds on every stall.
                task.Accumulator %= task.Interval;
                RunOnce(task);
                return;
            }

            var runs = 0;
            while (task.Accumulator >= task.Interval && !task.IsDone && runs < MaxCatchUpRuns)
            {
                task.Accumulator -= task.Interval;
                runs++;
                RunOnce(task);
            }

            if (runs >= MaxCatchUpRuns)
            {
                task.Accumulator = 0f;
                SdkLog.Warn($"Scheduler: '{task.Describe()}' piled up more than {MaxCatchUpRuns} overdue runs " +
                            "(long freeze, or too small an interval). The remainder was dropped to avoid stalling the frame.");
            }
        }

        private static void TickConditionTask(ScheduledTask task, float deltaTime)
        {
            if (task.TimeoutRemaining > 0f)
            {
                task.TimeoutRemaining -= deltaTime;
                if (task.TimeoutRemaining <= 0f)
                {
                    task.IsDone = true;
                    SdkLog.Warn($"Scheduler.When: '{task.Describe()}' timed out before the condition became true — callback not run.");
                    return;
                }
            }

            bool ready;
            try
            {
                ready = task.Condition();
            }
            catch (Exception ex)
            {
                task.IsDone = true;
                SdkLog.Error($"Scheduler.When: the condition of '{task.Describe()}' threw, task cancelled: {ex}");
                return;
            }

            if (!ready) return;

            task.IsDone = true;
            SdkLog.SafeInvoke($"Scheduler.When('{task.Describe()}')", task.Action);
        }

        private static void RunOnce(ScheduledTask task)
        {
            // A task that throws is CANCELLED rather than left running: an Every() breaking
            // every frame would produce thousands of log lines per second and make the log
            // useless for everyone.
            if (!SdkLog.SafeInvoke($"Scheduler('{task.Describe()}')", task.Action))
            {
                task.IsDone = true;
                SdkLog.Error($"Scheduler: '{task.Describe()}' was cancelled because it threw.");
                return;
            }

            if (task.RemainingRuns < 0) return;

            task.RemainingRuns--;
            if (task.RemainingRuns <= 0) task.IsDone = true;
        }
    }
}
