using UnityEngine;

public static class CombatImpactAudio
{
    private const string ExplosionClipName = "BrokenEdge Explosion";

    public static void PlayExplosionAtPoint(Vector3 position)
    {
        PlayAtPoint(ExplosionClipName, position, Random.Range(0.94f, 1.06f), 1f);
    }

    public static void PlayAtPoint(string clipName, Vector3 position)
    {
        PlayAtPoint(clipName, position, 1f, 1f);
    }

    private static void PlayAtPoint(string clipName, Vector3 position, float pitch, float volume)
    {
        AudioClip clip = Resources.Load<AudioClip>($"Audio/SFX/{clipName}");
        if (clip == null)
        {
            Debug.LogWarning($"Combat impact SFX '{clipName}' was not found in Resources/Audio/SFX/.");
            return;
        }

        GameObject soundObject = new(clipName + " Spatial Sound");
        soundObject.transform.position = position;
        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialBlend = 1f;
        source.minDistance = 1.5f;
        source.maxDistance = 25f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.pitch = pitch;
        source.volume = volume;
        source.Play();
        Object.Destroy(soundObject, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)) + 0.1f);
    }
}
