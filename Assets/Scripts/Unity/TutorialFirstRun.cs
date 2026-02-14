using UnityEngine;

public sealed class TutorialFirstRun : MonoBehaviour
{
    [SerializeField] private TutorialOverlayView tutorial;

    private void Start()
    {
        if (tutorial == null) return;

        if (!TutorialSessionGate.TryConsume())
            return;

        tutorial.OpenFromStart();
    }
}
