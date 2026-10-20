using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;
using FIMSpace.FProceduralAnimation;

[RequireComponent(typeof(CharacterController))]
public class PlayerWorker : MonoBehaviour
{
    public static Transform player;
    public static Animator playerAnimator;
    [SerializeField] private Transform crosshead;
    [Header("Character rig overrides")]
    [Tooltip("Leave empty for the original character. Assign a child Animator when the visible character rig lives below this gameplay root.")]
    [SerializeField] private Animator animatorOverride;
    [Tooltip("Leave empty for the original character. Assign the Rig Builder that belongs to Animator Override when using a replacement character.")]
    [SerializeField] private RigBuilder rigBuilderOverride;
    private Animator anim;
    public static Transform psword;
    [SerializeField] private Transform sword;
    [SerializeField] private float sensitivity = 5;
    public enum InputMode { PC, Mobile }
    [Header("Input")]
    [SerializeField] private InputMode inputMode = InputMode.PC;
    [SerializeField, Min(0.1f)] private float mobilePoseLerpSpeed = 12f;
    [SerializeField, Min(50f)] private float mobileSwipeSpeed = 900f;
    [SerializeField, Min(0.1f)] private float mobileSwingPoseTolerance = 4f;
    private Vector2 mobileTargetPose;
    private bool mobilePoseInitialized;
    private bool queuedMobileSwing;
    private Vector2 previousMobileMousePosition;
    private bool mobileMouseDragging;
    private bool arenaGameplayEnabled = true;
    private float telegraphedGuardTimer;
    private EnemyWorker telegraphedAttacker;
    private int telegraphedAttackSequence;
    private bool telegraphedGuardAttempted;
    [SerializeField] private Vector3 swordOffset;
    private Vector3 camDis;
    [SerializeField] private float canSwing = 10f;
    public static float tx, ty;
    public static int swing, guard;
    private GameObject hitVFX, shieldVFX;
    public static GameObject ent;
    private Animator entAnim;
    private bool onTarget;
    private Rigidbody rigid;
    private Rig rig;
    private LegsAnimator legAnim;
    [SerializeField] private int health = 2;
    private RigBuilder rb;
    private AudioSource audioSource;
    private CapsuleCollider movementCapsule;
    // CharacterController owns normal locomotion; the root Rigidbody temporarily owns knockback.
    private CharacterController characterController;
    private NavMeshAgent nav;
    private GameObject navigationTarget;
    private Vector3 lastTargetDirection = Vector3.forward;
    private float navigationTargetRefreshTimer;
    private bool holdingCombatDistance;
    private Vector2 movementInput;
    private Vector3 movementVelocity;
    private float verticalVelocity;
    private Vector2 attackDirection;
    private int attackSequence;
    [Header("Attack reaction window")]
    [SerializeField, Range(0f, 0.5f)] private float attackSlowTrigger = 0.1f;
    [SerializeField, Range(0.001f, 0.2f)] private float attackSlowSpeed = 0.01f;
    [SerializeField, Min(0.1f)] private float attackSlowDuration = 1f;
    [SerializeField, Min(0.1f)] private float attackSpeedRestoreSharpness = 8f;
    private bool attackSlowTriggered;
    private bool restoringAttackSpeed;
    private float attackSlowTimer;
    private SwordHitbox swordHitbox;
    private EnemyWorker slowedAttackTarget;
    private bool defeatedTargetTriggerLock;
    private EnemyWorker resolvedIncomingAttacker;
    private int resolvedIncomingAttackSequence = -1;
    private bool hurtUpperLayerSuppressed;
    private bool hurtStateEntered;
    private float hurtStateEntryGrace;
    private float hurtUpperLayerWeight = 1f;
    private sealed class ReactionFadedRendererState
    {
        public Material[] OriginalMaterials;
        public Material[] RuntimeMaterials;
    }
    private readonly Dictionary<Renderer, ReactionFadedRendererState> reactionFadedRenderers = new();
    private bool reactionVisualsDimmed;
    [Header("Attack hit detection")]
    [SerializeField, Range(0f, 1f)] private float attackHitStart = 0f;
    [SerializeField, Range(0f, 1.2f)] private float attackHitEnd = 1.05f;
    [Header("Sword slash VFX")]
    [Tooltip("Length of the motion-synced slash ribbon, measured in blade lengths.")]
    [SerializeField, Min(0.25f)] private float swordSlashTravelInBladeLengths = 2.25f;
    [SerializeField, Range(0.25f, 1.5f)] private float swordSlashBladeWidth = 1f;
    [SerializeField, Range(0.1f, 2f)] private float swordSlashIntensity = 1.1f;
    [SerializeField, Range(0f, 1f)] private float swordSlashTextureFlow = 0.16f;
    [SerializeField] private bool swordSlashUseWeaponEffectColor = true;
    [SerializeField, ColorUsage(true, true)] private Color swordSlashColor = new(1.2f, 0.35f, 0.08f, 0.9f);
    [SerializeField] private Material swordSlashMaterialOverride;
    [Header("Movement collision")]
    [SerializeField] private float maxStepHeight = 0.45f;
    [SerializeField, Range(1f, 80f)] private float maxSlopeAngle = 55f;
    [SerializeField] private float movementSkin = 0.03f;
    [SerializeField, Min(1f)] private float groundAcceleration = 35f;
    [SerializeField, Min(1f)] private float airAcceleration = 8f;
    [Header("Facing and knockback")]
    [SerializeField, Min(0.1f)] private float facingLerpSpeed = 10f;
    [Header("Target handoff safety")]
    [SerializeField, Min(0f)] private float targetSwitchAttackGrace = 0.65f;
    [SerializeField, Range(2f, 35f)] private float targetFacingReadyAngle = 12f;
    [SerializeField, Min(0f)] private float targetFacingReadyHold = 0.18f;
    [SerializeField, Min(0.5f)] private float knockbackDistance = 5f;
    [SerializeField, Min(0.1f)] private float knockbackSlideDamping = 4.5f;
    [SerializeField, Min(0.01f)] private float knockbackStopSpeed = 0.15f;
    [SerializeField, Min(0.2f)] private float knockbackMaxTime = 3f;
    private Quaternion desiredFacingRotation;
    private bool hasFacingTarget;
    private float focusedEnemyAttackUnlockTime;
    private float focusedEnemyFacingStableTime;
    private GameObject readinessTarget;
    private bool rootPhysicsKnockback;
    private float knockbackTimer;
    private Vector3 knockbackStartPosition;
    private Vector3 knockbackDirection;
    [SerializeField] private MultiAimConstraint ma;
    [SerializeField] private Vector3 headRotOffset;
    [SerializeField, Min(0.1f)] private float headLookSpeed = 12f;
    private Vector3 smoothedHeadLookDirection;
    private bool headLookDirectionInitialized;
    private Quaternion headAxisCorrection;
    private bool headAxisCorrectionInitialized;
    [SerializeField] private float movementSpeed = 5f;
    [Header("Automatic NavMesh pursuit")]
    [SerializeField, Min(0.1f)] private float navigationTargetRefreshInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float navigationStoppingDistance = 2.4f;
    [SerializeField, Min(0.1f)] private float navigationResumeDistance = 3.4f;
    [SerializeField] private Vector3 camPos;
    [SerializeField] private RectTransform endUI;
    [SerializeField] private Text header;
    [SerializeField] private bool isGameMode;
    [SerializeField] private bool miiMotion;
    [SerializeField] private Transform body;
    [SerializeField] private Transform des;
    private float preScore;
    private bool isEnd;
    GameObject rightTemp, leftTemp;

