using System;
using System.Collections.Generic;

/// <summary>Addressable에서 읽은, 문서 파일 하나의 등록 사실. JsonAddressBook이 채우고 JsonAddressRule이 판정한다.</summary>
public readonly struct JsonAddressFacts
{
    public readonly bool Registered;      // 이 파일을 직접 등록한 항목이 있다 (폴더째 등록한 곳 안은 아니다)
    public readonly string Address;       // 직접 등록한 항목의 주소. 없으면 null
    public readonly bool HasLabel;        // 직접 등록한 항목에 PreLoad label이 있다
    public readonly string FolderAddress; // 직접 등록은 없고 폴더째 등록한 곳 안일 때 이 파일의 주소. 아니면 null
    public readonly string OtherUser;     // 문서 Id를 주소로 쓰는 다른 에셋의 경로 (하나). 없으면 null

    public JsonAddressFacts(bool registered, string address, bool hasLabel, string folderAddress, string otherUser)
    {
        Registered = registered;
        Address = address;
        HasLabel = hasLabel;
        FolderAddress = folderAddress;
        OtherUser = otherUser;
    }
}

/// <summary>문서 하나의 등록 판정 결과.</summary>
public enum EJsonAddressState
{
    Ok = 0,   // 게임이 찾는다
    Fixable,  // 툴이 맞출 수 있다 — 등록 · 주소 · label
    Blocked,  // 사람이 정해야 한다 — 주소 겹침 · 폴더째 등록
}

/// <summary>
/// 판정 문장의 낱말 — 부르는 툴이 정한다 (문서 이름 · 조사가 붙은 꼴 · 게임이 못 찾을 때 일어나는 일).
/// Id 이름 ("Set Id" · "트리 Id")은 뒤에 '가 · 나 · 로 · 와'가 붙는다.
/// </summary>
public readonly struct JsonAddressWords
{
    public readonly string IdName;        // "Set Id"
    public readonly string Noun;          // "Set" — "Set마다"
    public readonly string ThisOr;        // "이 Set이나"
    public readonly string ThisObject;    // "이 Set을"
    public readonly string NounObject;    // "Set을"
    public readonly string MissingEffect; // "상호작용 없이 동작" — 등록 안 된 문서를 게임이 찾지 못할 때

    public JsonAddressWords(string idName, string noun, string thisOr, string thisObject, string nounObject, string missingEffect)
    {
        IdName = idName;
        Noun = noun;
        ThisOr = thisOr;
        ThisObject = thisObject;
        NounObject = nounObject;
        MissingEffect = missingEffect;
    }
}

/// <summary>
/// 에디터 툴 공용 — 문서 파일의 Addressable 등록 판정 (순수 로직, 테스트 가능).
/// 게임은 문서를 주소 = 문서 Id, PreLoad label로 찾는다 (Interaction Set: InteractionManager.GetSet · 트리: BehaviorManager.GetDefinition → ResourceManager.Load — 미리 읽은 것만, 열쇠는 주소).
/// 문장은 무엇이 틀렸는지만 쓴다 — 고치는 방법(저장 · 버튼)은 부르는 쪽이 붙인다. 낱말은 부르는 쪽이 준다 (JsonAddressWords).
/// </summary>
public static class JsonAddressRule
{
    /// <summary>
    /// 게임이 이 문서를 찾는가. 고칠 것은 problems에 문장으로 더한다.
    /// 확인 순서: 주소 겹침 → 폴더째 등록 → 등록 · 주소 · label. 겹침과 폴더째 등록은 툴이 고치지 않는다 (고칠 쪽을 사람이 정해야 하고, 툴이 바꾸면 다른 에셋이 깨질 수 있다).
    /// pendingSave = 고쳤거나 새로 만들어 저장 전인 문서 — 저장할 때 맞추므로 주소 겹침만 본다 (겹치면 저장해도 등록하지 않으므로 바로 알린다).
    /// O(1)
    /// </summary>
    public static EJsonAddressState Judge(in JsonAddressFacts facts, string id, string label, bool pendingSave, in JsonAddressWords words, List<string> problems)
    {
        if (string.IsNullOrEmpty(id))
            return EJsonAddressState.Ok; // 빈 Id는 로드 검사가 에러로 알린다

        if (facts.OtherUser != null)
        {
            problems.Add(pendingSave
                ? $"{words.IdName}가 다른 에셋의 Addressable 주소와 같음 ('{id}' — {facts.OtherUser}). 저장해도 등록하지 않음 — 게임은 같은 주소의 에셋을 하나만 보관해 {words.ThisOr} 그 에셋을 못 찾을 수 있음. {words.IdName}나 그 에셋의 주소를 바꿀 것"
                : $"Addressable 주소가 다른 에셋과 겹침 ('{id}' — {facts.OtherUser}). 게임은 같은 주소의 에셋을 하나만 보관해 {words.ThisOr} 그 에셋을 못 찾을 수 있음. {words.IdName}나 그 에셋의 주소를 바꿀 것");
            return EJsonAddressState.Blocked;
        }
        if (pendingSave)
            return EJsonAddressState.Ok; // 나머지는 저장할 때 맞춘다

        if (facts.Registered == false)
        {
            if (facts.FolderAddress != null)
            {
                problems.Add($"폴더째 등록된 곳 안에 있음 (이 파일의 주소 '{facts.FolderAddress}') — 게임은 {words.IdName}로 찾으므로 못 찾음. 그 폴더의 등록을 풀면 {words.Noun}마다 등록할 수 있음");
                return EJsonAddressState.Blocked;
            }
            problems.Add($"Addressable에 등록 안 됨 — 게임이 {words.ThisObject} 찾지 못함 ({words.MissingEffect})");
            return EJsonAddressState.Fixable;
        }

        bool fixable = false;
        if (facts.Address != id) // 주소는 대소문자까지 같아야 한다 (ResourceManager 사전)
        {
            problems.Add($"Addressable 주소 '{facts.Address}' — {words.IdName}와 달라 게임이 찾지 못함");
            fixable = true;
        }
        if (facts.HasLabel == false)
        {
            problems.Add($"'{label}' label 없음 — 게임은 미리 읽은 것에서만 {words.NounObject} 찾음");
            fixable = true;
        }
        return fixable ? EJsonAddressState.Fixable : EJsonAddressState.Ok;
    }

    /// <summary>
    /// 새로 등록할 그룹 이름 — 다른 문서가 가장 많이 든 그룹. 같은 수면 이름순(Ordinal)으로 앞. 하나도 없으면 null (= 기본 그룹).
    /// 읽기 전용 그룹은 부르는 쪽이 넣지 않는다. O(n)
    /// </summary>
    public static string PickGroup(IReadOnlyList<string> groupsOfOtherDocuments)
    {
        if (groupsOfOtherDocuments == null)
            return null;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string best = null;
        int bestCount = 0;
        for (int i = 0; i < groupsOfOtherDocuments.Count; i++)
        {
            string name = groupsOfOtherDocuments[i];
            if (string.IsNullOrEmpty(name))
                continue;

            counts.TryGetValue(name, out int count);
            count++;
            counts[name] = count;

            // 수가 같아지는 순간마다 이름을 비교하므로, 끝나면 가장 많은 그룹 중 이름순으로 앞이 남는다
            if (count > bestCount || (count == bestCount && string.CompareOrdinal(name, best) < 0))
            {
                best = name;
                bestCount = count;
            }
        }
        return best;
    }
}