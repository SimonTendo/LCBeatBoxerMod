using System.Linq;
using System.Collections.Generic;
using Unity.Netcode;
using BepInEx.Logging;
using HarmonyLib;

public class HarmonyPatches
{
    private static ManualLogSource Logger = Plugin.Logger;

    private static bool alreadySetUp = false;

    [HarmonyPatch(typeof(GameNetworkManager), "Start")]
    public class NewGameNetworkManagerStart
    {
        [HarmonyPostfix]
        public static void Init(GameNetworkManager __instance)
        {
            if (Plugin.allAssets.allNetworkPrefabs == null || Plugin.allAssets.allNetworkPrefabs.Length == 0)
            {
                Logger.LogError("allNetworkPrefabs is not valid, please fix");
                return;
            }
            for (int i = 0; i < Plugin.allAssets.allNetworkPrefabs.Length; i++)
            {
                NetworkManager.Singleton.AddNetworkPrefab(Plugin.allAssets.allNetworkPrefabs[i]);
            }
        }
    }

    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    public class NewStartOfRoundAwake
    {
        [HarmonyPostfix]
        public static void AwakePostfix(StartOfRound __instance)
        {
            if (!alreadySetUp)
            {
                AssetsCollection.GetEnemyAssets(__instance.levels[0].Enemies.ToArray());
                AssetsCollection.GetItemAssets(__instance.allItemsList.itemsList.ToArray());
                AssetsCollection.GetMiscAssets(__instance);

                int debugCase = RegisterData.debugOtherEnemyCase;
                if (debugCase != -1)
                {
                    foreach (SelectableLevel level in __instance.levels)
                    {
                        Logger.LogError($"!!!Overwriting all {level} other enemies' rarities to {debugCase}!!!");
                        foreach (SpawnableEnemyWithRarity otherEnemy in level.Enemies)
                        {
                            otherEnemy.rarity = debugCase;
                        }
                        foreach (SpawnableEnemyWithRarity otherOutsideEnemy in level.OutsideEnemies)
                        {
                            otherOutsideEnemy.rarity = debugCase;
                        }
                    }
                }

                foreach (EnemyType enemy in Plugin.allAssets.allEnemies)
                {
                    RegisterData.SetUpEnemy(enemy, __instance);
                }
                RegisterData.AddAllEnemyTypesToLevels();

                foreach (Item item in Plugin.allAssets.allItems)
                {
                    RegisterData.SetUpItem(item, __instance);
                }
                alreadySetUp = true;
            }
        }
    }

    [HarmonyPatch(typeof(StartOfRound), "openingDoorsSequence")]
    public class NewStartOfroundOpeningDoorsSequence
    {
        [HarmonyPostfix]
        public static void OpeningDoorsSequencePostfix()
        {
            TestEnemyScript.DestroyAllNodeLights();
        }
    }

    [HarmonyPatch(typeof(Terminal), "Awake")]
    public class NewTerminalAwake
    {
        [HarmonyPostfix]
        public static void AwakePostfix(Terminal __instance)
        {
            List<TerminalKeyword> originalKeywords = __instance.terminalNodes.allKeywords.ToList();
            originalKeywords.AddRange(Plugin.allAssets.allKeywords);
            __instance.terminalNodes.allKeywords = originalKeywords.ToArray();
            Logger.LogDebug($"updated allKeywords to Length {__instance.terminalNodes.allKeywords.Length}");
            RegisterData.AddEnemyFilesToTerminal(__instance);
        }
    }
}
