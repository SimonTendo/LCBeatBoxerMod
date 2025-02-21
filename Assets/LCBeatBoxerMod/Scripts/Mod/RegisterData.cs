using UnityEngine;
using BepInEx.Logging;

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
                if (rarityData.levelID == level.levelID)
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
        for (int i = 0; i < Plugin.allAssets.allBestiaryPages.Length; i++)
        {
            TerminalNode file = Plugin.allAssets.allBestiaryPages[i];
            int thisEnemyID = terminalScript.enemyFiles.Count;
            Logger.LogDebug($"Setting up enemy file {file} with ID: [{thisEnemyID}]");
            terminalScript.enemyFiles.Add(file);
            file.creatureFileID = thisEnemyID;
            GameObject creaturePrefab = null;
            for (int j = 0; j < Plugin.allAssets.allEnemies.Length; j++)
            {
                EnemyType enemy = Plugin.allAssets.allEnemies[j];
                if (enemy.enemyName.Contains(file.creatureName))
                {
                    creaturePrefab = enemy.enemyPrefab;
                }
            }
            if (creaturePrefab != null)
            {
                ScanNodeProperties scanNode = creaturePrefab.GetComponentInChildren<ScanNodeProperties>();
                if (scanNode != null)
                {
                    scanNode.creatureScanID = thisEnemyID;
                    Logger.LogDebug($"set creatureScanID for {creaturePrefab.name} to {scanNode.creatureScanID}");
                }
            }
            else
            {
                Logger.LogWarning($"failed to find prefab ({creaturePrefab}) or scan node for {file.creatureName}");
            }
        }
    }
}
