using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LargeFolderFinder.LocalizationCheck.Model;

/// <summary>
/// 1つの言語ファイルの読み込み結果を表す不変のデータ型。
/// </summary>
/// <remarks>
/// 入出力の層（LanguageFileReader）が作り、判定の層（CoverageChecker）が読む。
/// 判定の層が入出力の層に依存しないよう、Model に置く（design.md: Model / LanguageFileContent）。
/// </remarks>
public sealed class LanguageFileContent
{
    /// <summary>言語ファイルの名前（例: "de.yaml"）。</summary>
    public string FileName { get; }

    /// <summary>キーと訳文の対。読み込めなかったときは null。</summary>
    public IReadOnlyDictionary<string, string>? Entries { get; }

    /// <summary>
    /// 読み込めなかった理由（例外の型名とメッセージ、または「中身が空」）。読み込めたときは null。
    /// </summary>
    public string? UnreadableReason { get; }

    /// <summary>1つのファイルに2回以上現れたキー（出現順、重複なし）。無いときは空。</summary>
    public IReadOnlyList<string> DuplicateKeys { get; }

    /// <param name="fileName">言語ファイルの名前。空にはできない。</param>
    /// <param name="entries">キーと訳文の対。読み込めなかったときは null。</param>
    /// <param name="unreadableReason">読み込めなかった理由。読み込めたときは null。</param>
    /// <param name="duplicateKeys">2回以上現れたキー。無いときは null または空。</param>
    /// <exception cref="ArgumentException">
    /// ファイル名が空のとき、または読み込みの成否と理由の有無が食い違うとき。
    /// </exception>
    public LanguageFileContent(
        string fileName,
        IReadOnlyDictionary<string, string>? entries,
        string? unreadableReason,
        IReadOnlyList<string>? duplicateKeys)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("言語ファイルの名前が空です。", nameof(fileName));
        }

        // 「読み込めた（Entries がある）」と「読み込めなかった（理由がある）」は排他とし、
        // 判定の層がどちらか一方だけを見れば済むようにする。
        if (entries == null && string.IsNullOrEmpty(unreadableReason))
        {
            throw new ArgumentException("読み込めなかった場合は理由が必要です。", nameof(unreadableReason));
        }

        if (entries != null && !string.IsNullOrEmpty(unreadableReason))
        {
            throw new ArgumentException("読み込めた場合に読み込み不能の理由は指定できません。", nameof(unreadableReason));
        }

        FileName = fileName;
        // 渡された内容を複製して保持し、生成後に外部から変えられないようにする。
        Entries = entries == null
            ? null
            : new ReadOnlyDictionary<string, string>(CopyOf(entries));
        UnreadableReason = unreadableReason;
        DuplicateKeys = duplicateKeys == null
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : new List<string>(duplicateKeys).AsReadOnly();
    }

    /// <summary>
    /// 読み取り専用の辞書を、複製できる形の辞書へ写し取る。
    /// </summary>
    private static Dictionary<string, string> CopyOf(IReadOnlyDictionary<string, string> source)
    {
        var copy = new Dictionary<string, string>(source.Count);
        foreach (var pair in source)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }
}
