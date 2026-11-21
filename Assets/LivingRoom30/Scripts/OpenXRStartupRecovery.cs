#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace LivingRoom30
{
    /// <summary>Let OpenXR recover when the PC headset is not ready on the first Play frame.</summary>
    internal static class OpenXRStartupRecovery
    {
        // XR Management starts at AfterAssembliesLoaded. Configure the native retry policy first,
        // on every Play entry, including when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void EnableStartupRetry()
        {
            // Use the package's restart loop so loader cleanup and session startup have one owner.
            OpenXRRuntime.retryInitializationOnFormFactorErrors = true;
            Debug.Log("[XRStartupRecovery] OpenXR startup retry enabled; waiting for a real headset does not require another Play cycle.");
        }
    }
}
#endif
