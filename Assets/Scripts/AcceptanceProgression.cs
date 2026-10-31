using UnityEngine;

public sealed class AcceptanceProgression : MonoBehaviour
{
    [SerializeField] private DemandingGroup demanding;
    [SerializeField] private DividedGroup divided;
    [SerializeField] private ConformistGroup conformist;
    [SerializeField] private AcceptingGroup accepting;
    [SerializeField, Min(0f)] private float quietDelay = 2f;
    private float unlockAt = float.PositiveInfinity;
    private bool unlocked;

    private void Update()
    {
        if (unlocked || demanding == null || divided == null || conformist == null || accepting == null) return;
        bool allRejected = demanding.State == SheepGroupController.GroupState.Refusing
            && divided.CurrentPhase == DividedGroup.Phase.Silent
            && conformist.CurrentPhase == ConformistGroup.Phase.Locked;
        if (!allRejected) { unlockAt = float.PositiveInfinity; return; }
        if (float.IsPositiveInfinity(unlockAt)) unlockAt = Time.time + quietDelay;
        if (Time.time < unlockAt) return;
        accepting.gameObject.SetActive(true);
        accepting.Unlock();
        unlocked = true;
    }
}
