using FIMSpace.FProceduralAnimation;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(CharacterController))]
public class EnemyWorker : MonoBehaviour
{
    private static readonly List<EnemyWorker> ActiveEnemies = new();
    private static readonly Dictionary<EnemyWorker, int> SlotAssignments = new();
    private const int FormationSlotCount = 5;
    private static float nextSlotAssignmentTime;
    private static bool slotAssignmentsDirty;
    private static bool focusTransparencyActive;

    private Animator anim;
    private NavMeshAgent nav;
    private Animator playerAnim;
    private PlayerWorker playerWorker;
    [SerializeField] private Transform crosshead;
    public static Transform psword;
    [SerializeField] private Transform sword;
    [SerializeField] private Vector3 swordOffset;
    private float sensitivity = 0.01f;
    private float tx, ty, rtx, rty;
    private Vector2 attackDirection;
    private int attackSequence;
    private int interruptedAttackSequence = -1;
    [Header("Attack hit detection")]
    [SerializeField, Range(0f, 1f)] private float attackHitStart = 0f;
    [SerializeField, Range(0f, 1.2f)] private float attackHitEnd = 1.05f;
    [Header("Sword slash VFX")]
    [Tooltip("Length of the motion-synced slash ribbon, measured in blade lengths.")]
    [SerializeField, Min(0.25f)] private float swordSlashTravelInBladeLengths = 2.25f;
    [Tooltip("Time Based fades while the sword is still. Motion Distance freezes with the animation.")]
    [SerializeField] private Swordplay.Visuals.SwordSlashTrailAgingMode swordSlashAgingMode =
        Swordplay.Visuals.SwordSlashTrailAgingMode.MotionDistance;
    [SerializeField, Min(0.02f)] private float swordSlashTimeBasedLifetime = 0.25f;
    [SerializeField, Range(0.25f, 1.5f)] private float swordSlashBladeWidth = 1f;
    [SerializeField, Range(0.1f, 2f)] private float swordSlashIntensity = 1.1f;
    [SerializeField, Range(0f, 1f)] private float swordSlashTextureFlow = 0.16f;
    [SerializeField] private bool swordSlashUseWeaponEffectColor = true;
    [SerializeField, ColorUsage(true, true)] private Color swordSlashColor = new(1.2f, 0.35f, 0.08f, 0.9f);
    [SerializeField] private Material swordSlashMaterialOverride;
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
    [Header("Difficulty scaling")]
    [SerializeField, Range(0f, 100f)] private float startingGuardRate = 50f;
    [SerializeField, Range(0f, 100f)] private float minimumGuardRate = 10f;
    [Tooltip("Current guard chance is multiplied by this percentage after every defeat.")]
    [SerializeField, Range(0.5f, 0.99f)] private float guardRateMultiplierPerDefeat = 0.94f;
    [Tooltip("Per-reference-frame pose interpolation before difficulty scaling.")]
    [SerializeField, Range(0.001f, 0.5f)] private float basePoseLerp = 0.01f;
    [SerializeField, Range(0.001f, 0.5f)] private float baseAttackPoseLerp = 0.1f;
    [Tooltip("Pose interpolation speed is multiplied by this after every defeat.")]
    [SerializeField, Range(1.01f, 2f)] private float poseLerpMultiplierPerDefeat = 1.06f;
    [SerializeField, Range(0.001f, 0.5f)] private float minimumPoseLerp = 0.01f;
    [SerializeField, Range(0.01f, 0.5f)] private float maximumPoseLerp = 0.5f;
    [SerializeField] private int health = 2;
    private int startingHealth;
    [Header("Attack reaction window")]
    [SerializeField, Range(0f, 0.5f)] private float attackSlowTrigger = 0.1f;
    [SerializeField, Range(0.001f, 0.2f)] private float attackSlowSpeed = 0.01f;
    [SerializeField, Min(0.1f)] private float attackSlowDuration = 1f;
    [SerializeField, Min(0.1f)] private float attackSpeedRestoreSharpness = 8f;
    private bool attackSlowTriggered;
    private bool restoringAttackSpeed;
    private float attackSlowTimer;
    private bool returnToIdleAfterHurt;
    private bool hurtStateEntered;
    private SwordHitbox swordHitbox;
    private int resolvedPlayerAttackSequence = -1;
    [SerializeField] private float movementSpeed = 5f;
    [Header("NavMesh pursuit")]
    [SerializeField] private float engagementRadius = 4.5f;
    [SerializeField] private float slotSampleRadius = 2.5f;
    [SerializeField] private float destinationRefreshInterval = 0.12f;
    [Header("Formation route around player")]
    [Tooltip("Enemies first join this outer ring, travel clockwise, then enter their slot radially.")]
    [SerializeField, Min(0.75f)] private float formationRouteClearance = 1.8f;
    [SerializeField, Range(8f, 45f)] private float formationArcStepDegrees = 24f;
    [SerializeField, Range(0.2f, 1.5f)] private float formationWaypointTolerance = 0.55f;
    [Tooltip("Temporary spacing between clockwise/counter-clockwise travel lanes. This does not create extra combat slots.")]
    [SerializeField, Range(0.25f, 1.5f)] private float formationOppositeLaneSeparation = 0.8f;
    private float destinationRefreshTimer;
    private Vector3 directPursuitTarget;
    private bool useDirectPursuit;
    private Vector3 previousNavPosition;
    private float navStuckTimer;
    private float formationPathFailureTimer;
    private float offNavMeshRecoveryTimer;
    private Vector3 lastRequestedSlot;
    private bool hasRequestedSlot;
    private NavMeshPath slotPathProbe;
    private Vector3 reachableSlotPosition;
    private int reachableSlotIndex = -1;
    private bool hasReachableSlotPosition;
    [Header("Formation arrival state")]
    [SerializeField, Range(0.35f, 1.5f)] private float slotArrivalTolerance = 0.8f;
    [SerializeField, Range(0.5f, 2.5f)] private float slotDepartureTolerance = 1.35f;
    private int arrivalTrackedSlot = -1;
    private bool slotArrivalLatched;
    private enum FormationRouteStage { ExitToOuterRing, OrbitOuterRing, EnterAssignedSlot, Arrived }
    private FormationRouteStage formationRouteStage = FormationRouteStage.ExitToOuterRing;
    private int routedSlot = -1;
    private int formationOrbitDirection = 1;
    [Header("Focused slot recovery")]
    [Tooltip("If the focused enemy cannot recover its slot after this time, focus passes to an adjacent occupied slot.")]
    [SerializeField, Min(1f)] private float focusedSlotRecoveryTimeout = 5f;
    [Tooltip("Lower NavMesh avoidance values have higher priority.")]
    [SerializeField, Range(0, 99)] private int focusedRecoveryAvoidancePriority = 5;
    [SerializeField, Min(0.25f)] private float navMeshRecoverySampleRadius = 1.5f;
    private float focusedSlotRecoveryTimer;
    private int authoredAvoidancePriority = 50;
    private bool avoidancePriorityCaptured;
    [SerializeField, Min(0.2f)] private float knockbackMaxTime = 1.5f;
    [SerializeField, Min(0.01f)] private float knockbackStopSpeed = 0.15f;
    [SerializeField, Min(0.5f)] private float knockbackDistance = 5f;
    [SerializeField, Min(0.1f)] private float facingLerpSpeed = 10f;
    [SerializeField, Min(0.1f)] private float knockbackSlideDamping = 6f;
    private bool physicsReaction;
    private float physicsReactionTimer;
    private float combatReactionLockTimer;
    private const float CombatReactionWatchdogSeconds = 4f;
    private Vector3 knockbackStartPosition;
    private Vector3 knockbackDirection;
    private RigBuilder rb;
    [SerializeField] private bool miiMotion;
    private bool usesDirectHandSocketWeapon;
    private sealed class FadedRendererState
    {
        public Material[] OriginalMaterials;
        public Material[] RuntimeMaterials;
    }
    private readonly Dictionary<Renderer, FadedRendererState> fadedRenderers = new();
    private bool visualsDimmed;
    private float fadedRendererRefreshTimer;
    // ------- 프레임 독립용: 초 단위 타이머로 변환 -------
    [Header("Frame→Seconds 변환(참고 FPS=60 기준)")]
    [SerializeField] float refFps = 60f;

