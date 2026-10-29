using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SheepGroupController : MonoBehaviour
{
    public enum GroupState { Idle, Responding, Together, Requesting, Joined, Ejecting, Refusing }
    private static readonly List<SheepGroupController> ActiveGroups = new List<SheepGroupController>();

    [SerializeField] protected SheepAgent[] members;
    [SerializeField, Min(0f)] protected float hearingRadius = 6f;
    [SerializeField, Min(0f)] protected float joinRadius = 3f;
    [SerializeField, Min(0f)] private float responseCooldown = 2f;
    [SerializeField, Min(0f)] private float approachDistance = 1.2f;

    [Tooltip("Silence kept between the player's call and the flock's first answer, so the answer never lands on the same frame as the call.")]
    [SerializeField, Min(0f)] private float firstResponseDelay = 0.8f;

    [Tooltip("How far away a flock can still be heard answering faintly. The opening call needs this: the player starts alone, out of normal hearing range.")]
    [SerializeField, Min(0f)] private float distantHearingRadius = 45f;

    private Transform caller;
    private Coroutine response;
    private Coroutine distantResponse;
    private float nextResponseTime;
    private float nextDistantTime;
    private bool playerInside;

    public GroupState State { get; protected set; }
    public int HeardBleatCount { get; private set; }
    public IReadOnlyList<SheepAgent> Members => members;
    public bool PlayerInside => playerInside;

    /// <summary>
    /// A flock that has permanently rejected the player stays silent: no close answer and no faint
    /// distant answer either, otherwise "you are done here" would not read as final.
    /// </summary>
    public virtual bool CanAnswer => State != GroupState.Refusing;
    public event Action PlayerEntered;
    public event Action PlayerExited;

    protected void RecordHeardBleat() => HeardBleatCount++;

    protected void SetPlayerInside(bool inside)
    {
        if (inside == playerInside) return;
        playerInside = inside;
        if (inside) PlayerEntered?.Invoke();
        else PlayerExited?.Invoke();
    }

    protected virtual void Awake()
    {
        if (members == null || members.Length == 0) members = GetComponentsInChildren<SheepAgent>();
    }

    protected virtual void OnEnable() => ActiveGroups.Add(this);

    public float DistanceTo(Vector2 position)
    {
        float distance = float.PositiveInfinity;
        foreach (var member in members)
            if (member != null && member.isActiveAndEnabled)
                distance = Mathf.Min(distance, Vector2.Distance(position, member.transform.position));
        return distance;
    }

    public static SheepGroupController FindNearestInRange(Vector2 position)
    {
        SheepGroupController nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var group in ActiveGroups)
        {
            if (!group.CanAnswer) continue;
            float distance = group.DistanceTo(position);
            if (distance <= group.hearingRadius && distance < bestDistance)
            {
                nearest = group;
                bestDistance = distance;
            }
        }
        return nearest;
    }

    public virtual void HearBleat(Transform player)
    {
        if (!CanAnswer) return;
        if (player == null || DistanceTo(player.position) > hearingRadius) return;
        RecordHeardBleat();
        caller = player;
        if (Time.time < nextResponseTime) return;
        nextResponseTime = Time.time + responseCooldown;
        if (response != null) StopCoroutine(response);
        response = StartCoroutine(Respond());
    }

    /// <summary>Nearest flock that can still answer faintly (used for the lonely opening call).</summary>
    public static SheepGroupController FindNearestDistant(Vector2 position)
    {
        SheepGroupController nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var group in ActiveGroups)
        {
            if (!group.CanAnswer) continue;
            float distance = group.DistanceTo(position);
            if (distance <= group.distantHearingRadius && distance < bestDistance)
            {
                nearest = group;
                bestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>All audible flocks offer directions when the player has nobody nearby.</summary>
    public static List<SheepGroupController> FindAllDistantInRange(Vector2 position)
    {
        var groups = new List<SheepGroupController>();
        foreach (var group in ActiveGroups)
            if (group.isActiveAndEnabled && group.CanAnswer && group.DistanceTo(position) <= group.distantHearingRadius)
                groups.Add(group);
        groups.Sort((a, b) => a.DistanceTo(position).CompareTo(b.DistanceTo(position)));
        return groups;
    }

    /// <summary>
    /// The flock is too far to react properly, but the player should still hear that someone is out
    /// there: one or two sheep answer quietly, after a pause, without moving.
    /// </summary>
    public virtual void HearDistantBleat(Transform player)
    {
        if (!CanAnswer) return;
        if (player == null || DistanceTo(player.position) > distantHearingRadius) return;
        RecordHeardBleat();
        if (Time.time < nextDistantTime) return;
        nextDistantTime = Time.time + responseCooldown;
        if (distantResponse != null) StopCoroutine(distantResponse);
        distantResponse = StartCoroutine(DistantAnswer(player));
    }

    private IEnumerator DistantAnswer(Transform player)
    {
        yield return new WaitForSeconds(firstResponseDelay + UnityEngine.Random.Range(0f, 0.15f));

        int voices = 0;
        int maxVoices = UnityEngine.Random.Range(1, 3);
        float best = float.PositiveInfinity;
        SheepAgent closest = null;
        foreach (var member in members)
        {
            if (member == null || !member.isActiveAndEnabled) continue;
            float d = ((Vector2)member.transform.position - (Vector2)player.position).sqrMagnitude;
            if (d < best) { best = d; closest = member; }
        }
        if (closest != null) { closest.RespondDistant(player.position); voices++; }

        foreach (var member in members)
        {
            if (voices >= maxVoices) break;
            if (member == null || member == closest || !member.isActiveAndEnabled) continue;
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.12f, 0.22f));
            member.RespondDistant(player.position, 0.4f);
            voices++;
        }
        distantResponse = null;
    }

    private IEnumerator Respond()
    {
        State = GroupState.Responding;
        if (firstResponseDelay > 0f) yield return new WaitForSeconds(firstResponseDelay);
        foreach (var member in members)
        {
            if (caller == null || DistanceTo(caller.position) > hearingRadius) break;
            if (member == null || !member.isActiveAndEnabled) continue;
            Vector2 delta = (Vector2)caller.position - member.HomePosition;
            member.MoveTo(member.HomePosition + delta.normalized * Mathf.Min(approachDistance, Mathf.Max(0f, delta.magnitude - 2f)));
            member.RespondTo(caller.position);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.08f, 0.14f));
        }
        State = playerInside ? GroupState.Together : GroupState.Idle;
        response = null;
    }

    protected virtual void Update()
    {
        if (caller == null) return;
        bool inside = Vector2.Distance(caller.position, transform.position) <= joinRadius;
        SetPlayerInside(inside);
        if (DistanceTo(caller.position) > hearingRadius)
        {
            if (response != null) StopCoroutine(response);
            response = null;
            foreach (var member in members) if (member != null) member.ReturnHome();
            caller = null;
            State = GroupState.Idle;
        }
        else if (response == null) State = playerInside ? GroupState.Together : GroupState.Idle;
    }

    protected virtual void OnDisable()
    {
        ActiveGroups.Remove(this);
        if (response != null) StopCoroutine(response);
        if (distantResponse != null) StopCoroutine(distantResponse);
        response = null;
        distantResponse = null;
        caller = null;
        playerInside = false;
        State = GroupState.Idle;
    }
}
