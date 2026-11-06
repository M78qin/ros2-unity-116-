using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoController : MonoBehaviour
{
    // 组件绑定
    public VideoPlayer videoPlayer;     // 视频播放器组件
    public RawImage rawImage;           // 视频显示载体
    public Button playPauseBtn;         // 播放/暂停按钮
    public Button fastForwardBtn;       // 快进按钮
    public Button rewindBtn;            // 后退按钮

    [Header("快进/后退设置")]
    [Tooltip("可调节的快进/后退时间（秒）")]
    public float skipTime = 5f;         // 可设置的快进时间

    private RenderTexture renderTexture;

    void Start()
    {
        // 验证组件
        if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
        if (rawImage == null) rawImage = GetComponent<RawImage>();

        // 创建渲染纹理
        renderTexture = new RenderTexture(1024, 1024, 24);
        videoPlayer.targetTexture = renderTexture;
        rawImage.texture = renderTexture;

        // 初始设置
        videoPlayer.playOnAwake = false;
        videoPlayer.Prepare();

        // 按钮事件绑定
        playPauseBtn.onClick.AddListener(TogglePlayPause);
        fastForwardBtn.onClick.AddListener(FastForward);
        rewindBtn.onClick.AddListener(Rewind);
    }

    // 播放/暂停切换
    public void TogglePlayPause()
    {
        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            playPauseBtn.GetComponentInChildren<Text>().text = "播放";
        }
        else
        {
            videoPlayer.Play();
            playPauseBtn.GetComponentInChildren<Text>().text = "暂停";
        }
    }

    // 快进功能
    public void FastForward()
    {
        double newTime = videoPlayer.time + skipTime;
        videoPlayer.time = Mathf.Min((float)newTime, (float)videoPlayer.length);
    }

    // 后退功能
    public void Rewind()
    {
        double newTime = videoPlayer.time - skipTime;
        videoPlayer.time = Mathf.Max((float)newTime, 0);
    }

    void OnDestroy()
    {
        // 释放渲染纹理资源
        if (renderTexture != null && renderTexture.IsCreated())
        {
            renderTexture.Release();
        }
    }
}