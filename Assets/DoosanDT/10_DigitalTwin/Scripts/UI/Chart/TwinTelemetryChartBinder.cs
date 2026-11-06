// Feeds TwinTelemetryChart from the live runtime frame (read-only) or from the replay probe.
//
// Data-source discipline (this is the part a thesis reviewer will ask about):
//   * LIVE  - samples come from DigitalTwinRuntime.TryGetLatestFrame(); nothing is written back.
//   * REPLAY- samples come from a recorded unity_receive.csv through TwinTelemetryReplayProbe and
//             the panel must say so, because replayed curves are NOT new measurements.
//   * NONE  - no frame for `staleSeconds`: the chart is emptied and the label says "无数据源".
// A missing force/torque subscription is reported as "未订阅" instead of drawing zeros, so a flat
// line can never be mistaken for measured data.
using System;
using TMPro;
using UnityEngine;

namespace DigitalTwin
{
    public sealed class TwinTelemetryChartBinder : MonoBehaviour
    {
        public enum ChartGroup
        {
            JointAngle = 0,
            Wrench = 1,
            JointTorque = 2,
        }

        [Header("Sources")]
        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private TwinTelemetryReplayProbe replayProbe;

        [Header("Target")]
        [SerializeField] private TwinTelemetryChart chart;
        [SerializeField] private ChartGroup group = ChartGroup.JointAngle;
        [SerializeField] private bool fallbackToJointTorque = true;
        [SerializeField] private int jointSeriesCount = 3;

        [Header("Sampling")]
        [SerializeField] private float sampleHz = 20f;
        [SerializeField] private float staleSeconds = 1.5f;

        [Header("Labels")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text rangeText;
        [SerializeField] private string title = "实时曲线";

        private float _accumulator;
        private float _lastSampleTime = -999f;
        private string _lastStatus = string.Empty;
        private ChartGroup _activeGroup = (ChartGroup)(-1);
        private int _activeSeriesCount = -1;

        public ChartGroup Group => group;
        public TwinTelemetryChart Chart => chart;

        private void Awake()
        {
            if (titleText != null && !string.IsNullOrEmpty(title))
            {
                titleText.text = title;
            }

            ApplyGroup(group, jointSeriesCount);
            RefreshLabels(false, ChartLabel.NoData);
        }

        private enum ChartLabel
        {
            NoData = 0,
            NotSubscribed = 1,
            Live = 2,
            Replay = 3,
        }

        private void Update()
        {
            if (chart == null)
            {
                return;
            }

            _accumulator += Time.unscaledDeltaTime;
            float interval = 1f / Mathf.Max(1f, sampleHz);
            if (_accumulator < interval)
            {
                return;
            }

            _accumulator = 0f;

            float now = Time.unscaledTime;
            bool gotFrame = false;
            bool usedReplay = false;

            bool missingSubscription = false;
            if (replayProbe != null && replayProbe.IsPlaying)
            {
                gotFrame = PushReplay(now, out missingSubscription);
                usedReplay = gotFrame;
            }
            else if (runtime != null && runtime.TryGetLatestFrame(out RobotStateFrame frame) && frame != null)
            {
                gotFrame = PushFrame(frame, now, out missingSubscription);
            }

            if (!gotFrame && missingSubscription)
            {
                chart.SetHasData(false);
                RefreshLabels(false, ChartLabel.NotSubscribed);
                return;
            }

            if (gotFrame)
            {
                _lastSampleTime = now;
            }

            bool fresh = gotFrame || (now - _lastSampleTime) <= Mathf.Max(0.1f, staleSeconds);
            if (!fresh)
            {
                chart.SetHasData(false);
                RefreshLabels(true, ChartLabel.NoData);
                return;
            }

            RefreshLabels(true, usedReplay ? ChartLabel.Replay : ChartLabel.Live);
        }

        private bool PushFrame(RobotStateFrame frame, float now, out bool missingSubscription)
        {
            missingSubscription = false;
            if (frame == null)
            {
                return false;
            }

            bool hasJoint = frame.JointPositionRad != null && frame.JointPositionRad.Length >= 6;
            bool hasForce = (frame.Flags & RobotFrameFlags.HasForce) != 0 && frame.ForceVector != null && frame.ForceVector.Length >= 3;
            bool hasTorque = (frame.Flags & RobotFrameFlags.HasJointTorque) != 0 && frame.JointTorqueNm != null && frame.JointTorqueNm.Length >= 6;

            ChartGroup wanted = ResolveGroup(hasForce, hasTorque);
            ApplyGroup(wanted, jointSeriesCount);

            switch (wanted)
            {
                case ChartGroup.Wrench:
                    if (!hasForce)
                    {
                        // subscription off / topic silent: report it, never draw zeros
                        missingSubscription = true;
                        return false;
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.ForceX + i), now, frame.ForceVector[i]);
                    }

                    return true;

                case ChartGroup.JointTorque:
                    if (!hasTorque)
                    {
                        missingSubscription = true;
                        return false;
                    }

                    for (int i = 0; i < Mathf.Min(3, frame.JointTorqueNm.Length); i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.JointTorque1 + i), now, frame.JointTorqueNm[i]);
                    }

