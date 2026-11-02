using System;
using System.Collections.Generic;

/// <summary>
/// 변형 편집 (편집 사본의 트리) — 더하기 · 복제 · 지우기 · 이름 바꾸기 · 키 기본값 바꾸기 · 기반 값으로 (값 지우기) · Blackboard 키 이름 바꾸기 · 지우기를 따라가기.
/// 이름 규칙 = BehaviorTreeVariants.NameError + 다른 변형과 겹치지 않음 (대소문자를 가리지 않는다), 값 규칙 = BehaviorTreeVariants.ValueError — 게임 검사와 같다.
/// Undo 기록은 부르는 쪽이 한다.
/// </summary>
public static class BehaviorVariantEditing
{
    public const string NewVariantName = "Variant";

    /// <summary>변형 값 자리의 칸 이름 앞 — 검사 줄 · 패널의 키 줄이 "blackboard.키 이름"으로 찾는다 (변형 이름 칸 "name"과 겹치지 않게)</summary>
    public const string ValueFieldPrefix = "blackboard.";

    /// <summary>변형 값 자리의 칸 이름 — "blackboard.키 이름"</summary>
    public static string ValueField(string key)
    {
        return ValueFieldPrefix + (key ?? "");
    }

    /// <summary>쓰이지 않은 변형 이름 — stem, stem1, stem2 … 의 첫 이름 (대소문자를 가리지 않고 겹치지 않게). O(시도 수 × 변형 수)</summary>
    public static string NextName(BehaviorTreeAsset tree, string stem = NewVariantName)
    {
        if (string.IsNullOrEmpty(stem) || BehaviorTreeVariants.NameError(stem) != null)
            stem = NewVariantName;
        string name = stem;
        for (int number = 1; IsTaken(tree, name, -1); number++)
            name = stem + number;
        return name;
    }

    /// <summary>빈 변형 (바꾼 값 없음)을 목록 끝에 더한다 — 이름 NextName. 더한 자리</summary>
    public static int Add(BehaviorTreeAsset tree)
    {
        return Insert(tree, Variants(tree).Length, new BehaviorTreeVariant { Name = NextName(tree) });
    }

    /// <summary>변형을 복사해 바로 다음 자리에 더한다 — 같은 값, 이름은 NextName(원래 이름). 더한 자리 (없는 자리면 -1)</summary>
    public static int Duplicate(BehaviorTreeAsset tree, int index)
    {
        BehaviorTreeVariant source = At(tree, index);
        if (source == null)
            return -1;
        var copy = new BehaviorTreeVariant
        {
            Name = NextName(tree, source.Name),
            Overrides = (BehaviorKeyOverride[])(source.Overrides ?? Array.Empty<BehaviorKeyOverride>()).Clone(),
        };
        return Insert(tree, index + 1, copy);
    }

    /// <summary>변형 하나를 지운다. 없는 자리면 false</summary>
    public static bool Remove(BehaviorTreeAsset tree, int index)
    {
        BehaviorTreeVariant[] variants = Variants(tree);
        if (index < 0 || index >= variants.Length)
            return false;
        var left = new BehaviorTreeVariant[variants.Length - 1];
        Array.Copy(variants, 0, left, 0, index);
        Array.Copy(variants, index + 1, left, index, variants.Length - index - 1);
        tree.Variants = left;
        return true;
    }

    /// <summary>그 자리의 변형 이름으로 쓸 수 없는 까닭 — 규칙 (BehaviorTreeVariants.NameError) · 다른 변형과 겹침 (대소문자를 가리지 않는다). 쓸 수 있으면 null</summary>
    public static string NameError(BehaviorTreeAsset tree, int index, string name)
    {
        string error = BehaviorTreeVariants.NameError(name);
        if (error != null)
            return error;
        return IsTaken(tree, name, index) ? $"'{name}' 변형이 이미 있습니다 (대소문자를 가리지 않는다)" : null;
    }

    /// <summary>변형 이름을 바꾼다 — 쓸 수 없으면 바꾸지 않고 까닭, 바꿨거나 같은 이름이면 null. 그 변형을 가리키던 몬스터 데이터는 바꾸지 않는다</summary>
    public static string Rename(BehaviorTreeAsset tree, int index, string name)
    {
        BehaviorTreeVariant variant = At(tree, index);
        if (variant == null)
            return "변형을 찾을 수 없습니다";
        if (string.Equals(variant.Name, name, StringComparison.Ordinal))
            return null;
        string error = NameError(tree, index, name);
        if (error != null)
            return error;
        variant.Name = name;
        return null;
    }

