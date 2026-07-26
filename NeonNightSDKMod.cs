using Asuna.Dialogues;
using Modding;
using NeonNightSDK.Core;

namespace NeonNightSDK
{
    // Pure library mod: no gameplay content of its own. Other mods declare
    // "Requires": { "neonnightsdk": "v0.2.0" } in their manifest.json and call
    // into NeonNightSDK.Core / .Clothing / .Items / .Animations / .Loader directly.
    //
    // The only thing this mod DOES is bring up the Core runtime (events + scheduler) that
    // other mods consume. ModDependencyResolver guarantees it loads before anyone declaring
    // it in Requires, so SdkRuntime is already installed by the time a dependent's
    // OnModLoaded runs.
    public class NeonNightSDKMod : ITCMod
    {
        public void OnModLoaded(ModManifest manifest)
        {
            SdkRuntime.Install();
            SdkLog.Info($"Loaded (v{SdkRuntime.Version}).");
        }

        public void OnModUnLoaded()
        {
            SdkRuntime.Shutdown();
            SdkLog.Info("Unloaded.");
        }

        // Second path to the tick, alongside SdkRuntime's own FramePump. The frameCount latch
        // inside Tick() guarantees having both doesn't run anything twice — it's cheap
        // redundancy in case either path fails (this game's "OnFrame doesn't fire" history
        // justifies the belt and braces).
        public void OnFrame()
        {
            SdkRuntime.Tick();
        }

        // ITCMod hands these two hooks only to each mod's root class. Re-broadcasting them
        // through SdkEvents lets any service in any mod listen without implementing the
        // interface and manually forwarding calls down into itself.
        public void OnDialogueStarted(Dialogue dialogue)
        {
            SdkEvents.RaiseDialogueStarted(dialogue);
        }

        public void OnLineStarted(DialogueLine line)
        {
            SdkEvents.RaiseLineStarted(line);
        }
    }
}
