using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetMatching : RigidbodyMatchingBase
{
    [Header("Target")]
    public Transform Target;

    protected override void FixedUpdate()
    {
        SetTargetPlacement(Target);

        base.FixedUpdate();
    }
}
