using System;
using UnityEngine;

public sealed class DemandingGroup : SheepGroupController
{
    [SerializeField] private PlayerController player;
    [SerializeField] private FlowerInventory inventory;
    [SerializeField] private FlowerCollectible[] flowers;
    [SerializeField] private int[] flowerDemands = { 1, 2, 3 };
    [SerializeField, Min(0.5f)] private float welcomeDuration = 6f;
    [SerializeField, Min(0f)] private float pushSpeed = 4f;
    [SerializeField, Min(0.01f)] private float pushDuration = 0.25f;
    [SerializeField, Min(0.25f)] private float pushCooldown = 1.5f;
    [SerializeField, Min(0.5f)] private float boundaryPadding = 1.1f;
    [SerializeField, Min(0.5f)] private float offeringReach = 1.2f;
    [SerializeField, Min(0.5f)] private float requestDisplayDuration = 3.5f;
    [Tooltip("Pause between the player's call and the flock's answer.")]
    [SerializeField, Min(0f)] private float replyDelay = 0.8f;

    private int demandIndex;
    private bool hasRequested;
    private bool spending;
    private float welcomeUntil;
    private bool welcomeStarted;
    private float ejectUntil;
    private float nextPushTime;
    private float nextReplyTime;
    private float requestVisibleUntil;
    private Coroutine replyRoutine;
    private float boundaryRadius;
    private Vector2 lastApproachDirection = Vector2.down;
    private bool guardsActive;

    public int CurrentDemand => flowerDemands == null || flowerDemands.Length == 0
        ? 1 : Mathf.Max(1, flowerDemands[Mathf.Min(demandIndex, flowerDemands.Length - 1)]);
    public int RemainingFlowers
    {
        get
        {
            int count = 0;
            if (flowers != null)
                foreach (var flower in flowers) if (flower != null && !flower.IsCollected) count++;
            return count;
        }
    }
    public int CompletedRequests { get; private set; }
    public int PushCount { get; private set; }
    public bool IsNearby => player != null && DistanceTo(player.transform.position) <= hearingRadius;
    public float BoundaryRadius => boundaryRadius;
    public bool CanGiveFlowers => State == GroupState.Requesting && player != null
        && Vector2.Distance(player.transform.position, transform.position) <= boundaryRadius + offeringReach
        && inventory != null && inventory.Count >= CurrentDemand;
    public SheepAgent RequestSpeaker { get; private set; }
    // The demand has to stay readable for as long as it is outstanding, not just for a few seconds
    // after the last reply: late in the run the player is pushed out and still needs to see "3 flowers".
    // Refusing is included on purpose: that is the moment the demand explains the rejection.
    public bool ShowRequest => (State == GroupState.Requesting || State == GroupState.Ejecting || State == GroupState.Refusing)
        && RequestSpeaker != null && IsNearby;
    public bool CanCollectFlowers => hasRequested && (State == GroupState.Requesting || State == GroupState.Idle);
    public float WelcomeTimeRemaining => State != GroupState.Joined ? 0f
        : welcomeStarted ? Mathf.Max(0f, welcomeUntil - Time.time) : welcomeDuration;
    public event Action PermanentlyRejected;

    protected override void Awake()
    {
        base.Awake();
        float flockRadius = joinRadius;
        foreach (var sheep in members)
            if (sheep != null)
                flockRadius = Mathf.Max(flockRadius, Vector2.Distance(sheep.transform.position, transform.position));
        boundaryRadius = flockRadius + boundaryPadding;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (inventory != null) inventory.Changed += OnInventoryChanged;
    }

    private void OnInventoryChanged(int count)
    {
        if (!spending) CheckSupply();
    }

    public override void HearBleat(Transform caller)
    {
        if (caller == null || player == null || caller != player.transform || DistanceTo(caller.position) > hearingRadius) return;
        RecordHeardBleat();
        if (State == GroupState.Refusing || State == GroupState.Ejecting) return;
        SetPlayerInside(Vector2.Distance(player.transform.position, transform.position) <= joinRadius);
        if (State == GroupState.Joined)
        {
            Reply(false);
            return;
        }

        hasRequested = true;
        State = GroupState.Requesting;
        if (CheckSupply()) return;
        if (!TryDeliverFlowers()) Reply();
    }

