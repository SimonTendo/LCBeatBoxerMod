using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Unity.Netcode;
using GameNetcodeStuff;
using BepInEx.Logging;

public class TheBoxerAI : EnemyAI
{
    private static ManualLogSource Logger = Plugin.Logger;

    private Vector3 positionLastInterval;
    private float distanceLastIntervalTarget;
    private bool approachedTarget;
    private float timeLastVoiceChatLocalPlayer;
    private float timeLastScoreInterval;
    private float timeLastTurningTo;
    private float timeLastCollisionLocalPlayer;
    private float timeLastSwitchingTarget;
    private float timeLastSeeingPlayer;
    private Vector3 turnTo;
    private int animState;
    private float headTargetWeight;
    private bool inSpecialAnimationPreVulnerable;
    private Vector3 specialAnimVelocity;
    private bool heldShovelLastInterval;
    private bool initiatedHostileToLocalPlayer;

    [Space(3f)]
    [Header("MOVEMENT")]
    public float topSpeed;
    public float minTargetDistance;
    public float maxTargetDistance;
    public float calculateMovementInterval;
    private float calculateMovementTimer;
    public float distanceToLosePlayer;
    public float timeWithoutTargetToReset;
    public float[] accelerationPerState;
    public TwoBoneIKConstraint headIK;
    public Transform headTarget;

    [Space(3f)]
    [Header("SENSES")]
    public int seeDistance;
    public float seeWidth;
    public Transform turnCompass;

    [Space(3f)]
    [Header("SFX")]
    [Header("Enemy")]
    public AudioClip intimidateSFX;
    public AudioClip reelSFX;
    public AudioClip punchSFX;
    public AudioClip blockSFX;
    public AudioClip stunEnemySFX;
    public AudioClip stunPlayersSFX;
    [Header("Audience")]
    public AudioSource crowdVoices;
    public static AudioSource boxerAudience;
    public AudioClip bellSFX;
    public AudioClip spotlightSFX;
    public AudioClip crowdStartCheer;
    public AudioClip crowdKillCheer;
    public AudioClip[] crowdHubbubs;

    [Space(3f)]
    [Header("PARAMETERS")]
    public float minTargetFocusTime;
    public float localPlayerLikeMeter;
    public float feedforwardThreshold;
    [Header("Docile")]
    public float docileThreshold;
    [Space]
    public float changePerAlone;
    public float changePerCrouching;
    public float changePerLight;
    [Space]
    public float carryWeightLight;
    [Header("Hostile")]
    public float hostileThreshold;
    [Space]
    public float changePerTogether;
    public float changePerStanding;
    public float changePerArmed;
    public float changePerApproach;
    public float changePerHeavy;
    [Space]
    public float approachLeeway;
    public float approachIncrements;
    public float carryWeightHeavy;

    [Space(3f)]
    [Header("DOCILE")]
    public GrabbableObject[] itemSlots;
    public Transform[] itemParents;
    public InteractTrigger interactScript;
    public float checkItemGiveInterval;
    private float checkItemGiveTimer;

    [Space(3f)]
    [Header("HOSTILE")]
    public bool vulnerable;
    public float collisionCooldown;
    public float collisionDistance;
    public float stunTime;
    public HitboxScript managerHitbox;
    public AttackSequence currentAttackSequence;
    private int currentAttackIndex;
    public float[] stunTimePerAttackEnum;
    [Space]
    public Transform shovelParent;
    public Shovel heldShovel;
    public Transform holdPlayerParent;
    public int turnPlayerIterations;
    public Light enemySpotlight;
    public Light playerSpotlight;
    [Space]
    public EnemyAI targetEnemy;
    public Item bellItem;
    public int bellValue;

    [Space(3f)]
    [Header("DEBUG")]
    public GameObject debugNestPrefab;
    public static GameObject nestObject;
    public GameObject debugEyeForward;

    public override void Start()
    {
        favoriteSpot = GetNestTransform();
        base.Start();
        itemSlots = new GrabbableObject[itemParents.Length];
        interactScript.interactable = false;

        if (boxerAudience == null)
        {
            crowdVoices.transform.SetParent(RoundManager.Instance.spawnedScrapContainer);
            boxerAudience = crowdVoices;
        }

        enemySpotlight.intensity = 5000;
        enemySpotlight.range = 25;
        enemySpotlight.shadows = LightShadows.None;
        SetSpotlight(enemySpotlight, false);

        playerSpotlight.intensity = 5000;
        playerSpotlight.range = 25;
        playerSpotlight.shadows = LightShadows.None;
        SetSpotlight(playerSpotlight, false, false); 

        DebugEnemy = Plugin.DebugLogLevel() >= 1;
        debugEnemyAI = Plugin.DebugLogLevel() == 2;
    }

    private Transform GetNestTransform()
    {
        for (int i = 0; i < RoundManager.Instance.enemyNestSpawnObjects.Count; i++)
        {
            if (RoundManager.Instance.enemyNestSpawnObjects[i].enemyType == enemyType)
            {
                return RoundManager.Instance.enemyNestSpawnObjects[i].gameObject.transform;
            }
        }
        Log($"failed to find nest, creating new empty nest at current position", 3);
        if (nestObject == null)
        {
            debugNestPrefab.transform.SetParent(RoundManager.Instance.spawnedScrapContainer);
            nestObject = debugNestPrefab;
        }
        nestObject.transform.position = transform.position;
        return nestObject.transform;
    }

