using System;
using System.Collections.Generic;
using LivingRoom30;
using LivingRoom30.Review;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AnnotationUI : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI submitButtonText;
    public TMP_InputField descriptionInput;

    private GameObject targetObject;
    private HoverDwellHighlighter targetHighlighter;
    private string selectedCategory;
    private IssueMarkerVirtualKeyboard virtualKeyboard;
    private readonly List<CategoryButton> categoryButtons = new List<CategoryButton>();
    private Button submitButton, keepEditingButton;
    private TextMeshProUGUI messageText;
    private GameObject messagePanel;
    private Action onFinishSubmitted;
    private bool initialized, submitting, closing;

    private sealed class CategoryButton
    {
        public Button Button;
        public TextMeshProUGUI Label;
        public string Category;
        public FontStyles OriginalStyle;
    }

    public bool IsFinishing => onFinishSubmitted != null;
    public bool IsClosing => closing;
    public string SelectedCategory => selectedCategory;
    public string LastError { get; private set; }

    private void Start()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        Transform fieldTransform = transform.Find("DescriptionDisplay");
        if (descriptionInput == null && fieldTransform != null)
        {
            TextMeshProUGUI text = fieldTransform.GetComponent<TextMeshProUGUI>();
            if (text != null)
            {
                fieldTransform.gameObject.SetActive(false);
                descriptionInput = fieldTransform.gameObject.AddComponent<TMP_InputField>();
                descriptionInput.textComponent = text;
                descriptionInput.targetGraphic = text;
                descriptionInput.textViewport = fieldTransform.GetComponent<RectTransform>();

                GameObject placeholderObject = new GameObject("TypingPlaceholder", typeof(RectTransform), typeof(TextMeshProUGUI));
                placeholderObject.transform.SetParent(fieldTransform, false);
                RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
                placeholderRect.anchorMin = Vector2.zero;
                placeholderRect.anchorMax = Vector2.one;
                placeholderRect.offsetMin = new Vector2(8f, 4f);
                placeholderRect.offsetMax = new Vector2(-8f, -4f);

                TextMeshProUGUI placeholder = placeholderObject.GetComponent<TextMeshProUGUI>();
                placeholder.font = text.font;
                placeholder.fontSize = text.fontSize;
                placeholder.color = new Color(0.65f, 0.65f, 0.65f, 1f);
                placeholder.alignment = text.alignment;
                placeholder.text = "typing...";
                placeholder.raycastTarget = false;
                descriptionInput.placeholder = placeholder;
                descriptionInput.text = string.Empty;
                descriptionInput.contentType = TMP_InputField.ContentType.Standard;
                descriptionInput.lineType = TMP_InputField.LineType.MultiLineNewline;
                descriptionInput.characterLimit = ReviewAnnotationStore.ReasonLimit;
                fieldTransform.gameObject.SetActive(true);
            }
        }

        if (descriptionInput != null)
        {
            descriptionInput.characterLimit = ReviewAnnotationStore.ReasonLimit;
            descriptionInput.onSelect.AddListener(_ => ShowVirtualKeyboard());
            descriptionInput.onValueChanged.AddListener(_ => ClearError());
            virtualKeyboard = new IssueMarkerVirtualKeyboard(transform, descriptionInput);
        }
        else
            Debug.LogError("Issue Marker is missing a usable DescriptionDisplay input.");

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            string category = ReviewAnnotationStore.NormalizeCategory(label != null ? label.text : null);
            if (category != null)
            {
                categoryButtons.Add(new CategoryButton { Button = button, Label = label,
                    Category = category, OriginalStyle = label.fontStyle });
                if (!HasPersistentListener(button, nameof(OnSelectCategory)))
                    button.onClick.AddListener(() => OnSelectCategory(category));
            }
            else if (label == submitButtonText || (label != null
                && string.Equals(label.text.Trim(), "Submit", StringComparison.OrdinalIgnoreCase)))
                submitButton = button;
        }
        if (submitButton != null && !HasPersistentListener(submitButton, nameof(OnSubmit)))
            submitButton.onClick.AddListener(OnSubmit);

        // Keep the existing prefab and its serialized X event. The small extra strip makes
        // save errors and the finish decision visible without replacing the current draft.
        messagePanel = new GameObject("AnnotationStatus", typeof(RectTransform), typeof(Image));
        messagePanel.transform.SetParent(transform, false);
        RectTransform strip = messagePanel.GetComponent<RectTransform>();
        strip.anchoredPosition = new Vector2(0f, -169f);
        strip.sizeDelta = new Vector2(382f, 45f);
        Image stripImage = messagePanel.GetComponent<Image>();
        stripImage.color = new Color(.16f, .18f, .21f, .98f);
        stripImage.raycastTarget = false;
        messageText = CreateText(messagePanel.transform, "Message", Vector2.zero,
            new Vector2(365f, 43f), 14f);
        messagePanel.SetActive(false);

        keepEditingButton = CreateButton("KeepEditing", new Vector2(98f, -121.74f),
            new Vector2(178f, 29.47f), "KEEP EDITING", KeepEditing);
        keepEditingButton.gameObject.SetActive(false);

        TextMeshProUGUI targetText = CreateText(transform, "AnnotationTarget", new Vector2(0f, 90f),
            new Vector2(350f, 20f), 14f);
        FurnitureInfo info = targetObject != null ? targetObject.GetComponent<FurnitureInfo>() : null;
        targetText.text = info != null && !string.IsNullOrWhiteSpace(info.EnglishName)
            ? info.EnglishName : targetObject != null ? targetObject.name : "Object annotation";
        RefreshSubmitLabel();
    }

    private bool HasPersistentListener(Button button, string method)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentTarget(i) == this
                && button.onClick.GetPersistentMethodName(i) == method)
                return true;
        return false;
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, Vector2 position,
        Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = titleText != null ? titleText.font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(string name, Vector2 position, Vector2 size, string label, Action action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(transform, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = new Color(.23f, .25f, .28f, 1f);
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        CreateText(go.transform, "Label", Vector2.zero, size - new Vector2(8f, 2f), 12f).text = label;
        return button;
    }

    private void ShowVirtualKeyboard()
    {
        if (virtualKeyboard == null && descriptionInput != null)
            virtualKeyboard = new IssueMarkerVirtualKeyboard(transform, descriptionInput);
        virtualKeyboard?.Show();
    }

    public void Setup(GameObject target, HoverDwellHighlighter highlighter)
    {
        targetObject = target;
        targetHighlighter = highlighter;
    }

    public void OnSelectCategory(string category)
    {
        EnsureInitialized();
        selectedCategory = ReviewAnnotationStore.NormalizeCategory(category);
        ClearError();
        foreach (CategoryButton item in categoryButtons)
            item.Label.fontStyle = item.Category == selectedCategory
                ? item.OriginalStyle | FontStyles.Bold : item.OriginalStyle;
        RefreshSubmitLabel();
    }

    public void OnSubmit()
    {
        TrySubmit();
    }

    public bool TrySubmit()
    {
        if (submitting || closing) return false;
        EnsureInitialized();
        virtualKeyboard?.Hide();
        ReviewAnnotationStore store = ReviewAnnotationStore.Active;
        string error;
        if (store == null)
        {
            ShowError("The review is not ready to save. Your draft is still here.");
            return false;
        }
        submitting = true;
        bool success = store.TrySubmit(targetObject, selectedCategory,
            descriptionInput != null ? descriptionInput.text : string.Empty, out error);
        submitting = false;
        if (!success)
        {
            ShowError(error);
            return false;
        }

        Action finish = onFinishSubmitted;
        CloseUI();
        finish?.Invoke();
        return true;
    }

    public void RequestFinish(Action onSubmitted)
    {
        if (closing || onSubmitted == null) return;
        EnsureInitialized();
        onFinishSubmitted = onSubmitted;
        keepEditingButton.gameObject.SetActive(true);
        if (submitButton != null)
        {
            RectTransform rect = submitButton.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-98f, -121.74f);
            rect.sizeDelta = new Vector2(178f, 29.47f);
        }
        virtualKeyboard?.Hide();
        ClearError();
        RefreshSubmitLabel();
    }

    public void KeepEditing()
    {
        onFinishSubmitted = null;
        if (keepEditingButton != null) keepEditingButton.gameObject.SetActive(false);
        if (submitButton != null)
        {
            RectTransform rect = submitButton.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-.2f, -121.74f);
            rect.sizeDelta = new Vector2(91.27f, 29.47f);
        }
        ClearError();
        RefreshSubmitLabel();
    }

    private void RefreshSubmitLabel()
    {
        if (submitButtonText != null) submitButtonText.text = IsFinishing ? "SAVE & END" : "Submit";
    }

    private void ShowError(string error)
    {
        LastError = string.IsNullOrEmpty(error) ? "Could not save. Your draft is still here." : error;
        messageText.text = LastError;
        messagePanel.SetActive(true);
    }

    private void ClearError()
    {
        LastError = null;
        if (messageText == null) return;
        messageText.text = IsFinishing ? "Save this draft to finish, or keep editing." : string.Empty;
        messagePanel.SetActive(IsFinishing);
    }

    public void OnCancel()
    {
        if (IsFinishing)
        {
            KeepEditing();
            return;
        }
        CloseUI();
    }

    public void CloseUI()
    {
        if (closing) return;
        closing = true;
        onFinishSubmitted = null;
        if (targetHighlighter != null)
            targetHighlighter.SetSelected(false);
        if (AnnotationSpawner.currentActiveUI == gameObject)
            AnnotationSpawner.currentActiveUI = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (AnnotationSpawner.currentActiveUI == gameObject)
            AnnotationSpawner.currentActiveUI = null;
        if (!closing && targetHighlighter != null) targetHighlighter.SetSelected(false);
    }
}

