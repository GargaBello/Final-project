using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows a short "sound wave" cue at the screen edge in the direction a sheep group answers from.
/// Pure feedback: it subscribes to <see cref="PlayerBleat.Bleated"/> and never changes gameplay state.
/// </summary>
public sealed class BleatEchoIndicator : MonoBehaviour
{
    [SerializeField] private PlayerBleat bleat;
    [SerializeField] private Sprite waveSprite;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private SheepGroupController[] groups;

    [Header("Timing")]
    [Tooltip("The cue must land with the answer, not with the player's call. Keep this in sync with the flock's reply delay.")]
    [SerializeField, Min(0f)] private float cueDelay = 0.8f;
    [SerializeField, Min(1)] private int pulses = 3;
    [SerializeField, Min(0.05f)] private float pulseDuration = 0.7f;
    [SerializeField, Min(0f)] private float pulseInterval = 0.15f;

    [Header("Placement")]
    [SerializeField, Min(0f)] private float edgeMargin = 0.07f;
    [SerializeField, Min(0f)] private float arcScale = 0.85f;
    [SerializeField, Min(0f)] private float driftPerPulse = 0.3f;

    [Header("Opacity")]
    [SerializeField, Range(0f, 1f)] private float answerAlpha = 0.9f;
    [SerializeField, Range(0f, 1f)] private float lonelyAlpha = 0.35f;

    private readonly List<SheepGroupController> runtimeGroups = new List<SheepGroupController>();

    private void Awake()
    {
        if (bleat == null) bleat = Object.FindFirstObjectByType<PlayerBleat>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (bleat != null) bleat.Bleated += OnBleated;
    }

    private void OnDisable()
    {
        if (bleat != null) bleat.Bleated -= OnBleated;
    }

    private void OnBleated(SheepGroupController responder)
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null || waveSprite == null) return;
        if (responder == null) return; // silence on both channels is the honest answer here

        // The waves belong to the answer, not to the call: hold them back by the same pause.
        if (cueDelay > 0f) StartCoroutine(ShowAfterDelay(responder, cueDelay));
        else ShowCueAt(responder.transform.position, answerAlpha);
    }

    private System.Collections.IEnumerator ShowAfterDelay(SheepGroupController responder, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (responder != null) ShowCueAt(responder.transform.position, answerAlpha);
    }

    /// <summary>Cue pointing at an arbitrary world position. Useful for scripted or distant answers.</summary>
    public void ShowCueAt(Vector3 worldTarget, float alpha)
    {
        if (worldCamera == null || waveSprite == null) return;

        Vector3 viewport = worldCamera.WorldToViewportPoint(worldTarget);
        bool onScreen = viewport.z > 0f
                        && viewport.x > edgeMargin && viewport.x < 1f - edgeMargin
                        && viewport.y > edgeMargin && viewport.y < 1f - edgeMargin;
        if (onScreen) return;

        Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        direction.Normalize();

        float half = 0.5f - edgeMargin;
        float tX = Mathf.Abs(direction.x) < 1e-4f ? float.PositiveInfinity : half / Mathf.Abs(direction.x);
        float tY = Mathf.Abs(direction.y) < 1e-4f ? float.PositiveInfinity : half / Mathf.Abs(direction.y);
        float t = Mathf.Min(tX, tY);

        Vector2 edge = new Vector2(0.5f, 0.5f) + direction * t;
        Vector3 world = worldCamera.ViewportToWorldPoint(new Vector3(edge.x, edge.y, -worldCamera.transform.position.z));
        world.z = 0f;

        // The arc sprite opens towards -X, so turn it so the opening faces the flock.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 180f;
        StartCoroutine(PlayPulses(world, angle, alpha));
    }

    private SheepGroupController NearestGroup()
    {
        runtimeGroups.Clear();
        runtimeGroups.AddRange(Object.FindObjectsByType<SheepGroupController>(FindObjectsSortMode.None));
        if (groups != null && groups.Length > 0)
            foreach (var extra in groups) if (extra != null && !runtimeGroups.Contains(extra)) runtimeGroups.Add(extra);

        Vector3 from = bleat != null ? bleat.transform.position : Vector3.zero;
        SheepGroupController best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var group in runtimeGroups)
        {
            if (group == null || !group.isActiveAndEnabled) continue;
            float distance = Vector2.Distance(from, group.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = group;
            }
        }
        return best;
    }

    private IEnumerator PlayPulses(Vector3 position, float angle, float alpha)
    {
        var root = new GameObject("BleatEcho");
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));

        for (int i = 0; i < pulses; i++)
        {
            var pulse = new GameObject("Pulse" + i);
            pulse.transform.SetParent(root.transform, false);
            var renderer = pulse.AddComponent<SpriteRenderer>();
            renderer.sprite = waveSprite;
            renderer.sortingOrder = 30000;
            StartCoroutine(FadePulse(pulse.transform, renderer, i, alpha));
            yield return new WaitForSeconds(pulseInterval);
        }

        yield return new WaitForSeconds(pulseDuration - pulseInterval * (pulses - 1));
        if (root != null) Destroy(root);
    }

    private IEnumerator FadePulse(Transform pulse, SpriteRenderer renderer, int index, float alpha)
    {
        float start = Time.time;
        float delay = index * pulseInterval;
        Color color = renderer.color;
        while (Time.time - start < pulseDuration)
        {
            float k = Mathf.Clamp01((Time.time - start - delay) / Mathf.Max(0.01f, pulseDuration - delay));
            color.a = Mathf.Lerp(alpha, 0f, k);
            renderer.color = color;
            float scale = Mathf.Lerp(arcScale, arcScale * 1.25f, k);
            pulse.localScale = new Vector3(scale, scale, 1f);
            pulse.localPosition = new Vector3(k * driftPerPulse, 0f, 0f);
            yield return null;
        }
        if (pulse != null) Destroy(pulse.gameObject);
    }
}