    public override void Update()
    {
        base.Update();
        if (isEnemyDead || !ventAnimationFinished)
        {
            return;
        }
        if (inSpecialAnimation)
        {
            updatePositionThreshold = 0.1f;
            syncMovementSpeed = 0.1f;
            if (!IsOwner)
            {
                transform.position = Vector3.SmoothDamp(transform.position, serverPosition, ref specialAnimVelocity, syncMovementSpeed);
            }
        }
        else if (calculateMovementTimer >= calculateMovementInterval)
        {
            CalculateAnimations();
            calculateMovementTimer = 0;
        }
        else
        {
            updatePositionThreshold = 1.0f;
            calculateMovementTimer += Time.deltaTime;
        }
        debugEyeForward.transform.position = eye.position + eye.forward * 2;
        if (stunNormalizedTimer > 0)
        {
            if (animState != 4)
            {
                Log("STUN - START!!", 3);
                SetAnimation("Stunned", IsOwner, true, true);
            }
        }
        else
        {
            if (animState == 4)
            {
                Log("STUN - END!!", 3);
                SetEnemyVulnerable(false);
                SetAnimation("Stunned", false, true, false);
            }
        }
        headIK.weight = Mathf.Lerp(headIK.weight, headTargetWeight, 0.1f);
        if (headIK.weight > 0.1f && targetNode != null)
        {
            headTarget.position = Vector3.Lerp(headTarget.position, targetNode.position, 0.075f); 
        }
        useSecondaryAudiosOnAnimatedObjects = currentBehaviourStateIndex == 2;
        switch (currentBehaviourStateIndex)
        {
            case 0:
                if (interactScript.enabled)
                {
                    interactScript.interactable = false;
                    interactScript.enabled = false;
                    interactScript.gameObject.SetActive(false);
                }
                enemyType.pushPlayerForce = 8f;
                TurnToLookAt();
                break;
            case 1:
                if (!interactScript.enabled)
                {
                    interactScript.gameObject.SetActive(true);
                    interactScript.enabled = true;
                }
                enemyType.pushPlayerForce = 5f;
                if (checkItemGiveTimer >= checkItemGiveInterval)
                {
                    checkItemGiveTimer = 0;
                    if (targetEnemy == null)
                    {
                        CheckInventory();
                    }
                }
                else
                {
                    checkItemGiveTimer += Time.deltaTime;
                }
                break;
            case 2:
                if (interactScript.enabled)
                {
                    interactScript.interactable = false;
                    interactScript.enabled = false;
                    interactScript.gameObject.SetActive(false);
                }
                enemyType.pushPlayerForce = 1f;
                if (inSpecialAnimationWithPlayer != null)
                {
                    inSpecialAnimationWithPlayer.transform.position = holdPlayerParent.position;
                    inSpecialAnimationWithPlayer.transform.rotation = holdPlayerParent.rotation;
                }
                else if (inSpecialAnimation && inSpecialAnimationPreVulnerable && targetPlayer != null)
                {
                    if (vulnerable)
                    {
                        inSpecialAnimationPreVulnerable = false;
                    }
                    else
                    {
                        DetectNewSighting(targetPlayer.transform.position, true);
                    }
                }
                if (targetPlayer != null)
                {
                    if (!GetHoldingShovel())
                    {
                        playerSpotlight.transform.position = targetPlayer.transform.position + Vector3.up * 15;
                    }
                }
                else
                {
                    TurnToLookAt();
                }
                break;
        }
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (isEnemyDead || !ventAnimationFinished)
        {
            return;
        }
        if (StartOfRound.Instance.allPlayersDead)
        {
            if (animState != 0)
            {
                SetAnimation("Sitting");
            }
            return;
        }
        if (stunNormalizedTimer > 0)
        {
            timeLastSeeingPlayer = Time.realtimeSinceStartup;
            agent.speed = 0;
            return;
        }
        if (heldShovelLastInterval && !GetHoldingShovel())
        {
            heldShovelLastInterval = false;
            if (heldShovel != null && heldShovel.playerHeldBy != null)
            {
                DetectNewSighting(heldShovel.playerHeldBy.playerEye.position);
                GoIntoHostile(heldShovel.playerHeldBy);
            }
            return;
        }
        PlayerControllerB[] allSeenPlayers = GetAllPlayersInLineOfSight(seeWidth, seeDistance);
        switch (currentBehaviourStateIndex)
        {
            case 0:
                if (allSeenPlayers != null)
                {
                    timeLastSeeingPlayer = Time.realtimeSinceStartup;
                }
                else
                {
                    LogAI("A");
                    SetTargetPlayer(null);
                    if (Time.realtimeSinceStartup - timeLastSeeingPlayer > timeWithoutTargetToReset)
                    {
                        if (enemySpotlight.enabled)
                        {
                            SetSpotlight(enemySpotlight, true, false);
                        }
                        float nestDistance = Vector3.Distance(transform.position, favoriteSpot.position);
                        if (nestDistance > minTargetDistance)
                        {
                            if (animState != 1)
                            {
                                SetAnimation("Hunched");
                            }
                            SetDestinationToPosition(favoriteSpot.position);
                            CalculateSpeedToDestination(favoriteSpot);
                        }
                        else
                        {
                            LogAI("1");
                            DoIdleSit();
                        }
                    }
                    else
                    {
                        LogAI("2");
                        DoIdleSit();
                    }
                    break;
                }
                if (SetTargetPlayer(GetClosestSeenPlayerEye(allSeenPlayers, true)))
                {
                    Log($"owner switching OWNERSHIP, not performing rest of currentBehaviorState({currentBehaviourStateIndex})", 3);
                    break;
                }
                if (targetPlayer != null)
                {
                    turnTo = targetPlayer.transform.position;
                    timeLastTurningTo = Time.realtimeSinceStartup;
                    if (LocalPlayerScoreInterval(canGoToIdle: true))
                    {
                        Log($"owner switching STATE, not performing rest of previousBehaviorState({previousBehaviourStateIndex})", 2);
                        break;
                    }
                }
                else if (Time.realtimeSinceStartup - timeLastSeeingPlayer > minTargetFocusTime)
                {
                    LogAI("3");
                    DoIdleSit();
                }
                break;
            case 1:
                if (targetEnemy != null)
                {
                    if (targetEnemy.isEnemyDead || (targetPlayer != null && targetPlayer.inAnimationWithEnemy != targetEnemy))
                    {
                        SetTargetEnemy(targetPlayer.inAnimationWithEnemy);
                        break;
                    }
                    float distanceToEnemy = Vector3.Distance(transform.position, targetEnemy.transform.position);
                    if (distanceToEnemy < collisionDistance * 2)
                    {
                        SetAnimation("BoxerStun");
                    }
                    else
                    {
                        CalculateSpeedToDestination(targetEnemy.transform, false);
                        SetDestinationToPosition(targetEnemy.transform.position);
                    }
                    break;
                }
                if (targetPlayer != null && targetPlayer.isPlayerControlled && !targetPlayer.isInsideFactory && !(StartOfRound.Instance.hangarDoorsClosed && isInsidePlayerShip != targetPlayer.isInHangarShipRoom))
                {
                    if (targetEnemy == null && targetPlayer.inAnimationWithEnemy != null)
                    {
                        SetWavingGoodbye(false, true);
                        ClearInventory();
                        SetTargetEnemy(targetPlayer.inAnimationWithEnemy);
                        break;
                    }
                    timeLastSeeingPlayer = Time.realtimeSinceStartup;
                    bool switchedStateUponArmedPlayer = false;
                    if (allSeenPlayers != null)
                    {
                        for (int i = 0; i < allSeenPlayers.Length; i++)
                        {
                            PlayerControllerB seenPlayer = allSeenPlayers[i];
                            if (PlayerIsArmed(seenPlayer) && GoIntoIdle(seenPlayer))
                            {
                                LogAI("I");
                                switchedStateUponArmedPlayer = true;
                                break;
                            }
                        }
                    }
                    if (switchedStateUponArmedPlayer)
                    {
                        Log($"owner switching STATE, not performing rest of previousBehaviorState({previousBehaviourStateIndex})", 2);
                        break;
                    }
                    if (animState == 3)
                    {
                        agent.speed = 0;
                        DetectNewSighting(targetPlayer.playerEye.position);
                        break;
                    }
                    else if (animState == 0)
                    {
                        SetAnimation("Hunched");
                    }
                    if (LocalPlayerScoreInterval(0.25f, 0.1f))
                    {
                        Log($"owner switching STATE, not performing rest of previousBehaviorState({currentBehaviourStateIndex})", 2);
                        break;
                    }
                    CalculateSpeedToDestination(targetPlayer.playerEye);
                    if (distanceLastIntervalTarget > distanceToLosePlayer && Time.realtimeSinceStartup - timeLastSeeingPlayer > minTargetFocusTime)
                    {
                        LogAI("II");
                        GoIntoIdle();
                        break;
                    }
                    SetMovingTowardsTargetPlayer(targetPlayer);
                }
                else
                {
                    LogAI("III");
                    GoIntoIdle();
                }
                break;
            case 2:
                if (targetPlayer != null)
                {
                    timeLastSeeingPlayer = Time.realtimeSinceStartup;
                    if (!inSpecialAnimation)
                    {
                        CalculateSpeedToDestination(targetPlayer.playerEye, false);
                        if (!targetPlayer.isPlayerControlled || targetPlayer.isInsideFactory || distanceLastIntervalTarget > distanceToLosePlayer || (StartOfRound.Instance.hangarDoorsClosed && isInsidePlayerShip != targetPlayer.isInHangarShipRoom))
                        {
                            LogAI("B");
                            SetTargetPlayer(null);
                            break;
                        }
                        if (LocalPlayerScoreInterval(0.75f, 1.25f, !PlayerIsArmed()))
                        {
                            Log($"owner switching STATE, not performing rest of currentBehaviorState({previousBehaviourStateIndex})", 2);
                            break;
                        }
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        if (animState != 2)
                        {
                            SetAnimation("Upright");
                        }
                        float distanceFromPlayer = Vector3.Distance(transform.position, targetPlayer.transform.position);
                        if (distanceFromPlayer < collisionDistance && Time.realtimeSinceStartup - timeLastCollisionLocalPlayer > collisionCooldown)
                        {
                            PerformPlayerCollision(targetPlayer);
                            break;
                        }
                        if (GetHoldingShovel())
                        {
                            targetPlayer.JumpToFearLevel(0.8f);
                        }
                        else
                        {
                            if (!initiatedHostileToLocalPlayer)
                            {
                                InitiateAttackSequence();
                            }
                            if (!enemySpotlight.enabled)
                            {
                                SetSpotlight(enemySpotlight);
                            }
                            if (!playerSpotlight.enabled)
                            {
                                SetSpotlight(playerSpotlight);
                            }
                        }
                    }
                    else
                    {
                        agent.speed = 0;
                    }
                }
                else
                {
                    agent.speed = 0;
                    if (playerSpotlight.enabled)
                    {
                        SetSpotlight(playerSpotlight, true, false);
                    }
                    if (allSeenPlayers != null && SetTargetPlayer(GetClosestSeenPlayerEye(allSeenPlayers, true)))
                    {
                        Log($"owner switching OWNERSHIP, not performing rest of currentBehaviorState({currentBehaviourStateIndex})", 3);
                        break;
                    }
                    if (Time.realtimeSinceStartup - timeLastSeeingPlayer > timeWithoutTargetToReset)
                    {
                        LogAI("IV");
                        GoIntoIdle();
                        break;
                    }
                }
                break;
        }
        heldShovelLastInterval = GetHoldingShovel();
    }

    private bool GoIntoIdle(PlayerControllerB withTarget = null, bool enableOwnSpotlight = false)
    {
        bool switchState = false;
        if (!IsOwner)
        {
            return switchState;
        }
        if (withTarget == null)
        {
            withTarget = targetPlayer;
        }
        switchState = currentBehaviourStateIndex != 0;
        LogAI($"switchState? {switchState}");
        if (!switchState)
        {
            SetTargetPlayer(withTarget);
            return switchState;
        }
        Log($"switching to: IDLE", 1); 
        SetSpotlight(enemySpotlight, true, enableOwnSpotlight);
        SetSpotlight(playerSpotlight, true, false);
        timeLastSeeingPlayer = Time.realtimeSinceStartup;
        ClearInventory();
        SwitchToBehaviourState(0);
        SetTargetPlayer(withTarget);
        return switchState;
    }

