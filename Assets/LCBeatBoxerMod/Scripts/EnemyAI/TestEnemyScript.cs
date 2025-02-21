using BepInEx.Logging;
using GameNetcodeStuff;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class TestEnemyScript : EnemyAI
{
    //All virtual EnemyAI methods:
    /// <summary>
    /// Start
    /// UseNestSpawnObject
    /// 
    /// Update
    /// DoAIInterval
    /// 
    /// HitEnemy
    /// HitFromExplosion
    /// SetEnemyStunned
    /// KillEnemy
    /// 
    /// OnCollideWithPlayer
    /// OnCollideWithEnemy
    /// 
    /// ReachedNodeInSearch
    /// FinishedCurrentSearchRoutine
    /// OnSyncPositionFromServer
    /// SetEnemyOutside
    /// ShipTeleportEnemy
    /// DaytimeEnemyLeave
    /// 
    /// DetectNoise
    /// ReceiveLoudNoiseBlast
    /// 
    /// AnimationEventA
    /// AnimationEventB
    /// 
    /// OnDrawGizmos
    /// EnableEnemyMesh
    /// CancelSpecialAnimationWithPlayer
    /// </summary>

    private static ManualLogSource Logger = Plugin.Logger;

    public enum HideMode
    {
        InvalidNodesContains,
        PathIsIntersectedByLineOfSight,
        LessDistanceThanTargetPlayer
    }

    public enum NodeSelectionMode
    {
        DistanceAllPlayers,
        OnlyTargetPlayer,
        PrioritizeTargetPlayer
    }

    public enum VerticalOffsetMode
    {
        DontCalculate,
        RandomChance,
        IncreaseWithPower,
        DecreaseWithDivider
    }

    public enum AttackAnim
    {
        Headbutt,
        Punch,
        LowSweep
    }

    private float tempTimer;
    private bool setLureLocally;
    private bool waitingToSpawnLure;
    private Transform spawnLureAt;
    private Transform hideSpot;
    private bool reachedHideSpot;
    private Transform lastSeenAt;
    private Transform fleeToWhileCalculating;
    private Coroutine pickLurePositionCoroutine;
    private float lookingTime;
    private Vector3 lookingAt;
    private static List<Vector3> lastInvalidCorners = new List<Vector3>();
    private bool everyoneOutside;
    private float timeLastCollisionLocalPlayer;
    private bool seenDuringThisAmbush;
    private bool performedAmushAttack;

    [Space(3f)]
    [Header("CUSTOM")]
    public Item itemToSpawn;
    [HideInInspector]
    public EnemyLureTestItem linkedItem;
    public TestCombatHitbox hitboxScript;
    public Transform turnCompass;
    public Transform shovelParent;
    public MeshRenderer changeMatOf;
    public Material changeMatTo;

    [Space(3f)]
    [Header("COMBAT")]
    public bool inAttackAnimation;
    public bool vulnerable;
    public int currentAttackIndex;
    public string currentAttackTrigger;
    public string currentAttackState;
    public AttackAnim[] attackSequence;
    public TestAttackAnimSequence currentAttackSequence;
    public TestAttackAnimSequence[] testAttackAnimSequences;
    public static Item shovelItem;
    public GameObject spawnedShovel;
    public float attackCooldown;
    private float timeLastAttackAnimChange;

    [Space(3f)]
    [Header("SFX")]
    public AudioSource realAudio;
    public AudioClip footstepSFX;
    public AudioClip runSFX;
    public AudioClip blockSFX;
    public AudioClip intimidateSFX;
    public AudioClip reelSFX;
    public AudioClip punchSFX;
    public AudioClip bellSFX;

    [Space(3f)]
    [Header("SPEEDS")]
    public float roamSpeed;
    public float hideSpeed;
    public float chaseSpeed;

    [Space(3f)]
    [Header("SENSES")]
    public int seeDistance;
    public float seeWidth;
    public float awarenessDistance;
    public float noiseThreshold;
    [Range(0f, 1f)]
    public float noiseLookThreshold;
    public float noiseDistanceDropoffPower;

    [Space(3f)]
    [Header("PARAMETERS")]
    public bool randomizeNodeCheck;
    public float minDistanceBetweenFarawayNodes;
    public float maxDistanceBetweenFarawayNodes;
    public float maxPathBetweenFarawayNodes;
    public VerticalOffsetMode offsetMode;
    public AnimationCurve chanceToSkipOnVerticalOffset;
    public float verticalOffsetDropOffPower;
    public float verticalOffsetDropOffDivider;
    public float maxSearchDistance;
    public float maxLureSpawnTime;
    public float maxHideSpotTime;
    [Range(0, 100)]
    public int chanceToPlayFakeAudio;
    public HideMode hideMode;
    public float maxPlayerDistanceToHidePath;
    public NodeSelectionMode selectionMode;
    public int minNodesAbsolute;
    [Range(0f, 1f)]
    public float minNodesPercentage;
    public int[] statesDetectingNoise;
    public float attackDistanceThreshold;
    public float collisionCooldown;

    [Space(3f)]
    [Header("DEBUG")]
    public bool debugPrintBehaviourStateIndex;
    public bool debugSpawnLights;
    public GameObject debugNodeLightPrefab;
    public float debugIterationDelay;
    public bool debugCheckSeenPosToHide;
    public int debugMaxSearches;
    public bool debugInstantKillOnChase;
    public int debugTurnPlayerIterations;



    //CUSTOM CODE
    public override void Start()
    {
        enemyType.isOutsideEnemy = transform.position.y > -50f;
        Logger.LogError($"isOutside: {isOutside} | isOutsideEnemy: {enemyType.isOutsideEnemy}");
        base.Start();
        Logger.LogError($"isOutside: {isOutside} | isOutsideEnemy: {enemyType.isOutsideEnemy}");
        //LinkEnemyToLure();
        lastInvalidCorners.Clear();
        if (IsServer)
        {
            SpawnShovelAndSync();
        }
    }

    public override void Update()
    {
        base.Update();
        if (!inAttackAnimation && Time.realtimeSinceStartup - lookingTime < 1f)
        {
            turnCompass.LookAt(lookingAt);
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
        }
        if (currentBehaviourStateIndex == 1 && StartOfRound.Instance.localPlayerController.HasLineOfSightToPosition(transform.position))
        {
            StartOfRound.Instance.localPlayerController.IncreaseFearLevelOverTime(0.3f);
        }
        if (!IsOwner)
        {
            return;
        }
        //Owner-only functionality
        if (waitingToSpawnLure || (setLureLocally && (!reachedHideSpot || everyoneOutside)) || (currentBehaviourStateIndex == 2 && targetPlayer == null))
        {
            tempTimer += Time.deltaTime;
        }
    }



    //Calculations
    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (StartOfRound.Instance.allPlayersDead)
        {
            return;
        }
        //if (IsServer && currentBehaviourStateIndex != 2 && !IsOwner)
        //{
        //    Logger.LogDebug($"Server non-owner during roam or hide");
        //    ChangeOwnershipOfEnemy(StartOfRound.Instance.localPlayerController.actualClientId);
        //}
        if (debugPrintBehaviourStateIndex)
        {
            Logger.LogInfo($"currentBehaviourStateIndex: [{currentBehaviourStateIndex}]");
        }
        switch (currentBehaviourStateIndex)
        {
            case 0:
                useSecondaryAudiosOnAnimatedObjects = false;
                if (setLureLocally)
                {
                    //Make sure he does not go into roaming
                    if (currentSearch.inProgress)
                    {
                        Logger.LogDebug("STOP search (0)");
                        StopSearch(currentSearch);
                    }
                    //Stuff that happens to reach and after reaching the hide spot
                    if (!reachedHideSpot)
                    {
                        if (hideSpot != null)
                        {
                            Logger.LogDebug("going to HIDE");
                            SetDestinationToPosition(hideSpot.position);
                            agent.speed = hideSpeed;
                            if (debugIterationDelay <= 0 && tempTimer > maxHideSpotTime)
                            {
                                Logger.LogError("TIMER: Unable to reach hideSpot, pausing");
                                OnReachHideSpot();
                                tempTimer = 0;
                            }
                            else if (Vector3.Distance(transform.position, hideSpot.position) < 1)
                            {
                                Logger.LogDebug($"reached hide spot, starting sound");
                                OnReachHideSpot();
                            }
                        }
                    }
                    else
                    {
                        PlayerControllerB playerSeeing = AnyPlayerHasLineOfSight();
                        if (playerSeeing != null)
                        {
                            Logger.LogDebug($"playerSeeing: {playerSeeing} | reachedHideSpot: {reachedHideSpot} | targetPlayer & moving: {targetPlayer} | {movingTowardsTargetPlayer}");
                            targetPlayer = playerSeeing;
                            SwitchToBehaviourState(2);
                            break;
                        }
                        PlayerControllerB closestPlayer = GetClosestPlayer(true);
                        if (closestPlayer != null && Vector3.Distance(transform.position, closestPlayer.transform.position) < 5)
                        {
                            Logger.LogDebug($"distance between Enemy and ClosestPlayer below 5, moving to chase");
                            targetPlayer = closestPlayer;
                            SwitchToBehaviourState(2);
                            break;
                        }
                    }
                    //NOTE (2025-01-12):
                    //Currently = Go to the player who might steal the lure
                    //Future = Trigger the chase from the item's EquipItem()
                    if (linkedItem != null && linkedItem.playerHeldBy != null)
                    {
                        targetPlayer = linkedItem.playerHeldBy;
                        SwitchToBehaviourState(2);
                    }
                    /*else if (linkedItem.lureOrRepel)
                    {
                        Logger.LogDebug("going to CLOSEST");
                        agent.speed = hideSpeed;
                        SetDestinationToPosition(ChooseClosestNodeToPosition(linkedItem.transform.position, true).position);
                    }*/

                    //So this only happens if it does not have a linkedItem or it is not held, they are not going to the player, have not reached the hidespot and that is not null
                    //So this doesnt happen if it has a linkeditem, the linkeditem is held, they are moving to a player, have not reached the hide spot, or the hide spot is null
                    //This SHOULD happen.... when??? Commenting this out since his going back to roaming is handled by everyoneOutside

                    //else if (!currentSearch.inProgress)
                    //{
                    //    if (linkedItem != null)
                    //    {
                    //        linkedItem.ToggleAudioFromEnemy(false);
                    //    }
                    //    Logger.LogDebug("START search");
                    //    StartSearch(transform.position);
                    //    agent.speed = hideSpeed;
                    //    reachedHideSpot = false;
                    //    ToggleAudioServerRpc(true, hideSpeed);
                    //}

                    //If in the waiting-stage, delay going back to roaming for 10 seconds after everyone has left
                    if (EveryoneOutside(false))
                    {
                        if (tempTimer > 10f)
                        {
                            ResetOnEveryoneOutside();
                        }
                    }
                    else
                    {
                        tempTimer = 0f;
                    }
                }
                else if (!currentSearch.inProgress)
                {
                    if (currentSearch.timesFinishingSearch > debugMaxSearches)
                    {
                        if (realAudio.isPlaying)
                        {
                            Logger.LogDebug($"out of searches, stopping");
                            ToggleAudioServerRpc(false);
                        }
                    }
                    else
                    {
                        Logger.LogDebug("START search (0)");
                        StartSearch(transform.position);
                        agent.speed = roamSpeed;
                        ToggleAudioServerRpc(true, true, roamSpeed);
                    }
                }
                //When in uninterrupted default roaming, go into placing a lure and the ambush when spotting a player
                else
                {
                    if (GetAllPlayersInLineOfSight(seeWidth, seeDistance) != null)
                    {
                        Logger.LogDebug("enemy spotted player, moving to state 1");
                        MoveToAmbush();
                        if (!debugCheckSeenPosToHide)
                        {
                            agent.SetDestination(transform.position);
                        }
                    }
                }
                break;
            case 1:
                useSecondaryAudiosOnAnimatedObjects = false;
                if (currentSearch.inProgress)
                {
                    StopSearch(currentSearch);
                }
                if (waitingToSpawnLure)
                {
                    PlayerControllerB playerSeeingInHide = AnyPlayerHasLineOfSight();
                    if (playerSeeingInHide != null)
                    {
                        targetPlayer = playerSeeingInHide;
                        lastSeenAt = transform;
                        DetectNewSighting(targetPlayer.transform.position);
                        //NOTE (2025-01-09):
                        //Don't do this on AiInterval
                        //fleeToWhileCalculating = GetFleeToWhileCalculating();
                        Logger.LogInfo($"enemy currently seen, set lastSeenPosition to {lastSeenAt.position}");
                        if (!seenDuringThisAmbush)
                        {
                            seenDuringThisAmbush = true;
                            PlayAnimSFX(2);
                        }
                    }
                    if (spawnLureAt == null)
                    {
                        if (pickLurePositionCoroutine == null)
                        {
                            pickLurePositionCoroutine = StartCoroutine(PickLurePosition());
                        }
                        if (debugCheckSeenPosToHide)
                        {
                            agent.speed = fleeToWhileCalculating == null ? 0 : hideSpeed;
                            if (fleeToWhileCalculating == null)
                            {
                                //NOTE (2025-01-09):
                                //Make him walk away from players, or accept that he will walk towards them for now
                                fleeToWhileCalculating = GetFleeToWhileCalculating(playerSeeingInHide);
                            }
                            else
                            {
                                Logger.LogDebug($"trying to hide out of sight");
                                //NOTE (2025-01-08):
                                //He keeps running towards the player
                                SetDestinationToPosition(fleeToWhileCalculating.position);
                            }
                        }
                    }
                    else
                    {
                        SetDestinationToPosition(spawnLureAt.position, true);
                        if (Vector3.Distance(transform.position, spawnLureAt.position) < 1)
                        {
                            Logger.LogDebug($"spawn by: DISTANCE");
                            SpawnLureAndHide();
                        }
                        else if (debugIterationDelay <= 0 && tempTimer > maxLureSpawnTime)
                        {
                            Logger.LogError($"spawn by: TIMER");
                            SpawnLureAndHide();
                        }
                    }
                }
                
                break;
            case 2:
                useSecondaryAudiosOnAnimatedObjects = true;
                if (currentSearch.inProgress)
                {
                    Logger.LogDebug($"STOP search (2)");
                    StopSearch(currentSearch);
                }
                bool unlimitedAwareness = GetValidPlayerHoldingLure();
                if (EveryoneOutside(!unlimitedAwareness, maxDistanceBetweenFarawayNodes + 1) && tempTimer > 5)
                {
                    ResetOnEveryoneOutside();
                    break;
                }
                if (!inAttackAnimation)
                {
                    if (targetPlayer != null && spawnedShovel != null && spawnedShovel.transform.parent != shovelParent && Vector3.Distance(transform.position, targetPlayer.transform.position) < attackDistanceThreshold)
                    {
                        if (Time.realtimeSinceStartup - timeLastAttackAnimChange > attackCooldown)
                        {
                            PerformNextAttack();
                        }
                        break;
                    }
                    agent.speed = chaseSpeed;
                    if (targetPlayer == null)
                    {
                        if (GetValidPlayerHoldingLure())
                        {
                            targetPlayer = linkedItem.playerHeldBy;
                        }
                        else
                        {
                            targetPlayer = GetClosestPlayer();
                        }
                    }
                    else if (isOutside == targetPlayer.isInsideFactory || !targetPlayer.isPlayerControlled)
                    {
                        tempTimer = 0;
                        targetPlayer = null;
                        movingTowardsTargetPlayer = false;
                    }
                    Logger.LogDebug($"going to PLAYER ({targetPlayer})");
                    ChaseNewPlayer(targetPlayer);
                    if (!realAudio.isPlaying || realAudio.clip != runSFX)
                    {
                        ToggleAudioServerRpc(true, false, chaseSpeed);
                    }
                }
                //SOLVED (2025-01-09) - NOTE (2025-01-08):
                //ow my ears
                if (targetPlayer == StartOfRound.Instance.localPlayerController && Vector3.Distance(transform.position, targetPlayer.transform.position) < realAudio.maxDistance)
                {
                    targetPlayer.JumpToFearLevel(0.8f);
                }
                break;
        }
    }

    private IEnumerator PickLurePosition()
    {
        //spawnLurePosition = allAINodes[Random.Range(0, allAINodes.Length)].transform.position;

        Logger.LogError("STARTING COROUTINE PickLurePosition()!!!");
        Logger.LogInfo($"allAINodes.Length = {allAINodes.Length}");
        Vector3 startPos = transform.position;
        if (debugSpawnLights)
        {
            Instantiate(debugNodeLightPrefab, startPos, Quaternion.identity);
        }

        List<GameObject> nodesAwayFromPlayer = new List<GameObject>();
        lastInvalidCorners.Clear();

        PlayerControllerB fromPlayer = targetPlayer != null ? targetPlayer : GetClosestPlayer();

        for (int i = 0; i < allAINodes.Length; i++)
        {
            GameObject checkingNode = allAINodes[i];
            Light spawnedLight = Instantiate(debugNodeLightPrefab, checkingNode.transform.position, Quaternion.identity).GetComponent<Light>();
            if (!debugSpawnLights)
            {
                spawnedLight.enabled = false;
            }
            Logger.LogDebug($"checking node {checkingNode} {checkingNode.transform.position}");

            bool nodeOutOfReach = false;
            float ownDistanceToNode = Vector3.Distance(startPos, checkingNode.transform.position);
            if (ownDistanceToNode > maxSearchDistance)
            {
                nodeOutOfReach = true;
                Logger.LogDebug($"nodeOutOfReach: maxSearchDistance");
            }
            else if (!agent.CalculatePath(checkingNode.transform.position, path1))
            {
                nodeOutOfReach = true;
                Logger.LogDebug($"nodeOutOfReach: CalculatePatch()");
            }
            else if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(checkingNode.transform.position, RoundManager.Instance.navHit, 2.7f)) > 1.5f)
            {
                nodeOutOfReach = true;
                Logger.LogDebug($"nodeOutOfReach: GetNavMeshPosition()");
            }
            Logger.LogInfo($"ownDistance: {ownDistanceToNode} | outOfReach: {nodeOutOfReach}");

            float closestPlayerDistanceToNode = 999f;
            if (!nodeOutOfReach)
            {
                switch (selectionMode)
                {
                    case NodeSelectionMode.DistanceAllPlayers:
                        for (int j = 0; j < StartOfRound.Instance.allPlayerScripts.Length; j++)
                        {
                            PlayerControllerB checkingPlayer = StartOfRound.Instance.allPlayerScripts[j];
                            if (checkingPlayer == null || !checkingPlayer.isPlayerControlled || isOutside == checkingPlayer.isInsideFactory)
                            {
                                continue;
                            }
                            Logger.LogDebug($"checking player {checkingPlayer}");
                            float thisPlayerDistanceToNode = Vector3.Distance(checkingPlayer.transform.position, checkingNode.transform.position);
                            Logger.LogInfo($"playerDistance = {thisPlayerDistanceToNode}");
                            if (thisPlayerDistanceToNode < closestPlayerDistanceToNode)
                            {
                                closestPlayerDistanceToNode = thisPlayerDistanceToNode;
                            }
                        }
                        break;
                    case NodeSelectionMode.OnlyTargetPlayer:
                        Logger.LogDebug($"checking player {fromPlayer}");
                        float fromPlayerDistanceToNode = Vector3.Distance(fromPlayer.transform.position, checkingNode.transform.position);
                        Logger.LogInfo($"playerDistance = {fromPlayerDistanceToNode}");
                        if (fromPlayerDistanceToNode < closestPlayerDistanceToNode)
                        {
                            closestPlayerDistanceToNode = fromPlayerDistanceToNode;
                        }
                        break;
                    case NodeSelectionMode.PrioritizeTargetPlayer:
                        for (int n = 0; n < StartOfRound.Instance.allPlayerScripts.Length; n++)
                        {
                            PlayerControllerB playerPrioritized = StartOfRound.Instance.allPlayerScripts[n];
                            if (playerPrioritized == null || !playerPrioritized.isPlayerControlled || isOutside == playerPrioritized.isInsideFactory)
                            {
                                continue;
                            }
                            Logger.LogDebug($"checking player {playerPrioritized}");
                            float distanceToNodePrioritized = Vector3.Distance(playerPrioritized.transform.position, checkingNode.transform.position);
                            //NOTE (2025-01-11):
                            //Dividing the player's distance from the node means it is smaller, and smaller distance means player is closer
                            //The intention is to have players that aren't the prioritized player CHECK for a smaller distance around them
                            //Old solution this note is based on commented out
                            if (playerPrioritized != fromPlayer)
                            {
                                //distanceToNodePrioritized /= StartOfRound.Instance.connectedPlayersAmount + 2;
                                distanceToNodePrioritized *= StartOfRound.Instance.connectedPlayersAmount + 1;
                            }
                            Logger.LogInfo($"playerDistance = {distanceToNodePrioritized}");
                            if (distanceToNodePrioritized < closestPlayerDistanceToNode)
                            {
                                closestPlayerDistanceToNode = distanceToNodePrioritized;
                            }
                        }
                        break;
                }
                
            }

            if (debugIterationDelay > 0)
            {
                yield return new WaitForSeconds(debugIterationDelay / 2f);
            }

            Color colorLightToSpawn;
            if (ownDistanceToNode < closestPlayerDistanceToNode && !nodeOutOfReach)
            {
                Logger.LogWarning($"node eligible, adding (distance: {ownDistanceToNode} VS {closestPlayerDistanceToNode})");
                nodesAwayFromPlayer.Add(checkingNode);
                colorLightToSpawn = Color.green;
            }
            else if (nodeOutOfReach)
            {
                //lastInvalidCorners.Add(checkingNode.transform.position);
                colorLightToSpawn = Color.yellow;
            }
            else
            {
                lastInvalidCorners.Add(checkingNode.transform.position);
                colorLightToSpawn = Color.red;
            }
            spawnedLight.color = colorLightToSpawn;

            if (debugIterationDelay > 0)
            {
                yield return new WaitForSeconds(debugIterationDelay / 2f);
            }
            yield return null;
        }

        Logger.LogError($"finished search of allAINodes with Count {nodesAwayFromPlayer.Count} (invalid Count: {lastInvalidCorners.Count})");

        if (nodesAwayFromPlayer.Count < minNodesAbsolute || nodesAwayFromPlayer.Count < (float)(allAINodes.Length * minNodesPercentage))
        {
            Logger.LogDebug($"counted enemy having access to too little of level (<{minNodesAbsolute} = {nodesAwayFromPlayer.Count < 7} || {minNodesPercentage * 100}% = {(float)(allAINodes.Length * minNodesPercentage)}), going straight into chase");
            SwitchToBehaviourState(2);
            yield break;
        }

        GameObject farthestNode = null;
        GameObject secondNode = null;
        float distanceToFarthestNode = 0;

        Logger.LogInfo($"sort node check? {!randomizeNodeCheck}");
        if (!randomizeNodeCheck)
        {
            nodesAwayFromPlayer = nodesAwayFromPlayer.OrderBy((GameObject g) => Vector3.Distance(startPos, g.transform.position)).ToList();
        }

        //CHANGED (2025-01-09) - NOTE (2025-01-08):
        //Only get node if it is not intersected by other nodes to not check
        for (int k = 0; k < nodesAwayFromPlayer.Count; k++)
        {
            Logger.LogInfo($"starting iteration [{k}]");
            GameObject farawayNode = nodesAwayFromPlayer[k];
            bool skipNode = false;
            if (AnyPlayerHasLineOfSight(farawayNode.transform.position) != null)
            {
                Logger.LogWarning($"!!!Player can see node at {farawayNode.transform.position}, skipping!!!");
                skipNode = true;
            }
            if (!skipNode)
            {
                NavMeshPath tempPath = new NavMeshPath();
                agent.CalculatePath(farawayNode.transform.position, tempPath);
                switch (hideMode)
                {
                    case HideMode.InvalidNodesContains:
                        for (int l = 0; l < tempPath.corners.Length; l++)
                        {
                            Vector3 currentCorner = tempPath.corners[l];
                            //Logger.LogDebug($"PATH CALCULATION: checking corner {currentCorner}");
                            if (lastInvalidCorners.Contains(currentCorner))
                            {
                                Logger.LogWarning($"!!!Node at {farawayNode.transform.position} goes across invalid node at {currentCorner}, skipping???");
                                skipNode = true;
                                break;
                            }
                        }
                        break;
                    case HideMode.PathIsIntersectedByLineOfSight:
                        if (PathIsIntersectedByLineOfSight(farawayNode.transform.position, false, true, true))
                        {
                            Logger.LogWarning($"!!!Node at {farawayNode.transform.position} intersected by line of sight, skipping???");
                            skipNode = true;
                        }
                        break;
                    case HideMode.LessDistanceThanTargetPlayer:
                        if (TargetPlayerCloserAlongPath(tempPath))
                        {
                            Logger.LogWarning($"!!!Node at {farawayNode.transform.position} at end of path too close to targetPlayer, skipping???");
                            skipNode = true;
                        }
                        break;
                }
            }
            if (skipNode)
            {
                continue;
            }
            Logger.LogDebug($"still calculating... checking farawayNode {farawayNode} {farawayNode.transform.position}");

            float distanceToThisNode = Vector3.Distance(startPos, farawayNode.transform.position);
            Logger.LogDebug($"distance: {distanceToThisNode}");
            float distanceFromPreviousFarthestNode = 0f;
            
            //SOLVED (2025-01-09) - NOTE (2025-01-08):
            //Will not calculate second farthest node because check only occurs after finding new farthest node
            if (distanceToThisNode > distanceToFarthestNode)
            {
                if (farthestNode != null)
                {
                    distanceFromPreviousFarthestNode = Vector3.Distance(farawayNode.transform.position, farthestNode.transform.position);
                    Logger.LogDebug($"distanceFromPreviousFarthestNode = {distanceFromPreviousFarthestNode}");
                    if (secondNode == null || distanceFromPreviousFarthestNode >= minDistanceBetweenFarawayNodes && distanceFromPreviousFarthestNode <= maxDistanceBetweenFarawayNodes)
                    {
                        Logger.LogInfo($"calculating for second node (current farthestNode) {farthestNode} {farthestNode.transform.position} || secondNode == null? {secondNode == null}");
                        float vertOffset = 0f;
                        bool skipSecondNode = false;
                        switch (offsetMode)
                        {
                            case VerticalOffsetMode.DontCalculate:
                                Logger.LogDebug("VerticalOffsetMode.DontCalculate");
                                break;
                            case VerticalOffsetMode.RandomChance:
                                if (Random.Range(1, 101) < chanceToSkipOnVerticalOffset.Evaluate(vertOffset))
                                {
                                    Logger.LogWarning("VerticalOffsetMode.RandomChance");
                                    skipSecondNode = true;
                                }
                                break;
                            case VerticalOffsetMode.IncreaseWithPower:
                                vertOffset = Mathf.Pow(Mathf.Abs(farthestNode.transform.position.y - farawayNode.transform.position.y), verticalOffsetDropOffPower);
                                if (vertOffset + distanceFromPreviousFarthestNode > maxDistanceBetweenFarawayNodes)
                                {
                                    Logger.LogWarning("VerticalOffsetMode.IncreaseWithPower"); 
                                    skipSecondNode = true;
                                }
                                break;
                            case VerticalOffsetMode.DecreaseWithDivider:
                                vertOffset = Mathf.Abs(farthestNode.transform.position.y - farawayNode.transform.position.y) / verticalOffsetDropOffDivider;
                                if (vertOffset + distanceFromPreviousFarthestNode > maxDistanceBetweenFarawayNodes)
                                {
                                    Logger.LogWarning("VerticalOffsetMode.DecreaseWithDivider"); 
                                    skipSecondNode = true;
                                }
                                break;

                        }
                        
                        Logger.LogDebug($"verticalOffset = {vertOffset}");

                        if (!skipSecondNode && !Physics.Linecast(farawayNode.transform.position + Vector3.up * 1.0f, farthestNode.transform.position + Vector3.up * 1.0f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            Logger.LogWarning($"no colliders in Linecast between farthest node and this checked second node, meaning hasLOS, skipping secondNode");
                            skipSecondNode = true;
                        }

                        //
                        if (!skipSecondNode || secondNode == null)
                        {
                            secondNode = farthestNode;
                            Logger.LogDebug($"picked new secondNode {secondNode.transform.position}");
                        }

                        //NOTE (2025-01-10):
                        //CalculatePath only happens from CURRENT position of agent, not from given node we wish to move to
                        //Above solution is done assuming HideMode.LessDistanceThanTargetPlayer
                        //if (GetPathDistance(farawayNode.transform.position, farthestNode.transform.position) && pathDistance < maxPathBetweenFarawayNodes)
                        //{
                        //    Logger.LogWarning($"node in range, pathDistance: {pathDistance}");
                        //    secondNode = farthestNode;
                        //}
                        //else
                        //{
                        //    Logger.LogError($"node too far, unreachable? {pathDistance == 0} | pathDistance: {pathDistance}");
                        //}
                    }
                }

                distanceToFarthestNode = distanceToThisNode;
                farthestNode = farawayNode;
                Logger.LogDebug($"picked new farthest node {farthestNode.transform.position}");


            }
            if (debugIterationDelay > 0)
            {
                yield return new WaitForSeconds(debugIterationDelay);
            }
            yield return null;
        }

        if (secondNode == null)
        {
            Logger.LogError($"failed to find spawnLurePosition, returning farthestPosition with maxSearchDistance as maxAsync");
            secondNode = ChooseFarthestNodeFromPosition(startPos, true, 0, false, (int)maxSearchDistance, true).gameObject;
        }

        spawnLureAt = secondNode.transform;
        Logger.LogError($"set spawnLurePosition to (second): {spawnLureAt.position}");

        //SOLVED (2025-01-09) - NOTE (2025-01-08):
        //Make sure this works
        hideSpot = farthestNode.transform;
        Logger.LogWarning($"hideSpot (farthest): {hideSpot.position}");


        pickLurePositionCoroutine = null;
    }

    private void StopPickLurePositionCoroutine()
    {
        if (pickLurePositionCoroutine != null)
        {
            StopCoroutine(pickLurePositionCoroutine);
            pickLurePositionCoroutine = null;
        }
    }

    private Transform GetFleeToWhileCalculating(PlayerControllerB fleeFrom = null)
    {
        Logger.LogError("Getting fleeToWhileCalculating!!!");

        Transform toReturn = transform;

        ///SOLUTION 1: Get the farthest node from the list of within-reach eligible nodes 
        ///NOTE: Currently not good because lastInvalidCorners is being filled in while/before this happens

        //List<Vector3> tempVecArray = new List<Vector3>();
        //for (int i = 0; i < allAINodes.Length; i++)
        //{
        //    Vector3 thisVec = allAINodes[i].transform.position;
        //    if (!lastInvalidCorners.Contains(thisVec))
        //    {
        //        tempVecArray.Add(thisVec);
        //    }
        //}
        //tempVecArray = tempVecArray.OrderByDescending((Vector3 v) => Vector3.Distance(transform.position, v)).ToList();
        //toReturn = tempVecArray[0];



        ///SOLUTION 2: Get the farthest node from his own position, then calculate if the player is closer to any point on the path than he himself,
        ///if they are they are probably blocking said path, then get the farthest node away from this last farthest position
        ///NOTE: Probably very very performant as it would do ChooseFarthestNodeFromPosition AND calculatePath within the same frame, if not a coroutine

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
                Logger.LogDebug($"getting new farthestNode {farthestNode}");
            }
            else
            {
                Logger.LogInfo($"managed to finish first path to farthestNode {farthestNode}");
            }
            toReturn = farthestNode;
        }
        else
        {
            Logger.LogError("failed to get toReturn!");
        }

        Logger.LogInfo($"sending toReturn {toReturn} fleeToWhileCalculating {toReturn.position}");
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
        }
        for (int i = 0; i < path.corners.Length; i++)
        {
            Vector3 currentCorner = path.corners[i];
            float ownDistance = Vector3.Distance(transform.position, currentCorner);
            float playerDistance = Vector3.Distance(fleeFrom.transform.position, currentCorner);
            if (ownDistance > playerDistance && playerDistance < maxPlayerDistanceToHidePath)
            {
                Logger.LogWarning($"player {playerDistance} closer than I {ownDistance} to corner {currentCorner}");
                return true;
            }
        }
        return false;
    }

    private bool EveryoneOutside(bool factorInAwarenessDistance = true, float overrideAwarenessDistance = -1)
    {
        if (overrideAwarenessDistance == -1)
        {
            overrideAwarenessDistance = awarenessDistance;
        }
        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
        {
            if (!player.isPlayerControlled)
            {
                continue;
            }
            if (isOutside == !player.isInsideFactory && (!factorInAwarenessDistance || Vector3.Distance(transform.position, player.transform.position) < overrideAwarenessDistance))
            {
                everyoneOutside = false;
                return everyoneOutside;
            }
        }
        everyoneOutside = true;
        return everyoneOutside;
    }

    private PlayerControllerB AnyPlayerHasLineOfSight(Vector3 pos = default)
    {
        if (pos == default)
        {
            pos = eye.position;
        }
        for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[i];
            if (player != null && player.isPlayerControlled && player.HasLineOfSightToPosition(pos, 50, 40))
            {
                return player;
            }
        }
        return null;
    }

    public static void DestroyAllNodeLights()
    {
        Light[] allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        int lightsDestroyed = 0;
        for (int i = allLights.Length - 1; i >= 0; i--)
        {
            GameObject lightObj = allLights[i].gameObject;
            if (lightObj.name == "EnemyNodeLight(Clone)")
            {
                Destroy(lightObj);
                lightsDestroyed++;
            }
        }
        Logger.LogInfo($"DestroyAllNodeLights finished with lightsDestroyed {lightsDestroyed}");
    }



    //The results of or going into new states
    private void ResetOnEveryoneOutside(bool unlinkItem = true, bool despawnNetObj = false, bool resetAmbush = false)
    {
        if (!IsServer)
        {
            Logger.LogWarning($"non-server owner trying to reset, relinquishing control back to player #0 in hopes they notice no one is outside during chase, to reset properly");
            ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
            return;
        }
        Logger.LogWarning("RESET!!!");
        if (linkedItem != null)
        {
            NetworkObject netObj = linkedItem.NetworkObject;
            if (unlinkItem)
            {
                Logger.LogDebug($"unlinking {linkedItem}");
                linkedItem.ToggleAudioFromEnemy(false);
                linkedItem = null;
            }
            if (despawnNetObj && netObj != null)
            {
                Logger.LogInfo($"despawning {netObj}");
                netObj.Despawn();
            }
        }
        if (resetAmbush)
        {
            Logger.LogDebug("resetting ambush"); 
            performedAmushAttack = false;
            SwitchToAttackSequence(0);
        }
        setLureLocally = false;
        reachedHideSpot = false;
        targetPlayer = null;
        movingTowardsTargetPlayer = false;
        hideSpot = null;
        spawnLureAt = null;
        waitingToSpawnLure = false;
        tempTimer = 0;
        lastSeenAt = null;
        fleeToWhileCalculating = null;
        pickLurePositionCoroutine = null;
        seenDuringThisAmbush = false;
        currentSearch.timesFinishingSearch = 0;
        currentAttackIndex = -1;
        Logger.LogInfo("set everything back");
        DestroyAllNodeLights();
        if (currentBehaviourStateIndex != 0)
        {
            Logger.LogDebug("exiting chase");
            SwitchToBehaviourState(0);
        }
        Logger.LogInfo("Successfully reached end of ResetOnEveryoneOutside()");
    }

    private void OnReachHideSpot()
    {
        reachedHideSpot = true;
        movingTowardsTargetPlayer = false;
        tempTimer = 0;
        if (linkedItem != null)
        {
            linkedItem.ToggleAudioFromEnemy(true);
        }
        else
        {
            ToggleAudioServerRpc(true);
        }
    }

    private void SpawnLureAndHide()
    {
        setLureLocally = true;
        int randomNr = Random.Range(1, 101);
        Logger.LogDebug(randomNr);
        if (!performedAmushAttack || randomNr <= chanceToPlayFakeAudio)
        {
            SpawnLureItemServerRpc();
        }
        waitingToSpawnLure = false;
        reachedHideSpot = false;
        tempTimer = 0f;
        //SOLVED (2025-01-09) - NOTE (2025-01-08):
        //Find better null check, also this should stay ==
        if (hideSpot == null)
        {
            Logger.LogInfo("ChooseFarthestNodeFromPosition()!!");
            hideSpot = ChooseFarthestNodeFromPosition(spawnLureAt.position, true, 0, true, (int)maxDistanceBetweenFarawayNodes, true);
        }
        Logger.LogDebug($"spawned lure, going back to state 0");
        //NavMeshPath pathTohide = new NavMeshPath();
        //agent.CalculatePath(hideSpot.position, pathTohide);
        //float length = 0f;
        //for (int i = 1; i < pathTohide.corners.Length; i++)
        //{
        //    length += Vector3.Distance(pathTohide.corners[i - 1], pathTohide.corners[i]);
        //}
        //Logger.LogInfo($"just to confirm: pathDistance {length}");
        SwitchToBehaviourState(0);
    }

    private void MoveToAmbush()
    {
        if (currentBehaviourStateIndex != 0 || setLureLocally)
        {
            return;
        }
        agent.speed = 0;
        waitingToSpawnLure = true;
        spawnLureAt = null;
        tempTimer = 0;
        if (currentSearch.inProgress)
        {
            StopSearch(currentSearch);
        }
        SwitchToBehaviourState(1);
        ToggleAudioServerRpc(false, true, hideSpeed);
    }

    private void DetectNewSighting(Vector3 lookPos, bool lookImmediately = false)
    {
        if (lookImmediately)
        {
            turnCompass.LookAt(lookPos);
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, turnCompass.eulerAngles.y, transform.eulerAngles.z);
            return;
        }
        lookingTime = Time.realtimeSinceStartup;
        lookingAt = lookPos;
    }

    public void SetEnemyVulnerable(bool setVulnerableTo)
    {
        vulnerable = setVulnerableTo;
        Logger.LogDebug($"vulnerable: {vulnerable}");
    }

    public void SetEnemyInAttackAnimation(bool setInAttackAnimTo)
    {
        inAttackAnimation = setInAttackAnimTo;
        Logger.LogDebug($"inAttackAnimation: {inAttackAnimation}");
        timeLastAttackAnimChange = Time.realtimeSinceStartup;
    }

    private bool GetValidPlayerHoldingLure()
    {
        if (linkedItem == null)
        {
            return false;
        }
        if (linkedItem.playerHeldBy == null)
        {
            return false;
        }
        if (isOutside == linkedItem.playerHeldBy.isInsideFactory)
        {
            return false;
        }
        if (!linkedItem.playerHeldBy.isPlayerControlled)
        {
            return false;
        }
        return true;
    }

    private void LinkEnemyToLure()
    {
        if (linkedItem != null)
        {
            Logger.LogDebug($"{name} #{NetworkObjectId} already linked to {linkedItem.name} #{linkedItem.NetworkObjectId}");
            return;
        }
        EnemyLureTestItem lureItem = FindAnyObjectByType<EnemyLureTestItem>();
        if (lureItem != null)
        {
            lureItem.LinkLureToEnemy(this);
        }
    }

    private void TurnPlayerTo(PlayerControllerB player, Vector3 turnTo = default)
    {
        if (turnTo == default)
        {
            turnTo = transform.position;
        }
        RoundManager.Instance.tempTransform.position = player.transform.position;
        RoundManager.Instance.tempTransform.LookAt(turnTo);
        Quaternion rotation = RoundManager.Instance.tempTransform.rotation;
        player.transform.rotation = rotation;
        player.transform.eulerAngles = new Vector3(0f, player.transform.eulerAngles.y, 0f);
    }

    private IEnumerator PerformPlayerCollision(PlayerControllerB collidedPlayer)
    {
        if (spawnedShovel != null && spawnedShovel.transform.parent != shovelParent)
        {
            Logger.LogDebug($"shovel not on {shovelParent.name} anymore, likely already given to player, breaking");
            yield break;
        }
        if (currentBehaviourStateIndex == 2)
        {
            Logger.LogDebug($"inAttackAnimation? {inAttackAnimation} | debugInstantKillOnChase? {debugInstantKillOnChase}");
            if (debugInstantKillOnChase)
            {
                collidedPlayer.KillPlayer(eye.forward * 3, true, CauseOfDeath.Mauling, 8);
                yield break;
            }
            if (inAttackAnimation)
            {
                yield break;
            }
            Logger.LogInfo("starting kill animation!!");
            agent.speed = 0;
            SetEnemyInAttackAnimation(true);
            ToggleAudioServerRpc(false);
            //TurnPlayerTo(collidedPlayer);
            //collidedPlayer.disableMoveInput = true;
            //yield return null;
            //collidedPlayer.disableMoveInput = false;
            //collidedPlayer.externalForces = Vector3.zero;
            //NOTE (2025-01-13):
            //The point is for this to be slightly more restrictive and focus the player's attention from the flight to the monster
            //The above code is not bad but does not cancel out the running speed

            inSpecialAnimationWithPlayer = collidedPlayer;
            collidedPlayer.inAnimationWithEnemy = this;
            collidedPlayer.inSpecialInteractAnimation = true;
            RoundManager.Instance.tempTransform.position = collidedPlayer.transform.position;
            RoundManager.Instance.tempTransform.LookAt(transform.position);
            Quaternion startingPlayerRot = collidedPlayer.transform.rotation;
            Quaternion targetPlayerRot = RoundManager.Instance.tempTransform.rotation;
            for (int i = 0; i < debugTurnPlayerIterations; i++)
            {
                collidedPlayer.transform.rotation = Quaternion.Lerp(startingPlayerRot, targetPlayerRot, (float)i / (float)debugTurnPlayerIterations);
                collidedPlayer.transform.eulerAngles = new Vector3(0f, collidedPlayer.transform.eulerAngles.y, 0f);
                yield return null;
            }
            DetectNewSighting(collidedPlayer.transform.position, true);
            yield return new WaitForSeconds(0.1f);
            if (performedAmushAttack)
            {
                collidedPlayer.DropAllHeldItemsAndSync();
                SetAnimation("GiveShovel");
                yield return new WaitForSeconds(0.75f);
                GiveShovelLocal(collidedPlayer);
                GiveShovelToPlayerServerRpc((int)collidedPlayer.playerClientId);
                if (linkedItem != null && linkedItem.fakeAudio.isPlaying)
                {
                    linkedItem.ToggleAudioFromEnemy(false);
                }
                collidedPlayer.inSpecialInteractAnimation = false;
                collidedPlayer.inAnimationWithEnemy = null;
                inSpecialAnimationWithPlayer = null;
                yield return null;
                collidedPlayer.externalForceAutoFade = transform.forward * 20 + Vector3.up * 20;
                //yield return new WaitForSeconds(0.25f);
                PlayAnimSFX(3);
                yield return new WaitForSeconds(1.0f);
                SetEnemyInAttackAnimation(false);
            }
            else
            {
                collidedPlayer.inSpecialInteractAnimation = false;
                collidedPlayer.inAnimationWithEnemy = null;
                inSpecialAnimationWithPlayer = null;
                yield return null;
                collidedPlayer.externalForceAutoFade = transform.forward * 5 + Vector3.up * 5;
                yield return new WaitForSeconds(0.33f);
                PerformNextAttack();
            }
        }
        else
        {
            collidedPlayer.DamagePlayer(10, true, true, CauseOfDeath.Kicking, 0, false, eye.forward * 10 + Vector3.up * 3);
        }
        //bool shouldKill = currentBehaviourStateIndex == 2 && debugInstantKillOnChase;
        //Logger.LogDebug($"shouldKill? {shouldKill}");
        //if (shouldKill)
        //{
        //    collidedPlayer.KillPlayer(eye.forward * 3, true, CauseOfDeath.Mauling, 8);
        //    linkedItem.ToggleAudioFromEnemy(false);
        //}
        //else
        //{
        //    collidedPlayer.DamagePlayer(10);
        //}
    }

    private void PerformNextAttack()
    {
        ToggleAudioServerRpc(false);
        DetectNewSighting(targetPlayer.transform.position, true);
        agent.speed = 0;
        SetEnemyInAttackAnimation(true);
        GetNextAttackIndex();
        //currentAttackTrigger = attackSequence[currentAttackIndex].ToString();
        currentAttackTrigger = currentAttackSequence.attackAnimSequence[currentAttackIndex].ToString();
        currentAttackState = "WindUp";
        SetAnimation($"{currentAttackTrigger}{currentAttackState}", currentAttackIndex);
    }

    private void GetNextAttackIndex()
    {
        currentAttackIndex++;
        //if (currentAttackIndex >= attackSequence.Length)
        //{
        //    currentAttackIndex = 0;
        //}
        //Logger.LogDebug($"index: {currentAttackIndex} | trigger: {attackSequence[currentAttackIndex].ToString()}");

        if (currentAttackIndex >= currentAttackSequence.attackAnimSequence.Length)
        {
            currentAttackIndex = 0;
        }
        Logger.LogDebug($"index: {currentAttackIndex} | trigger: {currentAttackSequence.attackAnimSequence[currentAttackIndex].ToString()}");
    }

    private void SwitchToAttackSequence(int index)
    {
        currentAttackSequence = testAttackAnimSequences[index];
        currentAttackIndex = -1;
        Logger.LogInfo($"!!!SWITCHING TO NEW ATTACK PATTERN: [{index}] '{currentAttackSequence.name}'");
    }



    //Overrides and other shared EnemyAI code
    public override void FinishedCurrentSearchRoutine()
    {
        base.FinishedCurrentSearchRoutine();
        Logger.LogDebug($"FINISHED SEARCH; times {currentSearch.timesFinishingSearch}");
        if (currentSearch.timesFinishingSearch > debugMaxSearches)
        {
            Logger.LogDebug("NO MORE SEARCHES LEFT");
            StopSearch(currentSearch);
            destination = transform.position;
            agent.SetDestination(destination);
        }
    }

    public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null)
    {
        base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
        Logger.LogDebug($"stunning {name} #{NetworkObjectId} [{thisEnemyIndex}]");
    }

    public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        if (isEnemyDead)
        {
            return;
        }
        Logger.LogDebug($"hitEnemy | vulnerable: {vulnerable}");
        if (vulnerable)
        {
            creatureSFX.PlayOneShot(intimidateSFX);
            enemyHP -= force;
            Logger.LogDebug($"HP: {enemyHP}");
            if (enemyHP == 3)
            {
                changeMatOf.material = changeMatTo;
                SwitchToAttackSequence(1);
                creatureSFX.PlayOneShot(bellSFX);
                currentAttackIndex = -1;
            }
            if (enemyHP <= 0)
            {
                KillEnemyOnOwnerClient();
            }
        }
        else
        {
            creatureSFX.PlayOneShot(blockSFX);
        }
    }

    public override void KillEnemy(bool destroy = false)
    {
        base.KillEnemy(destroy);
        Landmine.SpawnExplosion(transform.position, true, 0, 0, 0, 0);
        EnableEnemyMesh(false);
        StopPickLurePositionCoroutine();
        realAudio.Stop();
        SetEnemyInAttackAnimation(false);
    }

    public override void ReachedNodeInSearch()
    {
        base.ReachedNodeInSearch();
        Logger.LogDebug($"{name} #{NetworkObjectId} reached node");
    }

    public override void OnCollideWithPlayer(Collider other)
    {
        if (isEnemyDead || inAttackAnimation)
        {
            return;
        }
        if (Time.realtimeSinceStartup - timeLastCollisionLocalPlayer > collisionCooldown)
        {
            PlayerControllerB collidedPlayer = MeetsStandardPlayerCollisionConditions(other);
            if (collidedPlayer != null && collidedPlayer == targetPlayer && collidedPlayer.thisController.isGrounded)
            {
                base.OnCollideWithPlayer(other);
                timeLastCollisionLocalPlayer = Time.realtimeSinceStartup;
                StartCoroutine(PerformPlayerCollision(collidedPlayer));
            }
        }
    }

    public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
    {
        if (isEnemyDead)
        {
            return;
        }
        base.OnCollideWithEnemy(other, collidedEnemy);
        if (collidedEnemy != null && !collidedEnemy.isEnemyDead && collidedEnemy.enemyType.canDie)
        {
            Logger.LogDebug($"demolishing {collidedEnemy.name} #{collidedEnemy.thisEnemyIndex}");
            collidedEnemy.KillEnemyOnOwnerClient();
        }
    }

    public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
    {
        base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
        if (!OnDetectNoiseValid())
        {
            return;
        }
        if (noiseID == 6 || noiseID == 7)
        {
            noiseLoudness = 0.6f;
        }
        float distanceFromNoise = Mathf.Max(1, 80 - Mathf.Pow(Vector3.Distance(transform.position, noisePosition), noiseDistanceDropoffPower));
        float loudnessOfNoise = noiseLoudness * 100;
        float additive = distanceFromNoise + loudnessOfNoise;
        Logger.LogDebug($"(ID: {noiseID}) // {distanceFromNoise} + {loudnessOfNoise} = {additive}");
        if (additive >= noiseThreshold * noiseLookThreshold)
        {
            DetectNewSighting(noisePosition);
        }
        if (additive >= noiseThreshold)
        {
            Logger.LogDebug($"passed threshold, moving to state 1");
            MoveToAmbush();
        }
    }

    private bool OnDetectNoiseValid()
    {
        if (!IsOwner)
        {
            Logger.LogDebug($"DetectNoise(): not owner");
            return false;
        }
        if (isEnemyDead)
        {
            Logger.LogDebug($"DetectNoise(): dead");
            return false;
        }
        if (!statesDetectingNoise.Contains(currentBehaviourStateIndex))
        {
            Logger.LogDebug($"DetectNoise(): not state detecting noise");
            return false;
        }
        if (EveryoneOutside(true))
        {
            Logger.LogDebug($"DetectNoise(): everyone outside awarness radius");
            return false;
        }
        return true;
    }



    //Rpc's
    [ServerRpc(RequireOwnership = false)]
    private void SpawnLureItemServerRpc()
    {
        GameObject obj = Instantiate(itemToSpawn.spawnPrefab, transform.position + Vector3.up * 2, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer);
        obj.GetComponent<NetworkObject>().Spawn();
    }

    private void SpawnShovelAndSync()
    {
        spawnedShovel = Instantiate(shovelItem.spawnPrefab, shovelParent.transform.position, Quaternion.identity, shovelParent);
        Shovel script = spawnedShovel.GetComponent<Shovel>();
        script.hasHitGround = true;
        script.reachedFloorTarget = true;
        script.isInFactory = true;
        script.grabbable = false;
        script.parentObject = shovelParent;
        NetworkObject netObj = script.NetworkObject;
        netObj.Spawn();
        Logger.LogDebug($"spawned shovel on host");
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
            Logger.LogError("failed to get Shovel netObj on client!");
            yield break;
        }
        yield return new WaitForEndOfFrame();
        spawnedShovel = netObj.gameObject;
        netObj.transform.SetParent(shovelParent);
        Shovel script = netObj.GetComponent<Shovel>();
        script.hasHitGround = true;
        script.reachedFloorTarget = true;
        script.isInFactory = true;
        script.grabbable = false;
        script.parentObject = shovelParent;
        Logger.LogDebug($"spawned shovel on client");
    }

    [ServerRpc(RequireOwnership = false)]
    private void GiveShovelToPlayerServerRpc(int playerID)
    {
        GiveShovelToPlayerClientRpc(playerID);
    }

    [ClientRpc]
    private void GiveShovelToPlayerClientRpc(int playerID)
    {
        if (playerID != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            GiveShovelLocal(StartOfRound.Instance.allPlayerScripts[playerID]);
        }
    }

    private void GiveShovelLocal(PlayerControllerB giveToPlayer)
    {
        Logger.LogWarning($"GIVING SHOVEL TO {giveToPlayer}!!!");
        Shovel script = spawnedShovel.GetComponent<Shovel>();

        giveToPlayer.ItemSlots[giveToPlayer.currentItemSlot] = script;
        giveToPlayer.playerBodyAnimator.SetBool(shovelItem.grabAnim, true);
        giveToPlayer.playerBodyAnimator.SetBool("GrabValidated", true);
        giveToPlayer.playerBodyAnimator.SetBool("cancelHolding", false);
        giveToPlayer.playerBodyAnimator.ResetTrigger("SwitchHoldAnimationTwoHanded");
        giveToPlayer.playerBodyAnimator.SetTrigger("SwitchHoldAnimationTwoHanded");
        giveToPlayer.itemAudio.PlayOneShot(shovelItem.grabSFX);
        giveToPlayer.currentlyHeldObject = script;
        giveToPlayer.currentlyHeldObjectServer = script;
        giveToPlayer.twoHanded = shovelItem.twoHanded;
        giveToPlayer.twoHandedAnimation = shovelItem.twoHandedAnimation;
        giveToPlayer.isHoldingObject = true;
        giveToPlayer.carryWeight = Mathf.Clamp(giveToPlayer.carryWeight + (shovelItem.weight - 1f), 1f, 10f);
        if (giveToPlayer == GameNetworkManager.Instance.localPlayerController)
        {
            HUDManager.Instance.itemSlotIcons[giveToPlayer.currentItemSlot].sprite = shovelItem.itemIcon;
            HUDManager.Instance.itemSlotIcons[giveToPlayer.currentItemSlot].enabled = true;
        }

        script.parentObject = giveToPlayer == GameNetworkManager.Instance.localPlayerController ? giveToPlayer.localItemHolder : giveToPlayer.serverItemHolder;
        script.isHeld = true;
        script.playerHeldBy = giveToPlayer;
        script.grabbable = true;
        script.transform.localScale = script.originalScale;
        script.EnableItemMeshes(true);
        script.EnablePhysics(false);
        script.GrabItemOnClient();
        script.EquipItem();
        if (IsServer)
        {
            try
            {
                script.NetworkObject.ChangeOwnership(giveToPlayer.actualClientId);
            }
            catch
            {
                Logger.LogError("failed to ChangeOwnership to new player!");
            }
        }
        spawnedShovel.transform.SetParent(script.parentObject, true);
        Logger.LogDebug("reached end of GiveShovelLocal()");
    }

    public void ChaseNewPlayer(PlayerControllerB player)
    {
        if (player == null)
        {
            return;
        }
        if (IsOwner && !movingTowardsTargetPlayer)
        {
            Logger.LogWarning($"started new chase on owner, should sync AI-Calculation to chased player {player.name}");
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
        agent.speed = chaseSpeed;
        SetMovingTowardsTargetPlayer(playerToChase);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleAudioServerRpc(bool enableSFX, bool isFootstepSFX = true, float agentSpeed = -1)
    {
        ToggleAudioClientRpc(enableSFX, isFootstepSFX, agentSpeed);
    }

    [ClientRpc]
    private void ToggleAudioClientRpc(bool enableSFX, bool isFootstepSFX = true, float agentSpeed = -1)
    {
        if (agentSpeed == -1)
        {
            agentSpeed = agent.speed;
        }
        agent.speed = agentSpeed;
        ToggleRealAudio(enableSFX, isFootstepSFX);
    }

    private void ToggleRealAudio(bool enableSFX, bool isFootstepSFX)
    {
        if (!enableSFX || realAudio.isPlaying)
        {
            realAudio.Stop();
        }
        if (enableSFX)
        {
            realAudio.clip = isFootstepSFX ? footstepSFX : runSFX;
            realAudio.Play();
        }
    }

    public void PlayAnimSFX(int switchCase, bool sync = true, int sentPlayerID = -1)
    {
        switch (switchCase)
        {
            case 0:
                creatureSFX.PlayOneShot(reelSFX);
                break;
            case 1:
                creatureSFX.PlayOneShot(punchSFX);
                break;
            case 2:
                creatureSFX.PlayOneShot(intimidateSFX);
                break;
            case 3:
                creatureSFX.PlayOneShot(bellSFX);
                break;
            case 4:
                if (!performedAmushAttack)
                {
                    creatureSFX.PlayOneShot(intimidateSFX);
                    performedAmushAttack = true;
                    SwitchToAttackSequence(enemyHP > 3 ? 0 : 1);
                }
                break;
        }
        if (sync)
        {
            if (sentPlayerID == -1)
            {
                sentPlayerID = (int)GameNetworkManager.Instance.localPlayerController.playerClientId;
            }
            PlayAnimSFXServerRpc(switchCase, sentPlayerID);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayAnimSFXServerRpc(int switchCase, int sentPlayerID)
    {
        PlayAnimSFXClientRpc(switchCase, sentPlayerID);
    }

    [ClientRpc]
    private void PlayAnimSFXClientRpc(int switchCase, int sentPlayerID)
    {
        if (sentPlayerID != (int)GameNetworkManager.Instance.localPlayerController.playerClientId)
        {
            PlayAnimSFX(switchCase, false);
        }
    }

    public void SetAnimation(string trigger,int currentAttackOwner = -1)
    {
        if (!IsOwner)
        {
            return;
        }
        if (currentAttackOwner == -1)
        {
            currentAttackOwner = currentAttackIndex;
        }
        creatureAnimator.SetTrigger(trigger);
        SetAnimationNonOwnerServerRpc(trigger, currentAttackOwner);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetAnimationNonOwnerServerRpc(string trigger, int currentAttackOwner)
    {
        SetAnimationNonOwnerClientRpc(trigger, currentAttackOwner);
    }

    [ClientRpc]
    private void SetAnimationNonOwnerClientRpc(string trigger, int currentAttackOwner)
    {
        if (!IsOwner)
        {
            currentAttackIndex = currentAttackOwner;
            creatureAnimator.SetTrigger(trigger);
        }
    }
}
