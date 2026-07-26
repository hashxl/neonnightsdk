using System.Collections.Generic;
using Asuna.CharManagement;
using Spine.Unity;

namespace NeonNightSDK.Utility
{
    // Every kit in the SDK (ClothingKit, AnimationsKit) that touches a Character's
    // Spine rig repeats the same "walk Handlers, GetComponentInChildren<SkeletonAnimation>,
    // skip if it or its Skeleton is null" boilerplate. Centralized here so call sites
    // read as intent (foreach skeletonAnim in CharacterSkeletons.GetAll(character))
    // instead of re-deriving the null-check every time.
    public static class CharacterSkeletons
    {
        public static SkeletonAnimation Get(CharacterHandler handler)
        {
            if (handler == null) return null;
            var skeletonAnim = handler.GetComponentInChildren<SkeletonAnimation>();
            return skeletonAnim != null && skeletonAnim.Skeleton != null ? skeletonAnim : null;
        }

        public static IEnumerable<SkeletonAnimation> GetAll(Character character)
        {
            if (character?.Handlers == null) yield break;

            foreach (var handler in character.Handlers)
            {
                var skeletonAnim = Get(handler);
                if (skeletonAnim != null) yield return skeletonAnim;
            }
        }
    }
}
