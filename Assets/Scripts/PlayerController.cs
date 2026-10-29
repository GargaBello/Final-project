using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public sealed class PlayerController : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField, Min(0f)] private float moveSpeed = 3f;

    private Rigidbody2D body;
    private SpriteRenderer visual;
    private InputActionAsset runtimeActions;
    private InputAction moveAction;
    private InputAction bleatAction;
    private Vector2 pushVelocity;
    private float pushUntil;
    private Object exclusionOwner;
    private Vector2 exclusionCenter;
    private Vector2 escapeDirection = Vector2.down;
    private float exclusionRadius;
    private float escapeSpeed;

    public Vector2 MoveInput { get; private set; }
    public InputAction BleatAction { get { PrepareInput(); return bleatAction; } }
    public bool HasGroupExclusion => exclusionOwner != null;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        visual = GetComponent<SpriteRenderer>();
        PrepareInput();
    }

    private void PrepareInput()
    {
        if (runtimeActions != null || inputActions == null) return;
        // Each player owns its enabled actions; the source asset stays unchanged.
        runtimeActions = Instantiate(inputActions);
        moveAction = runtimeActions.FindAction("Player/Move", true);
        bleatAction = runtimeActions.FindAction("Player/Bleat", true);
    }

    private void OnEnable()
    {
        PrepareInput();
        moveAction?.Enable();
        bleatAction?.Enable();
    }

    private void Update()
    {
        MoveInput = moveAction == null ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        if (Mathf.Abs(MoveInput.x) > 0.01f) visual.flipX = MoveInput.x < 0f;
    }

    private void FixedUpdate()
    {
        Vector2 velocity = Time.time < pushUntil ? pushVelocity : MoveInput * moveSpeed;
        body.linearVelocity = ConstrainGroupEntry(velocity);
    }

    public void SetGroupExclusion(Object owner, Vector2 center, float radius, Vector2 fallbackDirection, float outwardSpeed)
    {
        exclusionOwner = owner;
        exclusionCenter = center;
        exclusionRadius = Mathf.Max(0f, radius);
        escapeDirection = fallbackDirection.sqrMagnitude > 0.001f ? fallbackDirection.normalized : Vector2.down;
        escapeSpeed = Mathf.Max(moveSpeed, outwardSpeed);
    }

    public void ClearGroupExclusion(Object owner)
    {
        if (exclusionOwner != owner) return;
        exclusionOwner = null;
        pushUntil = 0f;
    }

    private Vector2 ConstrainGroupEntry(Vector2 velocity)
    {
        if (exclusionOwner == null) return velocity;
        Vector2 fromCenter = body.position - exclusionCenter;
        float distance = fromCenter.magnitude;
        Vector2 outward = distance > 0.05f ? fromCenter / distance : escapeDirection;
        float radialSpeed = Vector2.Dot(velocity, outward);

        if (distance < exclusionRadius - 0.05f)
        {
            // Keep lateral steering and outward input while guiding an ejected player out.
            velocity += outward * Mathf.Max(0f, escapeSpeed - radialSpeed);
            return Vector2.ClampMagnitude(velocity, escapeSpeed);
        }
        Vector2 next = fromCenter + velocity * Time.fixedDeltaTime;
        if (radialSpeed < 0f && (distance <= exclusionRadius + 0.08f || next.magnitude < exclusionRadius))
            velocity -= outward * radialSpeed;
        return velocity;
    }

    public void ApplyPush(Vector2 direction, float speed, float duration)
    {
        if (direction.sqrMagnitude < 0.001f) direction = Vector2.down;
        pushVelocity = direction.normalized * Mathf.Max(0f, speed);
        pushUntil = Time.time + Mathf.Max(0f, duration);
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        bleatAction?.Disable();
        MoveInput = Vector2.zero;
        pushUntil = 0f;
        exclusionOwner = null;
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (runtimeActions != null) Destroy(runtimeActions);
    }
}