    /// <summary>변형이 그 키의 기본값을 바꾼 값의 자리 (적힌 차례의 첫 값). 없으면 -1</summary>
    public static int IndexOfValue(BehaviorTreeVariant variant, string key)
    {
        BehaviorKeyOverride[] values = variant?.Overrides;
        if (values == null || string.IsNullOrEmpty(key))
            return -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i].Key, key, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>변형의 키 기본값으로 쓸 수 없는 까닭 — 없는 변형 · 트리에 없는 키 (목록 차례의 첫 키로 본다) · BehaviorTreeVariants.ValueError. 쓸 수 있으면 null. O(키 수)</summary>
    public static string SetValueError(BehaviorTreeAsset tree, int index, BehaviorKeyOverride value)
    {
        if (At(tree, index) == null)
            return "변형을 찾을 수 없습니다";
        BehaviorBlackboardKey key = BehaviorBlackboardEditing.FindKey(tree, value.Key);
        return key == null ? $"Blackboard에 없는 키 '{value.Key}'" : BehaviorTreeVariants.ValueError(key, value);
    }

    /// <summary>
    /// 변형의 키 기본값을 바꾼다 — 이미 바꾼 키면 그 값을 (적힌 차례 그대로), 아니면 끝에 더한다. 쓸 수 없으면 (SetValueError) 바꾸지 않고 까닭, 바꿨으면 null. O(키 수 + 값 수)
    /// </summary>
    public static string SetValue(BehaviorTreeAsset tree, int index, BehaviorKeyOverride value)
    {
        string error = SetValueError(tree, index, value);
        if (error != null)
            return error;

        BehaviorTreeVariant variant = At(tree, index);
        BehaviorKeyOverride[] values = variant.Overrides ?? Array.Empty<BehaviorKeyOverride>();
        int at = IndexOfValue(variant, value.Key);
        if (at >= 0)
        {
            values[at] = value;
            variant.Overrides = values;
            return null;
        }
        var added = new BehaviorKeyOverride[values.Length + 1];
        Array.Copy(values, added, values.Length);
        added[values.Length] = value;
        variant.Overrides = added;
        return null;
    }

    /// <summary>변형이 바꾼 그 키의 값을 모두 지운다 (기반 값으로). 지웠으면 true</summary>
    public static bool ClearValue(BehaviorTreeAsset tree, int index, string key)
    {
        BehaviorTreeVariant variant = At(tree, index);
        return variant != null && RemoveValues(variant, value => string.Equals(value.Key, key, StringComparison.Ordinal)) > 0;
    }

    /// <summary>변형의 valueIndex번째 값을 지운다 (트리에 없는 키 · 같은 키 두 번 등 — 키 줄이 없는 값). 지웠으면 true</summary>
    public static bool RemoveValueAt(BehaviorTreeAsset tree, int index, int valueIndex)
    {
        BehaviorTreeVariant variant = At(tree, index);
        BehaviorKeyOverride[] values = variant?.Overrides;
        if (values == null || valueIndex < 0 || valueIndex >= values.Length)
            return false;
        var left = new BehaviorKeyOverride[values.Length - 1];
        Array.Copy(values, 0, left, 0, valueIndex);
        Array.Copy(values, valueIndex + 1, left, valueIndex, values.Length - valueIndex - 1);
        variant.Overrides = left;
        return true;
    }

