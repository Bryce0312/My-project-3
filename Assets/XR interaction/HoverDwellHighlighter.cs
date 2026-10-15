using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;

public class HoverDwellHighlighter : MonoBehaviour
{
    public float dwellTime = 1.5f;
    private Coroutine dwellCoroutine;
    private Outline outline;
    private XRSimpleInteractable interactable;

    private void Start()
    {
        outline = GetComponent<Outline>();
        if (outline == null) outline = gameObject.AddComponent<Outline>();
        outline.enabled = false;
        outline.OutlineColor = Color.yellow;
        outline.OutlineWidth = 5f;

        interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null) interactable = gameObject.AddComponent<XRSimpleInteractable>();
        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
    }

    private void OnDestroy()
    {
        if (interactable == null) return;
        interactable.hoverEntered.RemoveListener(OnHoverEntered);
        interactable.hoverExited.RemoveListener(OnHoverExited);
    }

    public void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (outline != null && outline.enabled) return;
        if (dwellCoroutine != null) StopCoroutine(dwellCoroutine);
        dwellCoroutine = StartCoroutine(DwellTimer());
    }

    public void OnHoverExited(HoverExitEventArgs args)
    {
        if (dwellCoroutine != null)
        {
            StopCoroutine(dwellCoroutine);
            dwellCoroutine = null;
        }
        if (outline != null) outline.enabled = false;
    }

    private IEnumerator DwellTimer()
    {
        yield return new WaitForSeconds(dwellTime);
        if (outline != null) outline.enabled = true;
        dwellCoroutine = null;
    }

    public void SetSelected(bool selected)
    {
        if (outline == null) return;
        outline.enabled = selected;
        outline.OutlineColor = selected ? Color.cyan : Color.green;
    }
}