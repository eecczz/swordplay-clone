using FIMSpace.FProceduralAnimation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(CharacterController))]
public class EnemyWorker : MonoBehaviour
{
    private static readonly List<EnemyWorker> ActiveEnemies = new();
    private static readonly Dictionary<EnemyWorker, int> SlotAssignments = new();

    private Animator anim;
    private NavMeshAgent nav;
    private Animator playerAnim;
    [SerializeField] private Transform crosshead;
    public static Transform psword;
    [SerializeField] private Transform sword;
    [SerializeField] private Vector3 swordOffset;
    private float sensitivity = 0.01f;
    private float tx, ty, rtx, rty;
    private Vector2 attackDirection;
    private int attackSequence;
    [Header("Attack hit detection")]
    [SerializeField, Range(0f, 1f)] private float attackHitStart = 0f;
    [SerializeField, Range(0f, 1.2f)] private float attackHitEnd = 1.05f;
    private int cool = 100;
    private int cool1 = 100;
    private float guardTime = -1;
    private int swing, guard;
    private GameObject hitVFX, shieldVFX;
    private AudioSource audioSource;
    private Rigidbody rigid;
    private CapsuleCollider movementCapsule;
    private CharacterController characterController;
    private float verticalVelocity;
    private Rig rig;
    private LegsAnimator legAnim;
    [SerializeField] private MultiAimConstraint ma;
    [SerializeField] private Vector3 headRotOffset;
    [SerializeField] private bool headOffsetIsRootLocal;
    [SerializeField, Min(0.1f)] private float headLookSpeed = 12f;
    private Vector3 smoothedHeadLookDirection;
    private bool headLookDirectionInitialized;
    private Quaternion headAxisCorrection;
    private bool headAxisCorrectionInitialized;
    [SerializeField] private int guardRate = 20;
    [SerializeField] private int health = 2;
    [SerializeField] private float movementSpeed = 5f;
    [Header("NavMesh pursuit")]
    [SerializeField] private float engagementRadius = 4.5f;
    [SerializeField] private float additionalRingSpacing = 2.5f;
    [SerializeField] private float slotSampleRadius = 2.5f;
    [SerializeField] private float destinationRefreshInterval = 0.12f;
    private float destinationRefreshTimer;
    private Vector3 directPursuitTarget;
    private bool useDirectPursuit;
    private Vector3 previousNavPosition;
    private float navStuckTimer;
    private Vector3 lastRequestedSlot;
    private bool hasRequestedSlot;
    private static readonly float[] SlotRadialOffsets = { 0f, 0.75f, 1.5f, -0.5f };
    private static readonly float[] SlotLateralOffsets = { 0f, 0.65f, -0.65f, 1.2f, -1.2f };
    private NavMeshPath slotPathProbe;
    [SerializeField, Min(0.2f)] private float knockbackMaxTime = 1.5f;
    [SerializeField, Min(0.01f)] private float knockbackStopSpeed = 0.15f;
    [SerializeField, Min(0.5f)] private float knockbackDistance = 5f;
    [SerializeField, Min(0.1f)] private float facingLerpSpeed = 10f;
    [SerializeField, Min(0.1f)] private float knockbackSlideDamping = 6f;
    private bool physicsReaction;
    private float physicsReactionTimer;
    private Vector3 knockbackStartPosition;
    private Vector3 knockbackDirection;
    private RigBuilder rb;
    [SerializeField] private bool miiMotion;
    private bool usesDirectHandSocketWeapon;
    // ------- 프레임 독립용: 초 단위 타이머로 변환 -------
    [Header("Frame→Seconds 변환(참고 FPS=60 기준)")]
    [SerializeField] float refFps = 60f;

    [SerializeField] int cool1Frames = 100;            // 기존: 100
    [SerializeField] Vector2Int coolFramesRange = new(100, 500);   // 기존: 100~500
    [SerializeField] Vector2Int guardFramesRange = new(500, 1000); // 기존: 500~1000

    float cool1Timer, coolTimer, guardTimer; // seconds

    // ------- 보간 속도(초당) : 기존 0.01/0.1 per-frame과 동일 체감 되도록 변환 -------
    // per-frame alpha≈0.1 → k≈-fps*ln(0.9), 0.01 → k≈-fps*ln(0.99)
    float kSlow, kFast;
    GameObject rightTemp, leftTemp;

    [Header("Rig refs")]
    [SerializeField] MultiParentConstraint weaponBlend; // Rig_Weapon에 붙은 컴포넌트
    [SerializeField] TwoBoneIKConstraint rightArmIK;    // Rig_Hands
    [SerializeField] TwoBoneIKConstraint leftArmIK;     // Rig_Hands

