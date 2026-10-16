using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BrokenEdgeExplosionAudioGenerator
{
    private const int SampleRate = 44100;
    private const float Duration = 1.15f;
    private const string OutputPath = "Assets/Resources/Audio/SFX/BrokenEdge Explosion.wav";

    [MenuItem("Tools/Swordplay/Generate Broken Edge Explosion SFX")]
    public static void Generate()
    {
        int sampleCount = Mathf.CeilToInt(SampleRate * Duration);
        short[] samples = new short[sampleCount];
        uint randomState = 0x7A31D4B9u;
        float lowNoise = 0f;
        float slowerNoise = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)SampleRate;
            float noise = NextNoise(ref randomState);
            lowNoise = Mathf.Lerp(lowNoise, noise, 0.075f);
            slowerNoise = Mathf.Lerp(slowerNoise, noise, 0.012f);

            float attack = Mathf.Clamp01(t / 0.006f);
            float boomEnvelope = attack * Mathf.Exp(-t * 4.6f);
            float crackEnvelope = attack * Mathf.Exp(-t * 21f);
            float debrisEnvelope = Mathf.Clamp01((t - 0.025f) / 0.02f) * Mathf.Exp(-t * 6.8f);
            float boom = Mathf.Sin(2f * Mathf.PI * (62f - 22f * t) * t) * boomEnvelope;
            float body = lowNoise * boomEnvelope;
            float crack = noise * crackEnvelope;
            float debris = slowerNoise * debrisEnvelope;

            float secondT = Mathf.Max(0f, t - 0.115f);
            float secondary = Mathf.Sin(2f * Mathf.PI * 47f * secondT)
                              * Mathf.Exp(-secondT * 8.5f)
                              * (t >= 0.115f ? 1f : 0f);

            float mixed = boom * 0.72f + body * 0.78f + crack * 0.5f + debris * 0.42f + secondary * 0.28f;
            mixed = (float)Math.Tanh(mixed * 1.35f) * 0.88f;
            samples[i] = (short)Mathf.RoundToInt(Mathf.Clamp(mixed, -1f, 1f) * short.MaxValue);
        }

        string absolutePath = Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
        File.WriteAllBytes(absolutePath, BuildWave(samples));
        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);

        AudioImporter importer = AssetImporter.GetAtPath(OutputPath) as AudioImporter;
        if (importer != null)
        {
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.78f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true
            };
            importer.SaveAndReimport();
        }

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(OutputPath);
        if (clip == null || clip.length < 1f)
            throw new InvalidOperationException("Generated explosion clip failed validation.");

        Debug.Log($"[Broken Edge Audio] EXPLOSION_READY path={OutputPath}, length={clip.length:F3}s, samples={clip.samples}");
    }

    private static float NextNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state / (float)uint.MaxValue) * 2f - 1f;
    }

    private static byte[] BuildWave(short[] samples)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataSize = samples.Length * sizeof(short);
        using MemoryStream stream = new MemoryStream(44 + dataSize);
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + dataSize);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(dataSize);
        foreach (short sample in samples) writer.Write(sample);
        return stream.ToArray();
    }
}
