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
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }

    protected override void FixedUpdate()
    {
        if (Target == null)
            return;

        // This object is a sword hit detector, not a simulated weapon. Spring-following
        // lets it trail a fast attack animation and causes visible hits to miss. Follow
        // the serialized sword target exactly before SwordHitbox performs its sweep.
        transform.SetPositionAndRotation(Target.position, Target.rotation);
    }
}
