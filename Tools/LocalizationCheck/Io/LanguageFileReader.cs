using System;
using System.Collections.Generic;
using System.IO;
using LargeFolderFinder.LocalizationCheck.Model;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace LargeFolderFinder.LocalizationCheck.Io;

/// <summary>
/// 1つの言語ファイルをアプリと同じ規則で読み込み、あわせて重複キーを数える部品
/// （design.md: Components and Interfaces / Io / LanguageFileReader）。
/// </summary>
/// <remarks>
/// <para>
/// 入出力の層に属し、入口（Program、SelfChecks）を参照しない
/// （design.md: Architecture Integration「依存の向き」）。
/// </para>
/// <para>
/// 読み込みの規則は <c>Services/LocalizationManager.cs</c> の <c>EnsureLoaded</c> に合わせている。
/// アプリの読み込みが変わったら、この部品も追従が必要になる
/// （research.md: Design Decisions「アプリと同じ読み込み規則を検証側で再現する」）。
/// </para>
/// <para>
/// ファイルは読み取り専用で開き、書き込みは一切行わない（requirements.md: 4.4）。
/// </para>
/// </remarks>
public static class LanguageFileReader
{
    /// <summary>「中身が空」で読み込めなかったことを表す理由の文言。</summary>
    private const string EmptyContentReason = "中身が空です（キーと訳文の対が読み取れませんでした）。";

    /// <summary>
    /// ファイルを読み取り専用で開き、読み込み結果を返す。
    /// 読み込みの失敗は結果の <see cref="LanguageFileContent.UnreadableReason"/> に表され、例外は外に出さない。
    /// </summary>
    /// <param name="filePath">言語ファイルのパス。</param>
    /// <returns>読み込み結果。読み込めなかった場合も理由を伴う結果として返る。</returns>
    /// <exception cref="ArgumentException">filePath が null または空の場合（呼び出し側の誤り）。</exception>
    public static LanguageFileContent Read(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("言語ファイルのパスが空です。", nameof(filePath));
        }

