using System;
using ANToolkit.Controllers;
using ANToolkit.Level;
using NeonNightSDK.Core;
using UnityEngine;

namespace NeonNightSDK.World
{
    // Spawning things the player can walk up to and interact with.
    //
    // This replaces ~40 lines of identical boilerplate that were copy-pasted into three
    // different TestMod services (ShopService.SpawnNpc, InfoNpcService.SpawnNpc,
    // VendingMachineService.SetupInScene): make a GameObject, parent a child for the visual,
    // add a SpriteRenderer, set sortingLayer/sortingOrder, compute a scale from the sprite
    // bounds, add a trigger collider, add an Interactable, wire OnInteracted.
    //
    // THE COLLIDER RULE (the one thing that's easy to get silently wrong):
    // Interactable needs a 3D UnityEngine.Collider. It collects them with
    // GetComponentsInChildren<Collider>() and GetIconDesiredLocation() then calls
    // _myColliders.First() — which THROWS InvalidOperationException when the list is empty.
    // A Collider2D does not count. Every method here guarantees a 3D trigger collider exists
    // before the Interactable is added.
    //
    // Objects spawned here are ordinary scene objects: Unity destroys them on scene change,
    // so there is nothing to clean up in OnModUnLoaded. Call the spawn again on the next
    // scene (SdkEvents.OnGameplaySceneReady is the natural place).
    public static class WorldKit
    {
        // Default visual height in world units. 2.2 is what TestMod's NPCs used and it reads
        // as roughly person-sized next to the player.
        public const float DefaultWorldHeight = 2.2f;

        private const string DefaultSortingLayer = "default";
        private const int DefaultSortingOrder = 100;

        // Spawns a brand new interactable object from a sprite: visual + collider +
        // Interactable, all wired up.
        //
        //   WorldKit.SpawnInteractable(sprite, pos, () => shop.OpenShop());
        //
        // IDEMPOTENT BY NAME: if an active object called `name` already exists in the scene,
        // nothing is spawned and the existing one is returned. That's what removes the
        // `private bool _spawned;` flag every service used to carry — call it as many times
        // as you like per scene, you get one object.
        //
        // Returns the root GameObject (null if it couldn't be created). The Interactable is on
        // the root, so `go.GetComponent<Interactable>()` gets you the full component if you
        // need something this signature doesn't expose (OnInteractionFinished, LookAtInteracter,
        // RequireLineOfSight, ...).
        public static GameObject SpawnInteractable(
            Sprite sprite,
            Vector3 position,
            Action onInteract,
            InteractionType type = InteractionType.Talk,
            string name = null,
            float worldHeight = DefaultWorldHeight,
            float maxDistance = 5f,
            Vector3? colliderSize = null,
            Vector2? iconOffset = null,
            string sortingLayer = DefaultSortingLayer,
            int sortingOrder = DefaultSortingOrder)
        {
            if (sprite == null)
            {
                SdkLog.Error($"WorldKit.SpawnInteractable('{name}'): sprite is null — check the path you passed to " +
                             "ModContext.LoadSprite / ModSpriteResolver.Resolve.");
                return null;
            }

            var objectName = string.IsNullOrEmpty(name) ? $"NeonNightSDK_Interactable_{sprite.name}" : name;

            var existing = GameObject.Find(objectName);
            if (existing != null)
            {
                SdkLog.Info($"WorldKit.SpawnInteractable: '{objectName}' already exists in this scene, reusing it.");
                return existing;
            }

            var root = new GameObject(objectName);
            root.transform.position = position;

            // The visual lives on a CHILD, not the root. That's deliberate: the sprite has to be
            // scaled to reach worldHeight, and scaling the root would scale the collider with
            // it, so the interaction volume would silently depend on the image's pixel size.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = sortingOrder;

            var spriteHeight = sprite.bounds.size.y;
            var scale = spriteHeight > 0f ? worldHeight / spriteHeight : 1f;
            visual.transform.localScale = new Vector3(scale, scale, 1f);

            // Collider derived from the scaled sprite so it actually matches what's on screen,
            // instead of the hardcoded 1.5 x 2.5 box the three TestMod services all used
            // regardless of their sprite.
            var size = colliderSize ?? new Vector3(
                Mathf.Max(0.5f, sprite.bounds.size.x * scale),
                Mathf.Max(0.5f, worldHeight),
                1f);

            var collider = root.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = new Vector3(0f, size.y * 0.5f, 0f);
            collider.isTrigger = true;

            var interactable = AttachInteractable(root, onInteract, type, maxDistance,
                iconOffset ?? new Vector2(0f, size.y + 0.3f));

            if (interactable == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            SdkLog.Info($"WorldKit.SpawnInteractable: '{objectName}' spawned at {position}.");
            return root;
        }

        // Makes objects that ALREADY EXIST in the scene interactable — the vending-machine
        // pattern: the game ships the prop, your mod adds behaviour to it.
        //
        //   WorldKit.AttachToExisting("CondomVendingMachine", onInteract: Buy);
        //
        // Matches every active object whose name CONTAINS `nameContains` (case-sensitive, same
        // as the string.Contains the original code used). Idempotent: objects that already
        // carry an Interactable are skipped, so calling this on every scene load is safe.
        //
        // Returns how many objects were wired up this call (0 is normal — it just means this
        // scene has none of them).
        public static int AttachToExisting(
            string nameContains,
            Action onInteract,
            InteractionType type = InteractionType.Talk,
            float maxDistance = 3f,
            Vector2 iconOffset = default,
            Vector3? colliderSizeIfMissing = null)
        {
            if (string.IsNullOrEmpty(nameContains))
            {
                SdkLog.Error("WorldKit.AttachToExisting: nameContains is null/empty.");
                return 0;
            }

            var count = 0;

            // FindObjectsOfType<Transform>() walks every transform in the scene. It's not cheap,
            // but it's a once-per-scene setup cost and it's what reliably finds props that
            // aren't reachable through any registry. Don't call this every frame.
            foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                var go = transform.gameObject;
                if (!go.name.Contains(nameContains)) continue;
                if (go.GetComponent<Interactable>() != null) continue;   // already wired

                if (AttachInteractable(go, onInteract, type, maxDistance, iconOffset, colliderSizeIfMissing) != null)
                    count++;
            }

            if (count > 0)
                SdkLog.Info($"WorldKit.AttachToExisting: wired up {count} object(s) matching '{nameContains}'.");

            return count;
        }

