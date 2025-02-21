using UnityEngine;
using BepInEx.Logging;

public class AssetsCollection
{
    private static ManualLogSource Logger = Plugin.Logger;

    public static Item shovelItem;

    public static AudioClip grabSFX;
    public static AudioClip dropSFX;

    public static Sprite scrapIcon;
    public static Sprite handIcon;

    public static Material mapDotRedMat;
    public static Material backUpMapDotMat;

    public static void GetEnemyAssets(SpawnableEnemyWithRarity[] enemies)
    {
        if (enemies == null || enemies.Length == 0)
        {
            Logger.LogWarning("GetEnemyAssets() called with null enemies");
            return;
        }
        foreach (SpawnableEnemyWithRarity enemyWithRarity in enemies)
        {
            if (enemyWithRarity == null || enemyWithRarity.enemyType == null)
            {
                return;
            }
            EnemyType enemy = enemyWithRarity.enemyType;
            if (enemy == null || enemy.enemyName == null || enemy.enemyPrefab == null)
            {
                continue;
            }
            if (enemy.enemyName == "Flowerman")
            {
                Logger.LogDebug("Trying to build MAP DOT MATERIAL");
                GameObject mapDot = enemy.enemyPrefab.transform.Find("MapDot (2)").gameObject;
                if (mapDot == null)
                {
                    Logger.LogDebug("no MapDot found");
                    return;
                }
                MeshRenderer renderer = mapDot.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.material == null || renderer.material.name == null)
                {
                    Logger.LogDebug("no meshRenderer or material found");
                    return;
                }
                string matName = "MapDotRed (Instance)";
                if (renderer.material.name != matName)
                {
                    Logger.LogDebug($"meshRenderer material is not {matName}");
                    return;
                }
                mapDotRedMat = renderer.material;
                Logger.LogDebug($"successfully found {mapDotRedMat.name}");
            }
        }
    }

    public static void GetItemAssets(Item[] itemsList)
    {
        foreach (Item item in itemsList)
        {
            if (item == null)
            {
                continue;
            }
            if (shovelItem == null && item.itemName == "Shovel")
            {
                Logger.LogDebug($"Trying to build shovelItem with prefab {item.spawnPrefab}");
                shovelItem = item;
                continue;
            }
            if (scrapIcon == null && item.itemIcon != null && item.itemIcon.name == "ScrapItemIcon2")
            {
                scrapIcon = item.itemIcon;
                Logger.LogDebug($"successfully found {scrapIcon}");
                continue;
            }
            if (grabSFX == null && item.grabSFX != null && item.grabSFX.name == "ShovelPickUp")
            {
                grabSFX = item.grabSFX;
                Logger.LogDebug($"found {grabSFX}");
                continue;
            }
            if (dropSFX == null && item.dropSFX != null && item.dropSFX.name == "DropPlasticLarge")
            {
                dropSFX = item.dropSFX;
                Logger.LogDebug($"found {dropSFX}");
                continue;
            }
        }
    }

    public static void GetMiscAssets(StartOfRound __instance)
    {
        Logger.LogDebug("Trying to build HAND ICON");
        if (__instance != null && __instance.allPlayerScripts[0] != null && __instance.allPlayerScripts[0].grabItemIcon != null)
        {
            handIcon = __instance.allPlayerScripts[0].grabItemIcon;
        }
    }
}
