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

    private InputAction action;
    private AudioSource voice;
    private SpriteRenderer visual;
    private Color originalColor;
    private float nextBleatTime;
    private Coroutine feedback;

    public int BleatCount { get; private set; }
    public SheepGroupController LastHeardGroup { get; private set; }
    public SheepGroupController DistantGroup { get; private set; }
    public event Action<SheepGroupController> Bleated;

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

    public bool TryBleat()
    {
        if (!isActiveAndEnabled || Time.time < nextBleatTime) return false;
        nextBleatTime = Time.time + cooldown;
        BleatCount++;
        if (bleatClip != null) voice.PlayOneShot(bleatClip, volume);
        if (feedback != null) StopCoroutine(feedback);
        feedback = StartCoroutine(ShowFeedback());

        LastHeardGroup = SheepGroupController.FindNearestInRange(transform.position);
        DistantGroup = null;
        if (LastHeardGroup != null)
        {
            LastHeardGroup.HearBleat(transform);
        }
        else
        {
            // Nothing close enough to walk over: still let the nearest far flock answer quietly,
            // otherwise the opening call reads as broken silence.
            DistantGroup = SheepGroupController.FindNearestDistant(transform.position);
            if (DistantGroup != null) DistantGroup.HearDistantBleat(transform);
        }
        Bleated?.Invoke(LastHeardGroup != null ? LastHeardGroup : DistantGroup);
        return true;
    }

    private IEnumerator ShowFeedback()
    {
        visual.color = Color.Lerp(originalColor, Color.white, 0.65f);
        yield return new WaitForSeconds(0.16f);
        visual.color = originalColor;
        feedback = null;
    }

    private void OnDisable()
    {
        if (action != null) action.performed -= OnBleatInput;
        if (feedback != null) StopCoroutine(feedback);
        feedback = null;
        if (visual != null) visual.color = originalColor;
    }
}
