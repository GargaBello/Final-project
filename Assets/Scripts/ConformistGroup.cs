using UnityEngine;

public sealed class ConformistGroup : SheepGroupController
{
    // Each pattern is one 4/4 bar, in quarter-note units; halves are eighth-note offbeats.
    private static readonly float[][] FormalPatterns =
    {
        new[] { 0f, 1f, 2f, 3f },
        new[] { 0f, 1f, 2.5f, 3f },
        new[] { 0f, 1.5f, 2f, 3f },
        new[] { 0f, 1f, 2f, 3.5f },
        // Stage two: offbeat ending, sustained offbeats, upbeat entry, then a long final rest.
        new[] { 0f, 1f, 2.5f, 3.5f },
        new[] { 0f, 1.5f, 2.5f, 3.5f },
        new[] { 0.5f, 1f, 2.5f, 3f },
        new[] { 0f, 0.5f, 1.5f, 3.5f }
    };
    public enum Phase { Waiting, Preparing, Demonstrating, Performing, BetweenRounds, Ejecting, Locked, Accepted, Ready, CountingIn, RandomPerforming }

    [SerializeField] private PlayerController player;
    [SerializeField] private Vector2 vacantSlot = new Vector2(2.1f, -1.7f);
    [SerializeField, Min(0.1f)] private float slotRadius = 0.85f;
    [SerializeField, Min(0.1f)] private float beatDuration = 0.6f;
    [SerializeField, Min(0.01f)] private float timingTolerance = 0.2f;
    [SerializeField, Min(0f)] private float preparationDuration = 1f;
    [SerializeField, Min(0f)] private float exclusionRadius = 4.3f;

    private PlayerBleat playerBleat;
    private Vector2[] formation;
    private double nextStageAt;
    private double demonstrationStart;
    private double formalMotionStart;
    private double playerRowStart;
    private double expectedCall;
    private double nextGreeting;
    private double greetingAt = double.PositiveInfinity;
    private int demonstrationBeat;
    private int playerRowBeat;
    private int round;
    private bool matched;
    private bool wrongCall;
    private double lastUpdateTime;
    private readonly int[] randomPatternOrder = new int[8];
    private int randomBar;
    private int lastRandomPattern = -1;

    public Phase CurrentPhase { get; private set; }
    public int FailedAttempts { get; private set; }
    public const int HeartCount = 3;
    public int RemainingHearts => Mathf.Max(0, HeartCount - FailedAttempts);
    public float LastHeartLostAt { get; private set; } = float.NegativeInfinity;
    public int FormalStage => IsTutorial ? 0 : IsThirdStage ? 3 : (round - 2) / 4 + 1;
    public bool ShowHearts => !IsTutorial && CurrentPhase != Phase.Waiting && CurrentPhase != Phase.Accepted
        && (CurrentPhase != Phase.Locked || Time.unscaledTime - LastHeartLostAt < 1.2f);
    public Vector2 VacantPosition => transform.TransformPoint(vacantSlot);
    public override bool CanAnswer => CurrentPhase != Phase.Locked;
    private bool IsTutorial => round < 2;
    private bool IsThirdStage => round >= 2 + FormalPatterns.Length;
    private float CurrentBeatDuration => beatDuration;
    private float CurrentTolerance => timingTolerance;

    protected override void Awake()
    {
        base.Awake();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (player != null) playerBleat = player.GetComponent<PlayerBleat>();
        if (player != null)
        {
            var hearts = player.GetComponent<RhythmHearts>();
            if (hearts == null) hearts = player.gameObject.AddComponent<RhythmHearts>();
            hearts.Bind(this);
        }
        formation = new Vector2[members.Length];
        for (int i = 0; i < members.Length; i++)
            if (members[i] != null) formation[i] = members[i].transform.position;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (playerBleat != null) playerBleat.Called += OnPlayerCall;
    }

    protected override void OnDisable()
    {
        if (playerBleat != null) playerBleat.Called -= OnPlayerCall;
        if (player != null)
        {
            player.ClearGroupExclusion(this);
            player.ReleasePosition(this);
        }
        if (playerBleat != null) playerBleat.SetRhythmVoice(this, false);
        CancelRhythmAudio();
        StopBouncing();
        greetingAt = double.PositiveInfinity;
        if (CurrentPhase != Phase.Locked && CurrentPhase != Phase.Accepted) CurrentPhase = Phase.Waiting;
        base.OnDisable();
    }

    public override void HearBleat(Transform caller)
    {
        if (!CanAnswer || caller == null || DistanceTo(caller.position) > hearingRadius) return;
        RecordHeardBleat();
        // Calls during the exercise are handled by the single timestamped input event.
        if (CurrentPhase == Phase.Waiting) QueueGreeting();
    }