                    return true;

                default:
                    if (!hasJoint)
                    {
                        return false;
                    }

                    for (int i = 0; i < Mathf.Min(jointSeriesCount, frame.JointPositionRad.Length); i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i), now, frame.JointPositionRad[i] * Mathf.Rad2Deg);
                    }

                    return true;
            }
        }

        private bool PushReplay(float now, out bool missingSubscription)
        {
            missingSubscription = false;
            TwinTelemetryReplayProbe.SampleRow row = replayProbe.CurrentRow;
            if (row == null)
            {
                return false;
            }

            bool hasForce = row.Wrench != null;
            bool hasTorque = row.Torque != null;
            ChartGroup wanted = ResolveGroup(hasForce, hasTorque);
            ApplyGroup(wanted, jointSeriesCount);

            switch (wanted)
            {
                case ChartGroup.Wrench:
                    if (!hasForce)
                    {
                        missingSubscription = true;
                        return false;
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.ForceX + i), now, row.Wrench[i]);
                    }

                    return true;

                case ChartGroup.JointTorque:
                    if (!hasTorque)
                    {
                        return false;
                    }

                    for (int i = 0; i < Mathf.Min(3, row.Torque.Length); i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.JointTorque1 + i), now, row.Torque[i]);
                    }

                    return true;

                default:
                    if (row.JointDeg == null)
                    {
                        return false;
                    }

                    for (int i = 0; i < Mathf.Min(jointSeriesCount, row.JointDeg.Length); i++)
                    {
                        chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i), now, row.JointDeg[i]);
                    }

                    return true;
            }
        }

        private ChartGroup ResolveGroup(bool hasForce, bool hasTorque)
        {
            if (group == ChartGroup.Wrench && !hasForce)
            {
                return fallbackToJointTorque && hasTorque ? ChartGroup.JointTorque : ChartGroup.Wrench;
            }

            if (group == ChartGroup.JointTorque && !hasTorque)
            {
                return ChartGroup.JointTorque;
            }

            return group;
        }

        private void ApplyGroup(ChartGroup wanted, int seriesCount)
        {
            if (chart == null)
            {
                return;
            }

            if (_activeGroup == wanted && _activeSeriesCount == seriesCount)
            {
                return;
            }

            _activeGroup = wanted;
            _activeSeriesCount = seriesCount;
            chart.ClearAll();

            switch (wanted)
            {
                case ChartGroup.Wrench:
                    chart.SetSeries(TwinTelemetryCatalog.DefaultWrenchSeries);
                    break;
                case ChartGroup.JointTorque:
                    chart.SetSeries(TwinTelemetryCatalog.DefaultTorqueSeries);
                    break;
                default:
                    {
                        var jointSeries = new TwinTelemetryChannel[Mathf.Clamp(seriesCount, 1, 6)];
                        for (int i = 0; i < jointSeries.Length; i++)
                        {
                            jointSeries[i] = (TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i);
                        }

                        chart.SetSeries(jointSeries);
                        break;
                    }
            }
        }

        private void RefreshLabels(bool hasData, ChartLabel label)
        {
            string status;
            switch (label)
            {
                case ChartLabel.Replay:
                    status = TwinUIShellText.ChartReplay;
                    break;
                case ChartLabel.Live:
                    status = TwinUIShellText.ChartLive;
                    break;
                case ChartLabel.NotSubscribed:
                    status = TwinUIShellText.ChartNoForce;
                    break;
                default:
                    status = TwinUIShellText.ChartNoData;
                    break;
            }

            if (statusText != null && status != _lastStatus)
            {
                _lastStatus = status;
                statusText.text = status;
            }

            if (rangeText != null)
            {
                if (!hasData || chart == null || !chart.HasData)
                {
                    rangeText.text = "--";
                }
                else
                {
                    string unit = _activeGroup == ChartGroup.JointAngle ? "deg" : (_activeGroup == ChartGroup.Wrench ? "N / N.m" : "N.m");
                    rangeText.text = string.Format("{0:0.###} .. {1:0.###} {2}", chart.RangeMin, chart.RangeMax, unit);
                }
            }
        }

        // ------------------------------------------------------------ editor wiring

        public void Configure(
            DigitalTwinRuntime runtimeSource,
            TwinTelemetryReplayProbe probe,
            TwinTelemetryChart target,
            ChartGroup chartGroup,
            TMP_Text titleLabel,
            TMP_Text statusLabel,
            TMP_Text rangeLabel,
            string chartTitle)
        {
            runtime = runtimeSource;
            replayProbe = probe;
            chart = target;
            group = chartGroup;
            titleText = titleLabel;
            statusText = statusLabel;
            rangeText = rangeLabel;
            title = chartTitle;
            _activeGroup = (ChartGroup)(-1);
            _activeSeriesCount = -1;
        }

        public void SetReplayProbe(TwinTelemetryReplayProbe probe)
        {
            replayProbe = probe;
        }
    }
}