    public Vector2 AttackDirection => attackDirection;
    public Vector2 GuardDirection => new(tx, ty);
    public Transform SwordTransform => sword;
    public int AttackSequence => attackSequence;
    public bool IsAttackHitActive => health > -1 && attackSequence > 0 && attackSlowTriggered
        && CombatDirectionUtility.IsAttackStateActive(anim, attackHitStart, attackHitEnd);
    public bool IsHoldingAttackSlowOn(EnemyWorker target)
    {
        return target != null
               && slowedAttackTarget == target
               && attackSlowTriggered
               && attackSlowTimer > 0f;
    }
    public bool UsesMobileInput => inputMode == InputMode.Mobile;
    public void SetSensitivity(float value) => sensitivity = Mathf.Max(0.1f, value);
    public void SetArenaGameplayEnabled(bool value)
    {
        arenaGameplayEnabled = value;
        if (!value)
        {
            movementInput = Vector2.zero;
            hasFacingTarget = false;
            StopNavigation();
            if (anim != null)
            {
                anim.SetFloat("mx", 0f);
                anim.SetFloat("mz", 0f);
            }
        }
    }

    /*3d character application
     head's up, forward axis compare and edit animation rigging's head ik
     add sword, fit the position, add lh target and fitting, apply Hitter's target reference
     hand ik enable and let left arm to follow lh target
     add components, legs animator - ik hint mode -> onGoal*/
    private void Awake()
    {
        player = transform;
        Time.timeScale = 1.0f;
        endUI.gameObject.SetActive(false);
        isEnd = false;
        if (ArenaGameDirector.Instance == null)
            Swordplay.UI.MedievalCursorController.HideGameplayCursor(inputMode != InputMode.Mobile);
        anim = animatorOverride != null ? animatorOverride : GetComponent<Animator>();
        playerAnimator = anim;
        InitializeHeadAxisCorrection();
        TargetMatching weaponTarget = GetComponentInChildren<TargetMatching>(true);
        psword = weaponTarget != null ? weaponTarget.transform : null;
        if (sword == null) sword = psword;
        hitVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeHitVFX");
        shieldVFX = Resources.Load<GameObject>("Prefab/BrokenEdgeGuardVFX");
        rigid = GetComponent<Rigidbody>();
        if (rigid == null) rigid = gameObject.AddComponent<Rigidbody>();
        movementCapsule = GetComponent<CapsuleCollider>();
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
            characterController = gameObject.AddComponent<CharacterController>();
        ConfigureCharacterController();
        nav = ResolveRootAgent();
        ConfigureNavigationAgent();
        if (rigid != null)
        {
            rigid.isKinematic = true;
            rigid.useGravity = false;
            rigid.detectCollisions = false;
            rigid.constraints = RigidbodyConstraints.FreezeAll;
        }
        if (anim != null)
            anim.applyRootMotion = false;
        ConfigurePhysicalRoot();
        if (psword != null)
            swordHitbox = SwordHitbox.Configure(psword.gameObject, this);
        Transform slashSword = sword != null ? sword : psword;
        if (slashSword != null)
            Swordplay.Visuals.SwordSlashVfxSynchronizer.Configure(
                slashSword, anim, swordSlashTravelInBladeLengths,
                swordSlashBladeWidth, swordSlashIntensity, swordSlashTextureFlow,
                swordSlashUseWeaponEffectColor, swordSlashColor, swordSlashMaterialOverride);
        rig = GetComponentInChildren<Rig>();
        legAnim = GetComponent<LegsAnimator>();
        rb = rigBuilderOverride != null ? rigBuilderOverride : GetComponent<RigBuilder>();
        audioSource = GetComponent<AudioSource>();
        preScore = (new Vector3(0, 60) - new Vector3(tx, ty)).magnitude;
        rightTemp = new GameObject("RightTemp");
        leftTemp = new GameObject("LeftTemp");
    }

    private List<GameObject> enemies = new List<GameObject>();
    public GameObject FindAdvancedEnemy(int idx)
    {
        GameObject[] gos;
        gos = GameObject.FindGameObjectsWithTag("Enemy");
        Vector3 position = transform.position;
        enemies.Clear();
        foreach (GameObject go in gos)
        {
            if(IsEnemyInView(go.gameObject))
                enemies.Add(go);
        }
        if (idx > enemies.Count - 1 || idx == -1)
            return ent = null;
        return ent = enemies[idx].gameObject;
    }

    public GameObject FindClosestEnemy()
    {
        GameObject[] gos;
        gos = GameObject.FindGameObjectsWithTag("Enemy");
        GameObject closest = null;
        float bestScore = float.NegativeInfinity;
        Vector3 position = transform.position;
        foreach (GameObject go in gos)
        {
            Vector3 diff = go.transform.position - position;
            diff.y = 0f;
            float distance = Mathf.Max(0.01f, diff.magnitude);
            float score = Vector3.Dot(transform.forward, diff / distance) * 100f - distance;
            if (score > bestScore && IsEnemyInView(go.gameObject))
            {
                closest = go;
                ent = closest;
                bestScore = score;
            }
        }
        return closest;
    }

