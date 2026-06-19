using FIMSpace.FProceduralAnimation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using XftWeapon;

public class PlayerMotion : MonoBehaviour
{
    public Transform jointParent, joint, body, crosshead;
    Collider sword;
    public static ConfigurableJoint psword;
    Animator anim;
    NavMeshAgent nav;
    public float sensitivity = 5;
    public float canSwing = 1;
    public static float tx, ty;
    public static int swing, guard;
    public static bool lr;
    public AudioClip[] clips;
    public GameObject hitVFX, shieldVFX;
    public static GameObject ent;
    bool onTarget;
    int sign;
    Vector3 vec, knockBack;
    Rigidbody rigid;
    int health = 2;
    RigBuilder rb;
    public MultiAimConstraint ma, hips1, hips2;
    public Material mat1, mat2, handmat;

    private void Start()
    {
        Cursor.visible = false;
        anim = GetComponent<Animator>();
        nav = body.GetComponent<NavMeshAgent>();
        sword = jointParent.GetComponentInChildren<Collider>();
        psword = jointParent.GetComponentInChildren<ConfigurableJoint>();
        rb = GetComponent<RigBuilder>();
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
            if (curDistance < distance)
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

    private void Update()
    {
        nav.Warp(transform.position);
        if (onTarget && !anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && swing == 0)
            body.rotation = Quaternion.Euler(new Vector3(0, Quaternion.LookRotation(ent.transform.position - body.position).eulerAngles.y, 0));
        if (!onTarget && ma.data.sourceObjects[0].transform != crosshead)
        {
            var data = ma.data.sourceObjects;
            data.SetTransform(0, crosshead);
            ma.data.sourceObjects = data;
            rb.Build();
        }
        if (guard == 0)
        {
            hips1.weight = Mathf.Lerp(hips1.weight, 1, 0.01f);
            hips2.weight = Mathf.Lerp(hips2.weight, 1, 0.01f);
        }
        else
        {
            hips1.weight = Mathf.Lerp(hips1.weight, 0, 0.01f);
            hips2.weight = Mathf.Lerp(hips2.weight, 0, 0.01f);
        }
        if (GetComponentInChildren<RigBuilder>().enabled && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") && !anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && health > -1)
        {
            Color color1 = handmat.color;
            if (color1.a != 0f)
            {
                handmat.CopyPropertiesFromMaterial(mat1);
                color1.a = 0.5f;
            }
            handmat.color = color1;
            foreach (Renderer render in GetComponentsInChildren<Renderer>())
            {
                foreach (Material mat in render.materials)
                {
                    Color color = mat.color;
                    if (color.a != 0f)
                    {
                        mat.CopyPropertiesFromMaterial(mat1);
                        color.a = 0.5f;
                    }
                    mat.color = color;
                }
            }
            GetComponentInChildren<Rig>().weight = Mathf.Lerp(GetComponentInChildren<Rig>().weight, 1, 0.01f);
            ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
            var limit0 = joint.lowAngularXLimit;
            limit0.limit = Mathf.Lerp(limit0.limit, 0, 0.01f);
            joint.lowAngularXLimit = limit0;
            var limit1 = joint.highAngularXLimit;
            limit1.limit = Mathf.Lerp(limit1.limit, 0, 0.01f);
            joint.highAngularXLimit = limit1;
            var limit2 = joint.angularZLimit;
            limit2.limit = Mathf.Lerp(limit2.limit, 0, 0.01f);
            joint.angularZLimit = limit2;
        }
        else
        {
            if (!anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
            {
                Color color1 = handmat.color;
                if (color1.a != 0f)
                {
                    handmat.CopyPropertiesFromMaterial(mat2);
                    color1.a = 1;
                }
                handmat.color = color1;
                foreach (Renderer render in GetComponentsInChildren<Renderer>())
                {
                    foreach(Material mat in render.materials)
                    {
                        if (render.GetComponent<DissolveSphere>() == null)
                        {
                            Color color = mat.color;
                            if (color.a != 0f)
                            {
                                mat.CopyPropertiesFromMaterial(mat2);
                                color.a = 1;
                            }
                            mat.color = color;
                        }
                    }
                }
                if(health==-1)
                {
                    foreach (Renderer render in anim.GetBoneTransform(HumanBodyBones.Hips).GetComponentsInChildren<Renderer>())
                    {
                        foreach (Material mat in render.materials)
                        {
                            if (render.GetComponent<DissolveSphere>() == null && mat.shader.FindPropertyIndex("_Color") != -1)
                            {
                                Color color = mat.color;
                                if (color.a != 0f)
                                {
                                    mat.CopyPropertiesFromMaterial(mat2);
                                    color.a = 1;
                                }
                                mat.color = color;
                            }
                        }
                    }
                }
            }
            GetComponentInChildren<Rig>().weight = 0;
        }
        if (health > -1)
        {
            //sword movement
            if (!Input.GetMouseButton(1))
            {
                tx += Input.GetAxis("Mouse X") * sensitivity;
                ty += Input.GetAxis("Mouse Y") * sensitivity;
            }
            else
            {
                tx -= Input.GetAxis("Mouse X") * sensitivity;
                ty -= Input.GetAxis("Mouse Y") * sensitivity;
            }
            if (guard == 0)
            {
                tx = Mathf.Clamp(tx, -180, 180);
                ty = Mathf.Clamp(ty, -30, 150);
            }
            else
            {
                tx = Mathf.Clamp(tx, -90, 90);
                ty = Mathf.Clamp(ty, -30, 90);
            }
            if (tx >= 180)
                tx = -180;
            else if (tx <= -180)
                tx = 180;
        }
        if (!Input.GetMouseButton(1))
        {
            if (guard == 1)
            {
                guard = 0;
            }
            joint.localRotation = Quaternion.Euler(new Vector3(0, 0, 0));
            joint.localPosition = new Vector3(0, 1.5f, 0);
            sword.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, ty * tx / 135 * Mathf.Clamp(ty, 52.5f, 150) / 52.5f));
            Vector3 v3 = Quaternion.AngleAxis(-ty * tx / 135 * Mathf.Clamp(ty, 52.5f, 150) / 52.5f, transform.forward) * transform.up;
            joint.RotateAround(joint.position + v3, sword.transform.up, tx);
            joint.RotateAround(joint.position + transform.up * 1.5f, transform.right, -ty);
            sword.transform.localPosition = new Vector3(0, 0, 1.75f);
        }
        else if (health > -1)
        {
            if (guard == 0)
            {
                guard = 1;
                SoundManager.Instance.SFXPlay("Shield Impact1");
            }
            joint.localRotation = Quaternion.Euler(new Vector3(0, 0, 0));
            joint.localPosition = new Vector3(0, 1.5f, 0);
            sword.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, ty * tx / 135 * Mathf.Clamp(ty, 52.5f, 150) / 52.5f));
            Vector3 v3 = Quaternion.AngleAxis(-ty * tx / 135 * Mathf.Clamp(ty, 52.5f, 150) / 52.5f, transform.forward) * transform.up;
            joint.RotateAround(joint.position + v3, sword.transform.up, tx);
            joint.RotateAround(joint.position + transform.up * 1.5f, transform.right, -ty);
            sword.transform.localPosition = new Vector3(0, 0, 1.75f);
            sword.transform.position = transform.position + transform.up * 3 + transform.forward * 1.75f + transform.right * -tx / 90 + transform.up * -ty / 100;
        }
        //animation parameter settings
        anim.SetBool("leftStep", lr);
        if (!onTarget)
            anim.SetFloat("dis", 1000);
        else
            anim.SetFloat("dis", (ent.transform.position - transform.position).magnitude);
        if (health > -1 && (new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"))).magnitude > canSwing && guard == 0 && swing == 0 && lr && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") && !anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded")) //������
        {
            anim.SetBool("leftStep", lr);
            anim.CrossFade("Attack", 0, 0);
            lr = false;
            SoundManager.Instance.SFXPlay("Swing1");
        }
        else if (health > -1 && (new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"))).magnitude > canSwing && guard == 0 && swing == 0 && !lr && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") && !anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded")) //������
        {
            anim.SetBool("leftStep", lr);
            anim.CrossFade("Attack", 0, 0);
            lr = true;
            SoundManager.Instance.SFXPlay("Swing1");
        }
        if (anim.GetCurrentAnimatorStateInfo(0).IsName("Attack") && ent != null && !ent.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Hurted") && health > -1)
        {
            if (swing == 0)
            {
                swing = 1;
                GetComponent<LegsAnimator>().UseGluing = false;
                Physics.IgnoreLayerCollision(7, 8, false);
            }
        }
        else if (health > -1)
        {
            if (swing == 1)
            {
                swing = 0;
                GetComponent<LegsAnimator>().UseGluing = true;
                Physics.IgnoreLayerCollision(7, 8, true);
            }
        }
        if (!onTarget)
        {
            nav.speed = 0;
            if (FindClosestEnemy() != null)
                onTarget = true;
        }
        if (onTarget)
        {
            nav.speed = 3.5f;
            if (ent == null)
            {
                onTarget = false;
            }
            if (ent != null)
            {
                if (nav.enabled && !anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted") && !anim.GetCurrentAnimatorStateInfo(0).IsName("Guarded") && swing == 0)
                    nav.SetDestination(ent.transform.position);
                if (ent.tag == "Untagged")
                {
                    onTarget = false;
                }
            }
        }
    }

    private void FixedUpdate()
    {
        GetComponent<Rigidbody>().linearVelocity = GetComponent<Rigidbody>().linearVelocity.normalized * Mathf.Clamp(GetComponent<Rigidbody>().linearVelocity.magnitude, 0, 5);
        float angle = Quaternion.Angle(GetComponent<Rigidbody>().rotation, body.rotation);
        if (angle < 10f && GetComponent<Rigidbody>().angularVelocity.magnitude < 0.1f && anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted"))
        {
            GetComponent<LegsAnimator>().UseGluing = true;
            anim.CrossFade("Idle", 0, 1);
            GetComponent<Animator>().applyRootMotion = true;
            GetComponent<RigBuilder>().enabled = true;
        }
    }

    private void LateUpdate()
    {
        if (health > -1)
        {
            anim.GetBoneTransform(HumanBodyBones.Hips).position = new Vector3(transform.position.x, anim.GetBoneTransform(HumanBodyBones.Hips).position.y, transform.position.z);
            float anx = transform.rotation.eulerAngles.x;
            float anz = transform.rotation.eulerAngles.z;
            if (anx >= 180)
                anx -= 360;
            if (anz >= 180)
                anz -= 360;
            if (!anim.GetBool("leftHit"))
            {
                anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).forward, -transform.rotation.eulerAngles.z);
                anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).right, transform.rotation.eulerAngles.x);

                anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).forward, -anz / 2);
                anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).right, anx / 2);
            }
            else
            {
                anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).forward, -transform.rotation.eulerAngles.z);
                anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).right, transform.rotation.eulerAngles.x);

                anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).forward, -anz / 2);
                anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).RotateAround(anim.GetBoneTransform(HumanBodyBones.Hips).position, anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).right, anx / 2);
            }
            //Head IK fix
            if (!anim.GetCurrentAnimatorStateInfo(1).IsName("Hurted"))
                if (ent == null && !anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    anim.GetBoneTransform(HumanBodyBones.Head).rotation = Quaternion.Euler(new Vector3(0, transform.rotation.eulerAngles.y, 0));
                else if(anim.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
                    anim.GetBoneTransform(HumanBodyBones.Head).rotation = transform.rotation;
            //manual parenting
            jointParent.position = transform.position;
            jointParent.rotation = transform.rotation;
            Camera.main.transform.position = transform.position + Vector3.up * 4.804f - Quaternion.Euler(new Vector3(0, transform.rotation.eulerAngles.y, 0)) * Vector3.forward * 4.542f;
            Camera.main.transform.rotation = Quaternion.Euler(new Vector3(20, transform.rotation.eulerAngles.y, 0));
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 9 && ent != null && !ent.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
        {
            if (guard == 0)
            {
                if (health > 0)
                {
                    health--;
                    GetComponent<LegsAnimator>().UseGluing = false;
                    ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
                    var limit0 = joint.lowAngularXLimit;
                    limit0.limit = -90;
                    joint.lowAngularXLimit = limit0;
                    var limit1 = joint.highAngularXLimit;
                    limit1.limit = 90;
                    joint.highAngularXLimit = limit1;
                    var limit2 = joint.angularZLimit;
                    limit2.limit = 90;
                    joint.angularZLimit = limit2;
                    anim.CrossFade("Hurted", 0, 1);
                    anim.applyRootMotion = false;
                    GetComponent<RigBuilder>().enabled = false;
                    GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                    Destroy(hit, 0.3f);
                    if ((hit.transform.position - anim.GetBoneTransform(HumanBodyBones.LeftShoulder).position).magnitude > (hit.transform.position - anim.GetBoneTransform(HumanBodyBones.RightShoulder).position).magnitude)
                        anim.SetBool("leftHit", true);
                    else
                        anim.SetBool("leftHit", false);
                    knockBack = hit.transform.forward;
                    vec = new Vector3(hit.transform.right.x, 0, hit.transform.right.z).normalized;
                    SoundManager.Instance.SFXPlay("Shield Impact 1");
                    Physics.IgnoreLayerCollision(6, 9, true);
                }
                else
                {
                    if (guard == 1)
                    {
                        guard = 0;
                    }
                    health = -1;
                    anim.SetBool("died", true);
                    GetComponentInChildren<Rig>().weight = 0;
                    GetComponentInChildren<Collider>().transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.eulerAngles.y, 0));
                    GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                    Destroy(hit, 0.3f);
                    if (hit.transform.position.y > anim.GetBoneTransform(HumanBodyBones.Neck).position.y)
                        sign = 1;
                    else
                        sign = -1;
                    knockBack = hit.transform.forward;
                    vec = new Vector3(hit.transform.right.x, 0, hit.transform.right.z).normalized;
                    Destroy(GetComponent<Collider>());
                    GetComponent<Animator>().enabled = false;
                    foreach (Collider collider in GetComponentsInChildren<Collider>())
                    {
                        collider.isTrigger = false;
                        collider.enabled = true;
                        if (!collider.gameObject.GetComponent<Rigidbody>())
                        {
                            rigid = collider.gameObject.AddComponent<Rigidbody>();
                            rigid.linearVelocity = Vector3.zero;
                            rigid.angularVelocity = Vector3.zero;
                            collider.GetComponent<Rigidbody>().AddForce((knockBack + Vector3.up) * 5, ForceMode.Impulse);
                            collider.GetComponent<Rigidbody>().AddTorque(vec * sign * 5, ForceMode.Impulse);
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
                    SoundManager.Instance.SFXPlay("Shield Impact 1");
                    Physics.IgnoreLayerCollision(6, 9, true); //prevent natural collision forced until hurt is 0
                    Physics.IgnoreLayerCollision(7, 8, true);
                    foreach (XWeaponTrail xw in jointParent.GetComponentsInChildren<XWeaponTrail>())
                        xw.MaxFrame = 0;
                    GetComponent<LegsAnimator>().enabled = false;
                    EnemyMotion.player = null;
                    gameObject.tag = "Untagged";
                    anim.GetBoneTransform(HumanBodyBones.Hips).parent = null;
                    transform.parent = anim.GetBoneTransform(HumanBodyBones.Hips);
                    foreach (Collider col in jointParent.GetComponentsInChildren<Collider>())
                        col.gameObject.layer = 10;
                    anim.GetBoneTransform(HumanBodyBones.RightShoulder).localScale = new Vector3(1, 1, 1);
                    jointParent.parent = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
                    jointParent.localPosition = new Vector3(0, jointParent.localPosition.y, 0);
                    Invoke("Dissolve", 4);
                    Destroy(jointParent.gameObject, 5f);
                    Destroy(anim.GetBoneTransform(HumanBodyBones.Hips).gameObject, 5f);
                    Destroy(gameObject, 5f);
                }
            }
            else if (guard == 1)
            {
                Vector3 v = Vector3.right * -tx / 90 + Vector3.up * -ty / 100;
                float r = Mathf.Atan2(v.y, -v.x) * Mathf.Rad2Deg + 180;
                Vector3 v1 = Quaternion.Euler(new Vector3(-ent.transform.rotation.eulerAngles.x, -ent.transform.rotation.eulerAngles.y, -ent.transform.rotation.eulerAngles.z)) * EnemyMotion.psword.GetComponent<Rigidbody>().linearVelocity;
                v1 = new Vector3(v1.x, v1.y, 0);
                float r1 = Mathf.Atan2(v1.y, v1.x) * Mathf.Rad2Deg + 180;
                float angle = Mathf.Abs(r - r1);
                if (angle < 30 || angle > 345 || (angle > 150 && angle < 210))
                {
                    if (health > 0)
                    {
                        health--;
                        GetComponent<LegsAnimator>().UseGluing = false;
                        ConfigurableJoint joint = GetComponent<ConfigurableJoint>();
                        var limit0 = joint.lowAngularXLimit;
                        limit0.limit = -90;
                        joint.lowAngularXLimit = limit0;
                        var limit1 = joint.highAngularXLimit;
                        limit1.limit = 90;
                        joint.highAngularXLimit = limit1;
                        var limit2 = joint.angularZLimit;
                        limit2.limit = 90;
                        joint.angularZLimit = limit2;
                        anim.CrossFade("Hurted", 0, 1);
                        anim.applyRootMotion = false;
                        GetComponent<RigBuilder>().enabled = false;
                        GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                        Destroy(hit, 0.3f);
                        if ((hit.transform.position - anim.GetBoneTransform(HumanBodyBones.LeftShoulder).position).magnitude > (hit.transform.position - anim.GetBoneTransform(HumanBodyBones.RightShoulder).position).magnitude)
                            anim.SetBool("leftHit", true);
                        else
                            anim.SetBool("leftHit", false);
                        knockBack = hit.transform.forward;
                        vec = new Vector3(hit.transform.right.x, 0, hit.transform.right.z).normalized;
                        SoundManager.Instance.SFXPlay("Shield Impact 1");
                        Physics.IgnoreLayerCollision(6, 9, true);
                    }
                    else
                    {
                        if (guard == 1)
                        {
                            guard = 0;
                        }
                        health = -1;
                        anim.SetBool("died", true);
                        GetComponentInChildren<Rig>().weight = 0;
                        GetComponentInChildren<Collider>().transform.rotation = Quaternion.Euler(new Vector3(0, transform.rotation.eulerAngles.y, 0));
                        GameObject hit = Instantiate(hitVFX, collision.contacts[0].point, Quaternion.LookRotation(anim.GetBoneTransform(HumanBodyBones.Neck).position - collision.contacts[0].point));
                        Destroy(hit, 0.3f);
                        if (hit.transform.position.y > anim.GetBoneTransform(HumanBodyBones.Neck).position.y)
                            sign = 1;
                        else
                            sign = -1;
                        knockBack = hit.transform.forward;
                        vec = new Vector3(hit.transform.right.x, 0, hit.transform.right.z).normalized;
                        Destroy(GetComponent<Collider>());
                        GetComponent<Animator>().enabled = false;
                        foreach (Collider collider in GetComponentsInChildren<Collider>())
                        {
                            collider.isTrigger = false;
                            collider.enabled = true;
                            if (!collider.gameObject.GetComponent<Rigidbody>())
                            {
                                rigid = collider.gameObject.AddComponent<Rigidbody>();
                                rigid.linearVelocity = Vector3.zero;
                                rigid.angularVelocity = Vector3.zero;
                                collider.GetComponent<Rigidbody>().AddForce((knockBack + Vector3.up) * 5, ForceMode.Impulse);
                                collider.GetComponent<Rigidbody>().AddTorque(vec * sign * 5, ForceMode.Impulse);
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
                        SoundManager.Instance.SFXPlay("Shield Impact 1");
                        Physics.IgnoreLayerCollision(6, 9, true); //prevent natural collision forced until hurt is 0
                        Physics.IgnoreLayerCollision(7, 8, true);
                        foreach (XWeaponTrail xw in jointParent.GetComponentsInChildren<XWeaponTrail>())
                            xw.MaxFrame = 0;
                        GetComponent<LegsAnimator>().enabled = false;
                        EnemyMotion.player = null;
                        gameObject.tag = "Untagged";
                        anim.GetBoneTransform(HumanBodyBones.Hips).parent = null;
                        transform.parent = anim.GetBoneTransform(HumanBodyBones.Hips);
                        foreach (Collider col in jointParent.GetComponentsInChildren<Collider>())
                            col.gameObject.layer = 10;
                        anim.GetBoneTransform(HumanBodyBones.RightShoulder).localScale = new Vector3(1, 1, 1);
                        jointParent.parent = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
                        jointParent.localPosition = new Vector3(0, jointParent.localPosition.y, 0);
                        Invoke("Dissolve", 4);
                        Destroy(jointParent.gameObject, 5f);
                        Destroy(anim.GetBoneTransform(HumanBodyBones.Hips).gameObject, 5f);
                        Destroy(gameObject, 5f);
                    }
                }
                else
                {
                    GameObject shield = Instantiate(shieldVFX, collision.contacts[0].point, Quaternion.LookRotation(transform.forward));
                    Destroy(shield, 0.3f);
                    SoundManager.Instance.SFXPlay("Armor Impact 2");
                    ent.transform.position -= ent.transform.forward;
                    if (!ent.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Guarded"))
                        ent.GetComponent<Animator>().CrossFade("Guarded", 0, 0);
                }
            }
        }
    }

    public Material dissolve;

    void Dissolve()
    {
        foreach (Renderer renderer in jointParent.GetComponentsInChildren<Renderer>())
        {
            renderer.gameObject.AddComponent<DissolveSphere>();
            Material[] mat = renderer.materials;
            for (int i = 0; i < renderer.materials.Length; i++)
                mat[i] = dissolve;
            renderer.materials = mat;
        }
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