internal sealed class IssueMarkerVirtualKeyboard
{
    private readonly TMP_InputField input;
    private readonly RectTransform keyboardRoot;
    private readonly GameObject keyboardObject;

    public IssueMarkerVirtualKeyboard(Transform canvasRoot, TMP_InputField targetInput)
    {
        input = targetInput;
        keyboardObject = new GameObject("IssueMarkerVirtualKeyboard", typeof(RectTransform), typeof(Image));
        keyboardObject.transform.SetParent(canvasRoot, false);
        keyboardRoot = keyboardObject.GetComponent<RectTransform>();
        keyboardRoot.anchorMin = new Vector2(0.5f, 0.5f);
        keyboardRoot.anchorMax = new Vector2(0.5f, 0.5f);
        keyboardRoot.pivot = new Vector2(0.5f, 0.5f);
        keyboardRoot.anchoredPosition = new Vector2(0f, -264f);
        keyboardRoot.sizeDelta = new Vector2(360f, 136f);

        Image panel = keyboardObject.GetComponent<Image>();
        panel.color = new Color(0.12f, 0.16f, 0.21f, 0.97f);

        CreateLetterRow("QWERTYUIOP", 44f, 31f);
        CreateLetterRow("ASDFGHJKL", 14f, 34f);
        CreateLetterRow("ZXCVBNM", -16f, 38f);

        CreateActionKey("Space", -65f, -48f, 112f, () => InsertText(" "));
        CreateActionKey("Back", 22f, -48f, 48f, Backspace);
        CreateActionKey("Clear", 75f, -48f, 58f, () => input.text = string.Empty);
        CreateActionKey("Done", 139f, -48f, 58f, Hide);
        keyboardObject.SetActive(false);
    }

