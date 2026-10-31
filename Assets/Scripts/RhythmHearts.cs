using UnityEngine;

// Three world-space pixel hearts follow the player's feet position, independently of squash.
public sealed class RhythmHearts : MonoBehaviour
{
    private ConformistGroup group;
    private Transform root;
    private SpriteRenderer[] hearts;
    private Texture2D texture;
    private Sprite sprite;

    public void Bind(ConformistGroup owner) => group = owner;

    private void CreateHearts()
    {
        string[] pixels =
        {
            ".rrr..rrr.", "rrrrrrrrrr", "rrrrrrrrrr", "rrrrrrrrrr",
            ".rrrrrrrr.", "..rrrrrr..", "...rrrr...", "....rr...."
        };
        texture = new Texture2D(10, 8, TextureFormat.RGBA32, false)
        {
            name = "RhythmHeart", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 10; x++)
                texture.SetPixel(x, 7 - y, pixels[y][x] == 'r' ? Color.white : Color.clear);
        texture.Apply(false, false);
        sprite = Sprite.Create(texture, new Rect(0, 0, 10, 8), new Vector2(0.5f, 0.5f), 24f);
        sprite.name = "RhythmHeart";
        root = new GameObject("RhythmHearts").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, 0.7f, 0f);
        hearts = new SpriteRenderer[ConformistGroup.HeartCount];
        for (int i = 0; i < hearts.Length; i++)
        {
            var icon = new GameObject("Heart" + (i + 1));
            icon.transform.SetParent(root, false);
            icon.transform.localPosition = new Vector3((i - 1) * 0.5f, 0f, 0f);
            hearts[i] = icon.AddComponent<SpriteRenderer>();
            hearts[i].sprite = sprite;
            hearts[i].sortingOrder = 30000;
        }
    }

    private void LateUpdate()
    {
        if (PauseMenu.IsPaused) return;
        bool visible = group != null && group.isActiveAndEnabled && group.ShowHearts;
        if (root == null)
        {
            if (!visible) return;
            CreateHearts();
        }
        root.gameObject.SetActive(visible);
        if (!visible) return;
        float elapsed = Time.unscaledTime - group.LastHeartLostAt;
        float shake = elapsed < 0.3f ? Mathf.Sin(elapsed * 60f) * 0.035f * (1f - elapsed / 0.3f) : 0f;
        root.localPosition = new Vector3(shake, 0.7f, 0f);
        for (int i = 0; i < hearts.Length; i++)
        {
            bool filled = i < group.RemainingHearts;
            bool justLost = i == group.RemainingHearts && elapsed < 0.2f;
            hearts[i].color = justLost ? new Color(1f, 0.85f, 0.85f, 1f)
                : filled ? new Color(1f, 0.25f, 0.32f, 0.95f) : new Color(0.5f, 0.5f, 0.5f, 0.25f);
            float scale = justLost ? 1f + elapsed * 1.5f : 1f;
            hearts[i].transform.localScale = Vector3.one * scale;
        }
    }

    private void OnDisable()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }
}
