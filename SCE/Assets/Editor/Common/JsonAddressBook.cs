using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

/// <summary>
/// 에디터 툴 공용 — 문서 파일 (Interaction Set · Behavior Tree)의 Addressable 등록 읽기와 맞추기. 판정은 JsonAddressRule (순수), 여기는 Addressables를 읽고 쓰는 얇은 층이다.
/// 게임은 문서를 주소 = 문서 Id, PreLoad label로 찾는다 (ResourceManager.Load — 미리 읽은 것만, 열쇠는 주소).
///
/// 색인은 만들 때 모든 항목을 한 번 훑는다 O(등록된 에셋 수) — 폴더째 등록한 곳 안 · Resources까지 본다.
/// 미리 읽기(ResourceManager.LoadAllAsync)는 위치마다 주소로 다시 찾으므로, 같은 주소의 에셋은 label과 상관없이 섞일 수 있기 때문이다.
/// 색인은 만든 때의 모습이다 — Addressables 수정 이벤트 횟수(EditorAddressables.Version)가 바뀌면 부르는 쪽이 새로 만든다. 여기서 맞춘 것은 색인에도 바로 반영한다.
/// </summary>
public sealed class JsonAddressBook
{
    private const string Label = ResourceManager.PreLoadLabel;

    /// <summary>주소 하나를 쓰는 에셋 — 앞의 둘만 기억한다 (자기 자신을 빼고 하나를 알리면 충분하다).</summary>
    private struct AddressUsers
    {
        public string FirstGuid;
        public string FirstPath;
        public string SecondGuid;
        public string SecondPath;
    }

    private readonly AddressableAssetSettings _settings;
    private readonly Dictionary<string, AddressableAssetEntry> _entriesByGuid = new Dictionary<string, AddressableAssetEntry>(StringComparer.Ordinal); // 직접 등록한 항목만
    private readonly Dictionary<string, string> _implicitAddressByGuid = new Dictionary<string, string>(StringComparer.Ordinal);                       // 폴더째 등록한 곳 안 · Resources — 그때의 주소
    private readonly Dictionary<string, AddressUsers> _usersByAddress = new Dictionary<string, AddressUsers>(StringComparer.Ordinal);

    public bool HasSettings => _settings != null;

    private JsonAddressBook(AddressableAssetSettings settings)
    {
        _settings = settings;
    }

