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

    private static readonly Color Surface = new Color32(20, 32, 40, 255);
    private static readonly Color Control = new Color32(45, 62, 71, 255);
    private static readonly Color Accent = new Color32(29, 122, 109, 255);
    private static readonly Color Ink = new Color32(237, 245, 244, 255);
    private static readonly Color Muted = new Color32(181, 204, 207, 255);
    private static readonly Color Success = new Color32(150, 242, 202, 255);
    private static readonly Color Warning = new Color32(255, 201, 158, 255);

    private static readonly Dictionary<string, string[]> QuickReasons = new Dictionary<string, string[]>
    {
        { ReviewAnnotationStore.SizeCategory, new[] { "Too high", "Too large" } },
        { ReviewAnnotationStore.CrampedCategory, new[] { "Too close", "Hard to pass" } },
        { ReviewAnnotationStore.LayoutCategory, new[] { "Hard to reach", "Blocks the view" } },
    };

    private static readonly Vector2 PanelSize = new Vector2(384f, 320f);
    private const float ScalePerMeter = .0021f;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[24];
    private readonly Collider[] panelOverlaps = new Collider[24];
    private Camera viewer;
    private GameObject targetObject;
    private HoverDwellHighlighter targetHighlighter;
    private string selectedCategory, selectedReason;
    private IssueMarkerVirtualKeyboard virtualKeyboard;
    private readonly Dictionary<Button, string> categoryButtons = new Dictionary<Button, string>();
    private readonly Button[] reasonButtons = new Button[2];
    private Button submitButton, keepEditingButton, doneButton;
    private TextMeshProUGUI targetText, categoryTitle, reasonTitle, messageText, savedStatus, savedHint;
    private Action onFinishSubmitted;
    private bool initialized, submitting, closing, saved, keyboardOpen;

    public bool IsFinishing => onFinishSubmitted != null;
    public bool IsClosing => closing;
    public string SelectedCategory => selectedCategory;
    public string LastError { get; private set; }

    private void Start() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        var background = transform.Find("Image").GetComponent<Image>();
        background.transform.localScale = Vector3.one;
        background.sprite = null;
        background.color = Surface;
        background.raycastTarget = true;
        Place(background.rectTransform, Vector2.zero, PanelSize);

        titleText.text = "Object note";
        StyleLabel(titleText, 26f, Ink, TextAlignmentOptions.Left, FontStyles.Bold);
        Place(titleText.rectTransform, new Vector2(-22f, 127f), new Vector2(302f, 34f));
        targetText = MakeText("AnnotationTarget", new Vector2(0f, 96f), new Vector2(348f, 27f), 18f, Muted);
        RefreshTargetLabel();

        categoryTitle = MakeText("CategoryTitle", new Vector2(0f, 61f), new Vector2(348f, 19f), 14f, Muted);
        categoryTitle.text = "ISSUE CATEGORY";
        reasonTitle = MakeText("ReasonTitle", new Vector2(0f, 1f), new Vector2(348f, 19f), 14f, Muted);
        for (int i = 0; i < reasonButtons.Length; i++)
        {
            int slot = i;
            reasonButtons[i] = MakeButton("QuickReason" + (i + 1), new Vector2(-89f + 178f * i, -23f),
                new Vector2(170f, 30f), string.Empty, () => SelectQuickReason(slot));
        }

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) continue;
            string category = ReviewAnnotationStore.NormalizeCategory(label.text);
            if (category != null)
            {
                categoryButtons.Add(button, category);
                int column = category == ReviewAnnotationStore.SizeCategory ? -1
                    : category == ReviewAnnotationStore.CrampedCategory ? 0 : 1;
                Place((RectTransform)button.transform, new Vector2(column * 118f, 36f), new Vector2(112f, 31f));
                StyleButton(button, Control, 12f);
                if (!HasPersistentListener(button, nameof(OnSelectCategory)))
                    button.onClick.AddListener(() => OnSelectCategory(category));
            }
            else if (label == submitButtonText)
            {
                submitButton = button;
                StyleButton(button, Accent, 14f);
                Place((RectTransform)button.transform, new Vector2(100f, -133f), new Vector2(148f, 32f));
                if (!HasPersistentListener(button, nameof(OnSubmit))) button.onClick.AddListener(OnSubmit);
            }
            else if (button.name == "Btn_Close")
            {
                Place((RectTransform)button.transform, new Vector2(157f, 126f), new Vector2(34f, 34f));
                StyleButton(button, Control, 14f);
            }
        }

        BuildInput();

        messageText = MakeText("AnnotationStatus", new Vector2(0f, -99f), new Vector2(348f, 34f), 13f, Warning);
        keepEditingButton = MakeButton("KeepEditing", new Vector2(-90f, -133f),
            new Vector2(168f, 32f), "Keep editing", KeepEditing);

        savedStatus = MakeText("SavedStatus", new Vector2(0f, 13f), new Vector2(348f, 42f), 27f, Success);
        savedStatus.alignment = TextAlignmentOptions.Center;
        savedStatus.fontStyle = FontStyles.Bold;
        savedStatus.text = "Note saved";
        savedHint = MakeText("SavedHint", new Vector2(0f, -39f), new Vector2(320f, 55f), 17f, Muted);
        savedHint.alignment = TextAlignmentOptions.Center;
        savedHint.text = "Find it under OBJECT NOTES\nin your review results.";
        doneButton = MakeButton("SavedDone", new Vector2(0f, -112f), new Vector2(156f, 40f), "Done", CloseUI);
        StyleButton(doneButton, Accent, 16f);

        RefreshForm();
        ClearError();
    }

    private void BuildInput()
    {
        if (descriptionInput == null)
        {
            var text = transform.Find("DescriptionDisplay").GetComponent<TextMeshProUGUI>();
            text.gameObject.name = "InputText";
            text.text = string.Empty;
            StyleLabel(text, 16f, Ink, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true;
            text.richText = false;

            Image field = ReviewVisuals.Image(transform, "DescriptionDisplay", Vector2.zero,
                new Vector2(348f, 44f), Control);
            field.raycastTarget = true;
            field.gameObject.SetActive(false);
            var viewport = new GameObject("TextViewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(field.transform, false);
            Stretch((RectTransform)viewport.transform, new Vector2(10f, 6f));
            text.transform.SetParent(viewport.transform, false);
            Stretch(text.rectTransform, Vector2.zero);

            var placeholder = MakeText("TypingPlaceholder", Vector2.zero, Vector2.zero, 15f, Muted);
            placeholder.transform.SetParent(viewport.transform, false);
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            Stretch(placeholder.rectTransform, Vector2.zero);

            descriptionInput = field.gameObject.AddComponent<TMP_InputField>();
            descriptionInput.textComponent = text;
            descriptionInput.textViewport = (RectTransform)viewport.transform;
            descriptionInput.targetGraphic = field;
            descriptionInput.placeholder = placeholder;
            descriptionInput.lineType = TMP_InputField.LineType.MultiLineNewline;
            descriptionInput.richText = false;
            descriptionInput.shouldHideSoftKeyboard = true;
            field.gameObject.SetActive(true);
        }

        descriptionInput.characterLimit = ReviewAnnotationStore.ReasonLimit;
        descriptionInput.onSelect.AddListener(_ => virtualKeyboard.Show());
        descriptionInput.onValueChanged.AddListener(_ => ClearError());
        virtualKeyboard = new IssueMarkerVirtualKeyboard(transform, descriptionInput, SetKeyboardOpen);
    }

    private bool HasPersistentListener(Button button, string method)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentTarget(i) == this
                && button.onClick.GetPersistentMethodName(i) == method)
                return true;
        return false;
    }

    private TextMeshProUGUI MakeText(string name, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(transform, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = titleText.font;
        label.richText = false;
        StyleLabel(label, fontSize, color, TextAlignmentOptions.Left);
        Place(label.rectTransform, position, size);
        return label;
    }

    private Button MakeButton(string name, Vector2 position, Vector2 size, string title, Action action)
    {
        Image image = ReviewVisuals.Image(transform, name, position, size, Control);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        var label = MakeText("Label", Vector2.zero, Vector2.zero, 14f, Ink);
        label.transform.SetParent(image.transform, false);
        Stretch(label.rectTransform, new Vector2(4f, 2f));
        label.text = title;
        StyleButton(button, Control, 14f);
        return button;
    }

    private static void StyleButton(Button button, Color fill, float fontSize)
    {
        StyleLabel(button.GetComponentInChildren<TextMeshProUGUI>(true), fontSize, Ink, TextAlignmentOptions.Center);
        var image = (Image)button.targetGraphic;
        image.sprite = null;
        image.color = fill;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.76f, .76f, .76f);
        colors.disabledColor = new Color(.65f, .65f, .65f);
        button.colors = colors;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
    }

    private static void StyleLabel(TextMeshProUGUI label, float size, Color color, TextAlignmentOptions alignment,
        FontStyles style = FontStyles.Normal)
    {
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.fontStyle = style;
        label.color = color;
        label.alignment = alignment;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2 padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = padding;
        rect.offsetMax = -padding;
    }

    public void Setup(GameObject target, HoverDwellHighlighter highlighter)
    {
        targetObject = target;
        targetHighlighter = highlighter;
        if (targetText != null) RefreshTargetLabel();
    }

    public void SetViewer(Camera camera)
    {
        viewer = camera;
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (viewer == null) return;

        Transform head = viewer.transform;
        const float desiredDistance = 1.05f;
        const float clearance = .045f;
        float distance = desiredDistance;
        Vector2 angularHalfSize = PanelSize * (.5f * ScalePerMeter);

        // Test the corners, edges and center of the whole panel, including the inline keyboard.
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector3 direction = head.TransformDirection(new Vector3(x * angularHalfSize.x, y * angularHalfSize.y, 1f));
            float length = direction.magnitude;
            int count = Physics.RaycastNonAlloc(head.position, direction / length, obstructionHits,
                desiredDistance * length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!(obstructionHits[i].collider is CharacterController))
                    distance = Mathf.Min(distance, obstructionHits[i].distance / length - clearance);
        }

        float minimum = viewer.nearClipPlane + clearance;
        distance = Mathf.Max(minimum, distance);
        while (distance > minimum && IntersectsScene(head, angularHalfSize, distance))
            distance = Mathf.Max(minimum, distance - clearance);
        transform.SetPositionAndRotation(head.position + head.forward * distance, head.rotation);
        transform.localScale = Vector3.one * (ScalePerMeter * distance);
    }

    private bool IntersectsScene(Transform head, Vector2 halfSize, float distance)
    {
        Vector3 extent = new Vector3(halfSize.x * distance, halfSize.y * distance, .012f);
        int count = Physics.OverlapBoxNonAlloc(head.position + head.forward * distance, extent,
            panelOverlaps, head.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!(panelOverlaps[i] is CharacterController)) return true;
        return false;
    }

    private void RefreshTargetLabel()
    {
        FurnitureInfo info = targetObject != null ? targetObject.GetComponent<FurnitureInfo>() : null;
        string name = info != null && !string.IsNullOrWhiteSpace(info.EnglishName)
            ? info.EnglishName : targetObject != null ? targetObject.name : "Object annotation";
        bool hasNotes = ReviewAnnotationStore.Active?.HasSavedNotes(targetObject) == true;
        targetText.text = hasNotes ? name + "  |  ANNOTATED" : name;
        targetText.color = hasNotes ? Success : Muted;
    }

    public void OnSelectCategory(string category)
    {
        if (saved || closing) return;
        EnsureInitialized();
        string next = ReviewAnnotationStore.NormalizeCategory(category);
        if (next != selectedCategory) selectedReason = null;
        selectedCategory = next;
        ClearError();
        RefreshForm();
    }

    private void SelectQuickReason(int slot)
    {
        if (!QuickReasons.TryGetValue(selectedCategory ?? string.Empty, out var reasons)) return;
        string reason = reasons[slot];
        selectedReason = selectedReason == reason ? null : reason;
        ClearError();
        RefreshForm();
    }

    private void SetKeyboardOpen(bool open)
    {
        keyboardOpen = open;
        RefreshForm();
    }

    private void RefreshForm()
    {
        bool showChoices = !saved && !keyboardOpen;
        categoryTitle.gameObject.SetActive(showChoices);
        reasonTitle.gameObject.SetActive(showChoices);
        reasonTitle.text = selectedCategory == null ? "Choose a category to see quick reasons" : "QUICK REASON (OPTIONAL)";
        foreach (var pair in categoryButtons)
        {
            pair.Key.gameObject.SetActive(showChoices);
            bool chosen = pair.Value == selectedCategory;
            pair.Key.targetGraphic.color = chosen ? Accent : Control;
            pair.Key.GetComponentInChildren<TextMeshProUGUI>(true).fontStyle = chosen ? FontStyles.Bold : FontStyles.Normal;
        }

        bool hasReasons = QuickReasons.TryGetValue(selectedCategory ?? string.Empty, out var reasons);
        for (int i = 0; i < reasonButtons.Length; i++)
        {
            var button = reasonButtons[i];
            button.gameObject.SetActive(showChoices && hasReasons);
            if (!hasReasons) continue;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            bool chosen = selectedReason == reasons[i];
            label.text = chosen ? "[ " + reasons[i] + " ]" : reasons[i];
            button.targetGraphic.color = chosen ? Accent : Control;
        }

        descriptionInput.gameObject.SetActive(!saved);
        Place((RectTransform)descriptionInput.transform, new Vector2(0f, keyboardOpen ? 36f : -64f),
            new Vector2(348f, keyboardOpen ? 52f : 44f));
        if (descriptionInput.placeholder is TextMeshProUGUI placeholder)
            placeholder.text = selectedReason == null ? "Enter a reason or choose a quick reason" : "Add a detail (optional)";

        messageText.gameObject.SetActive(showChoices && (IsFinishing || LastError != null));
        keepEditingButton.gameObject.SetActive(showChoices && IsFinishing);
        submitButton.gameObject.SetActive(showChoices);
        submitButtonText.text = IsFinishing ? "SAVE & END" : "Save note";
        savedStatus.gameObject.SetActive(saved);
        savedHint.gameObject.SetActive(saved);
        doneButton.gameObject.SetActive(saved);
    }

    public void OnSubmit() => TrySubmit();

    public bool TrySubmit()
    {
        if (submitting || closing || saved) return false;
        EnsureInitialized();
        virtualKeyboard.Hide();
        string detail = descriptionInput.text.Trim();
        string reason = selectedReason == null ? detail : detail.Length == 0 ? selectedReason
            : selectedReason + "\n" + detail;
        ReviewAnnotationStore store = ReviewAnnotationStore.Active;
        if (store == null)
        {
            ShowError("The review is not ready to save. Your draft is still here.");
            return false;
        }

        submitting = true;
        bool success = store.TrySubmit(targetObject, selectedCategory, reason, out string error);
        submitting = false;
        if (!success)
        {
            ShowError(error);
            return false;
        }

        saved = true;
        RefreshTargetLabel();
        ClearError();
        RefreshForm();
        Action finish = onFinishSubmitted;
        if (finish != null)
        {
            CloseUI();
            finish.Invoke();
        }
        return true;
    }

    public void RequestFinish(Action onSubmitted)
    {
        if (closing || onSubmitted == null) return;
        if (saved)
        {
            CloseUI();
            onSubmitted.Invoke();
            return;
        }
        EnsureInitialized();
        onFinishSubmitted = onSubmitted;
        virtualKeyboard.Hide();
        ClearError();
        RefreshForm();
    }

    public void KeepEditing()
    {
        onFinishSubmitted = null;
        ClearError();
        RefreshForm();
    }

    private void ShowError(string error)
    {
        LastError = string.IsNullOrEmpty(error) ? "Could not save. Your draft is still here." : error;
        messageText.text = LastError;
        messageText.color = Warning;
        messageText.gameObject.SetActive(true);
    }

    private void ClearError()
    {
        LastError = null;
        if (messageText == null) return;
        messageText.text = IsFinishing ? "Save this draft to finish, or keep editing." : string.Empty;
        messageText.color = Muted;
        messageText.gameObject.SetActive(IsFinishing && !keyboardOpen && !saved);
    }

    public void OnCancel()
    {
        if (keyboardOpen) virtualKeyboard.Hide();
        else if (IsFinishing) KeepEditing();
        else CloseUI();
    }

    public void CloseUI()
    {
        if (closing) return;
        closing = true;
        onFinishSubmitted = null;
        if (targetHighlighter != null) targetHighlighter.SetSelected(false);
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
    private readonly Action<bool> onVisibilityChanged;
    private readonly RectTransform keyboardRoot;
    private readonly GameObject keyboardObject;

    public IssueMarkerVirtualKeyboard(Transform canvasRoot, TMP_InputField targetInput, Action<bool> onVisibilityChanged)
    {
        input = targetInput;
        this.onVisibilityChanged = onVisibilityChanged;
        keyboardObject = new GameObject("IssueMarkerVirtualKeyboard", typeof(RectTransform), typeof(Image));
        keyboardObject.transform.SetParent(canvasRoot, false);
        keyboardRoot = keyboardObject.GetComponent<RectTransform>();
        keyboardRoot.anchorMin = new Vector2(0.5f, 0.5f);
        keyboardRoot.anchorMax = new Vector2(0.5f, 0.5f);
        keyboardRoot.pivot = new Vector2(0.5f, 0.5f);
        keyboardRoot.anchoredPosition = new Vector2(0f, -72f);
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
        if (keyboardObject.activeSelf) return;
        keyboardObject.SetActive(true);
        onVisibilityChanged(true);
    }

    public void Hide()
    {
        if (!keyboardObject.activeSelf) return;
        keyboardObject.SetActive(false);
        onVisibilityChanged(false);
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
