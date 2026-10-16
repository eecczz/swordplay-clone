using UnityEngine;

[DisallowMultipleComponent]
public sealed class SwordTriggerRelay : MonoBehaviour
{
    private SwordHitbox owner;
    private Collider source;

    public void Configure(SwordHitbox hitbox, Collider sourceCollider)
    {
        owner = hitbox;
        source = sourceCollider;
    }

    private void OnTriggerEnter(Collider other)
    {
        owner?.ReportTriggerHit(source, other);
    }

    private void OnTriggerStay(Collider other)
    {
        owner?.ReportTriggerHit(source, other);
    }
}
