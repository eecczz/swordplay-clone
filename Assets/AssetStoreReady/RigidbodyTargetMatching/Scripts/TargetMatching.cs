using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetMatching : RigidbodyMatchingBase
{
    [Header("Target")]
    public Transform Target;

    private void OnEnable()
    {
        ConfigureTriggerCollider();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ConfigureTriggerCollider();
    }
#endif

    private void ConfigureTriggerCollider()
    {
        foreach (Collider value in GetComponentsInChildren<Collider>(true))
            value.isTrigger = true;

        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    protected override void FixedUpdate()
    {
        if (Target == null)
            return;

        SetTargetPlacement(Target);

        base.FixedUpdate();
    }
}
