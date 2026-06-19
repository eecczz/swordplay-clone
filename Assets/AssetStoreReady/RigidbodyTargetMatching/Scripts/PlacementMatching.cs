using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlacementMatching : RigidbodyMatchingBase
{
    [Header("Placement")]
    public Vector3 TargetPosition;
    public Vector3 TargetRotation;

    protected override void FixedUpdate()
    {
        SetTargetPlacement(TargetPosition, Quaternion.Euler(TargetRotation));

        base.FixedUpdate();
    }
}