    private bool GoIntoDocile(PlayerControllerB withTarget = null, bool enableOwnSpotlight = false)
    {
        bool switchState = false;
        if (!IsOwner)
        {
            return switchState;
        }
        if (withTarget == null)
        {
            withTarget = targetPlayer;
        }
        switchState = currentBehaviourStateIndex != 1;
        LogAI($"switchState? {switchState}");
        if (!switchState)
        {
            SetTargetPlayer(withTarget);
            return switchState;
        }
        Log($"switching to: DOCILE", 2);
        SetSpotlight(enemySpotlight, true, enableOwnSpotlight);
        SetSpotlight(playerSpotlight, true, false);
        ClearInventory();
        SwitchToBehaviourState(1);
        SetTargetPlayer(withTarget);
        return switchState;
    }

    private bool GoIntoHostile(PlayerControllerB withTarget = null, bool enableOwnSpotlight = false)
    {
        bool switchState = false;
        if (!IsOwner)
        {
            return switchState;
        }
        if (withTarget == null)
        {
            withTarget = targetPlayer;
        }
        switchState = currentBehaviourStateIndex != 2;
        LogAI($"switchState? {switchState}");
        if (!switchState)
        {
            SetTargetPlayer(withTarget);
            return switchState;
        }
        Log($"switching to: HOSTILE", 3);
        SetWavingGoodbye(false, true);
        PlaySFX(intimidateSFX);
        SetSpotlight(playerSpotlight, true, false);
        ClearInventory();
        SwitchToBehaviourState(2);
        SetTargetPlayer(withTarget);
        return switchState;
    }

    private void DoIdleSit()
    {
        if (animState != 0)
        {
            SetAnimation("Sitting");
        }
        agent.speed = 0;
    }


    private void CalculateAnimations()
    {
        if (currentBehaviourStateIndex == 1 && StartOfRound.Instance.shipIsLeaving && targetPlayer != null)
        {
            if (targetPlayer.isInElevator)
            {
                SetWavingGoodbye(true, sync: false);
            }
            else
            {
                SetWavingGoodbye(false, sync: false);
            }
        }
        float distanceLastIntervalSelf = Vector3.Distance(transform.position, positionLastInterval);
        positionLastInterval = transform.position;
        float moveSpeed = distanceLastIntervalSelf / 6;
        if (distanceLastIntervalSelf < 0.5f)
        {
            if (creatureAnimator.GetBool("Moving"))
            {
                SetAnimation("Moving", false, true, false);
            }
        }
        else
        {
            if (!creatureAnimator.GetBool("Moving"))
            {
                SetAnimation("Moving", false, true);
            }
            LogAI($"moveSpeed = {moveSpeed}");
            SetAnimation(null, false, false, false, "WalkSpeedMultiplier", moveSpeed);
        }
        syncMovementSpeed = Mathf.Clamp(0.8f - moveSpeed, 0.15f, 0.5f);
        targetNode = null;
        if (!inSpecialAnimation && stunNormalizedTimer <= 0)
        {
            if (targetEnemy != null)
            {
                targetNode = targetEnemy.eye;
            }
            else if (targetPlayer != null)
            {
                targetNode = targetPlayer.playerEye;
            }
            Vector3 mainTransformOnEye = transform.TransformPoint(transform.InverseTransformPoint(eye.position));
            if (animState != 3 && targetNode != null && Vector3.Angle(transform.forward, targetNode.position - mainTransformOnEye) > Mathf.Clamp(seeWidth, 0.0f, 33.0f))
            {
                targetNode = null;
            }
        }
        headTargetWeight = targetNode == null ? 0 : 1;
    }

    private void CalculateDistanceTarget(Transform target)
    {
        if (target == null)
        {
            distanceLastIntervalTarget = -1;
            approachedTarget = false;
            return;
        }
        float distanceThisIntervalTarget = Vector3.Distance(eye.position, target.position);
        LogAI($"CalculateDistanceChangeTarget: this = {distanceThisIntervalTarget} | last = {distanceLastIntervalTarget}");
        approachedTarget = distanceThisIntervalTarget < distanceLastIntervalTarget - approachLeeway;
        distanceLastIntervalTarget = distanceThisIntervalTarget;
        LogAI($"approached? {approachedTarget}");
    }

    private void CalculateSpeedToDestination(Transform toDestination, bool keepMinDistance = true)
    {
        if (toDestination == null || stunNormalizedTimer > 0)
        {
            agent.speed = 0.0f;
            return;
        }
        float negativeMultiplier = 1.0f;
        if (distanceLastIntervalTarget != -1)
        {
            CalculateDistanceTarget(toDestination);
            LogAI($"distanceLastInterval: {distanceLastIntervalTarget}");
            if (keepMinDistance && distanceLastIntervalTarget < minTargetDistance)
            {
                agent.speed = 0.0f;
                return;
            }
            else if (approachedTarget && distanceLastIntervalTarget < maxTargetDistance)
            {
                negativeMultiplier = -1.0f;
            }
        }
        agent.speed = Mathf.Clamp(agent.speed + accelerationPerState[currentBehaviourStateIndex % accelerationPerState.Length] * negativeMultiplier, 0.0f, topSpeed);
    }

    private void CalculateLocalPlayerScoreChange(float positiveMultiplier = 1.0f, float negativeMultiplier = 1.0f)
    {
        timeLastScoreInterval = Time.realtimeSinceStartup;
        if (targetPlayer == null || targetPlayer != StartOfRound.Instance.localPlayerController)
        {
            Log($"owner called LocalPlayerScoreInterval despite targetPlayer being {targetPlayer}, returning");
            return;
        }
        float changeThisInterval = 0;
        if (targetPlayer.isPlayerAlone)
        {
            LogAI("alone", 3);
            changeThisInterval += changePerAlone * positiveMultiplier;
        }
        else
        {
            LogAI("together", 3);
            changeThisInterval += changePerTogether * negativeMultiplier;
        }
        if (targetPlayer.isCrouching)
        {
            LogAI("crouching", 3);
            changeThisInterval += changePerCrouching * positiveMultiplier;
        }
        else
        {
            LogAI("standing", 3);
            changeThisInterval += changePerStanding * negativeMultiplier;
        }
        if (targetPlayer.carryWeight < carryWeightLight)
        {
            LogAI("light", 3);
            changeThisInterval += changePerLight * positiveMultiplier;
        }
        if (PlayerIsArmed())
        {
            LogAI("armed", 3);
            changeThisInterval += changePerArmed * negativeMultiplier;
        }
        if (currentBehaviourStateIndex == 0 && targetPlayer != null)
        {
            CalculateDistanceTarget(targetPlayer.playerEye);
            if (approachedTarget)
            {
                int approachScore = 0;
                float playerDistanceFromEdge = seeDistance - distanceLastIntervalTarget;
                approachScore += (int)(playerDistanceFromEdge / approachIncrements);
                LogAI($"approach (score: {approachScore})", 3);
                changeThisInterval += changePerApproach * approachScore * negativeMultiplier;
            }
        }
        if (targetPlayer.carryWeight >= carryWeightHeavy)
        {
            LogAI("heavy", 3);
            changeThisInterval += changePerHeavy * negativeMultiplier;
        }
        localPlayerLikeMeter = Mathf.Clamp(localPlayerLikeMeter + changeThisInterval, hostileThreshold - 99, docileThreshold + 1);
        LogAI($"updated localPlayerLikeMeter to {localPlayerLikeMeter} with changeThisInterval {changeThisInterval} for player {targetPlayer}", 1);
    }

    private bool LocalPlayerScoreInterval(float positiveMultiplier = 1.0f, float negativeMultiplier = 1.0f, bool canGoToIdle = false)
    {
        bool scoreSwitchedState = false;
        if (Time.realtimeSinceStartup - timeLastScoreInterval > 1)
        {
            CalculateLocalPlayerScoreChange(positiveMultiplier, negativeMultiplier);
            if (localPlayerLikeMeter > docileThreshold)
            {
                scoreSwitchedState = GoIntoDocile(targetPlayer, false);
            }
            else if (localPlayerLikeMeter < hostileThreshold)
            {
                scoreSwitchedState = GoIntoHostile(targetPlayer);
            }
            else if (localPlayerLikeMeter > feedforwardThreshold && canGoToIdle)
            {
                LogAI("V");
                if (animState != 1)
                {
                    LogAI($"animState: {animState}");
                    SetAnimation("Hunched");
                }
                scoreSwitchedState = GoIntoIdle(targetPlayer, enemySpotlight.enabled);
            }
            else if (localPlayerLikeMeter < feedforwardThreshold && canGoToIdle)
            {
                LogAI("VI");
                if (animState != 2)
                {
                    LogAI($"animState: {animState}");
                    PlaySFX(intimidateSFX);
                    SetAnimation("Upright");
                }
                scoreSwitchedState = GoIntoIdle(targetPlayer, enemySpotlight.enabled);
            }
        }
        LogAI($"LocalPlayerScoreInterval() // targetPlayer: {targetPlayer} | scoreSwitchedState: {scoreSwitchedState}", 1);
        if (scoreSwitchedState)
        {
            timeLastScoreInterval = Time.realtimeSinceStartup + minTargetFocusTime;
            Log($"scoreSwitchedState {scoreSwitchedState}: adding cooldown of {minTargetFocusTime} to LocalPlayerScoreInterval");
        }
        return scoreSwitchedState;
    }

