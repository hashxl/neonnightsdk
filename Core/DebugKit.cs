using System.Collections.Generic;
using System.Text;
using ANToolkit.Debugging;
using Asuna.CharManagement;
using UnityEngine;

namespace NeonNightSDK.Core
{
    // Diagnostic console commands — the "Ferramentas de Diagnóstico" item from the SDK roadmap
    // (nn-sdk.md): eliminate the need for a modder's own personal tooling to answer basic
    // questions the SDK itself can answer. First one: what Item.All KEY does an item I'm
    // carrying actually have? Item.All is keyed by the ScriptableObject's `name.ToLower()` (see
    // Asuna.Items.Item.InitializeBaseItems), which does not always match the display name or its
    // casing — guessing it wrong (as happened with "Web_Camera_Phone") fails silently at
    // Item.All[key] lookups.
    internal static class DebugKit
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;

            ConCommand.Add("nnsdk.dump.inventory", DumpInventory);
        }

        // nnsdk.dump.inventory [nome do personagem]
        // Sem argumento, usa o jogador. Lista Inventory + EquippedItems (Character.AllItemsList)
        // porque uma arma ou roupa equipada some de Inventory sozinho enquanto está em uso.
        private static void DumpInventory(List<string> args)
        {
            var character = ResolveCharacter(args);
            if (character == null)
            {
                Debug.LogWarning("[NeonNightSDK] nnsdk.dump.inventory: personagem não encontrado " +
                                  "(jogador indisponível e nenhum nome válido foi passado).");
                return;
            }

            var items = character.AllItemsList;
            var text = new StringBuilder();
            text.AppendLine($"{items.Count} item(ns) — {character.Name}\n");

            foreach (var item in items)
                text.AppendLine($"{item.name.ToLower()}   (\"{item.Name}\", {item.GetType().Name})");

            ANToolkit.Debugging.Console.WriteMessage(new ConsoleLog(
                $"nnsdk.dump.inventory — {character.Name}", text.ToString(), LogType.Log));
        }

        private static Character ResolveCharacter(List<string> args)
        {
            if (args != null && args.Count > 0 && !string.IsNullOrEmpty(args[0]))
                return Character.GetByDisplayName(args[0]);

            return PlayerRef.Current;
        }
    }
}
