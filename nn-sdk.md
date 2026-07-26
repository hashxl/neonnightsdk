# Roadmap do NeonNightSDK

## Prioridade 1 — Fundação do SDK

### 1. Core — Eventos, Contexto e Scheduler

É o componente mais importante do SDK e o que mais reduz código repetido.

Atualmente, o `ITCMod` expõe apenas:

- `OnModLoaded`
- `OnFrame`
- `OnModUnLoaded`

Como consequência, cada serviço precisa implementar manualmente:

- flags como `_spawned`;
- verificações de cena (`if (scene.name == "MainMenu") return`);
- timers usando `Time.deltaTime`;
- lógica para descobrir quando o jogador está disponível.

### Exemplo

```csharp
NeonNightSDK.Core.SdkEvents.OnSceneReady += scene =>
{
    ...
};

NeonNightSDK.Core.SdkEvents.OnPlayerReady += player =>
{
    ...
};

NeonNightSDK.Core.Scheduler.Every(1f, Tick);

NeonNightSDK.Core.Scheduler.After(4.33f, () =>
{
    Rob(zoey);
});
```

### Benefícios

- Elimina dezenas de timers manuais espalhados pelo código.
- Centraliza o ciclo de vida do jogo.
- Resolve automaticamente quando o jogador está pronto.
- Evita verificações repetidas de cena.
- Executa callbacks em `try/catch`, impedindo que um mod com erro interrompa os demais.

Esse último ponto resolve exatamente o problema comentado em `AnimationsKit.cs`, onde uma exceção em um callback pode interromper toda a fila de execução.

---

## 2. WorldKit — Objetos e Interações

Hoje diversos serviços repetem praticamente o mesmo código para criar:

- `GameObject`
- `SpriteRenderer`
- `Sorting Layer`
- `BoxCollider`
- `Interactable`

Esse bloco aparece em diversos lugares (`ShopService`, `VendingMachineService`, `InfoNpcService`, etc.).

Em vez de cerca de 40 linhas sempre iguais:

```csharp
WorldKit.SpawnInteractable(
    sprite,
    position,
    InteractionType.Talk,
    () => shop.OpenShop());

WorldKit.AttachToExisting(
    "CondomVendingMachine",
    Buy);

WorldKit.CreateTrigger(
    position,
    size,
    onEnter: character =>
    {
        ...
    });
```

### Benefícios

- Menos código duplicado.
- API simples e consistente.
- Criação padronizada de NPCs, máquinas e objetos.

---

## 3. DialogueKit — Builder Fluente

Hoje um simples diálogo de confirmação ocupa dezenas de linhas.

Como praticamente toda mecânica nova utiliza diálogos, faz sentido existir uma API dedicada.

```csharp
DialogueKit
    .Say("Insert 5 fB for a random condom?")
    .Choice("Buy (5 fB)", () =>
    {
        DialogueKit.Pay(
            5,
            zoey,
            onSuccess: Dispense);
    })
    .Choice("No thanks")
    .Show();
```

### Benefícios

- Código muito mais legível.
- Menos boilerplate.
- Facilita a criação de mods focados em conteúdo.

---

## 4. StatsKit — Sistema de Atributos

Grande parte das mecânicas do jogo gira em torno de atributos.

Hoje a lógica para:

- criar um atributo;
- garantir sua existência;
- controlar decaimento;
- executar eventos;

está misturada com regras específicas do mod.

### Exemplo

```csharp
var hunger = StatsKit
    .Define("mod.hunger", "Hunger")
    .Max(100)
    .Color(Color.orange)
    .EnsureOnPlayer()
    .DecayBy(1, everySeconds: 30)
    .OnReachZero(character =>
    {
        ...
    });
```

### Benefícios

- Criação de novas mecânicas em poucas linhas.
- Separação entre infraestrutura e regra de negócio.
- API reutilizável para qualquer atributo.

---

## 5. SaveKit — Persistência

Hoje existe um problema importante: atributos customizados não são persistidos.

