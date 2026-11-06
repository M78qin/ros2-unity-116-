using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using TMPro;
using System.Collections.Generic;

public class ExplosionControllerXR : MonoBehaviour
{
    [Header("爆炸设置")]
    public List<ExplodablePartXR> partsToExplode = new List<ExplodablePartXR>(); // 按爆炸顺序排列
    [Range(0.5f, 5f)] public float explosionSpeed = 2f; // 爆炸动画速度
    public TextMeshProUGUI messageText; // 提示信息UI
    public Color correctColor = Color.green; // 正确提示颜色
    public Color errorColor = Color.red; // 错误提示颜色
    public float messageDuration = 2f; // 提示信息显示时间

    private int currentStep = 0; // 当前步骤
    private bool isMessageShowing = false;

    private void Start()
    {
        // 初始化所有零件
        for (int i = 0; i < partsToExplode.Count; i++)
        {
            partsToExplode[i].SetController(this);
            partsToExplode[i].SetOrder(i);
            partsToExplode[i].SetInitialPosition();
        }

        // 初始化提示文本
        if (messageText != null)
        {
            messageText.text = "";
        }

        // 确保XR交互管理器存在
        if (FindObjectOfType<XRInteractionManager>() == null)
        {
            Debug.LogError("请添加XR Interaction Manager到场景中");
        }
    }

    // 尝试爆炸零件
    public void TryExplodePart(ExplodablePartXR part)
    {
        if (part.Order == currentStep)
        {
            // 正确的顺序
            ShowMessage($"爆炸零件 {currentStep + 1}/{partsToExplode.Count}", correctColor);
            part.Explode(OnExplosionComplete);
        }
        else
        {
            // 错误的顺序
            ShowMessage($"顺序错误! 请先爆炸第 {currentStep + 1} 个零件", errorColor);
        }
    }

    // 爆炸完成回调
    private void OnExplosionComplete()
    {
        currentStep++;
        if (currentStep >= partsToExplode.Count)
        {
            ShowMessage("所有零件已爆炸完成!", correctColor);
        }
    }

    // 显示提示信息
    private void ShowMessage(string message, Color color)
    {
        if (messageText == null) return;

        messageText.text = message;
        messageText.color = color;
        isMessageShowing = true;

        CancelInvoke(nameof(HideMessage));
        Invoke(nameof(HideMessage), messageDuration);

        // 添加闪烁效果（错误时）
        if (color == errorColor)
        {
            StartCoroutine(BlinkMessage());
        }
    }

    // 隐藏提示信息
    private void HideMessage()
    {
        if (messageText != null)
        {
            messageText.text = "";
        }
        isMessageShowing = false;
    }

    // 提示信息闪烁效果
    private System.Collections.IEnumerator BlinkMessage()
    {
        float elapsed = 0;
        while (elapsed < messageDuration && isMessageShowing)
        {
            messageText.enabled = !messageText.enabled;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        messageText.enabled = true;
    }

    // 重置爆炸状态
    public void ResetExplosion()
    {
        currentStep = 0;
        foreach (var part in partsToExplode)
        {
            part.ResetPosition();
        }
        ShowMessage("已重置所有零件", correctColor);
    }
}