    /// <summary>
    /// Blackboard 키 이름 바꾸기를 변형 값이 따라갈 때 막는 까닭 — 옛 이름과 새 이름의 값을 함께 가진 변형이 있으면 (새 이름의 값은 키 줄이 없는 값) 같은 키의 값이 둘이 된다.
    /// 막지 않으면 null. O(변형 수 × 값 수)
    /// </summary>
    public static string RenameKeyError(BehaviorTreeAsset tree, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName))
            return null;
        BehaviorTreeVariant[] variants = Variants(tree);
        for (int v = 0; v < variants.Length; v++)
        {
            if (IndexOfValue(variants[v], oldName) >= 0 && IndexOfValue(variants[v], newName) >= 0)
                return $"변형 '{variants[v].Name}'에 '{newName}' 값이 이미 있습니다 (키 줄이 없는 값) — 이름을 바꾸면 그 변형에 같은 키의 값이 둘이 됨: [변형] 탭에서 먼저 지울 것";
        }
        return null;
    }

    /// <summary>모든 변형에서 그 키를 바꾼 값의 키 이름을 바꾼다 (Blackboard 키 이름 바꾸기를 따라간다). 바꾼 값 수. O(변형 수 × 값 수)</summary>
    public static int RenameKey(BehaviorTreeAsset tree, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName))
            return 0;
        int renamed = 0;
        BehaviorTreeVariant[] variants = Variants(tree);
        for (int v = 0; v < variants.Length; v++)
        {
            BehaviorKeyOverride[] values = variants[v]?.Overrides;
            if (values == null)
                continue;
            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i].Key, oldName, StringComparison.Ordinal))
                {
                    values[i].Key = newName;
                    renamed++;
                }
            }
        }
        return renamed;
    }

    /// <summary>모든 변형에서 그 키를 바꾼 값을 지운다 (Blackboard 키 지우기를 따라간다). 지운 값 수. O(변형 수 × 값 수)</summary>
    public static int RemoveKey(BehaviorTreeAsset tree, string name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;
        int removed = 0;
        BehaviorTreeVariant[] variants = Variants(tree);
        for (int v = 0; v < variants.Length; v++)
        {
            if (variants[v] != null)
                removed += RemoveValues(variants[v], value => string.Equals(value.Key, name, StringComparison.Ordinal));
        }
        return removed;
    }

    /// <summary>
    /// 키 줄로 보이지 않는 값의 자리 — 빈 키 이름 · 트리에 없는 키 · 같은 키의 두 번째부터 (패널이 따로 보이고 지울 수 있게). into는 먼저 비운다. O(값 수 × (값 수 + 키 수))
    /// </summary>
    public static void CollectStrayValues(BehaviorTreeAsset tree, BehaviorTreeVariant variant, List<int> into)
    {
        into.Clear();
        BehaviorKeyOverride[] values = variant?.Overrides;
        if (values == null)
            return;
        for (int i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrEmpty(values[i].Key) || BehaviorBlackboardEditing.FindKey(tree, values[i].Key) == null || IndexOfValue(variant, values[i].Key) != i)
                into.Add(i);
        }
    }

    /// <summary>그 자리의 변형 — 없으면 null</summary>
    public static BehaviorTreeVariant At(BehaviorTreeAsset tree, int index)
    {
        BehaviorTreeVariant[] variants = Variants(tree);
        return index >= 0 && index < variants.Length ? variants[index] : null;
    }

    private static BehaviorTreeVariant[] Variants(BehaviorTreeAsset tree)
    {
        return tree.Variants ?? Array.Empty<BehaviorTreeVariant>();
    }

    /// <summary>다른 변형 (except 자리 빼고)이 그 이름을 쓰는가 — 대소문자를 가리지 않는다</summary>
    private static bool IsTaken(BehaviorTreeAsset tree, string name, int except)
    {
        BehaviorTreeVariant[] variants = Variants(tree);
        for (int i = 0; i < variants.Length; i++)
        {
            if (i != except && variants[i] != null && string.Equals(variants[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>변형을 그 자리에 넣는다 (배열을 새로). 넣은 자리</summary>
    private static int Insert(BehaviorTreeAsset tree, int index, BehaviorTreeVariant variant)
    {
        BehaviorTreeVariant[] variants = Variants(tree);
        index = Math.Max(0, Math.Min(index, variants.Length));
        var added = new BehaviorTreeVariant[variants.Length + 1];
        Array.Copy(variants, 0, added, 0, index);
        added[index] = variant;
        Array.Copy(variants, index, added, index + 1, variants.Length - index);
        tree.Variants = added;
        return index;
    }

    /// <summary>조건에 맞는 값을 지운다 (배열을 새로 — 맞는 값이 없으면 그대로). 지운 수</summary>
    private static int RemoveValues(BehaviorTreeVariant variant, Predicate<BehaviorKeyOverride> match)
    {
        BehaviorKeyOverride[] values = variant.Overrides;
        if (values == null)
            return 0;
        var left = new List<BehaviorKeyOverride>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (match(values[i]) == false)
                left.Add(values[i]);
        }
        if (left.Count == values.Length)
            return 0;
        variant.Overrides = left.ToArray();
        return values.Length - left.Count;
    }
}