using System;
using System.Collections.Generic;
using Asuna.CharManagement;
using Asuna.Dialogues;
using Modding;
using UnityEngine;

namespace NeonNightSDK.Core
{
    // Your mod's "context": manifest, tagged logging, sprite resolution and — the main point —
    // event subscriptions and scheduled tasks WHOSE LIFETIME IS TIED TO THE MOD.
    //
    // The problem this solves: today every mod subscribes to SceneManager.sceneLoaded,
    // Item.OnItemUsed, LevelTransition.PostTransition etc. in OnModLoaded and has to remember
    // to unsubscribe from each one in OnModUnLoaded (TestMod keeps that list by hand, and
    // already misses a few). A forgotten handler keeps running after the mod is unloaded,
    // referencing dead objects. With ModContext you register everything through it and call
    // Dispose() once — it all goes away together.
    //
    //   public class MyMod : ITCMod
    //   {
    //       private ModContext _ctx;
    //
    //       public void OnModLoaded(ModManifest manifest)
    //       {
    //           _ctx = ModContext.For(manifest);
    //           _ctx.OnGameplaySceneReady(scene => SpawnNpc(scene))
    //               .WhenPlayerReady(player => ApplyStats(player))
    //               .Every(30f, () => TickHunger());
    //       }
    //
    //       public void OnModUnLoaded() => _ctx.Dispose();
    //       public void OnFrame() { }
    //   }
    public sealed class ModContext : IDisposable
    {
        private static readonly Dictionary<string, ModContext> Contexts = new Dictionary<string, ModContext>();

        private readonly List<Action> _unsubscribers = new List<Action>();
        private bool _disposed;

        public ModManifest Manifest { get; }

        // The manifest's UniqueIdentifier (falling back to Name) — it's the key other mods use
        // in Requires, and the natural prefix for save keys.
        public string Id { get; }

        public string ModPath => Manifest.ModPath;

        private ModContext(ModManifest manifest, string id)
        {
            Manifest = manifest;
            Id = id;
        }

        // Gets (or creates) your mod's context. Calling it twice with the same manifest returns
        // the SAME context, so you can't accidentally leak duplicate subscriptions.
        public static ModContext For(ModManifest manifest)
        {
            if (manifest == null)
            {
                SdkLog.Error("ModContext.For: manifest is null — pass the manifest you received in OnModLoaded.");
                return null;
            }

            // A consuming mod should never need to know the runtime exists.
            SdkRuntime.Install();

            var id = !string.IsNullOrEmpty(manifest.UniqueIdentifier) ? manifest.UniqueIdentifier
                   : !string.IsNullOrEmpty(manifest.Name) ? manifest.Name
                   : "<mod without identifier>";

            if (Contexts.TryGetValue(id, out var existing) && !existing._disposed)
                return existing;

            var context = new ModContext(manifest, id);
            Contexts[id] = context;
            return context;
        }

        // ---- logging tagged with the mod's id -----------------------------------------

        public void Log(string message) => Debug.Log($"[{Id}] {message}");
        public void Warn(string message) => Debug.LogWarning($"[{Id}] {message}");
        public void Error(string message) => Debug.LogError($"[{Id}] {message}");

        // ---- assets -------------------------------------------------------------------

        // Path relative to the mod's folder, same convention as the rest of the SDK.
        public Sprite LoadSprite(string relativePath)
        {
            var resolver = Manifest.SpriteResolver;
            if (resolver == null)
            {
                Error($"LoadSprite('{relativePath}'): the manifest has no SpriteResolver.");
                return null;
            }

            return resolver.Resolve(relativePath);
        }

        public string PathTo(string relativePath) =>
            System.IO.Path.Combine(ModPath ?? string.Empty, relativePath);

        // ---- scoped events (removed on Dispose) ---------------------------------------

        public ModContext OnSceneLoaded(Action<string> handler)
        {
            if (!Accepts(handler, nameof(OnSceneLoaded))) return this;
            SdkEvents.OnSceneLoaded += handler;
            _unsubscribers.Add(() => SdkEvents.OnSceneLoaded -= handler);
            return this;
        }

        public ModContext OnSceneReady(Action<string> handler)
        {
            if (!Accepts(handler, nameof(OnSceneReady))) return this;
            SdkEvents.OnSceneReady += handler;
            _unsubscribers.Add(() => SdkEvents.OnSceneReady -= handler);
            return this;
        }

