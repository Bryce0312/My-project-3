using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections;
// using QuickOutline; // 如果你发现下面代码报错找不到 Outline，请把这行的注释去掉

public class HoverDwellHighlighter : MonoBehaviour
{
    public float dwellTime = 1.5f;
    private Coroutine dwellCoroutine;
    private Outline outline;

    void Start()
    {
        // 获取物体上的 Outline 组件
        outline = GetComponent<Outline>();
        if (outline != null) outline.enabled = false; // 确保一开始是关闭的
    }

    public void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (outline != null && outline.enabled) return; // 如果已经高亮了，就不重新计时
        dwellCoroutine = StartCoroutine(DwellTimer());
    }

    public void OnHoverExited(HoverExitEventArgs args)
    {
        // 停止计时
        if (dwellCoroutine != null)
        {
            StopCoroutine(dwellCoroutine);
            dwellCoroutine = null;
        }
        // 取消高亮，恢复原状
        if (outline != null) outline.enabled = false;
        Debug.Log(gameObject.name + " 射线离开，取消高亮");
    }

    private IEnumerator DwellTimer()
    {
        yield return new WaitForSeconds(dwellTime);
        if (outline != null) outline.enabled = true; // 1.5秒后开启描边
        Debug.Log(gameObject.name + " 停留1.5s，触发高亮！");
    }

    // 供 A 键选中后调用
    public void SetSelected(bool selected)
    {
        if (outline != null)
        {
            outline.enabled = selected;
            outline.OutlineColor = selected ? Color.cyan : Color.green; // 选中时变成青色
        }
    }
}