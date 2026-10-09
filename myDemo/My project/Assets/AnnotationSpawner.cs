//using UnityEngine;
//using UnityEngine.XR.Interaction.Toolkit;

//public class AnnotationSpawner : MonoBehaviour
//{
//    [Header("UI 预制体")]
//    public GameObject annotationUIPrefab; // 在 Inspector 里把 Canvas 预制体拖进来

//    private HoverDwellHighlighter highlighter;

//    void Start()
//    {
//        // 自动获取同物体上的高光脚本
//        highlighter = GetComponent<HoverDwellHighlighter>();
//    }

//    // 绑定到 XRSimpleInteractable 的 Select Entered 事件（左右手扳机键默认触发此事件）
//    public void OnSelectEntered(SelectEnterEventArgs args)
//    {
//        // 如果场景里已经有一个 UI 弹窗了，就不重复弹出
//        if (FindObjectOfType<AnnotationUI>() != null) return;

//        Debug.Log(gameObject.name + " 被扳机键选中，生成批注界面");

//        // 锁定高亮（变为青色）
//        if (highlighter != null) highlighter.SetSelected(true);

//        SpawnAnnotationUI();
//    }

//    private void SpawnAnnotationUI()
//    {
//        if (annotationUIPrefab == null) return;

//        // 1. 获取玩家摄像机
//        Transform cameraTransform = Camera.main.transform;

//        // 2. 生成在摄像机前方 1.5 米处
//        Vector3 spawnPos = cameraTransform.position + cameraTransform.forward * 1.5f;

//        // 生成时，让 UI 的旋转与摄像机保持一致（或者只保留Y轴朝向）
//        Quaternion spawnRot = Quaternion.LookRotation(spawnPos - cameraTransform.position);
//        GameObject ui = Instantiate(annotationUIPrefab, spawnPos, spawnRot);

//        // 3. 传给 UI 脚本
//        AnnotationUI uiScript = ui.GetComponent<AnnotationUI>();
//        if (uiScript != null)
//        {
//            uiScript.Setup(gameObject, highlighter);
//        }
//    }
//}
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class AnnotationSpawner : MonoBehaviour
{
    [Header("UI 预制体")]
    public GameObject annotationUIPrefab;

    // 静态变量，记录场景中当前唯一的UI
    public static GameObject currentActiveUI = null;

    private HoverDwellHighlighter highlighter;

    void Start()
    {
        highlighter = GetComponent<HoverDwellHighlighter>();
    }

    public void OnSelectEntered(SelectEnterEventArgs args)
    {
        // 如果当前已经有UI打开了，直接返回，什么都不做（防止重复生成）
        if (currentActiveUI != null) return;

        // 锁定高亮（变为青色）
        if (highlighter != null) highlighter.SetSelected(true);

        // 生成UI
        SpawnAnnotationUI();
    }

    private void SpawnAnnotationUI()
    {
        if (annotationUIPrefab == null) return;

        Transform cam = Camera.main.transform;
        float targetDistance = 1.0f;

        // 默认生成位置：相机前方 1.0 米
        Vector3 spawnPos = cam.position + cam.forward * targetDistance;

        // 🛠️ 射线避让机制：如果前方有物体，就把UI生成在那个物体前面
        RaycastHit hit;
        if (Physics.Raycast(cam.position, cam.forward, out hit, targetDistance))
        {
            // 生成在障碍物前方 0.2 米处，避免穿模
            spawnPos = hit.point - cam.forward * 0.2f;
        }

        GameObject ui = Instantiate(annotationUIPrefab, spawnPos, Quaternion.identity);
        ui.transform.LookAt(cam);
        ui.transform.Rotate(0, 180, 0); // 修正UI正反面

        currentActiveUI = ui; // 记录当前UI

        AnnotationUI uiScript = ui.GetComponent<AnnotationUI>();
        if (uiScript != null)
        {
            uiScript.Setup(gameObject, highlighter);
        }
    }
}