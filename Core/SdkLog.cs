using System;
using UnityEngine;

namespace NeonNightSDK.Core
{
    // Standard SDK logging, plus the helper that keeps ONE broken mod from taking the
    // others down with it.
    //
    // WHY SafeInvoke exists: same reasoning already spelled out in
    // AnimationsKit.PlayAnimationPipeline. Mod callbacks all run inside a single shared
    // foreach (the loader's OnFrame loop, or a UnityEvent's invocation list), so an
    // uncaught exception from mod A doesn't just break mod A: it aborts the whole loop and
    // silently skips every mod still queued behind it (re-applying clothing, setting up
    // shops, etc.). Every event dispatch and every scheduled callback in Core goes through
    // here, so the worst case of a buggy mod is one error line in the log — never the whole
    // ecosystem stopping with it.
    public static class SdkLog
    {
        public const string Tag = "[NeonNightSDK]";

        public static void Info(string message) => Debug.Log($"{Tag} {message}");
        public static void Warn(string message) => Debug.LogWarning($"{Tag} {message}");
        public static void Error(string message) => Debug.LogError($"{Tag} {message}");

        // Runs `action`, swallowing (and logging) any exception. Returns false if it threw.
        internal static bool SafeInvoke(string what, Action action)
        {
            if (action == null) return false;

            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                Error($"{what} threw (the rest of the SDK keeps running normally): {ex}");
                return false;
            }
        }

        // Readable name for a handler, so the error message can point at the culprit —
        // without it, "some OnSceneReady subscriber broke" is impossible to debug once
        // five mods are installed.
        internal static string Describe(Delegate handler)
        {
            if (handler?.Method == null) return "<unknown>";

            var declaring = handler.Method.DeclaringType;
            return declaring == null
                ? handler.Method.Name
                : $"{declaring.FullName}.{handler.Method.Name}";
        }
    }
}