        // Adds an Interactable to an existing GameObject, guaranteeing the 3D collider it
        // needs. Use this when you already have the object (from AttachToExisting's matching,
        // or your own lookup) and just want the interaction wired.
        public static Interactable AttachInteractable(
            GameObject target,
            Action onInteract,
            InteractionType type = InteractionType.Talk,
            float maxDistance = 5f,
            Vector2 iconOffset = default,
            Vector3? colliderSizeIfMissing = null)
        {
            if (target == null)
            {
                SdkLog.Error("WorldKit.AttachInteractable: target is null.");
                return null;
            }

            if (onInteract == null)
                SdkLog.Warn($"WorldKit.AttachInteractable('{target.name}'): onInteract is null — " +
                            "the object will show an icon but do nothing when used.");

            // THE important guard. Interactable does GetComponentsInChildren<Collider>() and
            // later _myColliders.First(), which throws InvalidOperationException on an empty
            // list — so an Interactable with no 3D collider is a crash waiting to happen the
            // moment the icon tries to position itself. A Collider2D does NOT satisfy this
            // (TestMod's VendingMachineService added a BoxCollider2D, which is why that path
            // was fragile).
            if (target.GetComponentInChildren<Collider>() == null)
            {
                var size = colliderSizeIfMissing ?? new Vector3(1f, 1f, 1f);
                var added = target.AddComponent<BoxCollider>();
                added.size = size;
                added.isTrigger = true;

                SdkLog.Info($"WorldKit: '{target.name}' had no 3D Collider, added a {size} trigger box " +
                            "(Interactable requires one).");
            }

            var interactable = target.AddComponent<Interactable>();
            interactable.TypeOfInteraction = type;
            interactable.MaxDistance = maxDistance;
            interactable.ShowFeelerIcon = true;
            interactable.IconOffset = iconOffset;

            if (onInteract != null)
            {
                // OnInteracted hands over the interacting CharController; most callers don't
                // care, so the ergonomic signature is a plain Action. Grab the returned
                // Interactable and add your own listener if you need the controller.
                interactable.OnInteracted.AddListener(_ =>
                    SdkLog.SafeInvoke($"WorldKit interaction on '{target.name}'", onInteract));
            }

            return interactable;
        }

        // Creates an invisible trigger zone that runs code when the player walks into it.
        //
        //   WorldKit.CreateTrigger(pos, new Vector3(2f, 2f, 1f), onEnter: _ => Notification.Create("..."));
        //
        // The wiki documents ANToolkit.Level.Trigger but nothing in this codebase used it yet.
        // Note Trigger is 3D physics (OnTriggerEnter(Collider)), so Unity needs a Rigidbody on
        // at least one side — the player's controller provides it.
        //
        // once: wires OnFirstEnter instead of OnEnter, so the callback runs a single time no
        //   matter how often the player re-enters. That's the usual want for a story beat.
        // mustBeEntirelyInside: Trigger's own default is true, which means the player has to
        //   fit COMPLETELY inside the box. That surprises people with small zones, so this
        //   defaults to false here.
        public static GameObject CreateTrigger(
            Vector3 position,
            Vector3 size,
            Action<Collider> onEnter,
            string name = null,
            bool onlyPlayer = true,
            bool mustBeEntirelyInside = false,
            bool once = false,
            Action<Collider> onExit = null)
        {
            if (onEnter == null && onExit == null)
            {
                SdkLog.Error("WorldKit.CreateTrigger: both onEnter and onExit are null, nothing to do.");
                return null;
            }

            var objectName = string.IsNullOrEmpty(name) ? "NeonNightSDK_Trigger" : name;

            if (!string.IsNullOrEmpty(name))
            {
                var existing = GameObject.Find(objectName);
                if (existing != null)
                {
                    SdkLog.Info($"WorldKit.CreateTrigger: '{objectName}' already exists in this scene, reusing it.");
                    return existing;
                }
            }

            var go = new GameObject(objectName);
            go.transform.position = position;

            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;   // non-blocking: the player walks THROUGH it, not into it

            var trigger = go.AddComponent<Trigger>();
            trigger.OnlyTriggerPlayer = onlyPlayer;
            trigger.MustBeEntirelyInside = mustBeEntirelyInside;

            if (onEnter != null)
            {
                var target = once ? trigger.OnFirstEnter : trigger.OnEnter;
                target.AddListener(collider =>
                    SdkLog.SafeInvoke($"WorldKit trigger '{objectName}' onEnter", () => onEnter(collider)));
            }

            if (onExit != null)
            {
                trigger.OnAllExited.AddListener(collider =>
                    SdkLog.SafeInvoke($"WorldKit trigger '{objectName}' onExit", () => onExit(collider)));
            }

            SdkLog.Info($"WorldKit.CreateTrigger: '{objectName}' created at {position} (size {size}).");
            return go;
        }
    }
}
