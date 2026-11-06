using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    public enum CaptureMode
    {
        GameViewWithUI,
        CameraOnly
    }

    public enum ResolutionPreset
    {
        NativeGameView,
        HD_1280x720,
        FullHD_1920x1080,
        QHD_2560x1440,
        UHD_3840x2160,
        Custom
    }

    public enum ResizeFilter
    {
        Bilinear,
        Point
    }

    [Header("截图设置")]
    [Tooltip("截图快捷键，建议不要与交互操作按键冲突。")]
    public KeyCode screenshotKey = KeyCode.F12;
    [Tooltip("截图模式：游戏视图含 UI，或仅相机画面。")]
    public CaptureMode captureMode = CaptureMode.GameViewWithUI;
    [Tooltip("常用分辨率预设，便于快速切换。")]
    public ResolutionPreset resolutionPreset = ResolutionPreset.NativeGameView;
    [Tooltip("仅当分辨率预设为 Custom 时生效。")]
    public int screenshotWidth = 1920;
    [Tooltip("仅当分辨率预设为 Custom 时生效。")]
    public int screenshotHeight = 1080;
    [Tooltip("当目标分辨率高于当前 Game 视图时，优先尝试超采样截图，避免直接拉伸变糊。")]
    public bool avoidUpscaleForGameView = true;
    [Tooltip("缩放图片时使用的滤波方式。")]
    public ResizeFilter resizeFilter = ResizeFilter.Bilinear;
    [Range(1, 3)]
    [Tooltip("仅相机画面模式的超采样倍数。越高越清晰，但开销也越大。")]
    public int cameraSupersample = 1;
    [Tooltip("按快捷键循环切换分辨率预设：原生 -> 720p -> 1080p -> 1440p -> 4K。")]
    public bool enablePresetHotSwitch = true;
    public KeyCode cyclePresetKey = KeyCode.F10;
    [Tooltip("截图保存目录。相对路径在编辑器下相对于项目根目录，在打包后相对于程序目录。")]
    public string screenshotFolder = "Screenshots";
    [Tooltip("两次截图之间的最小间隔时间（秒）。")]
    public float captureCooldownSeconds = 0.2f;
    [Tooltip("截图完成后是否在控制台输出保存路径。")]
    public bool logSavedPath = true;

    [Header("可选移动控制")]
    [Tooltip("默认关闭，避免与 SmartRaycaster 的相机控制冲突。")]
    public bool enableMovementControl = false;
    [Tooltip("运行时切换移动控制开关的快捷键。")]
    public KeyCode movementToggleKey = KeyCode.F9;
    [Tooltip("WASD 水平移动速度。")]
    public float horizontalSpeed = 10f;
    [Tooltip("Q/E 上下移动速度。")]
    public float verticalSpeed = 8f;
    [Tooltip("按住 Shift 时的加速倍率。")]
    public float fastMoveMultiplier = 2.0f;
    [Tooltip("鼠标视角旋转灵敏度。")]
    public float mouseSensitivity = 2.5f;
    [Tooltip("镜头允许抬头的最大角度。")]
    public float maxUpAngle = 85f;
    [Tooltip("镜头允许低头的最大角度。")]
    public float maxDownAngle = -85f;
    [Tooltip("仅在按住鼠标右键时才允许旋转视角。")]
    public bool requireRightMouseForLook = true;
    [Tooltip("旋转视角时是否锁定并隐藏鼠标。")]
    public bool lockCursorWhileLooking = true;

    private Camera _targetCamera;
    private Transform _camTransform;
    private float _nextCaptureTime;
    private string _resolvedFolderPath;
    private bool _isCapturing;
    private bool _movementControlRuntimeEnabled;
    private float _xRotation;
    private static readonly MethodInfo CaptureScreenshotAsTextureMethod = typeof(ScreenCapture).GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);
    private static readonly MethodInfo CaptureScreenshotAsTextureWithSuperSizeMethod = typeof(ScreenCapture).GetMethod("CaptureScreenshotAsTexture", new[] { typeof(int) });

    private void Awake()
    {
        _targetCamera = GetComponent<Camera>();
        _camTransform = transform;
        if (_targetCamera == null)
        {
            Debug.LogError("CameraController 需要挂在带有 Camera 的同一个物体上。");
            enabled = false;
            return;
        }

        screenshotWidth = Mathf.Max(1, screenshotWidth);
        screenshotHeight = Mathf.Max(1, screenshotHeight);
        captureCooldownSeconds = Mathf.Max(0f, captureCooldownSeconds);
        cameraSupersample = Mathf.Clamp(cameraSupersample, 1, 3);
        _xRotation = NormalizeAngle(_camTransform.eulerAngles.x);
        _movementControlRuntimeEnabled = enableMovementControl;

        _resolvedFolderPath = ResolveScreenshotFolder(screenshotFolder);
        Directory.CreateDirectory(_resolvedFolderPath);
    }

    private void OnDisable()
    {
        SetCursorLock(false);
    }

    private void Update()
    {
        HandleMovementToggleHotkey();
        HandlePresetSwitchHotkey();

        if (_isCapturing)
            return;

        if (!Input.GetKeyDown(screenshotKey))
            return;

        if (Time.unscaledTime < _nextCaptureTime)
            return;

        _nextCaptureTime = Time.unscaledTime + captureCooldownSeconds;
        StartCoroutine(CaptureAtEndOfFrame());
    }

    private void FixedUpdate()
    {
        if (!_movementControlRuntimeEnabled)
            return;

        HandleMovement();
        HandleMouseRotation();
    }

    [ContextMenu("立即截图")]
    public void CaptureScreenshotNow()
    {
        if (_isCapturing)
            return;

        StartCoroutine(CaptureAtEndOfFrame());
    }

    private IEnumerator CaptureAtEndOfFrame()
    {
        _isCapturing = true;
        yield return new WaitForEndOfFrame();
        CaptureScreenshotInternal();
        _isCapturing = false;
    }

    private void CaptureScreenshotInternal()
    {
        GetTargetResolution(out int targetWidth, out int targetHeight, out bool useNativeGameSize);

        try
        {
            Texture2D tex = captureMode == CaptureMode.GameViewWithUI
                ? CaptureGameViewWithUi(targetWidth, targetHeight, useNativeGameSize)
                : CaptureFromCamera(targetWidth, targetHeight);

            if (tex == null)
                return;

            int outputWidth = tex.width;
            int outputHeight = tex.height;
            string fileName = $"Game_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{outputWidth}x{outputHeight}.png";
            string fullPath = Path.Combine(_resolvedFolderPath, fileName);
            byte[] pngData = tex.EncodeToPNG();
            File.WriteAllBytes(fullPath, pngData);

            if (logSavedPath)
                Debug.Log($"截图已保存：{fullPath}");

            Destroy(tex);
        }
        catch (Exception ex)
        {
            Debug.LogError($"截图失败：{ex.Message}");
        }
    }

    private void GetTargetResolution(out int width, out int height, out bool useNativeGameSize)
    {
        useNativeGameSize = false;

        switch (resolutionPreset)
        {
            case ResolutionPreset.NativeGameView:
                width = Screen.width;
                height = Screen.height;
                useNativeGameSize = true;
                break;
            case ResolutionPreset.HD_1280x720:
                width = 1280;
                height = 720;
                break;
            case ResolutionPreset.FullHD_1920x1080:
                width = 1920;
                height = 1080;
                break;
            case ResolutionPreset.QHD_2560x1440:
                width = 2560;
                height = 1440;
                break;
            case ResolutionPreset.UHD_3840x2160:
                width = 3840;
                height = 2160;
                break;
            default:
                width = Mathf.Max(1, screenshotWidth);
                height = Mathf.Max(1, screenshotHeight);
                break;
        }

        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
    }

    private Texture2D CaptureGameViewWithUi(int targetWidth, int targetHeight, bool useNativeGameSize)
    {
        int sourceWidth = Screen.width;
        int sourceHeight = Screen.height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            Debug.LogError("截图失败：当前屏幕尺寸无效。");
            return null;
        }

        if (useNativeGameSize)
        {
            targetWidth = sourceWidth;
            targetHeight = sourceHeight;
        }

        bool wantsHigherThanCurrentView = targetWidth > sourceWidth || targetHeight > sourceHeight;
        if (wantsHigherThanCurrentView && avoidUpscaleForGameView)
        {
            int superSize = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(
                (float)targetWidth / sourceWidth,
                (float)targetHeight / sourceHeight)));

            Texture2D supersizedCapture = CaptureGameViewTexture(superSize, out bool usedSuperSizeCapture);
            if (supersizedCapture != null)
            {
                if (!usedSuperSizeCapture && superSize > 1)
                {
                    Debug.LogWarning("当前 Unity 版本不支持 GameViewWithUI 超采样截图，已退回当前 Game 视图尺寸，无法无损达到更高分辨率。");
                    targetWidth = Mathf.Min(targetWidth, supersizedCapture.width);
                    targetHeight = Mathf.Min(targetHeight, supersizedCapture.height);
                }

                if (supersizedCapture.width == targetWidth && supersizedCapture.height == targetHeight)
                    return supersizedCapture;

                Texture2D resizedSupersized = ResizeTexture(supersizedCapture, targetWidth, targetHeight, resizeFilter);
                Destroy(supersizedCapture);
                return resizedSupersized;
            }
        }

        Texture2D frame = CaptureGameViewTexture(1, out _);
        if (frame == null)
            return null;

        if (avoidUpscaleForGameView)
        {
            targetWidth = Mathf.Min(targetWidth, frame.width);
            targetHeight = Mathf.Min(targetHeight, frame.height);
        }

        if (frame.width == targetWidth && frame.height == targetHeight)
            return frame;

        Texture2D resized = ResizeTexture(frame, targetWidth, targetHeight, resizeFilter);
        Destroy(frame);
        return resized;
    }

    private Texture2D CaptureFromCamera(int width, int height)
    {
        if (_targetCamera == null)
        {
            Debug.LogError("截图失败：未找到相机。");
            return null;
        }

        int supersample = Mathf.Clamp(cameraSupersample, 1, 3);
        int renderWidth = width * supersample;
        int renderHeight = height * supersample;

        RenderTexture previousTarget = _targetCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32, GetScreenshotReadWrite());
        rt.filterMode = resizeFilter == ResizeFilter.Point ? FilterMode.Point : FilterMode.Bilinear;

        try
        {
            _targetCamera.targetTexture = rt;
            _targetCamera.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(renderWidth, renderHeight, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
            tex.Apply(false, false);

            if (supersample == 1)
                return tex;

            Texture2D downsampled = ResizeTexture(tex, width, height, resizeFilter);
            Destroy(tex);
            return downsampled;
        }
        finally
        {
            _targetCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static Texture2D ResizeTexture(Texture2D source, int width, int height, ResizeFilter filter)
    {
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        Color32[] sourcePixels = source.GetPixels32();
        Color32[] resizedPixels = new Color32[width * height];

        if (filter == ResizeFilter.Point)
        {
            for (int y = 0; y < height; y++)
            {
                int sourceY = Mathf.Clamp(Mathf.RoundToInt(((y + 0.5f) * sourceHeight / height) - 0.5f), 0, sourceHeight - 1);
                int targetRow = y * width;
                int sourceRow = sourceY * sourceWidth;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Mathf.Clamp(Mathf.RoundToInt(((x + 0.5f) * sourceWidth / width) - 0.5f), 0, sourceWidth - 1);
                    resizedPixels[targetRow + x] = sourcePixels[sourceRow + sourceX];
                }
            }
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                float sourceY = ((y + 0.5f) * sourceHeight / height) - 0.5f;
                int yMin = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, sourceHeight - 1);
                int yMax = Mathf.Clamp(yMin + 1, 0, sourceHeight - 1);
                float yLerp = Mathf.Clamp01(sourceY - yMin);

                for (int x = 0; x < width; x++)
                {
                    float sourceX = ((x + 0.5f) * sourceWidth / width) - 0.5f;
                    int xMin = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, sourceWidth - 1);
                    int xMax = Mathf.Clamp(xMin + 1, 0, sourceWidth - 1);
                    float xLerp = Mathf.Clamp01(sourceX - xMin);

                    Color topLeft = sourcePixels[yMin * sourceWidth + xMin];
                    Color topRight = sourcePixels[yMin * sourceWidth + xMax];
                    Color bottomLeft = sourcePixels[yMax * sourceWidth + xMin];
                    Color bottomRight = sourcePixels[yMax * sourceWidth + xMax];

                    Color top = Color.LerpUnclamped(topLeft, topRight, xLerp);
                    Color bottom = Color.LerpUnclamped(bottomLeft, bottomRight, xLerp);
                    resizedPixels[y * width + x] = (Color32)Color.LerpUnclamped(top, bottom, yLerp);
                }
            }
        }

        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        resized.SetPixels32(resizedPixels);
        resized.Apply(false, false);
        return resized;
    }

    private Texture2D CaptureGameViewTexture(int superSize, out bool usedSuperSizeCapture)
    {
        usedSuperSizeCapture = false;
        superSize = Mathf.Max(1, superSize);

        try
        {
            if (superSize > 1 && CaptureScreenshotAsTextureWithSuperSizeMethod != null)
            {
                Texture2D supersized = CaptureScreenshotAsTextureWithSuperSizeMethod.Invoke(null, new object[] { superSize }) as Texture2D;
                usedSuperSizeCapture = supersized != null;
                if (supersized != null)
                    return supersized;
            }

            if (CaptureScreenshotAsTextureMethod != null)
                return CaptureScreenshotAsTextureMethod.Invoke(null, null) as Texture2D;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"调用 ScreenCapture 截图接口失败，改用 ReadPixels 方式：{ex.Message}");
        }

        int width = Screen.width;
        int height = Screen.height;
        if (width <= 0 || height <= 0)
            return null;

        Texture2D fallback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        fallback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        fallback.Apply(false, false);
        return fallback;
    }

    private static RenderTextureReadWrite GetScreenshotReadWrite()
    {
        return QualitySettings.activeColorSpace == ColorSpace.Linear
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Default;
    }

    private static string ResolveScreenshotFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            folder = "Screenshots";

        if (Path.IsPathRooted(folder))
            return folder;

        string projectOrBuildRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectOrBuildRoot, folder);
    }

    private void HandlePresetSwitchHotkey()
    {
        if (!enablePresetHotSwitch || !Input.GetKeyDown(cyclePresetKey))
            return;

        resolutionPreset = resolutionPreset switch
        {
            ResolutionPreset.NativeGameView => ResolutionPreset.HD_1280x720,
            ResolutionPreset.HD_1280x720 => ResolutionPreset.FullHD_1920x1080,
            ResolutionPreset.FullHD_1920x1080 => ResolutionPreset.QHD_2560x1440,
            ResolutionPreset.QHD_2560x1440 => ResolutionPreset.UHD_3840x2160,
            ResolutionPreset.UHD_3840x2160 => ResolutionPreset.NativeGameView,
            ResolutionPreset.Custom => ResolutionPreset.NativeGameView,
            _ => ResolutionPreset.NativeGameView
        };

        GetTargetResolution(out int w, out int h, out _);
        Debug.Log($"截图分辨率预设已切换为：{resolutionPreset}（{w}x{h}）");
    }

    private void HandleMovementToggleHotkey()
    {
        if (!Input.GetKeyDown(movementToggleKey))
            return;

        _movementControlRuntimeEnabled = !_movementControlRuntimeEnabled;
        if (!_movementControlRuntimeEnabled)
            SetCursorLock(false);

        Debug.Log($"相机移动控制：{(_movementControlRuntimeEnabled ? "开启" : "关闭")}");
    }

    private void HandleMovement()
    {
        Vector3 forwardFlat = Vector3.ProjectOnPlane(_camTransform.forward, Vector3.up).normalized;
        Vector3 rightFlat = Vector3.ProjectOnPlane(_camTransform.right, Vector3.up).normalized;

        Vector3 horizontalMove = Vector3.zero;
        float verticalMove = 0f;

        if (Input.GetKey(KeyCode.W)) horizontalMove += forwardFlat;
        if (Input.GetKey(KeyCode.S)) horizontalMove -= forwardFlat;
        if (Input.GetKey(KeyCode.A)) horizontalMove -= rightFlat;
        if (Input.GetKey(KeyCode.D)) horizontalMove += rightFlat;
        if (Input.GetKey(KeyCode.Q)) verticalMove -= 1f;
        if (Input.GetKey(KeyCode.E)) verticalMove += 1f;

        if (horizontalMove.sqrMagnitude > 1f)
            horizontalMove.Normalize();

        float speedMultiplier = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            ? fastMoveMultiplier
            : 1f;

        _camTransform.position += horizontalMove * (horizontalSpeed * speedMultiplier * Time.fixedDeltaTime);
        _camTransform.position += Vector3.up * (verticalMove * verticalSpeed * speedMultiplier * Time.fixedDeltaTime);
    }

    private void HandleMouseRotation()
    {
        bool rightMouseHeld = Input.GetMouseButton(1);
        if (requireRightMouseForLook && !rightMouseHeld)
        {
            SetCursorLock(false);
            return;
        }

        SetCursorLock(lockCursorWhileLooking);

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        float yaw = _camTransform.eulerAngles.y + mouseX;
        _xRotation = Mathf.Clamp(_xRotation - mouseY, maxDownAngle, maxUpAngle);
        _camTransform.rotation = Quaternion.Euler(_xRotation, yaw, 0f);
    }

    private static float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }

    private static void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