    #region 색인
    /// <summary>지금 Addressable 설정으로 색인을 만든다. 설정이 없으면 HasSettings = false. O(등록된 에셋 수)</summary>
    public static JsonAddressBook Build()
    {
        var book = new JsonAddressBook(AddressableAssetSettingsDefaultObject.Settings);
        if (book._settings == null)
            return book;

        // 직접 등록한 항목 — 그룹의 entries에는 폴더째 등록한 곳 안의 에셋이 없다
        foreach (AddressableAssetGroup group in book._settings.groups)
        {
            if (group == null)
                continue;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry != null && entry.IsFolder == false && string.IsNullOrEmpty(entry.guid) == false)
                    book._entriesByGuid.TryAdd(entry.guid, entry);
            }
        }

        // 주소를 쓰는 에셋 — 폴더째 등록한 곳 안 · Resources 포함 (게임이 주소로 다시 찾을 때 섞일 수 있는 것 전부)
        // 직접 등록이 아닌 에셋의 주소도 여기서 기억한다 — 파일마다 Addressables에 묻는 FindAssetEntry(guid, true)는 항목 수만큼 걸릴 수 있다
        var all = new List<AddressableAssetEntry>();
        book._settings.GetAllAssets(all, false);
        for (int i = 0; i < all.Count; i++)
        {
            AddressableAssetEntry entry = all[i];
            if (entry == null || entry.IsFolder || string.IsNullOrEmpty(entry.address))
                continue;

            string guid = entry.guid ?? "";
            book.AddUser(entry.address, guid, entry.AssetPath);
            if (guid.Length > 0 && book._entriesByGuid.ContainsKey(guid) == false)
                book._implicitAddressByGuid.TryAdd(guid, entry.address);
        }
        return book;
    }

    /// <summary>문서 파일 하나의 등록 사실. filePath가 없거나(새 문서) Asset Database에 없으면 등록 안 됨으로 본다. O(1) — 색인만 찾는다</summary>
    public JsonAddressFacts Read(string filePath, string id)
    {
        string guid = string.IsNullOrEmpty(filePath) ? "" : AssetDatabase.AssetPathToGUID(filePath);
        string otherUser = OtherUser(id, guid);

        if (guid.Length > 0 && _entriesByGuid.TryGetValue(guid, out AddressableAssetEntry entry))
            return new JsonAddressFacts(true, entry.address, entry.labels.Contains(Label), null, otherUser);

        string folderAddress = guid.Length > 0 && _implicitAddressByGuid.TryGetValue(guid, out string address) ? address : null;
        return new JsonAddressFacts(false, null, false, folderAddress, otherUser);
    }

    /// <summary>이 주소를 쓰는 다른 에셋의 경로 (selfGuid 말고, 하나). 없으면 null. O(1)</summary>
    private string OtherUser(string address, string selfGuid)
    {
        if (string.IsNullOrEmpty(address) || _usersByAddress.TryGetValue(address, out AddressUsers users) == false)
            return null;
        if (users.FirstGuid != selfGuid)
            return users.FirstPath;
        return users.SecondGuid != null ? users.SecondPath : null;
    }

    private void AddUser(string address, string guid, string path)
    {
        string shown = string.IsNullOrEmpty(path) ? guid : path; // Resources · 장면 목록 같은 특수 항목은 경로가 비어 있을 수 있다
        if (_usersByAddress.TryGetValue(address, out AddressUsers users) == false)
        {
            _usersByAddress.Add(address, new AddressUsers { FirstGuid = guid, FirstPath = shown });
            return;
        }
        if (users.FirstGuid == guid || users.SecondGuid != null)
            return; // 같은 에셋이 두 번(직접 + 폴더째) 나왔거나, 이미 둘을 안다

        users.SecondGuid = guid;
        users.SecondPath = shown;
        _usersByAddress[address] = users;
    }

    private void RemoveUser(string address, string guid)
    {
        if (string.IsNullOrEmpty(address) || _usersByAddress.TryGetValue(address, out AddressUsers users) == false)
            return;

        if (users.SecondGuid == guid)
        {
            users.SecondGuid = null;
            users.SecondPath = null;
        }
        else if (users.FirstGuid == guid)
        {
            if (users.SecondGuid == null)
            {
                _usersByAddress.Remove(address);
                return;
            }
            users.FirstGuid = users.SecondGuid;
            users.FirstPath = users.SecondPath;
            users.SecondGuid = null;
            users.SecondPath = null;
        }
        else
            return;
        _usersByAddress[address] = users;
    }
    #endregion

    #region 맞추기
    /// <summary>
    /// 새로 등록할 그룹 — 이 문서 파일들이 가장 많이 든 그룹(읽기 전용 제외), 없으면 기본 그룹.
    /// 고르는 규칙은 JsonAddressRule.PickGroup. O(파일 수)
    /// </summary>
    public AddressableAssetGroup PickGroup(IReadOnlyList<string> documentFilePaths)
    {
        if (_settings == null)
            return null;

        var groupNames = new List<string>(documentFilePaths.Count);
        for (int i = 0; i < documentFilePaths.Count; i++)
        {
            string guid = AssetDatabase.AssetPathToGUID(documentFilePaths[i]);
            if (string.IsNullOrEmpty(guid) == false && _entriesByGuid.TryGetValue(guid, out AddressableAssetEntry entry)
                && entry.parentGroup != null && entry.parentGroup.ReadOnly == false)
                groupNames.Add(entry.parentGroup.Name);
        }

        string name = JsonAddressRule.PickGroup(groupNames);
        AddressableAssetGroup group = name != null ? _settings.FindGroup(name) : null;
        return group != null ? group : _settings.DefaultGroup;
    }

    /// <summary>
    /// 등록 · 주소 · label을 맞춘다 — 판정이 Fixable일 때만 부른다 (겹침 · 폴더째 등록은 손대지 않는다).
    /// 직접 등록이 없으면 group에 새로 만든다. 이미 있는 항목은 그룹을 옮기지 않는다.
    /// 바꾼 그룹을 touched에 더하고 무엇을 했는지 문장으로 반환한다 (바꿀 것이 없었으면 ""). 못 하면 null과 이유.
    /// 수정 이벤트는 내지 않는다 — Commit이 한 번에 낸다. 맞춘 것은 색인에도 바로 반영한다 (같은 저장 · 맞추기 안의 다음 판정이 보도록).
    /// O(1), 색인에 없는 파일만 O(그룹 수)
    /// </summary>
    public string Fix(string filePath, string id, AddressableAssetGroup group, HashSet<AddressableAssetGroup> touched, out string error)
    {
        error = null;
        if (_settings == null)
        {
            error = "Addressable 설정 없음";
            return null;
        }
        if (string.IsNullOrEmpty(filePath) || File.Exists(filePath) == false)
        {
            error = "파일이 없음 — [새로고침]으로 다시 읽을 것";
            return null;
        }
        string guid = AssetDatabase.AssetPathToGUID(filePath);
        if (string.IsNullOrEmpty(guid))
        {
            error = "Asset Database에 없는 파일 — Unity가 임포트한 뒤 다시";
            return null;
        }

        if (_entriesByGuid.TryGetValue(guid, out AddressableAssetEntry entry) == false || entry.parentGroup == null)
            entry = _settings.FindAssetEntry(guid); // 색인이 낡았을 수 있다 (버전 관리로 바뀐 그룹 파일) — 이미 있는 항목을 새 그룹으로 옮기지 않도록 한 번 더 묻는다

        if (entry == null || entry.parentGroup == null)
        {
            if (group == null)
            {
                error = "등록할 Addressable 그룹이 없음";
                return null;
            }
            entry = _settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
            {
                error = "Addressable에 등록하지 못함 — 등록할 수 없는 경로";
                return null;
            }
            _entriesByGuid[guid] = entry;
            _implicitAddressByGuid.Remove(guid);
            ApplyAddressAndLabel(entry, id, guid);
            touched.Add(entry.parentGroup);
            return $"등록 — 그룹 {entry.parentGroup.Name}, 주소 {id}, label {Label}";
        }

        _entriesByGuid[guid] = entry;
        string change = entry.address != id ? $"주소 {entry.address} → {id}" : "";
        if (entry.labels.Contains(Label) == false)
            change = change.Length > 0 ? $"{change}, label {Label} 추가" : $"label {Label} 추가";
        if (change.Length == 0)
            return change; // 색인이 낡아 Fixable로 보였을 뿐 — 이미 맞다

        ApplyAddressAndLabel(entry, id, guid);
        touched.Add(entry.parentGroup);
        return change;
    }

    private void ApplyAddressAndLabel(AddressableAssetEntry entry, string id, string guid)
    {
        if (entry.address != id)
        {
            RemoveUser(entry.address, guid);
            entry.SetAddress(id, false);
            AddUser(id, guid, entry.AssetPath);
        }
        if (entry.labels.Contains(Label) == false)
            entry.SetLabel(Label, true, true, false); // force: label이 설정에 없으면 만든다
    }

    /// <summary>
    /// 맞춘 뒤 한 번: 수정 이벤트를 한 번 내고(Groups 창 · 다른 색인이 따라온다), 바뀐 그룹 파일과 설정 파일만 디스크에 쓴다.
    /// 프로젝트 전체 저장(SaveAssets)은 하지 않는다 — 저장하지 않은 다른 변경을 함께 쓰지 않도록.
    /// </summary>
    public static void Commit(HashSet<AddressableAssetGroup> touched)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || touched.Count == 0)
            return;

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, false);
        foreach (AddressableAssetGroup group in touched)
        {
            if (group != null)
                AssetDatabase.SaveAssetIfDirty(group);
        }
        AssetDatabase.SaveAssetIfDirty(settings); // label을 처음 만들었을 때만 바뀌어 있다
    }
    #endregion
}