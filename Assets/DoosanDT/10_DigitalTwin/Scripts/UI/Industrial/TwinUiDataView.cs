// 采集数据页：论文级"最小数据采集集"的展示面。
//
// 每项统一给 当前 / 目标 / 误差 三列 + 状态列；状态列同时给颜色、ASCII 标记与文字，
// 不只靠颜色区分（色盲/投影仪友好）。能力缺失显示"未订阅"，有通道但无帧显示"无数据"。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiDataView : MonoBehaviour
    {
        private const int LinkRows = 9;
        private const int JointRows = 6;
        private const int WrenchRows = 6;
        private const int PoseRows = 6;

        private TMP_Text[][] _link = new TMP_Text[LinkRows][];
        private Image[] _linkBars = new Image[LinkRows];

        private TMP_Text[][] _joint = new TMP_Text[JointRows][];
        private Image[] _jointBars = new Image[JointRows];

        private TMP_Text[][] _wrench = new TMP_Text[WrenchRows][];
        private Image[] _wrenchBars = new Image[WrenchRows];
        private TMP_Text _wrenchNote;

        private TMP_Text[][] _torque = new TMP_Text[JointRows][];
        private Image[] _torqueBars = new Image[JointRows];
        private TMP_Text _torqueNote;

        private TMP_Text[][] _pose = new TMP_Text[PoseRows][];
        private Image[] _poseBars = new Image[PoseRows];

        private TwinTelemetryChart _chart;
        private TMP_Text _chartNote;
        private TMP_Text _linkNote;

        private bool _resolved;
        private float _chartTime;

        public void Render(TwinUiPipeline pipeline, TwinUiSettings settings)
        {
            Resolve();
            if (pipeline == null)
            {
                return;
            }

            var s = pipeline.Snapshot ?? TwinUiSnapshot.Empty(TwinUiSourceKind.None, string.Empty);
            var t = pipeline.Thresholds;
            bool hasFrame = s.Link.HasFrame;

            RenderLink(pipeline, s, t, settings, hasFrame);
            RenderJoints(pipeline, s, t, hasFrame);
            RenderTorque(pipeline, s, t, hasFrame);
            RenderWrench(pipeline, s, t, hasFrame);
            RenderPose(pipeline, s, t, hasFrame);
            RenderChart(pipeline, s, t, hasFrame);
        }

        private void RenderLink(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t,
            TwinUiSettings settings, bool hasFrame)
        {
            var link = s.Link;
            double uiHz = settings != null ? settings.uiRefreshHz : 0d;

            SetLink(0, TwinUITextR3.RowSampleHz,
                TwinUiFormat.Hz(link.SampleHz, hasFrame) + TwinUITextR3.UnitHz,
                ">= " + t.sampleHzWarn.ToString("F0") + TwinUITextR3.UnitHz,
                SignedErr(link.SampleHz - t.sampleHzWarn, " Hz", 1), pipeline.ToneOf(TwinUiPipeline.KeySample), hasFrame);
            SetLink(1, TwinUITextR3.RowApplyHz,
                TwinUiFormat.Hz(link.ApplyHz, hasFrame) + TwinUITextR3.UnitHz,
                ">= " + t.applyHzWarn.ToString("F0") + TwinUITextR3.UnitHz,
                SignedErr(link.ApplyHz - t.applyHzWarn, " Hz", 1), pipeline.ToneOf(TwinUiPipeline.KeyApply), hasFrame);
            SetLink(2, TwinUITextR3.RowUiHz,
                uiHz.ToString("F0") + TwinUITextR3.UnitHz,
                "5/10/20/30/60", TwinUiFormat.Dash, TwinUITone.Ok, hasFrame);
            SetLink(3, TwinUITextR3.RowFresh,
                TwinUiFormat.Ms(link.FreshnessMs, 0, hasFrame) + TwinUITextR3.UnitMs,
                "<= " + t.freshnessWarnMs.ToString("F0") + TwinUITextR3.UnitMs,
                SignedErr(link.FreshnessMs - t.freshnessWarnMs, " ms", 0), pipeline.ToneOf(TwinUiPipeline.KeyFresh), hasFrame);
            SetLink(4, TwinUITextR3.RowLatency,
                TwinUiFormat.Ms(link.ReceiveToApplyMs, 1, hasFrame) + TwinUITextR3.UnitMs,
                "<= " + t.receiveToApplyWarnMs.ToString("F0") + TwinUITextR3.UnitMs,
                SignedErr(link.ReceiveToApplyMs - t.receiveToApplyWarnMs, " ms", 1),
                pipeline.ToneOf(TwinUiPipeline.KeyLatency), hasFrame);
            SetLink(5, TwinUITextR3.RowJitter,
                TwinUiFormat.Ms(link.JitterP99Ms, 2, hasFrame) + TwinUITextR3.UnitMs,
                "<= " + t.jitterP99WarnMs.ToString("F1") + TwinUITextR3.UnitMs,
                SignedErr(link.JitterP99Ms - t.jitterP99WarnMs, " ms", 2),
                pipeline.ToneOf(TwinUiPipeline.KeyJitter), hasFrame);
            SetLink(6, TwinUITextR3.RowDropped,
                TwinUiFormat.Count(link.DroppedFrames), "0",
                link.DroppedFrames.ToString(), pipeline.ToneOf(TwinUiPipeline.KeyDrop), hasFrame);
            SetLink(7, TwinUITextR3.RowClock,
                string.IsNullOrEmpty(link.ClockSync) ? TwinUiFormat.Dash : link.ClockSync,
                "Synced", TwinUiFormat.Dash, pipeline.ToneOf(TwinUiPipeline.KeyClock), hasFrame);
            SetLink(8, TwinUITextR3.RowQuality,
                string.IsNullOrEmpty(link.Quality) ? TwinUiFormat.Dash : link.Quality,
                "Normal", TwinUiFormat.Dash, pipeline.ToneOf(TwinUiPipeline.KeyQuality), hasFrame);

            if (_linkNote != null)
            {
                _linkNote.text = hasFrame
                    ? "窗口 " + (settings != null ? settings.replayWindowSeconds.ToString("F1") : "2.0")
                      + " s；阈值待校准；源=" + s.SourceLabel
                    : TwinUITextR3.NoData + "（等待数据源）";
                _linkNote.color = TwinUIStyle.MutedColor;
            }
        }

        private void SetLink(int i, string label, string cur, string tgt, string err, TwinUITone tone, bool hasFrame)
        {
            var row = _link[i];
            if (row == null)
            {
                return;
            }

            SetText(row[0], label, TwinUIStyle.MutedColor);
            SetText(row[1], cur, TwinUIStyle.TextColor);
            SetText(row[2], tgt, TwinUIStyle.MutedColor);
            SetText(row[3], err, ToneOrMuted(tone));
            SetText(row[4], TwinUiFormat.StateFor(tone, true, hasFrame), TwinUiFormat.ToneTextColor(tone));
            SetBar(_linkBars[i], tone, hasFrame);
        }

        private void RenderJoints(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t, bool hasFrame)
        {
            bool hasPos = TwinUiCapability.Has(s.Capabilities, TwinUiCapability.JointPosition)
                && s.Joints.PositionDeg != null && s.Joints.PositionDeg.Length > 0;
            bool hasTarget = s.Joints.HasTarget && s.Joints.TargetDeg != null && s.Joints.TargetDeg.Length > 0;
            bool hasErr = s.Joints.ErrorDeg != null && s.Joints.ErrorDeg.Length > 0;

            for (int i = 0; i < JointRows; i++)
            {
                var row = _joint[i];
                if (row == null)
                {
                    continue;
                }

                string label = i < TwinUITextR3.JointLabels.Length ? TwinUITextR3.JointLabels[i] : "J" + (i + 1);
                SetText(row[0], label, TwinUIStyle.MutedColor);

                if (!hasPos)
                {
                    SetText(row[1], TwinUiFormat.Missing(false, hasFrame), TwinUIStyle.MutedColor);
                    SetText(row[2], TwinUiFormat.Missing(false, hasFrame), TwinUIStyle.MutedColor);
                    SetText(row[3], TwinUiFormat.Dash, TwinUIStyle.MutedColor);
                    SetText(row[4], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_jointBars[i], TwinUITone.Unknown, false);
                    continue;
                }

                float cur = i < s.Joints.PositionDeg.Length ? s.Joints.PositionDeg[i] : 0f;
                SetText(row[1], TwinUiFormat.Deg(cur, 2) + TwinUITextR3.UnitDeg, TwinUIStyle.TextColor);

                if (!hasTarget || i >= s.Joints.TargetDeg.Length)
                {
                    SetText(row[2], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                }
                else
                {
                    SetText(row[2], TwinUiFormat.Deg(s.Joints.TargetDeg[i], 2) + TwinUITextR3.UnitDeg, TwinUIStyle.MutedColor);
                }

                if (!hasErr || i >= s.Joints.ErrorDeg.Length)
                {
                    SetText(row[3], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[4], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_jointBars[i], TwinUITone.Unknown, hasFrame);
                    continue;
                }

                float err = s.Joints.ErrorDeg[i];
                TwinUITone tone = t.PositionError(Mathf.Abs(err));
                SetText(row[3], TwinUiFormat.Signed(err, " °", 2), TwinUiFormat.ToneTextColor(tone));
                SetText(row[4], TwinUiFormat.StateText(tone), TwinUiFormat.ToneTextColor(tone));
                SetBar(_jointBars[i], tone, hasFrame);
            }
        }

        private void RenderTorque(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t, bool hasFrame)
        {
            bool hasVel = TwinUiCapability.Has(s.Capabilities, TwinUiCapability.JointVelocity)
                && s.Joints.VelocityDegPerSec != null && s.Joints.VelocityDegPerSec.Length > 0;
            bool hasTorque = TwinUiCapability.Has(s.Capabilities, TwinUiCapability.JointTorque)
                && s.Joints.TorqueNm != null && s.Joints.TorqueNm.Length > 0;

            for (int i = 0; i < JointRows; i++)
            {
                var row = _torque[i];
                if (row == null)
                {
                    continue;
                }

                SetText(row[0], i < TwinUITextR3.JointLabels.Length ? TwinUITextR3.JointLabels[i] : "J" + (i + 1),
                    TwinUIStyle.MutedColor);

                if (!hasVel)
                {
                    SetText(row[1], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                }
                else
                {
                    float v = i < s.Joints.VelocityDegPerSec.Length ? s.Joints.VelocityDegPerSec[i] : 0f;
                    SetText(row[1], TwinUiFormat.Deg(v, 2) + TwinUITextR3.UnitDegPerSec, TwinUIStyle.TextColor);
                }

                if (!hasTorque)
                {
                    SetText(row[2], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[3], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_torqueBars[i], TwinUITone.Unknown, false);
                    continue;
                }

                float torque = i < s.Joints.TorqueNm.Length ? s.Joints.TorqueNm[i] : 0f;
                double ratio = t.torqueRatedNm > 0.001d ? Mathf.Abs(torque) / t.torqueRatedNm : 0d;
                TwinUITone tone = t.TorqueRatio(ratio);
                SetText(row[2], TwinUiFormat.Deg(torque, 2) + TwinUITextR3.UnitNm, TwinUiStyleTorqueText(tone));
                SetText(row[3], TwinUiFormat.Ratio(ratio) + " " + TwinUiFormat.StateText(tone), TwinUiFormat.ToneTextColor(tone));
                SetBar(_torqueBars[i], tone, hasFrame);
            }

            if (_torqueNote != null)
            {
                _torqueNote.text = hasTorque
                    ? "额定 " + t.torqueRatedNm.ToString("F0") + " N·m；黄>"
                      + (t.torqueWarnRatio * 100d).ToString("F0") + "% / 红>"
                      + (t.torqueCritRatio * 100d).ToString("F0") + "%（待校准）"
                    : "力矩通道未订阅：帧里没有 JointTorqueNm（不显示 0）";
                _torqueNote.color = TwinUIStyle.MutedColor;
            }
        }

        private Color TwinUiStyleTorqueText(TwinUITone tone)
        {
            return tone == TwinUITone.Ok ? TwinUIStyle.TextColor : TwinUiFormat.ToneTextColor(tone);
        }

        private void RenderWrench(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t, bool hasFrame)
        {
            bool has = s.Force.HasForce && s.Force.Force != null && s.Force.Force.Length > 0;
            for (int i = 0; i < WrenchRows; i++)
            {
                var row = _wrench[i];
                if (row == null)
                {
                    continue;
                }

                string label = i < TwinUITextR3.WrenchLabels.Length ? TwinUITextR3.WrenchLabels[i] : "F" + (i + 1);
                SetText(row[0], label, TwinUIStyle.MutedColor);

                if (!has)
                {
                    SetText(row[1], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[2], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[3], TwinUiFormat.Dash, TwinUIStyle.MutedColor);
                    SetText(row[4], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_wrenchBars[i], TwinUITone.Unknown, false);
                    continue;
                }

                float v = i < s.Force.Force.Length ? s.Force.Force[i] : 0f;
                TwinUITone tone = t.Force(Mathf.Abs(v));
                SetText(row[1], TwinUiFormat.Deg(v, 2) + (i < 3 ? TwinUITextR3.UnitN : TwinUITextR3.UnitNm),
                    tone == TwinUITone.Ok ? TwinUIStyle.TextColor : TwinUiFormat.ToneTextColor(tone));
                string target = s.Force.Target != null && i < s.Force.Target.Length
                    ? TwinUiFormat.Deg(s.Force.Target[i], 2) : "0.00";
                SetText(row[2], target + (i < 3 ? TwinUITextR3.UnitN : TwinUITextR3.UnitNm), TwinUIStyle.MutedColor);
                SetText(row[3], TwinUiFormat.Signed(v, null, 2), ToneOrMuted(tone));
                SetText(row[4], TwinUiFormat.StateText(tone), TwinUiFormat.ToneTextColor(tone));
                SetBar(_wrenchBars[i], tone, hasFrame);
            }

            if (_wrenchNote != null)
            {
                _wrenchNote.text = has
                    ? "力阈值 " + t.forceWarnN.ToString("F0") + "/" + t.forceCritN.ToString("F0") + " N（待校准）"
                    : "力通道未订阅：帧里没有 ForceVector（不显示 0）";
                _wrenchNote.color = TwinUIStyle.MutedColor;
            }
        }

        private void RenderPose(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t, bool hasFrame)
        {
            bool has = s.Pose.HasPose && s.Pose.PositionMeters != null && s.Pose.PositionMeters.Length >= 3;
            for (int i = 0; i < PoseRows; i++)
            {
                var row = _pose[i];
                if (row == null)
                {
                    continue;
                }

                SetText(row[0], i < TwinUITextR3.PoseLabels.Length ? TwinUITextR3.PoseLabels[i] : "P" + i,
                    TwinUIStyle.MutedColor);

                if (!has)
                {
                    SetText(row[1], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[2], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[3], TwinUiFormat.Dash, TwinUIStyle.MutedColor);
                    SetText(row[4], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_poseBars[i], TwinUITone.Unknown, false);
                    continue;
                }

                bool linear = i < 3;
                float cur = linear
                    ? (i < s.Pose.PositionMeters.Length ? s.Pose.PositionMeters[i] : 0f)
                    : (s.Pose.RpyDeg != null && i - 3 < s.Pose.RpyDeg.Length ? s.Pose.RpyDeg[i - 3] : 0f);
                float target = linear
                    ? (s.Pose.HasTarget && s.Pose.TargetMeters != null && i < s.Pose.TargetMeters.Length
                        ? s.Pose.TargetMeters[i] : 0f)
                    : (s.Pose.HasTarget && s.Pose.TargetRpyDeg != null && i - 3 < s.Pose.TargetRpyDeg.Length
                        ? s.Pose.TargetRpyDeg[i - 3] : 0f);
                float err = linear
                    ? (s.Pose.HasTarget && s.Pose.ErrorMeters != null && i < s.Pose.ErrorMeters.Length
                        ? s.Pose.ErrorMeters[i] : 0f)
                    : (s.Pose.HasTarget && s.Pose.ErrorRpyDeg != null && i - 3 < s.Pose.ErrorRpyDeg.Length
                        ? s.Pose.ErrorRpyDeg[i - 3] : 0f);

                string unit = linear ? TwinUITextR3.UnitMeter : TwinUITextR3.UnitDeg;
                int digits = linear ? 4 : 2;
                SetText(row[1], TwinUiFormat.Deg(cur, digits) + unit, TwinUIStyle.TextColor);
                if (!s.Pose.HasTarget)
                {
                    SetText(row[2], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[3], TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetText(row[4], "? " + TwinUITextR3.NotSubscribed, TwinUIStyle.MutedColor);
                    SetBar(_poseBars[i], TwinUITone.Unknown, hasFrame);
                }
                else
                {
                    SetText(row[2], TwinUiFormat.Deg(target, digits) + unit, TwinUIStyle.MutedColor);
                    SetText(row[3], TwinUiFormat.Signed(err, unit, digits), TwinUIStyle.TextColor);
                    SetText(row[4], TwinUiFormat.StateText(TwinUITone.Ok), TwinUiFormat.ToneTextColor(TwinUITone.Ok));
                    SetBar(_poseBars[i], TwinUITone.Ok, hasFrame);
                }
            }
        }

        private void RenderChart(TwinUiPipeline pipeline, TwinUiSnapshot s, TwinUiThresholds t, bool hasFrame)
        {
            if (_chart == null)
            {
                return;
            }

            var settings = pipeline.Settings;
            float hz = settings != null ? Mathf.Max(1f, settings.uiRefreshHz) : 10f;
            _chartTime += 1f / hz;

            bool hasErr = s.Joints.ErrorDeg != null && s.Joints.ErrorDeg.Length > 0;
            if (hasErr)
            {
                for (int i = 0; i < 6 && i < s.Joints.ErrorDeg.Length; i++)
                {
                    _chart.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i),
                        _chartTime, s.Joints.ErrorDeg[i]);
                }

                _chart.SetHasData(true);
            }

            _chart.SetTargetLine(true, 0f);
            _chart.SetThresholdBand(true, (float)t.positionErrorWarnDeg, (float)t.positionErrorCritDeg, true, 0f);

            if (_chartNote != null)
            {
                _chartNote.text = hasErr ? TwinUITextR3.ChartNote : TwinUITextR3.ChartEmpty;
                _chartNote.color = TwinUIStyle.MutedColor;
            }
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            for (int i = 0; i < LinkRows; i++)
            {
                string p = "LinkCard/Row" + i + "/";
                _link[i] = new[] { T(p + "Label"), T(p + "Current"), T(p + "Target"), T(p + "Error"), T(p + "State") };
                _linkBars[i] = I(p + "Bar");
            }

            for (int i = 0; i < JointRows; i++)
            {
                string p = "JointCard/Row" + i + "/";
                _joint[i] = new[] { T(p + "Label"), T(p + "Current"), T(p + "Target"), T(p + "Error"), T(p + "State") };
                _jointBars[i] = I(p + "Bar");

                string q = "TorqueCard/Row" + i + "/";
                _torque[i] = new[] { T(q + "Label"), T(q + "Velocity"), T(q + "Torque"), T(q + "State") };
                _torqueBars[i] = I(q + "Bar");

                string w = "WrenchCard/Row" + i + "/";
                _wrench[i] = new[] { T(w + "Label"), T(w + "Current"), T(w + "Target"), T(w + "Error"), T(w + "State") };
                _wrenchBars[i] = I(w + "Bar");

                string o = "PoseCard/Row" + i + "/";
                _pose[i] = new[] { T(o + "Label"), T(o + "Current"), T(o + "Target"), T(o + "Error"), T(o + "State") };
                _poseBars[i] = I(o + "Bar");
            }

            _linkNote = T("LinkCard/Note");
            _wrenchNote = T("WrenchCard/Note");
            _torqueNote = T("TorqueCard/Note");
            _chartNote = T("ChartCard/Note");
            var chartT = transform.Find("ChartCard/Chart");
            _chart = chartT == null ? null : chartT.GetComponent<TwinTelemetryChart>();
        }

        private void SetBar(Image bar, TwinUITone tone, bool hasFrame)
        {
            if (bar == null)
            {
                return;
            }

            bar.color = TwinUiFormat.ToneColor(tone);
            bar.enabled = hasFrame || tone == TwinUITone.Warn || tone == TwinUITone.Crit;
        }

        private static Color ToneOrMuted(TwinUITone tone)
        {
            return tone == TwinUITone.Ok ? TwinUIStyle.TextColor : TwinUiFormat.ToneTextColor(tone);
        }

        private static void SetText(TMP_Text text, string value, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (text.text != value)
            {
                text.text = value;
            }

            text.color = color;
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

        private static string SignedErr(double value, string unit, int digits)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return TwinUiFormat.Dash;
            }

            return TwinUiFormat.Signed(value, unit, digits);
        }
    }
}