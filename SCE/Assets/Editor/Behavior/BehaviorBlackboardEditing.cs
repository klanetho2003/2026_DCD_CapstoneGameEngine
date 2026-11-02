using System;
using System.Collections.Generic;

/// <summary>
/// Blackboard 키 편집 (편집 사본의 트리) — 더하기 · 지우기 · 차례 · 이름 바꾸기 (노드가 쓰는 그 이름 · 변형이 바꾼 그 키의 값도 함께) · 키를 쓰는 노드 찾기 ·
/// 붙이기 (노드가 고른 키 이름 모으기 · 붙일 트리에 없는 키 정의 더하기) · 값 또는 키 칸을 키로 빼기.
/// 키 이름 규칙은 컴파일과 같다 (비지 않음 · 앞뒤 공백 없음 · 겹치지 않음). Undo 기록은 부르는 쪽이 한다.
/// </summary>
public static class BehaviorBlackboardEditing
{
    public const string NewKeyName = "NewKey";

    /// <summary>키를 쓰는 노드의 칸 하나 — 노드와 칸 (JSON 키)</summary>
    public readonly struct KeyUser
    {
        public readonly BehaviorNode Node;
        public readonly string Field;

        public KeyUser(BehaviorNode node, string field)
        {
            Node = node;
            Field = field;
        }
    }

    /// <summary>쓰이지 않은 새 키 이름 — NewKey, NewKey1, NewKey2 … 의 첫 이름. O(시도 수 × 키 수)</summary>
    public static string NextKeyName(BehaviorTreeAsset tree)
    {
        return NextKeyName(tree, NewKeyName);
    }

    /// <summary>쓰이지 않은 키 이름 — stem, stem1, stem2 … 의 첫 이름 (쓸 수 없는 stem이면 NewKey부터). O(시도 수 × 키 수)</summary>
    public static string NextKeyName(BehaviorTreeAsset tree, string stem)
    {
        if (string.IsNullOrEmpty(stem) || stem.Trim() != stem)
            stem = NewKeyName;
        string name = stem;
        for (int number = 1; IndexOf(tree.Keys, name) >= 0; number++)
            name = stem + number;
        return name;
    }

    /// <summary>그 형식의 키를 목록 끝에 더한다 (기본값 · 출처 없음). 더한 자리를 반환한다</summary>
    public static int AddKey(BehaviorTreeAsset tree, EBehaviorKeyType type)
    {
        return Append(tree, new BehaviorBlackboardKey { Name = NextKeyName(tree), Type = type });
    }

    /// <summary>그 이름의 키 정의 (목록 차례의 첫 키). 없거나 빈 이름이면 null</summary>
    public static BehaviorBlackboardKey FindKey(BehaviorTreeAsset tree, string name)
    {
        int index = string.IsNullOrEmpty(name) ? -1 : IndexOf(tree.Keys, name);
        return index >= 0 ? tree.Keys[index] : null;
    }

