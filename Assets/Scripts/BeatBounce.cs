using UnityEngine;

// Keep an eight-beat squash pulse, with the bottom edge pinned in place.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BeatBounce : MonoBehaviour
{
    private static readonly float[] EightBeatStrength = { 0.16f, 0.12f, 0.12f, 0.12f, 0.16f, 0.12f, 0.12f, 0.12f };
    private SpriteRenderer source;
    private SpriteRenderer display;
    private Transform visualRoot;
    private bool sourceWasEnabled;
    private Object rhythmOwner;
    private double rhythmStart;
    private float beatDuration = 0.6f;
    private double pulseStart = double.NegativeInfinity;

    public static BeatBounce For(GameObject sheep)
    {
        var bounce = sheep.GetComponent<BeatBounce>();
        return bounce != null ? bounce : sheep.AddComponent<BeatBounce>();
    }

    public void SetRhythm(Object owner, double start, float beat)
    {
        rhythmOwner = owner;
        rhythmStart = start;
        beatDuration = Mathf.Max(0.05f, beat);
    }

    public void StopRhythm(Object owner)
    {
        if (rhythmOwner != owner) return;
        rhythmOwner = null;
        pulseStart = double.NegativeInfinity;
    }

    public void Pulse()
    {
        // Calling between beats must not reset the flock's fixed movement clock.
        if (rhythmOwner == null) pulseStart = AudioSettings.dspTime;
    }

    public void PulseAt(double time)
    {
        if (rhythmOwner == null) pulseStart = time;
    }

    private void EnsureVisual()
    {
        if (source == null) source = GetComponent<SpriteRenderer>();
        if (visualRoot != null) { source.enabled = false; return; }
        sourceWasEnabled = source.enabled;
        var child = new GameObject("RhythmVisual");
        visualRoot = child.transform;
        visualRoot.SetParent(transform, false);
        display = child.AddComponent<SpriteRenderer>();
        source.enabled = false;
    }

    private void LateUpdate()
    {
        double now = AudioSettings.dspTime;
        float duration = Mathf.Clamp(beatDuration * 0.5f, 0.1f, 0.24f);
        float pulse;
        float strength = 0.16f;
        if (rhythmOwner != null)
        {
            double elapsed = now - rhythmStart;
            if (elapsed < 0) pulse = -1f;
            else
            {
                double beats = elapsed / beatDuration;
                int beat = (int)(System.Math.Floor(beats) % 8);
                strength = EightBeatStrength[beat];
                pulse = (float)((beats - System.Math.Floor(beats)) * beatDuration) / duration;
            }
        }
        else pulse = (float)(now - pulseStart) / duration;
        // Compression peaks exactly on the beat, even when that beat has no call.
        float compression = pulse >= 0f && pulse < 1f ? strength * (1f - Mathf.SmoothStep(0f, 1f, pulse)) : 0f;
        if (visualRoot == null && compression < 0.001f) return;
        EnsureVisual();
        float verticalScale = 1f - compression;
        float bottom = source.sprite != null
            ? (source.flipY ? -source.sprite.bounds.max.y : source.sprite.bounds.min.y) : 0f;
        visualRoot.localScale = new Vector3(1f, verticalScale, 1f);
        visualRoot.localPosition = Vector3.up * (bottom * (1f - verticalScale));
        display.sprite = source.sprite;
        display.color = source.color;
        display.flipX = source.flipX;
        display.flipY = source.flipY;
        display.sharedMaterial = source.sharedMaterial;
        display.sortingLayerID = source.sortingLayerID;
        display.sortingOrder = source.sortingOrder;
        display.spriteSortPoint = source.spriteSortPoint;
        display.maskInteraction = source.maskInteraction;
        display.enabled = sourceWasEnabled;
    }

    private void OnDisable()
    {
        rhythmOwner = null;
        pulseStart = double.NegativeInfinity;
        if (source != null && visualRoot != null) source.enabled = sourceWasEnabled;
        if (display != null) display.enabled = false;
        if (visualRoot != null)
        {
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localScale = Vector3.one;
        }
    }
}
