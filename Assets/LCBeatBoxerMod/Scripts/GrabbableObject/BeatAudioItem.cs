using UnityEngine;
using Unity.Netcode;
using GameNetcodeStuff;
using BepInEx.Logging;

public class BeatAudioItem : GrabbableObject
{
    private static ManualLogSource Logger = Plugin.Logger;

    [Space(3f)]
    [Header("Custom")]
    public TheBeatAI linkedEnemy;
    public int intendedValue;
    [Range(0f, 1f)]
    public float distanceThresholdToStop;
    public float nearbyToStop;
    public float checkPlayerInterval;
    private float checkTimer;

    [Space]
    public Animator itemAnimator;
    public AudioSource fakeAudio;
    public AudioClip footstepSFX;
    public AudioClip alarmSFX;


    public override void Start()
    {
        base.Start();
        if (!StartOfRound.Instance.inShipPhase || StartOfRound.Instance.testRoom != null)
        {
            LinkItemToEnemy(null, true);
            SetScrapValue(intendedValue);
            RoundManager.Instance.totalScrapValueInLevel += intendedValue;
            HoarderBugAI.grabbableObjectsInMap.Add(gameObject);
        }
    }

    public override void Update()
    {
        base.Update();
        if (!IsOwner)
        {
            return;
        }
        if (checkTimer >= checkPlayerInterval)
        {
            checkTimer = 0;
            DoPlayerCheck();
        }
        else
        {
            checkTimer += Time.deltaTime;
        }
    }

    private void DoPlayerCheck()
    {
        if (!itemAnimator.GetBool("Footsteps"))
        {
            return;
        }
        for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
            if (player == null || !player.isPlayerControlled)
            {
                continue;
            }
            Vector3 from = transform.position + Vector3.up * 0.5f;
            Vector3 to = player.playerEye.position;
            float distance = Vector3.Distance(from, to);
            if (distance <= nearbyToStop || !Physics.Linecast(from, to, StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
            {
                if (distance < fakeAudio.maxDistance * distanceThresholdToStop)
                {
                    Logger.LogDebug($"no line between {name} #{NetworkObjectId} and {player.playerUsername}, going quiet");
                    ToggleFakeAudio(false, true);
                    return;
                }
            }
        }
    }

    public override void GrabItem()
    {
        base.GrabItem();
        Logger.LogDebug($"GrabItem(): playerHeldBy {playerHeldBy} | linkedEnemy {linkedEnemy}");
        if (playerHeldBy != null)
        {
            if (playerHeldBy.isInsideFactory && linkedEnemy != null && !linkedEnemy.isEnemyDead)
            {
                Logger.LogDebug("TOGGLE ALARM");
                ToggleAlarm(true);
            }
            else
            {
                itemAnimator.SetFloat("LookSpeedMultiplier", Random.Range(0.1f, 0.8f));
                itemAnimator.SetBool("Looking", true);
            }
        }
    }

    public override void DiscardItem()
    {
        base.DiscardItem();
        if (itemAnimator.GetBool("Looking"))
        {
            itemAnimator.SetBool("Looking", false);
        }
        if (linkedEnemy == null || linkedEnemy.isEnemyDead || linkedEnemy.currentBehaviourStateIndex != 2)
        {
            ToggleFakeAudio(false);
            ToggleAlarm(false);
        }
    }

    public void ToggleAlarm(bool setTo, bool sync = false)
    {
        if (sync)
        {
            ToggleAlarmServerRpc(setTo);
        }
        else
        {
            SetAlarmTo(setTo);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleAlarmServerRpc(bool setTo)
    {
        ToggleAlarmClientRpc(setTo);
    }

    [ClientRpc]
    private void ToggleAlarmClientRpc(bool setTo)
    {
        SetAlarmTo(setTo);
    }

    private void SetAlarmTo(bool setTo)
    {
        if (!setTo)
        {
            itemAnimator.SetBool("Alarm", false);
        }
        if (setTo)
        {
            Logger.LogDebug("setting trigger Alarm");
            itemAnimator.SetBool("Footsteps", false); 
            itemAnimator.SetBool("Alarm", true);
            if (linkedEnemy != null)
            {
                linkedEnemy.GoIntoChase(true);
            }
        }
    }

    public void ToggleFakeAudio(bool setTo, bool sync = false)
    {
        if (sync)
        {
            ToggleFakeAudioServerRpc(setTo);
        }
        else
        {
            SetFakeAudioTo(setTo);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleFakeAudioServerRpc(bool setTo)
    {
        ToggleFakeAudioClientRpc(setTo);
    }

    [ClientRpc]
    private void ToggleFakeAudioClientRpc(bool setTo)
    {
        SetFakeAudioTo(setTo);
    }

    private void SetFakeAudioTo(bool setTo)
    {
        if (!setTo)
        {
            itemAnimator.SetBool("Footsteps", false);
        }
        if (setTo)
        {
            Logger.LogDebug("setting trigger Footsteps");
            itemAnimator.SetBool("Alarm", false); 
            itemAnimator.SetBool("Footsteps", true);
        }
    }

    public void LinkItemToEnemy(TheBeatAI enemy = null, bool overwriteLink = false)
    {
        if (enemy == null)
        {
            enemy = FindAnyObjectByType<TheBeatAI>();
            if (enemy == null)
            {
                Logger.LogDebug("no beat enemy found to link item to");
                return;
            }
        }
        if (!overwriteLink && enemy.linkedItem != null)
        {
            Logger.LogDebug("enemy already linked to item");
            return;
        }
        linkedEnemy = enemy;
        linkedEnemy.linkedItem = this;
        Logger.LogDebug($"linked ITEM {this} #{NetworkObjectId} to ENEMY {linkedEnemy} #{linkedEnemy.NetworkObjectId}");
    }

    public void UnlinkItemFromEnemy(TheBeatAI enemy = null, bool keepItemLinkedToEnemy = true)
    {
        if (enemy == null)
        {
            if (linkedEnemy != null)
            {
                enemy = linkedEnemy;
            }
            else
            {
                enemy = FindAnyObjectByType<TheBeatAI>();
            }
            if (enemy == null)
            {
                Logger.LogDebug("no beat enemy found to link unitem from");
                return;
            }
        }
        linkedEnemy.linkedItem = null;
        if (!keepItemLinkedToEnemy)
        {
            linkedEnemy = null;
        }
        ToggleAlarm(false);
        ToggleFakeAudio(false);
    }
}