        public ModContext OnGameplaySceneReady(Action<string> handler)
        {
            if (!Accepts(handler, nameof(OnGameplaySceneReady))) return this;
            SdkEvents.OnGameplaySceneReady += handler;
            _unsubscribers.Add(() => SdkEvents.OnGameplaySceneReady -= handler);
            return this;
        }

        public ModContext OnPlayerReady(Action<Character> handler)
        {
            if (!Accepts(handler, nameof(OnPlayerReady))) return this;
            SdkEvents.OnPlayerReady += handler;
            _unsubscribers.Add(() => SdkEvents.OnPlayerReady -= handler);
            return this;
        }

        public ModContext OnPlayerLost(Action<Character> handler)
        {
            if (!Accepts(handler, nameof(OnPlayerLost))) return this;
            SdkEvents.OnPlayerLost += handler;
            _unsubscribers.Add(() => SdkEvents.OnPlayerLost -= handler);
            return this;
        }

        public ModContext OnUpdate(Action handler)
        {
            if (!Accepts(handler, nameof(OnUpdate))) return this;
            SdkEvents.OnUpdate += handler;
            _unsubscribers.Add(() => SdkEvents.OnUpdate -= handler);
            return this;
        }

        public ModContext OnDialogueStarted(Action<Dialogue> handler)
        {
            if (!Accepts(handler, nameof(OnDialogueStarted))) return this;
            SdkEvents.OnDialogueStarted += handler;
            _unsubscribers.Add(() => SdkEvents.OnDialogueStarted -= handler);
            return this;
        }

        public ModContext OnLineStarted(Action<DialogueLine> handler)
        {
            if (!Accepts(handler, nameof(OnLineStarted))) return this;
            SdkEvents.OnLineStarted += handler;
            _unsubscribers.Add(() => SdkEvents.OnLineStarted -= handler);
            return this;
        }

        // Runs `handler` as soon as the player exists — right away if she's already available,
        // or the first time she becomes available. Unlike OnPlayerReady, this fires ONCE.
        public ModContext WhenPlayerReady(Action<Character> handler)
        {
            if (!Accepts(handler, nameof(WhenPlayerReady))) return this;

            Own(Scheduler
                .When(() => PlayerRef.IsAvailable, () => handler(PlayerRef.Current))
                .Named($"{Id}.WhenPlayerReady"));

            return this;
        }

        // ---- scoped scheduling (cancelled on Dispose) ---------------------------------

        public ScheduledTask After(float seconds, Action action, bool unscaledTime = false) =>
            Own(Scheduler.After(seconds, action, unscaledTime));

        public ScheduledTask Every(float seconds, Action action, bool unscaledTime = false,
            bool fireImmediately = false, bool catchUp = false) =>
            Own(Scheduler.Every(seconds, action, unscaledTime, fireImmediately, catchUp));

        public ScheduledTask Repeat(float seconds, int times, Action action, bool unscaledTime = false) =>
            Own(Scheduler.Repeat(seconds, times, action, unscaledTime));

        public ScheduledTask NextFrame(Action action) => Own(Scheduler.NextFrame(action));

        public ScheduledTask When(Func<bool> condition, Action action, float timeoutSeconds = 0f) =>
            Own(Scheduler.When(condition, action, timeoutSeconds));

        // Removes ALL subscriptions and cancels ALL tasks registered through this context.
        // Call it in OnModUnLoaded — it's the only cleanup line you need.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var unsubscribe in _unsubscribers)
                SdkLog.SafeInvoke($"ModContext('{Id}').Dispose", unsubscribe);

            _unsubscribers.Clear();
            Scheduler.CancelOwnedBy(this);
            Contexts.Remove(Id);

            Log("context released (events unsubscribed, tasks cancelled).");
        }

        private ScheduledTask Own(ScheduledTask task)
        {
            task.Owner = this;
            if (string.IsNullOrEmpty(task.Name)) task.Named($"{Id}.{SdkLog.Describe(task.Action)}");
            return task;
        }

        private bool Accepts(Delegate handler, string what)
        {
            if (_disposed)
            {
                Error($"{what}: this ModContext was already released (Dispose). Registration ignored.");
                return false;
            }

            if (handler == null)
            {
                Error($"{what}: null handler, registration ignored.");
                return false;
            }

            return true;
        }
    }
}