    protected override void Update()
    {
        if (player == null || inventory == null) return;
        SetPlayerInside(Vector2.Distance(player.transform.position, transform.position) <= joinRadius);

        if (State == GroupState.Refusing)
        {
            UpdateDeniedBoundary();
            // The available art has only side views; face away without inventing a back sprite.
            if (IsNearby && !guardsActive)
                foreach (var sheep in members)
                    if (sheep != null) sheep.FaceTowards(2f * (Vector2)sheep.transform.position - (Vector2)player.transform.position);
            return;
        }
        if (State == GroupState.Joined)
        {
            if (!welcomeStarted && PlayerInside)
            {
                welcomeStarted = true;
                welcomeUntil = Time.time + welcomeDuration;
            }
            foreach (var sheep in members) if (sheep != null) sheep.FaceTowards(player.transform.position);
            if (welcomeStarted && Time.time >= welcomeUntil) BeginNextDemand();
            return;
        }
        if (State == GroupState.Ejecting)
        {
            UpdateDeniedBoundary();
            if (Time.time >= ejectUntil && Vector2.Distance(player.transform.position, transform.position) >= boundaryRadius)
            {
                State = IsNearby ? GroupState.Requesting : GroupState.Idle;
                CheckSupply();
            }
            return;
        }
        if (!hasRequested)
        {
            if (Vector2.Distance(player.transform.position, transform.position) > boundaryRadius + 0.15f) return;
            hasRequested = true;
            State = GroupState.Requesting;
        }

        State = IsNearby ? GroupState.Requesting : GroupState.Idle;
        if (!CheckSupply() && TryDeliverFlowers()) return;
        UpdateDeniedBoundary();
    }

    private bool TryDeliverFlowers()
    {
        if (!CanGiveFlowers) return false;
        bool paid;
        spending = true;
        try { paid = inventory.TrySpend(CurrentDemand); }
        finally { spending = false; }
        if (!paid) return false;
        AcceptTemporarily();
        return true;
    }

    private void AcceptTemporarily()
    {
        player.ClearGroupExclusion(this);
        guardsActive = false;
        CompletedRequests++;
        State = GroupState.Joined;
        // Give the player the whole welcome period after they actually enter the flock.
        welcomeStarted = false;
        welcomeUntil = 0f;
        requestVisibleUntil = 0f;
        Reply(false, true);
        foreach (var sheep in members)
        {
            if (sheep == null) continue;
            Vector2 fromCenter = sheep.HomePosition - (Vector2)transform.position;
            sheep.MoveTo(sheep.HomePosition + fromCenter.normalized * 0.65f);
        }
    }

    private void BeginNextDemand()
    {
        demandIndex++;
        State = GroupState.Ejecting;
        ejectUntil = Time.time + pushDuration;
        // Announce the new demand right away: the flock pushes the player out and immediately shows
        // what it now wants, instead of leaving the player outside with no request visible.
        Reply(true, true);
        UpdateDeniedBoundary();
    }

    private void PushPlayer()
    {
        if (player == null || Time.time < nextPushTime) return;
        if (Vector2.Distance(player.transform.position, transform.position) > boundaryRadius + 0.15f) return;
        Vector2 direction = player.transform.position - transform.position;
        if (direction.sqrMagnitude < 0.0025f) direction = lastApproachDirection;
        player.ApplyPush(direction, pushSpeed, pushDuration);
        nextPushTime = Time.time + pushCooldown;
        PushCount++;
        if (State != GroupState.Refusing) Reply();
    }