    public void Show()
    {
        if (keyboardObject != null)
            keyboardObject.SetActive(true);
    }

    public void Hide()
    {
        keyboardObject.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void CreateLetterRow(string letters, float y, float keyWidth)
    {
        const float gap = 3f;
        float totalWidth = letters.Length * keyWidth + (letters.Length - 1) * gap;
        float x = -totalWidth * 0.5f + keyWidth * 0.5f;

        foreach (char letter in letters)
        {
            string value = letter.ToString();
            CreateKey(value, new Vector2(x, y), new Vector2(keyWidth, 25f), () => InsertText(value));
            x += keyWidth + gap;
        }
    }

    private void CreateActionKey(string label, float x, float y, float width, Action action)
    {
        CreateKey(label, new Vector2(x, y), new Vector2(width, 23f), action);
    }

    private void CreateKey(string label, Vector2 position, Vector2 size, Action action)
    {
        GameObject keyObject = new GameObject("Key_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        keyObject.transform.SetParent(keyboardRoot, false);
        RectTransform keyRect = keyObject.GetComponent<RectTransform>();
        keyRect.anchorMin = new Vector2(0.5f, 0.5f);
        keyRect.anchorMax = new Vector2(0.5f, 0.5f);
        keyRect.pivot = new Vector2(0.5f, 0.5f);
        keyRect.anchoredPosition = position;
        keyRect.sizeDelta = size;

        Image image = keyObject.GetComponent<Image>();
        image.color = new Color(0.9f, 0.93f, 0.97f, 1f);

        Button button = keyObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(keyObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = label.Length == 1 ? 16f : 12f;
        text.color = new Color(0.08f, 0.1f, 0.14f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    private void InsertText(string value)
    {
        if (input == null)
            return;

        input.text += value;
        input.caretPosition = input.text.Length;
        input.selectionAnchorPosition = input.caretPosition;
        input.selectionFocusPosition = input.caretPosition;
    }

    private void Backspace()
    {
        if (input == null || input.text.Length == 0)
            return;

        input.text = input.text.Substring(0, input.text.Length - 1);
        input.caretPosition = input.text.Length;
    }
}
