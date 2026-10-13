using System;
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
    private string selectedCategory = "未选择";
    private IssueMarkerVirtualKeyboard virtualKeyboard;

    private void Start()
    {
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
                descriptionInput.characterLimit = 500;
                descriptionInput.onSelect.AddListener(_ => ShowVirtualKeyboard());
                fieldTransform.gameObject.SetActive(true);
            }
        }

        if (descriptionInput != null)
            virtualKeyboard = new IssueMarkerVirtualKeyboard(transform, descriptionInput);
        else
            Debug.LogError("Issue Marker is missing a usable DescriptionDisplay input.");
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
        selectedCategory = category;
        Debug.Log("Selected issue category: " + selectedCategory);

        if (submitButtonText != null)
            submitButtonText.text = "Submit (" + selectedCategory + ")";
    }

    public void OnSubmit()
    {
        string description = descriptionInput != null ? descriptionInput.text : string.Empty;
        string targetName = targetObject != null ? targetObject.name : "Unknown";
        Debug.Log("Issue submitted. Target: " + targetName + ", category: " + selectedCategory + ", description: " + description);
        CloseUI();
    }

    public void OnCancel()
    {
        CloseUI();
    }

    public void CloseUI()
    {
        if (targetHighlighter != null)
            targetHighlighter.SetSelected(false);
        if (AnnotationSpawner.currentActiveUI == gameObject)
            AnnotationSpawner.currentActiveUI = null;
        Destroy(gameObject);
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
        keyboardRoot.anchoredPosition = new Vector2(0f, -170f);
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

    private void Hide()
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
