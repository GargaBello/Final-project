using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController), typeof(AudioSource))]
public sealed class PlayerBleat : MonoBehaviour
{
    [SerializeField] private AudioClip bleatClip;
    [SerializeField, Min(0.01f)] private float cooldown = 0.25f;
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float rhythmVolume = 0.16f;

    private InputAction action;
    private AudioSource voice;
    private SpriteRenderer visual;
    private Color originalColor;
    private float nextBleatTime;
    private Coroutine feedback;
    private UnityEngine.Object rhythmOwner;

    public int BleatCount { get; private set; }
    public SheepGroupController LastHeardGroup { get; private set; }
    public SheepGroupController DistantGroup { get; private set; }
    public event Action<SheepGroupController> Bleated;
    // Once per actual player call, independent of how many distant flocks answer.
    public event Action<double> Called;

    private void Awake()
    {
        voice = GetComponent<AudioSource>();
        visual = GetComponent<SpriteRenderer>();
        originalColor = visual.color;
    }

    private void OnEnable()
    {
        action = GetComponent<PlayerController>().BleatAction;
        if (action != null) action.performed += OnBleatInput;
    }

    private void OnBleatInput(InputAction.CallbackContext context) => TryBleat();

    public void SetRhythmVoice(UnityEngine.Object owner, bool enabled)
    {
        if (enabled)
        {
            rhythmOwner = owner;
            RhythmBleat.GetClip(bleatClip);
        }
        else if (rhythmOwner == owner) rhythmOwner = null;
    }

    public bool TryBleat()
    {
        if (!isActiveAndEnabled || PauseMenu.IsPaused || Time.time < nextBleatTime) return false;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return false;
        nextBleatTime = Time.time + cooldown;
        BleatCount++;
        double calledAt = AudioSettings.dspTime;
        LastHeardGroup = SheepGroupController.FindNearestInRange(transform.position);
        DistantGroup = null;
        var accepting = LastHeardGroup as AcceptingGroup;
        bool shared = accepting != null && accepting.CanShareCall;
        double voiceTime = shared ? calledAt + 0.06 : calledAt;
        bool rhythmic = rhythmOwner != null;
        if (shared)
        {
            float sharedVolume = accepting.PreparePlayerCall(voiceTime);
            voice.Stop();
            FlockCallVoice.For(gameObject).Play(bleatClip, voiceTime, sharedVolume);
            BeatBounce.For(gameObject).PulseAt(voiceTime);
        }
        else
        {
            if (rhythmic) BeatBounce.For(gameObject).Pulse();
            if (rhythmic) { voice.Stop(); voice.pitch = 1f; }
            var clip = rhythmic ? RhythmBleat.GetClip(bleatClip) : bleatClip;
            if (clip != null) voice.PlayOneShot(clip, rhythmic ? rhythmVolume : volume);
        }
        Called?.Invoke(calledAt);
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowFeedback(voiceTime));
        if (LastHeardGroup != null)
        {
            if (!shared) LastHeardGroup.HearBleat(transform);
            Bleated?.Invoke(LastHeardGroup);
        }
        else
        {
            // Each audible flock offers its own answer and direction, so the opening has a choice.
            var groups = SheepGroupController.FindAllDistantInRange(transform.position);
            foreach (var group in groups)
            {
                if (DistantGroup == null) DistantGroup = group;
                group.HearDistantBleat(transform);
                Bleated?.Invoke(group);
            }
            if (groups.Count == 0) Bleated?.Invoke(null);
        }
        return true;
    }

    private IEnumerator ShowFeedback(double time)
    {
        while (AudioSettings.dspTime < time) yield return null;
        visual.color = new Color(1f, 0.82f, 0.42f, originalColor.a);
        yield return new WaitForSeconds(0.2f);
        visual.color = originalColor;
        feedback = null;
    }

    private void OnDisable()
    {
        if (action != null) action.performed -= OnBleatInput;
        if (feedback != null) StopCoroutine(feedback);
        feedback = null;
        rhythmOwner = null;
        if (visual != null) visual.color = originalColor;
    }
}
