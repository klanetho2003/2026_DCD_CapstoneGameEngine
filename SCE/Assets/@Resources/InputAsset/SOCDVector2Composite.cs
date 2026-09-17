using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;


#if UNITY_EDITOR
using UnityEditor;
[InitializeOnLoad] // 에디터 로드 시 자동 실행 보장
#endif
[DisplayStringFormat("{up}/{left}/{down}/{right}")]
public class SOCDVector2Composite : InputBindingComposite<Vector2>
{
    [InputControl(layout = "Button")] public int up = 0;
    [InputControl(layout = "Button")] public int down = 0;
    [InputControl(layout = "Button")] public int left = 0;
    [InputControl(layout = "Button")] public int right = 0;

    // 정적 생성자: Unity가 클래스에 처음 접근할 때 실행됨
    static SOCDVector2Composite()
    {
        // 에러를 뿜고 있는 'SOCD' 이름과, 띄어쓰기 없는 안전한 이름을 모두 등록
        InputSystem.RegisterBindingComposite<SOCDVector2Composite>("SOCD");
        InputSystem.RegisterBindingComposite<SOCDVector2Composite>("SOCDVector2");
    }

    // 런타임에 가장 빠른 타이밍(SubsystemRegistration)에 초기화 유도
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init() { }

    public override Vector2 ReadValue(ref InputBindingCompositeContext context)
    {
        bool isUp = context.ReadValueAsButton(up);
        bool isDown = context.ReadValueAsButton(down);
        bool isLeft = context.ReadValueAsButton(left);
        bool isRight = context.ReadValueAsButton(right);

        double upTime = context.GetPressTime(up);
        double downTime = context.GetPressTime(down);
        double leftTime = context.GetPressTime(left);
        double rightTime = context.GetPressTime(right);

        Vector2 result = Vector2.zero;

        // X축 (좌/우) 최신 입력 판별
        if (isLeft && isRight) result.x = leftTime > rightTime ? -1f : 1f;
        else if (isLeft) result.x = -1f;
        else if (isRight) result.x = 1f;

        // Y축 (상/하) 최신 입력 판별
        if (isUp && isDown) result.y = upTime > downTime ? 1f : -1f;
        else if (isUp) result.y = 1f;
        else if (isDown) result.y = -1f;

        return result.normalized;
    }
}