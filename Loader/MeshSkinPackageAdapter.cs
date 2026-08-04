using System;
using System.Collections.Generic;
using System.IO;
using Asuna.CharManagement;
using Asuna.Items;
using Modding;
using NeonNightSDK.Core;
using NeonNightSDK.Items;
using Newtonsoft.Json;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace NeonNightSDK.Loader
{
    public enum MeshSkinSurface
    {
        Auto,
        Portrait,
        Chibi,
        Both
    }

    // Imports the package produced by TCNN-Mod-Mesh-Editor:
    // outfit.json + single.json + its PNG page (+ optional hide.json).
    // LoadPackage registers the item immediately and keeps retrying the Spine
    // injection when scenes become ready, because player skeleton assets are
    // not necessarily available during a mod's OnModLoaded callback.
    //
    // A package may be flat (single.json in the package folder) or split into one subfolder per
    // surface — the editor exports overworld and portrait separately, each with its own page PNG
    // and hide.json, sharing one outfit.json at the root. Both layouts land on the same skin name
    // (tcnn/<id>) and the same item (tcnn_<id>); a surface whose meshes don't exist on a given
    // skeleton simply contributes nothing to it, which is what keeps one package working across
    // both the chibi and portrait skeletons.
    //
    // DiscoverAll makes this the registry for every installed mod's outfits: the SDK sweeps
    // Mods/*/assets/* at startup, so an artist ships a package folder and nothing else — no C#,
    // no console command, and the item exists in Item.All before any shop goes looking for it.
    public static class MeshSkinPackageAdapter
    {
        private const float DefaultImportScale = 0.01f;

        [Serializable]
        private sealed class PackageMeta
        {
            public string id;
            public string name;
            public string description;
            public string icon;
            public string skeleton;
            public string surface;
            public string[] slots;
            public float importScale;
        }

        [Serializable]
        private sealed class MeshRecord
        {
            public string slot;
            public int slotIndex;
            public string name;
            public string skin;
            public int[] bones;
            public float[] vertices;
            public int worldVerticesLength;
            public int[] triangles;
            public float[] regionUVs;
        }

        [Serializable]
        private sealed class SinglePagePackage
        {
            public string page;
            public int pageW;
            public int pageH;
            public MeshRecord[] meshes;

            // Name of the SkeletonDataAsset this surface's indices are aligned to, written by
            // remap_to_live_rig.py. When set, the surface is only ever built on that rig.
            public string rig;
        }

        // One exported surface: its own single.json, page PNG and hide.json.
        private sealed class PackageSurface
        {
            public string Folder;
            public SinglePagePackage Data;
            public string[] HiddenSlots;
            public Texture2D Texture;
        }

        private sealed class LoadedPackage
        {
            public string Folder;
            public string SkinName;
            public PackageMeta Meta;
            public MeshSkinSurface Surface;
            public readonly List<PackageSurface> Surfaces = new List<PackageSurface>();
            public readonly HashSet<SkeletonData> Applied = new HashSet<SkeletonData>();
        }

        private static readonly List<LoadedPackage> Packages = new List<LoadedPackage>();
        private static bool _subscribed;

        // Where DumpRig writes. Set by the SDK at startup; null disables the dump.
        public static string DiagnosticsFolder;
        private static readonly HashSet<string> DumpedRigs = new HashSet<string>();

        public static Equipment LoadPackage(
            ModManifest manifest,
            string folderRelativePath,
            MeshSkinSurface surface = MeshSkinSurface.Auto)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            // Icons keep going through the mod's own SpriteResolver here so this overload behaves
            // exactly as before; the folder-only overload has no manifest and reads them off disk.
            var folder = Path.Combine(manifest.ModPath, folderRelativePath);
            return LoadCore(folder, surface,
                iconName => manifest.SpriteResolver.Resolve(Path.Combine(folderRelativePath, iconName)));
        }

        // Loads a package by absolute path, for callers that have no ModManifest — notably
        // DiscoverAll sweeping other mods' folders.
        public static Equipment LoadPackageFrom(
            string packageFolder,
            MeshSkinSurface surface = MeshSkinSurface.Auto)
        {
            if (string.IsNullOrEmpty(packageFolder)) throw new ArgumentNullException(nameof(packageFolder));
            return LoadCore(packageFolder, surface,
                iconName => LoadSpriteFromDisk(Path.Combine(packageFolder, iconName)));
        }

        // A folder is a package if it holds a single.json, or if any of its immediate subfolders
        // does (the multi-surface layout).
        public static bool IsPackage(string folder) => SurfaceFoldersOf(folder).Count > 0;

        // Registers every outfit package under <modsRoot>/*/assets/*. Called once by the SDK at
        // startup so no outfit mod has to write code — or make the player type a console command —
        // for its clothes to exist as items.
        public static int DiscoverAll(string modsRoot)
        {
            if (string.IsNullOrEmpty(modsRoot) || !Directory.Exists(modsRoot)) return 0;

            var found = 0;
            foreach (var modFolder in Directory.GetDirectories(modsRoot))
            {
                var assets = Path.Combine(modFolder, "assets");
                if (!Directory.Exists(assets)) continue;

                foreach (var candidate in Directory.GetDirectories(assets))
                {
                    if (!IsPackage(candidate)) continue;
                    if (LoadPackageFrom(candidate) != null) found++;
                }
            }
            return found;
        }

        private static Equipment LoadCore(string folder, MeshSkinSurface surface, Func<string, Sprite> iconLoader)
        {
            var surfaceFolders = SurfaceFoldersOf(folder);
            if (surfaceFolders.Count == 0)
            {
                Debug.LogError($"[NeonNightSDK.MeshSkin] single.json not found at '{folder}'.");
                return null;
            }

            try
            {
                var meta = ReadMeta(folder, surfaceFolders);
                if (string.IsNullOrEmpty(meta.id))
                    meta.id = new DirectoryInfo(folder).Name;
                if (string.IsNullOrEmpty(meta.name))
                    meta.name = meta.id;

                var key = ("tcnn_" + meta.id).ToLowerInvariant();
                var skinName = "tcnn/" + meta.id;

                foreach (var existingPackage in Packages)
                {
                    if (!string.Equals(existingPackage.Folder, folder, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Apply(existingPackage, Character.Player);
                    return Item.All.TryGetValue(key, out var existingItem)
                        ? existingItem as Equipment
                        : null;
                }

                var scale = meta.importScale > 0f ? meta.importScale : DefaultImportScale;
                var loaded = new LoadedPackage { Folder = folder, SkinName = skinName, Meta = meta };

                foreach (var surfaceFolder in surfaceFolders)
                {
                    var singlePath = Path.Combine(surfaceFolder, "single.json");
                    var data = JsonConvert.DeserializeObject<SinglePagePackage>(File.ReadAllText(singlePath));
                    if (data?.meshes == null || data.meshes.Length == 0 ||
                        string.IsNullOrEmpty(data.page) || data.pageW <= 0 || data.pageH <= 0)
                    {
                        // Spell out what's missing — "invalid" alone says nothing when the file
                        // parses fine everywhere else.
                        Debug.LogError($"[NeonNightSDK.MeshSkin] Invalid single.json at '{singlePath}': " +
                                       $"page='{data?.page}' pageW={data?.pageW} pageH={data?.pageH} " +
                                       $"meshes={(data?.meshes == null ? "null" : data.meshes.Length.ToString())}.");
                        continue;
                    }

                    ScaleVertices(data.meshes, scale);
                    loaded.Surfaces.Add(new PackageSurface
                    {
                        Folder = surfaceFolder,
                        Data = data,
                        HiddenSlots = LoadHiddenSlots(surfaceFolder)
                    });
                }

                if (loaded.Surfaces.Count == 0)
                {
                    Debug.LogError($"[NeonNightSDK.MeshSkin] '{folder}' has no usable surface.");
                    return null;
                }

                loaded.Surface = surface == MeshSkinSurface.Auto ? InferSurface(meta) : surface;

                var iconName = string.IsNullOrEmpty(meta.icon) ? "icon.png" : meta.icon;
                var item = ItemsKit.RegisterBlankEquipment(
                    key,
                    meta.name,
                    string.IsNullOrEmpty(meta.description) ? "Custom skin: " + meta.name : meta.description,
                    iconLoader(iconName),
                    ParseSlots(meta.slots, meta.id));

                item.DialogueSpineSkins = UsesPortrait(loaded.Surface)
                    ? new List<string> { skinName }
                    : new List<string>();
                item.OverworldSpineSkins = UsesChibi(loaded.Surface)
                    ? new List<string> { skinName }
                    : new List<string>();

                Packages.Add(loaded);

                if (!_subscribed)
                {
                    SdkEvents.OnSceneReady += _ => ApplyAll();
                    _subscribed = true;
                }

                Apply(loaded, Character.Player);
                var meshCount = 0;
                foreach (var s in loaded.Surfaces) meshCount += s.Data.meshes.Length;
                Debug.Log($"[NeonNightSDK.MeshSkin] Loaded '{meta.id}' for {loaded.Surface} " +
                          $"({loaded.Surfaces.Count} surface(s), {meshCount} meshes) -> item '{key}'.");
                return item;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NeonNightSDK.MeshSkin] Failed to load '{folder}': {ex}");
                return null;
            }
        }

        private static List<string> SurfaceFoldersOf(string folder)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return found;

            if (File.Exists(Path.Combine(folder, "single.json")))
            {
                found.Add(folder);
                return found;
            }

            foreach (var sub in Directory.GetDirectories(folder))
            {
                if (File.Exists(Path.Combine(sub, "single.json"))) found.Add(sub);
            }
            return found;
        }

        private static PackageMeta ReadMeta(string folder, List<string> surfaceFolders)
        {
            var meta = ReadMetaFile(Path.Combine(folder, "outfit.json")) ?? new PackageMeta();

            // A split package's surfaces each carry their own outfit.json naming the skeleton they
            // were exported against. Only adopt that hint when there is a single surface — with
            // two, the package genuinely spans both skeletons and must not be pinned to either.
            if (surfaceFolders.Count == 1 && !string.Equals(surfaceFolders[0], folder, StringComparison.OrdinalIgnoreCase))
            {
                var surfaceMeta = ReadMetaFile(Path.Combine(surfaceFolders[0], "outfit.json"));
                if (surfaceMeta != null)
                {
                    if (string.IsNullOrEmpty(meta.id)) meta.id = surfaceMeta.id;
                    if (string.IsNullOrEmpty(meta.name)) meta.name = surfaceMeta.name;
                    if (string.IsNullOrEmpty(meta.skeleton)) meta.skeleton = surfaceMeta.skeleton;
                    if (string.IsNullOrEmpty(meta.surface)) meta.surface = surfaceMeta.surface;
                }
            }
            return meta;
        }

        private static PackageMeta ReadMetaFile(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<PackageMeta>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NeonNightSDK.MeshSkin] Ignoring unreadable '{path}': {ex.Message}");
                return null;
            }
        }

        private static Sprite LoadSpriteFromDisk(string path)
        {
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) return null;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f));
        }

        // One rig the skin can be injected into, named after its SkeletonDataAsset.
        private struct SkeletonTarget
        {
            public string Name;
            public SkeletonData Data;
        }

        // The rigs actually on screen, taken from the live Spine components rather than from
        // Character.SpineSkeleton. Asking the asset for its SkeletonData can hand back a copy that
        // was never populated with the game's skins — a rig with no 'naked', no 'outfits/...' —
        // and every source lookup then misses, producing an empty skin and a character who just
        // looks undressed. The component's Skeleton.Data is the one the renderer is really using.
        private static List<SkeletonTarget> LiveSkeletons()
        {
            var targets = new List<SkeletonTarget>();
            var player = Character.Player;

            // The player's own assets. These are what the game resolves an equipped item's skin
            // name against, so they must be covered whether or not anything is on screen yet.
            AddAsset(targets, player?.SpineSkeleton);
            AddAsset(targets, player?.OverworldSpineSkeleton);

            // Only rigs the player actually uses. Sweeping every Spine component in the scene
            // reaches NPCs too, and a stray name collision then injects the outfit into some
            // passer-by — 'Built tcnn/racer on ChubbyMaleNPC_SkeletonData' really happened.
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (player?.SpineSkeleton != null) allowed.Add(player.SpineSkeleton.name);
            if (player?.OverworldSpineSkeleton != null) allowed.Add(player.OverworldSpineSkeleton.name);

            void AddLive(string name, SkeletonData data)
            {
                if (data == null || !allowed.Contains(name ?? "")) return;
                foreach (var existing in targets)
                    if (ReferenceEquals(existing.Data, data)) return;
                targets.Add(new SkeletonTarget { Name = name, Data = data });
            }

            foreach (var graphic in UnityEngine.Object.FindObjectsOfType<SkeletonGraphic>())
            {
                if (graphic == null || graphic.skeletonDataAsset == null) continue;
                AddLive(graphic.skeletonDataAsset.name, graphic.Skeleton?.Data);
            }

            foreach (var animation in UnityEngine.Object.FindObjectsOfType<SkeletonAnimation>())
            {
                if (animation == null || animation.skeletonDataAsset == null) continue;
                AddLive(animation.skeletonDataAsset.name, animation.Skeleton?.Data);
            }

            return targets;
        }

        private static void AddAsset(List<SkeletonTarget> targets, SkeletonDataAsset asset)
        {
            if (asset == null) return;
            var data = asset.GetSkeletonData(true);
            if (data == null) return;
            foreach (var existing in targets)
                if (ReferenceEquals(existing.Data, data)) return;
            targets.Add(new SkeletonTarget { Name = asset.name, Data = data });
        }

        public static int ApplyAll()
        {
            var targets = LiveSkeletons();
            var applied = 0;
            foreach (var package in Packages)
                foreach (var target in targets)
                    applied += ApplyToTarget(package, target);
            return applied;
        }

        public static int ApplyToCharacter(Character character)
        {
            var applied = 0;
            foreach (var package in Packages)
                applied += Apply(package, character);
            return applied;
        }

        private static int Apply(LoadedPackage package, Character character)
        {
            if (character == null) return 0;
            var targets = new List<SkeletonTarget>();
            if (UsesPortrait(package.Surface)) AddAsset(targets, character.SpineSkeleton);
            if (UsesChibi(package.Surface)) AddAsset(targets, character.OverworldSpineSkeleton);

            var applied = 0;
            foreach (var target in targets)
                applied += ApplyToTarget(package, target);
            return applied;
        }

        private static int ApplyToTarget(LoadedPackage package, SkeletonTarget target)
        {
            if (target.Data == null) return 0;
            if (!string.IsNullOrEmpty(package.Meta.skeleton) &&
                !string.Equals(package.Meta.skeleton, target.Name, StringComparison.OrdinalIgnoreCase))
                return 0;

            if (package.Applied.Contains(target.Data)) return 0;
            if (BuildSkin(package, target.Data, target.Name))
            {
                package.Applied.Add(target.Data);
                return 1;
            }
            return 0;
        }

        private static bool BuildSkin(LoadedPackage package, SkeletonData data, string rigName)
        {
            var skin = data.FindSkin(package.SkinName) ?? new Skin(package.SkinName);
            if (data.FindSkin(package.SkinName) == null) data.Skins.Add(skin);
            else skin.Clear();

            var built = 0;
            foreach (var surface in package.Surfaces)
                built += BuildSurface(package, surface, data, skin, rigName);

            if (built > 0)
                Debug.Log($"[NeonNightSDK.MeshSkin] Built '{package.SkinName}' on '{rigName}' ({built} meshes).");
            return built > 0;
        }

        private static int BuildSurface(LoadedPackage package, PackageSurface surface, SkeletonData data,
            Skin skin, string rigName)
        {
            // Explicit lock, when the package carries one.
            if (!string.IsNullOrEmpty(surface.Data.rig) &&
                !string.Equals(surface.Data.rig, rigName, StringComparison.OrdinalIgnoreCase))
                return 0;

            // Does this surface belong to this rig at all? Answered before any donor substitution,
            // because a donor is happy to hand back whatever mesh occupies a slot number — which,
            // once every index is valid on both rigs, would let the portrait surface "match" the
            // chibi and drape the character in unrelated geometry. An exact hit on the authored
            // skin+attachment is the proof; without a single one, this surface was exported for a
            // different skeleton.
            var exact = 0;
            foreach (var record in surface.Data.meshes)
            {
                if (record.slotIndex < 0 || record.slotIndex >= data.Slots.Count) continue;
                if (data.FindSkin(record.skin)?.GetAttachment(record.slotIndex, record.name) != null)
                    exact++;
            }
            if (exact == 0)
            {
                var probe = surface.Data.meshes.Length > 0 ? surface.Data.meshes[0] : null;
                Debug.Log($"[NeonNightSDK.MeshSkin] '{Path.GetFileName(surface.Folder)}' matches nothing on " +
                          $"'{rigName}' (rig has {data.Skins.Count} skin(s), {data.Slots.Count} slot(s); " +
                          $"wanted e.g. '{probe?.skin}/{probe?.name}'@{probe?.slotIndex}) — skipped.");
                DumpRig(data, rigName);
                return 0;
            }

            if (!EnsureTexture(surface)) return 0;

            AtlasRegion pageRegion = null;
            var built = 0;
            var misses = new List<string>();

            foreach (var record in surface.Data.meshes)
            {
                if (record.slotIndex < 0 || record.slotIndex >= data.Slots.Count) continue;
                var source = data.FindSkin(record.skin)?.GetAttachment(record.slotIndex, record.name)
                             ?? FindDonor(data, record.slotIndex);
                if (!(source?.Copy() is MeshAttachment mesh))
                {
                    misses.Add(record.skin + "/" + record.name);
                    continue;
                }

                if (pageRegion == null)
                {
                    var originalRegion = (source as IHasTextureRegion)?.Region as AtlasRegion;
                    var sourceMaterial = originalRegion?.page?.rendererObject as Material;
                    if (sourceMaterial == null) continue;
                    pageRegion = MakePageRegion(package, surface, sourceMaterial);
                }

                mesh.Region = pageRegion;
                mesh.RegionUVs = record.regionUVs;
                if (record.triangles != null) mesh.Triangles = record.triangles;
                mesh.Bones = record.bones;
                mesh.Vertices = record.vertices;
                mesh.WorldVerticesLength = record.worldVerticesLength;
                mesh.TimelineAttachment = mesh;
                mesh.UpdateRegion();

                var keys = new HashSet<string> { record.name };
                var setupKey = data.Slots.Items[record.slotIndex].AttachmentName;
                if (!string.IsNullOrEmpty(setupKey)) keys.Add(setupKey);
                foreach (var key in keys) skin.SetAttachment(record.slotIndex, key, mesh);
                built++;
            }

            // The belongs-here gate above already passed, so a total failure here means the copies
            // themselves went wrong, not that this is the wrong rig.
            if (built == 0)
            {
                Debug.LogWarning($"[NeonNightSDK.MeshSkin] '{Path.GetFileName(surface.Folder)}' belongs to " +
                                 $"'{rigName}' but produced no mesh — nothing copied.");
                return 0;
            }

            foreach (var miss in misses)
                Debug.LogWarning($"[NeonNightSDK.MeshSkin] Source '{miss}' not on '{rigName}' — used a donor from the same slot.");

            AddHiddenAttachments(data, skin, surface.HiddenSlots);
            return built;
        }

        // Writes the live rig's slot/bone/skin names to disk the first time a package fails to
        // match it. A package addresses its source meshes by slot *number*, so when it was exported
        // against a different build of the skeleton every lookup misses and the log can only say
        // "nothing matched". This is the missing half: with the real rig's names on disk, an
        // exported package can be remapped onto it without redrawing anything.
        private static void DumpRig(SkeletonData data, string rigName)
        {
            if (string.IsNullOrEmpty(DiagnosticsFolder) || data == null) return;
            var safeName = string.IsNullOrEmpty(rigName) ? "unnamed" : rigName;
            if (!DumpedRigs.Add(safeName)) return;

            try
            {
                Directory.CreateDirectory(DiagnosticsFolder);

                var slots = new string[data.Slots.Count];
                for (var i = 0; i < data.Slots.Count; i++) slots[i] = data.Slots.Items[i].Name;

                var bones = new string[data.Bones.Count];
                for (var i = 0; i < data.Bones.Count; i++) bones[i] = data.Bones.Items[i].Name;

                var skins = new string[data.Skins.Count];
                for (var i = 0; i < data.Skins.Count; i++) skins[i] = data.Skins.Items[i].Name;

                var path = Path.Combine(DiagnosticsFolder, safeName + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(
                    new { rig = safeName, slots, bones, skins }, Formatting.Indented));
                Debug.Log($"[NeonNightSDK.MeshSkin] Wrote live rig layout to '{path}'.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NeonNightSDK.MeshSkin] Could not dump rig '{safeName}': {ex.Message}");
            }
        }

        // Any mesh sitting on this slot, from any skin. The named source skin is only ever a
        // template — its geometry, UVs and triangles are all overwritten from the package a few
        // lines later, and only its material/attachment shape is kept. So when the rig no longer
        // carries the skin the package was authored against (outfits get renamed and removed
        // between builds), a sibling on the same slot is a perfectly good stand-in, and beats
        // dropping the piece entirely.
        private static Attachment FindDonor(SkeletonData data, int slotIndex)
        {
            foreach (var skin in data.Skins)
            {
                foreach (var entry in skin.Attachments)
                {
                    if (entry.SlotIndex != slotIndex) continue;
                    if (entry.Attachment is MeshAttachment) return entry.Attachment;
                }
            }
            return null;
        }

        private static bool EnsureTexture(PackageSurface surface)
        {
            if (surface.Texture != null) return true;

            var pagePath = Path.Combine(surface.Folder, surface.Data.page);
            if (!File.Exists(pagePath))
            {
                Debug.LogError($"[NeonNightSDK.MeshSkin] Texture page not found: '{pagePath}'.");
                return false;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(pagePath))) return false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            surface.Texture = texture;
            return true;
        }

        private static AtlasRegion MakePageRegion(LoadedPackage package, PackageSurface surface, Material sourceMaterial)
        {
            var material = new Material(sourceMaterial) { mainTexture = surface.Texture };
            var page = new AtlasPage
            {
                name = package.Meta.id,
                width = surface.Data.pageW,
                height = surface.Data.pageH,
                pma = true,
                rendererObject = material
            };
            return new AtlasRegion
            {
                page = page,
                name = package.Meta.id,
                index = -1,
                u = 0f,
                v = 0f,
                u2 = 1f,
                v2 = 1f,
                width = surface.Data.pageW,
                height = surface.Data.pageH,
                originalWidth = surface.Data.pageW,
                originalHeight = surface.Data.pageH
            };
        }

        private static void AddHiddenAttachments(SkeletonData data, Skin target, string[] slots)
        {
            var naked = data.FindSkin("naked");
            if (naked == null || slots == null) return;
            foreach (var slotName in slots)
            {
                var slot = data.FindSlot(slotName);
                if (slot == null) continue;
                foreach (var entry in naked.Attachments)
                {
                    if (entry.SlotIndex != slot.Index || entry.Attachment == null) continue;
                    var clear = entry.Attachment.Copy();
                    if (clear is MeshAttachment mesh) mesh.A = 0f;
                    else if (clear is RegionAttachment region) region.A = 0f;
                    target.SetAttachment(entry.SlotIndex, entry.Name, clear);
                }
            }
        }

        private static MeshSkinSurface InferSurface(PackageMeta meta)
        {
            if (!string.IsNullOrEmpty(meta.surface) &&
                Enum.TryParse(meta.surface, true, out MeshSkinSurface explicitSurface) &&
                explicitSurface != MeshSkinSurface.Auto)
                return explicitSurface;

            var skeleton = meta.skeleton ?? "";
            if (skeleton.IndexOf("portrait", StringComparison.OrdinalIgnoreCase) >= 0)
                return MeshSkinSurface.Portrait;
            if (skeleton.IndexOf("chibi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                skeleton.IndexOf("overworld", StringComparison.OrdinalIgnoreCase) >= 0)
                return MeshSkinSurface.Chibi;
            return MeshSkinSurface.Both;
        }

        private static bool UsesPortrait(MeshSkinSurface value) =>
            value == MeshSkinSurface.Portrait || value == MeshSkinSurface.Both;

        private static bool UsesChibi(MeshSkinSurface value) =>
            value == MeshSkinSurface.Chibi || value == MeshSkinSurface.Both;

        private static List<EquipmentSlot> ParseSlots(string[] names, string id)
        {
            var result = new List<EquipmentSlot>();
            foreach (var name in names ?? Array.Empty<string>())
            {
                if (Enum.TryParse(name, true, out EquipmentSlot slot)) result.Add(slot);
                else Debug.LogWarning($"[NeonNightSDK.MeshSkin] '{id}' has unknown equipment slot '{name}'.");
            }
            return result;
        }

        private static string[] LoadHiddenSlots(string folder)
        {
            var path = Path.Combine(folder, "hide.json");
            if (!File.Exists(path)) return Array.Empty<string>();
            try
            {
                // hide.json is a bare top-level array. JsonUtility can't parse one at all, which is
                // why this used to wrap it in a synthetic object; Newtonsoft just reads it.
                return JsonConvert.DeserializeObject<string[]>(File.ReadAllText(path))
                       ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NeonNightSDK.MeshSkin] Ignoring unreadable '{path}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static void ScaleVertices(MeshRecord[] meshes, float scale)
        {
            foreach (var mesh in meshes)
            {
                if (mesh.vertices == null) continue;
                for (var i = 0; i + 2 < mesh.vertices.Length; i += 3)
                {
                    mesh.vertices[i] *= scale;
                    mesh.vertices[i + 1] *= scale;
                }
            }
        }
    }
}
