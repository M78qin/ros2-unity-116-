// TwinUiMissionModel.cs —— 拆解实训页的本地步骤模型（P8）
// 定位：把"步骤引导 / 训练评估"两份 UI 共用的**会话级状态**收在一处，避免两张卡片各记一套。
// 诚实边界：步骤文本来自既有 TwinUITextR2（原本就是演示文案），评估是本地自评（完成度×100），
//           不是评分系统、也不代表真实拆解考核结果；卡片上必须写明。
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiMissionModel
    {
        private static TwinUiMissionModel _instance;

        private bool[] _done;
        private int _current;
        private float _startTime;

        public static TwinUiMissionModel Instance => _instance ?? (_instance = new TwinUiMissionModel());

        private TwinUiMissionModel()
        {
            _done = new bool[Mathf.Max(1, TwinUITextR2.Steps.Length)];
            _startTime = Time.unscaledTime;
        }

        public int StepCount => _done.Length;

        public int Current => _current;

        /// <summary>已完成步骤数。</summary>
        public int DoneCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _done.Length; i++)
                {
                    if (_done[i])
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public bool Done(int index)
        {
            return index >= 0 && index < _done.Length && _done[index];
        }

        public float Progress01 => _done.Length <= 0 ? 0f : (float)DoneCount / _done.Length;

        public float ElapsedSeconds => Mathf.Max(0f, Time.unscaledTime - _startTime);

        public string ElapsedLabel
        {
            get
            {
                float s = ElapsedSeconds;
                return ((int)(s / 60f)).ToString("00") + ":" + ((int)(s % 60f)).ToString("00");
            }
        }

        public void Select(int index)
        {
            _current = Mathf.Clamp(index, 0, _done.Length - 1);
        }

        public void Next()
        {
            if (_current < _done.Length - 1)
            {
                _done[_current] = true;
                _current++;
            }
        }

        public void Prev()
        {
            if (_current > 0)
            {
                _current--;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _done.Length; i++)
            {
                _done[i] = false;
            }

            _current = 0;
            _startTime = Time.unscaledTime;
        }
    }
}