    [SerializeField] int cool1Frames = 100;            // 기존: 100
    [SerializeField] Vector2Int coolFramesRange = new(100, 500);   // 기존: 100~500
    [SerializeField] Vector2Int guardFramesRange = new(500, 1000); // 기존: 500~1000
    [SerializeField, Min(0.05f)] float minimumActionCooldown = 0.25f;
    [SerializeField, Min(1f)] float minimumGuardDuration = 1f;

    float cool1Timer, coolTimer, guardTimer; // seconds

    GameObject rightTemp, leftTemp;

    [Header("Rig refs")]
    [SerializeField] MultiParentConstraint weaponBlend; // Rig_Weapon에 붙은 컴포넌트
    [SerializeField] TwoBoneIKConstraint rightArmIK;    // Rig_Hands
    [SerializeField] TwoBoneIKConstraint leftArmIK;     // Rig_Hands

    public Vector2 AttackDirection => attackDirection;
    public Vector2 GuardDirection => new(tx, ty);
    public Transform SwordTransform => sword;
    public int AttackSequence => attackSequence;
    public bool IsAttackHitActive => health > -1 && attackSequence > 0
        && attackSequence != interruptedAttackSequence && attackSlowTriggered
        && CombatDirectionUtility.IsAttackStateActive(anim, attackHitStart, attackHitEnd);
    public bool IsAlive => health > -1 && gameObject.activeInHierarchy;
    public bool HasReachedFormationSlot => IsAlive && HasReachedAssignedSlot();
    public bool IsAvailableForCombatFocus => HasReachedFormationSlot && !IsCombatReactionLocked();

    public bool IsPerformingAttackSequence(int sequence)
    {
        if (health < 0 || sequence <= 0 || attackSequence != sequence || anim == null)
            return false;
        if (swing == 1 || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
            return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("Attack");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEnemySlots()
    {
        ActiveEnemies.Clear();
        SlotAssignments.Clear();
        nextSlotAssignmentTime = 0f;
        slotAssignmentsDirty = true;
        focusTransparencyActive = false;
    }

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
            ActiveEnemies.Add(this);
        slotAssignmentsDirty = true;
        nextSlotAssignmentTime = 0f;
        ResetFormationRoute();
        arrivalTrackedSlot = -1;
        slotArrivalLatched = false;
        focusedSlotRecoveryTimer = 0f;
        formationPathFailureTimer = 0f;
        offNavMeshRecoveryTimer = 0f;
        if (focusTransparencyActive)
            SetVisualDimmed(PlayerWorker.ent == null
                            || PlayerWorker.ent != gameObject
                            || !HasReachedAssignedSlot());
    }

    private void OnDisable()
    {
        RestoreAuthoredAvoidancePriority();
        SetSwordDamageTriggersEnabled(true);
        ResetTransientCombatState();
        RestoreOriginalVisualMaterials();
        ActiveEnemies.Remove(this);
        SlotAssignments.Remove(this);
        slotAssignmentsDirty = true;
        nextSlotAssignmentTime = 0f;
    }

    private void OnApplicationQuit()
    {
        RestoreOriginalVisualMaterials();
    }

    private void Awake()
    {
        startingHealth = health;
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
        cool1Timer = GetPoseRetargetDelay();
        coolTimer = 0f;
        guardTimer = -1f;

        nav = ResolveRootAgent();
        if (nav != null)
        {
            authoredAvoidancePriority = nav.avoidancePriority;
            avoidancePriorityCaptured = true;
        }
        hitVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeHitVFX");
        shieldVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeGuardVFX");
        audioSource = GetComponent<AudioSource>();
        playerAnim = PlayerWorker.playerAnimator != null
            ? PlayerWorker.playerAnimator
            : PlayerWorker.player != null ? PlayerWorker.player.GetComponent<Animator>() : null;
        playerWorker = PlayerWorker.player != null
            ? PlayerWorker.player.GetComponent<PlayerWorker>()
            : null;
        rigid = GetComponent<Rigidbody>();
        if (rigid == null) rigid = gameObject.AddComponent<Rigidbody>();
        movementCapsule = GetComponent<CapsuleCollider>();
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
            characterController = gameObject.AddComponent<CharacterController>();
        ConfigureCharacterController();
        if (nav != null)
            previousNavPosition = transform.position;
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
            swordHitbox = SwordHitbox.Configure(weaponTarget.gameObject, this);
        }
        Transform slashSword = sword != null ? sword : psword;
        if (slashSword != null)
            Swordplay.Visuals.SwordSlashVfxSynchronizer.Configure(
                slashSword, anim, swordSlashTravelInBladeLengths,
                swordSlashAgingMode, swordSlashTimeBasedLifetime,
                swordSlashBladeWidth, swordSlashIntensity, swordSlashTextureFlow,
                swordSlashUseWeaponEffectColor, swordSlashColor, swordSlashMaterialOverride);
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
        if (visualsDimmed)
        {
            fadedRendererRefreshTimer -= Time.unscaledDeltaTime;
            if (fadedRendererRefreshTimer <= 0f)
            {
                fadedRendererRefreshTimer = 0.5f;
                FadeNewChildRenderers();
            }
        }
        UpdateAttackSlowdown();
        UpdateHurtRecovery();
        UpdateCombatReactionWatchdog();

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
        UpdateFocusedSlotRecoveryWatchdog();

        // The focused enemy only becomes fully opaque after actually reaching its assigned
        // formation position. If it is displaced again, dim it until the slot is recovered.
        if (focusTransparencyActive)
        {
            bool reactionLocked = IsCombatReactionLocked();
            bool shouldDim = PlayerWorker.ent == null
                             || PlayerWorker.ent != gameObject
                             || !HasReachedAssignedSlot()
                             || reactionLocked;
            SetVisualDimmed(shouldDim);
        }

        if (playerWorker == null && PlayerWorker.player != null)
            playerWorker = PlayerWorker.player.GetComponent<PlayerWorker>();
        bool playerLocksActionStart = playerWorker != null
                                      && playerWorker.LocksEnemyActionStart;
        bool poseLockedByPlayerAttack = playerWorker != null
                                        && (playerWorker.IsHoldingAttackSlowOn(this)
                                            || (guard == 1 && playerLocksActionStart));

        // A guarding enemy holds its current blade pose for the whole player attack.
        // Keeping rtx/rty intact lets ordinary pose tracking resume after the exchange.
        if (!poseLockedByPlayerAttack)
        {
            // -------- cool1: 조준 보정 타이머(초) --------
            if (cool1Timer > 0f)
            {
                cool1Timer = Mathf.Max(0f, cool1Timer - dt);

                bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
                float a = GetPoseLerpFactor(inAttack, dt);

                tx = Mathf.Lerp(tx, rtx, a);
                ty = Mathf.Lerp(ty, rty, a);
            }
            if (cool1Timer <= 0f)
            {
                // 주기 리셋
                cool1Timer = GetPoseRetargetDelay();
                // 준비 자세와 가드 자세 모두 같은 방식으로 새 목표 방향을 선택한다.
                rtx = Random.Range(-180f, 180f);
                rty = Random.Range(-30f, 150f);
            }
        }

        // -------- cool: 공격 트리거 타이머(초) --------
        if (coolTimer > 0f) coolTimer = Mathf.Max(0f, coolTimer - dt);

        if (coolTimer <= 0f
            && guard == 0
            && guardTimer <= 0f
            && health > -1
            && !playerLocksActionStart
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            coolTimer = -1f;
            int r = Random.Range(0, 100);
            float currentGuardRate = GetCurrentGuardRate();

            bool badState = anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted")
                            || anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded")
                            || health == -1
                            || PlayerWorker.player == null;

            if (r < 100f - currentGuardRate && badState)
            {
                r = -1;
                coolTimer = GetActionCooldown();
            }
            else if (r < 100f - currentGuardRate)
            {
                if (PlayerWorker.ent && PlayerWorker.ent.transform == transform
                    && HasReachedAssignedSlot())
                {
                    if (playerWorker != null && playerWorker.CanFocusedEnemyAttack(this))
                    {
                        BeginAttack();

                        // 반대 방향으로 목표 갱신
                        cool1Timer = GetPoseRetargetDelay();
                        rtx = Mathf.Clamp(-tx, -180f, 180f);
                        rty = Mathf.Clamp(-ty, -30f, 150f);
                    }
                    else
                    {
                        // Target handoff is still rotating/settling. Retry soon instead
                        // of consuming a full random attack cooldown.
                        coolTimer = 0.12f;
                    }

                }
                else
                {
                    coolTimer = GetActionCooldown();
                }
            }
            else // 가드 선택
            {
                guardTimer = GetGuardDuration();
            }
        }

        // 공격 종료 후 처리
        if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            if (swing == 1)
            {
                coolTimer = GetActionCooldown();
                swing = 0;
            }
        }

