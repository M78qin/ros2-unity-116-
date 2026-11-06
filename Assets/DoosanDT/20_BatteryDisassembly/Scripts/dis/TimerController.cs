using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TimerController : MonoBehaviour
{
    public Button startPauseButton; // 开始/暂停按钮
    public Button resetButton;      // 重置按钮
    public Text timerText;          // 计时显示文本
    public Text statusText;         // 状态显示文本
    public Image progressCircle;    // 进度圆环
    public AudioClip buttonSound;   // 按钮音效

    private AudioSource audioSource;
    private bool isRunning = false;
    private float elapsedTime = 0f;
    private float lastStartTime = 0f;

    void Start()
    {
        // 初始化按钮事件
        startPauseButton.onClick.AddListener(ToggleTimer);
        resetButton.onClick.AddListener(ResetTimer);

        // 初始化音频源
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        // 初始状态
        UpdateTimerDisplay(0f);
        UpdateStatus("Ready");
        UpdateButtonColors();
    }

    void Update()
    {
        if (isRunning)
        {
            // 更新计时
            elapsedTime = Time.time - lastStartTime;
            UpdateTimerDisplay(elapsedTime);
            UpdateProgressCircle(elapsedTime);
        }
    }

    private void ToggleTimer()
    {
        PlayButtonSound();

        if (isRunning)
        {
            // 暂停计时
            isRunning = false;
            UpdateStatus("Paused");
        }
        else
        {
            // 开始计时
            isRunning = true;
            lastStartTime = Time.time - elapsedTime; // 考虑已经过去的时间
            UpdateStatus("Timing...");
        }

        UpdateButtonColors();
    }

    private void ResetTimer()
    {
        PlayButtonSound();

        // 重置计时
        elapsedTime = 0f;
        isRunning = false;

        UpdateTimerDisplay(0f);
        UpdateStatus("Reset");
        UpdateProgressCircle(0f);
        UpdateButtonColors();
    }

    private void UpdateTimerDisplay(float time)
    {
        // 格式化为分钟:秒.毫秒
        int minutes = (int)time / 60;
        int seconds = (int)time % 60;
        int milliseconds = (int)(time * 100) % 100;
        timerText.text = $"{minutes:00}:{seconds:00}.{milliseconds:00}";
    }

    private void UpdateStatus(string status)
    {
        statusText.text = status;
    }

    private void UpdateProgressCircle(float time)
    {
        // 更新进度圆环(每30秒一个循环)
        float progress = (time % 30) / 30f;
        progressCircle.fillAmount = progress;

        // 更新进度环颜色(基于进度)
        Color progressColor = Color.HSVToRGB(progress, 0.8f, 0.9f);
        progressColor.a = 0.85f; // 设置透明度
        progressCircle.color = progressColor;
    }

    private void UpdateButtonColors()
    {
        // 更新按钮颜色
        ColorBlock startColors = startPauseButton.colors;
        startColors.normalColor = isRunning ? Color.red : new Color(0.05f, 0.7f, 0.2f);
        startColors.highlightedColor = isRunning ? new Color(1f, 0.5f, 0.5f) : new Color(0.3f, 0.9f, 0.4f);
        startPauseButton.colors = startColors;

        // 更新按钮文本
        Text buttonText = startPauseButton.GetComponentInChildren<Text>();
        buttonText.text = isRunning ? "Stop" : "Start";
        buttonText.color = Color.white;
    }

    private void PlayButtonSound()
    {
        if (buttonSound != null)
        {
            audioSource.PlayOneShot(buttonSound);
        }
    }
}