    private bool IsEnemyInView(GameObject enemy)
    {
        if (enemy == null)
            return false;
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, enemy.transform.position - transform.position, out hit))
        {
            if (hit.collider.gameObject.layer != 7)
                return false;
        }
        return (enemy.transform.position - transform.position).magnitude < 10f;
    } 

    private void IsGoal()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 5, transform.forward, out hit))
        {
            if (isGameMode && hit.collider.gameObject.layer == 10)
            {
                isEnd = true;
                Swordplay.UI.MedievalCursorController.ShowMenuCursor();
                Invoke("ClearUI", 4);
            }
        }
    }

    private bool IsStuffInView()
    {
        RaycastHit hit;
        // 벽이 있을 경우 Raycast로 충돌 확인
        if (Physics.Raycast(transform.position + Vector3.up * 5, -transform.forward, out hit, 5f))
        {
            if (hit.collider.gameObject.layer == 0)
            {
                camDis = new Vector3(hit.point.x - transform.position.x, 0, hit.point.z - transform.position.z);
                return true;
            }
        }
        return false;
    }

    private void Update()
    {
        // The launch menu pauses gameplay without disabling this component. Disabling and
        // re-enabling the whole worker changes Animation Rigging evaluation order and can
        // leave the left-hand IK in a visibly different pose on the first gameplay frame.
        if (!arenaGameplayEnabled)
            return;

        UpdateAttackSlowdown();
        UpdateHurtLayerSuppression();
        UpdateReactionTransparency();
        movementInput = Vector2.zero;
        UpdateAutomaticNavigation();
        if(swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            anim.SetFloat("tx", tx);
            anim.SetFloat("ty", ty);
        }
        Vector2 pcPoseInput = Vector2.zero;
        if (inputMode == InputMode.Mobile)
            UpdateMobileInput();
        else
        {
            pcPoseInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            tx += pcPoseInput.x * sensitivity;
            ty += pcPoseInput.y * sensitivity;
        }
        tx = Mathf.Clamp(tx, -180, 180);
        ty = Mathf.Clamp(ty, -30, 150);
        IsGoal();
        if (isEnd)
            Time.timeScale = Mathf.Lerp(Time.timeScale, 0.5f, 0.01f);
        /*else if(onTarget && ma.data.sourceObjects[0].transform == crosshead)
        {
            var data = ma.data.sourceObjects;
            data.SetTransform(0, entAnim.GetBoneTransform(HumanBodyBones.Head));
            ma.data.sourceObjects = data;
            rb.Build();
        }*/
        if (health > -1)
        {
            // Locomotion is automatic through the root NavMeshAgent. Keyboard axes are
            // deliberately ignored; mouse input continues to control attacks and guarding.
            if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
            {
                UpdateNavigationAnimation();
            }
            if (onTarget && !isEnd && ent != null)
            {
                if (swing == 0 && health > -1)
                {
                    Vector3 toEnemy = ent.transform.position - transform.position;
                    toEnemy.y = 0f;
                    if (toEnemy.sqrMagnitude > 0.0001f)
                    {
                        desiredFacingRotation = Quaternion.LookRotation(toEnemy.normalized, Vector3.up);
                        hasFacingTarget = true;
                    }
                }
            }
        }
        UpdateFocusedEnemyAttackReadiness();
        bool manualGuard = inputMode == InputMode.PC && Input.GetMouseButton(1);
        if (inputMode == InputMode.PC && telegraphedGuardTimer > 0f
            && (pcPoseInput.sqrMagnitude > 0.0001f || Input.GetMouseButtonDown(1)))
            TryCommitTelegraphedGuard();

        if (telegraphedGuardTimer > 0f)
            telegraphedGuardTimer = Mathf.Max(0f, telegraphedGuardTimer - Time.unscaledDeltaTime);
        if (telegraphedAttacker != null
            && !telegraphedAttacker.IsPerformingAttackSequence(telegraphedAttackSequence))
            ClearTelegraphedGuard();
        if (telegraphedAttacker != null && telegraphedGuardTimer <= 0f && !telegraphedGuardAttempted)
            ClearTelegraphedGuard();

        bool forcedGuard = telegraphedGuardTimer > 0f
                           || (telegraphedGuardAttempted && telegraphedAttacker != null);
        bool wantsGuard = manualGuard || forcedGuard;
        if (!wantsGuard)
        {
            if (guard == 1)
            {
                guard = 0;
                anim.ResetTrigger("EnterBlock");
                anim.SetTrigger("ExitBlock");
                //sword.localPosition = swordOffset;//new Vector3(-0.38f, 0.514f, -0.027f);
            }
        }
        else if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
        {
            if (guard == 0)
            {
                guard = 1;
                anim.ResetTrigger("ExitBlock");
                anim.SetTrigger("EnterBlock");
                //sword.localPosition = new Vector3(sword.localPosition.x + 1, sword.localPosition.y - 2, sword.localPosition.z);
                //sword.position += transform.forward;
                SoundManager.Instance.SFXPlay("Shield");
            }
        }
        float curScore = (new Vector3(0, 60) - new Vector3(tx, ty)).magnitude;
        float goalScore = preScore - curScore;
        float testScore = (new Vector3(0, 0) - new Vector3(tx, ty)).magnitude;
        bool attackPoseOutsideCenter = CombatDirectionUtility.IsOutsideCenterAttackSector(
            new Vector2(tx, ty));

        anim.SetFloat("time", 1 - testScore / 180);
        preScore = curScore;
        bool wantsSwing = inputMode == InputMode.Mobile ? queuedMobileSwing : goalScore > canSwing;
        bool mobilePoseReady = inputMode != InputMode.Mobile || Vector2.Distance(new Vector2(tx, ty), mobileTargetPose) <= mobileSwingPoseTolerance;
        EnemyWorker combatTarget = ent != null ? ent.GetComponent<EnemyWorker>() : null;
        bool targetHasReachedSlot = combatTarget != null && combatTarget.HasReachedFormationSlot;
        if (inputMode == InputMode.Mobile && wantsSwing && !targetHasReachedSlot)
            queuedMobileSwing = false;
        if (health > -1 && targetHasReachedSlot && attackPoseOutsideCenter && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && wantsSwing && mobilePoseReady && guard == 0 && swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            queuedMobileSwing = false;
            BeginAttack();
        }
        if (anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") && ent != null && !entAnim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && health > -1)
        {
            if (swing == 0)
            {
                swing = 1;
            }
        }
        else if (health > -1)
        {
            if (swing == 1)
            {
                swing = 0;
            }
        }
        if (!onTarget)
        {
            if (FindClosestEnemy()!= null)
            {
                AssignCombatTarget(ent);
            }
        }
        if (onTarget && ent != null)
        {
            if (!ent.activeInHierarchy || !ent.CompareTag("Enemy"))
            {
                onTarget = false;
            }
        }
        else if (ent == null)
        {
            onTarget = false;
        }
        if (!onTarget || ent == null)
            hasFacingTarget = false;
        Camera activeCamera = Camera.main;
        Transform spineBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Spine) : null;
        Transform hipsBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        activeCamera.transform.position = new Vector3(spineBone.position.x, hipsBone.position.y + camPos.y, spineBone.position.z) - transform.forward * camPos.z;
        //Camera.main.transform.rotation = Quaternion.Euler(new Vector3(20, transform.rotation.eulerAngles.y, 0));
        /*if (!IsStuffInView())
        {
            Camera.main.transform.localPosition = camPos + Vector3.up * anim.GetBoneTransform(HumanBodyBones.Hips).localPosition.y;
            Camera.main.transform.localRotation = Quaternion.Euler(new Vector3(20, 0, 0));
        }
        else
        {
            Camera.main.transform.localPosition = new Vector3(camPos.x, camPos.y + anim.GetBoneTransform(HumanBodyBones.Hips).localPosition.y, 0);
            Camera.main.transform.position -=  Quaternion.Euler(new Vector3(0, transform.rotation.eulerAngles.y, 0)) * Vector3.forward * camDis.magnitude * 0.75f;
            Camera.main.transform.localRotation = Quaternion.Euler(new Vector3(20, 0, 0));
        }*/
        if (miiMotion) return;

        float w = Mathf.Clamp01((180f + tx) / 360f);

        // Source 0 = RightSocket, Source 1 = LeftSocket 라고 가정
        var sources = weaponBlend.data.sourceObjects;   // WeightedTransformArray (값 타입 취급 주의)
        sources.SetWeight(0, 1f - w); // 오른손 영향
        sources.SetWeight(1, w);      // 왼손 영향
        weaponBlend.data.sourceObjects = sources;
    }

    private void UpdateMobileInput()
    {
        if (!mobilePoseInitialized)
        {
            mobileTargetPose = new Vector2(tx, ty);
            mobilePoseInitialized = true;
        }
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            SetMobileTargetPose(touch.position);
            if (telegraphedGuardTimer > 0f && touch.phase != TouchPhase.Canceled)
                TryCommitTelegraphedGuard();
            float swipeSpeed = touch.deltaPosition.magnitude / Mathf.Max(0.001f, Time.unscaledDeltaTime);
            if (touch.phase == TouchPhase.Moved && swipeSpeed >= mobileSwipeSpeed)
                queuedMobileSwing = true;

            // A real touch always owns this mobile input path when one is present.
            mobileMouseDragging = false;
        }
