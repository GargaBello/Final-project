using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-20)]
public sealed class AcceptingGroup : SheepGroupController
{
    private static readonly float[] ResponseDelays = { 0.75f, 0.55f, 0.36f, 0.21f, 0.09f, 0f };
    private static readonly Vector2[] CloseOffsets =
    {
        new Vector2(-1.2f,-0.9f), new Vector2(-0.4f,-1.1f), new Vector2(0.45f,-1f), new Vector2(1.25f,-0.7f),
        new Vector2(-1.4f,0f), new Vector2(-0.65f,0.15f), new Vector2(0.7f,0f), new Vector2(1.45f,0.25f),
        new Vector2(-1.1f,0.9f), new Vector2(-0.3f,1.05f), new Vector2(0.55f,1.05f), new Vector2(1.3f,1.05f)
    };
    [SerializeField] private PlayerController player;
    [SerializeField, Range(0f, 1f)] private float chorusVolume = 0.4f;
    [Tooltip("Level of the player's own call during a shared call. Kept near a normal call so the flock's answer is a reply, not a whisper.")]
    [SerializeField, Range(0f, 1f)] private float playerCallVolume = 0.55f;
    private readonly List<SheepAgent> responders = new List<SheepAgent>();
    private Vector2[] looseOffsets;
    private Vector2 center;
    private Vector2 centerVelocity;
    private Coroutine greeting;
    private bool greeted;
    private bool hasJoined;
    private bool collisionsIgnored;
    private bool hasShownCallHint;
    private float callHintUntil;
    private GUIStyle callHintStyle;

    public bool IsUnlocked { get; private set; }
    public int SharedCalls { get; private set; }
    public bool FullySynchronized => SharedCalls >= 6;
    public bool CanShareCall => IsUnlocked && player != null && DistanceTo(player.PhysicsPosition) <= hearingRadius;
    public override bool CanAnswer => IsUnlocked;
    public override Vector3 AnswerPosition => ActualCenter();

    protected override void Awake()
    {
        base.Awake();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        center = transform.position;
        looseOffsets = new Vector2[members.Length];
        for (int i = 0; i < members.Length; i++)
            if (members[i] != null) looseOffsets[i] = (Vector2)members[i].transform.position - center;
    }

    public void Unlock()
    {
        IsUnlocked = true;
        State = GroupState.Idle;
    }

    // PlayerBleat reserves one shared audio time before either the player or flock plays.
    public float PreparePlayerCall(double sharedTime)
    {
        RecordHeardBleat();
        SharedCalls = Mathf.Min(6, SharedCalls + 1);
        if (!hasJoined)
        {
            hasJoined = true;
            center = ActualCenter();
        }
        if (greeting != null) { StopCoroutine(greeting); greeting = null; }
        responders.Clear();
        foreach (var sheep in members)
            if (sheep != null && sheep.isActiveAndEnabled) responders.Add(sheep);
        Vector2 position = player.PhysicsPosition;
        responders.Sort((a, b) => ((Vector2)a.transform.position - position).sqrMagnitude.CompareTo(
            ((Vector2)b.transform.position - position).sqrMagnitude));
        int count = Mathf.Min(responders.Count, Mathf.RoundToInt(Mathf.Lerp(3, responders.Count, (SharedCalls - 1) / 5f)));
        // Per-voice level has to stay audible: the old chorusVolume / (count + 1) made the first
        // answer 0.10 and the last one 0.03, i.e. the flock whispered while the player shouted.
        float perVoice = Mathf.Clamp(chorusVolume / Mathf.Sqrt(count + 1f), 0.10f, 0.30f);
        double delay = ResponseDelays[SharedCalls - 1];
        bool left = player.GetComponent<SpriteRenderer>().flipX;
        for (int i = 0; i < count; i++)
        {
            double stagger = FullySynchronized ? 0 : i * 0.035 * (1f - SharedCalls / 6f);
            responders[i].AcceptingCall(sharedTime + delay + stagger, perVoice, position, left, FullySynchronized);
        }
        // The player keeps their normal call level; only the flock needs the crowd maths.
        return playerCallVolume;
    }

    public override void HearBleat(Transform caller)
    {
        if (!IsUnlocked || caller == null || DistanceTo(caller.position) > hearingRadius) return;
        // Fallback for callers other than PlayerBleat. Normal local calls use PreparePlayerCall once.
        PreparePlayerCall(AudioSettings.dspTime + 0.06);
    }

    public override void HearDistantBleat(Transform caller)
    {
        if (!IsUnlocked || caller == null || DistanceTo(caller.position) > 100f || greeting != null) return;
        RecordHeardBleat();
        greeting = StartCoroutine(Greet(caller, true));
    }

