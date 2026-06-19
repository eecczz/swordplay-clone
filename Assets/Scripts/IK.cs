using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class IK : MonoBehaviour
{
    Animator anim;
    public float weight = 1f;
    public Transform leftHand;
    public Transform rightHand;
    public Transform leftHand1;
    public Transform rightHand1;
    public Transform hintLeft;
    public Transform hintRight;

    // Start is called before the first frame update
    void Start()
    {
        anim = GetComponent<Animator>();
    }


    private void OnAnimatorIK(int layerIndex)
    {
        anim.SetIKPosition(AvatarIKGoal.LeftHand, leftHand.position);
        anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1);
        anim.SetIKRotation(AvatarIKGoal.LeftHand, leftHand.rotation);
        anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1);

        anim.SetIKPosition(AvatarIKGoal.RightHand, rightHand.position);
        anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 1);
        anim.SetIKRotation(AvatarIKGoal.RightHand, rightHand.rotation);
        anim.SetIKRotationWeight(AvatarIKGoal.RightHand, 1);


        anim.SetIKHintPosition(AvatarIKHint.LeftElbow, hintLeft.position);
        anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1);

        anim.SetIKHintPosition(AvatarIKHint.RightElbow, hintRight.position);
        anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 1);
    }

    void LateUpdate()
    {
        //leftHand1.position = leftHand.position;
        //rightHand1.position = rightHand.position;
    }
}