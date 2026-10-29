using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer), typeof(AudioSource))]
public sealed class SheepAgent : MonoBehaviour
{
    private static readonly List<SheepAgent> ActiveSheep = new List<SheepAgent>();

    [SerializeField] private AudioClip bleatClip;
    [SerializeField, Min(0f)] private float moveSpeed = 1.4f;
    [SerializeField, Min(0.1f)] private float separationRadius = 0.8f;
    [SerializeField, Min(0f)] private float separationStrength = 1.5f;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.16f;

    private Rigidbody2D body;
    private SpriteRenderer visual;
    private AudioSource voice;
    private Color originalColor;
    private Vector2 destination;
    private bool hasDestination;
    private Coroutine feedback;

    public Vector2 HomePosition { get; private set; }
    public int ResponseCount { get; private set; }
    public bool IsMoving => body != null && body.linearVelocity.sqrMagnitude > 0.01f;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        visual = GetComponent<SpriteRenderer>();
        voice = GetComponent<AudioSource>();
        originalColor = visual.color;
        HomePosition = body.position;
        destination = HomePosition;
        voice.pitch = Random.Range(0.94f, 1.06f);
    }

    private void OnEnable() => ActiveSheep.Add(this);

    public void MoveTo(Vector2 position)
    {
        destination = position;
        hasDestination = true;
    }

    public void StopMoving()
    {
        hasDestination = false;
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    public void ReturnHome() => MoveTo(HomePosition);

    public void FaceTowards(Vector2 position)
    {
        float delta = position.x - transform.position.x;
        if (Mathf.Abs(delta) > 0.05f) visual.flipX = delta < 0f;
    }

    public void RespondTo(Vector2 caller)
    {
        FaceTowards(caller);
        ResponseCount++;
        if (bleatClip != null) voice.PlayOneShot(bleatClip, voiceVolume);
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowResponse());
    }

    /// <summary>
    /// A quiet answer from far away: audible enough to guide the player, without the close-range
    /// flash or camera-level volume. Used when the flock is outside normal hearing range.
    /// </summary>
    public void RespondDistant(Vector2 caller, float volumeScale = 0.5f)
    {
        FaceTowards(caller);
        ResponseCount++;
        if (bleatClip != null) voice.PlayOneShot(bleatClip, voiceVolume * Mathf.Clamp01(volumeScale));
    }

    private IEnumerator ShowResponse()
    {
        visual.color = new Color(1f, 0.88f, 0.55f, originalColor.a);
        yield return new WaitForSeconds(0.2f);
        visual.color = originalColor;
        feedback = null;
    }

    private void FixedUpdate()
    {
        Vector2 desired = Vector2.zero;
        if (hasDestination)
        {
            Vector2 delta = destination - body.position;
            if (delta.sqrMagnitude < 0.0144f) hasDestination = false;
            else desired = delta.normalized * Mathf.Min(moveSpeed, delta.magnitude * 3f);
        }

        Vector2 separation = Vector2.zero;
        foreach (var other in ActiveSheep)
        {
            if (other == this) continue;
            Vector2 away = body.position - other.body.position;
            float distance = away.magnitude;
            if (distance >= separationRadius) continue;
            if (distance > 0.001f)
                separation += away / distance * (1f - distance / separationRadius);
            else
                separation += GetInstanceID() < other.GetInstanceID() ? Vector2.left : Vector2.right;
        }

        body.linearVelocity = Vector2.ClampMagnitude(desired + separation * separationStrength, moveSpeed);
        if (Mathf.Abs(body.linearVelocity.x) > 0.05f) visual.flipX = body.linearVelocity.x < 0f;
    }

    private void OnDisable()
    {
        ActiveSheep.Remove(this);
        if (body != null) body.linearVelocity = Vector2.zero;
        if (feedback != null) StopCoroutine(feedback);
        feedback = null;
        if (visual != null) visual.color = originalColor;
    }
}
