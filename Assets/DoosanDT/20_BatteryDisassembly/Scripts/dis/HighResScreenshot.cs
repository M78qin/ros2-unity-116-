using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using TMPro;

public class HighResScreenshot : MonoBehaviour
{
    [Header("摄像机设置")]
    public Camera targetCamera;
    public bool useMainCamera = true;

    [Header("分辨率设置")]
    public TMP_InputField widthInput;
    public TMP_InputField heightInput;
    public Toggle transparentBgToggle;
    public Slider resolutionSlider;

    [Header("UI元素")]
    public TextMeshProUGUI statusText;
    public GameObject screenshotPanel;
    public Button captureButton;
    public Button openFolderButton;

    [Header("预设分辨率")]
    private Vector2Int[] presetResolutions = {
        new Vector2Int(1920, 1080),  // 1080p
        new Vector2Int(2560, 1440),  // 2K
        new Vector2Int(3840, 2160),  // 4K
        new Vector2Int(7680, 4320)   // 8K
    };

    private string screenshotFolder;

    void Start()
    {
        // 初始化UI
        resolutionSlider.onValueChanged.AddListener(OnResolutionSliderChanged);
        captureButton.onClick.AddListener(CaptureScreenshot);
        openFolderButton.onClick.AddListener(OpenScreenshotFolder);

        // 设置默认值
        widthInput.text = "3840";
        heightInput.text = "2160";
        resolutionSlider.value = 2; // 默认为4K

        // 创建截图文件夹
        screenshotFolder = Path.Combine(Application.dataPath, "../Screenshots/");
        Directory.CreateDirectory(screenshotFolder);

        // 初始状态
        statusText.text = "就绪";
        screenshotPanel.SetActive(false);
    }

    void OnResolutionSliderChanged(float value)
    {
        int index = Mathf.Clamp((int)value, 0, presetResolutions.Length - 1);
        Vector2Int res = presetResolutions[index];
        widthInput.text = res.x.ToString();
        heightInput.text = res.y.ToString();
    }

    public void CaptureScreenshot()
    {
        StartCoroutine(CaptureScreenshotCoroutine());
    }

    IEnumerator CaptureScreenshotCoroutine()
    {
        // 获取设置
        int width = int.Parse(widthInput.text);
        int height = int.Parse(heightInput.text);

        // 验证分辨率
        if (width <= 0 || height <= 0)
        {
            statusText.text = "错误: 分辨率无效!";
            yield break;
        }

        if (width > 16384 || height > 16384)
        {
            statusText.text = "警告: 超高分辨率可能造成性能问题!";
            yield return new WaitForSeconds(1);
        }

        // 获取摄像机
        Camera renderCamera = useMainCamera ? Camera.main : targetCamera;
        if (renderCamera == null)
        {
            statusText.text = "错误: 未找到摄像机!";
            yield break;
        }

        // 禁用UI避免被截入
        screenshotPanel.SetActive(true);
        statusText.text = "渲染中...";
        yield return null; // 等待一帧确保UI更新

        // 创建RenderTexture
        RenderTexture rt = new RenderTexture(width, height, 24);
        rt.antiAliasing = 8; // 8倍抗锯齿
        RenderTexture prevTarget = renderCamera.targetTexture;
        renderCamera.targetTexture = rt;

        // 处理透明背景
        CameraClearFlags prevClearFlags = renderCamera.clearFlags;
        Color prevBackground = renderCamera.backgroundColor;

        if (transparentBgToggle.isOn)
        {
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(0, 0, 0, 0); // 透明背景
        }

        // 渲染
        renderCamera.Render();

        // 读取像素
        RenderTexture.active = rt;
        Texture2D screenshot = new Texture2D(width, height,
            transparentBgToggle.isOn ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();

        // 恢复相机设置
        renderCamera.targetTexture = prevTarget;
        renderCamera.clearFlags = prevClearFlags;
        renderCamera.backgroundColor = prevBackground;
        RenderTexture.active = null;

        // 保存文件
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string filename = $"Screenshot_{width}x{height}_{timestamp}.png";
        string fullPath = Path.Combine(screenshotFolder, filename);

        byte[] bytes = screenshot.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);

        // 清理资源
        Destroy(rt);
        Destroy(screenshot);

        // 显示完成状态
        statusText.text = $"截图已保存!\n{filename}";
        screenshotPanel.SetActive(false);

        Debug.Log($"高分辨率截图保存至: {fullPath}");
    }

    void OpenScreenshotFolder()
    {
        Application.OpenURL("file://" + screenshotFolder);
    }
}