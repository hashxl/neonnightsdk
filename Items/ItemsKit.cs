using System.Collections.Generic;
using Asuna.CharManagement;
using Asuna.Items;
using Spine.Unity;
using UnityEngine;

namespace NeonNightSDK.Items
{
    public static class ItemsKit
    {
        // key must already be lowercase — Item.All is keyed by lowercase name by
        // convention throughout the existing mods (Item.Clone() re-resolves
        // clones by re-calling Item.Create(this.name), so `name`/`Name` must be
        // set to the same value used here or clone lookups fail silently).
        public static void RegisterConsumable(string key, string name, string description, Sprite sprite, int healAmount = 0)
        {
            if (Item.All.ContainsKey(key)) return;

            var item = ScriptableObject.CreateInstance<Consumable>();
            item.name = name;
            item.Name = name;
            item.Description = description;
            item.HealAmount = healAmount;
            item.DisplaySprite = sprite;
            Item.All.Add(key, item);
        }

        public static void RegisterKeyItem(string key, string name, string description, Sprite sprite)
        {
            if (Item.All.ContainsKey(key)) return;

            var item = ScriptableObject.CreateInstance<KeyItem>();
            item.name = name;
            item.Name = name;
            item.Description = description;
            item.DisplaySprite = sprite;
            Item.All.Add(key, item);
        }

        // Returns a fully-initialized Equipment ready for the caller to set
        // OverworldSpineSkins / DialogueSpineSkins / etc. If `key` is already
        // registered, returns the existing instance instead of creating another.
        //
        // WHY all the empty-list assignments matter: ScriptableObject.CreateInstance<Equipment>()
        // leaves every List<> field null, and OverrideSpineSkinOptions/StorageVisuals
        // null too. A real Unity-authored Equipment asset always has these serialized
        // as live (if empty) objects. CharacterSprite_Spine.Populate() calls into
        // OverrideSpineSkinOptions.IsActive / StorageVisuals.Handle... unconditionally
        // with no null-check — that crashes the Spine renderer every frame if left null.
        // This was discovered the hard way once; this helper exists so nobody has to
        // rediscover it.
        public static Equipment RegisterBlankEquipment(
            string key, string name, string description, Sprite displaySprite,
            List<EquipmentSlot> slots, ItemCategory category = ItemCategory.Clothing)
        {
            if (Item.All.TryGetValue(key, out var existing))
                return existing as Equipment;

            var item = ScriptableObject.CreateInstance<Equipment>();
            item.name = name;
            item.Name = name;
            item.Description = description;
            item.Category = category;
            item.DisplaySprite = displaySprite;
            item.CanRemove = true;
            item.Slots = slots;

            item.DisplayLayerInfos = new List<VisualLayerInfo>();
            item.OverworldSpineSkins = new List<string>();
            item.DialogueSpineSkins = new List<string>();
            item.CompatibleSkeletons = new List<SkeletonDataAsset>();
            item.SpineColors = new List<Color>();
            item.EffectsOnEquip = new List<LimbEffect>();
            item.EquipmentToHideOnEquip = new List<Equipment>();
            item.LimbEffectAdditions = new List<ItemLimbEffectAddition>();
            item.DurabilityDisplayLayers = new List<DurabilityLayerInfo>();
            item.AddToVisual = new List<GameObject>();
            item.AllowedCharacters = new List<Character>();
            item.StatRequirements = new List<StatModifierInfo>();
            item.RemovalOverrides = new List<StatRemovalOverride>();

            item.OverrideSpineSkinOptions = new ItemReplacementVisuals
            {
                VisualData = new List<ItemReplacementVisuals.ItemReplacementVisualsSkinData>()
            };
            item.StorageVisuals = new ItemStorageVisuals
            {
                VisualData = new List<ItemStorageVisuals.SkinData>()
            };

            Item.All.Add(key, item);
            return item;
        }
    }
}
