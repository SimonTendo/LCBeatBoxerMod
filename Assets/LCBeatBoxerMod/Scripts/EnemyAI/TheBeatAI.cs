using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using GameNetcodeStuff;
using BepInEx.Logging;

public class TheBeatAI : EnemyAI
{
    private static ManualLogSource Logger = Plugin.Logger;

    private float tempTimer;
    private bool setItemLocally;
    private bool reachedHideSpot;
    private bool everyoneOutside;
    private bool seenDuringThisAmbush;
    private int timesStartedAmbush;
    private bool fakeAudioThisAmbush;
    private bool enraged;
    private bool attemptingItemRetrieve;
    private int hpAmbushStart;
    private float timeLastCollisionLocalPlayer;
    private float timeLastVoiceChatLocalPlayer;
    private float timeLastLookingAt;
    private Transform fleeToWhileCalculating;
    private Vector3 lookingAt;

    [Space(3f)]
    [Header("SPEEDS")]
    public float roamSpeed;
    public float hideSpeed;
    public float chaseSpeed;
    public float enragedSpeed;

    [Space(3f)]
    [Header("SENSES")]
    public int seeDistance;
    public float seeWidth;
    public int[] awarenessDistancePerState;
    public float noiseThreshold;
    public float noiseLookThreshold;
    public float noiseDistanceDropoffPower;
    public Transform turnCompass;
    public Collider killBox;

    [Space(3f)]
    [Header("SFX")]
    [Header("Enemy")]
    public AudioSource realAudio;
    public AudioClip intimidateSFX;
    public AudioClip punchHitSFX;
    public AudioClip punchMissSFX;
    public AudioClip caughtSFX;
    public AudioClip reelSFX;
    [Range(0f, 1f)]
    public float footstepsVolume;
    [Range(0f, 1f)]
    public float runningVolume;
    public PlayAudioAnimationEvent animationAudio;
    [Header("Audience")]
    public AudioSource crowdVoices;
    public static AudioSource beatAudience;
    public AudioClip successCheer;

    [Space(3f)]
    [Header("ITEM")]
    public Item itemToSpawn;
    public BeatAudioItem linkedItem;
    public Transform holdItemParentObject;
    public Coroutine pickItemPositionCoroutine;

    [Space(3f)]
    [Header("PARAMETERS")]
    public float maxSearchDistance;
    public float minDistanceBetweenFarawayNodes;
    public float maxDistanceBetweenFarawayNodes;
    public float verticalOffsetDropoffDivider;
    public float maxPlayerDistanceToPath;
    public float maxItemSpawnTime;
    public float maxHideSpotTime;
    public float minNodesNecessary;
    public float collisionCooldown;
    public int turnPlayerIterations;

    [Space(3f)]
    [Header("DEBUG")]
    public GameObject debugNodeLightPrefab;
    public GameObject debugEyeForward;

    public override void Start()
    {
        base.Start();
        hpAmbushStart = enemyHP;
        if (beatAudience == null)
        {
            crowdVoices.transform.SetParent(RoundManager.Instance.spawnedScrapContainer);
            beatAudience = crowdVoices;
        }
        DebugEnemy = Plugin.DebugLogLevel() >= 1;
        debugEnemyAI = Plugin.DebugLogLevel() == 2;
    }

