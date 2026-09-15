using System.Diagnostics;
using UnityEngine;

public class Util
{
    #region Log Rapping

    /* 전처리 지시문을 사용하지 않는 이유는?
     * 
     * 매개변수로 넘겨 받는 연산은 실행되기 때문에,
     * 매개변수에서 문자열 연산($"testLog_{myHp.ToString()}")을
     * 하면 GC가 쌓일 수 있다. Conditional 어트리뷰트를 사용하면,
     * 매개변수로 넘겨 받는 연산 자체가 컴파일 타임에 제거되기 때문에
     * GC가 쌓이는 것을 방지할 수 있다.
     */

    [Conditional("UNITY_EDITOR")]
    // [Conditional("DEVELOPMENT_BUILD")] // 개발 빌드에서도 보고 싶다면 주석을 해제하셔
    public static void Log(object message)
    {
        UnityEngine.Debug.Log(message);
    }

    [Conditional("UNITY_EDITOR")]
    public static void Log(object message, Object context)
    {
        UnityEngine.Debug.Log(message, context);
    }

    // 경고 로그
    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message, Object context)
    {
        UnityEngine.Debug.LogWarning(message, context);
    }

    // 에러 로그
    [Conditional("UNITY_EDITOR")]
    public static void LogError(object message)
    {
        UnityEngine.Debug.LogError(message);
    }

    [Conditional("UNITY_EDITOR")]
    public static void LogError(object message, Object context)
    {
        UnityEngine.Debug.LogError(message, context);
    }
    #endregion

    #region Find Child

    public static GameObject FindChild(GameObject go, string name = null, bool recursive = false)
    {
        Transform transform = FindChild<Transform>(go, name, recursive);
        if (transform == null)
            return null;

        return transform.gameObject;
    }

    public static T FindChild<T>(GameObject go, string name = null, bool recursive = false) where T : UnityEngine.Object
    {
        if (go == null)
            return null;

        if (recursive == false)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform transform = go.transform.GetChild(i);
                if (string.IsNullOrEmpty(name) || transform.name == name)
                {
                    T component = transform.GetComponent<T>();
                    if (component != null)
                        return component;
                }
            }
        }
        else
        {
            foreach (T component in go.GetComponentsInChildren<T>())
            {
                if (string.IsNullOrEmpty(name) || component.name == name)
                    return component;
            }
        }

        return null;
    }
    #endregion

    public static T GetOrAddComponent<T>(GameObject go) where T : UnityEngine.Component
    {
        T component = go.GetComponent<T>();

        if (component == null) component = go.AddComponent<T>();

        return component;
    }
}
