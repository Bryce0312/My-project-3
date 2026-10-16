using LivingRoom30.Measurement;
using LivingRoom30.Review;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class AnnotationSpawner : MonoBehaviour
{
    [Header("Annotation UI prefab")]
    public GameObject annotationUIPrefab;

    public static GameObject currentActiveUI;
    private HoverDwellHighlighter highlighter;

    private void Start() => highlighter = GetComponent<HoverDwellHighlighter>();

    // Existing scene-authored Select Entered events keep this entry point.
    public void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (currentActiveUI != null) return;
        ReviewAnnotationStore store = ReviewAnnotationStore.Active;
        if (store != null && store.CanOpen != null && !store.CanOpen()) return;
        SpawnAnnotationUI();
    }

    private void SpawnAnnotationUI()
    {
        if (annotationUIPrefab == null) return;

        SpatialMeasureManager manager = FindObjectOfType<SpatialMeasureManager>();
        Camera camera = manager != null ? manager.ActiveCamera : null;
        if (camera == null) camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("An active camera is needed to open an annotation.", this);
            return;
        }

        Transform cam = camera.transform;
        const float targetDistance = 1f;
        Vector3 spawnPos = cam.position + cam.forward * targetDistance;
        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, targetDistance,
            ~0, QueryTriggerInteraction.Ignore))
            spawnPos = cam.position + cam.forward * Mathf.Max(.35f, hit.distance - .2f);

        UnityEngine.Object instance = Instantiate((UnityEngine.Object)annotationUIPrefab,
            spawnPos, Quaternion.LookRotation(cam.forward, cam.up));
        GameObject ui = instance as GameObject;
        if (ui == null)
        {
            Debug.LogError("Annotation UI prefab must reference a GameObject Canvas.", this);
            if (instance != null) Destroy(instance);
            return;
        }

        AnnotationUI uiScript = ui.GetComponent<AnnotationUI>();
        if (uiScript == null)
        {
            Debug.LogError("Annotation UI prefab is missing AnnotationUI.", this);
            Destroy(ui);
            return;
        }

        Canvas annotationCanvas = ui.GetComponent<Canvas>();
        if (annotationCanvas != null)
        {
            annotationCanvas.worldCamera = camera;
            if (ui.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                ui.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        if (highlighter == null) highlighter = GetComponent<HoverDwellHighlighter>();
        currentActiveUI = ui;
        uiScript.Setup(gameObject, highlighter);
        if (highlighter != null) highlighter.SetSelected(true);
    }
}
