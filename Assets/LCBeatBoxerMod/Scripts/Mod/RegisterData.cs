using UnityEngine;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;

public class RegisterData
{
    private static ManualLogSource Logger = Plugin.Logger;

    //-1 = Do not add TestEnemy // 0 = Add TestEnemy normally // 2 = Add TestEnemy and reduce chance of all other enemies // 3 = Make TestEnemy only enemy
    public static int debugEnemyCase = -1;
    public static int debugEnemyRarity = 70;

    //-1 = Do not alter other enemies' rarities // Anything else = Other enemy rarity to overwrite
    public static int debugOtherEnemyCase = -1;

    //Set-up for enemies (likely the TestEnemy and BeatBoxer)
    public static void SetUpEnemy(EnemyType enemyType, StartOfRound startOfRound = null)
    {
        if (startOfRound == null)
        {
            startOfRound = StartOfRound.Instance;
        }

        Logger.LogDebug($"Setting up '{enemyType}':");
        AddMapDotMat(enemyType);
        AddInteractHandIcon(enemyType);
        AddEnemyToDebugMenu(enemyType);

        AddDebugEnemyToAllLevels(startOfRound, enemyType);
    }

    public static void AddAllEnemyTypesToLevels()
    {
        for (int i = 0; i < Plugin.allAssets.allLevelRarities.Length; i++)
        {
            LevelWithRarity rarityData = Plugin.allAssets.allLevelRarities[i];
            Logger.LogDebug($"setting up for {rarityData}");
            for (int j = 0; j < StartOfRound.Instance.levels.Length; j++)
            {
                SelectableLevel level = StartOfRound.Instance.levels[j];
                if ((rarityData.levelID == -1 && level.levelID > 12) || rarityData.levelID == level.levelID)
                {
                    RegisterEnemyToLevel(rarityData, level);
                }
            }
        }
    }

    public static void RegisterEnemyToLevel(LevelWithRarity rarityData, SelectableLevel level)
    {
        foreach (SpawnableEnemyWithRarity enemy in rarityData.enemiesWithRarities)
        {
            if (Configs.overrideRarityAll.Value >= 1)
            {
                enemy.rarity = Configs.overrideRarityAll.Value;
            }
            if (enemy.enemyType.isOutsideEnemy)
            {
                Logger.LogDebug($"adding OUTside {enemy.enemyType.enemyName} to {level} #{level.levelID} with rarity {enemy.rarity}");
                level.OutsideEnemies.Add(enemy);
            }
            else
            {
                Logger.LogDebug($"adding INside {enemy.enemyType.enemyName} to {level} #{level.levelID} with rarity {enemy.rarity}");
                level.Enemies.Add(enemy);
            }
        }
    }

    private static void AddDebugEnemyToAllLevels(StartOfRound startOfRound, EnemyType enemyType)
    {
        if (debugEnemyCase == -1 || enemyType.enemyName != "TestEnemy" || !Application.isEditor) return;
        Logger.LogInfo($"AddDebugEnemyToAllLevels(): debugEnemyCase = {debugEnemyCase}");
        switch (debugEnemyCase)
        {
            case 1:
                foreach (SelectableLevel level in startOfRound.levels)
                {
                    AddEnemyToLevel(enemyType, level, debugEnemyRarity);
                }
                return;
            case 2:
                foreach (SelectableLevel level in startOfRound.levels)
                {
                    foreach (SpawnableEnemyWithRarity enemy in level.Enemies)
                    {
                        enemy.rarity = 1;
                    }
                    AddEnemyToLevel(enemyType, level, debugEnemyRarity);
                }
                return;
            case 3:
                foreach (SelectableLevel level in startOfRound.levels)
                {
                    level.Enemies.Clear();
                    AddEnemyToLevel(enemyType, level, debugEnemyRarity);
                }
                return;
        }
    }

