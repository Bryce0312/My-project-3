using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Review
{
    /// <summary>
    /// Adds an on-demand journey entry beside the existing wrist HUD controls. The scene and
    /// original button callbacks stay authored as they are; only this runtime entry is owned here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReviewHudEntry : MonoBehaviour
    {
        private sealed class HorizontalLayout
        {
            public RectTransform Rect;
            public float AnchorMin;
            public float AnchorMax;
            public float Position;
            public float Size;
            public float AppliedMin;
            public float AppliedMax;
            public float AppliedPosition;
            public float AppliedSize;
        }

        private readonly List<HorizontalLayout> layouts = new List<HorizontalLayout>();
        private readonly List<GameObject> retiringEntries = new List<GameObject>();
        private Button helpButton;
        private Action onOpen;
        private Action onFinish;
        private Button entryButton;
        private Button endButton;
        private Text endLabel;
        private bool resultsAvailable;

        public Button EntryButton => entryButton;
        public Button EndButton => endButton;
        public bool IsAvailable => isActiveAndEnabled && entryButton != null &&
            entryButton.gameObject.activeInHierarchy;

        public void Configure(Button help, Action openGuidance)
        {
            Configure(help, openGuidance, null);
        }

        public void Configure(Button help, Action openGuidance, Action finishReview)
        {
            bool sameButtonSet = (onFinish != null) == (finishReview != null);
            if (helpButton == help && entryButton != null && sameButtonSet)
            {
                onOpen = openGuidance;
                onFinish = finishReview;
                return;
            }

            ReleaseEntry();
            helpButton = help;
            onOpen = openGuidance;
            onFinish = finishReview;
            if (isActiveAndEnabled)
            {
                BuildEntry();
            }
        }

        public void SetResultsAvailable(bool finished)
        {
            resultsAvailable = finished;
            if (endLabel != null)
            {
                string text = finished ? "RESULTS" : "END REVIEW";
                if (endLabel.text != text) endLabel.text = text;
            }
        }

        private void OnEnable() => BuildEntry();
        private void OnDisable() => ReleaseEntry();
        private void OnDestroy() => ReleaseEntry();

        public void Dispose()
        {
            ReleaseEntry();
            helpButton = null;
            onOpen = null;
            onFinish = null;
        }

        private void BuildEntry()
        {
            if (entryButton != null || helpButton == null || onOpen == null)
            {
                return;
            }

            RectTransform helpRect = helpButton.transform as RectTransform;
            Transform parent = helpButton.transform.parent;
            if (helpRect == null || parent == null)
            {
                return;
            }

            var row = new List<RectTransform>();
            retiringEntries.RemoveAll(entry => entry == null);
            foreach (Transform child in parent)
            {
                // Destroy is deferred. A retired entry can still be a child during a
                // same-frame disable/re-enable, but must not become an original row button.
                if (retiringEntries.Contains(child.gameObject)) continue;
                RectTransform rect = child as RectTransform;
                if (rect != null && child.GetComponent<Button>() != null &&
                    Mathf.Abs(rect.anchorMin.y - helpRect.anchorMin.y) < 0.01f &&
                    Mathf.Abs(rect.anchorMax.y - helpRect.anchorMax.y) < 0.01f)
                {
                    row.Add(rect);
                }
            }

            if (row.Count == 0)
            {
                return;
            }

            row.Sort((a, b) => a.anchorMin.x.CompareTo(b.anchorMin.x));
            float rowMin = row[0].anchorMin.x;
            float rowMax = row[row.Count - 1].anchorMax.x;

            entryButton = CreateButton(parent, "Button_Guide", "GUIDE", OpenGuidance);
            GameObject entryObject = entryButton.gameObject;

            int insertion = row.IndexOf(helpRect) + 1;
            RectTransform entryRect = entryObject.transform as RectTransform;
            row.Insert(insertion, entryRect);
            entryRect.SetSiblingIndex(helpRect.GetSiblingIndex() + 1);

            RectTransform endRect = null;
            if (onFinish != null)
            {
                endButton = CreateButton(parent, "Button_EndReview", "END REVIEW", FinishReview);
                endLabel = endButton.GetComponentInChildren<Text>(true);
                if (endLabel != null)
                {
                    // END REVIEW can occupy two lines; RESULTS fits one line. Best-fit
                    // applies only to this added button, without changing original labels.
                    endLabel.resizeTextForBestFit = true;
                    endLabel.resizeTextMinSize = 16;
                    endLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
                    RectTransform textRect = endLabel.rectTransform;
                    textRect.sizeDelta = new Vector2(-4f, textRect.sizeDelta.y);
                }
                SetResultsAvailable(resultsAvailable);
                endRect = endButton.transform as RectTransform;
                row.Insert(insertion + 1, endRect);
                endRect.SetSiblingIndex(entryRect.GetSiblingIndex() + 1);
            }

            float gap = Mathf.Min(0.01f, (rowMax - rowMin) / (row.Count * 8f));
            float width = (rowMax - rowMin - gap * (row.Count - 1)) / row.Count;
            for (int i = 0; i < row.Count; i++)
            {
                RectTransform rect = row[i];
                var layout = new HorizontalLayout
                {
                    Rect = rect,
                    AnchorMin = rect.anchorMin.x,
                    AnchorMax = rect.anchorMax.x,
                    Position = rect.anchoredPosition.x,
                    Size = rect.sizeDelta.x,
                    AppliedMin = rowMin + i * (width + gap),
                    AppliedMax = rowMin + i * (width + gap) + width,
                    AppliedPosition = 0f,
                    AppliedSize = helpRect.sizeDelta.x,
                };
                rect.anchorMin = new Vector2(layout.AppliedMin, rect.anchorMin.y);
                rect.anchorMax = new Vector2(layout.AppliedMax, rect.anchorMax.y);
                rect.anchoredPosition = new Vector2(layout.AppliedPosition, rect.anchoredPosition.y);
                rect.sizeDelta = new Vector2(layout.AppliedSize, rect.sizeDelta.y);
                if (rect != entryRect && rect != endRect)
                {
                    layouts.Add(layout);
                }
            }

            entryObject.SetActive(true);
            if (endButton != null) endButton.gameObject.SetActive(true);
        }

        private Button CreateButton(Transform parent, string name, string text,
            UnityEngine.Events.UnityAction onClick)
        {
            // HELP is the scene's plain Button + Image + Text template. A new event object
            // removes serialized persistent callbacks as well as any copied runtime calls.
            GameObject buttonObject = Instantiate(helpButton.gameObject, parent, false);
            buttonObject.name = name;
            buttonObject.SetActive(false);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            button.interactable = true;
            button.enabled = true;
            Text label = buttonObject.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
            return button;
        }

        private void OpenGuidance()
        {
            if (IsAvailable)
            {
                onOpen?.Invoke();
            }
        }

        private void FinishReview()
        {
            if (isActiveAndEnabled && endButton != null && endButton.gameObject.activeInHierarchy)
            {
                onFinish?.Invoke();
            }
        }

        private void ReleaseEntry()
        {
            RetireButton(ref entryButton, OpenGuidance);
            RetireButton(ref endButton, FinishReview);
            endLabel = null;

            foreach (HorizontalLayout layout in layouts)
            {
                RectTransform rect = layout.Rect;
                if (rect == null)
                {
                    continue;
                }

                // Restore only values still owned by this component, preserving any later
                // layout changes made by another HUD feature while the journey was active.
                if (Mathf.Approximately(rect.anchorMin.x, layout.AppliedMin))
                    rect.anchorMin = new Vector2(layout.AnchorMin, rect.anchorMin.y);
                if (Mathf.Approximately(rect.anchorMax.x, layout.AppliedMax))
                    rect.anchorMax = new Vector2(layout.AnchorMax, rect.anchorMax.y);
                if (Mathf.Approximately(rect.anchoredPosition.x, layout.AppliedPosition))
                    rect.anchoredPosition = new Vector2(layout.Position, rect.anchoredPosition.y);
                if (Mathf.Approximately(rect.sizeDelta.x, layout.AppliedSize))
                    rect.sizeDelta = new Vector2(layout.Size, rect.sizeDelta.y);
            }
            layouts.Clear();
        }

        private void RetireButton(ref Button button, UnityEngine.Events.UnityAction onClick)
        {
            if (button == null) return;
            Button retiringButton = button;
            button = null;
            GameObject entryObject = retiringButton.gameObject;
            retiringButton.onClick.RemoveListener(onClick);
            // Never reparent from OnDisable/OnDestroy: the HUD's ancestor may already
            // be activating/deactivating. Track both entries until deferred destruction.
            retiringEntries.Add(entryObject);
            entryObject.SetActive(false);
            Destroy(entryObject);
        }
    }
}