Ao reiniciar o jogo, todo o progresso é perdido.

Como o `SaveManager` aceita apenas `bool`, `int` e `string`, o SDK pode encapsular automaticamente serialização JSON e namespace por mod.

### Exemplo

```csharp
var save = SaveKit.For(manifest);

save.Set("needs", needsState);

var state = save.Get<NeedsState>("needs");
```

Internamente:

```text
neonnightsdk.testmod.needs
```

↓

```text
JSON
```

↓

```text
string
```

### Benefícios

- Persistência transparente.
- Isolamento entre mods.
- Suporte a qualquer objeto serializável.

---

# Prioridade 2 — Qualidade de Vida

## PlayerKit

Centralizar operações comuns relacionadas ao jogador.

Exemplos:

- `SetMovementRestraint()`
- `TeleportTo(scene, position)`
- `ApplyEffectToAllLimbs()`

Também encapsular a lógica de transição utilizada atualmente por sistemas como `SleepRobbery`.

---

## ConsoleKit

Simplificar a criação de comandos de console.

```csharp
ConsoleKit.Command("give_money")
    .Usage("<amount>")
    .Execute(args =>
    {
        ...
    });
```

### Benefícios

- Validação automática de parâmetros.
- `usage` gerado automaticamente.
- Encapsula detalhes como o separador por vírgulas.

---

## HudKit

Construção simplificada de HUDs.

Hoje criar um HUD exige dezenas de linhas envolvendo:

- Canvas
- CanvasScaler
- Image
- Text

Uma API como:

```csharp
HudKit.Bar(...)
HudKit.Text(...)
HudKit.Icon(...)
```

reduziria significativamente esse trabalho.

---

## ConfigKit

Cada mod deveria possuir um `config.json` próprio.

O SDK faria automaticamente:

- carregamento;
- validação;
- criação do arquivo;
- salvamento.

Isso permite que jogadores ajustem configurações sem recompilar o mod.

---

# Prioridade 3 — Ecossistema

## Documentação

Hoje existe basicamente apenas `Animations.md`.

Seria interessante adicionar:

- README com Quick Start;
- documentação individual para cada Kit;
- exemplos completos;
- guia de boas práticas.

---

## Template de Mod

Disponibilizar um projeto base contendo:

- `manifest.json`;
- `.csproj`;
- estrutura inicial;
- exemplo funcional.

O objetivo é permitir sair do zero até um mod carregando em poucos minutos.

---

## Ferramentas de Diagnóstico

Adicionar comandos como:

```text
nnsdk.dump.skins

nnsdk.dump.bones

nnsdk.dump.animations
```

Isso elimina dependências de ferramentas pessoais e facilita o trabalho de qualquer modder.

---

## Versionamento

Expor a versão do SDK e fornecer validação automática de compatibilidade.

```csharp
NeonNightSDK.Version
```

Caso um mod exija uma versão mínima, o SDK pode emitir uma mensagem clara ao usuário, evitando exceções como `MissingMethodException`.

---

# Ordem Recomendada de Desenvolvimento

1. Core (Eventos + Scheduler)
2. WorldKit
3. DialogueKit
4. StatsKit
5. SaveKit
6. PlayerKit
7. ConsoleKit
8. HudKit
9. ConfigKit
10. Documentação, Templates e Ferramentas

## Justificativa

O **Core** deve ser desenvolvido primeiro, pois serve de base para praticamente todos os demais módulos. Ele elimina a maior quantidade de boilerplate imediatamente, padroniza o ciclo de vida dos mods e fornece a infraestrutura necessária para que os outros Kits possam ser construídos de forma consistente.

Após isso, **WorldKit** e **DialogueKit** cobrem a maior parte das necessidades de quem deseja criar mods de conteúdo. Em seguida, **StatsKit** e **SaveKit** completam a infraestrutura necessária para mecânicas persistentes, enquanto os demais Kits melhoram significativamente a experiência tanto do desenvolvedor quanto do jogador.