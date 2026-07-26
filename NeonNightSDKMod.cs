using Modding;
using UnityEngine;

namespace NeonNightSDK
{
    // Pure library mod: no gameplay content of its own. Other mods declare
    // "Requires": { "neonnightsdk": "v0.1.0" } in their manifest.json and call
    // into NeonNightSDK.Clothing / .Items / .Animations / .Loader directly.
    public class NeonNightSDKMod : ITCMod
    {
        public void OnModLoaded(ModManifest manifest)
        {
            Debug.Log("[NeonNightSDK] Loaded.");
        }

        public void OnModUnLoaded()
        {
            Debug.Log("[NeonNightSDK] Unloaded.");
        }

        public void OnFrame()
        {
        }
    }
}
