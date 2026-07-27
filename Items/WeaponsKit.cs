using System;
using ANToolkit;
using Asuna.Items;
using UnityEngine;
using UnityEngine.Events;

namespace NeonNightSDK.Items
{
    // WeaponUI (the panel that shows up with an action bar while a weapon is drawn) already
    // builds one WeaponActionUI — "ActionUI(Clone)" — per entry in Weapon.Actions, parented
    // under WeaponUI.Contents' HorizontalLayoutGroup. That happens in Asuna.Items.Weapon.WeaponEquip,
    // which calls the static WeaponUI.Create(this) every time a weapon is equipped. There is no
    // prefab to edit: adding an action button to a weapon is adding a Weapon.Action, nothing more.
    //
    // The one thing the base game does not handle is a weapon that is ALREADY equipped: Create()
    // only runs at equip time, and WeaponEquip only wires hotkeys for the Actions that existed in
    // that same frame. AddAction/RemoveAction below do that missing half — rebuild the on-screen
    // WeaponUI and (re)bind the hotkey — so a mod can add or remove an action at any time, not
    // only before the weapon is drawn.
    public static class WeaponsKit
    {
        // Adds (or replaces, if `index` is already taken) an action button on `weapon`.
        //
        //   WeaponsKit.AddAction(pistola, 2, "Recarregar", iconeRecarga, Recarregar);
        //
        // index 0/1/2 fall back to the game's own default hotkeys ("Use"/"Cancel"/
        // "Tool_TertiaryAction") when `hotkey` is left null — same rule Weapon.SetAction applies.
        // Any other index with no hotkey just gets a clickable button and no keybind, which
        // matches how the base game treats extra action slots.
        public static Weapon.Action AddAction(
            Weapon weapon,
            int index,
            string displayName,
            Sprite icon,
            UnityAction callback,
            string hotkey = null,
            float cooldown = 0f,
            float freezeDuration = 0f,
            Func<bool> canUse = null)
        {
            var action = new Weapon.Action
            {
                displayName = displayName,
                displayIcon = icon,
                hotkey = hotkey,
                cooldown = cooldown,
                freezeDuration = freezeDuration,
                callback = callback,
                CanUse = canUse,
            };

            weapon.SetAction(index, action);
            RefreshIfOnScreen(weapon, action);
            return action;
        }

        public static void RemoveAction(Weapon weapon, int index)
        {
            var action = weapon.GetAction(index);
            if (action == null) return;

            if (IsOnScreen(weapon) && action.hotkeyCallback != null)
                InputManager.RemoveBindDownListener(action.hotkey, action.hotkeyCallback);

            weapon.RemoveAction(index);
            if (IsOnScreen(weapon)) WeaponUI.Create(weapon);
        }

        private static bool IsOnScreen(Weapon weapon) =>
            weapon != null && weapon.IsEquipped && weapon.Owner != null && weapon.Owner.IsPlayer;

        private static void RefreshIfOnScreen(Weapon weapon, Weapon.Action action)
        {
            if (!IsOnScreen(weapon)) return;

            if (!string.IsNullOrEmpty(action.hotkey))
            {
                action.hotkeyCallback = () => weapon.UseAction(action.index);
                InputManager.AddBindDownListener(action.hotkey, action.hotkeyCallback);
            }

            WeaponUI.Create(weapon);
        }
    }
}
