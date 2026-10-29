using UnityEngine;

[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public sealed class FlowerCollectible : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float pickupDistance = 1.1f;

    public bool IsCollected { get; private set; }

    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other.GetComponentInParent<FlowerInventory>());
    private void OnTriggerStay2D(Collider2D other) => TryCollect(other.GetComponentInParent<FlowerInventory>());

    public bool TryCollect(FlowerInventory inventory)
    {
        if (IsCollected || !isActiveAndEnabled || inventory == null || !inventory.CanCollect) return false;
        if (Vector2.Distance(transform.position, inventory.transform.position) > pickupDistance) return false;

        // Set this before notifying the inventory so the group sees the correct remaining supply.
        IsCollected = true;
        if (!inventory.TryAddFlower())
        {
            IsCollected = false;
            return false;
        }
        gameObject.SetActive(false);
        return true;
    }
}
