using System;
using Asuna.CharManagement;
using Asuna.Dialogues;

namespace NeonNightSDK.Core
{
    // The lifecycle hooks ITCMod doesn't give you. Subscribe to what you need and you're
    // done — no more reimplementing SceneManager.sceneLoaded + a _spawned flag + a MainMenu
    // guard in every single service.
    //
    //   SdkEvents.OnSceneReady += scene => { ... };
    //
    // EVERY subscriber is invoked inside its own try/catch (see SdkLog.SafeInvoke): if mod A
    // blows up in OnSceneReady, mods B and C still get the event. Without that, one uncaught
    // exception aborts the entire invocation list and takes down everyone queued behind it.
    //
    // Requires SdkRuntime.Install() (NeonNightSDKMod already calls it in OnModLoaded, and
    // ModContext.For() calls it defensively — in practice you never call it by hand).
    public static class SdkEvents
    {
        // Scene loaded, raw — equivalent to SceneManager.sceneLoaded. The scene's objects
        // ALREADY EXIST, but the game's loading screen is still covering everything. Good for
        // registering/spawning things the player doesn't need to see appear. If you play an
        // animation or open a dialogue here, the player only sees the tail end — use
        // OnSceneReady instead.
        public static event Action<string> OnSceneLoaded;

        // Scene about to become actually VISIBLE (the loading curtain is closing now).
        // This is the right hook for animation, dialogue, notification, cutscene.
        //
        // Backed by ANToolkit.Level.LevelTransition.PostTransition, which the game invokes
        // immediately before the loading screen's "Close" trigger (confirmed by decompiling
        // LevelTransition.LoadSceneCoroutine: after the load it still waits 4x
        // WaitForEndOfFrame plus a 0.25s Timer.Simple, and only THEN calls PostTransition,
        // sets isVisible = false and closes the curtain). That discovery cost real time and
        // was stuck in a comment inside TestMod/SleepRobberyService — now it's API.
        //
        // Fallback: scenes that don't go through LevelTransition (the initial boot into
        // MainMenu, or a SceneManager.LoadScene called directly by another mod) never fire
        // PostTransition. For those, SdkRuntime watches LevelTransition.isVisible and fires
        // as soon as the curtain is down. First one wins, no double dispatch.
        public static event Action<string> OnSceneReady;

        // Same as OnSceneReady, but already skipping MainMenu — that's the
        // `if (scene.name == "MainMenu") return;` repeated in every mod service.
        public static event Action<string> OnGameplaySceneReady;

        // Fires when the player comes into existence (and every time the instance changes).
        // The Character arrives already resolved, free of the Character.Get("Zoey")
        // ambiguity — see PlayerRef for the details.
        public static event Action<Character> OnPlayerReady;

        // Fires when the player stops existing (back to menu, instance swap). The Character
        // passed is the OLD one, and is probably already destroyed — use it only to clear
        // your own references, don't read anything off it.
        public static event Action<Character> OnPlayerLost;

        // One tick per frame, already wrapped in try/catch. Prefer the Scheduler for anything
        // time-based — this one is for reading input and the like.
        public static event Action OnUpdate;

        // A dialogue started. ITCMod already exposes these two hooks (OnDialogueStarted /
        // OnLineStarted), but only to WHOEVER IMPLEMENTS the interface — i.e. the mod's root
        // class. Re-broadcasting them as events lets any service inside your mod listen
        // directly, instead of the main mod class becoming a call forwarder.
        //
        // Great extension point for reacting to base-game dialogue: grant an item when a
        // specific conversation ends, flag a quest, play an animation on a line.
        public static event Action<Dialogue> OnDialogueStarted;

        // A dialogue line started. Fires once per line, so you can match on
        // DialogueLine.LineID to react to an exact moment in a conversation.
        public static event Action<DialogueLine> OnLineStarted;

