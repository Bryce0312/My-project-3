using UnityEngine;

// Verification-only runtime helper. Copy into the isolated project's Assets/VerificationRuntime,
// never the real project's Assets, so OnDisable executes in a runtime assembly.
public sealed class ReviewHudLifecycleProbe : MonoBehaviour
{
    public Behaviour Target;
    public bool Armed;
    public int DisableCalls { get; private set; }

    private void OnDisable()
    {
        if (!Armed || Target == null) return;
        Armed = false;
        DisableCalls++;
        Target.enabled = false;
    }
}
