using UnityEngine;

public static class CombatDirectionUtility
{
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

    public enum GuardSector
    {
        TopLeft,
        TopRight,
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

    public static GuardSector GetGuardSector(Vector2 direction)
    {
        if (direction.y >= 75f)
            return direction.x <= 0f ? GuardSector.TopLeft : GuardSector.TopRight;

        if (direction.x <= -90f)
            return GuardSector.BottomLeft;
        if (direction.x >= 90f)
            return GuardSector.BottomRight;
        return GuardSector.Bottom;
    }

    public static bool CanBlock(Vector2 attackDirection, Vector2 guardDirection)
    {
        AttackSector attack = GetAttackSector(attackDirection);
        GuardSector guard = GetGuardSector(guardDirection);

        return attack switch
        {
            AttackSector.TopLeft => guard == GuardSector.TopLeft,
            AttackSector.Top => guard is GuardSector.TopLeft or GuardSector.TopRight,
            AttackSector.TopRight => guard == GuardSector.TopRight,
            AttackSector.MiddleLeft => guard is GuardSector.TopLeft or GuardSector.BottomLeft,
            AttackSector.MiddleRight => guard is GuardSector.TopRight or GuardSector.BottomRight,
            AttackSector.BottomLeft => guard == GuardSector.BottomLeft,
            AttackSector.Bottom => guard == GuardSector.Bottom,
            AttackSector.BottomRight => guard == GuardSector.BottomRight,
            _ => false
        };
    }
}
