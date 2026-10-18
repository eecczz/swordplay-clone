using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
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
    private bool arenaGameplayEnabled = true;
    private float telegraphedGuardTimer;
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
    [Header("Attack hit detection")]
    [SerializeField, Range(0f, 1f)] private float attackHitStart = 0f;
    [SerializeField, Range(0f, 1.2f)] private float attackHitEnd = 1.05f;
    [Header("Movement collision")]
    [SerializeField] private float maxStepHeight = 0.45f;
    [SerializeField, Range(1f, 80f)] private float maxSlopeAngle = 55f;
    [SerializeField] private float movementSkin = 0.03f;
    [SerializeField, Min(1f)] private float groundAcceleration = 35f;
    [SerializeField, Min(1f)] private float airAcceleration = 8f;
    [Header("Facing and knockback")]
    [SerializeField, Min(0.1f)] private float facingLerpSpeed = 10f;
    [SerializeField, Min(0.5f)] private float knockbackDistance = 5f;
    [SerializeField, Min(0.1f)] private float knockbackSlideDamping = 4.5f;
    [SerializeField, Min(0.01f)] private float knockbackStopSpeed = 0.15f;
    [SerializeField, Min(0.2f)] private float knockbackMaxTime = 3f;
    private Quaternion desiredFacingRotation;
    private bool hasFacingTarget;
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
    public int AttackSequence => attackSequence;
    public bool IsAttackHitActive => health > -1 && attackSequence > 0
        && CombatDirectionUtility.IsAttackStateActive(anim, attackHitStart, attackHitEnd);
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
        Cursor.visible = false;
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
            SwordHitbox.Configure(psword.gameObject, this);
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
                Cursor.visible = true;
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

        movementInput = Vector2.zero;
        UpdateAutomaticNavigation();
        if(swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            anim.SetFloat("tx", tx);
            anim.SetFloat("ty", ty);
        }
        if (inputMode == InputMode.Mobile)
            UpdateMobileInput();
        else
        {
            tx += Input.GetAxis("Mouse X") * sensitivity;
            ty += Input.GetAxis("Mouse Y") * sensitivity;
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
        bool manualGuard = inputMode == InputMode.PC && Input.GetMouseButton(1);
        if (telegraphedGuardTimer > 0f)
            telegraphedGuardTimer -= Time.deltaTime;
        if (!manualGuard && telegraphedGuardTimer <= 0f)
        {
            if (guard == 1)
            {
                guard = 0;
                anim.CrossFade("Idle", 0f, 0);
                anim.CrossFade("Idle", 0f, 1);
                //sword.localPosition = swordOffset;//new Vector3(-0.38f, 0.514f, -0.027f);
            }
        }
        else if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
        {
            if (guard == 0)
            {
                guard = 1;
                anim.CrossFade("BlockIdle", 0f, 0);
                anim.CrossFade("BlockIdle", 0f, 1);
                //sword.localPosition = new Vector3(sword.localPosition.x + 1, sword.localPosition.y - 2, sword.localPosition.z);
                //sword.position += transform.forward;
                SoundManager.Instance.SFXPlay("Shield");
            }
        }
        float curScore = (new Vector3(0, 60) - new Vector3(tx, ty)).magnitude;
        float goalScore = preScore - curScore;
        float testScore = (new Vector3(0, 0) - new Vector3(tx, ty)).magnitude;

        anim.SetFloat("time", 1 - testScore / 180);
        preScore = curScore;
        bool wantsSwing = inputMode == InputMode.Mobile ? queuedMobileSwing : goalScore > canSwing;
        bool mobilePoseReady = inputMode != InputMode.Mobile || Vector2.Distance(new Vector2(tx, ty), mobileTargetPose) <= mobileSwingPoseTolerance;
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && wantsSwing && mobilePoseReady && guard == 0 && swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            queuedMobileSwing = false;
            attackDirection = new Vector2(tx, ty);
            attackSequence++;
            anim.CrossFade("Attack", 0f, 0);
            anim.CrossFade("Attack", 0f, 1);
            SoundManager.Instance.SFXPlay("Swing1");
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
            mobileTargetPose = new Vector2(
                Mathf.Lerp(-180f, 180f, touch.position.x / Mathf.Max(1f, Screen.width)),
                Mathf.Lerp(-30f, 150f, touch.position.y / Mathf.Max(1f, Screen.height)));
            float swipeSpeed = touch.deltaPosition.magnitude / Mathf.Max(0.001f, Time.unscaledDeltaTime);
            if (touch.phase == TouchPhase.Moved && swipeSpeed >= mobileSwipeSpeed)
                queuedMobileSwing = true;
        }
        float t = 1f - Mathf.Exp(-mobilePoseLerpSpeed * Time.deltaTime);
        Vector2 pose = Vector2.Lerp(new Vector2(tx, ty), mobileTargetPose, t);
        tx = pose.x;
        ty = pose.y;
    }

    public void BeginTelegraphedBlock(Vector2 incomingDirection, float duration)
    {
        if (health < 0 || swing != 0) return;
        tx = incomingDirection.x;
        ty = incomingDirection.y;
        telegraphedGuardTimer = Mathf.Max(telegraphedGuardTimer, duration);
        if (guard == 0)
        {
            guard = 1;
            anim.CrossFade("BlockIdle", 0.08f, 0);
            anim.CrossFade("BlockIdle", 0.08f, 1);
        }
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
            if (!IsValidNavigationTarget(candidate)) continue;
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
            if (!IsValidNavigationTarget(candidate)) continue;
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
        ent = target;
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

    private static bool IsValidNavigationTarget(GameObject candidate)
    {
        return candidate != null && candidate.activeInHierarchy && candidate.CompareTag("Enemy");
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
        hitPoint = SwordHitbox.ResolveSurfaceContact(weaponCollider, bodyCollider, hitPoint);
        if (attacker != null && attacker.IsAttackHitActive
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
                    Cursor.visible = true;
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

    private void ClearUI()
    {
        header.text = "클리어!!";
        endUI.gameObject.SetActive(true);
    }

    private void OverUI()
    {
        header.text = "게임오버!!";
        endUI.gameObject.SetActive(true);
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
