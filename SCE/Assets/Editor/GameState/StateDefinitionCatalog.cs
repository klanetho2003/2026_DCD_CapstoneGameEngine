using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 디스크에 저장된 상태 정의를 읽어 만든 조회용 정보 — 읽기 전용.
/// Interaction Editor가 "이 키를 게임이 등록하는가", "이 신호를 받는 규칙이 있는가"를 묻는 데 쓴다.
/// 상태 파일을 고치는 곳은 State Definition Editor 하나다.
///
/// 등록소는 런타임 검사기(StateDefinitionValidator)가 채운다 — "게임이 실제로 등록할 키"의 판정이 게임·두 툴에서 한 곳이 되도록.
/// </summary>
public sealed class StateDefinitionCatalog
{
    private readonly List<StateDefinitionSet> _sets = new List<StateDefinitionSet>();
    private readonly List<string> _unreadableFiles = new List<string>(); // "파일 이름: 사유"
    private readonly Dictionary<string, string> _keyOwners = new Dictionary<string, string>(StringComparer.Ordinal); // 키 → 선언한 파일의 id
    private readonly List<StateDefinitionIssue> _issueScratch = new List<StateDefinitionIssue>();
    private StateKeyRegistry _registry; // 첫 읽기 전에는 null

    public string Folder { get; private set; } = "";
    public bool FolderExists { get; private set; }
    public IReadOnlyList<StateDefinitionSet> Sets { get { return _sets; } }
    public IReadOnlyList<string> UnreadableFiles { get { return _unreadableFiles; } }

    /// <summary>
    /// 판단 근거가 온전한가 — 폴더를 읽었고, 읽지 못한 상태 파일이 없다.
    /// 아니면 "선언되지 않았다", "받는 규칙이 없다"고 단정하지 않는다 (읽지 못한 파일에 있을 수 있다).
    /// </summary>
    public bool IsComplete { get { return FolderExists && _unreadableFiles.Count == 0; } }

    /// <summary>게임이 등록할 키의 수 (시스템 키 포함). 시스템 키가 먼저 온다.</summary>
    public int KeyCount { get { return _registry != null ? _registry.Count : 0; } }

    public StateKeyDefinition GetKey(int index)
    {
        return _registry.GetDefinition(index);
    }

    #region 읽기
    /// <summary>폴더 바로 아래의 JSON을 읽는다. 파싱에 실패한 상태 파일은 UnreadableFiles에 남기고, 다른 종류의 JSON은 건너뛴다.</summary>
    public void ScanFolder(string folder)
    {
        Folder = folder ?? "";
        FolderExists = Folder.Length > 0 && Directory.Exists(Folder);
        _sets.Clear();
        _unreadableFiles.Clear();

        if (FolderExists)
        {
            // State Definition Editor와 같은 순서로 읽는다 — 키 중복에서 "먼저 선언한 쪽"이 같아지도록
            string[] files = Directory.GetFiles(Folder, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                string text;
                try
                {
                    text = File.ReadAllText(files[i]);
                }
                catch (Exception e)
                {
                    _unreadableFiles.Add($"{name}: {e.Message}");
                    continue;
                }

                StateDefinitionSet set = StateDefinitionLoader.ParseRaw(text, out string error);
                if (set != null)
                    _sets.Add(set);
                else if (LooksLikeStateDefinition(text))
                    _unreadableFiles.Add($"{name}: {error}");
            }
        }
        Rebuild();
    }

    /// <summary>파일 IO 없이 Set 목록으로 채운다 (테스트용). 폴더는 읽힌 것으로 본다.</summary>
    public void SetSets(IReadOnlyList<StateDefinitionSet> sets, IReadOnlyList<string> unreadableFiles = null)
    {
        Folder = "";
        FolderExists = true;
        _sets.Clear();
        _sets.AddRange(sets);
        _unreadableFiles.Clear();
        if (unreadableFiles != null)
            _unreadableFiles.AddRange(unreadableFiles);
        Rebuild();
    }

    /// <summary>
    /// 파싱에 실패한 파일이 상태 정의를 쓰다 망가진 것인지, 다른 종류의 데이터인지 가른다.
    /// 최상위가 객체이고 keys나 rules가 있으면 상태 정의로 본다. 문법이 깨져 종류를 알 수 없으면 숨기지 않는다(true).
    /// State Definition Editor의 파일 목록과 이 클래스가 같은 기준을 쓴다.
    /// </summary>
    public static bool LooksLikeStateDefinition(string text)
    {
        try
        {
            JToken root = JToken.Parse(text);
            return root is JObject obj && (obj["keys"] != null || obj["rules"] != null);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>게임 로드와 같은 순서: 시스템 키를 먼저 등록하고, 검사기가 통과시킨 키를 등록한다.</summary>
    private void Rebuild()
    {
        _registry = new StateKeyRegistry();
        GameStateManager.RegisterSystemKeys(_registry);
        _issueScratch.Clear();
        StateDefinitionValidator.Validate(_sets, _registry, _issueScratch, null);

        // 등록된 키를 선언한 파일 — 등록소는 먼저 선언한 쪽을 받아들이므로 여기서도 처음 만난 쪽을 적는다
        _keyOwners.Clear();
        for (int s = 0; s < _sets.Count; s++)
        {
            StateDefinitionSet set = _sets[s];
            if (set == null || string.IsNullOrEmpty(set.Id) || set.Keys == null)
                continue;

            for (int k = 0; k < set.Keys.Length; k++)
            {
                string key = set.Keys[k]?.Key;
                if (IsDeclared(key) && _keyOwners.ContainsKey(key) == false)
                    _keyOwners.Add(key, set.Id);
            }
        }
    }
    #endregion

    #region 조회
    /// <summary>게임이 등록할 키인가 (시스템 키 포함). 게임의 StateValueCondition.OnLoad와 같은 조회다.</summary>
    public bool IsDeclared(string key)
    {
        return _registry != null && string.IsNullOrEmpty(key) == false && _registry.TryGetHandle(key, out _);
    }

    public bool TryGetKey(string key, out StateKeyDefinition definition)
    {
        if (_registry != null && string.IsNullOrEmpty(key) == false && _registry.TryGetHandle(key, out StateHandle handle))
        {
            definition = _registry.GetDefinition(handle.Index);
            return true;
        }
        definition = null;
        return false;
    }

    /// <summary>그 키를 선언한 파일의 id. 시스템 키이거나 모르는 키면 빈 문자열.</summary>
    public string OwnerOf(string key)
    {
        return key != null && _keyOwners.TryGetValue(key, out string owner) ? owner : "";
    }

    /// <summary>이 신호 ID를 받는 신호 규칙 문구. 없으면 null.</summary>
    public string DescribeSignalReceivers(int signalId, int max)
    {
        return StateInteractionCrossCheck.DescribeReceivers(_sets, signalId, max);
    }
    #endregion
}