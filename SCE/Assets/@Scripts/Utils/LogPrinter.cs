using System.Diagnostics;
using UnityEngine;

public static class LogPrinter
{
    /// <summary>
    /// Util Log Rapping
    /// </summary>

    #region static class
    [Conditional("UNITY_EDITOR")] 
    public static void Log(object message)
    {
        UnityEngine.Debug.Log(message);
    }
    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message)
    {
        UnityEngine.Debug.LogWarning(message);
    }
    [Conditional("UNITY_EDITOR")]
    public static void LogError(object message)
    {
        UnityEngine.Debug.LogError(message);
    }
    #endregion

    #region �Ϲ� class ��
    [Conditional("UNITY_EDITOR")]
    public static void Log(this Object obj, object message) // ��: "[PlayerController] ���� ü���� �����մϴ�."
    {
        string formattedMessage = $"[<b>{obj.GetType().Name}</b>] {message}";

        UnityEngine.Debug.Log(formattedMessage, obj);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(this Object obj, object message)
    {
        string formattedMessage = $"[<b>{obj.GetType().Name}</b>] {message}";
        UnityEngine.Debug.LogWarning(formattedMessage, obj);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogError(this Object obj, object message)
    {
        string formattedMessage = $"[<b>{obj.GetType().Name}</b>] {message}";
        UnityEngine.Debug.LogError(formattedMessage, obj);
    }
    #endregion
}
