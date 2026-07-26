using System.Collections.Generic;
using NeonNightSDK.Utility;
using Spine;
using Spine.Unity;
using Spine.Unity.AttachmentTools;
using UnityEngine;

namespace NeonNightSDK.Clothing
{
    // One piece of a deformable outfit: which existing attachment to reuse the
    // deformation of, and which Sprite to paint over it instead. SlotName +
    // SourceSkinName + SourceAttachmentKey identify the ORIGINAL piece — find
    // these three with the "Ver peças desta skin" button in the TC-spine web
    // calibrator (it lists slot/bone/key for every attachment in a given skin).
    public struct RemappedClothingPiece
    {
        public string SlotName;
        public string SourceSkinName;
        public string SourceAttachmentKey;
        public Sprite Sprite;

        public RemappedClothingPiece(string slotName, string sourceSkinName, string sourceAttachmentKey, Sprite sprite)
        {
            SlotName = slotName;
            SourceSkinName = sourceSkinName;
            SourceAttachmentKey = sourceAttachmentKey;
            Sprite = sprite;
        }
    }

    // Everything here was proven end-to-end on TestMod's custom hat before being
    // generalized — see Mods/TestMod/docs/Custom-Hat-How-It-Was-Made.md for the
    // full story of every gotcha this API already bakes in fixes for.
    //
    // Scope: RIGID accessories only (hat, glasses, held item, jewelry — anything
    // that follows a single existing bone without needing to deform). Clothing
    // that must bend with the body (shirts, pants, full outfits) needs an
    // existing deformable mesh to remap via Spine.Unity.AttachmentTools.GetRemappedClone
    // instead — this kit doesn't build new deformable meshes; that still requires
    // the original .spine project's bone weights.
    public static class ClothingKit
    {
        // Builds a BRAND NEW RegionAttachment from a Sprite — a genuine new quad
        // mesh, not reusing any existing character mesh. Correct approach for
        // anything rigid.
        //
        // IMPORTANT: pass the full source Material (not just its Shader) as
        // sourceMaterial. Passing only the Shader loses blend-mode config the
        // shader needs — transparent pixels (which become black RGB once
        // premultiplied, that's normal PMA) render as solid opaque black squares
        // instead of transparent. This cost real debugging time once; don't
        // repeat it.
        public static RegionAttachment BuildRigidAttachment(Sprite sprite, Material sourceMaterial, string attachmentName)
        {
            if (sprite == null || sourceMaterial == null)
            {
                Debug.LogError($"[NeonNightSDK.Clothing] BuildRigidAttachment: sprite={(sprite != null)} " +
                                $"sourceMaterial={(sourceMaterial != null)} — aborting.");
                return null;
            }

            try
            {
                var region = sprite.ToRegionAttachmentPMAClone(sourceMaterial);
                if (region == null)
                {
                    Debug.LogError("[NeonNightSDK.Clothing] BuildRigidAttachment: ToRegionAttachmentPMAClone returned null.");
                    return null;
                }

                return region;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NeonNightSDK.Clothing] BuildRigidAttachment FAILED: {ex}");
                return null;
            }
        }

        // Applies a calibrated transform to a RegionAttachment built by
        // BuildRigidAttachment and calls UpdateRegion() for you.
        //
        // x/y/width/height/rotation are expected to already be in the SAME units
        // the live game uses. If you calibrated them in a standalone Spine Web
        // Player (scale=1 by default) instead of reading them from an in-game
        // log, multiply by the SkeletonDataAsset's import Scale first (commonly
        // 0.01 — confirmed by comparing an existing MeshAttachment.Width in an
        // in-game log against the same attachment's width in the browser
        // console; they were 100x apart until the multiplication was applied).
        //
        // ScaleX/ScaleY are always forced to 1 here: final rendered size is
        // Width*ScaleX / Height*ScaleY, so leaving a non-1 native scale in place
        // while also setting Width/Height to the final desired size double-applies
        // the scale and the attachment renders far too big.
        public static void ApplyTransform(RegionAttachment region, float x, float y, float width, float height, float rotation = 0f)
        {
            if (region == null) return;

            region.ScaleX = 1f;
            region.ScaleY = 1f;
            region.X = x;
            region.Y = y;
            region.Rotation = rotation;
            region.Width = width;
            region.Height = height;
            region.UpdateRegion();
        }

        // Registers a rigid attachment as a named Skin into a live SkeletonData,
        // under attachmentKey — which MUST match the target slot's own
        // setup-pose attachment name (SlotData.AttachmentName) for the game's own
        // equip/skin-switch logic to pick it up automatically on every
        // equip/unequip, not just once. Registering it under any other key will
        // "work" only for as long as something manually forces the attachment
        // (e.g. Skeleton.SetAttachment) — it silently reverts the moment the game
        // re-resolves the skin on its own.
        //
        // No-ops (returns false) if a skin with this name already exists —
        // safe to call every scene load.
        public static bool RegisterRigidClothingSkin(
            SkeletonData skeletonData,
            string skinName,
            string slotName,
            string attachmentKey,
            Sprite sprite,
            Material sourceMaterial,
            float x, float y, float width, float height, float rotation = 0f)
        {
            if (skeletonData == null || skinName == null) return false;
            if (skeletonData.FindSkin(skinName) != null) return false;

            var slotData = skeletonData.FindSlot(slotName);
            if (slotData == null)
            {
                Debug.LogError($"[NeonNightSDK.Clothing] RegisterRigidClothingSkin: slot '{slotName}' not found.");
                return false;
            }

            var region = BuildRigidAttachment(sprite, sourceMaterial, attachmentKey);
            if (region == null) return false;

            ApplyTransform(region, x, y, width, height, rotation);

            var skin = new Skin(skinName);
            skin.SetAttachment(slotData.Index, attachmentKey, region);
            skeletonData.Skins.Add(skin);

            Debug.Log($"[NeonNightSDK.Clothing] Registered rigid clothing skin '{skinName}' on slot '{slotName}' " +
                      $"(X={region.X:0.###} Y={region.Y:0.###} Width={region.Width:0.###} Height={region.Height:0.###}).");
            return true;
        }