    public override void HearDistantBleat(Transform caller)
    {
        if (CurrentPhase == Phase.Waiting) base.HearDistantBleat(caller);
    }

    private void QueueGreeting()
    {
        double now = AudioSettings.dspTime;
        if (now < nextGreeting || !double.IsPositiveInfinity(greetingAt)) return;
        greetingAt = now + 0.8;
        nextGreeting = now + 4;
    }

    protected override void Update()
    {
        if (player == null) return;
        double now = AudioSettings.dspTime;
        bool stalled = lastUpdateTime > 0 && now - lastUpdateTime > 0.25;
        lastUpdateTime = now;
        if (stalled && (CurrentPhase == Phase.Demonstrating || CurrentPhase == Phase.Performing || CurrentPhase == Phase.RandomPerforming))
        {
            // Scheduled audio may continue through an editor stall; do not judge the skipped window.
            CancelRhythmAudio();
            CurrentPhase = Phase.BetweenRounds;
            nextStageAt = now + 0.6;
            return;
        }
        bool inSlot = Vector2.Distance(player.transform.position, VacantPosition) <= slotRadius;
        SetPlayerInside(inSlot && CurrentPhase != Phase.Locked && CurrentPhase != Phase.Ejecting);
        HoldFormation();
        if (CurrentPhase == Phase.Accepted) return;

        if (CurrentPhase == Phase.Locked || CurrentPhase == Phase.Ejecting)
        {
            float distance = Vector2.Distance(player.transform.position, transform.position);
            if (distance < exclusionRadius + 0.4f)
                player.SetGroupExclusion(this, transform.position, exclusionRadius, Vector2.down, 4f);
            else
            {
                player.ClearGroupExclusion(this);
                if (CurrentPhase == Phase.Ejecting && now >= nextStageAt)
                {
                    CurrentPhase = Phase.Waiting;
                    State = GroupState.Idle;
                }
            }
            return;
        }

        if (CurrentPhase != Phase.Waiting && CurrentPhase != Phase.Accepted)
            player.HoldPosition(this, VacantPosition);

        if (CurrentPhase == Phase.Waiting)
        {
            if (DistanceTo(player.transform.position) < 4f) QueueGreeting();
            if (now >= greetingAt)
            {
                Greet();
                greetingAt = double.PositiveInfinity;
            }
            if (inSlot && CurrentPhase == Phase.Waiting)
            {
                player.ClearGroupExclusion(this);
                player.HoldPosition(this, VacantPosition);
                if (playerBleat != null) playerBleat.SetRhythmVoice(this, true);
                foreach (var sheep in members) if (sheep != null) sheep.PrepareRhythmVoice();
                CurrentPhase = Phase.Preparing;
                State = GroupState.Responding;
                nextStageAt = now + preparationDuration;
                greetingAt = double.PositiveInfinity;
            }
            return;
        }

        if (CurrentPhase == Phase.Preparing || CurrentPhase == Phase.BetweenRounds)
        {
            if (now >= nextStageAt && Vector2.Distance(player.transform.position, VacantPosition) <= 0.05f)
            {
                if (IsThirdStage) BeginRandomBar(now);
                else BeginDemonstration(now);
            }
            return;
        }
        if (CurrentPhase == Phase.RandomPerforming)
        {
            UpdateRandomBar(now);
            return;
        }
        if (CurrentPhase == Phase.Demonstrating)
        {
            while (demonstrationBeat < DemonstrationVoices && now >= DemoTime(demonstrationBeat))
            {
                PlaySheep(demonstrationBeat);
                demonstrationBeat++;
            }
            if (now < playerRowStart) return;
            CurrentPhase = Phase.Performing;
        }
        if (CurrentPhase != Phase.Performing) return;
        int performanceVoices = IsTutorial ? 3 : 4;
        while (playerRowBeat < performanceVoices && now >= playerRowStart + VoiceBeat(playerRowBeat) * CurrentBeatDuration)
        {
            if (playerRowBeat < 3) PlaySheep(8 + playerRowBeat);
            if (!IsTutorial) PlaySheep(4 + playerRowBeat);
            playerRowBeat++;
        }
        // Wait through the complete window so a second call is still an error.
        if (playerRowBeat >= 3 && now > expectedCall + CurrentTolerance)
        {
            if (!matched || wrongCall) Fail(now);
            else CompleteRound(now);
        }
    }

