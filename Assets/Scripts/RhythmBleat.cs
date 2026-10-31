using System.Collections.Generic;
using UnityEngine;

// Preserve the whole voiced bleat, trimming silence rather than cutting off its vowel.
public static class RhythmBleat
{
    private static readonly Dictionary<AudioClip, AudioClip> Clips = new Dictionary<AudioClip, AudioClip>();

    public static AudioClip GetClip(AudioClip original)
    {
        if (original == null) return null;
        if (Clips.TryGetValue(original, out var cached) && cached != null) return cached;
        if (original.loadState != AudioDataLoadState.Loaded) original.LoadAudioData();
        var samples = new float[original.samples * original.channels];
        if (!original.GetData(samples, 0)) return original;
        float peak = 0f;
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        if (peak < 0.001f) return original;
        int onset = 0;
        int end = samples.Length - 1;
        float silenceThreshold = peak * 0.035f;
        // Put the clearly voiced attack at the beat, rather than the preceding faint breath.
        while (onset < samples.Length && Mathf.Abs(samples[onset]) < peak * 0.12f) onset++;
        while (end > onset && Mathf.Abs(samples[end]) < silenceThreshold) end--;
        int start = Mathf.Max(0, onset / original.channels - Mathf.RoundToInt(original.frequency * 0.004f));
        int finish = Mathf.Min(original.samples, end / original.channels + 1 + Mathf.RoundToInt(original.frequency * 0.04f));
        int sourceFrames = finish - start;
        // Slightly tighten long bleats, with a low cap so the sheep's voice remains recognizable.
        float speed = Mathf.Clamp(sourceFrames / (original.frequency * 0.48f), 1f, 1.2f);
        int frames = Mathf.FloorToInt(sourceFrames / speed);
        if (frames < 1) return original;
        var trimmed = new float[frames * original.channels];
        for (int frame = 0; frame < frames; frame++)
        {
            float time = frame / (float)original.frequency;
            float remaining = (frames - 1 - frame) / (float)original.frequency;
            float envelope = Mathf.Min(Mathf.Clamp01(time / 0.003f), Mathf.Clamp01(remaining / 0.055f));
            float sourcePosition = frame * speed;
            int first = Mathf.Min(Mathf.FloorToInt(sourcePosition), sourceFrames - 1);
            int second = Mathf.Min(first + 1, sourceFrames - 1);
            float blend = sourcePosition - first;
            for (int channel = 0; channel < original.channels; channel++)
                trimmed[frame * original.channels + channel] = Mathf.Lerp(
                    samples[(start + first) * original.channels + channel],
                    samples[(start + second) * original.channels + channel], blend)
                    * envelope * Mathf.Min(1.25f, 0.85f / peak);
        }
        var clip = AudioClip.Create(original.name + "_rhythm", frames, original.channels, original.frequency, false);
        clip.SetData(trimmed, 0);
        Clips[original] = clip;
        return clip;
    }
}
