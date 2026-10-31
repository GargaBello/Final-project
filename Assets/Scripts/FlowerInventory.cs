using System;
using UnityEngine;

public sealed class FlowerInventory : MonoBehaviour
{
    [SerializeField] private DemandingGroup requestingGroup;
    private bool visitedAfterFinalRefusal;

    public int Count { get; private set; }
    public int TotalCollected { get; private set; }
    public event Action<int> Changed;

    public bool CanCollect => requestingGroup != null && requestingGroup.CanCollectFlowers && Count < requestingGroup.CurrentDemand;

    private void Update()
    {
        if (requestingGroup == null || requestingGroup.State != SheepGroupController.GroupState.Refusing)
        {
            visitedAfterFinalRefusal = false;
            return;
        }
        // Keep the final flowers for the return to the flock; discard them only on departure.
        if (requestingGroup.IsNearby) visitedAfterFinalRefusal = true;
        else if (visitedAfterFinalRefusal && Count > 0)
        {
            Count = 0;
            Changed?.Invoke(Count);
        }
    }

    public bool TryAddFlower()
    {
        if (!CanCollect) return false;
        Count++;
        TotalCollected++;
        Changed?.Invoke(Count);
        return true;
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || Count < amount) return false;
        Count -= amount;
        Changed?.Invoke(Count);
        return true;
    }
}
