using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Review
{
    /// <summary>A single, paged result card. Its owner decides when and where it is shown.</summary>
    public sealed class ReviewSummaryPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class PointRow
        {
            public string id;
            public string title;
            public string question;
            public string answer;
            public int noteCount;
        }

        public enum Tab { Points, Notes }

        private sealed class NoteGroup
        {
            public string key, title;
            public readonly List<ReviewAnnotationRecord> notes = new List<ReviewAnnotationRecord>();
        }

        private sealed class DetailPage
        {
            public string title, meta, body, footer;
        }

        private const int RowsPerPage = 3;
        private const float BaseScale = .00115f;
        private readonly List<PointRow> points = new List<PointRow>();
        private readonly List<ReviewAnnotationRecord> notes = new List<ReviewAnnotationRecord>();
        private readonly List<NoteGroup> groups = new List<NoteGroup>();
        private readonly List<DetailPage> detailPages = new List<DetailPage>();
        private readonly Button[] rows = new Button[RowsPerPage];
        private readonly Text[] rowTitles = new Text[RowsPerPage];
        private readonly Text[] rowBodies = new Text[RowsPerPage];
        private Canvas canvas;
        private Transform listRoot, detailRoot;
        private Text header, statusText, saveText, emptyText, pageText;
        private Text detailTitle, detailMeta, detailBody, detailFooter;
        private Button pointsTab, notesTab, previous, next, backToList, stand, replay, resume;
        private Action onClose, onResume, onReplay, onStand;
        private int currentPage, detailPage;
        private bool detailOpen;

        public Canvas Canvas => canvas;
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;
        public Tab ActiveTab { get; private set; }
        public int CurrentPage => detailOpen ? detailPage : currentPage;
        public int PageCount => detailOpen ? Mathf.Max(1, detailPages.Count) : ListPageCount;
        public bool IsDetailOpen => detailOpen;
        public int PointCount => points.Count;
        public int NoteCount => notes.Count;
        public int ObjectCount => groups.Count;

        private int ListCount => ActiveTab == Tab.Points ? points.Count : groups.Count;
        private int ListPageCount => Mathf.Max(1, (ListCount + RowsPerPage - 1) / RowsPerPage);

        public void Build() => EnsureBuilt();

        public void Configure(Action close, Action resume, Action replay, Action standUp)
        {
            onClose = close;
            onResume = resume;
            onReplay = replay;
            onStand = standUp;
            EnsureBuilt();
        }

        public void Show(IReadOnlyList<PointRow> pointRows, IReadOnlyList<ReviewAnnotationRecord> annotationRows,
            string status, string saveMessage, bool seated)
        {
            EnsureBuilt();
            points.Clear();
            notes.Clear();
            if (pointRows != null)
                for (int i = 0; i < pointRows.Count; i++)
                {
                    var row = pointRows[i];
                    if (row == null) continue;
                    points.Add(new PointRow
                    {
                        id = row.id, title = row.title, question = row.question,
                        answer = row.answer, noteCount = row.noteCount
                    });
                }
            if (annotationRows != null)
                for (int i = 0; i < annotationRows.Count; i++)
                    if (annotationRows[i] != null) notes.Add(annotationRows[i]);
            GroupNotes();
            int rated = 0;
            for (int i = 0; i < points.Count; i++)
                if (!IsUnrated(points[i].answer)) rated++;
            header.text = "REVIEW RESULTS";
            statusText.text = rated + "/" + points.Count + " points rated  |  " + notes.Count + " object notes"
                + (string.IsNullOrWhiteSpace(status) ? string.Empty : "  |  " + status);
            saveText.text = saveMessage ?? string.Empty;
            detailOpen = false;
            detailPages.Clear();
            currentPage = Mathf.Clamp(currentPage, 0, ListPageCount - 1);
            canvas.gameObject.SetActive(true);
            SetSeated(seated, false);
            Render();
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        public void ResetView()
        {
            ActiveTab = Tab.Points; currentPage = 0; detailOpen = false;
            detailPages.Clear();
            if (canvas != null) Render();
        }

        public void SetSeated(bool seated, bool transitioning)
        {
            if (stand == null) return;
            stand.gameObject.SetActive(seated || transitioning);
            stand.interactable = seated && !transitioning;
            replay.interactable = !seated && !transitioning;
            resume.interactable = !transitioning;
            stand.GetComponentInChildren<Text>().text = transitioning ? "PLEASE WAIT" : "STAND UP";
        }

        public void SetPose(Camera viewer, Vector3 position, float scale)
        {
            if (canvas == null || viewer == null) return;
            canvas.worldCamera = viewer;
            canvas.transform.position = position;
            canvas.transform.localScale = Vector3.one * (BaseScale * Mathf.Max(.05f, scale));
            Vector3 forward = position - viewer.transform.position;
            if (forward.sqrMagnitude > .001f)
                canvas.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void EnsureBuilt()
        {
            if (canvas != null) return;
            canvas = ReviewVisuals.Canvas(transform, "ReviewSummaryCard", new Vector2(900, 650), BaseScale);
            var root = canvas.transform;
            ReviewVisuals.Image(root, "Summary_Background", Vector2.zero, new Vector2(900, 650), ReviewVisuals.Back);
            header = Text(root, "Summary_Header", new Vector2(-45, 281), new Vector2(760, 43), "REVIEW RESULTS", 31);
            Button(root, "Summary_Close", new Vector2(398, 281), new Vector2(57, 57), "X", () =>
            {
                Hide();
                onClose?.Invoke();
            });
            statusText = Text(root, "Summary_Status", new Vector2(0, 239), new Vector2(850, 39), string.Empty, 20);
            pointsTab = Button(root, "Summary_TabPoints", new Vector2(-216, 190), new Vector2(416, 52),
                "POINT REVIEWS", () => SelectTab(Tab.Points));
            notesTab = Button(root, "Summary_TabNotes", new Vector2(216, 190), new Vector2(416, 52),
                "OBJECT NOTES", () => SelectTab(Tab.Notes));
            listRoot = Container(root, "Summary_List");
            detailRoot = Container(root, "Summary_Detail");
            for (int i = 0; i < RowsPerPage; i++)
            {
                int slot = i;
                rows[i] = Button(listRoot, "Summary_Row_" + i, new Vector2(0, 110 - i * 94),
                    new Vector2(850, 84), string.Empty, () => OpenRow(slot));
                rows[i].GetComponentInChildren<Text>().gameObject.SetActive(false);
                rowTitles[i] = Text(rows[i].transform, "Title", new Vector2(-34, 20), new Vector2(750, 32), string.Empty, 24);
                rowBodies[i] = Text(rows[i].transform, "Description", new Vector2(-34, -17), new Vector2(750, 44), string.Empty, 18);
                Text(rows[i].transform, "Details", new Vector2(390, 0), new Vector2(36, 52), ">", 29, TextAnchor.MiddleCenter);
            }
            emptyText = Text(listRoot, "Summary_Empty", new Vector2(0, 14), new Vector2(800, 125),
                string.Empty, 25, TextAnchor.MiddleCenter);
            detailTitle = Text(detailRoot, "Summary_DetailTitle", new Vector2(-83, 131), new Vector2(680, 37), string.Empty, 25);
            backToList = Button(detailRoot, "Summary_BackToList", new Vector2(337, 131), new Vector2(174, 44),
                "BACK TO LIST", CloseDetails);
            detailMeta = Text(detailRoot, "Summary_DetailMeta", new Vector2(0, 87), new Vector2(840, 49), string.Empty, 20);
            detailBody = Text(detailRoot, "Summary_DetailReason", new Vector2(0, -39), new Vector2(824, 196), string.Empty, 24,
                TextAnchor.UpperLeft);
            detailFooter = Text(detailRoot, "Summary_DetailFooter", new Vector2(0, -151), new Vector2(840, 28), string.Empty, 18);
            previous = Button(root, "Summary_Previous", new Vector2(-323, -198), new Vector2(202, 48),
                "PREVIOUS", () => ChangePage(-1));
            next = Button(root, "Summary_Next", new Vector2(323, -198), new Vector2(202, 48),
                "NEXT", () => ChangePage(1));
            pageText = Text(root, "Summary_Page", new Vector2(0, -198), new Vector2(400, 43), string.Empty, 21,
                TextAnchor.MiddleCenter);
            saveText = Text(root, "Summary_SaveStatus", new Vector2(0, -241), new Vector2(850, 29), string.Empty, 18,
                TextAnchor.MiddleCenter);
            resume = Button(root, "Summary_Resume", new Vector2(-281, -286), new Vector2(286, 52),
                "RETURN TO REVIEW", () => onResume?.Invoke());
            replay = Button(root, "Summary_Replay", new Vector2(26, -286), new Vector2(286, 52),
                "NEW REVIEW", () => onReplay?.Invoke());
            stand = Button(root, "Summary_Stand", new Vector2(310, -286), new Vector2(228, 52),
                "STAND UP", () => onStand?.Invoke());
            canvas.gameObject.SetActive(false);
        }

        private void GroupNotes()
        {
            groups.Clear();
            var byObject = new Dictionary<string, NoteGroup>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                string key = string.IsNullOrWhiteSpace(note.targetId) ? "name:" + note.targetName : note.targetId;
                if (!byObject.TryGetValue(key, out var group))
                {
                    group = new NoteGroup { key = key, title = ObjectTitle(note) };
                    byObject.Add(key, group);
                    groups.Add(group);
                }
                group.notes.Add(note);
            }
        }

        private void SelectTab(Tab tab)
        {
            if (ActiveTab != tab) currentPage = 0;
            ActiveTab = tab;
            detailOpen = false;
            detailPages.Clear();
            Render();
        }

        private void Render()
        {
            listRoot.gameObject.SetActive(!detailOpen);
            detailRoot.gameObject.SetActive(detailOpen);
            MarkTab(pointsTab, ActiveTab == Tab.Points, "POINT REVIEWS (" + points.Count + ")");
            MarkTab(notesTab, ActiveTab == Tab.Notes, "OBJECT NOTES (" + notes.Count + ")");
            if (detailOpen)
            {
                detailPage = Mathf.Clamp(detailPage, 0, Mathf.Max(0, detailPages.Count - 1));
                if (detailPages.Count > 0)
                {
                    var item = detailPages[detailPage];
                    detailTitle.text = item.title;
                    detailMeta.text = item.meta;
                    detailBody.text = item.body;
                    detailFooter.text = item.footer;
                }
                pageText.text = "DETAIL " + (detailPage + 1) + " / " + PageCount;
            }
            else
            {
                currentPage = Mathf.Clamp(currentPage, 0, ListPageCount - 1);
                emptyText.gameObject.SetActive(ListCount == 0);
                emptyText.text = ActiveTab == Tab.Notes
                    ? "No object notes yet.\nReturn to review, point at an object and press Grip to add a note."
                    : "No review points were included in this session.";
                for (int slot = 0; slot < RowsPerPage; slot++)
                {
                    int index = currentPage * RowsPerPage + slot;
                    rows[slot].gameObject.SetActive(index < ListCount);
                    if (index >= ListCount) continue;
                    if (ActiveTab == Tab.Points)
                    {
                        var point = points[index];
                        rowTitles[slot].text = string.IsNullOrWhiteSpace(point.title) ? point.id : point.title;
                        rowBodies[slot].text = (IsUnrated(point.answer) ? "UNRATED" : point.answer)
                            + "  |  " + point.noteCount + " linked " + (point.noteCount == 1 ? "note" : "notes");
                    }
                    else
                    {
                        var group = groups[index];
                        rowTitles[slot].text = group.title + "  (" + group.notes.Count + ")";
                        rowBodies[slot].text = GroupTags(group) + "  |  Select to read reasons";
                    }
                }
                pageText.text = (ActiveTab == Tab.Points ? "POINTS " : "OBJECTS ")
                    + (currentPage + 1) + " / " + ListPageCount;
            }
            previous.interactable = CurrentPage > 0;
            next.interactable = CurrentPage < PageCount - 1;
        }

        private void OpenRow(int slot)
        {
            int index = currentPage * RowsPerPage + slot;
            if (index < 0 || index >= ListCount) return;
            detailPages.Clear();
            if (ActiveTab == Tab.Points)
            {
                var point = points[index];
                string body = "QUESTION\n" + (point.question ?? string.Empty) + "\n\nEVALUATION\n"
                    + (IsUnrated(point.answer) ? "Not rated yet. Return to review to add your evaluation." : point.answer);
                AddDetailPages(point.title, point.noteCount + " linked object notes", body,
                    "Review point: " + point.id);
            }
            else
            {
                var group = groups[index];
                for (int i = 0; i < group.notes.Count; i++)
                {
                    var note = group.notes[i];
                    string pointTitle = PointTitle(note.pointId);
                    string meta = "Note " + (i + 1) + " / " + group.notes.Count + "  |  ["
                        + Category(note.category) + "]\n" + pointTitle;
                    AddDetailPages(group.title, meta, string.IsNullOrWhiteSpace(note.reason)
                        ? "No reason was entered." : note.reason,
                        string.IsNullOrWhiteSpace(note.utc) ? "Object: " + group.key : "Saved: " + note.utc);
                }
            }
            detailOpen = true;
            detailPage = 0;
            Render();
        }

        private void AddDetailPages(string title, string meta, string body, string footer)
        {
            var pieces = Paginate(body ?? string.Empty);
            for (int i = 0; i < pieces.Count; i++)
                detailPages.Add(new DetailPage
                {
                    title = title ?? string.Empty,
                    meta = meta + (pieces.Count > 1 ? "  |  Reason part " + (i + 1) + "/" + pieces.Count : string.Empty),
                    body = pieces[i], footer = footer
                });
        }

        // Measure against the actual font and width, so long input and explicit newlines remain readable.
        private List<string> Paginate(string body)
        {
            var result = new List<string>();
            if (body.Length == 0) { result.Add(string.Empty); return result; }
            var generator = new TextGenerator();
            var settings = detailBody.GetGenerationSettings(detailBody.rectTransform.rect.size);
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            float height = detailBody.rectTransform.rect.height - 8f;
            int offset = 0;
            while (offset < body.Length)
            {
                int remaining = body.Length - offset;
                int low = 1, high = remaining, fit = 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    float preferred = generator.GetPreferredHeight(body.Substring(offset, middle), settings)
                        / Mathf.Max(.01f, detailBody.pixelsPerUnit);
                    if (preferred <= height) { fit = middle; low = middle + 1; }
                    else high = middle - 1;
                }
                if (fit < remaining)
                {
                    int candidate = offset + fit;
                    if (candidate > offset && char.IsHighSurrogate(body[candidate - 1])) fit--;
                    int earliestBreak = offset + Mathf.Max(1, fit * 2 / 3);
                    for (int j = offset + fit - 1; j >= earliestBreak; j--)
                        if (char.IsWhiteSpace(body[j])) { fit = j - offset + 1; break; }
                }
                fit = Mathf.Max(1, fit);
                result.Add(body.Substring(offset, fit));
                offset += fit;
            }
            ((IDisposable)generator).Dispose();
            return result;
        }

        private string PointTitle(string pointId)
        {
            if (string.IsNullOrWhiteSpace(pointId)) return "Added during free exploration";
            for (int i = 0; i < points.Count; i++)
                if (points[i].id == pointId) return "Review point: " + points[i].title;
            return "Review point: " + pointId;
        }

        private void CloseDetails()
        {
            detailOpen = false;
            detailPages.Clear();
            Render();
        }

        private void ChangePage(int delta)
        {
            if (detailOpen) detailPage = Mathf.Clamp(detailPage + delta, 0, PageCount - 1);
            else currentPage = Mathf.Clamp(currentPage + delta, 0, ListPageCount - 1);
            Render();
        }

        private static string GroupTags(NoteGroup group)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var text = new StringBuilder();
            for (int i = 0; i < group.notes.Count; i++)
            {
                string tag = Category(group.notes[i].category);
                if (!distinct.Add(tag)) continue;
                if (text.Length > 0) text.Append("  ");
                text.Append('[').Append(tag).Append(']');
            }
            return text.ToString();
        }

        private static string ObjectTitle(ReviewAnnotationRecord note)
        {
            string name = string.IsNullOrWhiteSpace(note.targetName) ? "Unnamed object" : note.targetName;
            return string.IsNullOrWhiteSpace(note.targetId) || note.targetId.Length > 16 ? name : note.targetId + " · " + name;
        }

        private static string Category(string value) => string.IsNullOrWhiteSpace(value) ? "Uncategorized" : value;

        private static bool IsUnrated(string answer) => string.IsNullOrWhiteSpace(answer)
            || string.Equals(answer, "UNRATED", StringComparison.OrdinalIgnoreCase);

        private static void MarkTab(Button button, bool selected, string label)
        {
            button.GetComponentInChildren<Text>().text = (selected ? "[ " : string.Empty) + label
                + (selected ? " ]" : string.Empty);
            button.targetGraphic.color = selected ? new Color(.34f, .36f, .38f, 1f)
                : new Color(.23f, .25f, .27f, 1f);
        }

        private static Transform Container(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 650);
            return go.transform;
        }

        private static Text Text(Transform parent, string name, Vector2 position, Vector2 size,
            string value, int fontSize, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var text = ReviewVisuals.Text(parent, name, position, size, value, fontSize, anchor);
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            return text;
        }

        private static Button Button(Transform parent, string name, Vector2 position, Vector2 size,
            string title, UnityEngine.Events.UnityAction action)
        {
            var button = ReviewVisuals.Button(parent, name, position, size, title, action);
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = 20;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            return button;
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }
}
