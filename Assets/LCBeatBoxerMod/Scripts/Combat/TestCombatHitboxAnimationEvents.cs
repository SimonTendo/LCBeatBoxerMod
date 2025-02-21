using UnityEngine;

public class TestCombatHitboxAnimationEvents : MonoBehaviour
{
    public TestEnemyScript mainScript;
    public int currentHitboxIndex;
    public TestCombatHitbox[] allHitboxes;

    //Go directly to EnemyAI
    public void SetEnemyVulnerable(int setVulnerableTo)
    {
        mainScript.SetEnemyVulnerable(Plugin.ConvertToBool(setVulnerableTo));
    }

    public void SetEnemyInAttackAnimTo(int setEnemyInAttackAnimTo)
    {
        mainScript.SetEnemyInAttackAnimation(Plugin.ConvertToBool(setEnemyInAttackAnimTo));
    }

    public void PlayAnimSFXOnEnemy(int playCaseSFX)
    {
        mainScript.PlayAnimSFX(playCaseSFX);
    }



    //Go to Hitbox
    public void ClearHitPlayerIDs()
    {
        allHitboxes[currentHitboxIndex].ClearHitPlayerIDs();
    }

    public void CheckAttackMiss()
    {
        allHitboxes[currentHitboxIndex].CheckAttackMiss();
    }
}
