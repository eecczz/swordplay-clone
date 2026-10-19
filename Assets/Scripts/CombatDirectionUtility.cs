using UnityEngine;

public static class CombatDirectionUtility
{
    private const float MinimumBlockingSwordAngle = 30f;

    public static bool IsAttackStateActive(Animator animator, float hitStart, float hitEnd)
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return false;

        if (IsAttackTime(animator.GetCurrentAnimatorStateInfo(0), hitStart, hitEnd))
            return true;

        return animator.IsInTransition(0)
               && IsAttackTime(animator.GetNextAnimatorStateInfo(0), hitStart, hitEnd);
    }

    private static bool IsAttackTime(AnimatorStateInfo state, float hitStart, float hitEnd)
    {
        return state.IsName("Attack")
               && state.normalizedTime >= hitStart
               && state.normalizedTime <= hitEnd;
    }

    public enum AttackSector
    {
        TopLeft,
        Top,
        TopRight,
        MiddleLeft,
        MiddleRight,
        BottomLeft,
        Bottom,
        BottomRight
    }

    private static readonly Vector2[] AttackAnchors =
    {
        new(-180f, 150f),
        new(0f, 150f),
        new(180f, 150f),
        new(-180f, 75f),
        new(180f, 75f),
        new(-180f, 0f),
        new(0f, 0f),
        new(180f, 0f)
    };

    public static AttackSector GetAttackSector(Vector2 direction)
    {
        int closest = 0;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < AttackAnchors.Length; i++)
        {
            // The blend tree spans 360 horizontally and 150 vertically.
            // Normalize both axes so horizontal distance does not dominate.
            Vector2 delta = direction - AttackAnchors[i];
            float distance = delta.x * delta.x / (180f * 180f)
                           + delta.y * delta.y / (75f * 75f);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = i;
            }
        }

        return (AttackSector)closest;
    }

    public static Vector2 GetNearestNonCenterAttackAnchor(Vector2 direction)
    {
        int closest = 0;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < AttackAnchors.Length; i++)
        {
            if ((AttackSector)i == AttackSector.Bottom)
                continue;

            Vector2 delta = direction - AttackAnchors[i];
            float distance = delta.x * delta.x / (180f * 180f)
                           + delta.y * delta.y / (75f * 75f);
            if (distance >= closestDistance)
                continue;
            closestDistance = distance;
            closest = i;
        }
        return AttackAnchors[closest];
    }

    /// <summary>
    /// The attack blend tree is not a square coordinate system, so raw Vector2 distance
    /// cannot identify its centre consistently. Reuse the aspect-corrected eight-sector
    /// mapping and reject only the sector whose authored anchor is (0, 0).
    /// </summary>
    public static bool IsOutsideCenterAttackSector(Vector2 direction)
    {
        return GetAttackSector(direction) != AttackSector.Bottom;
    }

    public static bool CanBlock(Transform attackingSword, Transform guardingSword)
    {
        if (attackingSword == null || guardingSword == null)
            return false;

        Camera viewCamera = Camera.main;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            return false;

        // Transform the world-space sword axes into camera space and discard depth. This
        // measures only the angle visible on screen; tilting either sword toward or away
        // from the camera cannot incorrectly increase the blocking angle.
        Vector3 attackView = viewCamera.transform.InverseTransformDirection(attackingSword.up);
        Vector3 guardView = viewCamera.transform.InverseTransformDirection(guardingSword.up);
        Vector2 attackScreen = new(attackView.x, attackView.y);
        Vector2 guardScreen = new(guardView.x, guardView.y);

        if (attackScreen.sqrMagnitude < 0.000001f || guardScreen.sqrMagnitude < 0.000001f)
            return false;

        float visibleAngle = Vector2.Angle(attackScreen, guardScreen);
        return visibleAngle >= MinimumBlockingSwordAngle;
    }
}
