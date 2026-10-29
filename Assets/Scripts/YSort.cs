using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class YSort : MonoBehaviour
{
    private SpriteRenderer visual;

    private void OnEnable()
    {
        visual = GetComponent<SpriteRenderer>();
        ApplySorting();
    }

    private void LateUpdate() => ApplySorting();

    private void ApplySorting()
    {
        if (visual == null) return;
        // Actors and individual decoration tiles share an order so world-Y decides occlusion.
        visual.sortingOrder = 0;
        visual.spriteSortPoint = SpriteSortPoint.Pivot;
        Camera camera = Camera.main;
        if (camera == null) return;
        camera.transparencySortMode = TransparencySortMode.CustomAxis;
        camera.transparencySortAxis = Vector3.up;
    }
}