    private static void AddEnemyToDebugMenu(EnemyType enemyToAdd)
    {
        if (enemyToAdd == null)
        {
            Logger.LogWarning("enemyToAdd given to AddEnemyToDebugMenu() is null!");
            return;
        }
        Logger.LogDebug($"starting AddEnemyToDebugMenu({enemyToAdd.name})");
        QuickMenuManager debugMenu = Object.FindAnyObjectByType<QuickMenuManager>();
        if (debugMenu == null)
        {
            Logger.LogWarning("failed to find debugMenu, try later");
            return;
        }
        SpawnableEnemyWithRarity testEnemyWithRarity = new SpawnableEnemyWithRarity();
        testEnemyWithRarity.enemyType = enemyToAdd;
        testEnemyWithRarity.rarity = 0;
        if (enemyToAdd.isOutsideEnemy)
        {
            debugMenu.testAllEnemiesLevel.OutsideEnemies.Add(testEnemyWithRarity);
        }
        else
        {
            debugMenu.testAllEnemiesLevel.Enemies.Add(testEnemyWithRarity);
        }
    }

    public static void AddEnemyToLevel(EnemyType enemyToAdd, SelectableLevel levelToAddTo, int rarityOnLevel)
    {
        if (enemyToAdd == null)
        {
            Logger.LogWarning("enemyToAdd given to AddEnemyToLevel() is null!");
            return;
        }
        if (levelToAddTo == null)
        {
            Logger.LogWarning("levelToAddTo given to AddEnemyToLevel() is null!");
            return;
        }
        if (rarityOnLevel <= 0)
        {
            Logger.LogWarning("rarityOnLevel given to AddEnemyToLevel() is too low!");
            return;
        }
        Logger.LogInfo($"starting AddEnemyToLevel({enemyToAdd.name}, {levelToAddTo.name}, {rarityOnLevel})");
        SpawnableEnemyWithRarity enemyWithRarity = new SpawnableEnemyWithRarity();
        enemyWithRarity.enemyType = enemyToAdd;
        enemyWithRarity.rarity = rarityOnLevel;
        levelToAddTo.Enemies.Add(enemyWithRarity);
        Logger.LogDebug("successfully reached end!");
    }

    private static void AddMapDotMat(EnemyType enemyType)
    {
        if (enemyType == null || enemyType.enemyPrefab == null)
        {
            Logger.LogWarning("AddMapDotMat() called with null enemyType");
            return;
        }
        Logger.LogDebug($"starting AddMapDotMat({enemyType.name})");
        Material materialToUse = AssetsCollection.mapDotRedMat != null ? AssetsCollection.mapDotRedMat : AssetsCollection.backUpMapDotMat;
        Transform mapDot = enemyType.enemyPrefab.transform.Find("MapDot");
        if (mapDot == null || mapDot.gameObject.GetComponent<MeshRenderer>() == null)
        {
            Logger.LogError($"Add a child called 'MapDot' with MeshRenderer to {enemyType.enemyPrefab}");
            return;
        }
        mapDot.gameObject.SetActive(true);
        mapDot.gameObject.GetComponent<MeshRenderer>().material = materialToUse;
    }

    private static void AddInteractHandIcon(EnemyType enemyType)
    {
        if (AssetsCollection.handIcon == null)
        {
            Logger.LogDebug("AssetsCollection does not have handIcon");
            return;
        }
        if (enemyType == null || enemyType.enemyPrefab == null || enemyType.enemyPrefab.GetComponentInChildren<InteractTrigger>() == null)
        {
            Logger.LogWarning("AddInterHandIcon() called with null enemyType");
            return;
        }
        Logger.LogDebug($"starting AddInteractHandIcon({enemyType.name})");
        InteractTrigger[] allInteracts = enemyType.enemyPrefab.GetComponentsInChildren<InteractTrigger>();
        foreach (InteractTrigger interact in allInteracts)
        {
            interact.hoverIcon = AssetsCollection.handIcon;
        }
    }



    //Set-up for items (likely fake footstep audio sources)
    public static void SetUpItem(Item item, StartOfRound startOfRound = null)
    {
        if (item == null)
        {
            Logger.LogWarning("item given to SetUpItem() is null!");
            return;
        }
        if (startOfRound == null)
        {
            if (StartOfRound.Instance == null)
            {
                Logger.LogWarning("startOfRound and StartOfRound.Instance given to SetUpItem() are both null!");
                return;
            }
            startOfRound = StartOfRound.Instance;
        }
        AddItemToItemsList(item, startOfRound);
    }

