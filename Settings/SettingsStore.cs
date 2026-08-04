using System.Collections.Generic;
using System.IO;
using System.Text;
using NeonNightSDK.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace NeonNightSDK.Settings
{
    // Reads and writes one flat key/value JSON file per mod.
    //
    // WHERE: Application.persistentDataPath, not the mod's own folder. A Steam install can sit
    // under Program Files, where a normal user account cannot write — settings saved next to
    // the DLL would silently fail to persist for some players and not others. persistentDataPath
    // is guaranteed writable and survives reinstalling the mod.
    //
    // WHAT: Dictionary<string,string>, not a typed object. Each option already knows how to
    // turn itself into a string and back (see ISettingOption), so the store never needs to know
    // what any of them mean — which is also what lets an unknown key from a newer mod version
    // sit in the file untouched instead of blowing up the read.
    internal static class SettingsStore
    {
        private static readonly Dictionary<string, string> Empty = new Dictionary<string, string>();

        internal static string DirectoryPath =>
            Path.Combine(Application.persistentDataPath, "ModSettings");

        internal static string PathFor(string id) =>
            Path.Combine(DirectoryPath, Sanitize(id) + ".json");

        internal static Dictionary<string, string> Load(string id)
        {
            var path = PathFor(id);

            try
            {
                if (!File.Exists(path)) return Empty;

                var json = File.ReadAllText(path, Encoding.UTF8);
                var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                return values ?? Empty;
            }
            catch (JsonException ex)
            {
                // A corrupt file must not cost the player their whole session. Defaults are
                // used, and the file is left on disk so it can be inspected or fixed by hand.
                SdkLog.Error($"Settings: '{path}' is not valid JSON, using defaults: {ex.Message}");
                return Empty;
            }
            catch (IOException ex)
            {
                SdkLog.Error($"Settings: could not read '{path}', using defaults: {ex.Message}");
                return Empty;
            }
        }

        internal static void Save(string id, Dictionary<string, string> values)
        {
            var path = PathFor(id);

            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(path, JsonConvert.SerializeObject(values, Formatting.Indented), Encoding.UTF8);
            }
            catch (IOException ex)
            {
                SdkLog.Error($"Settings: could not write '{path}': {ex.Message}");
            }
            catch (System.UnauthorizedAccessException ex)
            {
                SdkLog.Error($"Settings: no permission to write '{path}': {ex.Message}");
            }
        }

        // Mod ids are free-form strings from manifest.json and end up as a file name.
        private static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "unnamed";

            var builder = new StringBuilder(id.Length);
            var invalid = Path.GetInvalidFileNameChars();

            foreach (var c in id)
                builder.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            return builder.ToString();
        }
    }
}
