using System;
using System.Runtime.InteropServices;

/// <summary>노드 실행 결과 — 성공 · 실패 · 진행 중 (다음 평가에 이어서)</summary>
public enum EBehaviorStatus : byte
{
    Success,
    Failure,
    Running,
}

/// <summary>
/// 조건 Decorator의 결과가 바뀔 때 끊을 범위 (Unreal의 Observer Aborts).
///  - Self: 붙은 노드의 가지가 진행 중일 때 거짓이 되면 그 가지를 끊는다 (그 노드는 실패로 끝나고 부모가 다음 자식으로)
///  - LowerPriority: 더 낮은 우선순위 가지가 진행 중일 때 참이 되면, 붙은 노드부터 다시 검색한다 (시작할 Task를 찾았을 때만 끊음)
///  - Both: 둘 다
/// </summary>
public enum EBehaviorAbortMode : byte
{
    None,
    Self,
    LowerPriority,
    Both,
}

/// <summary>Task 끊기의 결과 — 바로 끝 / 끝날 때까지 미룸 (실행기가 OnAbortUpdate로 다시 묻는다)</summary>
public enum EBehaviorAbortResult : byte
{
    Done,
    Pending,
}

/// <summary>Blackboard 키의 값 형식</summary>
public enum EBehaviorKeyType : byte
{
    Bool,
    Int,
    Float,
    Vector2,
    Creature,
}

/// <summary>노드가 받는 키 형식 묶음 — 키 고르기 칸을 해석할 때 맞는 형식인지 본다</summary>
[Flags]
public enum EBehaviorKeyTypeMask : byte
{
    None = 0,
    Bool = 1 << 0,
    Int = 1 << 1,
    Float = 1 << 2,
    Vector2 = 1 << 3,
    Creature = 1 << 4,
    Any = Bool | Int | Float | Vector2 | Creature,
}

/// <summary>컴파일 문제의 무게 — 에러가 하나라도 있으면 그 트리를 쓰지 않는다</summary>
public enum EBehaviorMessageSeverity : byte
{
    Error,
    Warning,
}

/// <summary>노드 종류 — 등록소가 JSON 칸마다 맞는 종류인지 가른다</summary>
public enum EBehaviorNodeKind : byte
{
    Composite,
    Task,
    Decorator,
    Service,
}

/// <summary>
/// 몬스터마다 노드 하나가 쓰는 16바이트 칸. 노드 객체는 모든 몬스터가 공유하고 (설정만), 실행 상태는 실행기의 이 칸 배열에 둔다.
/// 칸의 뜻은 그 노드가 정한다 (끝 시각 · 남은 횟수 등). 처음 값은 모두 0 — 노드는 0을 '아직 없음'으로 읽도록 정한다.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BehaviorNodeMemory
{
    public int Int0;
    public int Int1;
    public float Float0;
    public float Float1;
}

/// <summary>
/// 노드 클래스에 붙인다. key는 JSON의 "type" 값이자 등록소 키 — 클래스 이름과 따로라 이름을 바꿔도 데이터는 그대로다.
/// 등록소가 등록할 때 이 어트리뷰트를 읽어 노드 목록 · 설명을 만든다. 등록 목록(BehaviorNodeCatalog)에 빠진 노드는 시험이 잡는다.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BehaviorNodeAttribute : Attribute
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    /// <summary>편집기의 노드 고르기 목록에서 묶는 이름 (예: "이동", "판정"). 비우면 종류 이름으로 묶는다</summary>
    public string Category { get; set; }

    public BehaviorNodeAttribute(string key, string displayName, string description = "")
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}