#if UNITY_EDITOR || UNITY_STANDALONE
        // A simulated touch can remain present in the editor. Keep mouse handling
        // independent so that it cannot starve every click after the first one.
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePosition = Input.mousePosition;
            SetMobileTargetPose(mousePosition);
            previousMobileMousePosition = mousePosition;
            mobileMouseDragging = true;
            if (telegraphedGuardTimer > 0f)
                TryCommitTelegraphedGuard();
        }
        else if (Input.GetMouseButton(0))
        {
            Vector2 mousePosition = Input.mousePosition;
            SetMobileTargetPose(mousePosition);
            if (telegraphedGuardTimer > 0f)
                TryCommitTelegraphedGuard();
            if (mobileMouseDragging)
            {
                float swipeSpeed = (mousePosition - previousMobileMousePosition).magnitude
                    / Mathf.Max(0.001f, Time.unscaledDeltaTime);
                if (swipeSpeed >= mobileSwipeSpeed)
                    queuedMobileSwing = true;
            }
            previousMobileMousePosition = mousePosition;
            mobileMouseDragging = true;
        }
        else
            mobileMouseDragging = false;
#endif

        float t = 1f - Mathf.Exp(-mobilePoseLerpSpeed * Time.unscaledDeltaTime);
        Vector2 pose = Vector2.Lerp(new Vector2(tx, ty), mobileTargetPose, t);
        tx = pose.x;
        ty = pose.y;
    }

    private void SetMobileTargetPose(Vector2 screenPosition)
    {
        mobileTargetPose = new Vector2(
            Mathf.Lerp(-180f, 180f, screenPosition.x / Mathf.Max(1f, Screen.width)),
            Mathf.Lerp(-30f, 150f, screenPosition.y / Mathf.Max(1f, Screen.height)));
    }

    public void BeginTelegraphedBlock(EnemyWorker attacker, Vector2 _,
        int incomingAttackSequence, float duration)
    {
        if (attacker == null || health < 0) return;

        bool attacking = IsPlayerAttackSequenceActive();
        if (attacking)
        {
            // Attacking has priority over the reaction guard in every input mode. Do not
            // create a guard window: the enemy's forced resolution will treat this as a hit.
            ClearTelegraphedGuard();
            return;
        }

        // Pooled enemies restart their local sequence counter. A newly announced attack is
        // therefore also the safe point to clear an old deduplication key from a prior life.
        if (resolvedIncomingAttacker == attacker
            && resolvedIncomingAttackSequence == incomingAttackSequence)
        {
            resolvedIncomingAttacker = null;
            resolvedIncomingAttackSequence = -1;
        }
        telegraphedAttacker = attacker;
        telegraphedAttackSequence = incomingAttackSequence;
        telegraphedGuardTimer = Mathf.Max(0.1f, duration);
        telegraphedGuardAttempted = false;
        queuedMobileSwing = false;
        swing = 0;
        guard = 1;
        anim.ResetTrigger("ExitBlock");
        anim.SetTrigger("EnterBlock");
    }

    private void TryCommitTelegraphedGuard()
    {
        if (telegraphedGuardTimer <= 0f || telegraphedAttacker == null)
            return;
        // Input authorizes a guard attempt, but the final result uses the swords' actual
        // rendered orientations at impact after the guard animation has caught up.
        telegraphedGuardAttempted = true;
    }

    private bool CanBlockTelegraphedAttack(EnemyWorker attacker)
    {
        return attacker != null
               && telegraphedAttacker == attacker
               && telegraphedAttackSequence == attacker.AttackSequence
               && telegraphedGuardAttempted
               && CombatDirectionUtility.CanBlock(attacker.SwordTransform, sword)
               && IsActivelyGuarding();
    }

    private void ResolveTelegraphedAttack(EnemyWorker attacker)
    {
        if (attacker != telegraphedAttacker) return;
        telegraphedGuardTimer = 0f;
        telegraphedAttacker = null;
        telegraphedAttackSequence = 0;
        telegraphedGuardAttempted = false;
    }

    private void ClearTelegraphedGuard()
    {
        telegraphedGuardTimer = 0f;
        telegraphedAttacker = null;
        telegraphedAttackSequence = 0;
        telegraphedGuardAttempted = false;
    }

    private void UpdateReactionTransparency()
    {
        bool shouldDim = rootPhysicsKnockback || hurtUpperLayerSuppressed || IsReactionStateActive();
        if (!shouldDim)
        {
            // Do not trust only the cached bool here. A state interruption (for example
            // Hurted -> BlockIdle on the same frame as a kill) can leave cloned materials
            // in the dictionary after the state flag has already changed.
            if (reactionVisualsDimmed || reactionFadedRenderers.Count > 0)
                RestoreReactionVisualMaterials();
            return;
        }

        if (reactionVisualsDimmed)
            return;

        reactionVisualsDimmed = true;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || reactionFadedRenderers.ContainsKey(renderer))
                continue;

            Material[] originals = renderer.sharedMaterials;
            Material[] runtimeMaterials = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                if (source == null) continue;
                Material runtime = new Material(source)
                {
                    name = source.name + " (Runtime Reaction Transparent)",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                ConfigureReactionTransparentMaterial(runtime);
                runtimeMaterials[i] = runtime;
            }

            reactionFadedRenderers.Add(renderer, new ReactionFadedRendererState
            {
                OriginalMaterials = originals,
                RuntimeMaterials = runtimeMaterials
            });
            renderer.sharedMaterials = runtimeMaterials;
        }
    }

    private bool IsReactionStateActive()
    {
        if (anim == null) return false;
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

    private static void ConfigureReactionTransparentMaterial(Material material)
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

    private void RestoreReactionVisualMaterials()
    {
        foreach (KeyValuePair<Renderer, ReactionFadedRendererState> pair in reactionFadedRenderers)
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
        reactionFadedRenderers.Clear();
        reactionVisualsDimmed = false;
    }

    private void BeginAttack()
    {
        if (!CombatDirectionUtility.IsOutsideCenterAttackSector(new Vector2(tx, ty)))
            return;

        attackDirection = new Vector2(tx, ty);
        attackSequence++;
        attackSlowTriggered = false;
        restoringAttackSpeed = false;
        attackSlowTimer = 0f;
        slowedAttackTarget = ent != null ? ent.GetComponent<EnemyWorker>() : null;
        defeatedTargetTriggerLock = false;
        SetSwordDamageTriggersEnabled(true);
        anim.speed = 1f;
        anim.CrossFade("Attack", 0f, 0);
        anim.CrossFade("Attack", 0f, 1);
        // SoundManager.Instance.SFXPlay("Swing1"); // Temporarily mute the player's swing SFX.
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
        }

        if (!attackSlowTriggered) return;
        if (!inAttack)
        {
            anim.speed = 1f;
            if (defeatedTargetTriggerLock && IsIdleStateActive())
                defeatedTargetTriggerLock = false;
            if (!defeatedTargetTriggerLock)
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
                ResolveAttackAtSlowEnd();
                restoringAttackSpeed = true;
                if (!defeatedTargetTriggerLock)
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

    private void ResolveAttackAtSlowEnd()
    {
        if (slowedAttackTarget != null)
            slowedAttackTarget.ResolvePlayerAttackAtSlowEnd(this, attackSequence);
    }

    private void SetSwordDamageTriggersEnabled(bool value)
    {
        if (swordHitbox == null)
            swordHitbox = GetComponentInChildren<SwordHitbox>(true);
        swordHitbox?.SetDamageTriggersEnabled(value);
    }

    public void NotifyAttackTargetDefeated()
    {
        bool attacking = swing != 0
                         || (anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                         || (anim != null && anim.IsInTransition(0)
                             && anim.GetNextAnimatorStateInfo(0).IsName("Attack"));
        if (!attacking) return;

        defeatedTargetTriggerLock = true;
        SetSwordDamageTriggersEnabled(false);
    }

    private bool IsIdleStateActive()
    {
        if (anim == null) return true;
        if (anim.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
            return true;
        return anim.IsInTransition(0) && anim.GetNextAnimatorStateInfo(0).IsName("Idle");
    }

    public void ResolveEnemyAttackAtSlowEnd(EnemyWorker attacker, int sourceAttackSequence)
    {
        if (attacker == null || sourceAttackSequence != attacker.AttackSequence)
            return;
        ReceiveSwordHit(attacker, null, null, GetForcedImpactPoint(), sourceAttackSequence);
    }

    private Vector3 GetForcedImpactPoint()
    {
        Transform chest = anim != null && anim.isHuman
            ? anim.GetBoneTransform(HumanBodyBones.Chest)
            : null;
        return chest != null ? chest.position : transform.position + Vector3.up;
    }

    private void EnterHurtState()
    {
        ClearBlockStateForReaction();
        swing = 0;
        queuedMobileSwing = false;
        anim.speed = 1f;

        // Layer 1 masks and overrides the upper body for sword/block poses. Leaving it at
        // full weight hides most of the full-body Hurted clip playing on layer 0.
        if (anim.layerCount > 1 && !hurtUpperLayerSuppressed)
        {
            hurtUpperLayerWeight = anim.GetLayerWeight(1);
            anim.SetLayerWeight(1, 0f);
            hurtUpperLayerSuppressed = true;
        }
        hurtStateEntered = false;
        hurtStateEntryGrace = 0.35f;
        anim.CrossFade("Hurted", 0f, 0);
    }

    private void UpdateHurtLayerSuppression()
    {
        if (!hurtUpperLayerSuppressed || anim == null) return;
        AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
        bool currentIsHurted = current.IsName("Hurted");
        bool nextIsHurted = anim.IsInTransition(0)
                            && anim.GetNextAnimatorStateInfo(0).IsName("Hurted");

        // CrossFade is applied by Animator after this script frame. Do not mistake the
        // old state visible in that frame for Hurted having already completed.
        if (!hurtStateEntered)
        {
            if (currentIsHurted || nextIsHurted)
            {
                hurtStateEntered = true;
                return;
            }

            // A guard is allowed during the attack-disabled window. If its transition
            // wins before Animator evaluates CrossFade(Hurted), the old code waited for a
            // Hurted state that could never arrive and kept the player translucent forever.
            hurtStateEntryGrace -= Time.unscaledDeltaTime;
            if (hurtStateEntryGrace > 0f)
                return;

            FinishHurtLayerSuppression(true);
            return;
        }

        if (nextIsHurted || (currentIsHurted && current.normalizedTime < 1f)) return;

        FinishHurtLayerSuppression(true);
    }

    private void FinishHurtLayerSuppression(bool returnToIdle)
    {
        ClearBlockStateForReaction();
        swing = 0;
        if (returnToIdle)
        {
            anim.CrossFade("Idle", 0f, 0);
            if (anim.layerCount > 1)
                anim.CrossFade("Idle", 0f, 1);
        }
        if (anim.layerCount > 1)
            anim.SetLayerWeight(1, hurtUpperLayerWeight);
        hurtUpperLayerSuppressed = false;
        hurtStateEntered = false;
        hurtStateEntryGrace = 0f;

        // Restore immediately instead of waiting for the next Update. This also covers a
        // simultaneous kill/hit where another combat callback changes Animator state later
        // in the same frame.
        if (!rootPhysicsKnockback && !IsReactionStateActive())
            RestoreReactionVisualMaterials();
    }

    private void ClearBlockStateForReaction()
    {
        guard = 0;
        ClearTelegraphedGuard();
        if (anim == null) return;
        // Clear both competing requests. Setting ExitBlock here can itself remain queued
        // while Hurted wins, then fire much later, so reaction states own the transition
        // directly and leave no block trigger pending.
        anim.ResetTrigger("EnterBlock");
        anim.ResetTrigger("ExitBlock");
    }

    public void EnterGuardBrokenState()
    {
        if (health < 0 || anim == null) return;
        ClearBlockStateForReaction();
        swing = 0;
        queuedMobileSwing = false;
        anim.speed = 1f;
        if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
            anim.CrossFade("Guarded", 0f, 0);
    }

    private void FixedUpdate()
    {
        if (rootPhysicsKnockback)
        {
            UpdateRootPhysicsKnockback();
            return;
        }

        UpdateFacingRotation();

        // A valid root agent owns ordinary movement, including slopes and stairs.
        if (nav != null && nav.enabled && nav.isOnNavMesh)
            return;

        if (characterController == null || !characterController.enabled)
            return;

        Vector3 localMove = new(movementInput.x, 0f, movementInput.y);
        Vector3 desiredDirection = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * localMove;
        bool grounded = characterController.isGrounded;
        Vector3 targetHorizontal = desiredDirection * movementSpeed;
        float acceleration = grounded ? groundAcceleration : airAcceleration;
        movementVelocity = Vector3.MoveTowards(movementVelocity, targetHorizontal,
            acceleration * Time.fixedDeltaTime);

        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        else
            verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        CollisionFlags flags = characterController.Move(
            (movementVelocity + Vector3.up * verticalVelocity) * Time.fixedDeltaTime);
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = -2f;
    }

    public void ApplyCombatKnockback(Vector3 direction)
    {
        if (knockbackDistance <= 0f) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || rigid == null) return;

        knockbackDirection = direction.normalized;
        knockbackStartPosition = transform.position;
        knockbackTimer = 0f;
        movementVelocity = Vector3.zero;
        verticalVelocity = 0f;
        rootPhysicsKnockback = true;

        if (nav != null && nav.enabled)
        {
            if (nav.isOnNavMesh)
            {
                nav.isStopped = true;
                nav.ResetPath();
            }
            nav.enabled = false;
        }
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
    }

    private void UpdateRootPhysicsKnockback()
    {
        knockbackTimer += Time.fixedDeltaTime;
        Vector3 travelled = rigid.position - knockbackStartPosition;
        travelled.y = 0f;
        Vector3 velocity = rigid.linearVelocity;
        velocity.y = 0f;
        bool reachedDistance = travelled.magnitude >= knockbackDistance;
        bool stopped = knockbackTimer > 0.2f
            && velocity.sqrMagnitude <= knockbackStopSpeed * knockbackStopSpeed;
        if (!reachedDistance && !stopped && knockbackTimer < knockbackMaxTime) return;

        if (reachedDistance)
        {
            Vector3 clamped = knockbackStartPosition + knockbackDirection * knockbackDistance;
            clamped.y = rigid.position.y;
            rigid.position = clamped;
        }
        FinishRootPhysicsKnockback();
    }

    private void FinishRootPhysicsKnockback()
    {
        rigid.linearVelocity = Vector3.zero;
        rigid.angularVelocity = Vector3.zero;
        rigid.linearDamping = 0f;
        rigid.isKinematic = true;
        rigid.detectCollisions = false;
        rigid.constraints = RigidbodyConstraints.FreezeAll;
        if (movementCapsule != null) movementCapsule.enabled = false;
        RestoreNavigationAfterKnockback();
        rootPhysicsKnockback = false;
    }

    private void ConfigurePhysicalRoot()
    {
        if (body != null && body.TryGetComponent(out Rigidbody balanceBody))
        {
            balanceBody.useGravity = false;
            balanceBody.isKinematic = true;
        }
    }

    private NavMeshAgent ResolveRootAgent()
    {
        NavMeshAgent rootAgent = GetComponent<NavMeshAgent>();
        if (rootAgent == null)
            Debug.LogError($"{name}: NavMeshAgent must be serialized on the PlayerWorker root.", this);
        return rootAgent;
    }

    private void ConfigureNavigationAgent()
    {
        if (nav == null) return;

        // Do not let two independent movement components write the transform.
        if (characterController != null)
            characterController.enabled = nav.isOnNavMesh ? false : true;
    }

    private void UpdateAutomaticNavigation()
    {
        bool movementLockedByAnimation = anim != null
            && (anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted")
                || anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"));
        if (rootPhysicsKnockback || health < 0 || isEnd || movementLockedByAnimation)
        {
            StopNavigation();
            return;
        }

        navigationTargetRefreshTimer -= Time.deltaTime;
        if (navigationTargetRefreshTimer <= 0f || !IsValidNavigationTarget(navigationTarget))
        {
            navigationTargetRefreshTimer = navigationTargetRefreshInterval;
            // Keep a valid target instead of switching between similarly close enemies every
            // refresh; target swapping would repeatedly cancel the combat-distance hold.
            GameObject refreshedTarget = IsValidNavigationTarget(navigationTarget)
                ? navigationTarget
                : navigationTarget != null
                    ? FindAdjacentEnemy(navigationTarget.transform.position - transform.position)
                    : FindFrontEnemyForNavigation();
            if (refreshedTarget != navigationTarget)
                holdingCombatDistance = false;
            navigationTarget = refreshedTarget;
            AssignCombatTarget(navigationTarget);
        }

        if (!IsValidNavigationTarget(navigationTarget))
        {
            holdingCombatDistance = false;
            StopNavigation();
            return;
        }

        Vector3 toEnemy = navigationTarget.transform.position - transform.position;
        toEnemy.y = 0f;
        if (toEnemy.sqrMagnitude > 0.0001f)
        {
            desiredFacingRotation = Quaternion.LookRotation(toEnemy.normalized, Vector3.up);
            hasFacingTarget = true;
        }
        StopNavigation();
        movementInput = Vector2.zero;
        return;
        float enemyDistance = toEnemy.magnitude;
        if (holdingCombatDistance)
        {
            if (enemyDistance < navigationResumeDistance)
            {
                StopNavigation();
                return;
            }
            holdingCombatDistance = false;
        }
        else if (enemyDistance <= navigationStoppingDistance)
        {
            holdingCombatDistance = true;
            StopNavigation();
            return;
        }

        Vector3 approachDirection = enemyDistance > 0.001f
            ? toEnemy / enemyDistance
            : transform.forward;
        Vector3 approachPoint = navigationTarget.transform.position
            - approachDirection * navigationStoppingDistance;

        if (nav != null && nav.enabled)
        {
            if (nav.isOnNavMesh)
            {
                if (characterController != null && characterController.enabled)
                    characterController.enabled = false;
                nav.isStopped = false;
                if (NavMesh.SamplePosition(approachPoint, out NavMeshHit approachHit, 1.5f, nav.areaMask))
                    nav.SetDestination(approachHit.position);
                else
                    nav.SetDestination(approachPoint);
                return;
            }
        }

        // Scene editing can momentarily leave the root off the baked mesh. Keep a safe
        // grounded fallback instead of accepting keyboard input or letting the actor fall.
        if (characterController != null && !characterController.enabled)
            characterController.enabled = true;
        Vector3 toApproachPoint = approachPoint - transform.position;
        toApproachPoint.y = 0f;
        Vector3 fallback = toApproachPoint.magnitude <= 0.1f
            ? Vector3.zero
            : toApproachPoint.normalized * movementSpeed;
        Vector3 localFallback = transform.InverseTransformDirection(fallback);
        movementInput = new Vector2(localFallback.x, localFallback.z) / Mathf.Max(0.01f, movementSpeed);
    }

    private GameObject FindFrontEnemyForNavigation()
    {
        GameObject closest = null;
        float bestScore = float.NegativeInfinity;
        foreach (GameObject candidate in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            if (!IsCombatReadyTarget(candidate)) continue;
            Vector3 delta = candidate.transform.position - transform.position;
            delta.y = 0f;
            float distance = Mathf.Max(0.01f, delta.magnitude);
            float score = Vector3.Dot(transform.forward, delta / distance) * 100f - distance;
            if (score <= bestScore) continue;
            closest = candidate;
            bestScore = score;
        }
        return closest;
    }

    private GameObject FindAdjacentEnemy(Vector3 previousDirection)
    {
        previousDirection.y = 0f;
        if (previousDirection.sqrMagnitude < 0.0001f)
            previousDirection = lastTargetDirection.sqrMagnitude > 0.0001f
                ? lastTargetDirection
                : transform.forward;
        previousDirection.Normalize();

        GameObject adjacent = null;
        float smallestAngle = float.PositiveInfinity;
        float nearestDistance = float.PositiveInfinity;
        foreach (GameObject candidate in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            if (!IsCombatReadyTarget(candidate)) continue;
            Vector3 direction = candidate.transform.position - transform.position;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < 0.001f) continue;
            float angle = Mathf.Abs(Vector3.SignedAngle(previousDirection, direction / distance, Vector3.up));
            if (angle > smallestAngle + 0.01f
                || (Mathf.Abs(angle - smallestAngle) <= 0.01f && distance >= nearestDistance))
                continue;
            adjacent = candidate;
            smallestAngle = angle;
            nearestDistance = distance;
        }
        return adjacent ?? FindFrontEnemyForNavigation();
    }

    private void AssignCombatTarget(GameObject target)
    {
        // A freshly pooled enemy may already be alive and tagged while it is still travelling
        // to its formation position. Do not publish it as ent until it is actually combat
        // ready; the navigation refresh retries automatically after it arrives.
        if (target != null && readinessTarget != target && !IsCombatReadyTarget(target))
            target = null;

        bool targetChanged = readinessTarget != target;
        readinessTarget = target;
        ent = target;
        EnemyWorker.SetFocusedEnemy(target);
        if (targetChanged)
        {
            focusedEnemyAttackUnlockTime = Time.unscaledTime + targetSwitchAttackGrace;
            focusedEnemyFacingStableTime = 0f;
        }
        if (target == null)
        {
            entAnim = null;
            onTarget = false;
            return;
        }

        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            lastTargetDirection = direction.normalized;
        entAnim = target.GetComponent<Animator>()
            ?? target.GetComponentInChildren<Animator>(true);
        onTarget = entAnim != null;
    }

    private void UpdateFocusedEnemyAttackReadiness()
    {
        if (ent == null || health < 0)
        {
            focusedEnemyFacingStableTime = 0f;
            return;
        }

        Vector3 toTarget = ent.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            focusedEnemyFacingStableTime = 0f;
            return;
        }

        float angle = Vector3.Angle(transform.forward, toTarget.normalized);
        if (Time.unscaledTime >= focusedEnemyAttackUnlockTime
                && angle <= targetFacingReadyAngle)
            focusedEnemyFacingStableTime += Time.unscaledDeltaTime;
        else
            focusedEnemyFacingStableTime = 0f;
    }

    public bool CanFocusedEnemyAttack(EnemyWorker enemy)
    {
        if (enemy == null || ent != enemy.gameObject || health < 0)
            return false;
        // Once the player owns the current exchange, the focused enemy may continue its
        // current pose but may not start a competing attack. This prevents a late enemy
        // telegraph from forcing the slowed player into BlockIdle.
        if (IsPlayerAttackSequenceActive())
            return false;
        if (Time.unscaledTime < focusedEnemyAttackUnlockTime
                || focusedEnemyFacingStableTime < targetFacingReadyHold)
            return false;
        if (anim == null) return false;
        AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
        return !state.IsName("Hurted") && !state.IsName("Guarded");
    }

    public bool LocksEnemyActionStart => IsPlayerAttackSequenceActive();

    private bool IsPlayerAttackSequenceActive()
    {
        if (attackSlowTriggered || swing != 0)
            return true;
        if (anim == null)
            return false;
        if (anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
            return true;
        return anim.IsInTransition(0)
               && anim.GetNextAnimatorStateInfo(0).IsName("Attack");
    }

    public void SelectTargetAfterEnemyDefeat(GameObject defeatedEnemy, GameObject adjacentEnemy)
    {
        if (ent != defeatedEnemy && navigationTarget != defeatedEnemy)
            return;

        navigationTarget = IsCombatReadyTarget(adjacentEnemy) ? adjacentEnemy : null;
        navigationTargetRefreshTimer = navigationTargetRefreshInterval;
        holdingCombatDistance = false;
        AssignCombatTarget(navigationTarget);
    }

    private static bool IsValidNavigationTarget(GameObject candidate)
    {
        return candidate != null && candidate.activeInHierarchy && candidate.CompareTag("Enemy");
    }

    private static bool IsCombatReadyTarget(GameObject candidate)
    {
        if (!IsValidNavigationTarget(candidate))
            return false;
        EnemyWorker worker = candidate.GetComponent<EnemyWorker>();
        return worker != null && worker.IsAvailableForCombatFocus;
    }

    private void UpdateNavigationAnimation()
    {
        Vector3 velocity = nav != null && nav.enabled && nav.isOnNavMesh
            ? nav.desiredVelocity
            : new Vector3(movementInput.x, 0f, movementInput.y) * movementSpeed;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        float speed = Mathf.Max(0.01f, movementSpeed);
        anim.SetFloat("mx", Mathf.Clamp(localVelocity.x / speed, -1f, 1f));
        anim.SetFloat("mz", Mathf.Clamp(localVelocity.z / speed, -1f, 1f));
    }

    private void StopNavigation()
    {
        if (nav != null && nav.enabled && nav.isOnNavMesh)
        {
            nav.isStopped = true;
            nav.ResetPath();
        }
        if (anim != null)
        {
            anim.SetFloat("mx", 0f);
            anim.SetFloat("mz", 0f);
        }
    }

    private void RestoreNavigationAfterKnockback()
    {
        if (nav != null)
        {
            nav.enabled = true;
            if (nav.isOnNavMesh)
            {
                nav.isStopped = true;
                nav.ResetPath();
                movementInput = Vector2.zero;
                navigationTargetRefreshTimer = 0f;
                if (characterController != null) characterController.enabled = false;
                return;
            }
        }
        if (characterController != null) characterController.enabled = true;
    }

    private void UpdateFacingRotation()
    {
        if (!hasFacingTarget)
            return;

        float t = 1f - Mathf.Exp(-facingLerpSpeed * Time.fixedDeltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredFacingRotation, t);
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

        characterController.stepOffset = Mathf.Min(maxStepHeight,
            Mathf.Max(0f, characterController.height - characterController.radius * 2f));
        characterController.slopeLimit = maxSlopeAngle;
        characterController.skinWidth = Mathf.Max(0.01f, movementSkin);
        characterController.minMoveDistance = 0f;
        characterController.enableOverlapRecovery = true;
    }

    [Header("Rig refs")]
    [SerializeField] MultiParentConstraint weaponBlend; // Rig_Weapon에 붙은 컴포넌트
    [SerializeField] TwoBoneIKConstraint rightArmIK;    // Rig_Hands
    [SerializeField] TwoBoneIKConstraint leftArmIK;     // Rig_Hands

    private void Start()
    {
        if (miiMotion)
        {
            weaponBlend.weight = 0f;
            rightArmIK.weight = 0f;
            leftArmIK.weight = 0f;
            return;
        }
    }
    private void LateUpdate()
    {
        if (health > -1
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted")
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
        {
            Transform enemyHead = entAnim != null && entAnim.isHuman
                ? entAnim.GetBoneTransform(HumanBodyBones.Head)
                : null;
            Vector3 target = enemyHead != null
                ? enemyHead.position
                : transform.position + transform.forward * 5f + Vector3.up * 1.6f;
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
        // The modular humanoid head bone does not use the character root's forward axis.
        // Build the final orientation from the authored per-model correction instead of
        // preserving the current animation's pitch. Otherwise the selected attacker keeps
        // the Attack clip's downward head pitch and appears to stare at the floor.
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

    public void ReceiveSwordHit(EnemyWorker attacker, Collider weaponCollider, Collider bodyCollider,
        Vector3 hitPoint, int sourceAttackSequence)
    {
        if (attacker != null && resolvedIncomingAttacker == attacker
            && resolvedIncomingAttackSequence == sourceAttackSequence)
            return;
        hitPoint = SwordHitbox.ResolveSurfaceContact(weaponCollider, bodyCollider, hitPoint);
        if (attacker != null && attacker.IsAttackHitActive
            && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted"))
        {
            resolvedIncomingAttacker = attacker;
            resolvedIncomingAttackSequence = sourceAttackSequence;
            bool blocked = CanBlockTelegraphedAttack(attacker);
            ResolveTelegraphedAttack(attacker);
            if (!blocked)
            {
                if (health > 0)
                {
                    health--;
                    Vector3 hitpoint = Quaternion.Euler(new Vector3(0, -transform.rotation.eulerAngles.y, 0)) * (hitPoint - (transform.position + Vector3.up));
                    anim.SetFloat("cx", Mathf.Clamp(hitpoint.x, -1, 1));
                    anim.SetFloat("cy", Mathf.Clamp(hitpoint.y, 0, 1));
                    EnterHurtState();
                    Vector3 impactDirection = transform.position - attacker.transform.position;
                    impactDirection.y = 0f;
                    ApplyCombatKnockback(impactDirection.normalized * 1.5f);
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                }
                else
                {
                    if (guard == 1)
                    {
                        guard = 0;
                        //sword.localPosition = swordOffset;//new Vector3(-0.38f, 0.514f, -0.027f);
                        //sword.position -= transform.forward;
                        anim.CrossFade("Idle", 0f, 0);
                    }
                    health = -1;
                    anim.enabled = false;
                    rig.weight = 0;
                    if (characterController != null)
                        characterController.enabled = false;
                    Swordplay.Visuals.BrokenEdgeCombatImpactVfx.SpawnHit(transform, hitPoint);
                    Destroy(legAnim);
                    Destroy(GetComponent<Collider>());
                    Destroy(rigid);
                    CombatImpactAudio.PlayExplosionAtPoint(hitPoint);
                    EnemyMotion.player = null;
                    Invoke("Dissolve", 4);
                    Invoke("OverUI", 4);
                    isEnd = true;
                    EnemyWorker.RestoreAllVisuals();
                    Swordplay.UI.MedievalCursorController.ShowMenuCursor();
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
                Animator attackerAnimator = attacker.GetComponent<Animator>();
                if (attackerAnimator != null
                    && !attackerAnimator.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
                    attackerAnimator.CrossFade("Guarded", 0f, 0);
            }
        }
    }

    private bool IsActivelyGuarding()
    {
        if (guard != 1 || anim == null)
            return false;

        for (int layer = 0; layer < anim.layerCount; layer++)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).IsName("BlockIdle"))
                return true;
            if (anim.IsInTransition(layer)
                && anim.GetNextAnimatorStateInfo(layer).IsName("BlockIdle"))
                return true;
        }
        return false;
    }

    private void ClearUI()
    {
        EnemyWorker.RestoreAllVisuals();
        header.text = "클리어!!";
        endUI.gameObject.SetActive(true);
        Swordplay.UI.MedievalCursorController.ShowMenuCursor();
    }

    private void OverUI()
    {
        EnemyWorker.RestoreAllVisuals();
        header.text = "게임오버!!";
        endUI.gameObject.SetActive(true);
        Swordplay.UI.MedievalCursorController.ShowMenuCursor();
    }

    private void OnDisable()
    {
        RestoreReactionVisualMaterials();
        EnemyWorker.RestoreAllVisuals();
    }

    private void OnApplicationQuit()
    {
        RestoreReactionVisualMaterials();
        EnemyWorker.RestoreAllVisuals();
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
