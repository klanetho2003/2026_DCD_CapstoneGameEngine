using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Define;

#region ShowMarker
/// <summary>
/// NPC 머리 위 마커("!" 등). Latched 전용 — Activate에서 생성, Deactivate에서 제거.
/// 마커 인스턴스는 런타임 상태이므로 프로토타입에는 없고 OnActivate에서만 만들어진다 (MemberwiseClone 안전).
/// </summary>
[Serializable]
[InteractionNode("ShowMarker", "마커 표시", "NPC 위에 마커 프리팹을 띄운다. Latched 모드에서만 의미 있음")]
public sealed class ShowMarkerEffect : InteractionEffect
{
    [JsonProperty("prefabKey")]
    public string PrefabKey = "Marker_Exclaim";

    [JsonProperty("offsetX")] public float OffsetX = 0f;
    [JsonProperty("offsetY")] public float OffsetY = 1.5f;

    private GameObject _instance; // 런타임 상태. 프로토타입에서는 항상 null

    [JsonIgnore] public override bool SupportsDeactivate { get { return true; } }

    public override void OnActivate(in InteractionContext ctx)
    {
        if (_instance != null)
            return;

        // 프로젝트 Resource API 이름에 맞춰 교체
        _instance = Managers.Resource.Instantiate(PrefabKey, ctx.Owner.transform);
        if (_instance == null)
        {
            LogPrinter.LogError($"[ShowMarkerEffect] 프리팹 없음 >> {PrefabKey}");
            return;
        }
        _instance.transform.localPosition = new Vector3(OffsetX, OffsetY, 0f);
    }

    public override void OnDeactivate(in InteractionContext ctx)
    {
        if (_instance == null)
            return;

        Managers.Resource.Destroy(_instance); // 풀링이면 풀 반환
        _instance = null;
    }
}
#endregion

#region PrintLog
[Serializable]
[InteractionNode("PrintLog", "로그 출력", "콘솔에 대사를 출력한다 (대사 시스템 전 임시)")]
public sealed class PrintLogEffect : InteractionEffect
{
    [JsonProperty("text")]
    public string Text = "";

    public override void OnActivate(in InteractionContext ctx)
    {
        LogPrinter.Log($"<color=cyan>[{ctx.Owner.name}]</color> {Text}"); // 문자열 보간 할당은 로그 효과 특성상 허용
    }
}
#endregion

#region DEV_Kill_Villager
[Serializable]
[InteractionNode("Kill_Villager", "주민 죽이기", "상호작용 Villager에게 10,000 데미지를 준다")]
public sealed class KillVillagerEffect : InteractionEffect
{
    public override void OnActivate(in InteractionContext ctx)
    {
        var target = ctx.Owner;

        if (ctx.Owner is not Villager villager)
            return;

        var monster = Managers.Object.Spawn<MonsterBase>(0, default);

        var damageInfo = DamageCalculator.Calculate(
            monster,
            0,
            10000,
            EDamageType.None,
            EElement.None,
            null,
            hitStopDuration: 0,
            hitVfxKey: null,
            cameraShakeStrength: 0);

        villager.OnDamaged(damageInfo);

        Managers.Object.Despawn(monster);
    }
}
#endregion

#region Possess
[Serializable]
[InteractionNode("Possess", "입력 주체 변경", "상호작용 Villager로 입력 주체를 옮긴다")]
public sealed class PossessEffect : InteractionEffect
{
    public override void OnActivate(in InteractionContext ctx)
    {
        var lastPossessed = Managers.Object.PossessedTarget;

        if (ctx.Owner is not Villager villager)
            return;

        Managers.Object.SetPossession(villager);
    }
}
#endregion

#region SpawnUI
[Serializable]
[InteractionNode("SpawnUI", "UI 열기", "이름으로 팝업 UI를 연다. 닫힘은 UI 자신이 처리")]
public sealed class SpawnUIEffect : InteractionEffect
{
    [JsonProperty("uiName")]
    public string UiName = "";

    public override void OnActivate(in InteractionContext ctx)
    {
        // 프로젝트 UI 매니저의 문자열 기반 오픈 API에 맞춰 교체 (제네릭 ShowPopupUI<T>는 데이터 구동에 부적합)
        //Managers.UI.ShowPopupUI(UiName);
    }
}
#endregion

#region PlayAnimation
[Serializable]
[InteractionNode("PlayAnimation", "애니메이션 재생", "NPC Animator의 Trigger 파라미터를 발동한다")]
public sealed class PlayAnimationEffect : InteractionEffect
{
    [JsonProperty("trigger")]
    public string Trigger = "";

    // 런타임 캐시. 효과 인스턴스는 NPC별이고 같은 owner에서만 재사용되므로 안전 (InteractionComponent.SetInfo의 sameSet 조건)
    private Animator _animator;
    private int _hash;

    public override void OnActivate(in InteractionContext ctx)
    {
        if (_animator == null)
        {
            _animator = ctx.Owner.GetComponent<Animator>(); // CreatureAnim 래퍼가 있으면 그쪽으로 교체
            _hash = Animator.StringToHash(Trigger);
            if (_animator == null)
            {
                LogPrinter.LogError($"[PlayAnimationEffect] Animator 없음 >> {ctx.Owner.name}");
                return;
            }
        }
        _animator.SetTrigger(_hash);
    }
}
#endregion

#region SwapCreatureData
/// <summary>
/// 스폰 요청 단계 전용 — 스폰할 templateID를 교체한다 (프리팹, 스탯, 상호작용 전부 교체).
/// 무상태: 프로토타입이 직접 실행되므로 파라미터 외 필드를 두지 않는다.
/// </summary>
[Serializable]
[InteractionNode("SwapCreatureData", "생성 데이터 교체", "스폰 요청 단계 전용 — 조건을 만족하면 이 templateID로 스폰한다. 같은 종류(NPC/Monster)끼리만")]
public sealed class SwapCreatureDataEffect : InteractionEffect, IInteractionNodeLoadHook
{
    [JsonProperty("dataId")]
    public int DataId;

    [JsonIgnore] public override EEffectPhase Phase { get { return EEffectPhase.SpawnRequest; } }

    public void OnLoad(StateKeyRegistry stateKeys, string where, List<string> errors)
    {
        // 존재·종류 검사는 런타임 해석기가 한다 (CreatureData 로드 순서와 무관하게 하기 위해)
        if (DataId <= 0)
            errors.Add($"{where}: SwapCreatureData dataId 미지정");
    }

    public override void OnActivate(in InteractionContext ctx)
    {
        SpawnRequest request = ctx.SpawnRequest;
        if (request == null)
        {
            LogPrinter.LogError("[SwapCreatureDataEffect] 스폰 요청 단계 밖에서 실행됨 — 검증을 우회한 배치 확인");
            return;
        }
        request.DataId = DataId;
    }
}
#endregion