    private static void AddItemToItemsList(Item item, StartOfRound __instance)
    {
        if (__instance.allItemsList == null || __instance.allItemsList.itemsList == null)
        {
            Logger.LogWarning("allItemsList or itemsList in StartOfRound.Instance is null!");
            return;
        }

        item.itemIcon = AssetsCollection.scrapIcon;
        item.grabSFX = AssetsCollection.grabSFX;
        item.dropSFX = AssetsCollection.dropSFX;

        Logger.LogDebug($"adding item {item.itemName} to StartOfRound.Instance.allItemsList.itemsList");
        __instance.allItemsList.itemsList.Add(item);
    }

    public static void AddEnemyFilesToTerminal(Terminal terminalScript)
    {
        TerminalKeyword infoKeyword = GetInfoKeyword(terminalScript.terminalNodes.allKeywords);
        List<CompatibleNoun> infoNouns = null;
        if (infoKeyword != null)
        {
            infoNouns = infoKeyword.compatibleNouns.ToList();
        }
        for (int i = 0; i < Plugin.allAssets.allBestiaryPages.Length; i++)
        {
            TerminalNode enemyFile = Plugin.allAssets.allBestiaryPages[i];
            int thisEnemyID = terminalScript.enemyFiles.Count;
            Logger.LogDebug($"Setting up enemy file {enemyFile} with ID: [{thisEnemyID}]");
            terminalScript.enemyFiles.Add(enemyFile);
            enemyFile.creatureFileID = thisEnemyID;
            GameObject enemyObject = null;
            for (int j = 0; j < Plugin.allAssets.allEnemies.Length; j++)
            {
                EnemyType enemyType = Plugin.allAssets.allEnemies[j];
                if (enemyType.enemyName.Contains(enemyFile.creatureName))
                {
                    enemyObject = enemyType.enemyPrefab;
                }
            }
            if (enemyObject != null)
            {
                ScanNodeProperties scanNode = enemyObject.GetComponentInChildren<ScanNodeProperties>();
                if (scanNode != null)
                {
                    scanNode.creatureScanID = thisEnemyID;
                    Logger.LogDebug($"set creatureScanID for {enemyObject.name} to {scanNode.creatureScanID}");
                }
            }
            else
            {
                Logger.LogWarning($"failed to find prefab ({enemyObject}) or scan node for {enemyFile.creatureName}");
            }
            infoNouns?.Add(SetEnemyInfoCnoun(enemyFile, infoKeyword));
        }
        if (infoNouns != null)
        {
            infoKeyword.compatibleNouns = infoNouns.ToArray();
        }
    }

    private static TerminalKeyword GetInfoKeyword(TerminalKeyword[] allKeywords)
    {
        foreach (TerminalKeyword keyword in allKeywords)
        {
            if (keyword.word == "info")
            {
                Logger.LogDebug($"found {keyword}");
                return keyword;
            }
        }
        Logger.LogWarning("failed to find TerminalKeyword 'info', bestiary files cannot be accessed using this keyword");
        return null;
    }

    private static CompatibleNoun SetEnemyInfoCnoun(TerminalNode enemyFile, TerminalKeyword infoVerb)
    {
        CompatibleNoun cNoun = new CompatibleNoun();
        cNoun.result = enemyFile;
        TerminalKeyword[] allKeywords = Plugin.allAssets.allKeywords;
        foreach (TerminalKeyword terminalKeyword in allKeywords)
        {
            if (terminalKeyword.specialKeywordResult == enemyFile)
            {
                terminalKeyword.defaultVerb = infoVerb;
                cNoun.noun = terminalKeyword;
                Logger.LogDebug($"successfully found keyword '{terminalKeyword}' with defaultVerb '{terminalKeyword.defaultVerb}' to link to node '{enemyFile}'");
            }
        }
        if (cNoun.noun == null)
        {
            Logger.LogWarning($"SetEnemyInfoCnoun({enemyFile}, {infoVerb}) did not find keyword for noun");
        }
        Logger.LogDebug($"Created new CompatibleNoun with result '{cNoun.result}' and noun '{cNoun.noun}'");
        return cNoun;
    }
}
