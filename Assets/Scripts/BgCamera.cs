using UnityEngine;

[DisallowMultipleComponent]
public class BgCamera : MonoBehaviour
{
    [Header("Handheld Motion")]
    [SerializeField, Min(0f)] private float motionFrequency = 0.22f;
    [SerializeField] private Vector3 positionAmplitude = new Vector3(0.012f, 0.009f, 0.006f);
    [SerializeField] private Vector3 rotationAmplitude = new Vector3(0.22f, 0.32f, 0.12f);
    [SerializeField, Min(0f)] private float breathingAmount = 0.004f;
    [SerializeField, Min(0.01f)] private float breathingFrequency = 0.16f;
    [SerializeField, Min(0f)] private float damping = 5f;

    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 currentPositionOffset;
    private Vector3 currentRotationOffset;
    private float noiseTimeOffset;
    private bool hasBasePose;

    private void OnEnable()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        currentPositionOffset = Vector3.zero;
        currentRotationOffset = Vector3.zero;
        noiseTimeOffset = Mathf.Abs(GetInstanceID() * 0.137f) % 1000f;
        hasBasePose = true;
    }

    private void LateUpdate()
    {
        float time = Time.unscaledTime * motionFrequency + noiseTimeOffset;

        // Several offset noise samples create slow, non-repeating operator drift
        // without accumulating rotation or orbiting around the scene.
        Vector3 targetPositionOffset = new Vector3(
            CenteredNoise(time, 11.3f),
            CenteredNoise(time * 0.83f, 37.1f),
            CenteredNoise(time * 0.61f, 73.9f));
        targetPositionOffset = Vector3.Scale(targetPositionOffset, positionAmplitude);
        targetPositionOffset.y += Mathf.Sin(Time.unscaledTime * breathingFrequency * Mathf.PI * 2f) * breathingAmount;

        Vector3 targetRotationOffset = new Vector3(
            CenteredNoise(time * 0.71f, 101.7f),
            CenteredNoise(time * 0.89f, 149.2f),
            CenteredNoise(time * 0.53f, 211.6f));
        targetRotationOffset = Vector3.Scale(targetRotationOffset, rotationAmplitude);

        float blend = damping <= 0f ? 1f : 1f - Mathf.Exp(-damping * Time.unscaledDeltaTime);
        currentPositionOffset = Vector3.Lerp(currentPositionOffset, targetPositionOffset, blend);
        currentRotationOffset = Vector3.Lerp(currentRotationOffset, targetRotationOffset, blend);

        transform.localPosition = baseLocalPosition + currentPositionOffset;
        transform.localRotation = baseLocalRotation * Quaternion.Euler(currentRotationOffset);
    }

    private void OnDisable()
    {
        if (!hasBasePose)
            return;

        transform.localPosition = baseLocalPosition;
        transform.localRotation = baseLocalRotation;
        hasBasePose = false;
    }

    private static float CenteredNoise(float time, float seed)
    {
        return Mathf.PerlinNoise(time, seed) * 2f - 1f;
    }
}