    private void BeginDemonstration(double now, double? scheduledStart = null)
    {
        demonstrationStart = scheduledStart ?? now + 0.5;
        if (!IsTutorial && !scheduledStart.HasValue) formalMotionStart = demonstrationStart;
        playerRowStart = demonstrationStart + RowBeats * CurrentBeatDuration * (IsTutorial ? 2 : 1);
        expectedCall = playerRowStart + VoiceBeat(3) * CurrentBeatDuration;
        demonstrationBeat = 0;
        playerRowBeat = 0;
        matched = false;
        wrongCall = false;
        if (!scheduledStart.HasValue) CancelRhythmAudio();
        for (int i = 0; i < DemonstrationVoices; i++) ScheduleSheep(i, DemoTime(i));
        for (int beat = 0; beat < 3; beat++)
        {
            double time = playerRowStart + VoiceBeat(beat) * CurrentBeatDuration;
            ScheduleSheep(8 + beat, time);
            if (!IsTutorial) ScheduleSheep(4 + beat, time);
        }
        if (!IsTutorial) ScheduleSheep(7, expectedCall);
        CurrentPhase = Phase.Demonstrating;
    }

    // Teaching uses both front rows; formal rounds give one first-row preview before the player's row.
    private int DemonstrationVoices => IsTutorial ? 8 : 4;
    private int RowBeats => IsTutorial ? (round == 0 ? 5 : 6) : 4;
    private float VoiceBeat(int voice)
    {
        if (IsTutorial) return round == 1 && voice == 3 ? 4 : voice;
        if (IsThirdStage) return FormalPatterns[randomPatternOrder[randomBar]][voice];
        return FormalPatterns[Mathf.Clamp(round - 2, 0, FormalPatterns.Length - 1)][voice];
    }

    private double DemoTime(int index)
    {
        int row = index / 4;
        int voice = index % 4;
        return demonstrationStart + (row * RowBeats + VoiceBeat(voice)) * CurrentBeatDuration;
    }

    private void OnPlayerCall(double time)
    {
        // The player's turn only opens after all three actual lead-in calls have sounded.
        if ((CurrentPhase != Phase.Performing && CurrentPhase != Phase.RandomPerforming)
            || player == null || time < playerRowStart + VoiceBeat(2) * CurrentBeatDuration) return;
        if (matched && time > expectedCall + CurrentTolerance) return;
        if (lastUpdateTime > 0 && time - lastUpdateTime > 0.25) return;
        if (!matched && System.Math.Abs(time - expectedCall) <= CurrentTolerance
            && Vector2.Distance(player.transform.position, VacantPosition) <= slotRadius + 0.65f)
            matched = true;
        else wrongCall = true;
    }

    private void CompleteRound(double now)
    {
        if (!IsTutorial)
        {
            if (round - 2 < FormalPatterns.Length - 1)
            {
                // Queue the next phrase before its downbeat; the eight-beat motion keeps running.
                double nextPhrase = demonstrationStart + 8 * CurrentBeatDuration;
                round++;
                BeginDemonstration(now, nextPhrase);
                return;
            }
            // Stage three starts on the next phrase boundary, with no first-row demonstration.
            double thirdStageStart = demonstrationStart + 8 * CurrentBeatDuration;
            round = 2 + FormalPatterns.Length;
            randomBar = 0;
            ShuffleRandomCycle();
            BeginRandomBar(now, thirdStageStart);
            return;
        }
        round++;
        if (round == 2)
        {
            CurrentPhase = Phase.BetweenRounds;
            State = GroupState.Together;
            nextStageAt = now + 0.8;
            return;
        }
        CurrentPhase = Phase.BetweenRounds;
        nextStageAt = now + (IsTutorial ? 1.2 : CurrentBeatDuration);
    }

    private void ShuffleRandomCycle()
    {
        for (int i = 0; i < randomPatternOrder.Length; i++) randomPatternOrder[i] = i;
        for (int i = randomPatternOrder.Length - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            int swap = randomPatternOrder[i];
            randomPatternOrder[i] = randomPatternOrder[other];
            randomPatternOrder[other] = swap;
        }
        if (randomPatternOrder[0] == lastRandomPattern)
        {
            int swap = randomPatternOrder[0];
            randomPatternOrder[0] = randomPatternOrder[1];
            randomPatternOrder[1] = swap;
        }
    }

    private void BeginRandomBar(double now, double? scheduledStart = null)
    {
        playerRowStart = scheduledStart ?? now + 0.5;
        if (!scheduledStart.HasValue)
        {
            CancelRhythmAudio();
            formalMotionStart = playerRowStart;
        }
        lastRandomPattern = randomPatternOrder[randomBar];
        expectedCall = playerRowStart + VoiceBeat(3) * CurrentBeatDuration;
        playerRowBeat = 0;
        matched = false;
        wrongCall = false;
        for (int beat = 0; beat < 4; beat++)
        {
            double time = playerRowStart + VoiceBeat(beat) * CurrentBeatDuration;
            ScheduleSheep(beat, time);
            ScheduleSheep(4 + beat, time);
            if (beat < 3) ScheduleSheep(8 + beat, time);
        }
        CurrentPhase = Phase.RandomPerforming;
        State = GroupState.Together;
    }

