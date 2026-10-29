using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class DividedGroup : SheepGroupController
{
    public enum Phase { Idle, PartialResponse, Separating, Together, Leaving, Silent }

    [SerializeField] private Transform player;
    [SerializeField] private SheepAgent[] responsiveSheep;
    [SerializeField, Min(0f)] private float greetingRadius = 4.5f;
    [SerializeField] private Vector2 retreatOffset = new Vector2(4f, 10f / 3f);
    [SerializeField, Min(0f)] private float separationDuration = 6f;
    [SerializeField, Min(0f)] private float companionshipDuration = 5f;
    [SerializeField, Min(0f)] private float departureInterval = 2.5f;
    [SerializeField, Min(0f)] private float lookingDuration = 1.1f;
    [SerializeField, Min(0f)] private float flockCallReactionDelay = 0.35f;
    [SerializeField, Min(0f)] private float recallResponseDelay = 0.3f;
    [SerializeField, Min(0f)] private float recallPauseDuration = 1.1f;
    [SerializeField, Min(0f)] private float answerDelay = 0.8f;
    [SerializeField, Min(0f)] private float answerCooldown = 2f;
    [Tooltip("Distance at which approaching the separated flock triggers avoidance.")]
    [SerializeField, Min(0f)] private float avoidanceDistance = 8f;
    [SerializeField, Min(0f)] private float avoidanceStepDistance = 4f;
    [SerializeField, Min(0f)] private float retreatSpeed = 1.8f;
    [SerializeField] private Vector2 pastureMin = new Vector2(-37f, -27f);
    [SerializeField] private Vector2 pastureMax = new Vector2(37f, 27f);

    private readonly List<SheepAgent> majority = new List<SheepAgent>();
    private readonly List<Vector2> majorityOffsets = new List<Vector2>();
    private Vector2[] responsiveOffsets;
    private Vector2 initialCenter;
    private Vector2 flockCenter;
    private Vector2 companionCenter;
    private float phaseStarted;
    private float nextAnswer;
    private float nextDeparture;
    private float departureStarted;
    private int returnedCount;
    private bool departing;
    private bool hasAnswered;
    private Coroutine answer;
    private Coroutine recall;
    private bool recallUsed;
    private float recallPauseUntil;
    private Transform recallCaller;
    private Vector2 previousPlayerPosition;
    private bool hasPreviousPlayerPosition;
    private bool avoidingPlayer;
    private Transform departureCaller;

    public Phase CurrentPhase { get; private set; }
    public override bool CanAnswer => CurrentPhase != Phase.Silent;

    protected override void Awake()
    {
        base.Awake();
        if (player == null)
        {
            var controller = FindFirstObjectByType<PlayerController>();
            if (controller != null) player = controller.transform;
        }
        initialCenter = transform.position;
        flockCenter = ClampToPasture(initialCenter + retreatOffset);
        companionCenter = initialCenter;
        if (responsiveSheep == null || responsiveSheep.Length == 0)
        {
            var selected = new List<SheepAgent>();
            for (int i = 1; i < members.Length && selected.Count < 3; i += 4)
                if (members[i] != null) selected.Add(members[i]);
            responsiveSheep = selected.ToArray();
        }
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] == null) continue;
            if (System.Array.IndexOf(responsiveSheep, members[i]) < 0)
            {
                majority.Add(members[i]);
                majorityOffsets.Add((Vector2)members[i].transform.position - initialCenter);
            }
        }
        responsiveOffsets = new Vector2[responsiveSheep.Length];
        for (int i = 0; i < responsiveSheep.Length; i++)
            if (responsiveSheep[i] != null)
                responsiveOffsets[i] = (Vector2)responsiveSheep[i].transform.position - initialCenter;
    }

    public override void HearBleat(Transform caller)
    {
        if (!CanAnswer || caller == null || DistanceTo(caller.position) > hearingRadius) return;
        player = caller;
        RecordHeardBleat();
        if (TryRecallDepartingSheep(caller)) return;
        QueueAnswer(caller, false);
    }

    private bool TryRecallDepartingSheep(Transform caller)
    {
        if (!departing || returnedCount >= responsiveSheep.Length || recallUsed || recall != null
            || Time.time - departureStarted < lookingDuration) return false;
        var sheep = responsiveSheep[returnedCount];
        if (sheep == null || !sheep.isActiveAndEnabled
            || Vector2.Distance(sheep.transform.position, caller.position) > hearingRadius) return false;
        recall = StartCoroutine(RecallDepartingSheep(sheep, returnedCount, caller));
        return true;
    }

    private IEnumerator RecallDepartingSheep(SheepAgent sheep, int index, Transform caller)
    {
        yield return new WaitForSeconds(recallResponseDelay);
        if (departing && returnedCount == index && caller != null && sheep != null
            && sheep.isActiveAndEnabled
            && Vector2.Distance(sheep.transform.position, caller.position) <= hearingRadius)
        {
            // Each departing sheep hesitates once. More calls cannot extend the pause.
            recallUsed = true;
            recallCaller = caller;
            recallPauseUntil = Time.time + recallPauseDuration;
            sheep.StopMoving();
            sheep.RespondTo(caller.position);
        }
        recall = null;
    }

    // Distant answers only guide the player. They never start the separation sequence.
    public override void HearDistantBleat(Transform caller)
    {
        if (!CanAnswer || caller == null || DistanceTo(caller.position) > 45f) return;
        RecordHeardBleat();
        QueueAnswer(caller, true);
    }

    private void QueueAnswer(Transform caller, bool distant)
    {
        if (answer != null || Time.time < nextAnswer) return;
        nextAnswer = Time.time + answerCooldown;
        answer = StartCoroutine(Answer(caller, distant));
    }

    private IEnumerator Answer(Transform caller, bool distant)
    {
        yield return new WaitForSeconds(answerDelay);
        for (int i = 0; i < responsiveSheep.Length; i++)
        {
            if (!CanAnswer || caller == null) break;
            var sheep = responsiveSheep[i];
            if (sheep == null || !sheep.isActiveAndEnabled || i < returnedCount || (departing && i == returnedCount)) continue;
            float distance = Vector2.Distance(sheep.transform.position, caller.position);
            if (distance > (distant ? 45f : hearingRadius)) continue;
            if (distant) sheep.RespondDistant(caller.position, 0.4f);
            else
            {
                sheep.RespondTo(caller.position);
                hasAnswered = true;
                if (CurrentPhase == Phase.Idle) ChangePhase(Phase.PartialResponse);
            }
            yield return new WaitForSeconds(0.18f + i * 0.04f);
        }
        answer = null;
    }

    protected override void Update()
    {
        bool nearPlayer = player != null && Vector2.Distance(player.position, initialCenter) <= joinRadius;
        SetPlayerInside(CurrentPhase != Phase.Silent && nearPlayer);
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.PartialResponse)
        {
            // Arriving is enough to receive the first welcome; a player call is optional.
            if (!hasAnswered && player != null
                && Vector2.Distance(player.position, initialCenter) <= Mathf.Max(greetingRadius, joinRadius))
                QueueAnswer(player, false);

            // The welcome happens before entry starts the separation, regardless of who called first.
            if (hasAnswered && nearPlayer)
            {
                companionCenter = player.position;
                ChangePhase(Phase.Separating);
            }
            return;
        }

        UpdateFlockAvoidance();
        if (CurrentPhase == Phase.Silent)
        {
            MoveMajority();
            for (int i = 0; i < responsiveSheep.Length; i++) MoveReturnedSheep(i);
            return;
        }

        MoveMajority();
        if (player != null)
            companionCenter = Vector2.MoveTowards(companionCenter, ClampToPasture(player.position), 3f * Time.deltaTime);

        for (int i = returnedCount; i < responsiveSheep.Length; i++)
        {
            var sheep = responsiveSheep[i];
            if (sheep == null) continue;
            if (departing && i == returnedCount)
            {
                if (Time.time < recallPauseUntil)
                {
                    sheep.StopMoving();
                    if (recallCaller != null) sheep.FaceTowards(recallCaller.position);
                }
                else if (Time.time < departureStarted)
                {
                    // Hear the flock first; the turn follows the call after a small reaction delay.
                    sheep.StopMoving();
                }
                else if (Time.time - departureStarted < lookingDuration)
                {
                    sheep.StopMoving();
                    sheep.FaceTowards(departureCaller != null ? (Vector2)departureCaller.position : ActualFlockCenter());
                }
                else
                {
                    MoveReturnedSheep(i);
                    if (HasReachedFlock(sheep, i))
                    {
                        returnedCount++;
                        departing = false;
                        nextDeparture = Time.time + departureInterval;
                    }
                }
            }
            else
            {
                float angle = (i * 110f + 30f) * Mathf.Deg2Rad;
                sheep.MoveTo(ClampToPasture(companionCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.35f));
                if (player != null && !sheep.IsMoving) sheep.FaceTowards(player.position);
            }
        }
        for (int i = 0; i < returnedCount; i++) MoveReturnedSheep(i);

        if (CurrentPhase == Phase.Separating && Time.time - phaseStarted >= separationDuration)
            ChangePhase(Phase.Together);
        else if (CurrentPhase == Phase.Together && Time.time - phaseStarted >= companionshipDuration)
        {
            ChangePhase(Phase.Leaving);
            nextDeparture = Time.time;
        }
        if (CurrentPhase != Phase.Leaving) return;
        if (returnedCount >= responsiveSheep.Length)
        {
            ChangePhase(Phase.Silent);
            SetPlayerInside(false);
        }
        else if (!departing && Time.time >= nextDeparture)
        {
            // Calling again cannot reset this schedule or bring a returned sheep back.
            if (responsiveSheep[returnedCount] == null) returnedCount++;
            else
            {
                departing = true;
                departureCaller = CallCompanionBack(responsiveSheep[returnedCount]);
                responsiveSheep[returnedCount].StopMoving();
                departureStarted = Time.time + flockCallReactionDelay;
                recallUsed = false;
                recallPauseUntil = 0f;
                recallCaller = null;
            }
        }
    }

    private Transform CallCompanionBack(SheepAgent companion)
    {
        SheepAgent caller = null;
        float nearest = float.PositiveInfinity;
        foreach (var sheep in majority)
        {
            if (sheep == null || !sheep.isActiveAndEnabled) continue;
            float distance = ((Vector2)sheep.transform.position - (Vector2)companion.transform.position).sqrMagnitude;
            if (distance < nearest) { nearest = distance; caller = sheep; }
        }
        if (caller == null) return null;
        caller.RespondTo(companion.transform.position);
        return caller.transform;
    }

    private void MoveMajority()
    {
        for (int i = 0; i < majority.Count; i++)
        {
            if (majority[i] == null) continue;
            if (CurrentPhase == Phase.Separating && Time.time - phaseStarted < i * 0.14f) continue;
            majority[i].MoveTo(ClampToPasture(flockCenter + majorityOffsets[i]));
        }
    }

    private Vector2 ActualFlockCenter()
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        foreach (var sheep in majority)
            if (sheep != null) { sum += (Vector2)sheep.transform.position; count++; }
        return count > 0 ? sum / count : flockCenter;
    }

    private void UpdateFlockAvoidance()
    {
        Vector2 playerMovement = Vector2.zero;
        if (player != null)
        {
            Vector2 position = player.position;
            if (hasPreviousPlayerPosition) playerMovement = position - previousPlayerPosition;
            previousPlayerPosition = position;
            hasPreviousPlayerPosition = true;
        }
        // The first departure has its own shorter destination. Proximity must not extend it.
        if (CurrentPhase == Phase.Separating) return;

        Vector2 center = ActualFlockCenter();
        if (!avoidingPlayer && player != null)
        {
            Vector2 towardFlock = center - (Vector2)player.position;
            if (towardFlock.magnitude < avoidanceDistance
                && Vector2.Dot(playerMovement, towardFlock.normalized) > 0.0001f)
                avoidingPlayer = true;
        }
        if (!avoidingPlayer) return;
        Vector2 away = Vector2.zero;
        if (player != null) AddThreat(player.position, center, ref away);
        // Companions belong to the player's small group until they decide to return.
        for (int i = returnedCount; i < responsiveSheep.Length; i++)
            if (responsiveSheep[i] != null && !(departing && i == returnedCount))
                AddThreat(responsiveSheep[i].transform.position, center, ref away);
        if (away.sqrMagnitude < 0.001f)
        {
            avoidingPlayer = false;
            return;
        }

        // Do not send the destination farther and farther ahead of the moving sheep.
        Vector2 destination = center + away.normalized * avoidanceStepDistance;
        Vector2 bounded = ClampFlockCenter(destination);
        if ((bounded - center).sqrMagnitude < 0.5f)
        {
            // At the fence, turn along it toward open space instead of bunching into a corner.
            Vector2 tangent = new Vector2(-away.y, away.x).normalized;
            Vector2 left = ClampFlockCenter(center + tangent * avoidanceStepDistance);
            Vector2 right = ClampFlockCenter(center - tangent * avoidanceStepDistance);
            bounded = left.sqrMagnitude < right.sqrMagnitude ? left : right;
        }
        flockCenter = Vector2.MoveTowards(flockCenter, bounded, retreatSpeed * Time.deltaTime);
    }

    private void AddThreat(Vector2 threat, Vector2 center, ref Vector2 away)
    {
        Vector2 delta = center - threat;
        float distance = delta.magnitude;
        if (distance >= avoidanceDistance) return;
        away += (distance > 0.05f ? delta / distance : retreatOffset.normalized)
            * (1f - distance / avoidanceDistance);
    }

    private Vector2 ClampFlockCenter(Vector2 point) => new Vector2(
        Mathf.Clamp(point.x, pastureMin.x + 3f, pastureMax.x - 3f),
        Mathf.Clamp(point.y, pastureMin.y + 3f, pastureMax.y - 3f));

    private Vector2 ReturnSlot(int index)
    {
        // A returning sheep aims at the actual moving flock, rather than chasing its distant target.
        Vector2 center = index < returnedCount || CurrentPhase == Phase.Silent ? flockCenter : ActualFlockCenter();
        return ClampToPasture(center + responsiveOffsets[index]);
    }

    private bool HasReachedFlock(SheepAgent sheep, int index)
    {
        if (Vector2.Distance(sheep.transform.position, ReturnSlot(index)) < 0.65f) return true;
        foreach (var other in majority)
            if (other != null && Vector2.Distance(sheep.transform.position, other.transform.position) < 1.25f)
                return true;
        return false;
    }

    private void MoveReturnedSheep(int index)
    {
        if (responsiveSheep[index] != null) responsiveSheep[index].MoveTo(ReturnSlot(index));
    }

    private Vector2 ClampToPasture(Vector2 point) => new Vector2(
        Mathf.Clamp(point.x, pastureMin.x, pastureMax.x), Mathf.Clamp(point.y, pastureMin.y, pastureMax.y));

    private void ChangePhase(Phase phase)
    {
        CurrentPhase = phase;
        phaseStarted = Time.time;
        State = phase == Phase.Silent ? GroupState.Refusing
            : phase == Phase.Idle ? GroupState.Idle
            : phase == Phase.PartialResponse ? GroupState.Responding : GroupState.Together;
    }

    protected override void OnDisable()
    {
        if (answer != null) StopCoroutine(answer);
        answer = null;
        if (recall != null) StopCoroutine(recall);
        recall = null;
        recallPauseUntil = 0f;
        recallCaller = null;
        base.OnDisable();
    }
}