        // -------- 가드 타이머 --------
        if (guardTimer > 0f && health > -1
                && (guard != 0 || !playerLocksActionStart))
        {
            guardTimer = Mathf.Max(0f, guardTimer - dt);

            if (guard == 0)
            {
                tx = Mathf.Clamp(tx, -180f, 180f);
                ty = Mathf.Clamp(ty, -30f, 150f);
                rtx = Mathf.Clamp(rtx, -180f, 180f);
                rty = Mathf.Clamp(rty, -30f, 150f);

                guard = 1;
                anim.ResetTrigger("ExitBlock");
                anim.SetTrigger("EnterBlock");

                // 가드 포즈 즉시 보정(프레임 독립)
                //sword.localPosition = new Vector3(sword.localPosition.x + 1f, sword.localPosition.y - 2f, sword.localPosition.z);
                //sword.position += transform.forward; // 월드 전진 1m
            }
        }

        if (guardTimer == 0f)
        {
            coolTimer = GetActionCooldown();
            guardTimer = -1f;
            guard = 0;
            anim.ResetTrigger("EnterBlock");
            anim.SetTrigger("ExitBlock");
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

    private void UpdateHurtRecovery()
    {
        if (!returnToIdleAfterHurt || anim == null) return;
        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        bool currentIsHurted = current.IsName("Hurted");
        bool nextIsHurted = anim.IsInTransition(0)
                            && anim.GetNextAnimatorStateInfo(0).IsName("Hurted");

        if (!hurtStateEntered)
        {
            if (currentIsHurted || nextIsHurted)
                hurtStateEntered = true;
            return;
        }

        if (nextIsHurted || (currentIsHurted && current.normalizedTime < 1f)) return;

        guard = 0;
        swing = 0;
        anim.CrossFade("Idle", 0f, 0);
        if (anim.layerCount > 1)
            anim.CrossFade("Idle", 0f, 1);
        returnToIdleAfterHurt = false;
        hurtStateEntered = false;
    }

    private void BeginAttack()
    {
        Vector2 snappedAttack = CombatDirectionUtility.GetNearestNonCenterAttackAnchor(
            new Vector2(tx, ty));
        tx = rtx = snappedAttack.x;
        ty = rty = snappedAttack.y;
        anim.SetFloat("tx", tx);
        anim.SetFloat("ty", ty);
        attackDirection = snappedAttack;
        attackSequence++;
        attackSlowTriggered = false;
        restoringAttackSpeed = false;
        attackSlowTimer = 0f;
        SetSwordDamageTriggersEnabled(true);
        anim.speed = 1f;
        anim.ResetTrigger("EnterAttack");
        anim.SetTrigger("EnterAttack");
        swing = 1;
    }

    private void UpdateAttackSlowdown()
    {
        if (anim == null) return;

        bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        float normalizedTime = inAttack ? anim.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
        if (!inAttack && anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("Attack"))
        {
            inAttack = true;
            normalizedTime = anim.GetNextAnimatorStateInfo(0).normalizedTime;
        }

        if (!attackSlowTriggered && inAttack && normalizedTime >= attackSlowTrigger)
        {
            attackSlowTriggered = true;
            attackSlowTimer = attackSlowDuration;
            anim.speed = attackSlowSpeed;
            SetSwordDamageTriggersEnabled(false);
            PlayerWorker.player?.GetComponent<PlayerWorker>()?.BeginTelegraphedBlock(
                this, attackDirection, attackSequence, attackSlowDuration);
        }

        if (!attackSlowTriggered) return;
        if (!inAttack)
        {
            anim.speed = 1f;
            SetSwordDamageTriggersEnabled(true);
            attackSlowTriggered = false;
            restoringAttackSpeed = false;
            attackSlowTimer = 0f;
            return;
        }

        if (attackSlowTimer > 0f)
        {
            attackSlowTimer = Mathf.Max(0f, attackSlowTimer - Time.unscaledDeltaTime);
            anim.speed = attackSlowSpeed;
            if (attackSlowTimer <= 0f)
            {
                PlayerWorker.player?.GetComponent<PlayerWorker>()?.ResolveEnemyAttackAtSlowEnd(
                    this, attackSequence);
                restoringAttackSpeed = true;
                SetSwordDamageTriggersEnabled(true);
            }
            return;
        }

        if (!restoringAttackSpeed) return;
        float blend = 1f - Mathf.Exp(-attackSpeedRestoreSharpness * Time.unscaledDeltaTime);
        anim.speed = Mathf.Lerp(anim.speed, 1f, blend);
        if (anim.speed >= 0.995f)
        {
            anim.speed = 1f;
            restoringAttackSpeed = false;
        }
    }

    private void InterruptCurrentAttackByHit()
    {
        if (attackSequence <= 0 || !IsPerformingAttackSequence(attackSequence))
            return;

        interruptedAttackSequence = attackSequence;
        attackSlowTriggered = false;
        restoringAttackSpeed = false;
        attackSlowTimer = 0f;
        if (anim != null)
            anim.speed = 1f;
        SetSwordDamageTriggersEnabled(true);
    }


    private void SetSwordDamageTriggersEnabled(bool value)
    {
        // psword is a legacy static shared by every enemy, so it must not be used here:
        // a pooled enemy could otherwise toggle another enemy's blade. Resolve locally.
        if (swordHitbox == null)
            swordHitbox = GetComponentInChildren<SwordHitbox>(true);
        swordHitbox?.SetDamageTriggersEnabled(value);
    }

    // === 유틸 ===
    float RandomRangeFramesToSeconds(Vector2Int framesRange)
    {
        int f = Random.Range(framesRange.x, framesRange.y + 1);
        return f / refFps;
    }

    private float CurrentTempoMultiplier => ArenaGameDirector.Instance != null
        ? ArenaGameDirector.Instance.EnemyTempoMultiplier
        : 1f;

    private int CurrentDefeatCount => ArenaGameDirector.Instance != null
        ? ArenaGameDirector.Instance.DefeatedEnemiesThisRun
        : 0;

    private float GetCurrentGuardRate()
    {
        float lower = Mathf.Min(startingGuardRate, minimumGuardRate);
        float upper = Mathf.Max(startingGuardRate, minimumGuardRate);
        float scaled = startingGuardRate
                       * Mathf.Pow(guardRateMultiplierPerDefeat, CurrentDefeatCount);
        return Mathf.Clamp(scaled, lower, upper);
    }

    private float GetPoseLerpFactor(bool inAttack, float deltaTime)
    {
        float lower = Mathf.Clamp(minimumPoseLerp, 0.001f, 0.5f);
        float upper = Mathf.Clamp(maximumPoseLerp, lower, 0.5f);
        float baseLerp = inAttack ? baseAttackPoseLerp : basePoseLerp;
        float scaledReferenceLerp = baseLerp
            * Mathf.Pow(poseLerpMultiplierPerDefeat, CurrentDefeatCount);
        float referenceLerp = Mathf.Clamp(scaledReferenceLerp, lower, upper);

        // Inspector values describe the familiar per-frame Lerp percentage at Ref FPS.
        // Convert it to a frame-rate-independent factor while preserving the 0.5 cap.
        float referenceFrames = Mathf.Max(0f, deltaTime) * Mathf.Max(1f, refFps);
        return 1f - Mathf.Pow(1f - referenceLerp, referenceFrames);
    }

    private float GetPoseRetargetDelay()
        => Mathf.Max(minimumActionCooldown,
            cool1Frames / refFps * CurrentTempoMultiplier);

    private float GetActionCooldown()
        => Mathf.Max(minimumActionCooldown,
            RandomRangeFramesToSeconds(coolFramesRange) * CurrentTempoMultiplier);

    private float GetGuardDuration()
        => Mathf.Max(minimumGuardDuration,
            RandomRangeFramesToSeconds(guardFramesRange) * CurrentTempoMultiplier);

    private int GetAssignedSlot()
    {
        if (slotAssignmentsDirty || Time.time >= nextSlotAssignmentTime
                || !SlotAssignments.ContainsKey(this))
        {
            RebuildOptimalSlotAssignments();
            nextSlotAssignmentTime = Time.time + 0.75f;
        }
        if (SlotAssignments.TryGetValue(this, out int assigned))
            return assigned;

        return -1;
    }

    private bool HasReachedAssignedSlot()
    {
        int slot = GetAssignedSlot();
        if (slot < 0 || PlayerWorker.player == null)
        {
            arrivalTrackedSlot = -1;
            slotArrivalLatched = false;
            return false;
        }

        if (arrivalTrackedSlot != slot)
        {
            arrivalTrackedSlot = slot;
            slotArrivalLatched = false;
        }

        // The requested formation point can be moved by NavMesh.SamplePosition before it
        // becomes the agent's real path endpoint. Compare against that accepted endpoint;
        // comparing against the unsampled point can leave an agent permanently "not
        // arrived" even after it has completed its valid path.
        Vector3 arrivalPosition = hasReachableSlotPosition && reachableSlotIndex == slot
            ? reachableSlotPosition
            : CalculateSlotPosition(slot);
        Vector3 offset = arrivalPosition - transform.position;
        offset.y = 0f;
        float stoppingDistance = nav != null && nav.enabled
            ? nav.stoppingDistance
            : 0.5f;
        float enterTolerance = Mathf.Max(
            slotArrivalTolerance, formationWaypointTolerance, stoppingDistance + 0.35f);
        float distance = offset.magnitude;

        bool finalRouteStage = formationRouteStage == FormationRouteStage.EnterAssignedSlot
                               || formationRouteStage == FormationRouteStage.Arrived;
        bool navCompletedFinalLeg = finalRouteStage
                                    && nav != null && nav.enabled && nav.isOnNavMesh
                                    && !nav.pathPending
                                    && (!nav.hasPath
                                        || nav.remainingDistance <= stoppingDistance + 0.2f)
                                    && nav.velocity.sqrMagnitude <= 0.04f;
        // SamplePosition can move the usable endpoint away from the mathematical slot.
        // A stopped, complete final leg is valid within the same radius used to sample it.
        float sampledEndpointTolerance = Mathf.Max(enterTolerance,
            Mathf.Clamp(slotSampleRadius, 0.5f, 1.25f) + stoppingDistance);
        if (distance <= enterTolerance
                || (navCompletedFinalLeg && distance <= sampledEndpointTolerance))
            slotArrivalLatched = true;

        float leaveTolerance = Mathf.Max(enterTolerance, slotDepartureTolerance);
        if (slotArrivalLatched && distance > leaveTolerance)
            slotArrivalLatched = false;

        return slotArrivalLatched;
    }

    public static void SetFocusedEnemy(GameObject focusedEnemy)
    {
        focusTransparencyActive = focusedEnemy != null;
        foreach (EnemyWorker enemy in ActiveEnemies.ToArray())
        {
            if (enemy == null) continue;
            enemy.SetVisualDimmed(focusTransparencyActive
                                  && (enemy.gameObject != focusedEnemy
                                      || !enemy.HasReachedAssignedSlot()
                                      || enemy.IsCombatReactionLocked()));
        }
    }

    public static void RestoreAllVisuals()
    {
        focusTransparencyActive = false;
        foreach (EnemyWorker enemy in ActiveEnemies.ToArray())
            if (enemy != null) enemy.RestoreOriginalVisualMaterials();
    }

    private void SetVisualDimmed(bool value)
    {
        if (!value)
        {
            // Always repair the real renderer state. Rapid pool/focus changes can happen in
            // one frame; relying only on the cached bool can otherwise leave cloned
            // transparent materials assigned after this enemy becomes the focus.
            if (visualsDimmed || fadedRenderers.Count > 0)
                RestoreOriginalVisualMaterials();
            return;
        }

        if (visualsDimmed)
            return;

        visualsDimmed = true;
        fadedRendererRefreshTimer = 0.5f;
        FadeNewChildRenderers();
    }

    private bool IsCombatReactionLocked()
    {
        if (physicsReaction || returnToIdleAfterHurt || anim == null)
            return physicsReaction || returnToIdleAfterHurt;

        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(layer);
            if (current.IsName("Hurted") || current.IsName("Guarded"))
                return true;
            if (anim.IsInTransition(layer))
            {
                AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(layer);
                if (next.IsName("Hurted") || next.IsName("Guarded"))
                    return true;
            }
        }
        return false;
    }