    public override void Update()
    {
        base.Update();
        if (isEnemyDead || !ventAnimationFinished)
        {
            return;
        }
        debugEyeForward.transform.position = eye.position + eye.forward * 2; 
        switch (currentBehaviourStateIndex)
        {
            case 0:
                syncMovementSpeed = 0.42f;
                animationAudio.playAudibleNoise = false;
                if (Time.realtimeSinceStartup - timeLastLookingAt < 1f)
                {
                    turnCompass.LookAt(lookingAt);
                    transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
                }
                break;
            case 1:
                syncMovementSpeed = 0.13f;
                animationAudio.playAudibleNoise = false;
                if (StartOfRound.Instance.localPlayerController.HasLineOfSightToPosition(eye.position))
                {
                    StartOfRound.Instance.localPlayerController.IncreaseFearLevelOverTime(0.4f, 0.5f);
                }
                break;
            case 2:
                syncMovementSpeed = 0.19f;
                animationAudio.playAudibleNoise = true;
                break;
        }
        if (!IsOwner)
        {
            return;
        }
        if (currentBehaviourStateIndex == 1 || (setItemLocally && (!reachedHideSpot || everyoneOutside)) || (currentBehaviourStateIndex == 2 && targetPlayer == null))
        {
            tempTimer += Time.deltaTime;
        }
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (isEnemyDead || !ventAnimationFinished || StartOfRound.Instance.allPlayersDead)
        {
            return;
        }
        if (tempTimer == 99)
        {
            ResetVariables(true);
            return;
        }
        useSecondaryAudiosOnAnimatedObjects = currentBehaviourStateIndex == 2;
        switch (currentBehaviourStateIndex)
        {
            case 0:
                if (setItemLocally)
                {
                    if (currentSearch.inProgress)
                    {
                        Log("STOP search (0)");
                        StopSearch(currentSearch);
                    }
                    
                    if (!reachedHideSpot)
                    {
                        if (favoriteSpot != null)
                        {
                            LogAI("going to HIDE");
                            SetDestinationToPosition(favoriteSpot.position);
                            agent.speed = hideSpeed;
                            if (tempTimer > maxHideSpotTime)
                            {
                                Log("TIMER: Unable to reach favoriteSpot, pausing", 3);
                                OnReachHideSpot();
                                tempTimer = 0;
                            }
                            else if (Vector3.Distance(transform.position, favoriteSpot.position) < 1)
                            {
                                Log($"reached hide spot, starting sound");
                                OnReachHideSpot();
                            }
                        }
                    }
                    else
                    {
                        if (PlayersOutside(false, true))
                        {
                            if (tempTimer > 10f)
                            {
                                Log($"currentBehaviourStateIndex: {currentBehaviourStateIndex} | reachedHideSpot: {reachedHideSpot} | tempTimer: {tempTimer}");
                                ResetVariables();
                                break;
                            }
                        }
                        else
                        {
                            tempTimer = 0f;
                        }
                        PlayerControllerB playerSeeing = AnyPlayerHasLineOfSight();
                        if (playerSeeing != null)
                        {
                            Log($"playerSeeing: {playerSeeing} | reachedHideSpot: {reachedHideSpot} | targetPlayer & moving: {targetPlayer} | {movingTowardsTargetPlayer}");
                            targetPlayer = playerSeeing;
                            GoIntoChase();
                            break;
                        }
                        PlayerControllerB closestPlayer = GetClosestPlayer(true);
                        if (closestPlayer != null && Vector3.Distance(transform.position, closestPlayer.transform.position) < 5)
                        {
                            Log($"distance between Enemy and ClosestPlayer below 5, moving to chase");
                            targetPlayer = closestPlayer;
                            GoIntoChase();
                            break;
                        }
                    }

                    
                }
                else if (!currentSearch.inProgress)
                {
                    Log("START search (0)");
                    StartSearch(transform.position);
                    SetAnimation("WalkHunched");
                    agent.speed = roamSpeed;
                    ToggleAudioServerRpc(footstepsVolume, roamSpeed);
                }
                else
                {
                    PlayerControllerB checkPlayerInLOS = CheckLineOfSightForPlayer(seeWidth, seeDistance);
                    if (checkPlayerInLOS != null)
                    {
                        Log("enemy spotted player, moving to state 1");
                        MoveToAmbush(checkPlayerInLOS);
                    }
                }
                break;
            case 1:
                if (currentSearch.inProgress)
                {
                    StopSearch(currentSearch);
                }
                if (inSpecialAnimation)
                {
                    agent.speed = 0;
                    break;
                }
                PlayerControllerB playerSeeingInHide = AnyPlayerHasLineOfSight(null, transform.position);
                if (playerSeeingInHide != null)
                {
                    DetectNewSighting(playerSeeingInHide.transform.position, true);
                    Log($"playerSeeingInHide {playerSeeingInHide}");
                    if (!seenDuringThisAmbush && !enraged)
                    {
                        agent.speed = chaseSpeed;
                        seenDuringThisAmbush = true;
                        enraged = true;
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        Log("CHASE PLAYER UNTIL OUT OF SIGHT!!!", 3);
                        ToggleAudioServerRpc(runningVolume, chaseSpeed);
                        agent.speed = 0;
                        SetAnimation("Intimidate");
                        SetAnimation(null, true, "WalkUprightSpeedMultiplier", 3);
                        break;
                    }
                }
                else if (enraged)
                {
                    Log("player lost sight during ambush chase");
                    enraged = false;
                    ToggleAudioServerRpc(0.0f, hideSpeed);
                    SetAnimation(null, true, "WalkUprightSpeedMultiplier", 5);
                }
                if (!enraged && realAudio.volume > 0)
                {
                    ToggleAudioServerRpc(0.0f, hideSpeed);
                }
                if (attemptingItemRetrieve && !enraged)
                {
                    if (!GetValidItemRetrieve())
                    {
                        Log($"GetValidItemRetrieve() interrupted attemptingItemRetrieve!!! setting up for new item!", 1);
                        SeverItemLinkServerRpc();
                        attemptingItemRetrieve = false;
                        break;
                    }
                    LogAI($"Trying to retrieve item {linkedItem}!");
                    SetDestinationToPosition(linkedItem.transform.position);
                    float distanceToItem = Vector3.Distance(holdItemParentObject.position, linkedItem.transform.position);
                    if (distanceToItem < 4)
                    {
                        Log($"owner grabbing {linkedItem} #{linkedItem.NetworkObjectId}", 1);
                        GrabLinkedItem();
                        attemptingItemRetrieve = false;
                    }
                }
                if (targetNode == null)
                {
                    if (pickItemPositionCoroutine == null)
                    {
                        pickItemPositionCoroutine = StartCoroutine(PickItemPosition());
                    }
                    agent.speed = hideSpeed;
                    if (!attemptingItemRetrieve)
                    {
                        if (fleeToWhileCalculating == null)
                        {
                            fleeToWhileCalculating = GetFleeToWhileCalculating(playerSeeingInHide);
                        }
                        else if (!enraged)
                        {
                            LogAI($"trying to hide out of sight");
                            agent.speed = hideSpeed;
                            SetDestinationToPosition(fleeToWhileCalculating.position);
                        }
                    }
                }
                else if (!enraged && !attemptingItemRetrieve)
                {
                    if (!fakeAudioThisAmbush)
                    {
                        Log("spawn by: MODULO", 2);
                        SpawnItemAndHide();
                        break;
                    }
                    SetDestinationToPosition(targetNode.position, true);
                    if (Vector3.Distance(transform.position, targetNode.position) < 1)
                    {
                        Log("spawn by: DISTANCE");
                        SpawnItemAndHide();
                    }
                    else if (tempTimer > maxItemSpawnTime)
                    {
                        Log("spawn by: TIMER", 3);
                        SpawnItemAndHide();
                    }
                }
                break;
            case 2:
                if (currentSearch.inProgress)
                {
                    Log($"STOP search (2)");
                    StopSearch(currentSearch);
                }
                PlayerControllerB playerHoldingItem = GetValidPlayerHoldingItem();
                bool unlimitedAwareness = enraged || playerHoldingItem != null;
                if (PlayersOutside(true, unlimitedAwareness) && tempTimer > 5)
                {
                    ResetVariables();
                    break;
                }
                if (!inSpecialAnimation)
                {
                    if (stunNormalizedTimer > 0)
                    {
                        agent.speed = 0;
                        if (realAudio.volume != 0.0f)
                        {
                            ToggleAudioServerRpc(0.0f, 0.0f);
                        }
                        if (stunnedByPlayer != null)
                        {
                            targetPlayer = stunnedByPlayer;
                        }
                        Log($"stunned: volume: {realAudio.volume} | targetPlayer {targetPlayer}");
                        break;
                    }
                    agent.speed = enraged ? enragedSpeed : chaseSpeed;
                    if (targetPlayer == null || (playerHoldingItem != null && targetPlayer != playerHoldingItem))
                    {
                        if (playerHoldingItem != null)
                        {
                            targetPlayer = playerHoldingItem;
                        }
                        else
                        {
                            targetPlayer = GetClosestPlayer(true);
                        }
                    }
                    else if (PlayersOutside(true, unlimitedAwareness))
                    {
                        tempTimer = 0;
                        targetPlayer = null;
                        movingTowardsTargetPlayer = false;
                    }
                    Log($"going to PLAYER ({targetPlayer})");
                    ChaseNewPlayer(targetPlayer);
                    if (realAudio.volume != runningVolume)
                    {
                        ToggleAudioServerRpc(runningVolume, agent.speed);
                    }
                }
                else
                {
                    agent.speed = 0;
                }
                if (targetPlayer == StartOfRound.Instance.localPlayerController && Vector3.Distance(transform.position, targetPlayer.transform.position) < realAudio.maxDistance * 0.7f)
                {
                    targetPlayer.JumpToFearLevel(0.7f, true);
                }
                break;
        }
    }

