using System;
using System.Collections.Generic;
using System.IO;
using Asuna.CharManagement;
using Asuna.Items;
using Modding;
using NeonNightSDK.Clothing;
using NeonNightSDK.Items;
using UnityEngine;

namespace NeonNightSDK.Loader
{
    // JSON schema for one clothing item, one file per item, living next to its
    // own texture PNG (e.g. Assets/Clothing/hat.json + Assets/Clothing/hat.png).
    // Field names are deliberately lowercase-flat, not camelCase-idiomatic C#:
    // UnityEngine.JsonUtility matches JSON keys to field names literally (no
    // casing conversion, no [JsonProperty] support), so the DTO fields ARE the
    // schema. x/y/width/height/rotation are expected in the units produced by
    // the TC-spine web calibrator (C:\Users\murillo\Documents\TC-spine\index.html),
    // i.e. scale=1 units — importScale converts them to the game's actual units
    // (see ClothingKit.ApplyTransform's doc comment for why that conversion
    // exists at all: SkeletonDataAsset.Scale, commonly 0.01, isn't applied by
    // the standalone browser player).
    [Serializable]
    public class ClothingImportEntry
    {
        public string key;
        public string name;
        public string description;
        public string texture;
        public string icon;
        public string equipSlot;
        public string skinName;
        public string slotName;
        public string attachmentKey;
        public float x;
        public float y;
        public float width;
        public float height;
        public float rotation;
        public float importScale = 0.01f;
    }

    // Generic "drop a PNG + JSON, no C# required" rigid-clothing importer.
    //
    // Two-phase, matching how every hand-written clothing item in this codebase
    // already works (see Mods/TestMod/Equipment/ClothingRegistry.cs, which this
    // class replaces the per-item version of):
    //
    //   1. LoadFolder(manifest, folder) — call once, from OnModLoaded. Scans
    //      folder for *.json entries and registers a blank Equipment item for
    //      each via ItemsKit.RegisterBlankEquipment. No live SkeletonData exists
    //      yet at this point, so no Spine skin is registered here.
    //   2. ApplyToCharacter(character) — call every scene load (idempotent, same
    //      as the rest of ClothingKit). Resolves each entry's texture through the
    //      calling mod's own ModSpriteResolver and registers the calibrated Spine
    //      skin via ClothingKit.RegisterRigidClothingSkinForCharacter.
    //
    // Scope: rigid accessories only, same limit as ClothingKit itself — see
    // Mods/TestMod/docs/Resumo-Descobertas.md for why deformable clothing (shirts,
    // pants) still needs a real .spine project and can't be made generic this way.
    public static class AssetImportLoader
    {
        private static readonly List<(ModManifest manifest, ClothingImportEntry entry)> _entries =
            new List<(ModManifest, ClothingImportEntry)>();

        public static void LoadFolder(ModManifest manifest, string folderRelativePath)
        {
            if (manifest == null)
            {
                Debug.LogError("[NeonNightSDK.Loader] LoadFolder: manifest is null.");
                return;
            }

            var folderFullPath = Path.Combine(manifest.ModPath, folderRelativePath);
            if (!Directory.Exists(folderFullPath))
            {
                Debug.LogWarning($"[NeonNightSDK.Loader] LoadFolder: folder not found at '{folderFullPath}', nothing to import.");
                return;
            }

            foreach (var jsonPath in Directory.GetFiles(folderFullPath, "*.json"))
            {
                try
                {
                    var entry = JsonUtility.FromJson<ClothingImportEntry>(File.ReadAllText(jsonPath));
                    if (!ValidateEntry(entry, jsonPath)) continue;

                    if (!Enum.TryParse<EquipmentSlot>(entry.equipSlot, true, out var slot))
                    {
                        Debug.LogError($"[NeonNightSDK.Loader] LoadFolder: '{jsonPath}' has invalid equipSlot '{entry.equipSlot}', skipping.");
                        continue;
                    }

                    var iconRelative = Path.Combine(folderRelativePath, string.IsNullOrEmpty(entry.icon) ? entry.texture : entry.icon);
                    var iconSprite = manifest.SpriteResolver.Resolve(iconRelative);

                    var item = ItemsKit.RegisterBlankEquipment(
                        entry.key, entry.name, entry.description,
                        iconSprite, new List<EquipmentSlot> { slot });
                    item.OverworldSpineSkins = new List<string> { entry.skinName };

                    // Store the texture path relative to the mod root (not the calibrator's
                    // raw filename) so ApplyToCharacter can resolve it straight through
                    // ModSpriteResolver, same as every other sprite in this codebase.
                    entry.texture = Path.Combine(folderRelativePath, entry.texture);
                    _entries.Add((manifest, entry));

                    Debug.Log($"[NeonNightSDK.Loader] Imported clothing '{entry.name}' (key='{entry.key}') from '{jsonPath}'.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NeonNightSDK.Loader] LoadFolder FAILED for '{jsonPath}': {ex}");
                }
            }
        }

        public static void ApplyToCharacter(Character character)
        {
            if (character == null) return;

            foreach (var (manifest, entry) in _entries)
            {
                var sprite = manifest.SpriteResolver.Resolve(entry.texture);
                if (sprite == null)
                {
                    Debug.LogError($"[NeonNightSDK.Loader] ApplyToCharacter: could not resolve texture '{entry.texture}' for '{entry.key}'.");
                    continue;
                }

                ClothingKit.RegisterRigidClothingSkinForCharacter(
                    character,
                    entry.skinName, entry.slotName, entry.attachmentKey,
                    sprite,
                    entry.x * entry.importScale, entry.y * entry.importScale,
                    entry.width * entry.importScale, entry.height * entry.importScale,
                    entry.rotation);
            }
        }

        private static bool ValidateEntry(ClothingImportEntry entry, string jsonPath)
        {
            if (entry == null)
            {
                Debug.LogError($"[NeonNightSDK.Loader] '{jsonPath}' failed to parse as JSON.");
                return false;
            }

            if (string.IsNullOrEmpty(entry.key) || string.IsNullOrEmpty(entry.name) ||
                string.IsNullOrEmpty(entry.texture) || string.IsNullOrEmpty(entry.skinName) ||
                string.IsNullOrEmpty(entry.slotName) || string.IsNullOrEmpty(entry.attachmentKey) ||
                string.IsNullOrEmpty(entry.equipSlot))
            {
                Debug.LogError($"[NeonNightSDK.Loader] '{jsonPath}' is missing a required field " +
                                "(key/name/texture/skinName/slotName/attachmentKey/equipSlot).");
                return false;
            }

            return true;
        }
    }
}
