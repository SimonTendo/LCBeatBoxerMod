using UnityEngine;

[CreateAssetMenu(menuName = "LCBeatBoxerMod/DataContainers/LevelWithRarity")]
public class LevelWithRarity : ScriptableObject
{
    public int levelID;

    public SpawnableEnemyWithRarity[] enemiesWithRarities;
}
