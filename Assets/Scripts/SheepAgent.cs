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
    private AudioSource rhythmVoice;
    private AudioSource alternateRhythmVoice;
    private bool useAlternateRhythmVoice;
    private Color originalColor;
    private Color initialColor;
    private Vector2 destination;
    private bool hasDestination;
    private float destinationSpeed;
    private Coroutine feedback;
    private Coroutine acceptingFeedback;
    private Object flockOwner;
    private Vector2 flockTarget;
    private Vector2 flockVelocity;
    private float flockSync;
    private bool flockFacingLeft;
    private bool movementFrozen;

    public Vector2 HomePosition { get; private set; }
    public int ResponseCount { get; private set; }
    public bool IsMoving => body != null && body.linearVelocity.sqrMagnitude > 0.01f;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        visual = GetComponent<SpriteRenderer>();
        voice = GetComponent<AudioSource>();
        originalColor = visual.color;
        initialColor = visual.color;
        HomePosition = body.position;
        destination = HomePosition;
        destinationSpeed = moveSpeed;
        voice.pitch = Random.Range(0.94f, 1.06f);
    }

    private void OnEnable() => ActiveSheep.Add(this);

    public void MoveTo(Vector2 position)
    {
        destination = position;
        destinationSpeed = moveSpeed;
        hasDestination = true;
    }

    public void MoveTo(Vector2 position, float speed)
    {
        MoveTo(position);
        destinationSpeed = Mathf.Max(0f, speed);
    }

    public void StopMoving()
    {
        hasDestination = false;
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    public void SetMovementFrozen(bool frozen)
    {
        movementFrozen = frozen;
        if (frozen) StopMoving();
    }

    public void ReturnHome() => MoveTo(HomePosition);

    public void FollowFlock(Object owner, Vector2 target, Vector2 velocity, float synchronization, bool facingLeft)
    {
        flockOwner = owner;
        flockTarget = target;
        flockVelocity = velocity;
        flockSync = Mathf.Clamp01(synchronization);
        flockFacingLeft = facingLeft;
    }

    public void LeaveFlockMotion(Object owner)
    {
        if (flockOwner != owner) return;
        flockOwner = null;
        StopMoving();
    }

    public void AcceptingCall(double time, float volume, Vector2 caller, bool facingLeft, bool synchronized)
    {
        voice.Stop();
        FlockCallVoice.For(gameObject).Play(bleatClip, time, volume);
        if (acceptingFeedback != null) StopCoroutine(acceptingFeedback);
        acceptingFeedback = StartCoroutine(AcceptingFeedbackAt(time, caller, facingLeft, synchronized));
    }

    private IEnumerator AcceptingFeedbackAt(double time, Vector2 caller, bool facingLeft, bool synchronized)
    {
        while (AudioSettings.dspTime < time) yield return null;
        if (synchronized) visual.flipX = facingLeft;
        else FaceTowards(caller);
        ResponseCount++;
        BeatBounce.For(gameObject).PulseAt(time);
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowResponse(0.2f, true));
        acceptingFeedback = null;
    }

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

    public void RespondRhythmTo(Vector2 caller)
    {
        PrepareRhythmVoice();
        if (rhythmVoice.clip != null) rhythmVoice.Play();
        ShowRhythmResponse(caller);
    }

    public void PrepareRhythmVoice()
    {
        if (rhythmVoice == null)
        {
            rhythmVoice = CreateRhythmVoice();
        }
        if (alternateRhythmVoice == null) alternateRhythmVoice = CreateRhythmVoice();
        var clip = RhythmBleat.GetClip(bleatClip);
        if (rhythmVoice.clip != clip) rhythmVoice.clip = clip;
        if (alternateRhythmVoice.clip != clip) alternateRhythmVoice.clip = clip;
        rhythmVoice.volume = voiceVolume;
        alternateRhythmVoice.volume = voiceVolume;
    }

    private AudioSource CreateRhythmVoice()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.pitch = 1f;
        source.dopplerLevel = 0f;
        return source;
    }

    public void ScheduleRhythm(double time)
    {
        PrepareRhythmVoice();
        // Preparing a future phrase must not truncate the same sheep's current bleat.
        var source = useAlternateRhythmVoice ? alternateRhythmVoice : rhythmVoice;
        useAlternateRhythmVoice = !useAlternateRhythmVoice;
        if (source.clip != null) source.PlayScheduled(time);
    }

    public void CancelRhythm()
    {
        if (rhythmVoice != null) rhythmVoice.Stop();
        if (alternateRhythmVoice != null) alternateRhythmVoice.Stop();
        useAlternateRhythmVoice = false;
    }

    /// <summary>
    /// Schedules one chorus note on the DSP clock with an explicit pitch and level. Same trimmed clip
    /// the rhythm group uses, so the attack lands on the beat instead of the breath before it.
    /// The visual flash waits on the same DSP time, so the eye and the ear agree.
    /// </summary>
    public static int ChorusNotesStarted;
    public static int ChorusNotesDropped;

    public void ScheduleChorus(double time, float pitch, float volume, Vector2 caller)
    {
        PrepareRhythmVoice();
        var source = useAlternateRhythmVoice ? alternateRhythmVoice : rhythmVoice;
        useAlternateRhythmVoice = !useAlternateRhythmVoice;
        source.pitch = Mathf.Clamp(pitch, 0.4f, 2.2f);
        source.volume = Mathf.Clamp01(volume);
        if (source.clip != null)
        {
            source.PlayScheduled(time);
            ChorusNotesStarted++;
        }
        else
        {
            ChorusNotesDropped++;
        }
        StartCoroutine(ChorusFlashAt(time, caller));
    }

    private IEnumerator ChorusFlashAt(double time, Vector2 caller)
    {
        while (AudioSettings.dspTime < time) yield return null;
        BeatBounce.For(gameObject).PulseAt(time);
        FaceTowards(caller);
        ResponseCount++;
        if (feedback != null) StopCoroutine(feedback);
        // Short flash: a long glow reads as if the sound came after the light.
        feedback = StartCoroutine(ShowResponse(0.12f));
    }

    /// <summary>
    /// Visual half of a chorus note: the audio comes from one shared pool, but each sheep still has
    /// to light up on its own beat.
    /// </summary>
    public void ScheduleChorusFlash(double time, Vector2 caller)
    {
        StartCoroutine(ChorusFlashAt(time, caller));
    }

    public void ShowRhythmResponse(Vector2 caller)
    {
        BeatBounce.For(gameObject).Pulse();
        FaceTowards(caller);
        ResponseCount++;
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowResponse(0.11f));
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

    /// <summary>
    /// Marks this sheep as part of a flock the player can no longer join: it keeps its silhouette but
    /// loses colour and brightness, so "closed off" is readable without a word of UI.
    /// </summary>
    public void SetClosed(bool closed, float brightness = 0.55f)
    {
        Color baseline = closed
            ? new Color(brightness, brightness, brightness * 1.04f, originalColor.a)
            : initialColor;
        originalColor = baseline;
        if (feedback != null) { StopCoroutine(feedback); feedback = null; }
        if (acceptingFeedback != null) { StopCoroutine(acceptingFeedback); acceptingFeedback = null; }
        if (visual != null) visual.color = baseline;
    }

    /// <summary>
    /// One voice in the closing chorus: explicit pitch and volume so a dozen sheep can form a chord
    /// instead of the same random bleat twelve times.
    /// </summary>
    public void ChorusBleat(float volume, float pitch, bool flash)
    {
        ResponseCount++;
        if (bleatClip != null)
        {
            // Keep the original octave-safe range: anything lower turns the recorded bleat into a growl.
            voice.pitch = Mathf.Clamp(pitch, 0.6f, 1.6f);
            voice.PlayOneShot(bleatClip, Mathf.Clamp01(volume));
        }
        if (!flash) return;
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowResponse(0.26f));
    }

    private IEnumerator ShowResponse(float duration = 0.2f, bool accepting = false)
    {
        visual.color = accepting ? new Color(1f, 0.82f, 0.42f, originalColor.a)
            : new Color(1f, 0.88f, 0.55f, originalColor.a);
        yield return new WaitForSeconds(duration);
        visual.color = originalColor;
        feedback = null;
    }

    private void FixedUpdate()
    {
        if (movementFrozen)
        {
            body.linearVelocity = Vector2.zero;
            return;
        }
        Vector2 desired = Vector2.zero;
        float speedLimit = hasDestination ? destinationSpeed : moveSpeed;
        if (flockOwner != null)
        {
            Vector2 error = flockTarget - body.position;
            Vector2 correction = error.sqrMagnitude < 0.0009f ? Vector2.zero
                : Vector2.ClampMagnitude(error * Mathf.Lerp(1.6f, 8f, flockSync), Mathf.Lerp(0.9f, 3.8f, flockSync));
            desired = flockVelocity * flockSync + correction;
            speedLimit = Mathf.Lerp(1f, 5f, flockSync);
        }
        else if (hasDestination)
        {
            Vector2 delta = destination - body.position;
            if (delta.sqrMagnitude < 0.0144f) hasDestination = false;
            else desired = delta.normalized * Mathf.Min(destinationSpeed, delta.magnitude * 3f);
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

        float separationAmount = flockOwner != null ? 1f - flockSync : 1f;
        body.linearVelocity = Vector2.ClampMagnitude(desired + separation * separationStrength * separationAmount, speedLimit);
        if (flockOwner != null && flockSync >= 0.999f) visual.flipX = flockFacingLeft;
        else if (Mathf.Abs(body.linearVelocity.x) > 0.05f) visual.flipX = body.linearVelocity.x < 0f;
    }

    private void OnDisable()
    {
        CancelRhythm();
        flockOwner = null;
        movementFrozen = false;
        if (acceptingFeedback != null) StopCoroutine(acceptingFeedback);
        acceptingFeedback = null;
        ActiveSheep.Remove(this);
        if (body != null) body.linearVelocity = Vector2.zero;
        if (feedback != null) StopCoroutine(feedback);
        feedback = null;
        if (visual != null) visual.color = originalColor;
    }
}
