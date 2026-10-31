using UnityEngine;

// Identical playback for the player and accepting sheep, scheduled on a shared audio clock.
public sealed class FlockCallVoice : MonoBehaviour
{
    private readonly AudioSource[] voices = new AudioSource[2];
    private int nextVoice;

    public static FlockCallVoice For(GameObject sheep)
    {
        var voice = sheep.GetComponent<FlockCallVoice>();
        return voice != null ? voice : sheep.AddComponent<FlockCallVoice>();
    }

    public void Play(AudioClip original, double time, float volume)
    {
        var clip = RhythmBleat.GetClip(original);
        if (clip == null) return;
        if (voices[nextVoice] == null)
        {
            voices[nextVoice] = gameObject.AddComponent<AudioSource>();
            voices[nextVoice].playOnAwake = false;
            voices[nextVoice].spatialBlend = 0f;
            voices[nextVoice].dopplerLevel = 0f;
        }
        var source = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        source.Stop();
        source.clip = clip;
        source.pitch = 1f;
        source.volume = volume;
        source.PlayScheduled(time);
    }

    private void OnDisable()
    {
        foreach (var source in voices) if (source != null) source.Stop();
    }
}
