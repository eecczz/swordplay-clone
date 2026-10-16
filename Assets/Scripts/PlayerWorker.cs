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
    [SerializeField] private float movementSpeed = 5f;
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

    /*3d character application
     head's up, forward axis compare and edit animation rigging's head ik
     add sword, fit the position, add lh target and fitting, apply Hitter's target reference
     hand ik enable and let left arm to follow lh target
     add components, legs animator - ik hint mode -> onGoal*/
    private void Awake()
    {
        ConfigurableJoint legacyRootJoint = GetComponent<ConfigurableJoint>();
        if (legacyRootJoint != null)
            Destroy(legacyRootJoint);

        player = transform;
        Time.timeScale = 1.0f;
        endUI.gameObject.SetActive(false);
        isEnd = false;
        Cursor.visible = false;
        anim = animatorOverride != null ? animatorOverride : GetComponent<Animator>();
        playerAnimator = anim;
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
        float distance = Mathf.Infinity;
        Vector3 position = transform.position;
        foreach (GameObject go in gos)
        {
            Vector3 diff = go.transform.position - position;
            float curDistance = diff.sqrMagnitude;
            if (curDistance < distance && IsEnemyInView(go.gameObject))
            {
                closest = go;
                ent = closest;
                distance = curDistance;
                if (closest != null)
                {
                    if (ma.data.sourceObjects[0].transform != closest.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head))
                    {
                        var data = ma.data.sourceObjects;
                        data.SetTransform(0, closest.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head));
                        ma.data.sourceObjects = data;
                        rb.Build();
                    }
                }
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
        movementInput = Vector2.zero;
        //nav.SetDestination(des.transform.position);
        if(swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
        {
            anim.SetFloat("tx", tx);
            anim.SetFloat("ty", ty);
        }
        tx += Input.GetAxis("Mouse X") * sensitivity;
        ty += Input.GetAxis("Mouse Y") * sensitivity;
        tx = Mathf.Clamp(tx, -180, 180);
        ty = Mathf.Clamp(ty, -30, 150);
        IsGoal();
        if (isEnd)
            Time.timeScale = Mathf.Lerp(Time.timeScale, 0.5f, 0.01f);
        if (!onTarget && ma.data.sourceObjects[0].transform != crosshead)
        {
            var data = ma.data.sourceObjects;
            data.SetTransform(0, crosshead);
            ma.data.sourceObjects = data;
            rb.Build();
        }
        /*else if(onTarget && ma.data.sourceObjects[0].transform == crosshead)
        {
            var data = ma.data.sourceObjects;
            data.SetTransform(0, entAnim.GetBoneTransform(HumanBodyBones.Head));
            ma.data.sourceObjects = data;
            rb.Build();
        }*/
        if (health > -1)
        {
            //player movement
            if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
            {
                float x = Input.GetAxis("Horizontal"); // 수평 입력
                float z = Input.GetAxis("Vertical"); // 수직 입력
                anim.SetFloat("mx", x);
                anim.SetFloat("mz", z);
                movementInput = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
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
        if (!Input.GetMouseButton(1))
        {
            if (guard == 1)
            {
                guard = 0;
                anim.CrossFade("Idle", 0.25f, 0);
                anim.CrossFade("Idle", 0.25f, 1);
                //sword.localPosition = swordOffset;//new Vector3(-0.38f, 0.514f, -0.027f);
            }
        }
        else if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
        {
            if (guard == 0)
            {
                guard = 1;
                anim.CrossFade("BlockIdle", 0.25f, 0);
                anim.CrossFade("BlockIdle", 0.25f, 1);
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
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && goalScore > canSwing && guard == 0 && swing == 0 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack")) //������
        {
            attackDirection = new Vector2(tx, ty);
            attackSequence++;
            anim.CrossFade("Attack", 0.25f, 0);
            anim.CrossFade("Attack", 0.25f, 1);
            SoundManager.Instance.SFXPlay("Swing1");
            ma.weight = 0;
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
                ma.weight = 1;
            }
        }
        if (!onTarget)
        {
            if (FindClosestEnemy()!= null)
            {
                entAnim = ent.GetComponent<Animator>();
                onTarget = true;
            }
        }
        if (onTarget && ent != null)
        {
            if (ent.gameObject.tag != "Enemy" || (ent.transform.position - transform.position).magnitude > 10f)
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
        Camera.main.transform.position = new Vector3(anim.GetBoneTransform(HumanBodyBones.Spine).position.x, anim.GetBoneTransform(HumanBodyBones.Hips).position.y + camPos.y, anim.GetBoneTransform(HumanBodyBones.Spine).position.z) - transform.forward * camPos.z;
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

    private void FixedUpdate()
    {
        if (rootPhysicsKnockback)
        {
            UpdateRootPhysicsKnockback();
            return;
        }

        if (characterController == null || !characterController.enabled)
            return;

        UpdateFacingRotation();

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
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || rigid == null) return;

        knockbackDirection = direction.normalized;
        knockbackStartPosition = transform.position;
        knockbackTimer = 0f;
        movementVelocity = Vector3.zero;
        verticalVelocity = 0f;
        rootPhysicsKnockback = true;

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
        if (characterController != null) characterController.enabled = true;
        rootPhysicsKnockback = false;
    }

    private void ConfigurePhysicalRoot()
    {
        foreach (NavMeshAgent childAgent in GetComponentsInChildren<NavMeshAgent>(true))
            childAgent.enabled = false;

        if (body != null && body.TryGetComponent(out Rigidbody balanceBody))
        {
            balanceBody.useGravity = false;
            balanceBody.isKinematic = true;
        }
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
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && (!entAnim || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack")))
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
                    anim.CrossFade("Hurted", 0.25f, 0);
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
                        anim.CrossFade("Idle", 0.25f, 0);
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
                    attackerAnimator.CrossFade("Guarded", 0.25f, 0);
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
