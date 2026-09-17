using System;
using Newtonsoft.Json;
using static Define;

/// <summary>
/// 조건/효과 클래스에 부착. key는 JSON의 "type" 값이자 레지스트리 키.
/// 툴은 이 어트리뷰트를 스캔해 노드 목록/설명을 만들고, 레지스트리 등록 누락을 검사한다.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InteractionNodeAttribute : Attribute
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }

    public InteractionNodeAttribute(string key, string displayName, string description = "")
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
    }
}

/// <summary>
/// 상태 술어. 부작용 없음.
///
/// 규칙
///   1. 필드는 기획자 파라미터만. 런타임 상태 필드 금지 → 정의를 모든 NPC가 공유한다
///   2. 파라미터 필드는 public (Unity SerializeReference + Newtonsoft 양쪽 직렬화 조건).
///      로드 이후 런타임 코드가 값을 바꾸면 모든 NPC에 반영되므로 "읽기 전용"으로 취급한다.
///   3. Evaluate는 몇 번 호출돼도 같은 입력에 같은 결과여야 한다.
/// </summary>
[Serializable]
public abstract class InteractionCondition
{
    public abstract bool Evaluate(in InteractionContext ctx);

    /// <summary>툴 정렬용. Newtonsoft가 프로퍼티도 직렬화하므로 반드시 JsonIgnore.</summary>
    [JsonIgnore] public virtual ENodeCost Cost { get { return ENodeCost.Cheap; } }
}

/// <summary>
/// 행위. 부작용 있음.
///
/// 규칙
///   1. 정의에 실린 인스턴스는 프로토타입. NPC마다 CreateInstance()로 복제한 것만 실행된다.
///   2. 프로토타입은 런타임 상태를 가지지 않는다 (마커 인스턴스 등은 OnActivate에서 생성).
///      그래야 기본 구현인 얕은 복사(MemberwiseClone)가 안전하다.
///   3. Latched 모드에 쓰이려면 SupportsDeactivate를 true로 하고 OnDeactivate를 구현한다.
///      툴은 Latched + SupportsDeactivate == false 조합을 경고한다.
/// </summary>
[Serializable]
public abstract class InteractionEffect
{
    public virtual InteractionEffect CreateInstance()
    {
        return (InteractionEffect)MemberwiseClone();
    }

    [JsonIgnore] public virtual bool SupportsDeactivate { get { return false; } }

    public virtual void OnActivate(in InteractionContext ctx) { }
    public virtual void OnDeactivate(in InteractionContext ctx) { }
}