    protected override void Update()
    {
        if (!IsUnlocked || player == null) return;
        Vector2 position = player.PhysicsPosition;
        bool nearby = DistanceTo(position) <= hearingRadius;
        bool inside = Vector2.Distance(position, ActualCenter()) <= joinRadius;
        SetPlayerInside(inside);
        if (inside && !hasShownCallHint)
        {
            hasShownCallHint = true;
            callHintUntil = Time.time + 6f;
        }
        if (inside && !hasJoined)
        {
            hasJoined = true;
            center = ActualCenter();
        }
        State = hasJoined && nearby ? GroupState.Together : GroupState.Responding;
        SetPlayerCollisionIgnored(hasJoined && nearby);
        if (nearby && !greeted)
        {
            greeted = true;
            if (greeting == null && SharedCalls == 0) greeting = StartCoroutine(Greet(player.transform, false));
        }
        else if (!nearby) greeted = false;
    }

    private void OnGUI()
    {
        if (!IsUnlocked || !PlayerInside || FullySynchronized || Time.time >= callHintUntil) return;
        if (callHintStyle == null)
            callHintStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        float scale = Mathf.Max(0.5f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((callHintUntil - Time.time) / 0.8f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        GUI.Label(new Rect(width * 0.5f - 180f, height - 50f, 360f, 34f), "SPACE: Bleat", callHintStyle);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private IEnumerator Greet(Transform caller, bool distant)
    {
        yield return new WaitForSeconds(0.8f);
        int voices = 0;
        foreach (var sheep in members)
        {
            if (caller == null || !IsUnlocked) break;
            if (sheep == null || !sheep.isActiveAndEnabled) continue;
            if (Vector2.Distance(sheep.transform.position, caller.position) > (distant ? 100f : hearingRadius)) continue;
            if (distant) sheep.RespondDistant(caller.position, 0.45f);
            else
            {
                sheep.RespondTo(caller.position);
                if (!hasJoined)
                {
                    Vector2 direction = ((Vector2)caller.position - sheep.HomePosition).normalized;
                    sheep.MoveTo(sheep.HomePosition + direction * 0.6f);
                }
            }
            if (++voices >= (distant ? 2 : 3)) break;
            yield return new WaitForSeconds(0.18f);
        }
        greeting = null;
    }

    private void FixedUpdate()
    {
        if (!IsUnlocked || player == null || !hasJoined) return;
        if (DistanceTo(player.PhysicsPosition) > hearingRadius + 1f)
        {
            foreach (var sheep in members) if (sheep != null) sheep.LeaveFlockMotion(this);
            return;
        }
        float amount = Mathf.SmoothStep(0f, 1f, SharedCalls / 6f);
        if (FullySynchronized) center = player.PhysicsPosition;
        else center = Vector2.SmoothDamp(center, player.PhysicsPosition, ref centerVelocity,
            Mathf.Lerp(0.95f, 0.08f, amount), Mathf.Lerp(1f, 3.2f, amount), Time.fixedDeltaTime);
        bool left = player.GetComponent<SpriteRenderer>().flipX;
        for (int i = 0; i < members.Length; i++)
        {
            var sheep = members[i];
            if (sheep == null) continue;
            Vector2 close = CloseOffsets[i % CloseOffsets.Length];
            Vector2 target = center + Vector2.Lerp(looseOffsets[i], close, amount);
            target.x = Mathf.Clamp(target.x, -38.5f, 38.5f);
            target.y = Mathf.Clamp(target.y, -28.5f, 28.5f);
            sheep.FollowFlock(this, target, player.RequestedVelocity, amount, left);
        }
    }

    private Vector2 ActualCenter()
    {
        Vector2 total = Vector2.zero;
        int count = 0;
        if (members != null)
            foreach (var sheep in members)
                if (sheep != null && sheep.isActiveAndEnabled) { total += (Vector2)sheep.transform.position; count++; }
        return count > 0 ? total / count : (Vector2)transform.position;
    }

    private void SetPlayerCollisionIgnored(bool ignore)
    {
        if (collisionsIgnored == ignore || player == null) return;
        foreach (var playerCollider in player.GetComponents<Collider2D>())
            foreach (var sheep in members)
                if (sheep != null)
                    foreach (var collider in sheep.GetComponents<Collider2D>())
                        Physics2D.IgnoreCollision(playerCollider, collider, ignore);
        collisionsIgnored = ignore;
    }

    protected override void OnDisable()
    {
        SetPlayerCollisionIgnored(false);
        if (greeting != null) StopCoroutine(greeting);
        greeting = null;
        if (members != null)
            foreach (var sheep in members) if (sheep != null) sheep.LeaveFlockMotion(this);
        base.OnDisable();
    }
}
