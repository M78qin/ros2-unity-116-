using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 主面板 A：实训任务台。任务向导（步骤列表 + 当前步骤说明 + 提示 + 时间轴/视频入口）
    /// 与本地评估（计时 / 完成度 / 错误次数 / 自评得分）。
    /// 明确：评估是本地自评，没有接评分系统，界面上也这么写。
    /// </summary>
    public sealed class TwinUIMissionView : MonoBehaviour
    {
        [SerializeField] private int current;
        [SerializeField] private float refreshHz = 2f;

        private readonly bool[] _done = new bool[TwinUITextR2.Steps.Length];
        private TMP_Text[] _stepTitles;
        private TMP_Text[] _stepStates;
        private Image[] _stepBars;
        private TMP_Text _detailTitle;
        private TMP_Text _detailBody;
        private TMP_Text _hint;
        private TMP_Text _timer;
        private TMP_Text _progress;
        private TMP_Text _errors;
        private TMP_Text _score;
        private float _startTime;
        private bool _resolved;
        private float _next;
        private CanvasGroup _timelineGroup;

        private void Awake()
        {
            Resolve();
            _startTime = Time.time;
            _next = 0f;
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            int n = TwinUITextR2.Steps.Length;
            _stepTitles = new TMP_Text[n];
            _stepStates = new TMP_Text[n];
            _stepBars = new Image[n];
            for (int i = 0; i < n; i++)
            {
                _stepTitles[i] = T("Steps/Step" + i + "/Title");
                _stepStates[i] = T("Steps/Step" + i + "/State");
                _stepBars[i] = I("Steps/Step" + i + "/Bar");
            }

            _detailTitle = T("Detail/Title");
            _detailBody = T("Detail/Body");
            _hint = T("Detail/Hint/Text");
            _timer = T("Eval/Timer/Value");
            _progress = T("Eval/Progress/Value");
            _errors = T("Eval/Errors/Value");
            _score = T("Eval/Score/Value");
            _timelineGroup = GetComponentInChildren<CanvasGroup>(true);

            Wire("Detail/Actions/BtnPrev", Prev);
            Wire("Detail/Actions/BtnNext", Next);
            Wire("Detail/Actions/BtnReset", ResetAll);
            Wire("Eval/BtnRoaming", BackToRoaming);
        }

        private TMP_Text T(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TMP_Text>();
        }

        private Image I(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<Image>();
        }

        private void Wire(string path, UnityEngine.Events.UnityAction action)
        {
            var t = transform.Find(path);
            if (t == null)
            {
                return;
            }

            var b = t.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(action);
            }
        }

        public void Select(int index)
        {
            current = Mathf.Clamp(index, 0, TwinUITextR2.Steps.Length - 1);
            Refresh();
        }

        public void Next()
        {
            _done[current] = true;
            if (current < TwinUITextR2.Steps.Length - 1)
            {
                current++;
            }

            Refresh();
        }

        public void Prev()
        {
            if (current > 0)
            {
                current--;
            }

            Refresh();
        }

        public void ResetAll()
        {
            for (int i = 0; i < _done.Length; i++)
            {
                _done[i] = false;
            }

            current = 0;
            _startTime = Time.time;
            Refresh();
        }

        public void BackToRoaming()
        {
            var router = GetComponentInParent<TwinDemoModeRouter>();
            if (router != null)
            {
                router.EnterMode(TwinDemoMode.Roaming);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _next)
            {
                return;
            }

            _next = Time.unscaledTime + 1f / Mathf.Max(0.5f, refreshHz);
            Refresh();
        }

        public void Refresh()
        {
            if (_stepTitles == null || _stepTitles.Length == 0 || _stepTitles[0] == null)
            {
                _resolved = false;
            }

            Resolve();
            int doneCount = 0;
            for (int i = 0; i < _done.Length; i++)
            {
                if (_done[i])
                {
                    doneCount++;
                }

                bool isCurrent = i == current;
                if (_stepTitles != null && _stepTitles[i] != null)
                {
                    _stepTitles[i].color = isCurrent ? TwinUIStyle.TextColor : TwinUIStyle.MutedColor;
                }

                if (_stepStates != null && _stepStates[i] != null)
                {
                    _stepStates[i].text = _done[i] ? TwinUITextR2.StepDone
                        : isCurrent ? TwinUITextR2.StepCurrent : TwinUITextR2.StepWaiting;
                    _stepStates[i].color = _done[i] ? TwinUIStyle.OkColor
                        : isCurrent ? TwinUIStyle.AccentColor : TwinUIStyle.MutedColor;
                }

                if (_stepBars != null && _stepBars[i] != null)
                {
                    _stepBars[i].color = _done[i] ? TwinUIStyle.OkColor
                        : isCurrent ? TwinUIStyle.AccentColor : TwinUIStyle.BorderColor;
                }
            }

            if (_detailTitle != null)
            {
                _detailTitle.text = "步骤 " + (current + 1) + " / " + TwinUITextR2.Steps.Length;
            }

            if (_detailBody != null)
            {
                _detailBody.text = TwinUITextR2.StepBodies[current];
            }

            if (_hint != null)
            {
                _hint.text = TwinUITextR2.HintPrefix + TwinUITextR2.StepHints[current];
            }

            float seconds = Mathf.Max(0f, Time.time - _startTime);
            if (_timer != null)
            {
                _timer.text = ((int)(seconds / 60f)).ToString("00") + ":" + ((int)(seconds % 60f)).ToString("00");
            }

            float ratio = TwinUITextR2.Steps.Length <= 0 ? 0f : (float)doneCount / TwinUITextR2.Steps.Length;
            if (_progress != null)
            {
                _progress.text = Mathf.RoundToInt(ratio * 100f) + " %";
            }

            if (_errors != null)
            {
                _errors.text = "0";
            }

            if (_score != null)
            {
                _score.text = Mathf.RoundToInt(ratio * 100f).ToString();
            }
        }
    }
}