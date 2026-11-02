using UnityEngine;
using System.Collections.Generic;

// Identical playback for the player and accepting sheep, scheduled on a shared audio clock.
public sealed class FlockCallVoice : MonoBehaviour
{
    private readonly List<AudioSource> voices = new List<AudioSource>();
    private readonly List<double> busyUntil = new List<double>();

    public static FlockCallVoice For(GameObject sheep)
    {
        var voice = sheep.GetComponent<FlockCallVoice>();
        return voice != null ? voice : sheep.AddComponent<FlockCallVoice>();
    }

    public void Play(AudioClip original, double time, float volume)
    {
        var clip = RhythmBleat.GetClip(original);
        if (clip == null) return;
        // Calls can be queued several beats ahead (the ending does this for the whole chorus).
        // A source is reusable only after its note has actually finished on the DSP clock,
        // even if the next requested note is much later than that reserved note.
        double now = AudioSettings.dspTime;
        int slot = -1;
        for (int i = 0; i < voices.Count; i++)
        {
            if (busyUntil[i] <= now)
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
        {
            slot = voices.Count;
            var added = gameObject.AddComponent<AudioSource>();
            added.playOnAwake = false;
            added.spatialBlend = 0f;
            added.dopplerLevel = 0f;
            added.loop = false;
            voices.Add(added);
            busyUntil.Add(double.NegativeInfinity);
        }
        var source = voices[slot];
        source.Stop();
        source.clip = clip;
        source.pitch = 1f;
        source.volume = volume;
        source.PlayScheduled(time);
        busyUntil[slot] = time + clip.length;
    }

    private void OnDisable()
    {
        foreach (var source in voices) if (source != null) source.Stop();
        for (int i = 0; i < busyUntil.Count; i++) busyUntil[i] = double.NegativeInfinity;
    }
}
