using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;
using BepInEx.Logging;

public class HitboxScript : MonoBehaviour
{
    private static ManualLogSource Logger = Plugin.Logger;

    private int logLevel;
    [Header("Manager")]
    public bool isManager;
    public EnemyAI mainScript;
    public HitboxScript[] allHitboxes;
    [Space(3f)]
    [Header("Hitbox")]
    public Collider hitboxCollider;
    public int attackPower;
    public Vector3 attackForce;
    public int deathAnimIndex;
    public HitboxScript managerHitbox;
    private List<int> hitPlayerIDs = new List<int>();

    private void Start()
    {
        logLevel = Plugin.DebugLogLevel();
        if (isManager)
        {
            managerHitbox = this;
            foreach (HitboxScript hitboxScript in allHitboxes)
            {
                hitboxScript.hitboxCollider.enabled = false;
                hitboxScript.managerHitbox = this;
                if (mainScript != null)
                {
                    hitboxScript.mainScript = mainScript;
                }
                else
                {
                    Log($"{hitboxScript.name} started without mainTransform, using own transform to calculate directions and cannot play punch SFX", 2);
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PerformAttackOnHitbox(other);
    }

    private void PerformAttackOnHitbox(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerControllerB hitPlayer = other.GetComponent<PlayerControllerB>();
            if (PerformHitPlayerCheck(hitPlayer) && hitPlayer == GameNetworkManager.Instance.localPlayerController)
            {
                Log($"{name} PerformAttack({other}) // hitPlayer: {hitPlayer} | attackPower {attackPower} | attackForce {attackForce}");
                Transform mainTransform = mainScript != null ? mainScript.transform : transform;
                Vector3 attackForceWithDirection = new Vector3(mainTransform.forward.x * attackForce.z, attackForce.y, mainTransform.forward.z * attackForce.x); 
                if (attackPower > 0)
                {
                    hitPlayer.DamagePlayer(attackPower, true, true, CauseOfDeath.Mauling, deathAnimIndex, false, attackForceWithDirection);
                }
                if (hitPlayer.isPlayerDead)
                {
                    if (mainScript == null)
                    {
                        Log($"mainScript on hitbox {name} is null, cannot call enemy-dependent functionality", 2);
                        return;
                    }
                    if (mainScript is TheBeatAI)
                    {
                        TheBeatAI beatEnemy = mainScript as TheBeatAI;
                    }
                    else if (mainScript is TheBoxerAI)
                    {
                        TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
                        boxerEnemy.PlaySFX(boxerEnemy.punchSFX);
                    }
                }
                else
                {
                    hitPlayer.externalForceAutoFade = attackForceWithDirection * 2;
                }
            }
        }
    }

    private bool PerformHitPlayerCheck(PlayerControllerB hitPlayer)
    {
        if (hitPlayer == null || !hitPlayer.isPlayerControlled)
        {
            return false;
        }
        int playerID = (int)hitPlayer.playerClientId;
        bool flag = !hitPlayerIDs.Contains(playerID);
        if (flag)
        {
            hitPlayerIDs.Add(playerID);
            bool flag2 = !managerHitbox.hitPlayerIDs.Contains(playerID);
            if (flag2)
            {
                managerHitbox.hitPlayerIDs.Add(playerID);
            }
        }
        return flag;
    }

    public void SetEnemyVulnerable(string setVulnerableTo)
    {
        if (mainScript == null)
        {
            Log($"called SetEnemyVulnerable on {name} with null mainScript", 2);
            return;
        }
        LogAI($"{name}: SetEnemyVulnerable({setVulnerableTo}) on {mainScript} #{mainScript.NetworkObjectId}");
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            if (boxerEnemy != null)
            {
                boxerEnemy.SetEnemyVulnerable(Plugin.ConvertToBool(setVulnerableTo));
            }
        }
    }

    public void SetEnemyInSpecialAnimTo(string setInSpecialAnimationTo)
    {
        if (mainScript == null)
        {
            Log($"called SetEnemyInSpecialAnimTo on {name} with null mainScript", 2);
            return;
        }
        LogAI($"{name}: PlayEnemyInSpecialAnimTo({setInSpecialAnimationTo}) on {mainScript} #{mainScript.NetworkObjectId}");
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            boxerEnemy.SetEnemyInSpecialAnimation(Plugin.ConvertToBool(setInSpecialAnimationTo));
        }
    }

    public void OnAttackStart()
    {
        if (mainScript == null)
        {
            Log($"called OnAttackStart on {name} with null mainScript", 2);
            return;
        }
        Log($"{name}: OnAttackStart()");
        managerHitbox.hitPlayerIDs.Clear();
        foreach (HitboxScript hitbox in allHitboxes)
        {
            LogAI($"clearing hitPlayerIDs on {hitbox}");
            hitbox.hitPlayerIDs.Clear();
        }
    }

    public void DisableAllHitboxes()
    {
        LogAI($"called DisableAllHitboxes on {name}");
        if (isManager)
        {
            foreach (HitboxScript hitboxScript in allHitboxes)
            {
                hitboxScript.attackPower = 0;
                hitboxScript.deathAnimIndex = 0;
                hitboxScript.attackForce = Vector3.zero;
                hitboxScript.hitboxCollider.enabled = false;
            }
        }
    }

