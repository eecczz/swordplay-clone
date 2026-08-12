using FIMSpace.FProceduralAnimation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

public class EnemyWorker : MonoBehaviour
{
    private Animator anim;
    private NavMeshAgent nav;
    private Animator playerAnim;
    [SerializeField] private Transform crosshead;
    public static Transform psword;
    [SerializeField] private Transform sword;
    [SerializeField] private Vector3 swordOffset;
    private float sensitivity = 0.01f;
    private float tx, ty, rtx, rty;
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
    private RigBuilder rb;
    [SerializeField] private LayerMask layermask;
    [SerializeField] private bool symmetricMotion;
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

    // Start is called before the first frame update
    private void Start()
    {
        // 타이머 초기화(초 단위)
        cool1Timer = cool1Frames / refFps; // ≈ 1.67s
        coolTimer = 0f;
        guardTimer = -1f;

        // 보간 속도 상수(초당)
        kFast = -refFps * Mathf.Log(0.9f);  // per-frame 0.1과 유사한 반응
        kSlow = -refFps * Mathf.Log(0.99f); // per-frame 0.01과 유사한 반응
        anim = GetComponent<Animator>();
        nav = GetComponent<NavMeshAgent>();
        hitVFX = Resources.Load<GameObject>($"Prefab/HitVFX");
        shieldVFX = Resources.Load<GameObject>($"Prefab/ShieldVFX");
        audioSource = GetComponent<AudioSource>();
        playerAnim = PlayerWorker.player.GetComponent<Animator>();
        rigid = GetComponent<Rigidbody>();
        rig = GetComponentInChildren<Rig>();
        legAnim = GetComponent<LegsAnimator>();
        rb = GetComponent<RigBuilder>();
        var data = GetComponentInChildren<MultiAimConstraint>().data.sourceObjects;
        data.SetTransform(0, playerAnim.GetBoneTransform(HumanBodyBones.Head));
        ma.data.sourceObjects = data;
        rb.Build();
        rightTemp = new GameObject("RightTemp");
        leftTemp = new GameObject("LeftTemp");
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

    private float GetGroundHeight(Vector3 position)
    {
        RaycastHit hit;

        // 플레이어 앞에 레이캐스트를 발사하여 바닥을 감지
        if (Physics.Raycast(position, Vector3.down, out hit))
        {
            // 레이캐스트가 바닥과 충돌하면 그 충돌 지점의 y값을 반환
            return hit.point.y;
        }

        // 바닥이 감지되지 않으면 현재 위치의 y값을 그대로 반환
        return position.y;
    }

    private bool CanMoveToPosition(Vector3 position)
    {
        // Raycast를 사용하여 이동할 위치에 벽이 있는지 확인
        RaycastHit hit;
        Vector3 direction = position - transform.position;

        // 벽이 있는 경우, 이동하지 않도록 false 반환
        if (Physics.Raycast(anim.GetBoneTransform(HumanBodyBones.Hips).position, direction, out hit, direction.magnitude + 2.5f, layermask))
            return false;

        return true; // 벽이 없으면 이동 가능
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
        if (PlayerWorker.player != null && swing == 0 && health > -1)
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

        // 이동(거리 조건은 초당 검사)
        if (health > -1)
        {
            bool inAttack = anim.GetCurrentAnimatorStateInfo(0).IsName("Attack");
            float dist = (PlayerWorker.player ? (PlayerWorker.player.position - transform.position).magnitude : Mathf.Infinity);

            // 원래 조건 괄호가 모호했음 → 의도대로 괄호 보강
            if ((!inAttack && PlayerWorker.player == null) || dist <= 4f || dist >= 6f)
            {
                Vector3 dir = (dist >= 4f ? transform.forward : -transform.forward);
                Vector3 targetPos = transform.position + dir * movementSpeed * dt;
                if (CanMoveToPosition(targetPos))
                {
                    anim.SetFloat("mz", dist >= 4f ? 1f : -1f);
                    float groundY = GetGroundHeight(targetPos + Vector3.up);
                    transform.position = new Vector3(targetPos.x, Mathf.Lerp(targetPos.y, groundY, 0.1f), targetPos.z);
                }
            }
        }

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
            if (guard == 0)
            {
                rtx = Random.Range(-180f, 180f);
                rty = Random.Range(-30f, 150f);
            }
            else
            {
                rtx = Mathf.Clamp(rtx, -180f, 180f);
                rty = Mathf.Clamp(rty, -30f, 150f);
            }
        }

        // -------- cool: 공격 트리거 타이머(초) --------
        if (coolTimer > 0f) coolTimer = Mathf.Max(0f, coolTimer - dt);

        if (coolTimer <= 0f && health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
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

                // 가드 포즈 즉시 보정(프레임 독립)
                sword.localPosition = new Vector3(sword.localPosition.x + 1f,
                                                  sword.localPosition.y - 2f,
                                                  sword.localPosition.z);
                sword.position += transform.forward; // 월드 전진 1m
            }
        }

        if (guardTimer == 0f)
        {
            coolTimer = RandomRangeFramesToSeconds(coolFramesRange);
            guardTimer = -1f;
            guard = 0;
            anim.CrossFade("Idle", 0f, 0);
            sword.localPosition = swordOffset;
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
    }

    // === 유틸 ===
    float RandomRangeFramesToSeconds(Vector2Int framesRange)
    {
        int f = Random.Range(framesRange.x, framesRange.y + 1);
        return f / refFps;
    }


    private void LateUpdate()
    {
        if (health > -1 && !anim.GetCurrentAnimatorStateInfo(0).IsName("Hurted") && (!playerAnim || anim.GetCurrentAnimatorStateInfo(0).IsName("Attack")))
        {
            anim.GetBoneTransform(HumanBodyBones.Head).rotation = Quaternion.Euler(headRotOffset + new Vector3(0, transform.rotation.eulerAngles.y, 0));
        }
        if (symmetricMotion)
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
            if (guard == 0)
            {
                if (health > 0)
                {
                    health--;
                    Vector3 hitpoint = Quaternion.Euler(new Vector3(0, -transform.rotation.eulerAngles.y, 0)) * (collision.contacts[0].point - (transform.position + Vector3.up));
                    anim.SetFloat("cx", Mathf.Clamp(hitpoint.x, -1, 1));
                    anim.SetFloat("cy", Mathf.Clamp(hitpoint.y, 0, 1));
                    anim.CrossFade("Hurted", 0, 0);
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
                        sword.localPosition = swordOffset;
                    }
                    health = -1;
                    anim.enabled = false;
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
            else if (guard == 1)
            {
                GameObject shield = Instantiate(shieldVFX, collision.contacts[0].point, Quaternion.LookRotation(transform.forward));
                Destroy(shield, 0.3f);
                SoundManager.Instance.SFXPlay("Shield Impact 2");
                PlayerWorker.player.position -= PlayerWorker.player.forward;
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
