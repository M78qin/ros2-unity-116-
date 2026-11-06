// 快照导出：5 Hz（可配）把 TwinUiSnapshot 写到 Logs/ui_snapshot.json，供外部 AI/分析脚本只读消费。
// 原子写：先写 .tmp 再替换，避免外部读到半个文件。
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiSnapshotExport
    {
        private string _path = "Logs/ui_snapshot.json";
        private float _nextWrite;
        private long _writeCount;
        private long _failures;
        private string _lastError = string.Empty;
        private int _lastBytes;

        public string FilePath => _path;
        public long WriteCount => _writeCount;
        public long Failures => _failures;
        public string LastError => _lastError;
        public int LastBytes => _lastBytes;

        public void Configure(TwinUiSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.snapshotPath))
            {
                _path = settings.snapshotPath;
            }
        }

        public string ResolvePath()
        {
            return Path.GetFullPath(_path);
        }

        /// <summary>按 settings.snapshotExportHz 节流写出；返回 true 表示本次真的写了。</summary>
        public bool MaybeWrite(TwinUiSnapshot snapshot, TwinUiSettings settings, float nowSeconds)
        {
            if (snapshot == null)
            {
                return false;
            }

            float hz = settings != null && settings.snapshotExportHz > 0f ? settings.snapshotExportHz : 5f;
            if (nowSeconds < _nextWrite)
            {
                return false;
            }

            _nextWrite = nowSeconds + 1f / hz;
            return Write(snapshot);
        }

        public bool Write(TwinUiSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            try
            {
                string full = ResolvePath();
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = snapshot.ToJson(true);
                string tmp = full + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(full))
                {
                    File.Delete(full);
                }

                File.Move(tmp, full);
                _writeCount++;
                _lastBytes = json.Length;
                return true;
            }
            catch (Exception ex)
            {
                _failures++;
                _lastError = ex.GetType().Name + ": " + ex.Message;
                if (_failures <= 3)
                {
                    Debug.LogWarning("[Twin UI export] write failed: " + _lastError);
                }

                return false;
            }
        }
    }
}