public sealed class PlayerWorkerMouseDrivenAttack : PlayerWorker
{
    // Opts only this player instance into mouse-driven Attack playback.
    protected override bool UsesProgressDrivenPcAttack => true;
    protected override bool UsesAttackSlowdown => false;
}
