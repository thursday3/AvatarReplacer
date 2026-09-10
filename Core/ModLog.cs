using System;
using BepInEx.Logging;

namespace AvatarReplacer
{

    public static class ModLog
    {
        private static ManualLogSource _log;

        public static void SetLogger(ManualLogSource log)
        {
            _log = log;
        }

        public static void Info(string message)
        {
            if (_log != null)
                _log.LogInfo(message);
            else
                UnityEngine.Debug.Log(message);
        }

        public static void Warning(string message)
        {
            if (_log != null)
                _log.LogWarning(message);
            else
                UnityEngine.Debug.LogWarning(message);
        }

        public static void Error(string message)
        {
            if (_log != null)
                _log.LogError(message);
            else
                UnityEngine.Debug.Log(message);
        }

        public static void Debug(string message)
        {
            if (!AvatarReplacerPlugin.IsDebugEnabled)
                return;
            string full = "[D] " + message;
            if (_log != null)
                _log.LogInfo(full);
            else
                UnityEngine.Debug.Log(full);
        }

        public static void ErrorException(Exception ex)
        {
            if (_log != null)
                _log.LogError(ex);
            else
                UnityEngine.Debug.LogException(ex);
        }
    }
}