    private PlayerControllerB GetClosestSeenPlayerEye(PlayerControllerB[] seenPlayers = null, bool requireLineOfSight = true)
    {
        if (seenPlayers == null)
        {
            seenPlayers = StartOfRound.Instance.allPlayerScripts;
        }
        PlayerControllerB result = null;
        mostOptimalDistance = (float)seeDistance;
        for (int i = 0; i < seenPlayers.Length; i++)
        {
            PlayerControllerB player = seenPlayers[i];
            if (!requireLineOfSight || !Physics.Linecast(eye.position + Vector3.up, player.playerEye.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                tempDist = Vector3.Distance(eye.position, player.playerEye.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    result = player;
                }
            }
        }
        return result;
    }

    private bool GetHoldingShovel()
    {
        if (heldShovel == null)
        {
            return false;
        }
        if (heldShovel.parentObject == shovelParent || heldShovel.parentObject == itemParents[1])
        {
            return true;
        }
        return false;
    }

    private bool PlayerIsArmed(PlayerControllerB player = null, bool checkHeldItemOnly = true)
    {
        if (player == null)
        {
            if (targetPlayer == null)
            {
                return false;
            }
            player = targetPlayer;
        }
        if (checkHeldItemOnly)
        {
            GrabbableObject heldObject = player.currentlyHeldObjectServer;
            if (heldObject == null || heldObject.itemProperties == null)
            {
                return false;
            }
            if (heldObject.itemProperties.isDefensiveWeapon)
            {
                return true;
            }
            if (heldObject.GetComponent<Shovel>() || heldObject.GetComponent<KnifeItem>() || heldObject.GetComponent<ShotgunItem>() || heldObject.GetComponent<GunAmmo>() || heldObject.GetComponent<PatcherTool>() || heldObject.GetComponent<StunGrenadeItem>() || heldObject.GetComponent<ExtensionLadderItem>() || heldObject.GetComponent<RadarBoosterItem>() || heldObject.GetComponent<RagdollGrabbableObject>() || heldObject.GetComponent<BeatAudioItem>())
            {
                return true;
            }
        }
        else
        {
            foreach (GrabbableObject item in player.ItemSlots)
            {
                if (item == null || item.itemProperties == null)
                {
                    continue;
                }
                if (item.itemProperties.isDefensiveWeapon)
                {
                    return true;
                }
                if (item.GetComponent<Shovel>() || item.GetComponent<KnifeItem>() || item.GetComponent<ShotgunItem>() || item.GetComponent<PatcherTool>() || item.GetComponent<StunGrenadeItem>() || item.GetComponent<ExtensionLadderItem>() || item.GetComponent<RagdollGrabbableObject>() || item.GetComponent<BeatAudioItem>())
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void SetWavingGoodbye(bool setTo, bool goToUpright = false, bool sync = true)
    {
        if (setTo && animState != 3)
        {
            ClearInventory(true, sync);
            SetAnimation("WaveGoodbye", sync);
        }
        else if (!setTo)
        {
            string animString = goToUpright ? "Upright" : "Hunched";
            int newState = goToUpright ? 2 : 1;
            if (animState != newState)
            {
                SetAnimation(animString, sync);
            }
        }
    }

    private void CheckInventory()
    {
        bool emptyInventory = true;
        for (int i = 0; i < itemSlots.Length; i++)
        {
            GrabbableObject item = itemSlots[i];
            if (item != null)
            {
                emptyInventory = false;
                if (item.parentObject != itemParents[i])
                {
                    Log($"found item {item} in slot {i} to be elsewhere than {itemParents[i]}, removing link from inventory");
                    RemoveItemFromInventoryLocal(item, i);
                }
            }
        }
        LogAI($"{emptyInventory} | {animState}");
        if (targetEnemy == null && emptyInventory && animState == 2)
        {
            Log($"counted enemy {name} #{NetworkObjectId} to have emptyInventory {emptyInventory}, going to hunched animation");
            SetAnimation("Hunched", false);
        }
        if (StartOfRound.Instance.localPlayerController.currentlyHeldObjectServer == null)
        {
            interactScript.interactable = false;
            interactScript.disabledHoverTip = "Not holding item";
            return;
        }
        if (GetFirstSlot() == -1)
        {
            interactScript.interactable = false;
            interactScript.disabledHoverTip = "Hands full";
            return;
        }
        interactScript.interactable = true;
        interactScript.hoverTip = "Give item : [ E ]";
    }

    public void InteractGiveHoldingItem(PlayerControllerB calledBy)
    {
        GrabbableObject currentItem = calledBy.currentlyHeldObjectServer;
        if (currentItem == null)
        {
            Log("local player tried giving item without holding one");
            return;
        }
        int putInSlot = GetFirstSlot(null, true);
        if (putInSlot == -1)
        {
            Log($"enemy found to have full inventory upon interact, likely thanks to other player handing over item at same time?", 2);
            return;
        }
        GiveHoldingItemLocal(currentItem, putInSlot, calledBy);
        GiveHoldingItemServerRpc(currentItem.NetworkObject, putInSlot, (int)calledBy.playerClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void GiveHoldingItemServerRpc(NetworkObjectReference itemNOR, int putInSlot, int sentBy)
    {
        GiveHoldingItemClientRpc(itemNOR, putInSlot, sentBy);
    }

    [ClientRpc]
    private void GiveHoldingItemClientRpc(NetworkObjectReference itemNOR, int putInSlot, int sentBy)
    {
        Log($"GiveHoldingItemClientRpc({putInSlot}, {sentBy})");
        if (sentBy < 0 || sentBy >= StartOfRound.Instance.allPlayerScripts.Length)
        {
            Log($"sentBy in GiveHoldingItemClientRpc invalid!!");
            return;
        }
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            if (itemNOR.TryGet(out var netObj))
            {
                GrabbableObject item = netObj.GetComponent<GrabbableObject>();
                if (item == null)
                {
                    Log($"failed to get item in GiveHoldingItemClientRpc()", 3);
                    return;
                }
                GiveHoldingItemLocal(item, putInSlot, StartOfRound.Instance.allPlayerScripts[sentBy]);
            }
            else
            {
                Log("failed to get netObj in GiveHoldingItemClientRpc()!", 3);
            }
        }
        else
        {
            Log("local player");
        }
    }

    private void GiveHoldingItemLocal(GrabbableObject itemToGrab, int putInSlot, PlayerControllerB removeFromPlayer)
    {
        if (itemToGrab == null || removeFromPlayer == null || putInSlot < 0 || putInSlot >= itemSlots.Length)
        {
            Log($"GiveHoldingItemLocal tried invalid giving item {itemToGrab} with slot {putInSlot}", 2);
            return;
        }
        Log($"grabbing {itemToGrab} #{itemToGrab.NetworkObjectId}!!", 1);
        if (itemSlots[putInSlot] != null)
        {
            Log($"DESYNC: player tried putting {itemToGrab} in slot [{putInSlot}] already occupied by {itemSlots[putInSlot]}, putting in next best slot locally!!!", 2);
            putInSlot = GetFirstSlot();
            if (putInSlot == -1)
            {
                Log($"ERROR: still could not find empty slot, not giving item locally", 3);
                return;
            }
        }
        RemoveItemFromPlayerInventory(itemToGrab, removeFromPlayer);
        itemSlots[putInSlot] = itemToGrab;
        itemToGrab.grabbableToEnemies = false;
        itemToGrab.isHeldByEnemy = true;
        itemToGrab.fallTime = 1;
        itemToGrab.hasHitGround = true;
        itemToGrab.reachedFloorTarget = true;
        itemToGrab.parentObject = itemParents[putInSlot];
        itemToGrab.EnableItemMeshes(true);
        itemToGrab.EnablePhysics(true);
        agent.speed = 0;
        SetAnimation("GrabItem", false);
        CheckInventory();
    }

    private void RemoveItemFromPlayerInventory(GrabbableObject item, PlayerControllerB player)
    {
        if (item == null || player == null)
        {
            Log($"RemoveItemFromPlayerInventory({item}, {player})", 3);
            return;
        }
        int slotOnPlayer = -1;
        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            if (player.ItemSlots[i] == item)
            {
                slotOnPlayer = i;
                break;
            }
        }
        if (slotOnPlayer == -1)
        {
            Log($"failed to find player {player} slot for item {item} #{item.NetworkObjectId} given to {name} #{NetworkObjectId}!!!", 3);
            return;
        }
        item.isHeld = false;
        item.playerHeldBy = null;
        item.heldByPlayerOnServer = false;
        item.DiscardItem();
        player.ItemSlots[slotOnPlayer] = null;
        if (player == GameNetworkManager.Instance.localPlayerController)
        {
            HUDManager.Instance.itemSlotIcons[slotOnPlayer].enabled = false;
            HUDManager.Instance.holdingTwoHandedItem.enabled = false;
            HUDManager.Instance.ClearControlTips();
        }
        player.twoHanded = false;
        player.twoHandedAnimation = false;
        player.activatingItem = false;
        player.IsInspectingItem = false;
        player.isHoldingObject = false;
        player.currentlyHeldObject = null;
        player.currentlyHeldObjectServer = null;
        player.equippedUsableItemQE = false;
        player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight - 1f), 1f, 10f);
        player.playerBodyAnimator.SetBool("cancelHolding", true);
        player.playerBodyAnimator.SetTrigger("Throw");
        player.playerBodyAnimator.SetBool("Grab", false);
        try
        {
            player.playerBodyAnimator.SetBool(item.itemProperties.grabAnim, false);
        }
        catch
        {
            Log($"error when trying to give item and setting playerBodyAnimator with grabAnim {item.itemProperties.grabAnim}", 2);
        }
    }

    private void ClearInventory(bool dropItem = true, bool sync = true)
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (itemSlots[i] != null)
            {
                RemoveItemFromInventoryLocal(itemSlots[i], i, dropItem);
            }
            if (sync)
            {
                DropItemInSlotServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, i, dropItem);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropItemInSlotServerRpc(int sentBy, int forSlot, bool dropItem)
    {
        DropItemInSlotClientRpc(sentBy, forSlot, dropItem);
    }

    [ClientRpc]
    private void DropItemInSlotClientRpc(int sentBy, int forSlot, bool dropItem)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            GrabbableObject itemInSlot = itemSlots[forSlot];
            if (itemInSlot != null)
            {
                RemoveItemFromInventoryLocal(itemInSlot, forSlot, dropItem);
            }
        }
    }

