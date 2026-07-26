# NeonNightSDK — Animações (`NeonNightSDK.Animations.AnimationsKit`)

Duas capacidades separadas, as duas injetando dados direto no `SkeletonData`
em runtime (mesma ideia do `ClothingKit`: `Animation`/`Timeline` são só dados,
não precisam do projeto `.spine` original):

1. **Criar uma animação nova do zero** — girar um osso ao longo do tempo
   (acenar, levantar a mão, balançar a cabeça), definida em código ou num JSON.
2. **Encadear animações que já existem no jogo** num "pipeline" — toca uma
   atrás da outra, e no final volta pro estado anterior ou vai pra um estado
   fixo. Estilo mod de animação de Skyrim (SKSE/FNIS): idle → pipeline → algo
   novo → volta pro idle (ou fica no novo estado, sua escolha).

Todo o código está em [`Animations/AnimationsKit.cs`](../Animations/AnimationsKit.cs).

---

## Índice

1. [Limitação de escopo](#1-limitação-de-escopo)
2. [Criar uma animação nova (rotação de osso)](#2-criar-uma-animação-nova-rotação-de-osso)
3. [Schema do JSON](#3-schema-do-json)
4. [Pipeline de animações existentes](#4-pipeline-de-animações-existentes)
5. [Como descobrir nomes de osso/animação que já existem](#5-como-descobrir-nomes-de-ossoanimação-que-já-existem)
6. [Referência rápida de API](#6-referência-rápida-de-api)

---

## 1. Limitação de escopo

Só **`RotateTimeline`** (osso girando inteiro em torno do próprio pivô).
Isso já cobre qualquer gesto onde um osso rígido gira: mão, antebraço,
cabeça, dedo, etc.

**Não cobre** (ainda):
- Translação/escala de osso (`TranslateTimeline`/`ScaleTimeline`) — a ideia é
  a mesma, só não foi implementado.
- Troca de attachment por frame, tipo flipbook (`AttachmentTimeline`).
- Deformação de malha por vértice (`DeformTimeline`) — isso só o `.spine`
  original resolve, pois exige os pesos de vértice autorados no editor.

---

## 2. Criar uma animação nova (rotação de osso)

```csharp
using NeonNightSDK.Animations;

var tracks = new List<BoneTrackDto>
{
    new BoneTrackDto
    {
        bone = "Fhand",
        keyframes = new List<AnimationKeyframeDto>
        {
            new AnimationKeyframeDto { time = 0.0f, angle = 0f },
            new AnimationKeyframeDto { time = 0.5f, angle = 90f },
            new AnimationKeyframeDto { time = 1.0f, angle = 0f },
        }
    }
};

AnimationsKit.RegisterBoneRotationAnimation(skeletonData, "modded/raise_hand", tracks);
```

`angle` é o ângulo **final desejado**, no espaço local do próprio osso — o
mesmo valor que você leria olhando o bone no editor/calibrador. Por baixo,
`RotateTimeline` do Spine guarda um **delta relativo à rotação de setup** do
osso (`BoneData.Rotation`), não um valor absoluto — `RegisterBoneRotationAnimation`
já faz essa subtração (`angle - boneData.Rotation`) sozinho, então você nunca
precisa pensar nisso.

Idempotente: no-op (retorna `false`) se já existir uma `Animation` com esse
nome — seguro chamar toda scene load.

Pra tocar, é o `AnimationState` normal do Spine:

```csharp
skeletonAnim.AnimationState.SetAnimation(0, "modded/raise_hand", false);
```

---

## 3. Schema do JSON

Em vez de montar a lista de `BoneTrackDto` na mão, dá pra carregar de um
arquivo `.json` dentro da pasta do seu mod:

```json
{
  "name": "modded/raise_hand",
  "tracks": [
    {
      "bone": "Fhand",
      "keyframes": [
        { "time": 0.0, "angle": 0 },
        { "time": 0.5, "angle": 90 },
        { "time": 1.0, "angle": 0 }
      ]
    }
  ]
}
```

```csharp
AnimationsKit.RegisterBoneRotationAnimationFromJson(
    skeletonData, manifest, "Assets\\Animations\\raise_hand.json");
```

`relativePath` é relativo à pasta do mod (`manifest.ModPath`), igual o
`manifest.SpriteResolver.Resolve(...)` usado pro resto do SDK — não usa o
`SpriteResolver` porque não é uma imagem, é lido direto com `File.ReadAllText`.

Pode ter **várias trilhas** (vários ossos) no mesmo JSON — útil pra animar o
braço inteiro (ombro + cotovelo + mão) em vez de só o pulso, por exemplo.

Exemplo real em [`Mods/TestMod/Assets/Animations/raise_hand.json`](../../TestMod/Assets/Animations/raise_hand.json).

---

## 4. Pipeline de animações existentes

`PlayAnimationPipeline` encadeia **qualquer animação que já existe no
esqueleto** (as centenas de `actions/*`, `general/*`, `expressions/*` que já
vêm com o jogo) ou uma sua registrada via `RegisterBoneRotationAnimation` —
funciona igual, é só o nome.

```csharp
using NeonNightSDK.Animations;

// idle -> cai -> fica deitada -> volta pro idle
AnimationsKit.PlayAnimationPipeline(skeletonAnim, 0, new List<AnimationPipelineStep>
{
    new AnimationPipelineStep("actions/faint/faint_fall"),
    new AnimationPipelineStep("actions/faint/faint_lying"),
}, returnToAnimationName: "general/idle/idle");
```

Cada passo toca **até o fim**, depois o próximo começa — usa o mecanismo
nativo do Spine (`AnimationState.SetAnimation` pro primeiro passo,
`AnimationState.AddAnimation` enfileirando os demais), não um timer/coroutine
nosso.

### O que acontece no final do pipeline

Escolha **um** dos dois (ou nenhum):

| Parâmetro | Comportamento |
|---|---|
| `returnToAnimationName = "general/idle/idle"` | Sempre volta pra essa animação específica, não importa o que tava tocando antes. Bom pra eventos pontuais (cair e levantar, tropeçar). |
| `returnToPreviousAnimation = true` | Memoriza o que tava tocando **antes** de chamar o pipeline (via `AnimationState.GetCurrent`) e volta pra aquilo. Bom quando você não sabe de antemão se o personagem tava parado, andando, etc. |
| nenhum dos dois | Fica parado no último passo — some com `Loop=false`, ou repete pra sempre com `Loop=true`. É o caso de "agora ele fica andando/engatinhando de outro jeito, ponto final". |

### Duração customizada por step

Por padrão cada step toca até o clipe nativo terminar um ciclo (o Spine calcula
isso sozinho). Pra forçar um step a durar um número específico de segundos
(cortar um clipe mais curto do que o normal, ou esticar um `Loop=true` por
mais de um ciclo antes de trocar), passe `durationOverride`:

```csharp
new AnimationPipelineStep("general/idle/idle_drunk", loop: true, durationOverride: 5f)
```

Confirmado por reflection em `spine-csharp.dll` (`Spine.AnimationState.Update`):
a troca pro próximo `TrackEntry` da fila acontece quando o tempo decorrido do
step atual alcança o `delay` que foi passado no `AddAnimation` do PRÓXIMO
step — não quando o clipe "acaba" de verdade (isso só é usado se `delay <= 0`,
caso em que o Spine calcula automaticamente via `TrackEntry.TrackComplete`,
que é o comportamento padrão sem `durationOverride`). `PlayAnimationPipeline`
usa o `DurationOverride` do step anterior como esse `delay` — por isso o valor
fica no step que você quer encurtar/esticar, não no que vem depois.

Vale só pra steps com algo depois na fila (outro step, ou um retorno via
`returnToAnimationName`/`returnToPreviousAnimation`) — num step que é
literalmente o último de tudo, sem retorno nenhum, não tem `next` TrackEntry
pra amarrar o delay, então `durationOverride` nesse caso específico é ignorado.

### ⚠️ `Loop=true` no MEIO do pipeline não faz o que parece

Um passo com `Loop=true` que **não é o último** só toca **um ciclo** antes do
próximo passo começar — é assim que `AnimationState.AddAnimation` calcula o
delay (baseado na duração de UM ciclo do passo anterior, mesmo que ele
tecnicamente dê loop pra sempre se ficasse sozinho). `Loop` só importa de
verdade no **último** passo, ou no passo de retorno (`returnToAnimationName`/
`returnToPreviousAnimation`, que já são sempre registrados com loop).

### Wrapper de conveniência

```csharp
AnimationsKit.PlayAnimationPipelineForCharacter(zoey, 0, steps, returnToAnimationName: "general/idle/idle");
```

Mesmo padrão do resto do SDK: percorre `character.Handlers` e chama
`PlayAnimationPipeline` em cada `SkeletonAnimation`.

### Travar o movimento durante o pipeline

Por padrão, o personagem continua respondendo ao input de movimento normal
**por cima** da animação — se o pipeline for uma queda, por exemplo, o
jogador ainda consegue andar enquanto a animação de cair/deitar toca. Pra
evitar isso, passe `lockMovement: true` (wrapper) ou um `Character` em
`lockMovementFor` (versão sem wrapper):

```csharp
AnimationsKit.PlayAnimationPipelineForCharacter(
    zoey, 0, steps, returnToAnimationName: "general/idle/idle", lockMovement: true);
```

Por baixo, isso usa o mesmo sistema de restrição de input que o próprio jogo
usa em outros lugares (`ANToolkit.Controllers.CharController.AddInputRestraint`/
`RemoveInputRestraint` — visto em `Player.log` como `"CharController: Removing
Input Restraint: PMA_PathfindingMoveTo"`, id do restraint da IA de
pathfinding): a restrição é adicionada assim que o pipeline começa e removida
automaticamente quando a **última** entrada da fila (o passo de retorno, ou o
último step se não houver retorno) começa a tocar de verdade — via
`Spine.TrackEntry.Start`, que só dispara quando tudo que veio antes na fila já
terminou.

Se o pipeline termina numa animação com `Loop=true` e sem retorno (ex: "agora
ele fica engatinhando"), o movimento é destravado nesse momento — faz sentido,
já que presumivelmente esse novo estado (engatinhar) tem seu próprio jeito de
responder a input.

### Nome de animação inválido não derruba mais o caller

Antes, um nome que não existe nesse skeleton (`AnimationState.SetAnimation`/
`AddAnimation` do Spine) lançava `ArgumentException` direto — e como isso
acontecia no meio de `PlayAnimationPipeline`, a exceção subia sem tratamento
até quem chamou (`OnFrame`/`OnSceneLoaded` do mod), potencialmente pulando
qualquer outro callback enfileirado depois na mesma cadeia (ex: um mod que
reaplica roupas modded a cada troca de cena, chamado depois de um pipeline
que crashou, simplesmente não rodava naquele frame).

Agora `PlayAnimationPipeline` valida cada nome via `skeletonData.FindAnimation(...)`
**antes** de tocar em `AnimationState`: um step inválido é descartado com
`Debug.LogError` (o resto do pipeline continua normalmente); se
`returnToAnimationName` for inválido, é ignorado do mesmo jeito. Só aborta de
verdade (sem tocar nada, sem travar movimento) se sobrar zero steps válidos e
nenhum retorno. Ou seja: um nome de animação chutado errado agora vira um log
de erro, não um crash — mas ainda vale conferir o `Debug.LogError` no console,
porque o step simplesmente não vai tocar.

---

## 5. Como descobrir nomes de osso/animação que já existem

Osso: qualquer nome usado como `bone=` no botão **"Ver peças desta skin"** do
calibrador web (`C:\Users\murillo\Documents\TC-spine\index.html`).

Animação: o calibrador não lista animações por padrão (só skins/slots), mas
dá pra extrair via o mesmo esqueleto real, headless, sem precisar abrir o
jogo — foi assim que confirmamos `actions/faint/faint_fall` (1.33s),
`actions/faint/faint_lying` (3s) e `general/idle/idle` (3s) pra este doc:
`skeletonData.animations.forEach(a => ...)` no `dump.html` que já existe na
mesma pasta do calibrador.

---

## 6. Referência rápida de API

| Método | Uso |
|---|---|
| `RegisterBoneRotationAnimation(skeletonData, name, tracks)` | Cria uma `Animation` nova (rotação de osso) a partir de uma lista de `BoneTrackDto`. Idempotente. |
| `RegisterBoneRotationAnimationFromJson(skeletonData, manifest, relativePath)` | Mesmo acima, lendo de um `.json` na pasta do mod. |
| `RegisterBoneRotationAnimationForCharacter(character, name, tracks)` | Wrapper: registra em todo `SkeletonAnimation` dos `Handlers` do personagem. |
| `PlayAnimationPipeline(skeletonAnim, track, steps, returnToAnimationName?, returnToPreviousAnimation?, lockMovementFor?)` | Toca uma sequência de animações (novas ou já existentes no jogo) e opcionalmente volta pro estado anterior/um estado fixo no final. `lockMovementFor` trava o input de movimento do `Character` até a última entrada da fila começar. |
| `PlayAnimationPipelineForCharacter(character, track, steps, ..., lockMovement?)` | Mesmo acima, wrapper por `Handlers`. |
| `AnimationPipelineStep(name, loop?, durationOverride?)` | `durationOverride` (segundos) força quanto tempo esse step toca antes do próximo (ou do retorno) assumir — ver [seção 4](#duração-customizada-por-step). `null` = duração nativa do clipe (padrão). |

Assinaturas do `spine-csharp.dll` usadas por baixo (confirmadas por reflection
antes de escrever qualquer coisa, mesma disciplina do `ClothingKit`):

```
Animation(string name, ExposedList<Timeline> timelines, float duration)
RotateTimeline(int frameCount, int bezierCount, int boneIndex)
CurveTimeline1.SetFrame(int frame, float time, float value)
CurveTimeline.SetLinear(int frame)
AnimationState.SetAnimation(int track, string name, bool loop) -> TrackEntry
AnimationState.AddAnimation(int track, string name, bool loop, float delay) -> TrackEntry
AnimationState.GetCurrent(int track) -> TrackEntry
```
