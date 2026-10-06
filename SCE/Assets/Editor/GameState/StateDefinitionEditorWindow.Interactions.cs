using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 상호작용 파일과의 연결 — 스캔 관리 · 교차 확인 · 신호 규칙 카드의 "내는 상호작용" 표시.
/// 상호작용 파일은 읽기만 한다. 고치는 곳은 Interaction Editor 하나다 (두 툴이 같은 파일을 쓰면 서로 덮어쓴다).
/// 스캔 결과는 디스크에 저장된 내용 기준이다 — Interaction Editor에서 저장해야 여기에 반영된다.
/// </summary>
public sealed partial class StateDefinitionEditorWindow
{
    private const int MaxSendersOnCard = 3; // 규칙 카드에 이름으로 적는 상호작용 수 (넘으면 "외 n곳")

    private readonly InteractionReferenceScan _interactionScan = new InteractionReferenceScan();
    private long _interactionSignature;
    private bool _interactionScanned; // 한 번이라도 읽었는가 — 특별한 서명 값 대신 플래그로 둔다

    #region 스캔

    /// <summary>상호작용 폴더 — Interaction Editor가 고른 폴더를 그대로 따른다 (EditorPrefs 키와 기본값의 원본은 그 창).</summary>
    private static string InteractionFolder()
    {
        return EditorPrefs.GetString(InteractionEditorWindow.FolderPrefKey, InteractionEditorWindow.DefaultFolder);
    }

    /// <summary>
    /// 상호작용 폴더를 다시 읽고, 결과를 쓰는 화면(키 사용처 · 규칙 카드 · 검증 목록)을 갱신한다.
    /// force가 아니면 폴더 내용이 바뀌었을 때만 읽는다 — 창이 포커스를 받을 때마다 불린다.
    /// </summary>
    private void RescanInteractions(bool force)
    {
        string folder = InteractionFolder();
        long signature = EditorJsonFolder.Signature(folder);
        if (force == false && _interactionScanned && folder == _interactionScan.Folder && signature == _interactionSignature)
            return;

        ScanInteractions(folder, signature);

        RefreshInspector();
        _ruleList?.RefreshExternalReferences();
        ScheduleValidation(); // 교차 확인 결과가 달라질 수 있다. 바로 뒤에 RunValidationNow가 불리면 이 예약은 취소된다
    }

    /// <summary>읽기만 한다 (화면 갱신 없음).</summary>
    private void ScanInteractions(string folder, long signature)
    {
        _interactionScanned = true;
        _interactionSignature = signature;
        InteractionReferenceScanner.ScanFolder(folder, _interactionScan);
    }

    /// <summary>화면을 만드는 순서와 상관없이, 스캔 결과를 쓰기 전에 한 번은 읽혀 있도록 한다.</summary>
    private void EnsureInteractionScan()
    {
        if (_interactionScanned)
            return;

        string folder = InteractionFolder();
        ScanInteractions(folder, EditorJsonFolder.Signature(folder));
    }
    #endregion

    #region 교차 확인 (검사가 호출)
    /// <summary>규칙에 위치가 있는 경고는 _issues에, 상호작용 쪽에 위치가 있는 경고는 _interactionIssues에 더한다.</summary>
    private void CrossCheckInteractions(List<StateDefinitionSet> sets, StateKeyRegistry registry)
    {
        EnsureInteractionScan();
        StateInteractionCrossCheck.Check(sets, registry, _interactionScan, _issues, _interactionIssues);
    }

    /// <summary>
    /// 검증 목록에서 상호작용 쪽 경고를 눌렀을 때.
    /// 그 JSON을 Project 창에서 가리키고, 상태 키에 대한 경고면 키 사용처 패널에 그 키를 띄운다.
    /// </summary>
    private void JumpToInteractionIssue(InteractionCrossIssue issue)
    {
        if (string.IsNullOrEmpty(issue.FilePath) == false)
            PingFile(issue.FilePath);
        if (string.IsNullOrEmpty(issue.Key) == false)
            InspectKey(issue.Key);
    }
    #endregion

    #region 신호 규칙 카드 (규칙 탭이 호출)
    /// <summary>이 신호 ID를 내는 상호작용 문구. 없으면 null.</summary>
    private string DescribeSignalSenders(int signalId)
    {
        EnsureInteractionScan();
        return StateInteractionCrossCheck.DescribeSenders(_interactionScan, signalId, MaxSendersOnCard);
    }
    #endregion
}