    private void UpdateCombatReactionWatchdog()
    {
        if (!IsCombatReactionLocked())
        {
            combatReactionLockTimer = 0f;
            return;
        }

        combatReactionLockTimer += Time.unscaledDeltaTime;
        if (combatReactionLockTimer < CombatReactionWatchdogSeconds)
            return;

        // A reaction animation or a pooled knockback may be interrupted by a defeat and
        // target hand-off. Never allow that transient state to permanently keep the current
        // focus translucent and non-interactive.
        ResetTransientCombatState(true);
        guard = 0;
        guardTimer = -1f;
        swing = 0;
        coolTimer = GetActionCooldown();
        if (anim != null && health > -1)
        {
            anim.ResetTrigger("EnterBlock");
            anim.ResetTrigger("ExitBlock");
            anim.CrossFade("Idle", 0f, 0);
            if (anim.layerCount > 1)
                anim.CrossFade("Idle", 0f, 1);
        }
    }

    private void ResetTransientCombatState(bool restoreMovement = false)
    {
        physicsReaction = false;
        physicsReactionTimer = 0f;
        combatReactionLockTimer = 0f;
        returnToIdleAfterHurt = false;
        hurtStateEntered = false;
        attackSlowTriggered = false;
        restoringAttackSpeed = false;
        attackSlowTimer = 0f;
        verticalVelocity = 0f;
        navStuckTimer = 0f;
        formationPathFailureTimer = 0f;
        offNavMeshRecoveryTimer = 0f;
        focusedSlotRecoveryTimer = 0f;
        RestoreAuthoredAvoidancePriority();
        SetSwordDamageTriggersEnabled(true);

        if (anim != null)
            anim.speed = 1f;
        if (rigid != null)
        {
            rigid.linearVelocity = Vector3.zero;
            rigid.angularVelocity = Vector3.zero;
            rigid.linearDamping = 0f;
            rigid.angularDamping = 0f;
            rigid.useGravity = false;
            rigid.detectCollisions = false;
            rigid.isKinematic = true;
            rigid.constraints = RigidbodyConstraints.FreezeAll;
        }
        if (movementCapsule != null)
            movementCapsule.enabled = false;
        if (restoreMovement)
        {
            if (characterController != null)
                characterController.enabled = true;
            if (nav != null)
            {
                nav.enabled = true;
                nav.updatePosition = true;
                if (nav.isOnNavMesh)
                    nav.isStopped = false;
            }
            destinationRefreshTimer = 0f;
        }
    }

