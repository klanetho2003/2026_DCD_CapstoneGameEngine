using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;


#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>네 방향 키를 벡터로 합성하고 같은 축의 반대 방향 입력을 상쇄한다.</summary>
#if UNITY_EDITOR
[InitializeOnLoad] // 에디터 로드 시 자동 실행 보장
#endif
[DisplayStringFormat("{up}/{left}/{down}/{right}")]
public class SOCDVector2Composite : InputBindingComposite<Vector2>
{
    /// <summary>위쪽 방향 버튼에 연결된 입력 바인딩 번호다.</summary>
    [InputControl(layout = "Button")] public int up = 0;
    /// <summary>아래쪽 방향 버튼에 연결된 입력 바인딩 번호다.</summary>
    [InputControl(layout = "Button")] public int down = 0;
    /// <summary>왼쪽 방향 버튼에 연결된 입력 바인딩 번호다.</summary>
    [InputControl(layout = "Button")] public int left = 0;
    /// <summary>오른쪽 방향 버튼에 연결된 입력 바인딩 번호다.</summary>
    [InputControl(layout = "Button")] public int right = 0;

    /// <summary>기존 입력 에셋이 사용하는 두 이름으로 방향 합성기를 등록한다.</summary>
    static SOCDVector2Composite()
    {
        // 에러를 뿜고 있는 'SOCD' 이름과, 띄어쓰기 없는 안전한 이름을 모두 등록
        InputSystem.RegisterBindingComposite<SOCDVector2Composite>("SOCD");
        InputSystem.RegisterBindingComposite<SOCDVector2Composite>("SOCDVector2");
    }

    /// <summary>런타임 시작 시 정적 생성자를 실행하여 방향 합성기의 등록을 보장한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init() { }

    /// <summary>각 축의 반대 입력을 상쇄하고 남은 방향을 정규화한 벡터로 반환한다.</summary>
    /// <param name="context">각 방향 버튼의 현재 입력 상태를 읽을 문맥이다.</param>
    public override Vector2 ReadValue(ref InputBindingCompositeContext context)
    {
        // 위쪽 방향 버튼이 현재 눌려 있는지 나타낸다.
        bool isUp = context.ReadValueAsButton(up);
        // 아래쪽 방향 버튼이 현재 눌려 있는지 나타낸다.
        bool isDown = context.ReadValueAsButton(down);
        // 왼쪽 방향 버튼이 현재 눌려 있는지 나타낸다.
        bool isLeft = context.ReadValueAsButton(left);
        // 오른쪽 방향 버튼이 현재 눌려 있는지 나타낸다.
        bool isRight = context.ReadValueAsButton(right);

        // 좌우 입력의 차이이며 두 버튼을 함께 누르면 0이 된다.
        float x = (isRight ? 1f : 0f) - (isLeft ? 1f : 0f);
        // 상하 입력의 차이이며 두 버튼을 함께 누르면 0이 된다.
        float y = (isUp ? 1f : 0f) - (isDown ? 1f : 0f);

        return new Vector2(x, y).normalized;
    }
}
