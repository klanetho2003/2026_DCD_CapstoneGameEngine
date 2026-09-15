using UnityEngine;

/// <summary>
/// 같은 Init 패턴을 사용하는 클래스들이 InitBase를 상속받아서
/// 번거롭게 같은 Typing을 반복하지 않도록 하는 Class
/// </summary>

public abstract class InitBase : MonoBehaviour
{
    protected bool _init = false;

    public virtual bool Init()
    {
        if (_init)
            return false;

        _init = true;
        return true;
    }

    private void Awake()
    {
        Init();
    }
}
