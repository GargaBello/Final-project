using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Closing cinematic. Triggered by the accepting flock once it is fully synchronised (six shared
/// calls). The flock stays exactly where it stands, sings a drum-grid chorus built from its own
/// bleats, the camera pulls back with the player at the centre, a line fades in, then it fades out.
/// </summary>
[DefaultExecutionOrder(500)]
public sealed class EndingSequence : MonoBehaviour
{
    [Serializable]
    public struct ChorusBar
    {
        [Tooltip("Drum-style step pattern for one 4/4 bar: position+letter, comma separated, in quarter notes. " +
                 "0K = kick on beat 1, 1S = snare on beat 2, 1.5h = hi-hat on the 'and' of 2. K = kick (heavy), S = snare (backbeat), h = hi-hat (light).")]
        public string groove;
        [Tooltip("Level multiplier for this bar.")]
        [Range(0f, 1.5f)] public float volume;
    }

    [Header("Wiring")]
    [SerializeField] private PlayerBleat bleat;
    [SerializeField] private PlayerController player;
    [SerializeField] private AcceptingGroup accepting;
    [SerializeField] private Camera view;

    [Header("Trigger")]
    [Tooltip("Only start once the flock is fully synchronised (six shared calls). Without this any single bleat fires the whole cinematic.")]
    [SerializeField] private bool requireFullySynchronized = true;

    [Header("Gathering (off by default: the flock freezes where it stands)")]
    [SerializeField] private bool gatherFlock = false;
    [SerializeField, Min(0f)] private float gatherDuration = 2.2f;
    [SerializeField, Min(0.5f)] private float gatherRadius = 2.4f;
    [SerializeField, Min(0.3f)] private float sheepWalkSpeed = 1.4f;
    [SerializeField, Min(0.5f)] private float gatherMaxDuration = 6f;
    [SerializeField, Min(1f)] private float gatherDetectRadius = 18f;

    [Header("Chorus")]
    [Tooltip("One beat in seconds. 0.68 = 88 BPM, the tempo both source patterns are written at.")]
    [SerializeField, Min(0.15f)] private float beatDuration = 0.68f;
    [Tooltip("Lead-in before the first note so the first hit is queued slightly ahead of the audio callback.")]
    [SerializeField, Min(0.02f)] private float chorusLead = 0.15f;

    [Header("Drum grid (bleats stand in for the kit)")]
    [Tooltip("Level of the downbeat (position 0).")]
    [SerializeField, Range(0f, 1f)] private float accentDownbeat = 0.34f;
    [Tooltip("Level of the other on-beat notes.")]
    [SerializeField, Range(0f, 1f)] private float accentOnBeat = 0.28f;
    [Tooltip("Level of the off-beat notes (x.5 positions).")]
    [SerializeField, Range(0f, 1f)] private float accentOffBeat = 0.22f;

    // Transcribed from two published patterns, both slow:
    //   half-time groove  - snare only on beat 3, kick on 1 and the "&" of 3   (drumpracticepro, half-time)
    //   boom bap groove   - kick on 1, the "a" of 1, 3 and the "&" of 3        (noteatlas, 88 BPM)
    // Hats sit on the beats rather than every eighth, so the bar breathes.
    // Positions taken straight from ConformistGroup.FormalPatterns: four notes per bar with a displaced
    // note (2.5 / 1.5 / 3.5) so the bar leans instead of marching. Last note of every bar is the player.
    // The four most lopsided bars from ConformistGroup.FormalPatterns, chosen by uneven note spacing:
    // 1.0/1.5/1.0, then 0.5/1.5/0.5, then 0.5/1.0/2.0, then a late entry at 1.5.
    [SerializeField] private ChorusBar[] bars =
    {
        new ChorusBar { groove = "0,1,2.5,3.5", volume = 1f },
        new ChorusBar { groove = "0.5,1,2.5,3", volume = 1.05f },
        new ChorusBar { groove = "0,0.5,1.5,3.5", volume = 1.1f },
        new ChorusBar { groove = "0,1.5,2.5,3.5", volume = 1.1f }
    };

    [Header("Camera")]
    [SerializeField, Min(0.1f)] private float zoomDuration = 3.5f;
    [SerializeField, Min(0.5f)] private float zoomPadding = 1.35f;
    [SerializeField, Min(1f)] private float zoomMaxSize = 16f;

    [Header("Text and fade")]
    [SerializeField, Min(0f)] private float textDelayAfterChorus = 1.6f;
    [SerializeField, Min(0.1f)] private float textFadeDuration = 1.2f;
    [SerializeField, Min(0f)] private float holdAfterLine = 4f;
    [SerializeField, Min(0.1f)] private float fadeOutDuration = 1.8f;

    private bool running;
    private bool finished;
    private float startedAt;
    private float restartAlpha;
    private float fadeAlpha;
    private GUIStyle buttonStyle;
    private readonly List<SheepAgent> cast = new List<SheepAgent>();
    private Vector2 frameCenter;
    private float frameSize;
    private float gatherTravelTime;

    /// <summary>Diagnostic: notes queued in the last chorus.</summary>
    public static int LastChorusFired;

    public bool IsRunning => running;

    private void Awake()
    {
        if (bleat == null) bleat = UnityEngine.Object.FindFirstObjectByType<PlayerBleat>();
        if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (accepting == null) accepting = UnityEngine.Object.FindFirstObjectByType<AcceptingGroup>(FindObjectsInactive.Include);
        if (view == null) view = Camera.main;
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
        if (running || finished) return;
        if (responder == null || responder != accepting) return;
        if (requireFullySynchronized && accepting != null && !accepting.FullySynchronized) return;
        Begin();
    }

    public void Begin()
    {
        if (running || finished) return;
        running = true;
        startedAt = Time.time;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        if (player != null) player.enabled = false;
        var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
        if (pause != null) pause.enabled = false;
        var follow = view != null ? view.GetComponent<PastureCameraFollow>() : null;
        if (follow != null) follow.enabled = false;

        CollectCast();
        FreezeFlock();
        if (gatherFlock) GatherAroundPlayer();

        float beat = Mathf.Max(0.15f, beatDuration);
        float chorusStart = gatherFlock ? Mathf.Clamp(gatherTravelTime, gatherDuration, gatherMaxDuration) : 0f;
        float chorusLength = BarsLength() * beat;
        float chorusEnd = chorusStart + chorusLength;
        float zoomStart = chorusStart;
        float zoomEnd = zoomStart + zoomDuration;
        float fadeStart = chorusEnd + Mathf.Max(0f, textDelayAfterChorus) + holdAfterLine;

        float startSize = view != null ? view.orthographicSize : 8f;
        Vector3 startPos = view != null ? view.transform.position : Vector3.zero;
        bool frameReady = false;
        bool chorusQueued = false;

        while (true)
        {
            float t = Time.time - startedAt;

            if (!chorusQueued && t >= chorusStart)
            {
                chorusQueued = true;
                QueueChorus(beat, chorusStart - t);
            }

            if (view != null)
            {
                if (t < zoomStart)
                {
                    view.orthographicSize = startSize;
                    view.transform.position = startPos;
                }
                else
                {
                    if (!frameReady) { ComputeFrame(zoomPadding, zoomMaxSize); frameReady = true; }
                    if (t < zoomEnd)
                    {
                        float k = Mathf.Clamp01((t - zoomStart) / zoomDuration);
                        float eased = 1f - Mathf.Pow(1f - k, 3f);
                        view.orthographicSize = Mathf.Lerp(startSize, frameSize, eased);
                        view.transform.position = Vector3.Lerp(startPos,
                            new Vector3(frameCenter.x, frameCenter.y, startPos.z), eased);
                    }
                    else
                    {
                        view.orthographicSize = frameSize;
                        view.transform.position = new Vector3(frameCenter.x, frameCenter.y, startPos.z);
                    }
                }
            }

            if (t >= fadeStart)
            {
                fadeAlpha = Mathf.Clamp01((t - fadeStart) / fadeOutDuration);
                if (fadeAlpha >= 1f) break;
            }

            yield return null;
        }

        StopChorusBounce();
        restartAlpha = 1f;
        running = false;
        finished = true;
    }

    private int BarsLength() => bars != null && bars.Length > 0 ? bars.Length * 4 : 0;

    private void CollectCast()
    {
        cast.Clear();
        if (accepting != null)
        {
            var members = accepting.Members;
            if (members != null)
                foreach (var m in members)
                    if (m != null && m.isActiveAndEnabled) cast.Add(m);
        }
        if (cast.Count > 0) return;

        Vector2 from = player != null ? player.transform.position : transform.position;
        var all = UnityEngine.Object.FindObjectsByType<SheepAgent>(FindObjectsSortMode.None);
        var nearby = new List<SheepAgent>();
        foreach (var sheep in all)
        {
            if (sheep == null || !sheep.isActiveAndEnabled) continue;
            if (Vector2.Distance(sheep.transform.position, from) <= gatherDetectRadius) nearby.Add(sheep);
        }
        if (nearby.Count == 0) nearby.AddRange(all);
        nearby.Sort((a, b) => Vector2.Distance(a.transform.position, from)
            .CompareTo(Vector2.Distance(b.transform.position, from)));
        cast.AddRange(nearby);
    }

    private void FreezeFlock()
    {
        for (int i = 0; i < cast.Count; i++)
        {
            if (cast[i] == null) continue;
            cast[i].StopMoving();
            var body = cast[i].GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }
    }

    private void GatherAroundPlayer()
    {
        gatherTravelTime = 0f;
        if (player == null || cast.Count == 0) return;
        Vector2 center = player.transform.position;
        for (int i = 0; i < cast.Count; i++)
        {
            float angle = (i / (float)cast.Count) * Mathf.PI * 2f + 0.35f;
            float radius = gatherRadius * (0.85f + 0.3f * ((i * 37) % 10) / 10f);
            var spot = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            gatherTravelTime = Mathf.Max(gatherTravelTime,
                Vector2.Distance(cast[i].transform.position, spot) / Mathf.Max(0.5f, sheepWalkSpeed));
            cast[i].MoveTo(spot);
        }
    }

    /// <summary>
    /// Same mechanism as ConformistGroup: each sheep owns two rhythm voices and gets the trimmed clip
    /// once, then every note is queued on its own source with PlayScheduled. One sheep per note (two
    /// for the kick) so nobody is booked twice while a bleat is still ringing.
    /// </summary>
    private void QueueChorus(float beat, float leadOffset)
    {
        LastChorusFired = 0;
        if (bars == null || cast.Count == 0) return;

        for (int i = 0; i < cast.Count; i++)
        {
            if (cast[i] == null) continue;
            cast[i].PrepareRhythmVoice();
            cast[i].CancelRhythm();
        }

        double startAt = AudioSettings.dspTime + chorusLead + Mathf.Max(0f, leadOffset);
        for (int i = 0; i < cast.Count; i++)
        {
            if (cast[i] == null) continue;
            BeatBounce.For(cast[i].gameObject).SetRhythm(this, startAt, beat);
        }
        if (player != null) BeatBounce.For(player.gameObject).SetRhythm(this, startAt, beat);

        // Four notes per bar: three sheep plus the player on the last note, so one phrase covers
        // every sheep exactly once and the player sings in every bar without pressing anything.
        // Picking them in formation order made the chorus sweep left to right like a wave, so the
        // order below always jumps to the sheep farthest from the previous note instead.
        var singOrder = BuildAlternatingOrder();
        int cursor = 0;
        int steps = 0;
        int playerNotes = 0;
        for (int barIndex = 0; barIndex < bars.Length; barIndex++)
        {
            var bar = bars[barIndex];
            var groove = ParseGroove(bar.groove);
            for (int n = 0; n < groove.Count; n++)
            {
                var step = groove[n];
                float whenF = (barIndex * 4 + step.position) * beat;
                double when = startAt + whenF;
                float level = Mathf.Clamp01(Accent(step.position) * Mathf.Max(0.2f, bar.volume));
                bool isPlayerNote = n == groove.Count - 1;

                if (isPlayerNote)
                {
                    if (bleat != null)
                    {
                        bleat.ScheduleSungCall(when, Mathf.Clamp01(level * 1.15f));
                        playerNotes++;
                    }
                }
                else
                {
                    int slot = cursor < singOrder.Count ? singOrder[cursor] : cursor % cast.Count;
                    cursor++;
                    var sheep = cast[slot];
                    if (sheep == null) continue;
                    sheep.ScheduleChorus(when, 1f, level,
                        player != null ? (Vector2)player.transform.position : (Vector2)sheep.transform.position);
                    LastChorusFired++;
                }
                steps++;
            }
        }
        Debug.Log("[EndingChorus] bars=" + bars.Length + " cast=" + cast.Count
            + " steps=" + steps + " sheepNotes=" + LastChorusFired + " playerNotes=" + playerNotes
            + " startIn=" + (startAt - AudioSettings.dspTime).ToString("F2") + "s");
    }

    private struct GrooveStep { public float position; public char voice; }

    private readonly List<GrooveStep> grooveBuffer = new List<GrooveStep>();
    private List<GrooveStep> ParseGroove(string text)
    {
        grooveBuffer.Clear();
        if (string.IsNullOrEmpty(text)) return grooveBuffer;
        foreach (var raw in text.Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue; // "0,1,2,3" uses bare positions, so a single char is valid
            char last = token[token.Length - 1];
            char upper = char.ToUpperInvariant(last);
            bool hasLetter = upper == 'K' || upper == 'S' || upper == 'H';
            float position;
            string numberPart = hasLetter ? token.Substring(0, token.Length - 1) : token;
            if (!float.TryParse(numberPart,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out position)) continue;
            grooveBuffer.Add(new GrooveStep
            {
                position = Mathf.Clamp(position, 0f, 3.99f),
                voice = hasLetter ? (upper == 'H' ? 'h' : upper) : 'S'
            });
        }
        return grooveBuffer;
    }

    /// <summary>
    /// Alternates middle and edge: even notes take the sheep closest to the flock centre, odd notes the
    /// farthest. Linear index order sounded like a wave sweeping the flock, and "always farthest" pushed
    /// the middle sheep to the end; this way everyone sings once and the middle is in from the first note.
    /// </summary>
    private List<int> BuildAlternatingOrder()
    {
        var order = new List<int>();
        if (cast.Count == 0) return order;

        Vector2 centre = Vector2.zero;
        int valid = 0;
        foreach (var sheep in cast)
        {
            if (sheep == null) continue;
            centre += (Vector2)sheep.transform.position;
            valid++;
        }
        if (valid > 0) centre /= valid;

        var used = new bool[cast.Count];
        for (int n = 0; n < cast.Count; n++)
        {
            bool wantMiddle = (n % 2) == 0;
            int best = -1;
            float bestDistance = wantMiddle ? float.MaxValue : -1f;
            for (int i = 0; i < cast.Count; i++)
            {
                if (used[i] || cast[i] == null) continue;
                float distance = Vector2.Distance(centre, cast[i].transform.position);
                bool better = wantMiddle ? distance < bestDistance : distance > bestDistance;
                if (better) { bestDistance = distance; best = i; }
            }
            if (best < 0) break;
            used[best] = true;
            order.Add(best);
        }
        return order;
    }

    /// <summary>Beat 1 is heaviest, offbeats are lighter - the weak/strong alternation is what reads as a beat.</summary>
    private float Accent(float position)
    {
        if (position < 0.01f) return accentDownbeat;
        return Mathf.Approximately(position, Mathf.Round(position)) ? accentOnBeat : accentOffBeat;
    }

    private void StopChorusBounce()
    {
        for (int i = 0; i < cast.Count; i++)
        {
            if (cast[i] == null) continue;
            cast[i].CancelRhythm();
            var bounce = cast[i].GetComponent<BeatBounce>();
            if (bounce != null) bounce.StopRhythm(this);
        }
        if (player != null)
        {
            var bounce = player.GetComponent<BeatBounce>();
            if (bounce != null) bounce.StopRhythm(this);
        }
    }

    private void ComputeFrame(float padding, float maxSize)
    {
        if (view == null) { frameCenter = Vector3.zero; frameSize = 8f; return; }

        // Keep the player in the middle and only pull back far enough to include the flock.
        Vector2 anchor = player != null ? (Vector2)player.transform.position : (Vector2)transform.position;
        frameCenter = anchor;

        float radius = 0f;
        foreach (var sheep in cast)
            if (sheep != null) radius = Mathf.Max(radius, Vector2.Distance(anchor, sheep.transform.position));

        float halfHeight = Mathf.Max(1.5f, radius + 1.5f);
        frameSize = Mathf.Clamp(halfHeight * padding, 4f, maxSize);
    }

    private void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void Update()
    {
        if (!finished) return;
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) Restart();
    }

    private void OnGUI()
    {
        if (!running && !finished) return;
        float scale = Mathf.Max(0.5f, Screen.height / 720f);
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        // Fade to white: the pasture washes out rather than going dark.
        if (fadeAlpha > 0.001f)
        {
            GUI.color = new Color(1f, 1f, 1f, fadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
        }

        // Drawn after the white overlay so the button sits on top of it. Label-based style, so there
        // is no button box: just black text on the white screen.
        if (restartAlpha > 0.001f)
        {
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 26,
                    alignment = TextAnchor.MiddleCenter
                };
                buttonStyle.normal.textColor = Color.black;
                buttonStyle.hover.textColor = new Color(0.25f, 0.25f, 0.25f);
                buttonStyle.active.textColor = Color.black;
            }
            Rect button = new Rect(width * 0.5f - 110f, height * 0.5f - 26f, 220f, 52f);
            GUI.color = new Color(1f, 1f, 1f, restartAlpha);
            if (GUI.Button(button, "Restart", buttonStyle)) Restart();
        }

        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }
}