    public Vector2 AttackDirection => attackDirection;
    public Vector2 GuardDirection => new(tx, ty);
    public int AttackSequence => attackSequence;
    public bool IsAttackHitActive => health > -1 && attackSequence > 0
        && CombatDirectionUtility.IsAttackStateActive(anim, attackHitStart, attackHitEnd);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEnemySlots()
    {
        ActiveEnemies.Clear();
        SlotAssignments.Clear();
    }

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
            ActiveEnemies.Add(this);
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        SlotAssignments.Remove(this);
    }

    private void Awake()
    {
        slotPathProbe = new NavMeshPath();
        anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            AnchorSimpleWeaponUnderRightSocket();
        }
        InitializeHeadAxisCorrection();
    }

    private void AnchorSimpleWeaponUnderRightSocket()
    {
        if (sword == null || anim == null)
            return;

        Transform rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (rightHand == null)
            return;

        Transform rightSocket = null;
        foreach (Transform child in rightHand.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Weapon_r Socket")
            {
                rightSocket = child;
                break;
            }
        }

        if (rightSocket == null || sword == rightSocket)
            return;

        if (sword.IsChildOf(rightSocket))
        {
            DisableSimpleWeaponRig();
            return;
        }

        // The knight installer previously placed the rendered weapon beside the socket as a
        // direct RightHand child. Animator can restore such extra animated transforms when a
        // state starts, which discards the grip pose authored in the scene. Move that whole
        // weapon branch below the non-animated socket while preserving its exact world pose.
        Transform weaponRoot = sword;
        while (weaponRoot.parent != null && weaponRoot.parent != rightHand)
            weaponRoot = weaponRoot.parent;

        if (weaponRoot.parent != rightHand
            || weaponRoot.name.IndexOf("RightHand Weapon", System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        weaponRoot.SetParent(rightSocket, true);
        DisableSimpleWeaponRig();
    }

    // Simple knight weapons follow the authored socket pose; legacy blend rigging is not used.
    private void DisableSimpleWeaponRig()
    {
        usesDirectHandSocketWeapon = true;
        if (weaponBlend != null) weaponBlend.weight = 0f;
        if (rightArmIK != null) rightArmIK.weight = 0f;
        if (leftArmIK != null) leftArmIK.weight = 0f;
    }

    // Start is called before the first frame update
    private void Start()
    {
        if (miiMotion)
        {
            if (weaponBlend != null) weaponBlend.weight = 0f;
            if (rightArmIK != null) rightArmIK.weight = 0f;
            if (leftArmIK != null) leftArmIK.weight = 0f;
        }
        // 타이머 초기화(초 단위)
        cool1Timer = cool1Frames / refFps; // ≈ 1.67s
        coolTimer = 0f;
        guardTimer = -1f;

        // 보간 속도 상수(초당)
        kFast = -refFps * Mathf.Log(0.9f);  // per-frame 0.1과 유사한 반응
        kSlow = -refFps * Mathf.Log(0.99f); // per-frame 0.01과 유사한 반응
        nav = ResolveRootAgent();
        hitVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeHitVFX");
        shieldVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeGuardVFX");
        audioSource = GetComponent<AudioSource>();
        playerAnim = PlayerWorker.playerAnimator != null
            ? PlayerWorker.playerAnimator
            : PlayerWorker.player != null ? PlayerWorker.player.GetComponent<Animator>() : null;
        rigid = GetComponent<Rigidbody>();
        if (rigid == null) rigid = gameObject.AddComponent<Rigidbody>();
        movementCapsule = GetComponent<CapsuleCollider>();
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
            characterController = gameObject.AddComponent<CharacterController>();
        ConfigureCharacterController();
        if (nav != null)
        {
            PlaceAgentOnNavMesh();
            previousNavPosition = transform.position;
        }
        if (rigid != null && nav != null)
        {
            rigid.isKinematic = true;
            rigid.useGravity = false;
            rigid.detectCollisions = false;
            rigid.constraints = RigidbodyConstraints.FreezeAll;
        }
        TargetMatching weaponTarget = GetComponentInChildren<TargetMatching>(true);
        if (weaponTarget != null)
        {
            psword = weaponTarget.transform;
            if (sword == null) sword = psword;
            SwordHitbox.Configure(weaponTarget.gameObject, this);
        }
        rig = GetComponentInChildren<Rig>();
        legAnim = GetComponent<LegsAnimator>();
        rb = GetComponent<RigBuilder>();
        rightTemp = new GameObject("RightTemp");
        leftTemp = new GameObject("LeftTemp");
        IgnoreCombatantMovementCollisions();
    }

    private void IgnoreCombatantMovementCollisions()
    {
        if (characterController == null) return;

        Collider[] ownMovementColliders = GetComponents<Collider>();
        if (PlayerWorker.player != null)
            IgnoreColliderSets(ownMovementColliders, PlayerWorker.player.GetComponents<Collider>());

        foreach (EnemyWorker other in ActiveEnemies)
        {
            if (other == null || other == this) continue;
            IgnoreColliderSets(ownMovementColliders, other.GetComponents<Collider>());
        }
    }

    private static void IgnoreColliderSets(Collider[] first, Collider[] second)
    {
        foreach (Collider a in first)
        foreach (Collider b in second)
            if (a != null && b != null && a != b)
                Physics.IgnoreCollision(a, b, true);
    }

    private NavMeshAgent ResolveRootAgent()
    {
        NavMeshAgent rootAgent = GetComponent<NavMeshAgent>();
        if (rootAgent == null)
            Debug.LogError($"{name}: NavMeshAgent must be serialized on the EnemyWorker root.", this);
        return rootAgent;
    }

    private void ConfigureCharacterController()
    {
        if (characterController == null)
            return;

        if (movementCapsule != null)
        {
            characterController.center = movementCapsule.center;
            characterController.radius = movementCapsule.radius;
            characterController.height = movementCapsule.height;
            movementCapsule.enabled = false;
        }

        characterController.stepOffset = Mathf.Min(0.45f,
            Mathf.Max(0f, characterController.height - characterController.radius * 2f));
        characterController.slopeLimit = 55f;
        characterController.skinWidth = 0.03f;
        characterController.minMoveDistance = 0f;
        characterController.enableOverlapRecovery = true;
    }

    private bool IsPlayerInView()
    {
        if (PlayerWorker.player == null)
            return false;
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, PlayerWorker.player.position - transform.position, out hit))
        {
            if (hit.collider.gameObject != PlayerWorker.player)
                return false;
        }
        return (PlayerWorker.player.position - transform.position).magnitude < 10f;
    }

    // Update is called once per frame
    void Update()
    {
        float dt = Time.deltaTime;

        if (swing==0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            // Animator 파라미터
            anim.SetFloat("tx", tx);
            anim.SetFloat("ty", ty);
        }
        float testScore = (new Vector2(tx, ty)).magnitude; // (0,0)에서의 거리
        anim.SetFloat("time", 1f - testScore / 180f);

        // 플레이어 방향 회전(공격 중/스턴 아닐 때)
        if (!physicsReaction && PlayerWorker.player != null && swing == 0 && health > -1)
        {
            Vector3 toPlayer = PlayerWorker.player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                float facingT = 1f - Mathf.Exp(-facingLerpSpeed * dt);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, facingT);
            }
        }

        UpdateNavigation(dt);

        // -------- cool1: 조준 보정 타이머(초) --------
        if (cool1Timer > 0f)
        {
            cool1Timer = Mathf.Max(0f, cool1Timer - dt);

            // 공격 중 빠르게, 그 외에는 느리게
            bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
            float k = inAttack ? kFast : kSlow;                 // 초당 반응 속도
            float a = 1f - Mathf.Exp(-k * dt);                  // 프레임 독립계수

            tx = Mathf.Lerp(tx, rtx, a);
            ty = Mathf.Lerp(ty, rty, a);
        }
        if (cool1Timer <= 0f)
        {
            // 주기 리셋
            cool1Timer = cool1Frames / refFps;
            // 준비 자세와 가드 자세 모두 같은 방식으로 새 목표 방향을 선택한다.
            rtx = Random.Range(-180f, 180f);
            rty = Random.Range(-30f, 150f);
        }

        // -------- cool: 공격 트리거 타이머(초) --------
        if (coolTimer > 0f) coolTimer = Mathf.Max(0f, coolTimer - dt);

        if (coolTimer <= 0f
            && guard == 0
            && guardTimer <= 0f
            && health > -1
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            coolTimer = -1f;
            int r = Random.Range(0, 100);

            bool badState = anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted")
                            || anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded")
                            || health == -1
                            || PlayerWorker.player == null;

            if (r < 100 - guardRate && badState)
            {
                r = -1;
                coolTimer = RandomRangeFramesToSeconds(coolFramesRange); // 100~500 f → s
            }
            else if (r < 100 - guardRate)
            {
                if (PlayerWorker.ent && PlayerWorker.ent.transform == transform)
                {
                    attackDirection = new Vector2(tx, ty);
                    attackSequence++;
                    anim.CrossFade("Attack", 0f, 0);
                    anim.CrossFade("Attack", 0f, 1);
                    swing = 1;

                    // 반대 방향으로 목표 갱신
                    cool1Timer = cool1Frames / refFps;
                    rtx = Mathf.Clamp(-tx, -180f, 180f);
                    rty = Mathf.Clamp(-ty, -30f, 150f);

                }
                else
                {
                    coolTimer = RandomRangeFramesToSeconds(coolFramesRange);
                }
            }
            else // 가드 선택
            {
                guardTimer = RandomRangeFramesToSeconds(guardFramesRange);
            }
        }

        // 공격 종료 후 처리
        if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            if (swing == 1)
            {
                coolTimer = RandomRangeFramesToSeconds(coolFramesRange);
                swing = 0;
            }
        }

        // -------- 가드 타이머 --------
        if (guardTimer > 0f && health > -1)
        {
            guardTimer = Mathf.Max(0f, guardTimer - dt);

            if (guard == 0)
            {
                tx = Mathf.Clamp(tx, -180f, 180f);
                ty = Mathf.Clamp(ty, -30f, 150f);
                rtx = Mathf.Clamp(rtx, -180f, 180f);
                rty = Mathf.Clamp(rty, -30f, 150f);

                guard = 1;
                anim.CrossFade("BlockIdle", 0f, 0);
                anim.CrossFade("BlockIdle", 0f, 1);

                // 가드 포즈 즉시 보정(프레임 독립)
                //sword.localPosition = new Vector3(sword.localPosition.x + 1f, sword.localPosition.y - 2f, sword.localPosition.z);
                //sword.position += transform.forward; // 월드 전진 1m
            }
        }

        if (guardTimer == 0f)
        {
            coolTimer = RandomRangeFramesToSeconds(coolFramesRange);
            guardTimer = -1f;
            guard = 0;
            anim.CrossFade("Idle", 0f, 0);
            anim.CrossFade("Idle", 0f, 1);
            //sword.localPosition = swordOffset;
        }

        // -------- 기타 상태 가중치 --------
        if (health > -1 && legAnim != null)
        {
            legAnim.UseGluing = !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        }
        if (miiMotion || usesDirectHandSocketWeapon || weaponBlend == null) return;

        float w = Mathf.Clamp01((180f + tx) / 360f);

        // Source 0 = RightSocket, Source 1 = LeftSocket 라고 가정
        var sources = weaponBlend.data.sourceObjects;   // WeightedTransformArray (값 타입 취급 주의)
        sources.SetWeight(0, 1f - w); // 오른손 영향
        sources.SetWeight(1, w);      // 왼손 영향
        weaponBlend.data.sourceObjects = sources;
    }

    // === 유틸 ===
    float RandomRangeFramesToSeconds(Vector2Int framesRange)
    {
        int f = Random.Range(framesRange.x, framesRange.y + 1);
        return f / refFps;
    }

    private int GetAssignedSlot()
    {
        if (SlotAssignments.TryGetValue(this, out int assigned))
            return assigned;

        HashSet<int> occupied = new(SlotAssignments.Values);
        int slot = 0;
        float closestDistance = float.PositiveInfinity;
        int candidateCount = Mathf.Max(8, ActiveEnemies.Count * 2);
        for (int candidate = 0; candidate < candidateCount; candidate++)
        {
            if (occupied.Contains(candidate))
                continue;

            float distance = PlayerWorker.player == null
                ? candidate
                : (CalculateSlotPosition(candidate) - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                slot = candidate;
            }
        }

        SlotAssignments[this] = slot;
        return slot;
    }

    private void ReleaseSlot()
    {
        SlotAssignments.Remove(this);
        ActiveEnemies.Remove(this);
    }

    private Vector3 CalculateSlotPosition(int slot)
    {
        const int slotsPerRing = 8;
        int ring = slot / slotsPerRing;
        int slotInRing = slot % slotsPerRing;
        float angle = slotInRing * (360f / slotsPerRing) + ring * (180f / slotsPerRing);
        float radius = engagementRadius + ring * additionalRingSpacing;
        // The formation translates with the player, but it must not rotate with the
        // player's facing. Otherwise player facing moves every enemy destination,
        // producing a feedback loop where both sides orbit forever.
        Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        return PlayerWorker.player.position + radial * radius;
    }

    private void PlaceAgentOnNavMesh()
    {
        if (nav == null || nav.isOnNavMesh)
            return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, nav.areaMask))
        {
            nav.Warp(hit.position);
            if (!nav.updatePosition && Vector3.Distance(transform.position, hit.position) > 0.05f)
            {
                bool controllerWasEnabled = characterController != null && characterController.enabled;
                if (controllerWasEnabled) characterController.enabled = false;
                transform.position = hit.position;
                if (controllerWasEnabled) characterController.enabled = true;
                nav.nextPosition = transform.position;
            }
        }
    }

    private void UpdateNavigation(float deltaTime)
    {
        bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        if (health < 0 || nav == null || PlayerWorker.player == null)
            return;

        if (physicsReaction)
        {
            UpdateRootPhysicsKnockback(deltaTime);
            return;
        }

        PlaceAgentOnNavMesh();
        int assignedSlot = GetAssignedSlot();
        Vector3 requestedSlot = CalculateSlotPosition(assignedSlot);
        if (!nav.isOnNavMesh)
        {
            MoveTowardWithoutNavMesh(requestedSlot, deltaTime);
            return;
        }

        // Reaching a slot is not a permanent stop. The formation follows the player's
        // position, so any meaningful slot displacement immediately invalidates the stop.
        if (!hasRequestedSlot || (requestedSlot - lastRequestedSlot).sqrMagnitude > 0.04f)
        {
            lastRequestedSlot = requestedSlot;
            hasRequestedSlot = true;
            destinationRefreshTimer = 0f;
        }

        float slotDistance = Vector3.Distance(transform.position, requestedSlot);
        if (inAttack && slotDistance <= nav.stoppingDistance + 0.35f)
        {
            nav.isStopped = true;
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
            return;
        }

        nav.updatePosition = true;
        nav.isStopped = false;
        destinationRefreshTimer -= deltaTime;
        if (destinationRefreshTimer <= 0f)
        {
            destinationRefreshTimer = destinationRefreshInterval;
            useDirectPursuit = false;
            bool pathAssigned = TrySetReachableSlotDestination(assignedSlot, false)
                || TrySetReachableSlotDestination(assignedSlot, true);
            if (!pathAssigned)
            {
                // Never drive directly through level geometry when a path is unavailable.
                // A fresh partial/complete path will be requested on the next refresh.
                nav.ResetPath();
                useDirectPursuit = false;
            }
        }

        Vector3 desiredVelocity;
        if (useDirectPursuit)
        {
            Vector3 toTarget = directPursuitTarget - transform.position;
            toTarget.y = 0f;
            desiredVelocity = toTarget.magnitude <= nav.stoppingDistance + 0.15f
                ? Vector3.zero
                : toTarget.normalized * nav.speed;
        }
        else
        {
            desiredVelocity = nav.desiredVelocity;
        }
        Vector3 localVelocity = transform.InverseTransformDirection(desiredVelocity);
        float speed = Mathf.Max(0.01f, nav.speed);
        anim.SetFloat("mx", Mathf.Clamp(localVelocity.x / speed, -1f, 1f));
        anim.SetFloat("mz", Mathf.Clamp(localVelocity.z / speed, -1f, 1f));

        if (!nav.pathPending && nav.hasPath
                && nav.remainingDistance <= nav.stoppingDistance + 0.15f
                && slotDistance <= nav.stoppingDistance + 0.35f)
        {
            nav.isStopped = true;
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
            desiredVelocity = Vector3.zero;
        }

        if (useDirectPursuit)
        {
            nav.updatePosition = false;
            MoveCharacter(desiredVelocity, deltaTime);
        }
        else
        {
            RecoverStuckAgent(desiredVelocity, deltaTime);
        }
    }

    private void RecoverStuckAgent(Vector3 desiredVelocity, float deltaTime)
    {
        Vector3 moved = transform.position - previousNavPosition;
        moved.y = 0f;
        bool shouldBeMoving = desiredVelocity.sqrMagnitude > 0.09f;
        navStuckTimer = shouldBeMoving && moved.sqrMagnitude < 0.0001f
            ? navStuckTimer + deltaTime
            : 0f;
        previousNavPosition = transform.position;

        if (navStuckTimer < 0.75f) return;

        navStuckTimer = 0f;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit recovery, 1.5f, nav.areaMask))
            nav.Warp(recovery.position);
        nav.ResetPath();
        destinationRefreshTimer = 0f;
    }

    private void MoveTowardWithoutNavMesh(Vector3 target, float deltaTime)
    {
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        Vector3 velocity = toTarget.magnitude <= 0.25f
            ? Vector3.zero
            : toTarget.normalized * movementSpeed;

        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        anim.SetFloat("mx", Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, movementSpeed), -1f, 1f));
        anim.SetFloat("mz", Mathf.Clamp(localVelocity.z / Mathf.Max(0.01f, movementSpeed), -1f, 1f));
        MoveCharacter(velocity, deltaTime);
    }

    private void MoveCharacter(Vector3 horizontalVelocity, float deltaTime)
    {
        if (characterController == null || !characterController.enabled)
            return;

        horizontalVelocity.y = 0f;
        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += Physics.gravity.y * deltaTime;

        CollisionFlags flags = characterController.Move(
            (horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (nav != null && nav.enabled && nav.isOnNavMesh)
            nav.nextPosition = transform.position;
    }

    private bool TrySetReachableSlotDestination(int slot, bool allowPartial)
    {
        Vector3 playerPosition = PlayerWorker.player.position;
        Vector3 requestedPosition = CalculateSlotPosition(slot);
        Vector3 radial = requestedPosition - playerPosition;
        radial.y = 0f;
        if (radial.sqrMagnitude < 0.0001f)
            radial = Vector3.forward;
        radial.Normalize();
        Vector3 tangent = Vector3.Cross(Vector3.up, radial);

        float requestedRadius = Vector3.Distance(
            new Vector3(requestedPosition.x, 0f, requestedPosition.z),
            new Vector3(playerPosition.x, 0f, playerPosition.z));
        float minimumRadius = Mathf.Max(2.2f, engagementRadius * 0.8f);
        float sampleRadius = Mathf.Clamp(slotSampleRadius, 0.5f, 1.25f);
        Vector3 bestDestination = default;
        bool hasBestDestination = false;
        float bestScore = float.PositiveInfinity;
        foreach (float radialOffset in SlotRadialOffsets)
        foreach (float lateralOffset in SlotLateralOffsets)
        {
            float radius = Mathf.Max(minimumRadius, requestedRadius + radialOffset);
            Vector3 candidate = playerPosition + radial * radius + tangent * lateralOffset;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, nav.areaMask))
                continue;

            Vector3 fromPlayer = hit.position - playerPosition;
            fromPlayer.y = 0f;
            if (fromPlayer.magnitude < minimumRadius)
                continue;

            if (!NavMesh.CalculatePath(nav.nextPosition, hit.position, nav.areaMask, slotPathProbe)
                    || slotPathProbe.status == NavMeshPathStatus.PathInvalid
                    || (!allowPartial && slotPathProbe.status != NavMeshPathStatus.PathComplete))
                continue;

            Vector3[] corners = slotPathProbe.corners;
            if (corners == null || corners.Length < 2)
                continue;
            Vector3 pathEnd = corners[corners.Length - 1];
            Vector3 pathEndFromPlayer = pathEnd - playerPosition;
            pathEndFromPlayer.y = 0f;
            if (pathEndFromPlayer.magnitude < minimumRadius)
                continue;

            float score = (pathEnd - requestedPosition).sqrMagnitude;
            if (slotPathProbe.status == NavMeshPathStatus.PathPartial)
                score += 100f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestDestination = hit.position;
            hasBestDestination = true;
        }

        return hasBestDestination
            && NavMesh.CalculatePath(nav.nextPosition, bestDestination, nav.areaMask, slotPathProbe)
            && nav.SetPath(slotPathProbe);
    }

    public void ApplyCombatKnockback(Vector3 direction)
    {
        BeginRootPhysicsKnockback(direction);
    }

    private void BeginRootPhysicsKnockback(Vector3 direction)
    {
        if (nav == null || rigid == null || health < 0)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        knockbackDirection = direction.normalized;
        knockbackStartPosition = transform.position;
        physicsReactionTimer = 0f;
        if (nav.enabled && nav.isOnNavMesh)
        {
            nav.isStopped = true;
            nav.ResetPath();
        }
        nav.enabled = false;
        if (characterController != null) characterController.enabled = false;
        if (movementCapsule != null) movementCapsule.enabled = true;

        rigid.isKinematic = false;
        rigid.useGravity = false;
        rigid.detectCollisions = true;
        rigid.interpolation = RigidbodyInterpolation.Interpolate;
        rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rigid.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
        rigid.linearDamping = knockbackSlideDamping;
        rigid.angularDamping = knockbackSlideDamping;
        rigid.linearVelocity = Vector3.zero;
        rigid.angularVelocity = Vector3.zero;
        rigid.AddForce(knockbackDirection * knockbackDistance * knockbackSlideDamping,
            ForceMode.VelocityChange);
        physicsReaction = true;
    }

    private void UpdateRootPhysicsKnockback(float deltaTime)
    {
        physicsReactionTimer += deltaTime;
        anim.SetFloat("mx", 0f);
        anim.SetFloat("mz", 0f);

        Vector3 travelled = rigid.position - knockbackStartPosition;
        travelled.y = 0f;
        Vector3 velocity = rigid.linearVelocity;
        velocity.y = 0f;
        bool reachedDistance = travelled.magnitude >= knockbackDistance;
        bool settled = physicsReactionTimer > 0.2f
            && velocity.sqrMagnitude <= knockbackStopSpeed * knockbackStopSpeed;
        bool timeout = physicsReactionTimer >= knockbackMaxTime;
        if (!reachedDistance && !settled && !timeout) return;

        if (reachedDistance)
        {
            Vector3 clamped = knockbackStartPosition + knockbackDirection * knockbackDistance;
            clamped.y = rigid.position.y;
            rigid.position = clamped;
        }
        rigid.linearVelocity = Vector3.zero;
        rigid.angularVelocity = Vector3.zero;
        rigid.linearDamping = 0f;
        rigid.isKinematic = true;
        rigid.detectCollisions = false;
        rigid.constraints = RigidbodyConstraints.FreezeAll;
        if (movementCapsule != null) movementCapsule.enabled = false;
        if (characterController != null) characterController.enabled = true;
        physicsReaction = false;
        nav.enabled = true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit recovery, 2f, nav.areaMask))
        {
            nav.Warp(recovery.position);
            nav.updatePosition = true;
        }
        if (nav.isOnNavMesh)
            nav.isStopped = false;
        destinationRefreshTimer = 0f;
    }


    private void LateUpdate()
    {
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
        {
            Transform playerHead = PlayerWorker.playerAnimator != null && PlayerWorker.playerAnimator.isHuman
                ? PlayerWorker.playerAnimator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            Vector3 target = playerHead != null
                ? playerHead.position
                : PlayerWorker.player != null ? PlayerWorker.player.position + Vector3.up * 1.6f : transform.position + transform.forward;
            UpdateHeadLook(target);
        }
        if (miiMotion)
        {
            float weight = Mathf.Clamp01((180 + tx) / 360f);
            Vector3 tmp = anim.GetBoneTransform(HumanBodyBones.RightHand).position;
            Vector3 lp = anim.GetBoneTransform(HumanBodyBones.LeftHand).position;
            anim.GetBoneTransform(HumanBodyBones.RightHand).position += (lp - anim.GetBoneTransform(HumanBodyBones.RightHand).position) * weight;
            anim.GetBoneTransform(HumanBodyBones.LeftHand).position += (tmp - anim.GetBoneTransform(HumanBodyBones.LeftHand).position) * weight;
            rightTemp.transform.position = anim.GetBoneTransform(HumanBodyBones.RightHand).position;
            leftTemp.transform.position = anim.GetBoneTransform(HumanBodyBones.LeftHand).position;
            Quaternion tmp1 = anim.GetBoneTransform(HumanBodyBones.RightHand).rotation;
            Quaternion lp1 = anim.GetBoneTransform(HumanBodyBones.LeftHand).rotation;
            rightTemp.transform.rotation = lp1;
            rightTemp.transform.RotateAround(rightTemp.transform.position, rightTemp.transform.up, 180);
            leftTemp.transform.rotation = tmp1;
            leftTemp.transform.RotateAround(leftTemp.transform.position, leftTemp.transform.up, 180);


            Quaternion rightRot = anim.GetBoneTransform(HumanBodyBones.RightHand).rotation;
            Quaternion leftRot = anim.GetBoneTransform(HumanBodyBones.LeftHand).rotation;
            Quaternion rightTargetRot = rightTemp.transform.rotation;
            Quaternion leftTargetRot = leftTemp.transform.rotation;

            Quaternion newRightRot = new Quaternion(
                rightRot.x + (rightTargetRot.x - rightRot.x) * weight,
                rightRot.y + (rightTargetRot.y - rightRot.y) * weight,
                rightRot.z + (rightTargetRot.z - rightRot.z) * weight,
                rightRot.w + (rightTargetRot.w - rightRot.w) * weight
            ).normalized;

            Quaternion newLeftRot = new Quaternion(
                leftRot.x + (leftTargetRot.x - leftRot.x) * weight,
                leftRot.y + (leftTargetRot.y - leftRot.y) * weight,
                leftRot.z + (leftTargetRot.z - leftRot.z) * weight,
                leftRot.w + (leftTargetRot.w - leftRot.w) * weight
            ).normalized;

            anim.GetBoneTransform(HumanBodyBones.RightHand).rotation = newRightRot;
            anim.GetBoneTransform(HumanBodyBones.LeftHand).rotation = newLeftRot;
        }
    }

    private void UpdateHeadLook(Vector3 targetPosition)
    {
        Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;

        Vector3 direction = targetPosition - head.position;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();
        if (!headLookDirectionInitialized)
        {
            smoothedHeadLookDirection = direction;
            headLookDirectionInitialized = true;
        }

        float blend = 1f - Mathf.Exp(-headLookSpeed * Time.deltaTime);
        smoothedHeadLookDirection = Vector3.Slerp(smoothedHeadLookDirection, direction, blend).normalized;
        // Use the serialized model-axis correction and fully replace the clip's head pitch.
        // The current attacker plays an animation that pitches the head down; applying only
        // a root-forward delta leaves that pitch intact and makes the ent stare at the floor.
        if (!headAxisCorrectionInitialized)
            InitializeHeadAxisCorrection();
        head.rotation = Quaternion.LookRotation(smoothedHeadLookDirection, Vector3.up)
                        * headAxisCorrection;
    }

    private void InitializeHeadAxisCorrection()
    {
        Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        headAxisCorrection = headRotOffset.sqrMagnitude > 0.0001f
            ? Quaternion.Euler(headRotOffset)
            : Quaternion.Inverse(Quaternion.LookRotation(transform.forward, Vector3.up)) * head.rotation;
        headAxisCorrectionInitialized = true;
    }

    public void ReceiveSwordHit(PlayerWorker attacker, Collider weaponCollider, Collider bodyCollider,
        Vector3 hitPoint, int sourceAttackSequence)
    {
        hitPoint = SwordHitbox.ResolveSurfaceContact(weaponCollider, bodyCollider, hitPoint);
        if (attacker != null && attacker.IsAttackHitActive && PlayerWorker.player != null
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
        {
            bool blocked = guard == 1
                           && CombatDirectionUtility.CanBlock(attacker.AttackDirection, GuardDirection);
            if (!blocked)
            {
                if (health > 0)
                {
                    health--;
                    Vector3 hitpoint = Quaternion.Euler(new Vector3(0, -transform.rotation.eulerAngles.y, 0)) * (hitPoint - (transform.position + Vector3.up));
                    anim.SetFloat("cx", Mathf.Clamp(hitpoint.x, -1, 1));
                    anim.SetFloat("cy", Mathf.Clamp(hitpoint.y, 0, 1));
                    anim.CrossFade("Hurted", 0f, 0);
                    Vector3 impactDirection = transform.position - PlayerWorker.player.position;
                    impactDirection.y = 0f;
                    ApplyCombatKnockback(impactDirection);
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                }
                else
                {
                    if (guard == 1)
                    {
                        guard = 0;
                        anim.CrossFade("Idle", 0f, 0);
                        anim.CrossFade("Idle", 0f, 1);
                        //sword.localPosition = swordOffset;
                    }
                    health = -1;
                    anim.enabled = false;
                    ReleaseSlot();
                    if (nav != null)
                        nav.enabled = false;
                    if (characterController != null)
                        characterController.enabled = false;
                    rig.weight = 0;
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    Destroy(legAnim);
                    Destroy(GetComponent<Collider>());
                    Destroy(rigid);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                    gameObject.tag = "Untagged";
                    Destroy(gameObject, 5f);
                    Invoke("Dissolve", 4);
                    foreach (Collider collider in GetComponentsInChildren<Collider>())
                    {
                        collider.gameObject.layer = 2;
                        collider.isTrigger = false;
                        collider.enabled = true;
                        if (!collider.gameObject.GetComponent<Rigidbody>())
                        {
                            Rigidbody rrigid = collider.gameObject.AddComponent<Rigidbody>();
                            rrigid.linearVelocity = Vector3.zero;
                            rrigid.angularVelocity = Vector3.zero;
                            Vector3 ragdollImpulse = attacker.transform.forward * 3f;
                            rrigid.AddForceAtPosition(ragdollImpulse, hitPoint, ForceMode.Impulse);
                            ConfigurableJoint joint = collider.gameObject.AddComponent<ConfigurableJoint>();
                            if (collider.gameObject.name != "Hips")
                            {
                                joint.xMotion = ConfigurableJointMotion.Locked;
                                joint.yMotion = ConfigurableJointMotion.Locked;
                                joint.zMotion = ConfigurableJointMotion.Locked;
                                joint.angularXMotion = ConfigurableJointMotion.Limited;
                                joint.angularYMotion = ConfigurableJointMotion.Limited;
                                joint.angularZMotion = ConfigurableJointMotion.Limited;
                                JointDrive drive = new JointDrive();
                                drive.positionSpring = 1000;
                                drive.positionDamper = 1000;
                                joint.angularXDrive = drive;
                                joint.angularYZDrive = drive;
                                var limit0 = joint.lowAngularXLimit;
                                limit0.limit = -60;
                                joint.lowAngularXLimit = limit0;
                                var limit = joint.highAngularXLimit;
                                limit.limit = 60;
                                joint.highAngularXLimit = limit;
                                var limit1 = joint.angularYLimit;
                                limit1.limit = 60;
                                joint.angularYLimit = limit1;
                                var limit2 = joint.angularYLimit;
                                limit2.limit = 60;
                                joint.angularZLimit = limit2;
                                Transform rigidParent = joint.transform.parent;
                                for (int i = 0; i < 5; i++)
                                {
                                    if (rigidParent.GetComponent<Rigidbody>())
                                    {
                                        joint.connectedBody = rigidParent.GetComponent<Rigidbody>();
                                        break;
                                    }
                                    else
                                    {
                                        rigidParent = rigidParent.transform.parent;
                                    }
                                }
                            }
                        }
                    }

                }
            }
            else
            {
                // A successful guard uses the same contact blast as a hit for now,
                // but rebounds the attacker rather than moving the defender.
                Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                Vector3 reboundDirection = attacker.transform.position - transform.position;
                reboundDirection.y = 0f;
                if (reboundDirection.sqrMagnitude < 0.0001f)
                    reboundDirection = -transform.forward;
                attacker.ApplyCombatKnockback(reboundDirection);
                if (!playerAnim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
                    playerAnim.CrossFade("Guarded", 0f, 0);
            }
        }
    }

    private void Dissolve()
    {
        Material dissolve = Resources.Load<Material>($"Material/Shader Graphs_Dissolve_Dissolve_Metallic");
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.gameObject.AddComponent<DissolveSphere>();
            Material[] mat = renderer.materials;
            for (int i = 0; i < renderer.materials.Length; i++)
                mat[i] = dissolve;
            renderer.materials = mat;
        }
        foreach (Renderer renderer in anim.GetBoneTransform(HumanBodyBones.Hips).GetComponentsInChildren<Renderer>())
        {
            renderer.gameObject.AddComponent<DissolveSphere>();
            Material[] mat = renderer.materials;
            for (int i = 0; i < renderer.materials.Length; i++)
                mat[i] = dissolve;
            renderer.materials = mat;
        }
    }
}   
