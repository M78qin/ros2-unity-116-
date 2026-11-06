// 事件/告警总线：帧/命令/告警/实验事件 → 内存订阅 + 追加写 Logs/ui_events.jsonl。
// 只做记录与分发，不做任何控制动作；不触碰 TwinRecorder（论文 recorder 由实验流程负责）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiEventBus
    {
        private const int Capacity = 240;

        public struct Entry
        {
            public string TimeUtc;
            public string TimeLocal;
            public string Kind;   // session / frame / alert / recover / export / settings
            public string Code;
            public string Text;
            public TwinUITone Tone;
        }

        private readonly List<Entry> _entries = new List<Entry>(Capacity);
        private readonly object _lock = new object();
        private string _filePath = "Logs/ui_events.jsonl";
        private long _total;
        private long _writeFailures;

        public TwinUiEventBus(string filePath)
        {
            _filePath = string.IsNullOrEmpty(filePath) ? "Logs/ui_events.jsonl" : filePath;
        }

        public string FilePath => _filePath;
        public long Total => _total;
        public long WriteFailures => _writeFailures;
        public bool Paused { get; set; }
        public IReadOnlyList<Entry> Recent => _entries;
        public int Count => _entries.Count;

        public string ResolvePath()
        {
            return Path.GetFullPath(_filePath);
        }

        public void Publish(string kind, TwinUITone tone, string code, string text)
        {
            var entry = new Entry
            {
                TimeUtc = DateTime.UtcNow.ToString("O"),
                TimeLocal = DateTime.Now.ToString("HH:mm:ss.fff"),
                Kind = kind ?? string.Empty,
                Code = code ?? string.Empty,
                Text = text ?? string.Empty,
                Tone = tone
            };

            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > Capacity)
                {
                    _entries.RemoveAt(0);
                }

                _total++;
            }

            if (!Paused)
            {
                AppendJsonl(entry);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        private void AppendJsonl(Entry entry)
        {
            try
            {
                string dir = Path.GetDirectoryName(ResolvePath());
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder(160);
                sb.Append("{\"ts\":\"").Append(Escape(entry.TimeUtc))
                    .Append("\",\"kind\":\"").Append(Escape(entry.Kind))
                    .Append("\",\"level\":\"").Append(ToneName(entry.Tone))
                    .Append("\",\"code\":\"").Append(Escape(entry.Code))
                    .Append("\",\"text\":\"").Append(Escape(entry.Text))
                    .Append("\"}");
                File.AppendAllText(ResolvePath(), sb.ToString() + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _writeFailures++;
                if (_writeFailures <= 3)
                {
                    Debug.LogWarning("[Twin UI events] append failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static string ToneName(TwinUITone tone)
        {
            return tone == TwinUITone.Crit ? "Crit" : tone == TwinUITone.Warn ? "Warn"
                : tone == TwinUITone.Ok ? "Ok" : "Info";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");
        }
    }
}