    private IEnumerator PickItemPosition()
    {
        Log("STARTING COROUTINE PickItemPosition()!!!", 3);
        LogAI($"allAINodes.Length = {allAINodes.Length}", 1);
        Vector3 startPos = transform.position;
        if (debugEnemyAI)
        {
            Instantiate(debugNodeLightPrefab, startPos, Quaternion.identity);
        }

        List<GameObject> possibleHideNodes = new List<GameObject>();

        PlayerControllerB fromPlayer = targetPlayer != null ? targetPlayer : GetClosestPlayer();

        for (int a = 0; a < allAINodes.Length; a++)
        {
            yield return null;
            GameObject checkingNode = allAINodes[a];
            Light spawnedLight = null;
            Color colorLightToSpawn = Color.red;
            if (debugEnemyAI)
            {
                spawnedLight = Instantiate(debugNodeLightPrefab, checkingNode.transform.position, Quaternion.identity).GetComponent<Light>();
            }
            LogAI($"A) checking node {checkingNode} {checkingNode.transform.position}", 0);

            bool nodeOutOfReach = false;
            float ownDistanceToNode = Vector3.Distance(startPos, checkingNode.transform.position);
            NavMeshPath pathToNode = new NavMeshPath();
            if (ownDistanceToNode > maxSearchDistance)
            {
                nodeOutOfReach = true;
                colorLightToSpawn = Color.yellow;
                LogAI($"nodeOutOfReach: maxSearchDistance", 0);
            }
            else if (!agent.CalculatePath(checkingNode.transform.position, pathToNode))
            {
                nodeOutOfReach = true;
                colorLightToSpawn = Color.yellow;
                LogAI($"nodeOutOfReach: CalculatePatch()", 0);
            }
            else if (Vector3.Distance(pathToNode.corners[pathToNode.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(checkingNode.transform.position, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
            {
                nodeOutOfReach = true;
                colorLightToSpawn = Color.yellow;
                LogAI($"nodeOutOfReach: GetNavMeshPosition()", 0);
            }
            LogAI($"ownDistance: {ownDistanceToNode} | outOfReach: {nodeOutOfReach}", 1);

            float closestPlayerDistanceToNode = 999f;
            if (!nodeOutOfReach)
            {
                for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
                {
                    PlayerControllerB playerInCheckA = StartOfRound.Instance.allPlayerScripts[i];
                    if (playerInCheckA == null || !playerInCheckA.isPlayerControlled || !playerInCheckA.isInsideFactory)
                    {
                        continue;
                    }
                    LogAI($"checking player {playerInCheckA}", 0);
                    if (AnyPlayerHasLineOfSight(playerInCheckA, checkingNode.transform.position) != null)
                    {
                        LogAI($"!!!Player can see node at {checkingNode.transform.position}, skipping!!!", 2);
                        nodeOutOfReach = true;
                        continue;
                    }
                    float distanceToNodePrioritized = Vector3.Distance(playerInCheckA.transform.position, checkingNode.transform.position);
                    if (playerInCheckA != fromPlayer)
                    {
                        distanceToNodePrioritized *= StartOfRound.Instance.connectedPlayersAmount + 1;
                    }
                    else if (TargetPlayerCloserAlongPath(pathToNode, fromPlayer))
                    {
                        LogAI($"!!!Node at {checkingNode.transform.position} at end of path too close to targetPlayer, skipping???", 2);
                        nodeOutOfReach = true;
                        continue;
                    }
                    LogAI($"playerDistance = {distanceToNodePrioritized}", 1);
                    if (distanceToNodePrioritized < closestPlayerDistanceToNode)
                    {
                        closestPlayerDistanceToNode = distanceToNodePrioritized;
                    }
                }
            }

            if (!nodeOutOfReach && ownDistanceToNode < closestPlayerDistanceToNode)
            {
                LogAI($"node eligible, adding (distance: {ownDistanceToNode} VS {closestPlayerDistanceToNode})", 2);
                possibleHideNodes.Add(checkingNode);
                colorLightToSpawn = Color.green;
            }
            if (spawnedLight != null)
            {
                spawnedLight.color = colorLightToSpawn;
            }
        }

        LogAI($"finished search [A] of allAINodes with Count {possibleHideNodes.Count}", 3);
        if (possibleHideNodes.Count < minNodesNecessary)
        {
            Log($"counted enemy having access to too little of level (<{minNodesNecessary} = {possibleHideNodes.Count < minNodesNecessary}), going straight into chase", 3);
            GoIntoChase();
            yield break;
        }

        GameObject farthestNode = null;
        GameObject secondNode = null;

        possibleHideNodes = possibleHideNodes.OrderBy((GameObject g) => Vector3.Distance(startPos, g.transform.position)).ToList();
        farthestNode = possibleHideNodes[possibleHideNodes.Count - 1];
        Log($"picked farthest node {farthestNode} at distance {Vector3.Distance(startPos, farthestNode.transform.position)}", 0);

        for (int b = 0; b < possibleHideNodes.Count; b++)
        {
            yield return null;
            LogAI($"B) searching at [{b}]", 1);
            GameObject itemNode = possibleHideNodes[b];
            if (itemNode == farthestNode)
            {
                continue;
            }
            float distanceFromFarthestNode = Vector3.Distance(itemNode.transform.position, farthestNode.transform.position);
            LogAI($"distanceFromFarthestNode = {distanceFromFarthestNode}", 0);
            if (distanceFromFarthestNode < minDistanceBetweenFarawayNodes || distanceFromFarthestNode > maxDistanceBetweenFarawayNodes)
            {
                LogAI("too far");
                continue;
            }
            LogAI($"calculating for secondNode {itemNode} {itemNode.transform.position} || secondNode == null? {secondNode == null}");
            bool skipItemNode = false;
            float vertOffset = Mathf.Abs(farthestNode.transform.position.y - itemNode.transform.position.y) / verticalOffsetDropoffDivider;
            LogAI($"verticalOffset = {vertOffset}", 0);
            if (vertOffset + distanceFromFarthestNode > maxDistanceBetweenFarawayNodes)
            {
                LogAI("VerticalOffsetMode.DecreaseWithDivider", 2);
                skipItemNode = true;
            }

            if (!skipItemNode && !Physics.Linecast(itemNode.transform.position + Vector3.up * 0.5f, farthestNode.transform.position + Vector3.up * 1.0f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                LogAI($"no colliders in Linecast between farthest node and this checked second node, meaning hasLOS, skipping secondNode", 2);
                skipItemNode = true;
            }

            if (!skipItemNode || secondNode == null)
            {
                secondNode = itemNode;
                LogAI($"picked new secondNode {secondNode.transform.position}", 1);
            }
        }

        LogAI($"finished search [B] with secondNode: {secondNode}");
        if (secondNode == null)
        {
            Log($"failed to find targetNode, returning ChooseFarthestNodeFromPosition", 3);
            secondNode = ChooseFarthestNodeFromPosition(startPos, true, 0, false, (int)maxSearchDistance, true).gameObject;
        }
        else
        {
            Log($"picked second node {secondNode} at distance {Vector3.Distance(startPos, secondNode.transform.position)} and distance from farthest node: {Vector3.Distance(farthestNode.transform.position, secondNode.transform.position)}", 0);
        }

        targetNode = secondNode.transform;
        Log($"set targetNode to (second): {targetNode.position}", 3);

        favoriteSpot = farthestNode.transform;
        Log($"favoriteSpot (farthest): {favoriteSpot.position}", 2);


        pickItemPositionCoroutine = null;
    }

    private void StopPickItemPositionCoroutine()
    {
        if (pickItemPositionCoroutine != null)
        {
            StopCoroutine(pickItemPositionCoroutine);
            pickItemPositionCoroutine = null;
        }
    }

    private Transform GetFleeToWhileCalculating(PlayerControllerB fleeFrom = null)
    {
        Log("Getting fleeToWhileCalculating!!!", 3);

        Transform toReturn = transform;

        Transform farthestNode = ChooseFarthestNodeFromPosition(transform.position, false, 0, false, (int)maxSearchDistance, true);
        NavMeshPath tempPath = new NavMeshPath();
        if (fleeFrom == null)
        {
            fleeFrom = GetClosestPlayer();
        }
        if (agent.CalculatePath(farthestNode.position, tempPath))
        {
            if (TargetPlayerCloserAlongPath(tempPath, fleeFrom))
            {
                farthestNode = ChooseFarthestNodeFromPosition(farthestNode.position, false, 0, false, (int)maxSearchDistance * 2, true);
                LogAI($"getting new farthestNode {farthestNode}", 0);
            }
            else
            {
                LogAI($"managed to finish first path to farthestNode {farthestNode}", 1);
            }
            toReturn = farthestNode;
        }
        else
        {
            Log("failed to get toReturn!", 3);
        }

        Log($"sending toReturn {toReturn} fleeToWhileCalculating {toReturn.position}", 1);
        return toReturn;
    }

    private bool TargetPlayerCloserAlongPath(NavMeshPath path, PlayerControllerB fleeFrom = null)
    {
        if (fleeFrom == null)
        {
            if (targetPlayer != null)
            {
                fleeFrom = targetPlayer;
            }
            else
            {
                fleeFrom = GetClosestPlayer();
            }
            if (fleeFrom == null)
            {
                return false;
            }
        }
        for (int i = 0; i < path.corners.Length; i++)
        {
            Vector3 currentCorner = path.corners[i];
            float ownDistance = Vector3.Distance(transform.position, currentCorner);
            float playerDistance = Vector3.Distance(fleeFrom.transform.position, currentCorner);
            if (ownDistance > playerDistance && playerDistance < maxPlayerDistanceToPath)
            {
                LogAI($"player {playerDistance} closer than I {ownDistance} to corner [{i}] {currentCorner}", 2);
                return true;
            }
        }
        return false;
    }

    private PlayerControllerB AnyPlayerHasLineOfSight(PlayerControllerB checkFromPlayer = null, Vector3 pos = default)
    {
        if (pos == default)
        {
            pos = eye.position;
        }
        if (checkFromPlayer == null)
        {
            for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
                if (player != null && player.isPlayerControlled && player.HasLineOfSightToPosition(pos, 50, 40))
                {
                    return player;
                }
            }
        }
        else if (checkFromPlayer.isPlayerControlled && checkFromPlayer.HasLineOfSightToPosition(pos, 50, 40))
        {
            return checkFromPlayer;
        }
        return null;
    }

    private PlayerControllerB GetValidPlayerHoldingItem()
    {
        if (linkedItem == null)
        {
            return null;
        }
        if (linkedItem.playerHeldBy == null)
        {
            return null;
        }
        if (!linkedItem.playerHeldBy.isPlayerControlled)
        {
            return null;
        }
        if (!linkedItem.playerHeldBy.isInsideFactory)
        {
            return null;
        }
        return linkedItem.playerHeldBy;
    }

    private bool PlayersOutside(bool onlyTargetPlayer = false, bool unlimitedAwareness = false, float overrideAwarenessDistance = -1)
    {
        if (overrideAwarenessDistance == -1)
        {
            overrideAwarenessDistance = awarenessDistancePerState[currentBehaviourStateIndex % awarenessDistancePerState.Length];
        }
        if (onlyTargetPlayer)
        {
            if (targetPlayer != null && targetPlayer.isPlayerControlled && targetPlayer.isInsideFactory && (unlimitedAwareness || Vector3.Distance(transform.position, targetPlayer.transform.position) < overrideAwarenessDistance))
            {
                everyoneOutside = false;
                return everyoneOutside;
            }
        }
        else
        {
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled || !player.isInsideFactory)
                {
                    continue;
                }
                if (unlimitedAwareness || Vector3.Distance(transform.position, player.transform.position) < overrideAwarenessDistance)
                {
                    everyoneOutside = false;
                    return everyoneOutside;
                }
            }
        }
        everyoneOutside = true;
        return everyoneOutside;
    }

    private void ResetVariables(bool moveToAmbush = false)
    {
        if (!IsServer)
        {
            Log($"non-server owner trying to reset, relinquishing control back to player #0 in hopes they notice no one is outside during chase, to reset properly", 2);
            ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
            return;
        }
        Log("RESET!!!", 2);
        foreach (BeatAudioItem item in FindObjectsByType<BeatAudioItem>(FindObjectsSortMode.None))
        {
            item.ToggleFakeAudio(false, true);
            item.ToggleAlarm(false, true);
        }
        setItemLocally = false;
        reachedHideSpot = false;
        movingTowardsTargetPlayer = false;
        favoriteSpot = null;
        targetNode = null;
        tempTimer = 0;
        fleeToWhileCalculating = null;
        pickItemPositionCoroutine = null;
        seenDuringThisAmbush = false;
        enraged = false;
        attemptingItemRetrieve = false;
        currentSearch.timesFinishingSearch = 0;
        Log("set everything back", 1);
        TestEnemyScript.DestroyAllNodeLights();
        if (moveToAmbush)
        {
            Log("moving to ambush");
            seenDuringThisAmbush = true;
            MoveToAmbush(targetPlayer);
        }
        else
        {
            targetPlayer = null;
            if (currentBehaviourStateIndex != 0)
            {
                Log("exiting chase");
                SwitchToBehaviourState(0);
            }
        }
        Log("Successfully reached end of ResetOnEveryoneOutside()", 1);
    }

    private void DetectNewSighting(Vector3 lookPos, bool lookImmediately = false)
    {
        timeLastLookingAt = Time.realtimeSinceStartup;
        lookingAt = lookPos;
        if (lookImmediately)
        {
            turnCompass.LookAt(lookPos);
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
        }
    }

    private void MoveToAmbush(PlayerControllerB setTargetPlayer = null)
    {
        if (!IsServer || currentBehaviourStateIndex == 1 || setItemLocally || reachedHideSpot)
        {
            return;
        }
        timesStartedAmbush++;
        fakeAudioThisAmbush = timesStartedAmbush % 2 == 1;
        Log($"timesStartedAmbush: {timesStartedAmbush} || modulo: {timesStartedAmbush % 2} || fakeAudioThisAmbush: {fakeAudioThisAmbush}");
        agent.speed = 0;
        targetNode = null;
        tempTimer = 0;
        if (setTargetPlayer != null)
        {
            targetPlayer = setTargetPlayer;
            Log($"started ambush with targetPlayer {targetPlayer}");
        }
        SetAnimation("WalkUpright", true, "WalkUprightSpeedMultiplier", 5);
        if (currentSearch.inProgress)
        {
            StopSearch(currentSearch);
        }
        attemptingItemRetrieve = GetValidItemRetrieve(true);
        if (!attemptingItemRetrieve)
        {
            Log($"unlinking linkedItem {linkedItem} due to invalid attemptingItemRetrieve {attemptingItemRetrieve}");
            SeverItemLinkServerRpc();
        }
        SwitchToBehaviourState(1);
        ToggleAudioServerRpc(0.0f, hideSpeed);
    }

    public void GoIntoChase(bool enrage = false)
    {
        if (!IsServer || currentBehaviourStateIndex == 2)
        {
            return;
        }
        SyncHPAmbushStartServerRpc(enemyHP);
        if (linkedItem != null && linkedItem.itemAnimator.GetBool("Footsteps"))
        {
            linkedItem.itemAnimator.SetBool("Footsteps", false);
        }
        enraged = enrage;
        StopPickItemPositionCoroutine();
        SwitchToBehaviourState(2);
        SetAnimation("WalkUpright", true, "WalkUprightSpeedMultiplier", 3);
    }

    private void SpawnItemAndHide()
    {
        if (!IsServer)
        {
            return;
        }
        setItemLocally = true;
        if (fakeAudioThisAmbush)
        {
            if (linkedItem == null)
            {
                SpawnItemServerRpc();
            }
            else if (linkedItem.parentObject == holdItemParentObject)
            {
                Log($"owner dropping {linkedItem} #{linkedItem.NetworkObjectId}", 1);
                DropLinkedItem();
            }
        }
        reachedHideSpot = false;
        tempTimer = 0f;
        
        if (favoriteSpot == null)
        {
            Log("ChooseFarthestNodeFromPosition()!!", 3);
            favoriteSpot = ChooseFarthestNodeFromPosition(targetNode.position, true, 0, true, (int)maxDistanceBetweenFarawayNodes, true);
        }
        Log($"spawned item, going back to state 0");
        SwitchToBehaviourState(0);
    }

    private void OnReachHideSpot()
    {
        if (!IsServer)
        {
            return;
        }
        reachedHideSpot = true;
        movingTowardsTargetPlayer = false;
        tempTimer = 0;
        transform.Rotate(new Vector3(0.0f, 180.0f, 0.0f), Space.Self);
        if (fakeAudioThisAmbush && linkedItem != null)
        {
            Log("should call Idle here");
            SetAnimation("Idle");
            linkedItem.ToggleFakeAudio(true, true);
        }
        else
        {
            Log($"fakeAudioThisAmbush: {fakeAudioThisAmbush} | linkedItem: {linkedItem}");
            SetAnimation("Ambush");
            ToggleAudioServerRpc(footstepsVolume);
        }
    }

    private bool GetValidItemRetrieve(bool calculatePath = false)
    {
        if (linkedItem == null)
        {
            LogAI("retrieve False: null");
            return false;
        }
        if (!linkedItem.hasBeenHeld)
        {
            LogAI("retrieve True: !hasBeenHeld");
            return true;
        }
        if (linkedItem.parentObject == holdItemParentObject)
        {
            LogAI("retrieve True: holding");
            return true;
        }
        if (!linkedItem.isInFactory || linkedItem.playerHeldBy != null || linkedItem.deactivated || !linkedItem.grabbable || !linkedItem.grabbableToEnemies)
        {
            LogAI("retrieve False: misc");
            return false;
        }
        if (calculatePath)
        {
            Log("GetValidItemRetrieve(): trying to calculate path");
            if (!agent.CalculatePath(linkedItem.transform.position, path1))
            {
                Log($"CalculatePath()", 2);
                return false;
            }
            else if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(linkedItem.transform.position, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
            {
                Log($"GetNavMeshPosition()", 2);
                return false;
            }
            Log("successfully going for item retrieve");
        }
        LogAI("retrieve True");
        return true;
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
            PassNoiseToOwnerServerRpc(noisePosition, noiseLoudness);
        }
        else if (OnDetectNoiseValid())
        {
            DetectNoiseOwner(noisePosition, noiseLoudness, noiseID);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PassNoiseToOwnerServerRpc(Vector3 noisePosition, float noiseLoudness)
    {
        PassNoiseToOwnerClientRpc(noisePosition, noiseLoudness);
    }

    [ClientRpc]
    private void PassNoiseToOwnerClientRpc(Vector3 noisePosition, float noiseLoudness)
    {
        if (IsOwner)
        {
            DetectNoiseOwner(noisePosition, noiseLoudness);
        }
    }

    private void DetectNoiseOwner(Vector3 noisePosition, float noiseLoudness, int noiseID = 0)
    {
        if (noiseID == 6 || noiseID == 7)
        {
            noiseLoudness = 0.6f;
        }
        float distanceFromNoise = Mathf.Max(1, 80 - Mathf.Pow(Vector3.Distance(transform.position, noisePosition), noiseDistanceDropoffPower));
        float loudnessOfNoise = noiseLoudness * 100;
        float additive = distanceFromNoise + loudnessOfNoise;
        LogAI($"(ID: {noiseID}) // {distanceFromNoise} + {loudnessOfNoise} = {additive}");
        if (additive >= noiseLookThreshold)
        {
            Log("looking at");
            DetectNewSighting(noisePosition);
        }
        if (additive >= noiseThreshold)
        {
            Log($"passed threshold");
            MoveToAmbush();
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
        if (currentBehaviourStateIndex == 0 && setItemLocally && !reachedHideSpot)
        {
            LogAI("DetectNoise(): in 0 and moving to hideSpot");
            return false;
        }
        if (currentBehaviourStateIndex == 1 && (attemptingItemRetrieve || enraged))
        {
            LogAI("DetectNoise(): in 1 and focused on item or player");
            return false;
        }
        if (currentBehaviourStateIndex == 2 && targetPlayer != null)
        {
            LogAI("DetectNoise(): in 2 and chasing player");
            return false;
        }
        if (PlayersOutside())
        {
            LogAI("DetectNoise(): everyone outside awareness radius");
            return false;
        }
        return true;
    }

    public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null)
    {
        base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
        GoIntoChase();
    }

    public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (isEnemyDead)
        {
            return;
        }
        targetPlayer = playerWhoHit;
        GoIntoChase(true);
        PlaySFX(intimidateSFX, true, false, false);
        enemyHP -= force;
        Log($"HP: {enemyHP}");
        if (enemyHP <= 0)
        {
            KillEnemyOnOwnerClient();
        }
    }

    public override void KillEnemy(bool destroy = false)
    {
        base.KillEnemy(destroy);
        StopPickItemPositionCoroutine();
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        if (Time.realtimeSinceStartup - timeLastCollisionLocalPlayer > collisionCooldown)
        {
            PlayerControllerB collidedPlayer = MeetsStandardPlayerCollisionConditions(other, inSpecialAnimation);
            if (collidedPlayer != null && (currentBehaviourStateIndex != 2 || collidedPlayer == targetPlayer))
            {
                base.OnCollideWithPlayer(other);
                timeLastCollisionLocalPlayer = Time.realtimeSinceStartup;
                PerformPlayerCollision(collidedPlayer);
            }
        }
    }

    private void PerformPlayerCollision(PlayerControllerB localPlayer)
    {
        Vector3 forceToHit = Vector3.zero;
        switch (currentBehaviourStateIndex)
        {
            case 0:
                forceToHit = eye.forward * 3 + Vector3.up * 15;
                localPlayer.DamagePlayer(10, true, true, CauseOfDeath.Kicking, 0, false, forceToHit);
                if (!localPlayer.isPlayerDead)
                {
                    localPlayer.externalForceAutoFade += forceToHit;
                }
                if (IsOwner)
                {
                    MoveToAmbush(localPlayer);
                }
                else
                {
                    CollisionLogicServerRpc();
                }
                break;
            case 1:
                forceToHit = eye.forward * 5 + Vector3.up * 25;
                localPlayer.DamagePlayer(20, true, true, CauseOfDeath.Kicking, 0, false, forceToHit);
                if (!localPlayer.isPlayerDead)
                {
                    localPlayer.externalForceAutoFade += forceToHit;
                }
                if (IsOwner)
                {
                    enraged = false;
                }
                else
                {
                    CollisionLogicServerRpc();
                }
                break;
            case 2:
                StartCoroutine(AttackPlayer(localPlayer));
                CollisionLogicServerRpc((int)localPlayer.playerClientId);
                break;
        }
    }

    private IEnumerator AttackPlayer(PlayerControllerB player)
    {
        Log("starting collision coroutine!!", 1);
        agent.speed = 0;
        realAudio.volume = 0.0f;
        PlaySFX(caughtSFX, false, false, false);
        SetAnimation("AttackStart", false);
        inSpecialAnimationWithPlayer = player;
        player.inAnimationWithEnemy = this;
        player.inSpecialInteractAnimation = true;
        player.isCrouching = false;
        player.playerBodyAnimator.SetBool("crouching", false);
        RoundManager.Instance.tempTransform.position = player.transform.position;
        RoundManager.Instance.tempTransform.LookAt(transform.position);
        Quaternion startingPlayerRot = player.transform.rotation;
        Quaternion targetPlayerRot = RoundManager.Instance.tempTransform.rotation;
        for (int i = 0; i < turnPlayerIterations; i++)
        {
            player.transform.rotation = Quaternion.Lerp(startingPlayerRot, targetPlayerRot, (float)i / (float)turnPlayerIterations);
            player.transform.eulerAngles = new Vector3(0f, player.transform.eulerAngles.y, 0f);
            yield return null;
        }
        DetectNewSighting(player.transform.position, true);
        if (linkedItem != null)
        {
            linkedItem.ToggleFakeAudio(false);
            if (linkedItem.playerHeldBy == null)
            {
                linkedItem.ToggleAlarm(false);
            }
        }
        player.inSpecialInteractAnimation = false;
        player.inAnimationWithEnemy = null;
        inSpecialAnimationWithPlayer = null;
        yield return null;
        player.averageVelocity = 0f;
        player.velocityLastFrame = Vector3.zero;
        SetAnimation("Attack", false);
        PlaySFX(reelSFX, false, false, false);
        yield return new WaitForSeconds(0.6f);
        creatureSFX.Stop();
        Vector3 eyePos = player.playerEye.position;
        bool hasKilled = true;
        if (killBox.bounds.Contains(eyePos))
        {
            Log("KillPlayer: playerEye in killBox");
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Mauling, 8);
            PlaySFX(punchHitSFX);
        }
        else if (killBox.bounds.Contains(new Vector3(eyePos.x, killBox.transform.position.y, eyePos.z)) && eyePos.y > killBox.transform.position.y - killBox.bounds.extents.y && eyePos.y < killBox.transform.position.y + 3)
        {
            Log("KillPlayer: playerEye in killBox.x && killBox.z, playerEye > killBox.y - 0.5 && playereye < killBox.y + 3");
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Mauling, 7);
            PlaySFX(punchHitSFX);
        }
        else
        {
            Log("KillPlayer: fail");
            hasKilled = false;
            PlaySFX(punchMissSFX);
        }
        yield return new WaitForSeconds(0.6f);
        if (hasKilled)
        {
            PlaySFX(successCheer, false, true);
        }
        CheckEnragedAfterAttack();
    }

    private IEnumerator AttackPlayerNonLocal(PlayerControllerB player)
    {
        Log("starting collision coroutine NON LOCAL!!!", 2);
        agent.speed = 0;
        realAudio.volume = 0.0f;
        PlaySFX(caughtSFX, false, false, false);
        SetAnimation("AttackStart", false);
        inSpecialAnimationWithPlayer = player;
        player.inAnimationWithEnemy = this;
        player.inSpecialInteractAnimation = true;
        for (int i = 0; i < turnPlayerIterations; i++)
        {
            yield return null;
        }
        DetectNewSighting(player.transform.position, true);
        if (linkedItem != null)
        {
            linkedItem.ToggleFakeAudio(false);
            if (linkedItem.playerHeldBy == null)
            {
                linkedItem.ToggleAlarm(false);
            }
        }
        player.inSpecialInteractAnimation = false;
        player.inAnimationWithEnemy = null;
        inSpecialAnimationWithPlayer = null;
        yield return null;
        player.averageVelocity = 0f;
        player.velocityLastFrame = Vector3.zero;
        SetAnimation("Attack", false);
        PlaySFX(reelSFX, false, false, false);
        yield return new WaitForSeconds(1.2f);
        CheckEnragedAfterAttack();
    }

    private void CheckEnragedAfterAttack()
    {
        enraged = enemyHP < hpAmbushStart || GetValidPlayerHoldingItem() != null;
        Log($"enraged: {enraged}");
        if (enraged)
        {
            return;
        }
        if (IsServer)
        {
            Log($"SERVER READYING UP TO RESET VARIABLES AND MOVE TO AMBUSH!!", 1);
            if (IsOwner)
            {
                ResetVariables(true);
            }
            else
            {
                tempTimer = 99;
            }
        }
        else if (IsOwner)
        {
            Log($"OWNER READYING UP TO GIVE CONTROL BACK TO SERVER!!", 1);
            ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
        }
    }

    public void SetEnemyInSpecialAnimation(bool setInSpecialAnimationTo)
    {
        inSpecialAnimation = setInSpecialAnimationTo;
        LogAI($"inSpecialAnimation = {inSpecialAnimation}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void CollisionLogicServerRpc(int sentBy = -1)
    {
        CollisionLogicClientRpc(sentBy);
    }

    [ClientRpc]
    private void CollisionLogicClientRpc(int sentBy = -1)
    {
        if (sentBy == -1 || sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            Log($"CollisionLogicClientRpc(): currentBehaviourStateIndex {currentBehaviourStateIndex} | sentBy {sentBy}");
            switch (currentBehaviourStateIndex)
            {
                case 0:
                    if (IsOwner)
                    {
                        MoveToAmbush(StartOfRound.Instance.allPlayerScripts[sentBy]);
                    }
                    break;
                case 1:
                    if (IsOwner)
                    {
                        enraged = false;
                    }
                    break;
                case 2:
                    StartCoroutine(AttackPlayerNonLocal(StartOfRound.Instance.allPlayerScripts[sentBy]));
                    break;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SpawnItemServerRpc()
    {
        Instantiate(itemToSpawn.spawnPrefab, transform.position + Vector3.up * 2, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer).GetComponent<NetworkObject>().Spawn();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleAudioServerRpc(float newVolume, float agentSpeed = -1)
    {
        ToggleAudioClientRpc(newVolume, agentSpeed);
    }

    [ClientRpc]
    private void ToggleAudioClientRpc(float newVolume, float agentSpeed = -1)
    {
        if (agentSpeed == -1)
        {
            agentSpeed = agent.speed;
        }
        agent.speed = agentSpeed;
        if (newVolume == -1)
        {
            newVolume = realAudio.volume;
        }
        realAudio.volume = newVolume;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SyncHPAmbushStartServerRpc(int hpAmbushStartValue)
    {
        SyncHPAmbushStartClientRpc(hpAmbushStartValue);
    }

    [ClientRpc]
    private void SyncHPAmbushStartClientRpc(int hpAmbushStartValue)
    {
        hpAmbushStart = hpAmbushStartValue;
        Log($"SyncHPAmbushStartClientRpc({hpAmbushStart})");
}

    public void ChaseNewPlayer(PlayerControllerB player)
    {
        if (player == null)
        {
            return;
        }
        if (IsOwner && (!movingTowardsTargetPlayer || player.OwnerClientId != NetworkObject.OwnerClientId))
        {
            Log($"started new chase on owner, should sync AI-Calculation to chased player {player.playerUsername}", 2);
            ChangeOwnershipOfEnemy(player.actualClientId);
            ChasePlayerServerRpc((int)player.playerClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ChasePlayerServerRpc(int playerID)
    {
        ChasePlayerClientRpc(playerID);
    }

    [ClientRpc]
    private void ChasePlayerClientRpc(int playerID)
    {
        ChaseNewPlayerOnLocalClient(StartOfRound.Instance.allPlayerScripts[playerID]);
    }

    private void ChaseNewPlayerOnLocalClient(PlayerControllerB playerToChase)
    {
        SetMovingTowardsTargetPlayer(playerToChase);
    }

    private void SetAnimation(string animString = null, bool sync = true, string paramString = null, float paramFloat = 1.0f)
    {
        SetAnimationLocal(animString, paramString, paramFloat);
        if (sync)
        {
            SetAnimationServerRpc((int)GameNetworkManager.Instance.localPlayerController.playerClientId, animString, paramString, paramFloat);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetAnimationServerRpc(int sentBy, string animString, string paramString, float paramFloat)
    {
        SetAnimationClientRpc(sentBy, animString, paramString, paramFloat);
    }

    [ClientRpc]
    private void SetAnimationClientRpc(int sentBy, string animString, string paramString, float paramFloat)
    {
        if (sentBy != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            SetAnimationLocal(animString, paramString, paramFloat);
        }
    }

    private void SetAnimationLocal(string animString, string paramString, float paramFloat)
    {
        if (!string.IsNullOrEmpty(animString))
        {
            creatureAnimator.SetTrigger(animString);
        }
        if (!string.IsNullOrEmpty(paramString))
        {
            creatureAnimator.SetFloat(paramString, paramFloat);
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

    private void PlaySFXLocal(AudioClip clipToPlay, bool audibleNoise, bool isAudience)
    {
        AudioSource sourceToPlay = creatureSFX;
        if (isAudience)
        {
            sourceToPlay = beatAudience;
            beatAudience.transform.position = eye.position;
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
                return punchHitSFX;
            case 2:
                return punchMissSFX;
            case 3:
                return reelSFX;
            case 4:
                return caughtSFX;
            case 5:
                return successCheer;
        }
    }

    private int GetIntOfClip(AudioClip ofClip)
    {
        if (ofClip == punchHitSFX) return 1;
        if (ofClip == punchMissSFX) return 2;
        if (ofClip == reelSFX) return 3;
        if (ofClip == caughtSFX) return 4;
        if (ofClip == successCheer) return 5;
        else return 0;
    }

    private void GrabLinkedItem()
    {
        if (IsOwner)
        {
            GrabLinkedItemLocal(linkedItem);
            GrabLinkedItemServerRpc(linkedItem.NetworkObject);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void GrabLinkedItemServerRpc(NetworkObjectReference itemNOR)
    {
        GrabLinkedItemClientRpc(itemNOR);
    }

    [ClientRpc]
    private void GrabLinkedItemClientRpc(NetworkObjectReference itemNOR)
    {
        if (!IsOwner)
        {
            if (itemNOR.TryGet(out var netObj))
            {
                BeatAudioItem itemScript = netObj.GetComponent<BeatAudioItem>();
                if (itemScript == null)
                {
                    Log($"failed to get itemScript for GrabLinkedItemClientRpc on netObj {netObj.name} #{NetworkObjectId}");
                    return;
                }
                GrabLinkedItemLocal(itemScript);
            }
            else
            {
                Log($"failed to get netObj of linkedItem!", 3);
                return;
            }
        }
    }

    private void GrabLinkedItemLocal(BeatAudioItem itemToGrab)
    {
        Log($"!!!locally grabbing {itemToGrab.name} #{itemToGrab.NetworkObjectId}!!!", 1);
        itemToGrab.ToggleFakeAudio(false);
        itemToGrab.ToggleAlarm(false);
        itemToGrab.grabbableToEnemies = false;
        itemToGrab.isHeldByEnemy = true;
        itemToGrab.fallTime = 1;
        itemToGrab.hasHitGround = true;
        itemToGrab.reachedFloorTarget = true;
        itemToGrab.parentObject = holdItemParentObject;
    }

    private void DropLinkedItem()
    {
        if (IsOwner)
        {
            DropLinkedItemLocal(linkedItem);
            DropLinkedItemServerRpc(linkedItem.NetworkObject);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropLinkedItemServerRpc(NetworkObjectReference itemNOR)
    {
        DropLinkedItemClientRpc(itemNOR);
    }

    [ClientRpc]
    private void DropLinkedItemClientRpc(NetworkObjectReference itemNOR)
    {
        if (!IsOwner)
        {
            if (itemNOR.TryGet(out var netObj))
            {
                BeatAudioItem itemScript = netObj.GetComponent<BeatAudioItem>();
                if (itemScript == null)
                {
                    Log($"failed to get itemScript for DropLinkedItemClientRpc on netObj {netObj.name} #{NetworkObjectId}");
                    return;
                }
                DropLinkedItemLocal(itemScript);
            }
            else
            {
                Log($"failed to get netObj of linkedItem!", 3);
                return;
            }
        }
    }

    private void DropLinkedItemLocal(BeatAudioItem itemToDrop)
    {
        Log($"!!!locally dropping {itemToDrop.name} #{itemToDrop.NetworkObjectId}!!!", 1);
        itemToDrop.grabbable = true;
        itemToDrop.grabbableToEnemies = true;
        itemToDrop.isHeld = false;
        itemToDrop.isHeldByEnemy = false;
        itemToDrop.fallTime = 0;
        itemToDrop.hasHitGround = false;
        itemToDrop.reachedFloorTarget = false;
        itemToDrop.parentObject = null;
        itemToDrop.startFallingPosition = itemToDrop.transform.parent == null ? holdItemParentObject.position + Vector3.up * 0.5f : itemToDrop.transform.parent.InverseTransformPoint(holdItemParentObject.position + Vector3.up * 0.5f);
        itemToDrop.FallToGround();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SeverItemLinkServerRpc()
    {
        SeverItemLinkClientRpc();
    }

    [ClientRpc]
    private void SeverItemLinkClientRpc()
    {
        if (linkedItem != null)
        {
            linkedItem.UnlinkItemFromEnemy(this);
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