    private void UpdateDeniedBoundary()
    {
        Vector2 center = transform.position;
        Vector2 delta = (Vector2)player.transform.position - center;
        float distance = delta.magnitude;
        if (distance > boundaryRadius + 0.2f) lastApproachDirection = delta.normalized;
        Vector2 outward = distance > 0.05f ? delta / distance : lastApproachDirection;
        player.SetGroupExclusion(this, center, boundaryRadius, lastApproachDirection, pushSpeed);

        if (distance > boundaryRadius + 2f)
        {
            if (guardsActive) foreach (var sheep in members) if (sheep != null) sheep.ReturnHome();
            guardsActive = false;
            return;
        }

        Vector2 side = new Vector2(-outward.y, outward.x);
        Vector2 rear = center + outward * Mathf.Min(Mathf.Max(0f, distance - 0.65f), boundaryRadius - 0.55f);
        Vector2 firstTarget = rear + side * 0.55f;
        Vector2 secondTarget = rear - side * 0.55f;
        SheepAgent first = null;
        SheepAgent second = null;
        float best = float.PositiveInfinity;
        foreach (var sheep in members)
        {
            if (sheep == null) continue;
            float cost = ((Vector2)sheep.transform.position - firstTarget).sqrMagnitude;
            if (cost < best) { best = cost; first = sheep; }
        }
        best = float.PositiveInfinity;
        foreach (var sheep in members)
        {
            if (sheep == null || sheep == first) continue;
            float cost = ((Vector2)sheep.transform.position - secondTarget).sqrMagnitude;
            if (cost < best) { best = cost; second = sheep; }
        }
        foreach (var sheep in members)
        {
            if (sheep == null) continue;
            if (sheep == first) sheep.MoveTo(firstTarget);
            else if (sheep == second) sheep.MoveTo(secondTarget);
            else
            {
                Vector2 home = sheep.HomePosition - center;
                float ahead = Vector2.Dot(home, outward);
                float across = Vector2.Dot(home, side);
                // Leave an outward escape lane instead of closing the flock around the player.
                if (distance < boundaryRadius && ahead > distance && Mathf.Abs(across) < 1f)
                    sheep.MoveTo(sheep.HomePosition + side * (across < 0 ? -1f : 1f));
                else sheep.ReturnHome();
            }
            sheep.FaceTowards(player.transform.position);
        }
        guardsActive = true;
        PushPlayer();
    }

    private void Reply(bool showDemand = true, bool force = false)
    {
        if (!force && Time.time < nextReplyTime) return;
        nextReplyTime = Time.time + 1.5f;
        if (replyRoutine != null) StopCoroutine(replyRoutine);
        replyRoutine = StartCoroutine(ReplyAfterPause(showDemand));
    }

    // The player's call has to land first; the flock answers into the gap, never on the same frame.
    private System.Collections.IEnumerator ReplyAfterPause(bool showDemand)
    {
        if (replyDelay > 0f) yield return new WaitForSeconds(replyDelay);
        SheepAgent nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var sheep in members)
        {
            if (sheep == null || !sheep.isActiveAndEnabled) continue;
            float distance = ((Vector2)sheep.transform.position - (Vector2)player.transform.position).sqrMagnitude;
            if (distance < bestDistance) { nearest = sheep; bestDistance = distance; }
        }
        if (nearest != null)
        {
            nearest.RespondTo(player.transform.position);
            if (showDemand)
            {
                RequestSpeaker = nearest;
                requestVisibleUntil = Time.time + requestDisplayDuration;
            }
        }
        replyRoutine = null;
    }

    private bool CheckSupply()
    {
        if (!hasRequested || State == GroupState.Joined || State == GroupState.Ejecting || State == GroupState.Refusing) return State == GroupState.Refusing;
        if (RemainingFlowers != 0 || inventory == null || inventory.Count >= CurrentDemand) return false;
        State = GroupState.Refusing;
        // Keep the last demand visible while the flock turns the player away: being pushed out makes
        // no sense if the "3 flowers" that caused it vanishes at the same moment.
        if (RequestSpeaker == null) RequestSpeaker = ClosestMember();
        foreach (var sheep in members) if (sheep != null) sheep.ReturnHome();
        PermanentlyRejected?.Invoke();
        return true;
    }

    private SheepAgent ClosestMember()
    {
        SheepAgent closest = null;
        float best = float.PositiveInfinity;
        Vector3 from = player != null ? player.transform.position : transform.position;
        foreach (var sheep in members)
        {
            if (sheep == null || !sheep.isActiveAndEnabled) continue;
            float d = ((Vector2)sheep.transform.position - (Vector2)from).sqrMagnitude;
            if (d < best) { best = d; closest = sheep; }
        }
        return closest;
    }

    protected override void OnDisable()
    {
        if (replyRoutine != null) { StopCoroutine(replyRoutine); replyRoutine = null; }
        if (player != null) player.ClearGroupExclusion(this);
        if (inventory != null) inventory.Changed -= OnInventoryChanged;
        requestVisibleUntil = 0f;
        RequestSpeaker = null;
        base.OnDisable();
    }
}