    public void PlaySFXOnEnemy(int clipCase)
    {
        if (mainScript == null)
        {
            Log($"called PlaySFXOnEnemy on {name} with null mainScript", 2);
            return;
        }
        LogAI($"{name}: PlaySFXOnEnemy({clipCase}) on {mainScript} #{mainScript.NetworkObjectId}");
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
            beatEnemy.PlaySFX(clipCase: clipCase, sync: false, isAudience: false);
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            boxerEnemy.PlaySFX(clipCase: clipCase, sync: false, isAudience: false);
        }
    }

    public void PlayAudienceOnEnemy(int clipCase)
    {
        if (mainScript == null)
        {
            Log($"called PlayAudienceOnEnemy on {name} with null mainScript", 2);
            return;
        }
        LogAI($"{name}: PlayAudienceOnEnemy({clipCase}) on {mainScript} #{mainScript.NetworkObjectId}"); 
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
            beatEnemy.PlaySFX(clipCase: clipCase, sync: false, isAudience: true);
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            boxerEnemy.PlaySFX(clipCase: clipCase, sync: false, isAudience: true);
        }
    }

    public void SpecialAnimEvent(int eventCase)
    {
        if (mainScript == null)
        {
            Log($"called SpecialAnimEvent on {name} with null mainScript", 2);
            return;
        }
        LogAI($"{name}: SpecialAnimEvent({eventCase}) on {mainScript} #{mainScript.NetworkObjectId}"); 
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            switch (eventCase)
            {
                default:
                    boxerEnemy.SetSpotlight(boxerEnemy.enemySpotlight, false, false);
                    break;
                case 1:
                    boxerEnemy.SpawnShovelAndSync();
                    break;
                case 2:
                    boxerEnemy.EndGiveShovelAnim();
                    break;
                case 3:
                    mainScript.enemyType.canBeStunned = false;
                    StunGrenadeItem.StunExplosion(mainScript.transform.position, true, 1, 5);
                    mainScript.enemyType.canBeStunned = true;
                    PlayerControllerB player = StartOfRound.Instance.localPlayerController;
                    if (!player.isPlayerControlled || player.isInsideFactory || player.inAnimationWithEnemy != null)
                    {
                        break;
                    }
                    float distanceFromEnemy = Vector3.Distance(player.transform.position, mainScript.transform.position);
                    if (distanceFromEnemy > 10)
                    {
                        break;
                    }
                    Vector3 forceToHit = mainScript.eye.forward * (distanceFromEnemy / 1.5f) + Vector3.up * 25;
                    if (distanceFromEnemy < 2)
                    {
                        player.DamagePlayer(10, true, true, CauseOfDeath.Blast, 0, false, forceToHit);
                        int playerID = (int)player.playerClientId;
                        if (!managerHitbox.hitPlayerIDs.Contains(playerID))
                        {
                            managerHitbox.hitPlayerIDs.Add(playerID);
                        }
                    }
                    if (!player.isPlayerDead)
                    {
                        player.externalForceAutoFade += forceToHit;
                    }
                    break;
            }
        }
    }

    public void CheckAttackHitOnManager()
    {
        if (mainScript == null)
        {
            Log($"called CheckAttackHitOnManager on {name} with null mainScript", 2);
            return;
        }
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            if (boxerEnemy != null && boxerEnemy.IsOwner)
            {
                Log($"{boxerEnemy} hit {managerHitbox.hitPlayerIDs.Count}", 1);
                if (managerHitbox.hitPlayerIDs.Count != 0)
                {
                    boxerEnemy.OnHitSuccessful(managerHitbox.hitPlayerIDs.ToArray());
                }
            }
        }
    }

    public void CheckAttackHit(int hitboxIndex)
    {
        if (mainScript == null)
        {
            Log($"called CheckAttackHit on {name} with null mainScript", 2);
            return;
        }
        if (hitboxIndex < 0 || hitboxIndex >= allHitboxes.Length)
        {
            Log($"called CheckAttackHitOnHitbox with invalid hitboxIndex {hitboxIndex}", 2);
            return;
        }
        allHitboxes[hitboxIndex].CheckAttackHitOnHitbox(mainScript);
    }

    private void CheckAttackHitOnHitbox(EnemyAI enemyScript)
    {
        if (enemyScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = enemyScript as TheBeatAI;
        }
        else if (enemyScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = enemyScript as TheBoxerAI;
            if (boxerEnemy != null && boxerEnemy.IsOwner)
            {
                Log($"{boxerEnemy} hit {hitPlayerIDs.Count}", 1);
                if (hitPlayerIDs.Count != 0)
                {
                    boxerEnemy.OnHitSuccessful(hitPlayerIDs.ToArray());
                }
            }
        }
    }

    //Useful for once-off information that needs to be distuinguishable in the debug log
    private void Log(string message, int type = 0)
    {
        if (logLevel < 1)
        {
            return;
        }
        switch (type)
        {
            case 0:
                Logger.LogDebug(message);
                return;
            case 1:
                Logger.LogInfo(message);
                return;
            case 2:
                Logger.LogWarning(message);
                return;
            case 3:
                Logger.LogError(message);
                return;
        }
    }

    //For printing every individual step of the enemy, similarly to its currentSearch coroutine on DoAIInterval
    private void LogAI(string message, int type = 0)
    {
        if (logLevel < 2)
        {
            return;
        }
        switch (type)
        {
            case 0:
                Logger.LogDebug(message);
                return;
            case 1:
                Logger.LogInfo(message);
                return;
            case 2:
                Logger.LogWarning(message);
                return;
            case 3:
                Logger.LogError(message);
                return;
        }
    }
}
