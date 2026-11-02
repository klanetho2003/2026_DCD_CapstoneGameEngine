using System;

/// <summary>
/// 에디터 툴 공용 — 창을 다시 만들 때 넘긴 편집의 '담은 때 표식' "{실행}:{횟수}". 순수 로직, 테스트 가능.
/// 실행 번호는 편집기 실행마다 하나, 횟수는 창마다 하나 — 그 창의 사본이 바뀔 수 있는 일(값 바꿈 · 고른 파일 바꿈 · Undo · 저장 · 다시 읽기 · 버리기 등)마다 오른다
/// (창이 SessionState에 둔다 — InteractionEditorWindow.Save.cs · StateDefinitionEditorWindow.Save.cs, 키는 창마다).
/// 다시 만든 창은 담은 때 표식이 지금 표식과 같을 때만 입힌다 — 레이아웃 파일(끌 때 · 이름 붙인 레이아웃)에 담긴 지난 편집이 되살아나지 않게.
/// </summary>
public static class JsonUnsavedStamp
{
    /// <summary>담아 둔 편집을 다시 만든 창이 어떻게 다룰까.</summary>
    public enum EMatch
    {
        Apply = 0, // 입힌다 — 표식이 같거나, 표식을 쓰기 전의 코드가 리로드 직전에 담은 것(빈 표식)
        Drop,      // 말없이 버린다 — 같은 편집기 실행 안의 것인데, 담은 뒤 그 창의 횟수가 올랐다
        Skip,      // 입히지 않고 내용을 남긴다 — 지난 편집기 실행의 것, 또는 지금 실행 번호를 아직 모름
    }

    /// <summary>"{실행}:{횟수}"</summary>
    public static string Make(string session, int generation)
    {
        return $"{session}:{generation}";
    }

    /// <summary>담은 때 표식(captured)을 지금 실행 번호 · 그 창의 횟수와 견준다. O(표식 길이)</summary>
    public static EMatch Match(string captured, string session, int generation)
    {
        if (string.IsNullOrEmpty(captured))
            return EMatch.Apply; // 표식을 쓰기 전의 코드는 리로드 직전에만 담았다
        if (string.IsNullOrEmpty(session))
            return EMatch.Skip;
        if (captured == Make(session, generation))
            return EMatch.Apply;
        return captured.StartsWith(session + ":", StringComparison.Ordinal) ? EMatch.Drop : EMatch.Skip;
    }
}