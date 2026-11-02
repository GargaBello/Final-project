using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
public sealed class PauseMenu : MonoBehaviour
{
    private static PauseMenu instance;
    public static bool IsPaused { get; private set; }
    private float previousTimeScale = 1f;
    private bool previousAudioPause;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    private GUIStyle buttonStyle;
    private int endingShortcutPresses;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance = null;
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsPaused) Resume();
            else Pause();
        }
        if (IsPaused && Keyboard.current != null && Keyboard.current.uKey.wasPressedThisFrame)
        {
            endingShortcutPresses++;
            if (endingShortcutPresses >= 3)
            {
                endingShortcutPresses = 0;
                var progression = FindFirstObjectByType<AcceptanceProgression>();
                if (progression != null) progression.UnlockForTesting();
            }
        }
    }

    private void Pause()
    {
        if (IsPaused) return;
        endingShortcutPresses = 0;
        previousTimeScale = Time.timeScale;
        previousAudioPause = AudioListener.pause;
        previousCursorVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        IsPaused = true;
        Time.timeScale = 0f;
        // This freezes dspTime as well as scheduled voices, preserving the rhythm judgment clock.
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        if (!IsPaused || instance != this) return;
        endingShortcutPresses = 0;
        IsPaused = false;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = previousAudioPause;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    private void Restart()
    {
        Scene scene = SceneManager.GetActiveScene();
        Resume();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
#if UNITY_EDITOR
        else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        else Debug.LogError("The active scene must be included in Build Settings to restart.");
#endif
    }

    private void OnGUI()
    {
        if (!IsPaused || instance != this) return;
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0)
            };
            foreach (var state in new[] { buttonStyle.normal, buttonStyle.hover, buttonStyle.active, buttonStyle.focused,
                buttonStyle.onNormal, buttonStyle.onHover, buttonStyle.onActive, buttonStyle.onFocused })
            {
                state.background = Texture2D.whiteTexture;
                state.textColor = Color.black;
            }
        }
        int previousDepth = GUI.depth;
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.depth = -1000;
        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.white;
        float scale = Mathf.Max(0.75f, Screen.height / 720f);
        float width = 220f * scale;
        float height = 52f * scale;
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height * 0.5f - height - 8f * scale;
        if (GUI.Button(new Rect(x, y, width, height), "Resume", buttonStyle)) Resume();
        if (GUI.Button(new Rect(x, y + height + 16f * scale, width, height), "Restart", buttonStyle)) Restart();
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    private void OnDisable()
    {
        if (instance == this) Resume();
    }

    private void OnDestroy()
    {
        if (instance == this) { Resume(); instance = null; }
    }
}
