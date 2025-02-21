using UnityEngine;
using BepInEx.Logging;
using Unity.Netcode;

public class EnemyLureTestItem : GrabbableObject
{
    private static ManualLogSource Logger = Plugin.Logger;

    [Space(3f)]
    [Header("CUSTOM")]
    public int intendedValue;

    public TestEnemyScript linkedEnemy;
    //Read like trueOrFalse
    public bool lureOrRepel;
    public AudioSource fakeAudio;
    public AudioClip footstepSFX;
    public AudioClip alarmSFX;

    public override void Start()
    {
        base.Start();
        SetInitialValues();
        LinkLureToEnemy();
        if (!StartOfRound.Instance.inShipPhase)
        {
            SetScrapValue(intendedValue);
            RoundManager.Instance.totalScrapValueInLevel += intendedValue;
        }
    }

    private void SetInitialValues()
    {
        lureOrRepel = false;
    }

    private void SwapValues()
    {
        lureOrRepel = !lureOrRepel;
        Color newColor = lureOrRepel ? Color.green : Color.red;
        Logger.LogDebug($"#{NetworkObjectId} swapped to {lureOrRepel}");
    }

    public void ToggleAudioFromEnemy(bool enabled)
    {
        ToggleAudioServerRpc(enabled);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleAudioServerRpc(bool enabled)
    {
        ToggleAudioClientRpc(enabled);
    }

    [ClientRpc]
    private void ToggleAudioClientRpc(bool enabled)
    {
        ToggleFakeAudio(enabled);
    }

    private void ToggleFakeAudio(bool enable)
    {
        if (!enable || fakeAudio.isPlaying)
        {
            fakeAudio.Stop();
        }
        if (enable)
        {
            fakeAudio.clip = footstepSFX;
            fakeAudio.Play();
        }
    }

    private void ToggleAlarm(bool enable)
    {
        if (!enable || fakeAudio.isPlaying)
        {
            fakeAudio.Stop();
        }
        if (enable)
        {
            fakeAudio.clip = alarmSFX;
            fakeAudio.Play();
            if (playerHeldBy != null && linkedEnemy != null && linkedEnemy.IsOwner && linkedEnemy.currentBehaviourStateIndex != 2)
            {
                linkedEnemy.SwitchToBehaviourState(2);
            }
        }
    }

    public void LinkLureToEnemy(TestEnemyScript enemy = null)
    {
        if (enemy == null)
        {
            enemy = FindAnyObjectByType<TestEnemyScript>();
        }
        if (enemy == null)
        {
            Logger.LogDebug("no test enemy found to link item to");
            return;
        }
        if (enemy.linkedItem != null)
        {
            Logger.LogDebug("enemy already linked to item");
            return;
        }
        linkedEnemy = enemy;
        linkedEnemy.linkedItem = this;
        Logger.LogDebug($"linked ITEM {this} #{NetworkObjectId} to ENEMY {linkedEnemy} #{linkedEnemy.NetworkObjectId}");
    }

    public override void ItemInteractLeftRight(bool right)
    {
        base.ItemInteractLeftRight(right);
        if (!right)
        {
            //SwapValues();
        }
    }

    public override void EquipItem()
    {
        base.EquipItem();
        if (playerHeldBy != null)
        {
            playerHeldBy.equippedUsableItemQE = true;
            if (isInFactory && linkedEnemy != null && !linkedEnemy.isEnemyDead)
            {
                ToggleAlarm(true);
            }
        }
    }

    public override void PocketItem()
    {
        if (playerHeldBy != null)
        {
            playerHeldBy.equippedUsableItemQE = false;
        }
        base.PocketItem();
    }

    public override void DiscardItem()
    {
        if (playerHeldBy != null)
        {
            playerHeldBy.equippedUsableItemQE = false;
            ToggleAlarm(false);
        }
        base.DiscardItem();
    }
}