    private void FadeNewChildRenderers()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || fadedRenderers.ContainsKey(renderer)) continue;
            Material[] originals = renderer.sharedMaterials;
            Material[] runtimeMaterials = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                if (source == null) continue;
                Material runtime = new Material(source)
                {
                    name = source.name + " (Runtime Half Transparent)",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                ConfigureHalfTransparent(runtime);
                runtimeMaterials[i] = runtime;
            }

            fadedRenderers.Add(renderer, new FadedRendererState
            {
                OriginalMaterials = originals,
                RuntimeMaterials = runtimeMaterials
            });
            renderer.sharedMaterials = runtimeMaterials;
        }
    }

    private static void ConfigureHalfTransparent(Material material)
    {
        string[] colorProperties =
        {
            "_BaseColor", "_Color", "_TintColor", "_WarriorTintColor",
            "_GlowColor", "_CoreColor", "_OutlineColor", "_EmissionColor"
        };
        foreach (string property in colorProperties)
        {
            if (!material.HasProperty(property)) continue;
            Color color = material.GetColor(property);
            color.a *= 128f / 255f;
            material.SetColor(property, color);
        }

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private void RestoreOriginalVisualMaterials()
    {
        foreach (KeyValuePair<Renderer, FadedRendererState> pair in fadedRenderers)
        {
            if (pair.Key != null)
                pair.Key.sharedMaterials = pair.Value.OriginalMaterials;
            foreach (Material runtime in pair.Value.RuntimeMaterials)
            {
                if (runtime == null) continue;
                if (Application.isPlaying) Destroy(runtime);
                else DestroyImmediate(runtime);
            }
        }
        fadedRenderers.Clear();
        visualsDimmed = false;
        fadedRendererRefreshTimer = 0f;
    }

    // Keep the five positions around the player fixed. Existing occupants retain their
    // positions; the Hungarian pass only assigns newly enabled enemies to genuinely free
    // positions, so a defeated enemy's position is immediately reusable without creating
    // an extra position between neighbours or on another ring.
    private static void RebuildOptimalSlotAssignments()
    {
        ActiveEnemies.RemoveAll(value => value == null || !value.IsAlive);
        HashSet<EnemyWorker> activeSet = new(ActiveEnemies);
        HashSet<int> occupiedSlots = new();
        foreach (EnemyWorker enemy in SlotAssignments.Keys.ToArray())
        {
            int slot = SlotAssignments[enemy];
            if (!activeSet.Contains(enemy)
                    || slot < 0 || slot >= FormationSlotCount
                    || !occupiedSlots.Add(slot))
            {
                SlotAssignments.Remove(enemy);
            }
        }

        List<EnemyWorker> unassigned = ActiveEnemies
            .Where(enemy => !SlotAssignments.ContainsKey(enemy))
            .ToList();
        List<int> freeSlots = Enumerable.Range(0, FormationSlotCount)
            .Where(slot => !occupiedSlots.Contains(slot))
            .ToList();
        int n = Mathf.Min(unassigned.Count, freeSlots.Count);
        int m = freeSlots.Count;
        slotAssignmentsDirty = false;
        if (n == 0 || m == 0 || PlayerWorker.player == null) return;

        double[,] cost = new double[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                cost[i, j] = unassigned[i - 1].CalculateFormationRouteCost(freeSlots[j - 1]);
        double[] u = new double[n + 1], v = new double[m + 1];
        int[] p = new int[m + 1], way = new int[m + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            double[] minv = Enumerable.Repeat(double.PositiveInfinity, m + 1).ToArray();
            bool[] used = new bool[m + 1];
            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                double delta = double.PositiveInfinity;
                for (int j = 1; j <= m; j++) if (!used[j])
                {
                    double cur = cost[i0, j] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (int j = 0; j <= m; j++)
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }
        for (int j = 1; j <= m; j++)
            if (p[j] > 0 && p[j] <= n)
                SlotAssignments[unassigned[p[j] - 1]] = freeSlots[j - 1];
    }

    private int ReleaseSlot()
    {
        int releasedSlot = SlotAssignments.TryGetValue(this, out int slot) ? slot : -1;
        SlotAssignments.Remove(this);
        ActiveEnemies.Remove(this);
        slotAssignmentsDirty = true;
        nextSlotAssignmentTime = 0f;
        return releasedSlot;
    }

    public static EnemyWorker FindLivingEnemyAdjacentToSlot(int releasedSlot, EnemyWorker defeatedEnemy)
    {
        if (releasedSlot < 0 || releasedSlot >= FormationSlotCount)
            return null;

        for (int distance = 1; distance < FormationSlotCount; distance++)
        {
            int clockwiseSlot = (releasedSlot + distance) % FormationSlotCount;
            int counterClockwiseSlot = (releasedSlot - distance + FormationSlotCount) % FormationSlotCount;
            EnemyWorker clockwise = FindLivingEnemyInSlot(clockwiseSlot, defeatedEnemy);
            EnemyWorker counterClockwise = FindLivingEnemyInSlot(counterClockwiseSlot, defeatedEnemy);

            if (clockwise != null && counterClockwise != null)
            {
                Vector3 origin = defeatedEnemy != null ? defeatedEnemy.transform.position : Vector3.zero;
                return (clockwise.transform.position - origin).sqrMagnitude
                    <= (counterClockwise.transform.position - origin).sqrMagnitude
                    ? clockwise
                    : counterClockwise;
            }
            if (clockwise != null) return clockwise;
            if (counterClockwise != null) return counterClockwise;
        }
        return null;
    }

    private static EnemyWorker FindLivingEnemyInSlot(int slot, EnemyWorker excluded)
    {
        EnemyWorker candidate = null;
        foreach (KeyValuePair<EnemyWorker, int> assignment in SlotAssignments)
        {
            EnemyWorker enemy = assignment.Key;
            if (assignment.Value == slot && enemy != null && enemy != excluded && enemy.IsAlive)
            {
                candidate = enemy;
                break;
            }
        }
        // Evaluate readiness after dictionary enumeration. HasReachedAssignedSlot can refresh
        // the assignment table, which must never happen while it is being enumerated.
        return candidate != null && candidate.IsAvailableForCombatFocus ? candidate : null;
    }

    private Vector3 CalculateSlotPosition(int slot)
    {
        int fixedSlot = Mathf.Clamp(slot, 0, FormationSlotCount - 1);
        float angle = fixedSlot * (360f / FormationSlotCount);
        // The formation translates with the player, but it must not rotate with the
        // player's facing. Otherwise player facing moves every enemy destination,
        // producing a feedback loop where both sides orbit forever.
        Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        return PlayerWorker.player.position + radial * engagementRadius;
    }

    private double CalculateFormationRouteCost(int slot)
    {
        Vector3 center = PlayerWorker.player.position;
        Vector3 fromCenter = transform.position - center;
        fromCenter.y = 0f;
        float radius = Mathf.Max(0.05f, fromCenter.magnitude);
        float outerRadius = engagementRadius + formationRouteClearance;
        float currentAngle = Mathf.Repeat(
            Mathf.Atan2(fromCenter.x, fromCenter.z) * Mathf.Rad2Deg, 360f);
        float targetAngle = slot * (360f / FormationSlotCount);
        float shortestArcRadians = Mathf.Abs(Mathf.DeltaAngle(currentAngle, targetAngle)) * Mathf.Deg2Rad;
        float radialToOuter = Mathf.Abs(radius - outerRadius);
        float orbitDistance = shortestArcRadians * outerRadius;
        float finalEntry = Mathf.Abs(outerRadius - engagementRadius);
        float total = radialToOuter + orbitDistance + finalEntry;
        return total * total;
    }

    private void ResetFormationRoute()
    {
        formationRouteStage = FormationRouteStage.ExitToOuterRing;
        routedSlot = -1;
        formationOrbitDirection = 1;
        hasRequestedSlot = false;
        hasReachableSlotPosition = false;
        reachableSlotIndex = -1;
        destinationRefreshTimer = 0f;
        formationPathFailureTimer = 0f;
    }

    private void BeginDirectSlotRecoveryAfterKnockback()
    {
        ResetFormationRoute();
        int slot = GetAssignedSlot();
        if (slot < 0)
            return;

        // Knockback normally moves the enemy radially away from the player. Returning
        // directly to its own now-empty slot is shorter and avoids an unnecessary orbit.
        routedSlot = slot;
        formationRouteStage = FormationRouteStage.EnterAssignedSlot;
    }

    private void UpdateFocusedSlotRecoveryWatchdog()
    {
        bool focused = health > -1 && PlayerWorker.ent == gameObject;
        if (!focused || physicsReaction)
        {
            focusedSlotRecoveryTimer = 0f;
            RestoreAuthoredAvoidancePriority();
            return;
        }

        if (HasReachedAssignedSlot())
        {
            focusedSlotRecoveryTimer = 0f;
            RestoreAuthoredAvoidancePriority();
            return;
        }

        if (nav != null && avoidancePriorityCaptured)
            nav.avoidancePriority = Mathf.Min(authoredAvoidancePriority,
                focusedRecoveryAvoidancePriority);

        focusedSlotRecoveryTimer += Time.unscaledDeltaTime;
        if (focusedSlotRecoveryTimer < focusedSlotRecoveryTimeout)
            return;

        int slot = GetAssignedSlot();
        EnemyWorker replacement = FindLivingEnemyAdjacentToSlot(slot, this);
        if (playerWorker == null && PlayerWorker.player != null)
            playerWorker = PlayerWorker.player.GetComponent<PlayerWorker>();

        if (replacement != null && playerWorker != null
                && playerWorker.TryHandoffStalledFocusedEnemy(this, replacement))
        {
            focusedSlotRecoveryTimer = 0f;
            RestoreAuthoredAvoidancePriority();
            return;
        }

        // No adjacent enemy is ready yet. Retry soon without firing every frame.
        focusedSlotRecoveryTimer = Mathf.Max(0f, focusedSlotRecoveryTimeout - 1f);
    }

    private void RestoreAuthoredAvoidancePriority()
    {
        if (nav != null && avoidancePriorityCaptured
                && nav.avoidancePriority != authoredAvoidancePriority)
            nav.avoidancePriority = authoredAvoidancePriority;
    }

    private Vector3 CalculateFormationRouteDestination(int slot, out bool finalLeg)
    {
        finalLeg = false;
        Vector3 center = PlayerWorker.player.position;
        Vector3 finalSlot = CalculateSlotPosition(slot);
        float baseOuterRadius = engagementRadius + formationRouteClearance;

        if (routedSlot != slot)
        {
            routedSlot = slot;
            formationRouteStage = FormationRouteStage.ExitToOuterRing;
            hasRequestedSlot = false;
            hasReachableSlotPosition = false;
            reachableSlotIndex = -1;

            Vector3 initialOffset = transform.position - center;
            initialOffset.y = 0f;
            Vector3 initialRadial = initialOffset.sqrMagnitude > 0.0025f
                ? initialOffset.normalized
                : (finalSlot - center).normalized;
            float initialAngle = Mathf.Repeat(
                Mathf.Atan2(initialRadial.x, initialRadial.z) * Mathf.Rad2Deg, 360f);
            // Positive Y rotation is the clockwise direction in this formation's
            // angle convention. Keep the chosen shortest direction for the whole route.
            formationOrbitDirection = Mathf.DeltaAngle(initialAngle,
                slot * (360f / FormationSlotCount)) >= 0f ? 1 : -1;
        }

        // Opposite traffic directions use nearby concentric transit lanes, preventing
        // head-on NavMesh avoidance fights without creating another formation ring.
        float outerRadius = baseOuterRadius
            + (formationOrbitDirection > 0 ? formationOppositeLaneSeparation : 0f);

        Vector3 fromCenter = transform.position - center;
        fromCenter.y = 0f;
        float radius = fromCenter.magnitude;
        Vector3 currentRadial = radius > 0.05f
            ? fromCenter / radius
            : (finalSlot - center).normalized;

        // First leave the protected combat circle along the character's own radial.
        // This can never cut through the player or another occupied slot.
        if (formationRouteStage == FormationRouteStage.ExitToOuterRing)
        {
            Vector3 outerEntry = center + currentRadial * outerRadius;
            if (radius < outerRadius - formationWaypointTolerance
                    && Vector3.Distance(transform.position, outerEntry) > formationWaypointTolerance)
                return outerEntry;
            formationRouteStage = FormationRouteStage.OrbitOuterRing;
        }

        float targetAngle = slot * (360f / FormationSlotCount);
        if (formationRouteStage == FormationRouteStage.OrbitOuterRing)
        {
            float currentAngle = Mathf.Repeat(
                Mathf.Atan2(currentRadial.x, currentRadial.z) * Mathf.Rad2Deg, 360f);
            float angularTolerance = Mathf.Max(2f,
                formationWaypointTolerance / Mathf.Max(outerRadius, 0.1f) * Mathf.Rad2Deg);
            if (Mathf.Abs(Mathf.DeltaAngle(currentAngle, targetAngle)) <= angularTolerance)
            {
                formationRouteStage = FormationRouteStage.EnterAssignedSlot;
            }
            else
            {
                float remaining = formationOrbitDirection > 0
                    ? Mathf.Repeat(targetAngle - currentAngle, 360f)
                    : Mathf.Repeat(currentAngle - targetAngle, 360f);
                float nextAngle = currentAngle
                    + formationOrbitDirection * Mathf.Min(formationArcStepDegrees, remaining);
                Vector3 nextRadial = Quaternion.Euler(0f, nextAngle, 0f) * Vector3.forward;
                return center + nextRadial * outerRadius;
            }
        }

        finalLeg = true;
        float finalDistance = Vector3.Distance(transform.position, finalSlot);
        if (finalDistance <= formationWaypointTolerance)
            formationRouteStage = FormationRouteStage.Arrived;
        else if (formationRouteStage == FormationRouteStage.Arrived)
            formationRouteStage = FormationRouteStage.EnterAssignedSlot;
        return finalSlot;
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

        int assignedSlot = GetAssignedSlot();
        if (assignedSlot < 0)
        {
            if (nav.enabled && nav.isOnNavMesh)
            {
                nav.isStopped = true;
                nav.ResetPath();
            }
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
            return;
        }
        Vector3 requestedSlot = CalculateSlotPosition(assignedSlot);
        Vector3 routeDestination = CalculateFormationRouteDestination(assignedSlot, out bool finalRouteLeg);
        if (!nav.isOnNavMesh)
        {
            offNavMeshRecoveryTimer -= deltaTime;
            if (offNavMeshRecoveryTimer <= 0f)
            {
                offNavMeshRecoveryTimer = 0.25f;
                if (TryRecoverAgentOntoNavMesh(transform.position, false))
                {
                    BeginDirectSlotRecoveryAfterKnockback();
                    previousNavPosition = transform.position;
                    return;
                }
            }
            MoveTowardWithoutNavMesh(routeDestination, deltaTime);
            return;
        }
        offNavMeshRecoveryTimer = 0f;

        // Reaching a slot is not a permanent stop. The formation follows the player's
        // position, so any meaningful slot displacement immediately invalidates the stop.
        if (!hasRequestedSlot || (routeDestination - lastRequestedSlot).sqrMagnitude > 0.04f)
        {
            lastRequestedSlot = routeDestination;
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
            bool pathAssigned = TrySetReachableFormationDestination(routeDestination, finalRouteLeg, false)
                || TrySetReachableFormationDestination(routeDestination, finalRouteLeg, true);
            if (pathAssigned)
            {
                formationPathFailureTimer = 0f;
            }
            else
            {
                // Never drive directly through level geometry when a path is unavailable.
                // A fresh partial/complete path will be requested on the next refresh.
                nav.ResetPath();
                useDirectPursuit = false;
                formationPathFailureTimer += Mathf.Max(destinationRefreshInterval, deltaTime);
                if (formationPathFailureTimer >= 0.75f)
                    ResetFormationRoute();
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
        nav.ResetPath();
        destinationRefreshTimer = 0f;
        ResetFormationRoute();
    }

    private bool TryRecoverAgentOntoNavMesh(Vector3 desiredPosition, bool backtrackKnockback)
    {
        if (nav == null || !nav.enabled)
            return false;

        float radius = Mathf.Max(0.25f, navMeshRecoverySampleRadius);
        int steps = backtrackKnockback ? 8 : 1;
        for (int step = 0; step < steps; step++)
        {
            float t = steps <= 1 ? 0f : step / (float)(steps - 1);
            Vector3 probe = Vector3.Lerp(desiredPosition, knockbackStartPosition, t);
            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, radius, nav.areaMask))
                continue;

            if (!nav.Warp(hit.position))
                continue;

            transform.position = hit.position;
            if (rigid != null)
                rigid.position = hit.position;
            nav.updatePosition = true;
            nav.isStopped = false;
            return true;
        }
        return false;
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

    private bool TrySetReachableFormationDestination(
        Vector3 requestedPosition, bool finalRouteLeg, bool allowPartial)
    {
        Vector3 playerPosition = PlayerWorker.player.position;
        float minimumRadius = Mathf.Max(2.2f, engagementRadius * 0.8f);
        float sampleRadius = Mathf.Clamp(slotSampleRadius, 0.5f, 1.25f);
        if (!NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, sampleRadius, nav.areaMask))
            return false;

        Vector3 fromPlayer = hit.position - playerPosition;
        fromPlayer.y = 0f;
        if (fromPlayer.magnitude < minimumRadius)
            return false;

        if (!NavMesh.CalculatePath(nav.nextPosition, hit.position, nav.areaMask, slotPathProbe)
                || slotPathProbe.status == NavMeshPathStatus.PathInvalid
                || (!allowPartial && slotPathProbe.status != NavMeshPathStatus.PathComplete))
            return false;

        Vector3[] corners = slotPathProbe.corners;
        // A complete path whose start is already at its destination legitimately contains
        // one corner. Rejecting it prevents an already-positioned enemy from ever recording
        // the sampled slot endpoint.
        if (corners == null || corners.Length < 1)
            return false;
        Vector3 pathEndFromPlayer = corners[corners.Length - 1] - playerPosition;
        pathEndFromPlayer.y = 0f;
        if (pathEndFromPlayer.magnitude < minimumRadius)
            return false;

        // NavMesh may replace two safe outer-ring waypoints with a straight chord.
        // Reject any non-final segment that cuts into the occupied combat circle.
        if (!finalRouteLeg)
        {
            float protectedRadius = engagementRadius + formationRouteClearance * 0.35f;
            Vector3 startFromPlayer = corners[0] - playerPosition;
            startFromPlayer.y = 0f;
            // An enemy displaced inside the circle must be allowed to travel radially
            // outward first. Once outside, every subsequent orbit segment is guarded.
            if (startFromPlayer.magnitude >= protectedRadius - formationWaypointTolerance)
                for (int i = 1; i < corners.Length; i++)
                    if (DistanceFromPointToSegmentXZ(playerPosition, corners[i - 1], corners[i])
                        < protectedRadius)
                        return false;
        }

        bool pathAccepted = nav.SetPath(slotPathProbe);
        if (pathAccepted && finalRouteLeg
                && slotPathProbe.status == NavMeshPathStatus.PathComplete)
        {
            reachableSlotPosition = corners[corners.Length - 1];
            reachableSlotIndex = routedSlot;
            hasReachableSlotPosition = true;
        }
        else if (finalRouteLeg && slotPathProbe.status != NavMeshPathStatus.PathComplete)
        {
            hasReachableSlotPosition = false;
            reachableSlotIndex = -1;
        }
        return pathAccepted;
    }

    private static float DistanceFromPointToSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector2 p = new(point.x, point.z);
        Vector2 a = new(start.x, start.z);
        Vector2 b = new(end.x, end.z);
        Vector2 segment = b - a;
        float denominator = segment.sqrMagnitude;
        if (denominator <= 0.000001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / denominator);
        return Vector2.Distance(p, a + segment * t);
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

        Vector3 settledPosition = rigid.position;
        if (reachedDistance)
        {
            settledPosition = knockbackStartPosition + knockbackDirection * knockbackDistance;
            settledPosition.y = rigid.position.y;
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
        nav.updatePosition = true;
        bool recoveredToNavMesh = TryRecoverAgentOntoNavMesh(settledPosition, true);
        if (recoveredToNavMesh && nav.isOnNavMesh)
            nav.isStopped = false;
        else
        {
            transform.position = settledPosition;
            rigid.position = settledPosition;
        }
        previousNavPosition = transform.position;
        navStuckTimer = 0f;
        BeginDirectSlotRecoveryAfterKnockback();
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
        if (sourceAttackSequence == resolvedPlayerAttackSequence)
            return;
        hitPoint = SwordHitbox.ResolveSurfaceContact(weaponCollider, bodyCollider, hitPoint);
        if (attacker != null && attacker.IsAttackHitActive && PlayerWorker.player != null
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
        {
            resolvedPlayerAttackSequence = sourceAttackSequence;
            bool blocked = guard == 1
                           && CombatDirectionUtility.HasDifferentHorizontalSign(
                               attacker.AttackDirection.x, GuardDirection.x)
                           && CombatDirectionUtility.CanBlock(
                               attacker.AttackMouseDirection, sword);
            if (!blocked)
            {
                // Being hit first invalidates this enemy's pending forced attack even if
                // Animator still reports Attack until its next evaluation.
                InterruptCurrentAttackByHit();
                health = Mathf.Max(0, health - 1);
                if (health > 0)
                {
                    Vector3 hitpoint = Quaternion.Euler(new Vector3(0, -transform.rotation.eulerAngles.y, 0)) * (hitPoint - (transform.position + Vector3.up));
                    anim.SetFloat("cx", Mathf.Clamp(hitpoint.x, -1, 1));
                    anim.SetFloat("cy", Mathf.Clamp(hitpoint.y, 0, 1));
                    guard = 0;
                    swing = 0;
                    returnToIdleAfterHurt = true;
                    hurtStateEntered = false;
                    anim.CrossFade("Hurted", 0f, 0);
                    Vector3 impactDirection = transform.position - PlayerWorker.player.position;
                    impactDirection.y = 0f;
                    ApplyCombatKnockback(impactDirection);
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                }
                else
                {
                    health = -1;
                    attacker.NotifyAttackTargetDefeated();
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                    if (ArenaGameDirector.Instance != null)
                    {
                        int releasedSlot = ReleaseSlot();
                        ArenaGameDirector.Instance.NotifyEnemyDefeated(this, releasedSlot);
                        return;
                    }
                    if (guard == 1)
                    {
                        guard = 0;
                        anim.CrossFade("Idle", 0f, 0);
                        anim.CrossFade("Idle", 0f, 1);
                        //sword.localPosition = swordOffset;
                    }
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
                attacker.EnterGuardBrokenState();
            }
        }
    }

    public void ResolvePlayerAttackAtSlowEnd(PlayerWorker attacker, int sourceAttackSequence)
    {
        if (attacker == null || sourceAttackSequence != attacker.AttackSequence || !IsAlive)
            return;

        // There is intentionally no physical contact for this fallback. Use the struck
        // character's chest centre only in that case; real trigger hits retain their contact.
        Transform chest = anim != null && anim.isHuman
            ? anim.GetBoneTransform(HumanBodyBones.Chest)
            : null;
        Vector3 impactPoint = chest != null ? chest.position : transform.position + Vector3.up;
        ReceiveSwordHit(attacker, null, null, impactPoint, sourceAttackSequence);
    }

    public void ResetForPool()
    {
        ResetTransientCombatState(true);
        health = startingHealth;
        guard = 0;
        swing = 0;
        attackSlowTriggered = false;
        restoringAttackSpeed = false;
        attackSlowTimer = 0f;
        SetSwordDamageTriggersEnabled(true);
        coolTimer = GetActionCooldown();
        guardTimer = -1f;
        attackSequence = 0;
        interruptedAttackSequence = -1;
        resolvedPlayerAttackSequence = -1;
        gameObject.tag = "Enemy";
        if (anim != null)
        {
            anim.enabled = true;
            anim.speed = 1f;
            anim.Rebind();
            anim.Update(0f);
        }
        if (rig != null) rig.weight = 1f;
        if (characterController != null) characterController.enabled = true;
        if (nav != null)
        {
            nav.enabled = true;
            if (nav.isOnNavMesh)
            {
                nav.isStopped = false;
                nav.ResetPath();
            }
        }
        destinationRefreshTimer = 0f;
        ResetFormationRoute();
        arrivalTrackedSlot = -1;
        slotArrivalLatched = false;
        if (!ActiveEnemies.Contains(this)) ActiveEnemies.Add(this);
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
