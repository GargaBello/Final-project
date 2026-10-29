using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class PastureCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 pastureMin = new Vector2(-40f, -30f);
    [SerializeField] private Vector2 pastureMax = new Vector2(40f, 30f);
    [SerializeField, Min(0f)] private float followSmoothTime = 0.18f;

    private Camera view;
    private Vector3 velocity;

    private void Awake()
    {
        view = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float halfHeight = view.orthographicSize;
        float halfWidth = halfHeight * view.aspect;
        Vector3 destination = new Vector3(
            ClampAxis(target.position.x, pastureMin.x, pastureMax.x, halfWidth),
            ClampAxis(target.position.y, pastureMin.y, pastureMax.y, halfHeight),
            transform.position.z);

        transform.position = Vector3.SmoothDamp(
            transform.position, destination, ref velocity, followSmoothTime);
    }

    private static float ClampAxis(float value, float min, float max, float halfView)
    {
        return max - min <= halfView * 2f
            ? (min + max) * 0.5f
            : Mathf.Clamp(value, min + halfView, max - halfView);
    }
}