        // Convenience wrapper: walks every SkeletonAnimation on a Character's
        // Handlers (same pattern used for the overworld skeleton throughout
        // TestMod) and calls RegisterRigidClothingSkin against each one's live
        // SkeletonData, resolving sourceMaterial from that handler's own
        // MeshRenderer. Call this every scene load — it's idempotent.
        public static void RegisterRigidClothingSkinForCharacter(
            Asuna.CharManagement.Character character,
            string skinName,
            string slotName,
            string attachmentKey,
            Sprite sprite,
            float x, float y, float width, float height, float rotation = 0f)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                var meshRenderer = skeletonAnim.GetComponent<MeshRenderer>();
                var sourceMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;

                RegisterRigidClothingSkin(
                    skeletonAnim.Skeleton.Data,
                    skinName, slotName, attachmentKey,
                    sprite, sourceMaterial,
                    x, y, width, height, rotation);
            }
        }

        // Roupa de verdade (precisa dobrar com o corpo — torso, saia, etc.): em vez
        // de construir um quad novo, reaproveita a MALHA (pesos de vértice) de uma
        // peça que já existe no jogo (ex: "default_outfit/Torsooutfit" na skin
        // "outfits/default/top") e só troca a textura, via GetRemappedClone. Sem
        // X/Y/Width/Height pra calibrar — a forma final é a da peça original, só
        // com a sua imagem em cima.
        //
        // Uma peça de roupa geralmente precisa de VÁRIOS slots ao mesmo tempo (ex:
        // um vestido cobrindo torso + as duas peças que tampam o peito por cima —
        // "outfit_Bboobout"/"outfit_Fbooboutfit", que na Zoey desenham DEPOIS do
        // peito na ordem de profundidade e por isso são as responsáveis por
        // cobri-lo). Por isso esse método recebe uma lista de peças e monta TUDO
        // numa única Skin — assim um item só (um "OverworldSpineSkins" com 1 nome)
        // já cobre o corpo inteiro.
        //
        // No-ops (retorna false) se já existir uma skin com esse nome.
        public static bool RegisterRemappedClothingSkin(
            SkeletonData skeletonData,
            string newSkinName,
            Material sourceMaterial,
            IList<RemappedClothingPiece> pieces)
        {
            if (skeletonData == null || newSkinName == null) return false;
            if (skeletonData.FindSkin(newSkinName) != null) return false;
            if (sourceMaterial == null)
            {
                Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: sourceMaterial is null, aborting '{newSkinName}'.");
                return false;
            }

            var skin = new Skin(newSkinName);
            var appliedAny = false;

            foreach (var piece in pieces)
            {
                var sourceSkin = skeletonData.FindSkin(piece.SourceSkinName);
                if (sourceSkin == null)
                {
                    Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: skin de origem '{piece.SourceSkinName}' não encontrada (peça '{piece.SlotName}' pulada).");
                    continue;
                }

                var slotData = skeletonData.FindSlot(piece.SlotName);
                if (slotData == null)
                {
                    Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: slot '{piece.SlotName}' não encontrado.");
                    continue;
                }

                var original = sourceSkin.GetAttachment(slotData.Index, piece.SourceAttachmentKey);
                if (original == null)
                {
                    Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: peça original '{piece.SourceAttachmentKey}' não encontrada no slot '{piece.SlotName}' da skin '{piece.SourceSkinName}'.");
                    continue;
                }

                if (piece.Sprite == null)
                {
                    Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: sprite nulo pra peça '{piece.SlotName}'.");
                    continue;
                }

                var remapped = original.GetRemappedClone(piece.Sprite, sourceMaterial);
                if (remapped == null)
                {
                    Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: GetRemappedClone falhou pra '{piece.SlotName}'.");
                    continue;
                }

                skin.SetAttachment(slotData.Index, piece.SourceAttachmentKey, remapped);
                appliedAny = true;
            }

            if (!appliedAny)
            {
                Debug.LogError($"[NeonNightSDK.Clothing] RegisterRemappedClothingSkin: nenhuma peça aplicada, '{newSkinName}' não foi registrada.");
                return false;
            }

            skeletonData.Skins.Add(skin);
            Debug.Log($"[NeonNightSDK.Clothing] Registered remapped clothing skin '{newSkinName}'.");
            return true;
        }

        // Convenience wrapper: mesmo padrão de RegisterRigidClothingSkinForCharacter —
        // percorre os Handlers do Character e chama RegisterRemappedClothingSkin em
        // cada SkeletonData, resolvendo o Material a partir do MeshRenderer de cada um.
        public static void RegisterRemappedClothingSkinForCharacter(
            Asuna.CharManagement.Character character,
            string newSkinName,
            IList<RemappedClothingPiece> pieces)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                var meshRenderer = skeletonAnim.GetComponent<MeshRenderer>();
                var sourceMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : null;

                RegisterRemappedClothingSkin(skeletonAnim.Skeleton.Data, newSkinName, sourceMaterial, pieces);
            }
        }
    }
}
