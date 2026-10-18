using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger-only sword hitbox with a continuous capsule sweep. The sweep samples the
/// real collider volume between physics frames, so fast animated blades cannot tunnel
/// through a character and the collider does not need to be artificially enlarged.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordHitbox : MonoBehaviour
{
    // Kept on the weapon itself so trigger and swept hits share one deduplication path.
    private const int MaxSweepSamples = 16;
    private readonly Collider[] overlapResults = new Collider[32];
    private readonly HashSet<int> hitReceivers = new();
    private readonly List<CapsuleState> previousCapsules = new();

    private PlayerWorker playerOwner;
    private EnemyWorker enemyOwner;
    private Collider[] swordColliders;
    private int observedAttackSequence = -1;

    private struct CapsuleState
    {
        public Vector3 Start;
        public Vector3 End;
        public float Radius;
        public bool Valid;
    }

    public static SwordHitbox Configure(GameObject weapon, PlayerWorker owner)
    {
        if (weapon == null || owner == null) return null;
        SwordHitbox hitbox = weapon.GetComponent<SwordHitbox>();
        if (hitbox == null) hitbox = weapon.AddComponent<SwordHitbox>();
        hitbox.playerOwner = owner;
        hitbox.enemyOwner = null;
        hitbox.ConfigurePhysics();
        return hitbox;
    }

    public static SwordHitbox Configure(GameObject weapon, EnemyWorker owner)
    {
        if (weapon == null || owner == null) return null;
        SwordHitbox hitbox = weapon.GetComponent<SwordHitbox>();
        if (hitbox == null) hitbox = weapon.AddComponent<SwordHitbox>();
        hitbox.enemyOwner = owner;
        hitbox.playerOwner = null;
        hitbox.ConfigurePhysics();
        return hitbox;
    }

    private void Awake() => ConfigurePhysics();

    private void ConfigurePhysics()
    {
        swordColliders = GetComponentsInChildren<Collider>(true);
        previousCapsules.Clear();
        foreach (Collider swordCollider in swordColliders)
        {
            swordCollider.isTrigger = true;
            SwordTriggerRelay relay = swordCollider.GetComponent<SwordTriggerRelay>();
            if (relay == null) relay = swordCollider.gameObject.AddComponent<SwordTriggerRelay>();
            relay.Configure(this, swordCollider);
            previousCapsules.Add(default);
        }

        foreach (Rigidbody swordBody in GetComponentsInChildren<Rigidbody>(true))
        {
            swordBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            swordBody.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void OnEnable()
    {
        observedAttackSequence = -1;
        hitReceivers.Clear();
    }

    private void FixedUpdate()
    {
        if (swordColliders == null || swordColliders.Length == 0)
            ConfigurePhysics();

        int attackSequence = CurrentAttackSequence;
        if (attackSequence != observedAttackSequence)
        {
            observedAttackSequence = attackSequence;
            hitReceivers.Clear();
        }

        for (int i = 0; i < swordColliders.Length; i++)
        {
            Collider source = swordColliders[i];
            if (source == null || !source.enabled)
                continue;

            CapsuleState current = GetCapsuleState(source);
            CapsuleState previous = previousCapsules[i];
            if (IsAttackActive && current.Valid)
                SweepCapsule(source, previous, current);
            previousCapsules[i] = current;
        }
    }

    public void ReportTriggerHit(Collider source, Collider target)
    {
        TryReportHit(source, target, null);
    }

    private bool IsAttackActive => playerOwner != null
        ? playerOwner.IsAttackHitActive
        : enemyOwner != null && enemyOwner.IsAttackHitActive;

    private int CurrentAttackSequence => playerOwner != null
        ? playerOwner.AttackSequence
        : enemyOwner != null ? enemyOwner.AttackSequence : -1;

    private void SweepCapsule(Collider source, CapsuleState previous, CapsuleState current)
    {
        if (!previous.Valid)
        {
            QueryCapsule(source, current.Start, current.End, current.Radius);
            return;
        }

        float travel = Mathf.Max(Vector3.Distance(previous.Start, current.Start),
            Vector3.Distance(previous.End, current.End));
        float sampleSpacing = Mathf.Max(0.025f, Mathf.Min(previous.Radius, current.Radius) * 0.75f);
        int samples = Mathf.Clamp(Mathf.CeilToInt(travel / sampleSpacing), 1, MaxSweepSamples);
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            QueryCapsule(source,
                Vector3.Lerp(previous.Start, current.Start, t),
                Vector3.Lerp(previous.End, current.End, t),
                Mathf.Lerp(previous.Radius, current.Radius, t));
        }
    }

    private void QueryCapsule(Collider source, Vector3 start, Vector3 end, float radius)
    {
        // Do not make combat correctness depend on every child/body collider inheriting
        // the root's Player/Enemy layer. Modular characters frequently contain colliders
        // on Default; TryReportHit already performs the authoritative owner-type filter.
        int count = Physics.OverlapCapsuleNonAlloc(start, end, radius, overlapResults,
            ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider target = overlapResults[i];
            if (target == null) continue;

            Vector3 axisPoint = ClosestPointOnSegment(target.bounds.center, start, end);
            Vector3 targetPoint = target.ClosestPoint(axisPoint);
            Vector3 towardTarget = targetPoint - axisPoint;
            Vector3 capsulePoint = towardTarget.sqrMagnitude > 0.000001f
                ? axisPoint + towardTarget.normalized * Mathf.Min(radius, towardTarget.magnitude)
                : axisPoint;
            TryReportHit(source, target, (capsulePoint + targetPoint) * 0.5f);
        }
    }

    private void TryReportHit(Collider source, Collider target, Vector3? sweepPoint)
    {
        if (!IsAttackActive || target == null || target.transform.IsChildOf(transform))
            return;

        Vector3 hitPoint = ResolveHitPoint(source, target, sweepPoint);
        if (playerOwner != null)
        {
            EnemyWorker receiver = target.GetComponentInParent<EnemyWorker>();
            if (receiver == null || !hitReceivers.Add(receiver.GetInstanceID())) return;
            receiver.ReceiveSwordHit(playerOwner, source, target, hitPoint, observedAttackSequence);
        }
        else if (enemyOwner != null)
        {
            PlayerWorker receiver = target.GetComponentInParent<PlayerWorker>();
            if (receiver == null || !hitReceivers.Add(receiver.GetInstanceID())) return;
            receiver.ReceiveSwordHit(enemyOwner, source, target, hitPoint, observedAttackSequence);
        }
    }

    private static Vector3 ResolveHitPoint(Collider source, Collider target, Vector3? sweepPoint)
    {
        if (sweepPoint.HasValue) return sweepPoint.Value;
        if (source == null) return target.ClosestPoint(target.bounds.center);

        Vector3 sourcePoint = source.ClosestPoint(target.bounds.center);
        Vector3 targetPoint = target.ClosestPoint(sourcePoint);
        sourcePoint = source.ClosestPoint(targetPoint);
        return (sourcePoint + targetPoint) * 0.5f;
    }

    /// <summary>
    /// Projects a reported overlap point onto the struck collider's weapon-facing surface.
    /// Trigger overlaps often report a point inside a capsule, which can hide VFX in the body
    /// or make every hit appear at the capsule centre.
    /// </summary>
    public static Vector3 ResolveSurfaceContact(Collider source, Collider target, Vector3 reportedPoint)
    {
        if (target == null) return reportedPoint;

        Vector3 sourceReference = source != null ? source.bounds.center : reportedPoint;
        Vector3 outward = sourceReference - target.bounds.center;
        if (outward.sqrMagnitude < 0.0001f)
            outward = reportedPoint - target.bounds.center;
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.forward;
        outward.Normalize();

        float castDistance = target.bounds.extents.magnitude
            + (source != null ? source.bounds.extents.magnitude : 0.25f) + 0.75f;
        Vector3 rayOrigin = target.bounds.center + outward * castDistance;
        Ray surfaceRay = new Ray(rayOrigin, -outward);
        if (target.Raycast(surfaceRay, out RaycastHit surfaceHit, castDistance * 2f))
            return surfaceHit.point + outward * 0.025f;

        Vector3 closest = target.ClosestPoint(sourceReference);
        return closest + outward * 0.025f;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.000001f) return start;
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return start + segment * t;
    }

    private static CapsuleState GetCapsuleState(Collider value)
    {
        if (value is CapsuleCollider capsule)
        {
            Transform t = capsule.transform;
            Vector3 scale = t.lossyScale;
            Vector3 axis;
            float axisScale;
            float radiusScale;
            switch (capsule.direction)
            {
                case 0:
                    axis = t.right;
                    axisScale = Mathf.Abs(scale.x);
                    radiusScale = Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    break;
                case 2:
                    axis = t.forward;
                    axisScale = Mathf.Abs(scale.z);
                    radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                    break;
                default:
                    axis = t.up;
                    axisScale = Mathf.Abs(scale.y);
                    radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    break;
            }

            float radius = Mathf.Max(0.005f, capsule.radius * radiusScale);
            float halfSegment = Mathf.Max(0f, capsule.height * axisScale * 0.5f - radius);
            Vector3 center = t.TransformPoint(capsule.center);
            return new CapsuleState
            {
                Start = center - axis.normalized * halfSegment,
                End = center + axis.normalized * halfSegment,
                Radius = radius,
                Valid = true
            };
        }

        Bounds bounds = value.bounds;
        Vector3 extents = bounds.extents;
        Vector3 axisFallback = Vector3.up;
        float halfLength = extents.y;
        float radiusFallback = Mathf.Max(extents.x, extents.z);
        if (extents.x >= extents.y && extents.x >= extents.z)
        {
            axisFallback = Vector3.right;
            halfLength = extents.x;
            radiusFallback = Mathf.Max(extents.y, extents.z);
        }
        else if (extents.z >= extents.y)
        {
            axisFallback = Vector3.forward;
            halfLength = extents.z;
            radiusFallback = Mathf.Max(extents.x, extents.y);
        }

        float segment = Mathf.Max(0f, halfLength - radiusFallback);
        return new CapsuleState
        {
            Start = bounds.center - axisFallback * segment,
            End = bounds.center + axisFallback * segment,
            Radius = Mathf.Max(0.005f, radiusFallback),
            Valid = bounds.size.sqrMagnitude > 0f
        };
    }
}
