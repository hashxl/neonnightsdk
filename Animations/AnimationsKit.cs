using System;
using System.Collections.Generic;
using System.IO;
using Modding;
using NeonNightSDK.Utility;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace NeonNightSDK.Animations
{
    // One step of a PlayAnimationPipeline. AnimationName can be ANY animation
    // that already exists in the skeleton (the game's own "actions/faint/faint_fall",
    // "general/idle/idle", etc. — found via the "Listar todas as skins"-style dump
    // in the TC-spine calibrator, extended to also list skeletonData.animations) —
    // it does not have to be one you registered yourself.
    //
    // Loop only really matters on the LAST step (or on the return-to-idle step):
    // Spine's AddAnimation queues each step to start when the PREVIOUS one's own
    // duration elapses once, even if that previous step has Loop=true — so a
    // looping step in the MIDDLE of a pipeline still only plays one cycle before
    // the next step starts. Only a step with nothing queued after it actually
    // loops forever.
    [Serializable]
    public class AnimationPipelineStep
    {
        public string AnimationName;
        public bool Loop;

        // Quanto tempo (segundos) esse step toca antes do próximo da fila (ou o retorno) assumir —
        // null = comportamento padrão do Spine (troca quando o clipe nativo termina um ciclo, via
        // TrackEntry.TrackComplete, ver comentário em PlayAnimationPipeline). Setar aqui é o que
        // controla a troca de verdade: por baixo isso vira o "delay" passado pro AddAnimation do
        // PRÓXIMO step — confirmado por reflection em spine-csharp.dll (Spine.AnimationState.Update):
        // a troca pro TrackEntry seguinte acontece quando trackLast do atual alcança next.delay,
        // não quando o clipe nativo "acaba" de verdade. Então dá pra cortar uma animação mais curta
        // do que o clipe (ex: 0.5s de um clipe de 2s) ou esticar um Loop=true por mais tempo do que
        // um ciclo só (ex: 5s de "general/idle/idle_drunk" antes de ir pro próximo step).
        public float? DurationOverride;

        public AnimationPipelineStep(string animationName, bool loop = false, float? durationOverride = null)
        {
            AnimationName = animationName;
            Loop = loop;
            DurationOverride = durationOverride;
        }
    }

    // JSON schema for one keyframe: `angle` is the FINAL desired rotation in the
    // bone's own local space (what you'd read off a reference pose) — NOT the
    // raw value Spine's RotateTimeline stores internally (which is a delta from
    // BoneData.Rotation). RegisterBoneRotationAnimation does that subtraction
    // for you so nobody has to think about setup-pose-relative math by hand.
    [Serializable]
    public class AnimationKeyframeDto
    {
        public float time;
        public float angle;
    }

    [Serializable]
    public class BoneTrackDto
    {
        public string bone;
        public List<AnimationKeyframeDto> keyframes;
    }

    [Serializable]
    public class AnimationDto
    {
        public string name;
        public List<BoneTrackDto> tracks;
    }

    // Animações simples via código/JSON, sem precisar do projeto .spine original —
    // mesma ideia do ClothingKit: Animation/Timeline são só dados, dá pra injetar
    // direto no SkeletonData em runtime. Assinaturas confirmadas por reflection em
    // spine-csharp.dll deste projeto antes de escrever qualquer coisa aqui:
    //   Animation(string name, ExposedList<Timeline> timelines, float duration)
    //   RotateTimeline(int frameCount, int bezierCount, int boneIndex)
    //   CurveTimeline1.SetFrame(int frame, float time, float value)
    //   CurveTimeline.SetLinear(int frame)
    //
    // Escopo: só rotação de osso (RotateTimeline) — um osso girando inteiro (mão,
    // antebraço, cabeça) já cobre acenar/levantar a mão/balançar a cabeça. Não
    // cobre troca de attachment por frame (flipbook) nem deformação de malha por
    // vértice (isso ainda exige o .spine original).
    public static class AnimationsKit
    {
        // Constrói e registra uma Animation nova a partir de uma ou mais trilhas
        // (um osso + seus keyframes de rotação). No-op (retorna false) se já
        // existir uma Animation com esse nome — seguro chamar toda scene load,
        // igual ClothingKit.
        public static bool RegisterBoneRotationAnimation(SkeletonData skeletonData, string animationName, List<BoneTrackDto> tracks)
        {
            if (skeletonData == null || string.IsNullOrEmpty(animationName)) return false;
            if (skeletonData.FindAnimation(animationName) != null) return false;
            if (tracks == null || tracks.Count == 0) return false;

            var timelines = new ExposedList<Timeline>();
            var duration = 0f;

            foreach (var track in tracks)
            {
                var boneData = skeletonData.FindBone(track.bone);
                if (boneData == null)
                {
                    Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimation: osso '{track.bone}' não encontrado, pulando trilha.");
                    continue;
                }
                if (track.keyframes == null || track.keyframes.Count == 0) continue;

                var timeline = new RotateTimeline(track.keyframes.Count, 0, boneData.Index);
                for (var i = 0; i < track.keyframes.Count; i++)
                {
                    var kf = track.keyframes[i];
                    // RotateTimeline guarda um DELTA relativo à rotação de setup do osso,
                    // não o ângulo absoluto — por isso a subtração aqui.
                    timeline.SetFrame(i, kf.time, kf.angle - boneData.Rotation);
                    timeline.SetLinear(i);
                    duration = Mathf.Max(duration, kf.time);
                }
                timelines.Add(timeline);
            }

            if (timelines.Count == 0)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimation: nenhuma trilha válida, '{animationName}' não foi registrada.");
                return false;
            }

            var animation = new Animation(animationName, timelines, duration);
            skeletonData.Animations.Add(animation);
            Debug.Log($"[NeonNightSDK.Animations] Registered animation '{animationName}' ({timelines.Count} trilha(s), duração={duration:0.###}s).");
            return true;
        }

        // Lê um JSON no formato { "name": "...", "tracks": [ { "bone": "...",
        // "keyframes": [ {"time":0,"angle":0}, ... ] } ] } e registra via
        // RegisterBoneRotationAnimation. Caminho relativo à pasta do mod, mesmo
        // padrão de manifest.SpriteResolver usado no resto do NeonNightSDK.
        public static bool RegisterBoneRotationAnimationFromJson(SkeletonData skeletonData, ModManifest manifest, string relativePath)
        {
            if (manifest == null)
            {
                Debug.LogError("[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: manifest is null.");
                return false;
            }

            var fullPath = Path.Combine(manifest.ModPath, relativePath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: arquivo não encontrado em '{fullPath}'.");
                return false;
            }

            AnimationDto dto;
            try
            {
                dto = JsonUtility.FromJson<AnimationDto>(File.ReadAllText(fullPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: falha ao ler '{fullPath}': {ex}");
                return false;
            }

            if (dto == null || string.IsNullOrEmpty(dto.name) || dto.tracks == null)
            {
                Debug.LogError($"[NeonNightSDK.Animations] RegisterBoneRotationAnimationFromJson: JSON inválido em '{fullPath}'.");
                return false;
            }

            return RegisterBoneRotationAnimation(skeletonData, dto.name, dto.tracks);
        }

        // Convenience wrapper: mesmo padrão de ClothingKit — percorre os Handlers
        // do Character e registra em cada SkeletonData. Chame a cada scene load.
        public static void RegisterBoneRotationAnimationForCharacter(Asuna.CharManagement.Character character, string animationName, List<BoneTrackDto> tracks)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                RegisterBoneRotationAnimation(skeletonAnim.Skeleton.Data, animationName, tracks);
            }
        }

        // Toca uma sequência de animações JÁ EXISTENTES no jogo (ou registradas por
        // você via RegisterBoneRotationAnimation — funciona igual, pelo nome), uma
        // atrás da outra, cada uma até acabar, num track do AnimationState. Estilo
        // "pipeline" de mod de animação (SKSE/FNIS): idle -> [pipeline] -> alguma
        // coisa nova, e no final volta pra onde estava ou vai pra um estado fixo.
        //
        // O que toca DEPOIS do pipeline (opcional, escolha um dos dois):
        //   - returnToAnimationName: sempre volta pra essa animação específica
        //     (ex: sempre "general/idle/idle" — útil quando a pipeline é um evento
        //     pontual tipo cair e levantar).
        //   - returnToPreviousAnimation=true: memoriza o que tava tocando ANTES de
        //     chamar o pipeline (via AnimationState.GetCurrent) e volta pra aquilo
        //     no final — útil quando você não sabe de antemão se o personagem
        //     estava parado, andando, etc.
        // Se nenhum dos dois for passado, o pipeline simplesmente para no último
        // step (fica ali parado se Loop=false, ou repete pra sempre se Loop=true) —
        // é o caso de "agora ele fica engatinhando/andando de um jeito novo, ponto".
        //
        // Assinaturas confirmadas por reflection em spine-csharp.dll:
        //   AnimationState.SetAnimation(int track, string name, bool loop) -> TrackEntry
        //   AnimationState.AddAnimation(int track, string name, bool loop, float delay) -> TrackEntry
        //   AnimationState.GetCurrent(int track) -> TrackEntry (Animation, Loop)
        // lockMovementFor: se passado, trava o input de movimento do personagem
        // (via ANToolkit.Controllers.CharController.AddInputRestraint — o mesmo
        // sistema que o próprio jogo usa pra travar o jogador durante coisas como
        // pathfinding automático, log confirmado: "CharController: Removing Input
        // Restraint: PMA_PathfindingMoveTo") assim que o pipeline começa, e destrava
        // (RemoveInputRestraint) automaticamente quando a ÚLTIMA entrada da fila
        // (o retorno, ou o último step se não houver retorno) realmente começa a
        // tocar — via Spine TrackEntry.Start, que dispara exatamente quando todas as
        // entradas anteriores da fila já terminaram. Sem isso, o personagem
        // continua andando normalmente por cima da animação (ex: cair e ainda
        // conseguir caminhar).
        public static void PlayAnimationPipeline(
            SkeletonAnimation skeletonAnim,
            int trackIndex,
            List<AnimationPipelineStep> steps,
            string returnToAnimationName = null,
            bool returnToPreviousAnimation = false,
            Asuna.CharManagement.Character lockMovementFor = null)
        {
            if (skeletonAnim == null || skeletonAnim.AnimationState == null)
            {
                Debug.LogError("[NeonNightSDK.Animations] PlayAnimationPipeline: skeletonAnim/AnimationState nulo.");
                return;
            }
            if (steps == null || steps.Count == 0) return;

            // Spine's AnimationState.SetAnimation/AddAnimation throw ArgumentException for an
            // unknown animation name instead of no-oping — and since that call happens partway
            // through building the queue (and before the lockMovement restraint is added), an
            // uncaught throw here doesn't just skip this pipeline: it propagates out of whatever
            // OnSceneLoaded/OnFrame handler called us, potentially skipping every mod callback
            // still queued after it (clothing re-apply, shop/vending setup, etc.). Validate every
            // name against the skeleton up front so a missing/renamed animation just gets skipped
            // and logged, never crashes the caller.
            var skeletonData = skeletonAnim.Skeleton.Data;
            var validSteps = new List<AnimationPipelineStep>(steps.Count);
            foreach (var step in steps)
            {
                if (skeletonData.FindAnimation(step.AnimationName) != null)
                {
                    validSteps.Add(step);
                }
                else
                {
                    Debug.LogError($"[NeonNightSDK.Animations] PlayAnimationPipeline: animação '{step.AnimationName}' não existe neste skeleton, pulando step.");
                }
            }

            if (!string.IsNullOrEmpty(returnToAnimationName) && skeletonData.FindAnimation(returnToAnimationName) == null)
            {
                Debug.LogError($"[NeonNightSDK.Animations] PlayAnimationPipeline: returnToAnimationName '{returnToAnimationName}' não existe neste skeleton, ignorando retorno.");
                returnToAnimationName = null;
            }

            if (validSteps.Count == 0 && string.IsNullOrEmpty(returnToAnimationName) && !returnToPreviousAnimation)
            {
                Debug.LogError("[NeonNightSDK.Animations] PlayAnimationPipeline: nenhum step válido e nenhum retorno definido, abortando.");
                return;
            }

            var state = skeletonAnim.AnimationState;

            string previousName = null;
            var previousLoop = false;
            if (returnToPreviousAnimation)
            {
                var currentEntry = state.GetCurrent(trackIndex);
                if (currentEntry != null)
                {
                    previousName = currentEntry.Animation.Name;
                    previousLoop = currentEntry.Loop;
                }
            }

            TrackEntry lastEntry;
            var startIndex = 0;
            // Delay pro PRÓXIMO AddAnimation — vem do DurationOverride do step que acabou de ser
            // enfileirado (null = 0f = deixa o Spine calcular automaticamente pela duração nativa
            // do clipe, comportamento antigo inalterado). Ver DurationOverride em AnimationPipelineStep.
            var nextDelay = 0f;
            if (validSteps.Count > 0)
            {
                lastEntry = state.SetAnimation(trackIndex, validSteps[0].AnimationName, validSteps[0].Loop);
                nextDelay = validSteps[0].DurationOverride ?? 0f;
                startIndex = 1;
            }
            else if (!string.IsNullOrEmpty(returnToAnimationName))
            {
                lastEntry = state.SetAnimation(trackIndex, returnToAnimationName, true);
                returnToAnimationName = null; // already played as the only entry, don't queue it again below
            }
            else
            {
                lastEntry = state.SetAnimation(trackIndex, previousName, previousLoop);
            }

            for (var i = startIndex; i < validSteps.Count; i++)
            {
                lastEntry = state.AddAnimation(trackIndex, validSteps[i].AnimationName, validSteps[i].Loop, nextDelay);
                nextDelay = validSteps[i].DurationOverride ?? 0f;
            }

            if (!string.IsNullOrEmpty(returnToAnimationName))
            {
                lastEntry = state.AddAnimation(trackIndex, returnToAnimationName, true, nextDelay);
            }
            else if (returnToPreviousAnimation && previousName != null)
            {
                lastEntry = state.AddAnimation(trackIndex, previousName, previousLoop, nextDelay);
            }

            if (lockMovementFor != null)
            {
                var restraintId = $"NeonNightSDK.AnimationPipeline.track{trackIndex}";
                SetMovementRestraint(lockMovementFor, restraintId, true);
                lastEntry.Start += _ => SetMovementRestraint(lockMovementFor, restraintId, false);
            }

            Debug.Log($"[NeonNightSDK.Animations] PlayAnimationPipeline: {validSteps.Count}/{steps.Count} step(s) válido(s) na track {trackIndex}" +
                      (returnToAnimationName != null ? $", volta pra '{returnToAnimationName}'" :
                       returnToPreviousAnimation ? $", volta pra anterior ('{previousName ?? "nenhuma"}')" : ", sem retorno") +
                      (lockMovementFor != null ? ", movimento travado até o fim" : "") + ".");
        }

        private static void SetMovementRestraint(Asuna.CharManagement.Character character, string restraintId, bool add)
        {
            if (character?.Handlers == null) return;

            foreach (var handler in character.Handlers)
            {
                var controller = handler.Controller;
                if (controller == null) continue;

                if (add) controller.AddInputRestraint(restraintId);
                else controller.RemoveInputRestraint(restraintId);
            }
        }

        // Convenience wrapper: mesmo padrão do resto do kit — percorre os Handlers
        // do Character e chama PlayAnimationPipeline em cada SkeletonAnimation.
        // lockMovement=true trava o input do personagem durante o pipeline inteiro
        // (ver comentário em PlayAnimationPipeline).
        public static void PlayAnimationPipelineForCharacter(
            Asuna.CharManagement.Character character,
            int trackIndex,
            List<AnimationPipelineStep> steps,
            string returnToAnimationName = null,
            bool returnToPreviousAnimation = false,
            bool lockMovement = false)
        {
            foreach (var skeletonAnim in CharacterSkeletons.GetAll(character))
            {
                PlayAnimationPipeline(skeletonAnim, trackIndex, steps, returnToAnimationName, returnToPreviousAnimation,
                    lockMovement ? character : null);
            }
        }
    }
}