    /// <summary>
    /// 들어온 키 정의 (붙이기 — 다른 트리에서 복사한 키) 가운데 이 트리에 없는 이름만 목록 끝에 더한다 — 정의 그대로 (형식 · 기본값 · 처음 값 출처, 새 객체).
    /// 같은 이름의 키가 이미 있으면 더하지 않는다 — 형식이 다르면 clashed에 (노드 칸은 이 트리의 키를 가리키고 검사가 에러로 알린다).
    /// 빈 이름은 빼고, 들어온 목록 안에서 겹친 이름은 앞의 것만 본다. added · clashed는 먼저 비운다. O(들어온 키 수 × 키 수)
    /// </summary>
    public static void AddMissingKeys(BehaviorTreeAsset tree, IReadOnlyList<BehaviorBlackboardKey> keys, List<string> added, List<string> clashed)
    {
        added.Clear();
        clashed.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            BehaviorBlackboardKey key = keys[i];
            if (key == null || string.IsNullOrEmpty(key.Name) || seen.Add(key.Name) == false)
                continue;
            BehaviorBlackboardKey existing = FindKey(tree, key.Name);
            if (existing != null)
            {
                if (existing.Type != key.Type)
                    clashed.Add(key.Name);
                continue;
            }
            Append(tree, new BehaviorBlackboardKey
            {
                Name = key.Name,
                Type = key.Type,
                DefaultBool = key.DefaultBool,
                DefaultInt = key.DefaultInt,
                DefaultFloat = key.DefaultFloat,
                DefaultVector2 = key.DefaultVector2,
                Seed = key.Seed ?? "",
            });
            added.Add(key.Name);
        }
    }

    /// <summary>노드들이 고른 키 이름 (키 고르기 · 값 또는 키 — 고르지 않은 칸은 빼고) — 처음 나온 차례, 겹침 없이. names는 먼저 비운다. O(노드 수 × 칸 수)</summary>
    public static void CollectKeyNames(IReadOnlyList<BehaviorNode> nodes, List<string> names)
    {
        names.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int n = 0; n < nodes.Count; n++)
        {
            if (nodes[n] == null)
                continue;
            IReadOnlyList<BehaviorNodeFields.Field> fields = BehaviorNodeFields.Of(nodes[n].GetType());
            for (int f = 0; f < fields.Count; f++)
            {
                if (BehaviorNodeFields.IsKeyField(fields[f].Info.FieldType) == false)
                    continue;
                string name = BehaviorNodeFields.GetKeyName(nodes[n], fields[f].Info);
                if (string.IsNullOrEmpty(name) == false && seen.Add(name))
                    names.Add(name);
            }
        }
    }

    /// <summary>키를 목록 끝에 더한다 (배열을 새로). 더한 자리를 반환한다</summary>
    private static int Append(BehaviorTreeAsset tree, BehaviorBlackboardKey key)
    {
        BehaviorBlackboardKey[] keys = tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        var added = new BehaviorBlackboardKey[keys.Length + 1];
        Array.Copy(keys, added, keys.Length);
        added[keys.Length] = key;
        tree.Keys = added;
        return keys.Length;
    }

    /// <summary>키 하나를 지운다 — 노드는 그대로 (그 키를 쓰는 칸은 검사가 에러로 알린다). 자리가 없으면 false</summary>
    public static bool RemoveKey(BehaviorTreeAsset tree, int index)
    {
        return RemoveKey(tree, index, out _);
    }

    /// <summary>
    /// 키 하나를 지운다 — 노드는 그대로 (그 키를 쓰는 칸은 검사가 에러로 알린다), 변형이 바꾼 그 키의 값은 지운다 (같은 이름의 다른 키가 남아 있으면 그대로 — 그 키의 값).
    /// variantValues = 지운 변형 값 수. 자리가 없으면 false
    /// </summary>
    public static bool RemoveKey(BehaviorTreeAsset tree, int index, out int variantValues)
    {
        variantValues = 0;
        BehaviorBlackboardKey[] keys = tree.Keys;
        if (keys == null || index < 0 || index >= keys.Length)
            return false;
        string name = keys[index]?.Name;
        var left = new BehaviorBlackboardKey[keys.Length - 1];
        Array.Copy(keys, 0, left, 0, index);
        Array.Copy(keys, index + 1, left, index, keys.Length - index - 1);
        tree.Keys = left;
        if (string.IsNullOrEmpty(name) == false && IndexOf(left, name) < 0)
            variantValues = BehaviorVariantEditing.RemoveKey(tree, name);
        return true;
    }

    /// <summary>키를 delta 칸 옮긴다 (−1 위로 · +1 아래로, 사이의 키는 한 칸씩 밀린다). 없는 자리 · 목록 끝을 넘으면 false — 칸 번호는 컴파일이 차례대로 다시 정한다</summary>
    public static bool MoveKey(BehaviorTreeAsset tree, int index, int delta)
    {
        BehaviorBlackboardKey[] keys = tree.Keys;
        if (keys == null || index < 0 || index >= keys.Length)
            return false;
        return BehaviorTreeEditing.TryMove(ref tree.Keys, keys[index], delta);
    }

    /// <summary>그 자리의 키 이름으로 쓸 수 없는 까닭 (컴파일 규칙 — 비었거나 앞뒤 공백 · 다른 키와 겹침). 쓸 수 있으면 null</summary>
    public static string NameError(BehaviorTreeAsset tree, int index, string name)
    {
        if (string.IsNullOrEmpty(name) || name.Trim() != name)
            return "키 이름은 비거나 앞뒤에 공백이 있으면 안 됩니다";
        BehaviorBlackboardKey[] keys = tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        for (int i = 0; i < keys.Length; i++)
        {
            if (i != index && keys[i] != null && string.Equals(keys[i].Name, name, StringComparison.Ordinal))
                return $"'{name}' 키가 이미 있습니다";
        }
        return null;
    }

    /// <summary>
    /// 그 자리의 키 이름을 name으로 바꿀 수 없는 까닭 — NameError, 그리고 변형 값이 따라갈 때 (옛 이름의 다른 키가 없을 때) 같은 키의 값이 둘이 되는 변형 (BehaviorVariantEditing.RenameKeyError).
    /// 바꿀 수 있거나 같은 이름이면 null. O(키 수 + 변형 수 × 값 수)
    /// </summary>
    public static string RenameError(BehaviorTreeAsset tree, int index, string name)
    {
        BehaviorBlackboardKey[] keys = tree.Keys ?? Array.Empty<BehaviorBlackboardKey>();
        string old = index >= 0 && index < keys.Length ? keys[index]?.Name : null;
        if (string.Equals(old, name, StringComparison.Ordinal))
            return null;
        string error = NameError(tree, index, name);
        if (error != null || string.IsNullOrEmpty(old))
            return error;
        for (int i = 0; i < keys.Length; i++)
        {
            if (i != index && keys[i] != null && string.Equals(keys[i].Name, old, StringComparison.Ordinal))
                return null; // 옛 이름의 다른 키가 남는다 — 변형 값은 그 키의 것
        }
        return BehaviorVariantEditing.RenameKeyError(tree, old, name);
    }

    /// <summary>
    /// 키 이름을 바꾼다 — 문서의 모든 노드 (이어지지 않은 노드 포함)에서 옛 이름을 고른 칸 (키 고르기 · 값 또는 키)도 새 이름으로 바꾼다.
    /// 옛 이름의 다른 키가 남아 있으면 (겹친 이름) 노드 칸은 그대로 둔다 — 그 칸은 남은 키를 가리킨다.
    /// 바꾼 노드 칸 수를 반환한다. 쓸 수 없는 이름이거나 자리가 없으면 -1과 까닭 (바꾸지 않음). 같은 이름이면 0. O(노드 수 × 칸 수)
    /// </summary>
    public static int RenameKey(BehaviorTreeDocumentAsset document, int index, string name, out string error)
    {
        return RenameKey(document, index, name, out error, out _);
    }

    /// <summary>
    /// RenameKey와 같고 변형이 바꾼 그 키의 값도 새 이름으로 — variantValues = 바꾼 변형 값 수 (노드 칸과 같은 규칙 — 옛 이름의 다른 키가 남아 있으면 그대로).
    /// 쓸 수 없는 이름은 RenameError (같은 키의 값이 둘이 되는 변형 포함). O(노드 수 × 칸 수 + 변형 값 수)
    /// </summary>
    public static int RenameKey(BehaviorTreeDocumentAsset document, int index, string name, out string error, out int variantValues)
    {
        variantValues = 0;
        BehaviorBlackboardKey[] keys = document.Tree.Keys;
        if (keys == null || index < 0 || index >= keys.Length || keys[index] == null)
        {
            error = "키를 찾을 수 없습니다";
            return -1;
        }
        string old = keys[index].Name;
        if (string.Equals(old, name, StringComparison.Ordinal))
        {
            error = null;
            return 0;
        }
        error = RenameError(document.Tree, index, name);
        if (error != null)
            return -1;

        keys[index].Name = name;
        if (string.IsNullOrEmpty(old) || IndexOf(keys, old) >= 0)
            return 0;

        variantValues = BehaviorVariantEditing.RenameKey(document.Tree, old, name);
        int renamed = 0;
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);
        for (int n = 0; n < nodes.Count; n++)
        {
            IReadOnlyList<BehaviorNodeFields.Field> fields = BehaviorNodeFields.Of(nodes[n].GetType());
            for (int f = 0; f < fields.Count; f++)
            {
                if (BehaviorNodeFields.IsKeyField(fields[f].Info.FieldType)
                    && string.Equals(BehaviorNodeFields.GetKeyName(nodes[n], fields[f].Info), old, StringComparison.Ordinal))
                {
                    BehaviorNodeFields.SetKeyName(nodes[n], fields[f].Info, name);
                    renamed++;
                }
            }
        }
        return renamed;
    }

    /// <summary>
    /// 노드의 값 또는 키 칸 (jsonKey)이 쓰는 값을 기본값으로 하는 새 키를 목록 끝에 더하고 그 칸이 키를 쓰게 한다 — 변형이 몬스터마다 그 값을 바꿀 수 있게. 동작은 같다 (몬스터는 그 기본값으로 시작).
    /// 키 이름 = 칸 이름의 첫 글자를 대문자로 (겹치면 뒤에 번호). 값 또는 키 칸이 아니거나 이미 키를 쓰면 -1과 까닭 (바꾸지 않음). 더한 키 자리. O(칸 수 + 시도 수 × 키 수)
    /// </summary>
    public static int PromoteToKey(BehaviorTreeAsset tree, BehaviorNode node, string jsonKey, out string keyName, out string error)
    {
        keyName = null;
        if (node == null || BehaviorNodeFields.TryGetField(node.GetType(), jsonKey, out BehaviorNodeFields.Field field) == false
            || BehaviorNodeFields.ValueKeyType(field.Info.FieldType) == 0)
        {
            error = "값 또는 키 칸이 아닙니다";
            return -1;
        }
        string used = BehaviorNodeFields.GetKeyName(node, field.Info);
        if (string.IsNullOrEmpty(used) == false)
        {
            error = $"이미 키 '{used}'를 씁니다";
            return -1;
        }

        var key = new BehaviorBlackboardKey();
        switch (field.Info.GetValue(node))
        {
            case BehaviorFloat value:
                key.Type = EBehaviorKeyType.Float;
                key.DefaultFloat = value.Value;
                break;
            case BehaviorInt value:
                key.Type = EBehaviorKeyType.Int;
                key.DefaultInt = value.Value;
                break;
            case BehaviorBool value:
                key.Type = EBehaviorKeyType.Bool;
                key.DefaultBool = value.Value;
                break;
            case BehaviorVector2 value:
                key.Type = EBehaviorKeyType.Vector2;
                key.DefaultVector2 = value.Value;
                break;
            default:
                error = "값 또는 키 칸이 아닙니다";
                return -1;
        }
        string stem = string.IsNullOrEmpty(jsonKey) ? NewKeyName : char.ToUpperInvariant(jsonKey[0]) + jsonKey.Substring(1);
        key.Name = keyName = NextKeyName(tree, stem);
        int index = Append(tree, key);
        BehaviorNodeFields.SetKeyName(node, field.Info, keyName);
        error = null;
        return index;
    }

    /// <summary>그 이름의 키를 고른 노드 칸 모두 (문서 차례 — 이어지지 않은 노드 포함). users는 먼저 비운다. O(노드 수 × 칸 수)</summary>
    public static void CollectUsers(BehaviorTreeDocumentAsset document, string keyName, List<KeyUser> users)
    {
        users.Clear();
        if (string.IsNullOrEmpty(keyName))
            return;
        var nodes = new List<BehaviorNode>();
        BehaviorTreeEditing.CollectAllNodes(document, nodes);
        for (int n = 0; n < nodes.Count; n++)
        {
            IReadOnlyList<BehaviorNodeFields.Field> fields = BehaviorNodeFields.Of(nodes[n].GetType());
            for (int f = 0; f < fields.Count; f++)
            {
                if (BehaviorNodeFields.IsKeyField(fields[f].Info.FieldType)
                    && string.Equals(BehaviorNodeFields.GetKeyName(nodes[n], fields[f].Info), keyName, StringComparison.Ordinal))
                    users.Add(new KeyUser(nodes[n], fields[f].JsonKey));
            }
        }
    }

    private static int IndexOf(BehaviorBlackboardKey[] keys, string name)
    {
        if (keys == null)
            return -1;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != null && string.Equals(keys[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}