    private void RemoveItemFromInventoryLocal(GrabbableObject itemToDrop = null, int removeFromSlot = -1, bool dropToGround = false)
    {
        if (removeFromSlot != -1 && removeFromSlot >= 0 && removeFromSlot < itemSlots.Length)
        {
            itemSlots[removeFromSlot] = null;
            itemToDrop.isHeldByEnemy = false;
        }
        else if (itemToDrop != null)
        {
            Log($"failed to find {itemToDrop} in slot [{removeFromSlot}], calculating locally", 2);
            removeFromSlot = GetFirstSlot(itemToDrop, true);
            if (removeFromSlot == -1)
            {
                Log($"still failed to find itemToDrop in any slot!!", 3);
                return;
            }
            itemSlots[removeFromSlot] = null;
            itemToDrop.isHeldByEnemy = false;
        }
        else
        {
            Log($"RemoveItemFromInventoryLocal() called with invalid itemToDrop '{itemToDrop}' and removeFromSlot [{removeFromSlot}]!!", 3);
            return;
        }
        if (dropToGround && itemToDrop != null)
        {
            Log($"dropping {itemToDrop} #{NetworkObjectId}!!", 1);
            itemToDrop.grabbableToEnemies = true;
            itemToDrop.hasHitGround = false;
            itemToDrop.reachedFloorTarget = false;
            itemToDrop.parentObject = null;
            Vector3 startAt = itemToDrop.transform.position + Vector3.up;
            itemToDrop.startFallingPosition = itemToDrop.transform.parent != null ? itemToDrop.transform.parent.InverseTransformPoint(startAt) : startAt;
            itemToDrop.FallToGround();
            itemToDrop.EnableItemMeshes(true);
            itemToDrop.EnablePhysics(true);
        }
        CheckInventory();
    }

