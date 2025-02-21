using BepInEx.Logging;
using BepInEx.Configuration;
using JetBrains.Annotations;

public class Configs
{
    private static ManualLogSource Logger = Plugin.Logger;

    public static ConfigEntry<int> overrideRarityAll;
    public static ConfigEntry<bool> printDebugEnemy;
    public static ConfigEntry<bool> printDebugEnemyAI;

    public Configs(ConfigFile cfg)
    {
        overrideRarityAll = cfg.Bind(
            "Customization",
            "Override Rarity All",
            -1,
            "Set this value to 1 or above to use that value as the rarity of both enemies on all levels, instead of the default values.\nLobby host's values are used."
            );
        printDebugEnemy = cfg.Bind(
            "Debug",
            "Print DebugEnemy",
            true,
            "Set this to true to print information of the more important enemy behaviours to the debug log window."
            );
        printDebugEnemyAI = cfg.Bind(
            "Debug",
            "Print debugEnemyAI",
            false,
            "Set this to true to print information of every little single individual step the enemies perform to the debug log window."
            );
    }

    public static void DisplayConfigs()
    {
        //[Override Rarity All]
        if (overrideRarityAll.Value >= 1)
        {
            Logger.LogInfo($"Config [Override Rarity All] is set to a value of {overrideRarityAll.Value}");
        }

        //[Print debugEnemyAI]
        if (printDebugEnemyAI.Value)
        {
            Logger.LogInfo($"Config [Print debugEnemyAI] is set to TRUE. This mod's enemies will print all their information to this log.");
        }
        else
        {
            //[Print DebugEnemy]
            if (printDebugEnemy.Value)
            {
                Logger.LogInfo($"Config [Print DebugEnemy] is set to TRUE. This mod's enemies will print their important information to this log.");
            }
            else
            {
                Logger.LogInfo($"Config [Print DebugEnemy] is set to FALSE. This mod's enemies won't print their primary debug information.");
            }
        }
    }
}
