using FIMSpace.FProceduralAnimation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(-1000)]
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
    private int cool = 100;
    private int cool1 = 100;
    private float guardTime = -1;
    private int swing, guard;
    private GameObject hitVFX, shieldVFX;
    private AudioSource audioSource;
    private Rigidbody rigid;
    private Rig rig;
    private LegsAnimator legAnim;
    [SerializeField] private MultiAimConstraint ma;
    [SerializeField] private Vector3 headRotOffset;
    [SerializeField] private int guardRate = 20;
    [SerializeField] private int health = 2;
    [SerializeField] private float movementSpeed = 5f;
    [Header("NavMesh pursuit")]
    [SerializeField] private float engagementRadius = 4.5f;
    [SerializeField] private float additionalRingSpacing = 2.5f;
    [SerializeField] private float slotSampleRadius = 2.5f;
    [SerializeField] private float destinationRefreshInterval = 0.12f;
    private float destinationRefreshTimer;
    [SerializeField, Min(0.1f)] private float physicsReactionMinTime = 0.35f;
    [SerializeField, Min(0.2f)] private float physicsReactionMaxTime = 1.5f;
    [SerializeField, Min(0.05f)] private float physicsSettleSpeed = 0.5f;
    private bool physicsReaction;
    private float physicsReactionTimer;
    private RigBuilder rb;
    [SerializeField] private bool miiMotion;
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
        anim = GetComponent<Animator>();
        if (anim != null)
            anim.applyRootMotion = false;
        ConfigurePhysicalRoot();
        RebindHeadConstraints();
    }

    private void ConfigurePhysicalRoot()
    {
        ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
        if (joint == null)
            return;

        joint.xMotion = ConfigurableJointMotion.Free;
        joint.yMotion = ConfigurableJointMotion.Free;
        joint.zMotion = ConfigurableJointMotion.Free;
        if (joint.connectedBody != null)
        {
            joint.connectedBody.useGravity = false;
            joint.connectedBody.isKinematic = true;
        }
    }

    private void RebindHeadConstraints()
    {
        if (anim == null || ma == null)
            return;

        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
            return;

        MultiAimConstraintData aimData = ma.data;
        aimData.constrainedObject = head;
        ma.data = aimData;

        // HeadLayer에는 시선용 MultiAim과 위치 추종용 MultiParent가 같이 있다.
        // 복제된 적에서 두 constraint가 다른 캐릭터의 Head를 참조하면
        // RigBuilder 전체 빌드가 실패해 무기 블렌드와 양손 IK도 모두 멈춘다.
        MultiParentConstraint headParent = ma.GetComponent<MultiParentConstraint>();
        if (headParent != null)
        {
            MultiParentConstraintData parentData = headParent.data;
            parentData.constrainedObject = head;
            headParent.data = parentData;
        }
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
        hitVFX = Resources.Load<GameObject>($"Prefab/HitVFX");
        shieldVFX = Resources.Load<GameObject>($"Prefab/ShieldVFX");
        audioSource = GetComponent<AudioSource>();
        playerAnim = PlayerWorker.playerAnimator != null
            ? PlayerWorker.playerAnimator
            : PlayerWorker.player.GetComponent<Animator>();
        rigid = GetComponent<Rigidbody>();
        if (nav != null)
        {
            nav.speed = movementSpeed;
            nav.acceleration = Mathf.Max(12f, movementSpeed * 4f);
            nav.angularSpeed = 0f;
            nav.updateRotation = false;
            nav.autoBraking = true;
            nav.autoRepath = true;
            nav.autoTraverseOffMeshLink = true;
            nav.stoppingDistance = 0.25f;
            nav.avoidancePriority = 20 + GetAssignedSlot() % 70;
            PlaceAgentOnNavMesh();
        }
        if (rigid != null && nav != null)
        {
            rigid.isKinematic = true;
            rigid.useGravity = false;
            rigid.constraints = RigidbodyConstraints.None;
            rigid.interpolation = RigidbodyInterpolation.Interpolate;
        }
        rig = GetComponentInChildren<Rig>();
        legAnim = GetComponent<LegsAnimator>();
        rb = GetComponent<RigBuilder>();
        var data = ma.data.sourceObjects;
        data.SetTransform(0, playerAnim.GetBoneTransform(HumanBodyBones.Head));
        ma.data.sourceObjects = data;
        rb.Build();
        rightTemp = new GameObject("RightTemp");
        leftTemp = new GameObject("LeftTemp");
    }

    private NavMeshAgent ResolveRootAgent()
    {
        NavMeshAgent rootAgent = GetComponent<NavMeshAgent>();
        NavMeshAgent[] allAgents = GetComponentsInChildren<NavMeshAgent>(true);
        NavMeshAgent source = rootAgent;
        foreach (NavMeshAgent candidate in allAgents)
        {
            if (candidate.gameObject != gameObject && source == null)
                source = candidate;
        }

        if (rootAgent == null && source != null)
        {
            bool sourceWasEnabled = source.enabled;
            source.enabled = false;
            rootAgent = gameObject.AddComponent<NavMeshAgent>();
            rootAgent.agentTypeID = source.agentTypeID;
            rootAgent.radius = source.radius;
            rootAgent.height = source.height;
            rootAgent.baseOffset = source.baseOffset;
            rootAgent.areaMask = source.areaMask;
            rootAgent.obstacleAvoidanceType = source.obstacleAvoidanceType;
            rootAgent.avoidancePriority = source.avoidancePriority;
            rootAgent.enabled = sourceWasEnabled;
        }

        foreach (NavMeshAgent candidate in allAgents)
        {
            if (candidate != rootAgent)
                candidate.enabled = false;
        }

        return rootAgent;
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

        // 무기 트리거 on/off
        if (PlayerWorker.ent && PlayerWorker.ent == gameObject)
        {
            var t = GetComponentInChildren<TargetMatching>().transform;
            t.GetComponent<Collider>().isTrigger = false;
        }
        else
        {
            GetComponentInChildren<TargetMatching>().GetComponent<Collider>().isTrigger = true;
        }

        // 충돌 무시 토글
        if (PlayerWorker.ent && PlayerWorker.ent.transform == transform)
        {
            bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
            bool inTransition = anim.IsInTransition(0);
            if (!inAttack || (inAttack && inTransition))
                Physics.IgnoreLayerCollision(6, 9, true);
        }

        // 플레이어 방향 회전(공격 중/스턴 아닐 때)
        if (!physicsReaction && PlayerWorker.player != null && swing == 0 && health > -1)
        {
            Vector3 toPlayer = PlayerWorker.player.position - transform.position;
            float yaw = Quaternion.LookRotation(toPlayer).eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ma 소스 재지정(플레이어 없을 때)
        if (PlayerWorker.player == null && ma.data.sourceObjects[0].transform != crosshead)
        {
            var data = ma.data.sourceObjects;
            data.SetTransform(0, crosshead);
            ma.data.sourceObjects = data;
            rb.Build();
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
                    anim.CrossFade("Attack", 0f, 0);
                    anim.CrossFade("Attack", 0f, 1);
                    ma.weight = 0f;
                    swing = 1;

                    // 반대 방향으로 목표 갱신
                    cool1Timer = cool1Frames / refFps;
                    rtx = Mathf.Clamp(-tx, -180f, 180f);
                    rty = Mathf.Clamp(-ty, -30f, 150f);

                    Physics.IgnoreLayerCollision(6, 9, false);
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
                ma.weight = 1f;
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
        if (health > -1)
        {
            if (anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
                rig.weight = 0f;
            else
                rig.weight = 1f;

            legAnim.UseGluing = !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        }
        if (miiMotion) return;

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
        Vector3 radial = Quaternion.Euler(0f, angle, 0f) * PlayerWorker.player.forward;
        return PlayerWorker.player.position + radial * radius;
    }

    private void PlaceAgentOnNavMesh()
    {
        if (nav == null || nav.isOnNavMesh)
            return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, nav.areaMask))
            nav.Warp(hit.position);
    }

    private void UpdateNavigation(float deltaTime)
    {
        bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        if (health < 0 || nav == null || PlayerWorker.player == null)
            return;

        if (physicsReaction)
        {
            UpdatePhysicsRecovery(deltaTime);
            return;
        }

        PlaceAgentOnNavMesh();
        if (!nav.isOnNavMesh)
        {
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
            return;
        }

        if (inAttack)
        {
            nav.isStopped = true;
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
            return;
        }

        nav.isStopped = false;
        destinationRefreshTimer -= deltaTime;
        if (destinationRefreshTimer <= 0f)
        {
            destinationRefreshTimer = destinationRefreshInterval;
            int slot = GetAssignedSlot();
            Vector3 requestedSlot = CalculateSlotPosition(slot);

            if (!TrySetReachableDestination(requestedSlot, slotSampleRadius))
                TrySetReachableDestination(PlayerWorker.player.position, slotSampleRadius * 2f);
        }

        Vector3 localVelocity = transform.InverseTransformDirection(nav.desiredVelocity);
        float speed = Mathf.Max(0.01f, nav.speed);
        anim.SetFloat("mx", Mathf.Clamp(localVelocity.x / speed, -1f, 1f));
        anim.SetFloat("mz", Mathf.Clamp(localVelocity.z / speed, -1f, 1f));

        if (!nav.pathPending && nav.hasPath
                && nav.remainingDistance <= nav.stoppingDistance + 0.15f)
        {
            nav.isStopped = true;
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
        }
    }

    private bool TrySetReachableDestination(Vector3 requestedPosition, float sampleRadius)
    {
        if (!NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, sampleRadius, nav.areaMask))
            return false;

        NavMeshPath path = new();
        if (!NavMesh.CalculatePath(nav.nextPosition, hit.position, nav.areaMask, path)
                || path.status != NavMeshPathStatus.PathComplete)
            return false;

        return nav.SetPath(path);
    }

    public void ApplyCombatKnockback(Vector3 displacement)
    {
        BeginPhysicsReaction(Vector3.ClampMagnitude(displacement, 3f));
    }

    private void BeginPhysicsReaction(Vector3 velocityChange)
    {
        if (rigid == null || health < 0)
            return;

        if (!physicsReaction)
        {
            if (nav != null && nav.enabled)
            {
                if (nav.isOnNavMesh)
                    nav.ResetPath();
                nav.enabled = false;
            }
            rigid.isKinematic = false;
            rigid.useGravity = true;
            physicsReaction = true;
            physicsReactionTimer = 0f;
        }

        rigid.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    private void UpdatePhysicsRecovery(float deltaTime)
    {
        physicsReactionTimer += deltaTime;
        anim.SetFloat("mx", 0f);
        anim.SetFloat("mz", 0f);

        bool minimumElapsed = physicsReactionTimer >= physicsReactionMinTime;
        bool settled = rigid.linearVelocity.sqrMagnitude <= physicsSettleSpeed * physicsSettleSpeed;
        bool timeout = physicsReactionTimer >= physicsReactionMaxTime;
        if (!minimumElapsed || (!settled && !timeout))
            return;

        float sampleRadius = timeout ? 5f : 2f;
        if (!NavMesh.SamplePosition(rigid.position, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            return;

        rigid.linearVelocity = Vector3.zero;
        rigid.angularVelocity = Vector3.zero;
        rigid.position = hit.position;
        rigid.isKinematic = true;
        rigid.useGravity = false;
        nav.enabled = true;
        nav.Warp(hit.position);
        physicsReaction = false;
        destinationRefreshTimer = 0f;
    }


    private void LateUpdate()
    {
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && (!playerAnim || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack")))
        {
            anim.GetBoneTransform(HumanBodyBones.Head).rotation = Quaternion.Euler(headRotOffset + new Vector3(0, transform.rotation.eulerAngles.y, 0));
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

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 8 && PlayerWorker.player != null && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
        {
            PlayerWorker attacker = PlayerWorker.player.GetComponent<PlayerWorker>();
            bool blocked = guard == 1
                           && attacker != null
                           && CombatDirectionUtility.CanBlock(attacker.AttackDirection, GuardDirection);
            if (!blocked)
            {
                if (health > 0)
                {
                    health--;
                    Vector3 hitpoint = Quaternion.Euler(new Vector3(0, -transform.rotation.eulerAngles.y, 0)) * (collision.contacts[0].point - (transform.position + Vector3.up));
                    anim.SetFloat("cx", Mathf.Clamp(hitpoint.x, -1, 1));
                    anim.SetFloat("cy", Mathf.Clamp(hitpoint.y, 0, 1));
                    anim.CrossFade("Hurted", 0, 0);
                    Vector3 impactDirection = transform.position - PlayerWorker.player.position;
                    impactDirection.y = 0.2f;
                    BeginPhysicsReaction(impactDirection.normalized * 1.5f);
                    GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                    Destroy(hit, 0.3f);
                    SoundManager.Instance.SFXPlay("Armor Impact 1");
                }
                else
                {
                    if (guard == 1)
                    {
                        guard = 0;
                        anim.CrossFade("Idle", 0, 0);
                        anim.CrossFade("Idle", 0, 1);
                        //sword.localPosition = swordOffset;
                    }
                    health = -1;
                    anim.enabled = false;
                    ReleaseSlot();
                    if (nav != null)
                        nav.enabled = false;
                    rig.weight = 0;
                    GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                    Destroy(hit, 0.3f);
                    Destroy(legAnim);
                    Destroy(GetComponent<ConfigurableJoint>());
                    Destroy(GetComponent<Collider>());
                    Destroy(rigid);
                    SoundManager.Instance.SFXPlay("Armor Impact 1");
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
                            rrigid.AddForceAtPosition(collision.gameObject.GetComponent<Rigidbody>().linearVelocity, collision.contacts[0].point, ForceMode.Impulse);
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
                GameObject shield = Instantiate(shieldVFX, collision.contacts[0].point, Quaternion.LookRotation(transform.forward));
                Destroy(shield, 0.3f);
                SoundManager.Instance.SFXPlay("Shield Impact 2");
                attacker.ApplyCombatKnockback(-PlayerWorker.player.forward * 0.75f);
                if (!playerAnim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
                    playerAnim.CrossFade("Guarded", 0, 0);
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