    private int GetFirstSlot(GrabbableObject getItem = null, bool printDebug = false)
    {
        int toReturn = -1;
        if (getItem == null)
        {
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] == null)
                {
                    toReturn = i;
                    break;
                }
            }
        }
        else
        {
            for (int i = 0; i < itemSlots.Length; i++)
            {
                if (itemSlots[i] == getItem)
                {
                    toReturn = i;
                    break;
                }
            }
        }
        if (printDebug)
        {
            Log($"GetFirstFreeSlot returned {toReturn}");
        }
        return toReturn;
    }

    public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
    {
        base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
        if (noiseID == 546)
        {
            return;
        }
        if (!IsOwner && noiseID == 75 && Time.realtimeSinceStartup - timeLastVoiceChatLocalPlayer > 3)
        {
            Log($"non-owner detected noise with ID {noiseID}, passing to server");
            timeLastVoiceChatLocalPlayer = Time.realtimeSinceStartup;
            PassNoiseToOwnerServerRpc(noisePosition);
        }
        else if (OnDetectNoiseValid())
        {
            DetectNoiseOnOwner(noisePosition);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PassNoiseToOwnerServerRpc(Vector3 noisePosition)
    {
        if (IsOwner)
        {
            DetectNoiseOnOwner(noisePosition);
        }
        else
        {
            PassNoiseToOwnerClientRpc(noisePosition);
        }
    }

    [ClientRpc]
    private void PassNoiseToOwnerClientRpc(Vector3 noisePosition)
    {
        if (IsOwner)
        {
            DetectNoiseOnOwner(noisePosition);
        }
    }

    private void DetectNoiseOnOwner(Vector3 noisePos)
    {
        DetectNewSighting(noisePos);
    }

    private void DetectNewSighting(Vector3 lookPos, bool lookImmediately = false)
    {
        timeLastTurningTo = Time.realtimeSinceStartup;
        turnTo = lookPos; 
        if (lookImmediately)
        {
            turnCompass.LookAt(lookPos);
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
        }
    }

    private bool OnDetectNoiseValid()
    {
        if (!IsOwner)
        {
            LogAI("DetectNoise(): not owner");
            return false;
        }
        if (!ventAnimationFinished)
        {
            LogAI("DetectNoise(): in vent animation");
            return false;
        }
        if (isEnemyDead)
        {
            LogAI("DetectNoise(): dead");
            return false;
        }
        if (inSpecialAnimation)
        {
            LogAI("DetectNoise(): inSpecialAnimation");
            return false;
        }
        if (stunNormalizedTimer > 0)
        {
            LogAI("DetectNoise(): stunNormalizedTimer");
            return false;
        }
        if (currentBehaviourStateIndex == 1 && targetPlayer != null)
        {
            LogAI($"DetectNoise(): docile");
            return false;
        }
        return true;
    }

    private void TurnToLookAt()
    {
        if (Time.realtimeSinceStartup - timeLastTurningTo < 1f)
        {
            turnCompass.LookAt(turnTo);
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
        }
    }

    public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (isEnemyDead)
        {
            return;
        }
        bool doRegularHitBehaviour = true;
        if (currentBehaviourStateIndex != 2 || targetPlayer == null)
        {
            GoIntoHostile(playerWhoHit);
        }
        else if (force <= 2 && (inSpecialAnimationWithPlayer != null || (!vulnerable && stunNormalizedTimer <= 0)))
        {
            doRegularHitBehaviour = false;
        }
        if (doRegularHitBehaviour)
        {
            SetEnemyStunned(true, stunTime, playerWhoHit);
            if (playerWhoHit == GameNetworkManager.Instance.localPlayerController)
            {
                localPlayerLikeMeter = -99;
            }
            if (IsOwner)
            {
                int newHp = enemyHP - force;
                SyncHPLocal(newHp);
                SyncHPServerRpc(newHp);
            }
        }
        else if (inSpecialAnimationWithPlayer == null)
        {
            if (playerWhoHit != null)
            {
                DetectNewSighting(playerWhoHit.transform.position, true);
            }
            SetAnimation("Block", IsOwner);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SyncHPServerRpc(int ownerHP)
    {
        SyncHPClientRpc(ownerHP);
    }

    [ClientRpc]
    private void SyncHPClientRpc(int ownerHP)
    {
        if (!IsOwner)
        {
            SyncHPLocal(ownerHP);
        }
    }

    private void SyncHPLocal(int newHP)
    {
        if (newHP < enemyHP)
        {
            PlaySFX(intimidateSFX, sync: false);
        }
        enemyHP = newHP;
        Log($"HP: {enemyHP}");
        if (enemyHP <= 0)
        {
            KillEnemyOnOwnerClient();
        }
    }

    public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null)
    {
        base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
        if (isEnemyDead || !enemyType.canBeStunned || stunNormalizedTimer > 0)
        {
            return;
        }
        PlaySFX(stunEnemySFX, false, sync: false);
        bool switchToHostile = false;
        PlayerControllerB withTargetPlayer = null;
        if (currentBehaviourStateIndex != 2)
        {
            switchToHostile = true;
        }
        if (setStunnedByPlayer != null)
        {
            if (setStunnedByPlayer == GameNetworkManager.Instance.localPlayerController)
            {
                localPlayerLikeMeter = -99;
            }
            withTargetPlayer = setStunnedByPlayer;
        }
        if (switchToHostile)
        {
            GoIntoHostile(withTargetPlayer);
        }
    }

    public override void KillEnemy(bool destroy = false)
    {
        base.KillEnemy(destroy);
        SetSpotlight(enemySpotlight, false, false);
        SetSpotlight(playerSpotlight, false, false);
        StartCoroutine(PlayBellDings(3, 0.4f, crowdKillCheer));
        if (IsServer)
        {
            Instantiate(bellItem.spawnPrefab, transform.position + Vector3.up * 2 + transform.right, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer).GetComponent<NetworkObject>().Spawn();
        }
        RoundManager.Instance.totalScrapValueInLevel += bellValue;
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        if (Time.realtimeSinceStartup - timeLastCollisionLocalPlayer > collisionCooldown)
        {
            PlayerControllerB collidedPlayer = MeetsStandardPlayerCollisionConditions(other, inSpecialAnimation);
            if (collidedPlayer != null && (currentBehaviourStateIndex != 2 || collidedPlayer == targetPlayer))
            {
                base.OnCollideWithPlayer(other);
                PerformPlayerCollision(collidedPlayer);
            }
        }
    }

    private void PerformPlayerCollision(PlayerControllerB localPlayer)
    {
        if (localPlayer.isClimbingLadder)
        {
            return;
        }
        timeLastCollisionLocalPlayer = Time.realtimeSinceStartup; 
        switch (currentBehaviourStateIndex)
        {
            case 0:
                break;
            case 1:
                break;
            case 2:
                if (localPlayer != targetPlayer)
                {
                    Log($"localPlayer {localPlayer} not targetPlayer {targetPlayer}, breaking");
                    break;
                }
                if (GetHoldingShovel())
                {
                    Log($"!!!GIVE SHOVEL HERE; heldShovel {heldShovel.name}");
                    StartCoroutine(StartGiveShovelAnim(localPlayer));
                }
                else
                {
                    Log($"{name} #{NetworkObjectId} calling PerformNextAttack");
                    PerformNextAttack();
                }
                break;
        }
    }

    private void PerformNextAttack()
    {
        DetectNewSighting(targetPlayer.transform.position, true);
        agent.speed = 0;
        int nextAttackIndex = currentAttackIndex + 1;
        int clampedAttackIndex = Mathf.Clamp(nextAttackIndex % currentAttackSequence.attacks.Length, 0, currentAttackSequence.attacks.Length - 1);
        AttackAnimationNames nextAttackTrigger = currentAttackSequence.attacks[clampedAttackIndex];
        int thisTriggerIndex = Mathf.Clamp((int)nextAttackTrigger, 0, currentAttackSequence.attacks.Length - 1);
        float nextStunTime = Mathf.Clamp(stunTimePerAttackEnum[thisTriggerIndex % stunTimePerAttackEnum.Length], 0, stunTimePerAttackEnum.Length - 1);
        SyncAttackAnimation(clampedAttackIndex, nextStunTime, atPos: transform.position);
    }

    private void SyncAttackAnimation(int ownerAttackIndex, float ownerStunTime, bool sync = true, bool onlySyncParameters = false, bool calculateInitiateAttack = false, Vector3 atPos = default)
    {
        if (!IsOwner)
        {
            return;
        }
        SyncAttackAnimationLocal(ownerAttackIndex, ownerStunTime, onlySyncParameters, calculateInitiateAttack, atPos);
        if (sync)
        {
            SyncAttackAnimationServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, ownerAttackIndex, ownerStunTime, onlySyncParameters, calculateInitiateAttack, atPos);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SyncAttackAnimationServerRpc(int sentBy, int ownerAttackIndex, float ownerStunTime, bool onlySyncParameters, bool calculateInitiateAttack, Vector3 atPos)
    {
        SyncAttackAnimationClientRpc(sentBy, ownerAttackIndex, ownerStunTime, onlySyncParameters, calculateInitiateAttack, atPos);
    }

    [ClientRpc]
    private void SyncAttackAnimationClientRpc(int sentBy, int ownerAttackIndex, float ownerStunTime, bool onlySyncParameters, bool calculateInitiateAttack, Vector3 atPos)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            SyncAttackAnimationLocal(ownerAttackIndex, ownerStunTime, onlySyncParameters, calculateInitiateAttack, atPos);
        }
    }

    private void SyncAttackAnimationLocal(int nextAttackIndex, float nextStunTime, bool onlySyncParameters = false, bool calculateInitiateAttack = false, Vector3 ownerServerPos = default)
    {
        if (calculateInitiateAttack)
        {
            initiatedHostileToLocalPlayer = targetPlayer == GameNetworkManager.Instance.localPlayerController;
            Log($"initiatedHostileToLocalPlayer? {initiatedHostileToLocalPlayer}", 1);
        }
        stunTime = nextStunTime;
        currentAttackIndex = nextAttackIndex;
        Log($"index: {currentAttackIndex} | stunTime: {stunTime}");
        if (onlySyncParameters)
        {
            return;
        }
        managerHitbox.OnAttackStart();
        inSpecialAnimationPreVulnerable = true;
        if (ownerServerPos != default)
        {
            serverPosition = ownerServerPos;
            positionLastInterval = ownerServerPos;
        }
        SetEnemyInSpecialAnimation(true);
        SetEnemyVulnerable(false);
        AttackAnimationNames currentAttackTrigger = currentAttackSequence.attacks[currentAttackIndex];
        Log($"trigger: {currentAttackTrigger}");
        PlaySFX(reelSFX, false, false, false);
        PlaySFX(crowdHubbubs[(int)currentAttackTrigger % crowdHubbubs.Length], false, true, false);
        SetAnimation(currentAttackTrigger.ToString(), false);
    }

    public bool OnHitSuccessful(int[] hitPlayerIDs)
    {
        if (hitPlayerIDs == null || hitPlayerIDs.Length == 0)
        {
            return false;
        }
        bool killedAnyPlayer = false;
        for (int i = 0; i < hitPlayerIDs.Length; i++)
        {
            if (StartOfRound.Instance.allPlayerScripts[hitPlayerIDs[i]].isPlayerDead)
            {
                killedAnyPlayer = true;
                break;
            }
        }
        if (!killedAnyPlayer)
        {
            return false;
        }
        StartCoroutine(PlayBellDings(5, 0.2f, crowdKillCheer, true));
        if (targetPlayer.isPlayerDead)
        {
            SetSpotlight(playerSpotlight, true, false);
        }
        SetAnimation("Taunt");
        return true;
    }

    public void SetEnemyVulnerable(bool setVulnerableTo)
    {
        vulnerable = setVulnerableTo;
        LogAI($"SetEnemyVulnerable(): vulnerable = {vulnerable}");
    }

    public void SetEnemyInSpecialAnimation(bool setinSpecialAnimationTo)
    {
        inSpecialAnimation = setinSpecialAnimationTo;
        LogAI($"SetEnemyInSpecialAnimationTo(): inSpecialAnimation = {inSpecialAnimation}");
    }

    public void SpawnShovelAndSync()
    {
        if (!IsServer)
        {
            return;
        }
        GameObject spawnedShovel = Instantiate(AssetsCollection.shovelItem.spawnPrefab);
        if (spawnedShovel == null)
        {
            Log("failed to instantiate shovel on server");
            return;
        }
        heldShovel = spawnedShovel.GetComponent<Shovel>();
        if (heldShovel == null)
        {
            Log("error spawning shovel on host", 3);
            return;
        }
        heldShovel.hasHitGround = true;
        heldShovel.reachedFloorTarget = true;
        heldShovel.isInFactory = true;
        heldShovel.parentObject = shovelParent;
        HoarderBugAI.grabbableObjectsInMap.Add(spawnedShovel);
        NetworkObject netObj = heldShovel.NetworkObject;
        netObj.Spawn();
        Log($"spawned shovel on host", 1);
        SpawnShovelClientRpc(netObj);
    }

    [ClientRpc]
    private void SpawnShovelClientRpc(NetworkObjectReference itemNOR)
    {
        if (!IsServer)
        {
            StartCoroutine(WaitForShovelToSpawnOnClient(itemNOR));
        }
    }

    private IEnumerator WaitForShovelToSpawnOnClient(NetworkObjectReference itemNOR)
    {
        NetworkObject netObj = null;
        float startTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startTime < 8 && !itemNOR.TryGet(out netObj))
        {
            yield return new WaitForSeconds(0.03f);
        }
        if (netObj == null)
        {
            Log("failed to get Shovel netObj on client!", 3);
            yield break;
        }
        yield return new WaitForEndOfFrame();
        GameObject spawnedShovel = netObj.gameObject;
        heldShovel = netObj.GetComponent<Shovel>();
        if (heldShovel == null)
        {
            Log("error spawning shovel on client", 3);
            yield break;
        }
        heldShovel.hasHitGround = true;
        heldShovel.reachedFloorTarget = true;
        heldShovel.isInFactory = true;
        heldShovel.parentObject = shovelParent;
        HoarderBugAI.grabbableObjectsInMap.Add(spawnedShovel);
        Log($"spawned shovel on client", 1);
    }

    private IEnumerator StartGiveShovelAnim(PlayerControllerB collidedPlayer)
    {
        if (heldShovel == null)
        {
            Log($"SHOVEL SCRIPT ON heldShovel {heldShovel} COULD NOT BE FOUND, returning", 3);
            inSpecialAnimation = false;
            yield break;
        }
        if (collidedPlayer == null)
        {
            Log($"collidedPlayer on StartGiveShovel() for some reason null, returning", 3);
            inSpecialAnimation = false;
            yield break;
        }
        PlaySFX(reelSFX);
        collidedPlayer.CancelSpecialTriggerAnimations();
        yield return null;
        collidedPlayer.ResetFallGravity();
        collidedPlayer.isCrouching = false;
        collidedPlayer.playerBodyAnimator.SetBool("crouching", false);
        collidedPlayer.playerBodyAnimator.SetBool("Walking", false);
        collidedPlayer.playerBodyAnimator.SetBool("Sprinting", false);
        collidedPlayer.playerBodyAnimator.SetBool("Sideways", false);
        collidedPlayer.playerBodyAnimator.SetBool("hinderedMovement", false);
        collidedPlayer.playerBodyAnimator.SetBool("Jumping", false);
        collidedPlayer.playerBodyAnimator.SetBool("FallNoJump", false);
        collidedPlayer.playerBodyAnimator.SetBool("Limp", false);
        collidedPlayer.DropAllHeldItemsAndSync();
        collidedPlayer.inSpecialInteractAnimation = true;
        collidedPlayer.inAnimationWithEnemy = this;
        inSpecialAnimationWithPlayer = collidedPlayer;
        SetAnimWithPlayerServerRpc((int)collidedPlayer.playerClientId, true, transform.position);
        SetEnemyInSpecialAnimation(true);
        heldShovel.grabbable = false;
        heldShovel.parentObject = itemParents[1];
        RoundManager.Instance.tempTransform.position = collidedPlayer.transform.position;
        RoundManager.Instance.tempTransform.LookAt(transform.position);
        Quaternion startingPlayerRot = collidedPlayer.transform.rotation;
        Quaternion targetPlayerRot = RoundManager.Instance.tempTransform.rotation;
        for (int i = 0; i < turnPlayerIterations; i++)
        {
            collidedPlayer.transform.rotation = Quaternion.Lerp(startingPlayerRot, targetPlayerRot, (float)i / (float)turnPlayerIterations);
            collidedPlayer.transform.eulerAngles = new Vector3(0f, collidedPlayer.transform.eulerAngles.y, 0f);
            yield return null;
        }
        DetectNewSighting(collidedPlayer.transform.position, true);
        SetSpotlight(enemySpotlight);
        SetSpotlight(playerSpotlight);
        SetAnimation("GiveShovel");
    }

    public void EndGiveShovelAnim()
    {
        PlayerControllerB collidedPlayer = inSpecialAnimationWithPlayer;
        if (collidedPlayer == null)
        {
            Log($"collidedPlayer null! cannot call GiveShovel and CancelSpecialAnimationWithPlayer");
            inSpecialAnimation = false;
            return;
        }
        StartCoroutine(GiveShovelLocal(collidedPlayer));
        
    }

    private IEnumerator GiveShovelLocal(PlayerControllerB giveToPlayer)
    {
        Log($"GIVING SHOVEL TO {giveToPlayer}!!!", 2);
        if (giveToPlayer == null)
        {
            Log("error finding player", 3);
            yield break;
        }
        if (heldShovel == null)
        {
            Log("error finding shovel script", 3);
            yield break;
        }
        giveToPlayer.ItemSlots[giveToPlayer.currentItemSlot] = heldShovel;
        giveToPlayer.playerBodyAnimator.SetBool(heldShovel.itemProperties.grabAnim, true);
        giveToPlayer.playerBodyAnimator.SetBool("GrabValidated", true);
        giveToPlayer.playerBodyAnimator.SetBool("cancelHolding", false);
        giveToPlayer.playerBodyAnimator.ResetTrigger("SwitchHoldAnimationTwoHanded");
        giveToPlayer.playerBodyAnimator.SetTrigger("SwitchHoldAnimationTwoHanded");
        giveToPlayer.itemAudio.PlayOneShot(heldShovel.itemProperties.grabSFX);
        giveToPlayer.currentlyHeldObject = heldShovel;
        giveToPlayer.currentlyHeldObjectServer = heldShovel;
        giveToPlayer.twoHanded = heldShovel.itemProperties.twoHanded;
        giveToPlayer.twoHandedAnimation = heldShovel.itemProperties.twoHandedAnimation;
        giveToPlayer.isHoldingObject = true;
        giveToPlayer.carryWeight = Mathf.Clamp(giveToPlayer.carryWeight + (heldShovel.itemProperties.weight - 1f), 1f, 10f);
        giveToPlayer.ResetFallGravity();
        giveToPlayer.externalForceAutoFade = transform.forward * 20 + Vector3.up * 20;
        if (giveToPlayer == GameNetworkManager.Instance.localPlayerController)
        {
            HUDManager.Instance.itemSlotIcons[giveToPlayer.currentItemSlot].sprite = heldShovel.itemProperties.itemIcon;
            HUDManager.Instance.itemSlotIcons[giveToPlayer.currentItemSlot].enabled = true;
        }
        CancelSpecialAnimationWithPlayer();
        heldShovel.parentObject = giveToPlayer == GameNetworkManager.Instance.localPlayerController ? giveToPlayer.localItemHolder : giveToPlayer.serverItemHolder;
        heldShovel.isHeld = true;
        heldShovel.playerHeldBy = giveToPlayer;
        heldShovel.grabbable = true;
        heldShovel.transform.localScale = heldShovel.originalScale;
        heldShovel.EnableItemMeshes(true);
        heldShovel.EnablePhysics(false);
        heldShovel.GrabItemOnClient();
        heldShovel.EquipItem();
        if (IsServer)
        {
            try
            {
                heldShovel.NetworkObject.ChangeOwnership(giveToPlayer.actualClientId);
            }
            catch
            {
                Log("failed to ChangeOwnership to new player!", 3);
            }
        }
        Log("reached end of GiveShovelLocal()");
        yield return null;
        timeLastCollisionLocalPlayer = Time.realtimeSinceStartup - collisionCooldown + 1;
        InitiateAttackSequence();
        yield return new WaitForSeconds(0.33f);
        SetAnimation("Taunt", false);
    }

    private void InitiateAttackSequence(bool playAudioVisual = true, bool sync = true)
    {
        Log("INITIATE ATTACK SEQUENCE!!!", 3);
        managerHitbox.DisableAllHitboxes();
        SyncAttackAnimation(-1, 2.5f, sync, true, true);
        if (playAudioVisual)
        {
            SetSpotlight(enemySpotlight, sync);
            StartCoroutine(PlayBellDings(2, 0.25f, crowdStartCheer, sync));
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetAnimWithPlayerServerRpc(int playerID, bool isGiveShovelAnim = false, Vector3 ownerPos = default)
    {
        SetAnimWithPlayerClientRpc(playerID, isGiveShovelAnim, ownerPos);
    }

    [ClientRpc]
    private void SetAnimWithPlayerClientRpc(int playerID, bool isGiveShovelAnim = false, Vector3 ownerPos = default)
    {
        if (playerID == (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            return;
        }
        serverPosition = ownerPos;
        if (playerID < 0 || playerID >= StartOfRound.Instance.allPlayerScripts.Length)
        {
            inSpecialAnimationWithPlayer = null;
            SetEnemyInSpecialAnimation(false);
        }
        else
        {
            PlayerControllerB inAnimWith = StartOfRound.Instance.allPlayerScripts[playerID];
            inAnimWith.inAnimationWithEnemy = this;
            inAnimWith.inSpecialInteractAnimation = true;
            inSpecialAnimationWithPlayer = inAnimWith;
            SetEnemyInSpecialAnimation(true);
            if (isGiveShovelAnim && heldShovel != null)
            {
                heldShovel.grabbable = false;
                heldShovel.parentObject = itemParents[1];
            }
        }
    }

    public IEnumerator PlayBellDings(int amountOfDings, float delayBetweenDings, AudioClip playCrowdSFX = null, bool sync = false)
    {
        int performedDings = 0;
        while (performedDings < amountOfDings)
        {
            PlaySFX(bellSFX, false, true, sync);
            performedDings++;
            if (performedDings < amountOfDings)
            {
                yield return new WaitForSeconds(delayBetweenDings);
            }
        }
        yield return new WaitForSeconds(0.2f);
        if (playCrowdSFX != null)
        {
            PlaySFX(playCrowdSFX, false, true, sync);
        }
    }

    public void SetSpotlight(Light lightToSet, bool sync = true, bool enableLight = true)
    {
        bool setEnemyLight = lightToSet == enemySpotlight;
        SetSpotlightLocal(setEnemyLight, enableLight);
        if (sync)
        {
            SetSpotlightServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, setEnemyLight, enableLight);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetSpotlightServerRpc(int sentBy, bool setEnemyLight, bool enableLight)
    {
        SetSpotlightClientRpc(sentBy, setEnemyLight, enableLight);
    }

    [ClientRpc]
    private void SetSpotlightClientRpc(int sentBy, bool setEnemyLight, bool enableLight)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            SetSpotlightLocal(setEnemyLight, enableLight);
        }
    }

    private void SetSpotlightLocal(bool setEnemyLight, bool enableLight = true)
    {
        Light lightToSet = setEnemyLight ? enemySpotlight : playerSpotlight;
        if (lightToSet == null)
        {
            return;
        }
        Log($"starting SetSpotlightLocal with setEnemyLight {setEnemyLight} | enableLight {enableLight}");
        lightToSet.enabled = enableLight;
        if (enableLight)
        {
            PlaySFXLocal(spotlightSFX, false, true, lightToSet.transform.position - Vector3.up * 10);
        }
    }

    public void SetAnimation(string animString = null, bool sync = true, bool boolAnim = false, bool boolVal = true, string paramString = null, float paramFloat = 1.0f)
    {
        SetAnimationLocal(animString, boolAnim, boolVal, paramString, paramFloat);
        if (sync)
        {
            SetAnimationServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, animString, boolAnim, boolVal, paramString, paramFloat);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetAnimationServerRpc(int sentBy, string animString, bool boolAnim, bool boolVal, string paramString, float paramFloat)
    {
        SetAnimationClientRpc(sentBy, animString, boolAnim, boolVal, paramString, paramFloat);
    }

    [ClientRpc]
    private void SetAnimationClientRpc(int sentBy, string animString, bool boolAnim, bool boolVal, string paramString, float paramFloat)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            SetAnimationLocal(animString, boolAnim, boolVal, paramString, paramFloat);
        }
    }

    private void SetAnimationLocal(string animString, bool boolAnim, bool boolVal, string paramString, float paramFloat)
    {
        if (boolAnim)
        {
            if (!string.IsNullOrEmpty(animString))
            {
                creatureAnimator.SetBool(animString, boolVal);
                UpdateAnimStateInt(animString, boolVal);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(animString))
            {
                creatureAnimator.SetTrigger(animString);
                UpdateAnimStateInt(animString);
            }
        }
        
        if (!string.IsNullOrEmpty(paramString))
        {
            creatureAnimator.SetFloat(paramString, paramFloat);
        }
    }

    private void UpdateAnimStateInt(string animString, bool boolVal = true)
    {
        int setTo = -1;
        switch (animString)
        {
            default:
                if (animString.StartsWith("Boxer"))
                {
                    setTo = 2;
                }
                break;
            case "Sitting":
                setTo = 0;
                break;
            case "Hunched":
                setTo = 1;
                break;
            case "Upright":
                setTo = 2;
                break;
            case "GiveShovel":
                setTo = 2;
                break;
            case "GrabItem":
                setTo = 2;
                break;
            case "Block":
                setTo = 2;
                break;
            case "WaveGoodbye":
                setTo = 3;
                break;
            case "Stunned":
                setTo = boolVal ? 4 : 2;
                break;
        }
        if (setTo != -1 && setTo != animState)
        {
            animState = setTo;
            LogAI($"UpdateAnimStateInt({animString}) successfully updated to {animState}");
        }
    }

    public void PlaySFX(AudioClip clipToPlay = null, bool audibleNoise = true, bool isAudience = false, bool sync = true, int clipCase = -1)
    {
        if (clipToPlay == null)
        {
            clipToPlay = GetClipOfInt(clipCase);
        }
        PlaySFXLocal(clipToPlay, audibleNoise, isAudience);
        if (sync)
        {
            PlaySFXServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, GetIntOfClip(clipToPlay), audibleNoise, isAudience);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaySFXServerRpc(int sentBy, int clipCase, bool audible, bool crowd)
    {
        PlaySFXClientRpc(sentBy, clipCase, audible, crowd);
    }

    [ClientRpc]
    private void PlaySFXClientRpc(int sentBy, int clipCase, bool audible, bool crowd)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            PlaySFXLocal(GetClipOfInt(clipCase), audible, crowd);
        }
    }

    private void PlaySFXLocal(AudioClip clipToPlay, bool audibleNoise, bool isAudience, Vector3 audiencePosition = default)
    {
        AudioSource sourceToPlay = creatureSFX;
        if (isAudience)
        {
            if (audiencePosition == default)
            {
                audiencePosition = transform.position;
            }
            sourceToPlay = boxerAudience;
            boxerAudience.transform.position = audiencePosition;
        }
        sourceToPlay.PlayOneShot(clipToPlay);
        WalkieTalkie.TransmitOneShotAudio(sourceToPlay, clipToPlay);
        if (audibleNoise)
        {
            RoundManager.Instance.PlayAudibleNoise(transform.position, 20);
        }
    }

    private AudioClip GetClipOfInt(int ofInt)
    {
        switch (ofInt)
        {
            default:
                return intimidateSFX;
            case 1:
                return bellSFX;
            case 2:
                return punchSFX;
            case 3:
                return stunPlayersSFX;
            case 4:
                return stunEnemySFX;
            case 5:
                return reelSFX;
            case 6:
                return blockSFX;
            case 7:
                return crowdStartCheer;
            case 8:
                return crowdKillCheer;
        }
    }

    private int GetIntOfClip(AudioClip ofClip)
    {
        if (ofClip == bellSFX) return 1;
        if (ofClip == punchSFX) return 2;
        if (ofClip == stunPlayersSFX) return 3;
        if (ofClip == stunEnemySFX) return 4;
        if (ofClip == reelSFX) return 5;
        if (ofClip == blockSFX) return 6;
        if (ofClip == crowdStartCheer) return 7;
        if (ofClip == crowdKillCheer) return 8;
        else return 0;
    }

    private bool SetTargetPlayer(PlayerControllerB player)
    {
        if (!IsOwner)
        {
            return false;
        }
        if (Time.realtimeSinceStartup - timeLastSwitchingTarget < minTargetFocusTime)
        {
            return false;
        }
        if (player == null && targetPlayer != null)
        {
            Log("owner setting targetPlayer to null");
            SetTargetPlayerLocal(null);
            SetTargetPlayerServerRpc(-1);
            return false;
        }
        else if (player != null && (targetPlayer == null || player.OwnerClientId != NetworkObject.OwnerClientId))
        {
            Log($"found new player for SetPlayerAsOwner {player.playerUsername}", 2);
            ChangeOwnershipOfEnemy(player.actualClientId);
            SetTargetPlayerLocal(player);
            SetTargetPlayerServerRpc((int)player.playerClientId);
            return true;
        }
        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetTargetPlayerServerRpc(int playerID)
    {
        SetTargetPlayerClientRpc(playerID);
    }

    [ClientRpc]
    private void SetTargetPlayerClientRpc(int playerID)
    {
        PlayerControllerB newTarget = playerID == -1 ? null : StartOfRound.Instance.allPlayerScripts[playerID];
        SetTargetPlayerLocal(newTarget);
    }

    private void SetTargetPlayerLocal(PlayerControllerB player)
    {
        targetPlayer = player;
        timeLastSwitchingTarget = Time.realtimeSinceStartup;
        Log($"targetPlayer = {targetPlayer}");
    }

    private void SetTargetEnemy(EnemyAI enemy, bool sync = true)
    {
        SetTargetEnemyLocal(enemy);
        if (sync)
        {
            if (enemy != null)
            {
                SetTargetEnemyServerRpc(enemy.NetworkObject, (int)GameNetworkManager.Instance.localPlayerController.playerClientId);
            }
            else
            {
                SetTargetEnemyServerRpc();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetTargetEnemyServerRpc(NetworkObjectReference enemyNOR, int playerID)
    {
        SetTargetEnemyClientRpc(enemyNOR, playerID);
    }

    [ClientRpc]
    private void SetTargetEnemyClientRpc(NetworkObjectReference enemyNOR, int playerID)
    {
        if (playerID == (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            return;
        }
        if (enemyNOR.TryGet(out var netObj))
        {
            EnemyAI enemy = netObj.GetComponent<EnemyAI>();
            if (enemy == null)
            {
                Log("failed to get script from enemyNOR", 3);
                return;
            }
            SetTargetEnemyLocal(enemy);
        }
        else
        {
            Log("failed to get netObj from enemyNOR", 3);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetTargetEnemyServerRpc()
    {
        SetTargetEnemyClientRpc();
    }

    [ClientRpc]
    private void SetTargetEnemyClientRpc()
    {
        SetTargetEnemyLocal(null);
    }

    private void SetTargetEnemyLocal(EnemyAI enemy)
    {
        targetEnemy = enemy;
        Log($"targetEnemy = {targetEnemy}");
    }

    private void OnDisable()
    {
        if (IsServer && heldShovel != null && GetHoldingShovel())
        {
            NetworkObject netObj = heldShovel.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                Log($"despawning netObj of {heldShovel}", 2);
                netObj.Despawn();
            }
            else
            {
                Log($"could not find netObj of {heldShovel}, destroying instead", 3);
                Destroy(heldShovel);
            }
        }
    }

    //Useful for once-off information that needs to be distuinguishable in the debug log
    private void Log(string message, int type = 0)
    {
        if (!DebugEnemy)
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
        if (!debugEnemyAI)
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
