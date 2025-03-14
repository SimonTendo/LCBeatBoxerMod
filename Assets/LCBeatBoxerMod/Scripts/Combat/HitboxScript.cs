using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;
using BepInEx.Logging;

public class HitboxScript : MonoBehaviour
{
    private static ManualLogSource Logger = Plugin.Logger;
    private static int debugLogLevel = -1;

    [Header("Manager")]
    public bool isManager;
    public EnemyAI mainScript;
    public HitboxScript[] allHitboxes;
    [Space(3f)]
    [Header("Hitbox")]
    public Collider hitboxCollider;
    public HitboxScript managerHitbox;
    [Space]
    public int attackPower;
    public Vector3 attackForce;
    public int deathAnimIndex;
    [Space]
    public bool canHitPlayers;
    public bool canHitEnemies;

    private List<int> hitPlayerIDs = new List<int>();
    private List<int> hitEnemyIDs = new List<int>();

    private void Start()
    {
        if (debugLogLevel == -1)
        {
            debugLogLevel = Plugin.DebugLogLevel();
        }
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
        if (canHitPlayers && other.CompareTag("Player"))
        {
            PlayerControllerB hitPlayer = other.GetComponent<PlayerControllerB>();
            if (PerformHitPlayerCheck(hitPlayer) && hitPlayer == GameNetworkManager.Instance.localPlayerController)
            {
                Log($"{name} PerformAttack({other}) // hitPlayer: {hitPlayer} | attackPower {attackPower} | attackForce {attackForce} | deathAnimIndex {deathAnimIndex}");
                Transform mainTransform = mainScript != null ? mainScript.transform : transform;
                Vector3 attackForceWithDirection = new Vector3(mainTransform.forward.x * attackForce.z, attackForce.y, mainTransform.forward.z * attackForce.x); 
                if (attackPower > 0)
                {
                    hitPlayer.DamagePlayer(attackPower, true, true, CauseOfDeath.Mauling, deathAnimIndex, false, attackForceWithDirection);
                    if (mainScript == null)
                    {
                        Log($"mainScript on hitbox {name} is null, cannot call enemy-dependent functionality", 2);
                    }
                    else
                    {
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
                }
                if (!hitPlayer.isPlayerDead)
                {
                    hitPlayer.externalForceAutoFade = attackForceWithDirection * 2;
                }
            }
        }
        else if (attackPower > 0 && canHitEnemies && other.CompareTag("Enemy"))
        {
            EnemyAICollisionDetect hitEnemy = other.GetComponent<EnemyAICollisionDetect>();
            if (PerformHitEnemyCheck(hitEnemy) && mainScript.IsOwner)
            {
                int enemyAttackPower = attackPower / 10;
                Log($"{name} PerformAttack({other}) // hitEnemy: {hitEnemy.mainScript} | enemyAttackPower {enemyAttackPower}");
                Transform mainTransform = mainScript != null ? mainScript.transform : transform;
                if (enemyAttackPower > 0)
                {
                    hitEnemy.mainScript.HitEnemyOnLocalClient(enemyAttackPower, playHitSFX: true);
                    if (mainScript == null)
                    {
                        Log($"mainScript on hitbox {name} is null, cannot call enemy-dependent functionality", 2);
                    }
                    else
                    {
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

    private bool PerformHitEnemyCheck(EnemyAICollisionDetect hitEnemy)
    {
        if (mainScript == null || hitEnemy == null || hitEnemy.mainScript == null)
        {
            return false;
        }
        EnemyAI enemy = hitEnemy.mainScript;
        if (mainScript == enemy || enemy.isEnemyDead || mainScript.isOutside != enemy.isOutside)
        {
            return false;
        }
        int enemyID = Plugin.GetEnemyIndexInRoundManager(enemy);
        bool flag3 = !hitEnemyIDs.Contains(enemyID);
        if (flag3)
        {
            hitEnemyIDs.Add(enemyID);
            bool flag4 = !managerHitbox.hitEnemyIDs.Contains(enemyID);
            if (flag4)
            {
                managerHitbox.hitEnemyIDs.Add(enemyID);
            }
        }
        return flag3;
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
        bool setTo = Plugin.ConvertToBool(setInSpecialAnimationTo);
        LogAI($"{name}: SetEnemyInSpecialAnimTo({setTo}) on {mainScript} #{mainScript.NetworkObjectId}");
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
            beatEnemy.SetEnemyInSpecialAnimation(setTo);
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            boxerEnemy.SetEnemyInSpecialAnimation(setTo);
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
        managerHitbox.hitEnemyIDs.Clear();
        foreach (HitboxScript hitbox in allHitboxes)
        {
            LogAI($"clearing hitIDs on {hitbox}");
            hitbox.hitPlayerIDs.Clear();
            hitbox.hitEnemyIDs.Clear();
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
                    StunGrenadeItem.StunExplosion(mainScript.transform.position, true, 1, 7.5f);
                    mainScript.enemyType.canBeStunned = true;
                    break;
                case 4:
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
                    Vector3 forceToHit = mainScript.eye.forward * (distanceFromEnemy / 1.33f) + Vector3.up * 33;
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
        bool hitSuccessful = false;
        if (mainScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = mainScript as TheBeatAI;
        }
        else if (mainScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = mainScript as TheBoxerAI;
            if (boxerEnemy != null && boxerEnemy.IsOwner)
            {
                Log($"{boxerEnemy} hit {managerHitbox.hitPlayerIDs.Count} PLAYERS and {managerHitbox.hitEnemyIDs.Count} ENEMIES", 1);
                if (managerHitbox.hitPlayerIDs.Count != 0)
                {
                    hitSuccessful = boxerEnemy.OnHitSuccessful(managerHitbox.hitPlayerIDs.ToArray(), true);
                }
                if (!hitSuccessful && hitEnemyIDs.Count != 0)
                {
                    hitSuccessful = boxerEnemy.OnHitSuccessful(managerHitbox.hitEnemyIDs.ToArray(), false);
                }
            }
        }
        if (hitSuccessful)
        {
            managerHitbox.hitPlayerIDs.Clear();
            managerHitbox.hitEnemyIDs.Clear();
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
        bool hitSuccessful = false;
        if (enemyScript is TheBeatAI)
        {
            TheBeatAI beatEnemy = enemyScript as TheBeatAI;
        }
        else if (enemyScript is TheBoxerAI)
        {
            TheBoxerAI boxerEnemy = enemyScript as TheBoxerAI;
            if (boxerEnemy != null && boxerEnemy.IsOwner)
            {
                Log($"{boxerEnemy} hit {hitPlayerIDs.Count} PLAYERS and {hitEnemyIDs.Count} ENEMIES", 1);
                if (hitPlayerIDs.Count != 0)
                {
                    hitSuccessful = boxerEnemy.OnHitSuccessful(hitPlayerIDs.ToArray(), true);
                }
                if (!hitSuccessful && hitEnemyIDs.Count != 0)
                {
                    hitSuccessful = boxerEnemy.OnHitSuccessful(hitEnemyIDs.ToArray(), false);
                }
            }
        }
        if (hitSuccessful)
        {
            hitPlayerIDs.Clear();
            hitEnemyIDs.Clear();
        }
    }

    //Useful for once-off information that needs to be distuinguishable in the debug log
    private void Log(string message, int type = 0)
    {
        if (debugLogLevel < 1)
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
        if (debugLogLevel < 2)
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
