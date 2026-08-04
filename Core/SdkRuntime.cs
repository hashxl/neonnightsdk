using ANToolkit.Level;
using Asuna.CharManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonNightSDK.Core
{
    // The Core's engine: makes sure an Update() is running, translates the game's events into
    // SdkEvents, and ticks the Scheduler.
    //
    // You normally call nothing in here — NeonNightSDKMod installs everything in OnModLoaded,
    // and ModContext.For() reinstalls defensively.
    public static class SdkRuntime
    {
        public const string Version = "0.4.0";

        private static bool _installed;
        private static bool _shuttingDown;
        private static int _lastTickedFrame = -1;

        private static FramePump _pump;
        private static Character _lastPlayer;
        private static bool _sceneReadyPending;
        private static ScheduledTask _sceneReadyFallback;

        public static bool IsInstalled => _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            _shuttingDown = false;

            SceneManager.sceneLoaded += OnUnitySceneLoaded;
            LevelTransition.PostTransition.AddListener(OnPostTransition);

            DebugKit.Install();
            EnsurePump();
            SdkLog.Info($"Core v{Version} installed (events + scheduler active).");
        }

        internal static void Shutdown()
        {
            if (!_installed) return;
            _installed = false;
            _shuttingDown = true;

            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            LevelTransition.PostTransition.RemoveListener(OnPostTransition);

            Scheduler.CancelAll();
            SdkEvents.ClearAll();

            if (_pump != null) Object.Destroy(_pump.gameObject);
            _pump = null;
            _lastPlayer = null;
            _sceneReadyPending = false;
            _sceneReadyFallback = null;

            SdkLog.Info("Core uninstalled.");
        }

        // One tick per frame, no matter what. Called both by FramePump's Update() and by
        // NeonNightSDKMod.OnFrame(); the frameCount latch guarantees that having both paths
        // doesn't double anything up (same protection TCModLoader itself uses in
        // RunModFrames).
        internal static void Tick()
        {
            if (!_installed || Time.frameCount == _lastTickedFrame) return;
            _lastTickedFrame = Time.frameCount;

            PollPlayer();
            Scheduler.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            SdkEvents.RaiseUpdate();
        }

        private static void PollPlayer()
        {
            // Nobody listening => don't even resolve the player. Without this the Core would
            // cost a Entity.GetPlayer + GetComponent every frame of every session, even for
            // players whose installed mods never use it.
            if (!SdkEvents.HasPlayerSubscribers) return;

            var current = PlayerRef.Current;
            // UnityEngine.Object's `!=`: also catches the "same reference, object already
            // destroyed" case that a ReferenceEquals would let through.
            if (current == _lastPlayer) return;

            var previous = _lastPlayer;
            _lastPlayer = current;

            if (previous != null) SdkEvents.RaisePlayerLost(previous);
            if (current != null) SdkEvents.RaisePlayerReady(current);
        }

        private static void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The pump may have died along with the bootstrap cleanup (see EnsurePump).
            // Revalidating on every scene is cheap and lets the SDK recover on its own.
            EnsurePump();

            Scheduler.OnSceneChanged();
            SdkEvents.RaiseSceneLoaded(scene.name);

            _sceneReadyFallback?.Cancel();
            _sceneReadyPending = true;

            // Fallback for scenes that DON'T go through LevelTransition — the initial boot
            // into MainMenu, or a SceneManager.LoadScene called directly by another mod. In
            // those cases PostTransition never fires, and without this OnSceneReady would
            // simply never happen. LevelTransition.isVisible is the game's own loading-curtain
            // flag (true while it covers the screen), so "not visible" is exactly "the scene is
            // showing". First one wins: _sceneReadyPending prevents a double dispatch.
            _sceneReadyFallback = Scheduler
                .When(() => !LevelTransition.isVisible, () => FireSceneReady(), timeoutSeconds: 60f)
                .Named("SdkRuntime.SceneReadyFallback");
        }

        private static void OnPostTransition() => FireSceneReady();

        private static void FireSceneReady()
        {
            if (!_sceneReadyPending) return;
            _sceneReadyPending = false;

            var sceneName = Scenes.Active;
            SdkEvents.RaiseSceneReady(sceneName);

            if (Scenes.IsGameplay(sceneName))
                SdkEvents.RaiseGameplaySceneReady(sceneName);
        }

        // WHY THIS IS NEEDED (and why the object is created late, then revalidated):
        // a GameObject with DontDestroyOnLoad created during the game's BOOTSTRAP scene gets
        // destroyed anyway on the transition to MainMenu. That was the exact root cause of the
        // "ITCMod.OnFrame() doesn't fire" bug (see
        // TestMod/docs/TCModLoader-OnFrame-Nao-Dispara.md): the loader's MonoBehaviour died
        // before the first frame. The fix, both there and here, is to create the object AFTER
        // a real scene has loaded — and since Install() may be called during bootstrap,
        // OnUnitySceneLoaded revalidates and recreates as needed.
        private static void EnsurePump()
        {
            if (_shuttingDown || !_installed) return;
            if (_pump != null) return;

            var go = new GameObject("NeonNightSDK_FramePump");
            Object.DontDestroyOnLoad(go);
            _pump = go.AddComponent<FramePump>();
        }

        private sealed class FramePump : MonoBehaviour
        {
            private void Update() => Tick();

            private void OnApplicationQuit() => _shuttingDown = true;
        }
    }
}
