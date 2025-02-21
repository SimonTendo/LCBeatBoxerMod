using UnityEngine;
using GameNetcodeStuff;
using BepInEx.Logging;
using System.Collections.Generic;

public class TestCombatHitbox : MonoBehaviour
{
    private static ManualLogSource Logger = Plugin.Logger;

    public TestEnemyScript mainScript;
    public Collider hitbox;
    public int attackPower;
    public Vector3 attackForce;
    public int deathAnimIndex;
    [HideInInspector]
    public List<int> hitPlayerIDs = new List<int>();

    //private void OnTriggerStay(Collider other)
    //{
    //    PerformAttack(other);
    //}

    private void OnTriggerEnter(Collider other)
    {
        PerformAttack(other);
    }

    private void PerformAttack(Collider other)
    {
        Logger.LogDebug($"other: {other}");
        if (other.CompareTag("Player"))
        {
            PlayerControllerB hitPlayer = other.GetComponent<PlayerControllerB>();
            if (PerformHitPlayerCheck(hitPlayer))
            {
                hitPlayerIDs.Add((int)hitPlayer.playerClientId);
                Logger.LogDebug($"hitPlayer: {hitPlayer}");
                Vector3 attackForceWithDirection = new Vector3(mainScript.transform.forward.x * attackForce.z, attackForce.y, mainScript.transform.forward.z * attackForce.x);
                hitPlayer.DamagePlayer(attackPower, true, true, CauseOfDeath.Mauling, deathAnimIndex, false, attackForceWithDirection);
                if (!hitPlayer.isPlayerDead)
                {
                    hitPlayer.externalForceAutoFade = attackForceWithDirection * 2;
                }
                mainScript.PlayAnimSFX(1);
            }
        }
    }

    private bool PerformHitPlayerCheck(PlayerControllerB hitPlayer)
    {
        if (hitPlayer == null || !hitPlayer.isPlayerControlled || hitPlayer != StartOfRound.Instance.localPlayerController)
        {
            return false;
        }
        int playerID = (int)hitPlayer.playerClientId;
        bool flag = !hitPlayerIDs.Contains(playerID);
        if (flag)
        {
            BoxCollider box = hitbox as BoxCollider;
            float lowestPoint = transform.position.y - box.size.y / 2;
            float num = lowestPoint - hitPlayer.playerEye.transform.position.y;
            Logger.LogDebug($"above head: {num}");
            if (num > 0.5f)
            {
                flag = false;
            }
            else
            {
                hitPlayerIDs.Add(playerID);
            }
        }
        return flag;
    }

    public void SetEnemyVulnerable(int setVulnerableTo)
    {
        mainScript.SetEnemyVulnerable(Plugin.ConvertToBool(setVulnerableTo));
    }

    public void SetEnemyInAttackAnimTo(int setInAttackAnimTo)
    {
        mainScript.SetEnemyInAttackAnimation(Plugin.ConvertToBool(setInAttackAnimTo));
    }

    public void ClearHitPlayerIDs()
    {
        Logger.LogDebug($"Clearing hitPlayerIDs on {name}");
        hitPlayerIDs.Clear();
    }

    public void CheckAttackMiss()
    {
        if (mainScript.IsOwner)
        {
            Logger.LogInfo($"attack {mainScript.currentAttackTrigger} hit {hitPlayerIDs.Count}");
            if (hitPlayerIDs.Count == 0)
            {
                mainScript.currentAttackState = "Miss";
                mainScript.SetAnimation($"{mainScript.currentAttackTrigger}{mainScript.currentAttackState}");
            }
        }
    }
}
