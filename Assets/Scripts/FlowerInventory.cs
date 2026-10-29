using System;
using UnityEngine;

public sealed class FlowerInventory : MonoBehaviour
{
    [SerializeField] private DemandingGroup requestingGroup;

    public int Count { get; private set; }
    public int TotalCollected { get; private set; }
    public event Action<int> Changed;

    public bool CanCollect => requestingGroup != null && requestingGroup.CanCollectFlowers && Count < requestingGroup.CurrentDemand;

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