    private void UpdateRandomBar(double now)
    {
        while (playerRowBeat < 4 && now >= playerRowStart + VoiceBeat(playerRowBeat) * CurrentBeatDuration)
        {
            PlaySheep(playerRowBeat);
            PlaySheep(4 + playerRowBeat);
            if (playerRowBeat < 3) PlaySheep(8 + playerRowBeat);
            playerRowBeat++;
        }
        if (now <= expectedCall + CurrentTolerance) return;
        if (!matched || wrongCall) { Fail(now); return; }

        double nextBar = playerRowStart + 4 * CurrentBeatDuration;
        randomBar++;
        if (randomBar == randomPatternOrder.Length)
        {
            randomBar = 0;
            ShuffleRandomCycle();
        }
        BeginRandomBar(now, nextBar);
    }

    private void Fail(double now)
    {
        if (CurrentPhase == Phase.Ejecting || CurrentPhase == Phase.Locked) return;
        CancelRhythmAudio();
        if (IsTutorial)
        {
            matched = false;
            greetingAt = double.PositiveInfinity;
            CurrentPhase = Phase.BetweenRounds;
            nextStageAt = now + 1.2;
            State = GroupState.Responding;
            SetPlayerInside(true);
            foreach (var sheep in members) if (sheep != null) sheep.StopMoving();
            return;
        }
        FailedAttempts++;
        LastHeartLostAt = Time.unscaledTime;
        if (RemainingHearts > 0)
        {
            // A lost heart restarts this pattern, preserving both stage progress and the held slot.
            matched = false;
            wrongCall = false;
            greetingAt = double.PositiveInfinity;
            CurrentPhase = Phase.BetweenRounds;
            State = GroupState.Together;
            nextStageAt = now + 2 * beatDuration;
            StopBouncing();
            foreach (var sheep in members) if (sheep != null) sheep.StopMoving();
            return;
        }
        RejectPlayer(now);
    }

    private void RejectPlayer(double now)
    {
        CancelRhythmAudio();
        StopBouncing();
        player.ReleasePosition(this);
        if (playerBleat != null) playerBleat.SetRhythmVoice(this, false);
        greetingAt = double.PositiveInfinity;
        CurrentPhase = Phase.Locked;
        ClosePermanently(); // permanent silence + dimmed look
        SetPlayerInside(false);
        foreach (var sheep in members) if (sheep != null) sheep.StopMoving();
        player.SetGroupExclusion(this, transform.position, exclusionRadius, Vector2.down, 4f);
        nextStageAt = now + 1.5;
    }

    private void PlaySheep(int index)
    {
        if (index < members.Length && members[index] != null)
            members[index].ShowRhythmResponse(player.transform.position);
    }

    private void ScheduleSheep(int index, double time)
    {
        if (index < members.Length && members[index] != null) members[index].ScheduleRhythm(time);
    }

    private void CancelRhythmAudio()
    {
        if (members == null) return;
        foreach (var sheep in members) if (sheep != null) sheep.CancelRhythm();
    }

    private void HoldFormation()
    {
        for (int i = 0; i < members.Length; i++)
            if (members[i] != null) members[i].MoveTo(formation[i]);
    }

    private void Greet()
    {
        // The two sheep beside the vacancy make the missing final position readable.
        if (members.Length > 7 && members[7] != null) members[7].RespondTo(player.transform.position);
        if (members.Length > 10 && members[10] != null) members[10].RespondTo(player.transform.position);
    }

    private void LateUpdate()
    {
        bool active = CurrentPhase == Phase.Demonstrating || CurrentPhase == Phase.Performing
            || CurrentPhase == Phase.RandomPerforming;
        if (!active) { StopBouncing(); return; }
        double origin = IsTutorial ? demonstrationStart : formalMotionStart;
        float duration = CurrentBeatDuration;
        foreach (var sheep in members)
            if (sheep != null) BeatBounce.For(sheep.gameObject).SetRhythm(this, origin, duration);
        if (player != null) BeatBounce.For(player.gameObject).SetRhythm(this, origin, duration);
    }

    private void StopBouncing()
    {
        if (members != null)
            foreach (var sheep in members)
                if (sheep != null)
                {
                    var bounce = sheep.GetComponent<BeatBounce>();
                    if (bounce != null) bounce.StopRhythm(this);
                }
        if (player != null)
        {
            var bounce = player.GetComponent<BeatBounce>();
            if (bounce != null) bounce.StopRhythm(this);
        }
    }

}
