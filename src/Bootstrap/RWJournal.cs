// SPDX-License-Identifier: MIT
using System;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWJournal
    {
        internal static readonly string Session = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0,8);
        private static readonly object Gate = new object();
        private static bool _writeDisabled;
        // Diagnostics are explicitly opt-in for public builds. Serious warnings,
        // camera failures, entry failures and errors still use Warn / Error.
        // Create <persistentDataPath>/RealWalkingG1/debug-logging.flag and restart
        // the game to retain high-frequency performance/geometry trace records.
        private static readonly bool _diagnosticsEnabled = ReadDiagnosticsFlag();
        internal static bool DiagnosticsEnabled { get { return _diagnosticsEnabled; } }
        private static bool ReadDiagnosticsFlag()
        {
            try
            {
                return System.IO.File.Exists(System.IO.Path.Combine(
                    UnityEngine.Application.persistentDataPath,
                    "RealWalkingG1", "debug-logging.flag"));
            }
            catch (Exception) { return false; }
        }
        internal static void Trace(string message)
        {
            if (!_diagnosticsEnabled) return;
            Warn(message);
        }

        internal static string JournalFile
        {
            get { return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,
                "RealWalkingG1", "RW-G1-v090-" + Session + ".txt"); }
        }
        internal static void Warn(string message) { Mod.Log.Warn(message); Append(message); }
        internal static void Error(string message) { Mod.Log.Error(message); Append(message); }
        private static void Append(string message)
        {
            if (_writeDisabled) return;
            lock (Gate)
            {
                try
                {
                    string path = JournalFile;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? UnityEngine.Application.persistentDataPath);
                    System.IO.File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
                }
                catch (Exception e)
                {
                    _writeDisabled = true;
                    Mod.Log.Warn("[RW-G1] JOURNAL-WRITE-FAILED (continuing normal log only): " + e.Message);
                }
            }
        }
    }
}