        private static readonly Dispatcher<Action<string>> SceneLoadedDispatcher = new Dispatcher<Action<string>>("SdkEvents.OnSceneLoaded");
        private static readonly Dispatcher<Action<string>> SceneReadyDispatcher = new Dispatcher<Action<string>>("SdkEvents.OnSceneReady");
        private static readonly Dispatcher<Action<string>> GameplaySceneReadyDispatcher = new Dispatcher<Action<string>>("SdkEvents.OnGameplaySceneReady");
        private static readonly Dispatcher<Action<Character>> PlayerReadyDispatcher = new Dispatcher<Action<Character>>("SdkEvents.OnPlayerReady");
        private static readonly Dispatcher<Action<Character>> PlayerLostDispatcher = new Dispatcher<Action<Character>>("SdkEvents.OnPlayerLost");
        private static readonly Dispatcher<Action> UpdateDispatcher = new Dispatcher<Action>("SdkEvents.OnUpdate");
        private static readonly Dispatcher<Action<Dialogue>> DialogueStartedDispatcher = new Dispatcher<Action<Dialogue>>("SdkEvents.OnDialogueStarted");
        private static readonly Dispatcher<Action<DialogueLine>> LineStartedDispatcher = new Dispatcher<Action<DialogueLine>>("SdkEvents.OnLineStarted");

        internal static void RaiseSceneLoaded(string sceneName) => SceneLoadedDispatcher.Raise(OnSceneLoaded, sceneName);
        internal static void RaiseSceneReady(string sceneName) => SceneReadyDispatcher.Raise(OnSceneReady, sceneName);
        internal static void RaiseGameplaySceneReady(string sceneName) => GameplaySceneReadyDispatcher.Raise(OnGameplaySceneReady, sceneName);
        internal static void RaisePlayerReady(Character player) => PlayerReadyDispatcher.Raise(OnPlayerReady, player);
        internal static void RaisePlayerLost(Character player) => PlayerLostDispatcher.Raise(OnPlayerLost, player);

        internal static void RaiseUpdate() => UpdateDispatcher.Raise(OnUpdate);

        internal static void RaiseDialogueStarted(Dialogue dialogue) => DialogueStartedDispatcher.Raise(OnDialogueStarted, dialogue);
        internal static void RaiseLineStarted(DialogueLine line) => LineStartedDispatcher.Raise(OnLineStarted, line);

        // Resolving the player costs a Entity.GetPlayer + GetComponent (see PlayerRef), so
        // SdkRuntime skips that work entirely when nothing is listening. Keeps the Core at
        // effectively zero per-frame cost for mods that never touch it.
        internal static bool HasPlayerSubscribers => OnPlayerReady != null || OnPlayerLost != null;

        // SDK shutdown only. Don't call this from a mod: it drops every other mod's handlers
        // along with yours.
        internal static void ClearAll()
        {
            OnSceneLoaded = null;
            OnSceneReady = null;
            OnGameplaySceneReady = null;
            OnPlayerReady = null;
            OnPlayerLost = null;
            OnUpdate = null;
            OnDialogueStarted = null;
            OnLineStarted = null;
        }

        // Dispatches each handler in isolation, with a cached invocation list.
        //
        // The cache exists because of OnUpdate: GetInvocationList() allocates a fresh array
        // on every call, and doing that every frame is free GC garbage. Since a multicast
        // delegate is immutable, the snapshot can be reused as long as the reference hasn't
        // changed — any += or -= creates a NEW delegate, so ReferenceEquals catches it
        // immediately.
        private sealed class Dispatcher<TDelegate> where TDelegate : Delegate
        {
            private static readonly Delegate[] Empty = new Delegate[0];

            private readonly string _name;
            private TDelegate _snapshotOf;
            private Delegate[] _snapshot = Empty;

            internal Dispatcher(string name) => _name = name;

            internal void Raise(TDelegate handlers)
            {
                var list = Snapshot(handlers);
                for (var i = 0; i < list.Length; i++)
                {
                    try { ((Action)list[i])(); }
                    catch (Exception ex) { Report(list[i], ex); }
                }
            }

            internal void Raise<TArg>(TDelegate handlers, TArg arg)
            {
                var list = Snapshot(handlers);
                for (var i = 0; i < list.Length; i++)
                {
                    try { ((Action<TArg>)list[i])(arg); }
                    catch (Exception ex) { Report(list[i], ex); }
                }
            }

            private Delegate[] Snapshot(TDelegate handlers)
            {
                if (ReferenceEquals(handlers, _snapshotOf)) return _snapshot;

                _snapshotOf = handlers;
                _snapshot = handlers == null ? Empty : handlers.GetInvocationList();
                return _snapshot;
            }

            private void Report(Delegate handler, Exception ex) =>
                SdkLog.Error($"{_name}: handler '{SdkLog.Describe(handler)}' threw " +
                             $"(the other handlers carried on normally): {ex}");
        }
    }
}