        // ファイル名が取り出せない形のパスでも、結果の識別子として何かしらの名前が要る。
        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = filePath;
        }

        string text;
        try
        {
            // FileAccess.Read で開き、他のプロセスからの読み取りも妨げない。書き込みは行わない。
            // StreamReader の既定（UTF-8、BOM による文字コードの判定あり）はアプリの
            // new StreamReader(filePath) と同じ。
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }
        }
        catch (Exception ex)
        {
            // ファイルが無い、権限が無い、読み取りに失敗した、などはすべて読み込み不能として扱う。
            return new LanguageFileContent(fileName, null, DescribeException(ex), null);
        }

        return Parse(fileName, text);
    }

    /// <summary>
    /// ファイル名と内容の文字列から読み込み結果を作る（自己検証とテスト用）。
    /// ファイルシステムには触れない。
    /// </summary>
    /// <param name="fileName">言語ファイルの名前（例: "de.yaml"）。</param>
    /// <param name="text">言語ファイルの内容。</param>
    /// <returns>読み込み結果。読み込めなかった場合も理由を伴う結果として返る。</returns>
    /// <exception cref="ArgumentException">fileName が null または空の場合（呼び出し側の誤り）。</exception>
    /// <exception cref="ArgumentNullException">text が null の場合（呼び出し側の誤り）。</exception>
    public static LanguageFileContent Parse(string fileName, string text)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("言語ファイルの名前が空です。", nameof(fileName));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        // 読み込みの可否と、重複キーの検出は別々に行う。
        // 辞書への読み込みでは重複キーが黙って上書きされるため、重複は検出できない
        // （design.md: Components and Interfaces / Io / LanguageFileReader「重複キー」）。
        var duplicateKeys = CountDuplicateKeys(text);

        Dictionary<string, string>? entries;
        try
        {
            entries = Deserialize(text);
        }
        catch (Exception ex)
        {
            // 構文の誤りは SyntaxErrorException、キーと訳文の対ではない構造は YamlException になる
            // （research.md「YamlDotNet の読み込みの挙動（実測）」）。
            // いずれもここで捕らえ、読み込み不能として外へは出さない（requirements.md: 3.5）。
            return new LanguageFileContent(fileName, null, DescribeException(ex), duplicateKeys);
        }

        if (entries == null)
        {
            // 空のファイルやコメントだけのファイルは、例外にならず null が返る。
            // アプリも同じ条件でその言語を失敗扱いにしている。
            return new LanguageFileContent(fileName, null, EmptyContentReason, duplicateKeys);
        }

        return new LanguageFileContent(fileName, entries, null, duplicateKeys);
    }

    /// <summary>
    /// アプリと同じ設定・同じ型でデシリアライズする
    /// （<c>Services/LocalizationManager.cs</c> の <c>EnsureLoaded</c> と同じ呼び出し）。
    /// </summary>
    /// <returns>キーと訳文の対。中身が空の場合は null。</returns>
    private static Dictionary<string, string>? Deserialize(string text)
    {
        using (var reader = new StringReader(text))
        {
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<Dictionary<string, string>>(reader);
        }
    }

    /// <summary>
    /// 最上位のマッピングのキーの出現回数を数え、2回以上現れたキーを出現順に返す
    /// （requirements.md: 3.10）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>YamlDotNet.Core.Parser</c> が出す事象の列をたどって数える
    /// （research.md: Design Decisions「重複キーは YAML の解析器の事象の列で検出する」）。
    /// 表現モデル（<c>YamlStream.Load</c>）は使えない。YamlDotNet 16.3.0 の
    /// <c>YamlStream.Load</c> は最上位に重複キーがあると
    /// <c>YamlException（"Duplicate key"）</c> を投げて文書全体の読み取りを中断し、
    /// 節点の並びからキーを数えることができないためである
    /// （research.md: Research Log「YamlDotNet の読み込みの挙動（実測）」）。
    /// </para>
    /// <para>
    /// 構文の誤りなどで事象の列を読めないときは、重複の検出を行わず空を返す。
    /// そのファイルは読み込み不能として報告されるため、重複を併せて報告する意味がない。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> CountDuplicateKeys(string text)
    {
        var duplicates = new List<string>();

        try
        {
            using (var reader = new StringReader(text))
            {
                var parser = new Parser(reader);

                parser.Consume<StreamStart>();
                if (!parser.TryConsume<DocumentStart>(out _))
                {
                    // 文書が1つも無い（空、またはコメントだけ）。
                    return duplicates;
                }

                if (!parser.TryConsume<MappingStart>(out _))
                {
                    // 最上位がマッピングでなければ、数える対象が無い。
                    return duplicates;
                }

                // 出現回数を出現順に保つため、数える辞書とは別に出現順の一覧を持つ。
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);

                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    if (!parser.TryConsume<Scalar>(out var key))
                    {
                        // キーがスカラーでない（複合キー）場合は数えず、キーと値をまとめて読み飛ばす。
                        parser.SkipThisAndNestedEvents();
                        parser.SkipThisAndNestedEvents();
                        continue;
                    }

                    // 値は最上位のキーの数え上げには関係しないため、入れ子ごと読み飛ばす。
                    parser.SkipThisAndNestedEvents();

                    counts.TryGetValue(key.Value, out var count);
                    counts[key.Value] = count + 1;

                    // 2回目に現れた時点で1回だけ記録する。3回以上現れても重複は1件にまとめる。
                    if (count == 1)
                    {
                        duplicates.Add(key.Value);
                    }
                }
            }
        }
        catch (Exception)
        {
            // 事象の列を読めない文書では重複の検出を行わない
            // （design.md: Components and Interfaces / Io / LanguageFileReader「重複キー」の同じ方針）。
            // 重複の数え上げは補助的な情報であり、ここで例外を外へ出すと、
            // 1本のファイルのために残りの言語の検証まで止まってしまう（requirements.md: 3.5、3.9）。
            return new List<string>();
        }

        return duplicates;
    }

    /// <summary>
    /// 例外を、原因を追えるだけの短い理由の文に直す。
    /// 型名を添えるのは、自己検証の失敗と同じく原因の切り分けを助けるため。
    /// </summary>
    private static string DescribeException(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }
}
