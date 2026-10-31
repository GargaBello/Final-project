using System.Collections.Generic;
using UnityEngine;

public sealed class InteractionUI : MonoBehaviour
{
    [SerializeField] private FlowerInventory inventory;
    [SerializeField] private DemandingGroup demandingGroup;
    [SerializeField] private Sprite flowerIcon;
    [SerializeField] private Camera worldCamera;
    private Transform requestAnchor;
    [SerializeField, Range(0f, 1f)] private float requestOpacity = 0.45f;
    [SerializeField, Min(0f)] private float openingHintDuration = 8f;

    private readonly List<SpriteRenderer> requestIcons = new List<SpriteRenderer>();
    private Transform iconRoot;
    private readonly List<SpriteRenderer> carriedIcons = new List<SpriteRenderer>();
    private Transform carriedRoot;
    private float openedAt;
    private GUIStyle hint;

    private void Start()
    {
        openedAt = Time.time;
        var root = new GameObject("FlowerDemandIcons");
        iconRoot = root.transform;
        iconRoot.SetParent(transform, false);
        iconRoot.localPosition = Vector3.up;
        root.SetActive(false);
    }

    private void LateUpdate()
    {
        UpdateCarriedFlowers();
        if (iconRoot == null || demandingGroup == null) return;
        bool show = demandingGroup.ShowRequest;
        iconRoot.gameObject.SetActive(show);
        if (!show) return;
        Transform speaker = demandingGroup.RequestSpeaker.transform;
        if (requestAnchor != speaker)
        {
            requestAnchor = speaker;
            iconRoot.SetParent(requestAnchor, false);
        }
        int count = demandingGroup.CurrentDemand;
        while (requestIcons.Count < count)
        {
            var go = new GameObject("DemandFlower");
            go.transform.SetParent(iconRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = flowerIcon;
            renderer.sortingOrder = 20000;
            requestIcons.Add(renderer);
        }
        iconRoot.localPosition = Vector3.up * (1f + Mathf.Sin(Time.time * 2f) * 0.04f);
        for (int i = 0; i < requestIcons.Count; i++)
        {
            var icon = requestIcons[i];
            icon.gameObject.SetActive(i < count);
            icon.color = new Color(1f, 1f, 1f, requestOpacity);
            icon.transform.localScale = Vector3.one * 0.65f;
            icon.transform.localPosition = new Vector3((i - (count - 1) * 0.5f) * 0.7f, 0, 0);
        }
    }

    private void UpdateCarriedFlowers()
    {
        if (inventory == null || flowerIcon == null)
        {
            if (carriedRoot != null) carriedRoot.gameObject.SetActive(false);
            return;
        }
        if (carriedRoot == null)
        {
            if (inventory.Count == 0) return;
            carriedRoot = new GameObject("CarriedFlowers").transform;
            carriedRoot.SetParent(inventory.transform, false);
        }
        carriedRoot.gameObject.SetActive(inventory.Count > 0);
        if (inventory.Count == 0) return;
        while (carriedIcons.Count < inventory.Count)
        {
            var icon = new GameObject("CarriedFlower");
            icon.transform.SetParent(carriedRoot, false);
            var renderer = icon.AddComponent<SpriteRenderer>();
            renderer.sprite = flowerIcon;
            renderer.color = Color.white;
            renderer.sortingOrder = 20001;
            carriedIcons.Add(renderer);
        }
        carriedRoot.localPosition = Vector3.up * (1.05f + Mathf.Sin(Time.time * 2f) * 0.035f);
        for (int i = 0; i < carriedIcons.Count; i++)
        {
            var icon = carriedIcons[i];
            icon.gameObject.SetActive(i < inventory.Count);
            icon.transform.localScale = Vector3.one * 0.65f;
            icon.transform.localPosition = new Vector3((i - (inventory.Count - 1) * 0.5f) * 0.65f, 0f, 0f);
        }
    }

    private void OnGUI()
    {
        if (inventory == null || demandingGroup == null) return;
        if (hint == null)
        {
            hint = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        }
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        float scale = Mathf.Max(0.5f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        if (Time.time - openedAt < openingHintDuration)
            GUI.Label(new Rect(width * 0.5f - 180, height - 50, 360, 34), "WASD: Move    SPACE: Bleat", hint);
        if (demandingGroup.State == SheepGroupController.GroupState.Refusing && demandingGroup.IsNearby)
            GUI.Label(new Rect(width * 0.5f - 180, height - 90, 360, 34), "No flowers left.", hint);
        GUI.color = oldColor;
        GUI.matrix = oldMatrix;
    }

    private void OnDisable()
    {
        if (iconRoot != null) iconRoot.gameObject.SetActive(false);
        if (carriedRoot != null) carriedRoot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (iconRoot != null) Destroy(iconRoot.gameObject);
        if (carriedRoot != null) Destroy(carriedRoot.gameObject);
    }
}
