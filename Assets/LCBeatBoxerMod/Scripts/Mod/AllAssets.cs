using UnityEngine;

[CreateAssetMenu(menuName = "LCBeatBoxerMod/AllAssets")]
public class AllAssets : ScriptableObject
{
    public EnemyType[] allEnemies;

    [Space(3f)]
    public LevelWithRarity[] allLevelRarities;

    [Space(3f)]
    public Item[] allItems;

    [Space(3f)]
    public TerminalNode[] allBestiaryPages;

    [Space(3f)]
    public TerminalKeyword[] allKeywords;

    [Space(3f)]
    public GameObject[] allNetworkPrefabs;
}
