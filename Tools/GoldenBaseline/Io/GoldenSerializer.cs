using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using LargeFolderFinder.GoldenBaseline.Model;

namespace LargeFolderFinder.GoldenBaseline.Io;

/// <summary>
/// 期待値データのテキスト形式での読み書きを表す契約（design.md: Io/GoldenSerializer）。
/// 本体のクラス構造やシリアライズ実装には一切依存しない（要件7.2, 7.3の根拠）。
/// </summary>
public interface IGoldenSerializer
{
    /// <summary>
    /// GoldenDocument をテキスト形式で書き出す。
    /// 同一の document からは常にバイト単位で同一のファイルが生成される。
    /// </summary>
    /// <param name="document">書き出す期待値データ。</param>
    /// <param name="filePath">出力先のファイルパス。格納先のディレクトリは存在している必要がある。</param>
    void Write(GoldenDocument document, string filePath);

    /// <summary>
    /// テキスト形式の期待値データを読み取り、GoldenDocument を復元する。
    /// </summary>
    /// <param name="filePath">読み取り元のファイルパス。</param>
    /// <returns>復元された GoldenDocument。</returns>
    GoldenDocument Read(string filePath);
}

/// <summary>
/// 期待値データの読み書きに失敗したことを表す例外。
/// 未知の形式バージョン、区切り文字の破損、必須ヘッダの欠落などを検出した際に送出する。
/// </summary>
public sealed class GoldenFormatException : Exception
{
    public GoldenFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 期待値データのテキスト形式での読み書きを実装する（design.md: Io/GoldenSerializer）。
/// 出力は UTF-8（BOMなし）・改行はLFに固定し、エントリは相対パスの序数昇順に並べ、
/// 数値は不変カルチャで桁区切りなしに出力する。ヘッダ部は「# キー: 値」形式、
/// エントリ部はタブ区切りの行指向テキストとし、人間が差分ツールで読める表記にする。
/// </summary>
public sealed class GoldenSerializer : IGoldenSerializer
{
    /// <summary>
    /// この実装が読み書きできる期待値データの形式バージョン。
    /// 初版は 1。タスク7.1 でヘッダに BaseFolderPathLength（基準フォルダの実効絶対パス長）を追加したため 2 へ上げた。
    /// バージョン1の期待値ファイルは、未知のバージョンとして読み取りを拒否する（要件7.3 の経路）。
    /// </summary>
    private const int CurrentFormatVersion = 2;

    /// <summary>日時の往復表記に用いる不変カルチャの丸め表現形式。</summary>
    private const string DateTimeRoundtripFormat = "o";

    private const string HeaderPrefix = "# ";
    private const string HeaderKeyValueSeparator = ": ";

    private const string HeaderKeyFormatVersion = "FormatVersion";
    private const string HeaderKeyBaseFolderLabel = "BaseFolderLabel";
    private const string HeaderKeyBaseFolderPathLength = "BaseFolderPathLength";
    private const string HeaderKeyGeneratedAt = "GeneratedAt";
    private const string HeaderKeyUsePhysicalSize = "UsePhysicalSize";
    private const string HeaderKeyClusterSizeInBytes = "ClusterSizeInBytes";
    private const string HeaderKeyFixtureComplete = "FixtureComplete";
    private const string HeaderKeyFixtureOmission = "FixtureOmission";

    private const string BoolTrueToken = "true";
    private const string BoolFalseToken = "false";

    private const string EntryKindFolderToken = "D";
    private const string EntryKindFileToken = "F";

    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public void Write(GoldenDocument document, string filePath)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("出力先のファイルパスが空です。", nameof(filePath));
        }

        // 検査はすべて書き込みより前に行い、失敗したときはファイルを作らず既存のファイルも変えない。
        ValidateHeaderValues(document.Header);
        ValidateEntryPaths(document.Entries);

        // 序数比較で昇順に並べる。カルチャ依存の比較は用いない（要件1.3）。
        var sortedEntries = document.Entries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        AppendHeader(builder, document.Header);
        AppendEntries(builder, sortedEntries);

        byte[] bytes = Utf8WithoutBom.GetBytes(builder.ToString());
        File.WriteAllBytes(LongPath.Extend(filePath), bytes);
    }

    /// <inheritdoc />
    public GoldenDocument Read(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("読み取り元のファイルパスが空です。", nameof(filePath));
        }

        byte[] bytes = File.ReadAllBytes(LongPath.Extend(filePath));
        string content = Utf8WithoutBom.GetString(bytes);

        // 書き出しは常にLF固定だが、読み取り側は保守的にCRも許容して剥がす。
        string[] lines = content.Split('\n');

        var headerValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var fixtureOmissions = new List<string>();
        var entries = new List<GoldenEntry>();

        int index = 0;
        // ヘッダ部: 先頭から連続する "# " 始まりの行を読み取る。
        for (; index < lines.Length; index++)
        {
            string line = TrimTrailingCarriageReturn(lines[index]);
            if (!line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
            {
                break;
            }

            ParseHeaderLine(line, headerValues, fixtureOmissions);
        }

        GoldenHeader header = BuildHeader(headerValues, fixtureOmissions);

        // エントリ部: 残りの非空行をタブ区切りのエントリとして読み取る。
        for (; index < lines.Length; index++)
        {
            string line = TrimTrailingCarriageReturn(lines[index]);
            if (line.Length == 0)
            {
                continue;
            }

            entries.Add(ParseEntryLine(line));
        }

        return new GoldenDocument(header, entries);
    }

    /// <summary>
    /// 行末に残る可能性のある CR を取り除く。書き出しはLF固定だが、読み取りは保守的に扱う。
    /// </summary>
    private static string TrimTrailingCarriageReturn(string line)
    {
        if (line.Length > 0 && line[line.Length - 1] == '\r')
        {
            return line.Substring(0, line.Length - 1);
        }

        return line;
    }

    /// <summary>
    /// エントリのタブ区切りが壊れないよう、相対パスにタブ文字が含まれていないことを検証する。
    /// 含まれていた場合は書き込み時に検出して失敗させる（design.md: Risks）。
    /// </summary>
    private static void ValidateEntryPaths(IReadOnlyList<GoldenEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.RelativePath.IndexOf('\t') >= 0)
            {
                throw new GoldenFormatException(
                    $"相対パス '{entry.RelativePath}' にタブ文字が含まれています。エントリ行の区切り文字と衝突するため書き込めません。");
            }
        }
    }

    /// <summary>
    /// ヘッダの文字列の値（基準の論理名と各未生成項目）に改行（CR または LF）が含まれていないことを検証する。
    /// 含まれていると行指向の形式が壊れ、書き出しは成功しても読み戻せなくなるため、書き込み時に検出して失敗させる
    /// （design.md: GoldenSerializer の Risks）。数値・真偽値・日時の値は書式上改行を含みえないため対象にしない。
    /// </summary>
    private static void ValidateHeaderValues(GoldenHeader header)
    {
        ValidateHeaderValueHasNoNewline(HeaderKeyBaseFolderLabel, header.BaseFolderLabel);

        foreach (var omission in header.FixtureOmissions)
        {
            ValidateHeaderValueHasNoNewline(HeaderKeyFixtureOmission, omission);
        }
    }

    private static void ValidateHeaderValueHasNoNewline(string key, string value)
    {
        if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
        {
            string visible = value.Replace("\r", "\\r").Replace("\n", "\\n");
            throw new GoldenFormatException(
                $"ヘッダ項目 '{key}' の値 '{visible}' に改行（CR または LF）が含まれています。行指向の形式が壊れ読み戻せなくなるため書き込めません。");
        }
    }

    private static void AppendHeader(StringBuilder builder, GoldenHeader header)
    {
        AppendHeaderLine(builder, HeaderKeyFormatVersion, header.FormatVersion.ToString(CultureInfo.InvariantCulture));
        AppendHeaderLine(builder, HeaderKeyBaseFolderLabel, header.BaseFolderLabel);
        AppendHeaderLine(builder, HeaderKeyBaseFolderPathLength, header.BaseFolderPathLength.ToString(CultureInfo.InvariantCulture));
        AppendHeaderLine(builder, HeaderKeyGeneratedAt, header.GeneratedAt.ToString(DateTimeRoundtripFormat, CultureInfo.InvariantCulture));
        AppendHeaderLine(builder, HeaderKeyUsePhysicalSize, header.UsePhysicalSize ? BoolTrueToken : BoolFalseToken);
        AppendHeaderLine(builder, HeaderKeyClusterSizeInBytes, header.ClusterSizeInBytes.ToString(CultureInfo.InvariantCulture));
        AppendHeaderLine(builder, HeaderKeyFixtureComplete, header.FixtureComplete ? BoolTrueToken : BoolFalseToken);

        foreach (var omission in header.FixtureOmissions)
        {
            AppendHeaderLine(builder, HeaderKeyFixtureOmission, omission);
        }
    }

    private static void AppendHeaderLine(StringBuilder builder, string key, string value)
    {
        builder.Append(HeaderPrefix).Append(key).Append(HeaderKeyValueSeparator).Append(value).Append('\n');
    }

    private static void AppendEntries(StringBuilder builder, IReadOnlyList<GoldenEntry> sortedEntries)
    {
        foreach (var entry in sortedEntries)
        {
            builder
                .Append(EntryKindToToken(entry.Kind))
                .Append('\t')
                .Append(entry.RelativePath)
                .Append('\t')
                .Append(entry.SizeInBytes.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
    }

    private static string EntryKindToToken(GoldenEntryKind kind)
    {
        switch (kind)
        {
            case GoldenEntryKind.Folder:
                return EntryKindFolderToken;
            case GoldenEntryKind.File:
                return EntryKindFileToken;
            default:
                throw new GoldenFormatException($"未知のエントリ種別です: {kind}");
        }
    }

    private static GoldenEntryKind TokenToEntryKind(string token)
    {
        if (token == EntryKindFolderToken)
        {
            return GoldenEntryKind.Folder;
        }

        if (token == EntryKindFileToken)
        {
            return GoldenEntryKind.File;
        }

        throw new GoldenFormatException($"未知のエントリ種別トークンです: '{token}'（'{EntryKindFolderToken}' または '{EntryKindFileToken}' である必要があります）。");
    }

    /// <summary>
    /// "# キー: 値" 形式の1行を解析し、ヘッダの値を蓄積する。
    /// FixtureOmission のみ複数回出現しうるため別枠で収集する。
    /// </summary>
    private static void ParseHeaderLine(string line, IDictionary<string, string> headerValues, IList<string> fixtureOmissions)
    {
        string rest = line.Substring(HeaderPrefix.Length);
        int separatorIndex = rest.IndexOf(HeaderKeyValueSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw new GoldenFormatException($"ヘッダ行の形式が不正です（'キー: 値' の形式ではありません）: '{line}'");
        }

        string key = rest.Substring(0, separatorIndex);
        string value = rest.Substring(separatorIndex + HeaderKeyValueSeparator.Length);

        if (key == HeaderKeyFixtureOmission)
        {
            fixtureOmissions.Add(value);
            return;
        }

        headerValues[key] = value;
    }

    private static GoldenHeader BuildHeader(IDictionary<string, string> headerValues, IReadOnlyList<string> fixtureOmissions)
    {
        int formatVersion = ParseInt(RequireHeaderValue(headerValues, HeaderKeyFormatVersion), HeaderKeyFormatVersion);

        if (formatVersion != CurrentFormatVersion)
        {
            throw new GoldenFormatException(
                $"未知の形式バージョンです: {formatVersion}（このツールが読み書きできる形式バージョンは {CurrentFormatVersion} のみです）。");
        }

        string baseFolderLabel = RequireHeaderValue(headerValues, HeaderKeyBaseFolderLabel);
        int baseFolderPathLength = ParseInt(RequireHeaderValue(headerValues, HeaderKeyBaseFolderPathLength), HeaderKeyBaseFolderPathLength);
        DateTimeOffset generatedAt = ParseDateTimeOffset(RequireHeaderValue(headerValues, HeaderKeyGeneratedAt));
        bool usePhysicalSize = ParseBool(RequireHeaderValue(headerValues, HeaderKeyUsePhysicalSize), HeaderKeyUsePhysicalSize);
        long clusterSizeInBytes = ParseLong(RequireHeaderValue(headerValues, HeaderKeyClusterSizeInBytes), HeaderKeyClusterSizeInBytes);
        bool fixtureComplete = ParseBool(RequireHeaderValue(headerValues, HeaderKeyFixtureComplete), HeaderKeyFixtureComplete);

        return new GoldenHeader(
            formatVersion,
            baseFolderLabel,
            baseFolderPathLength,
            generatedAt,
            usePhysicalSize,
            clusterSizeInBytes,
            fixtureComplete,
            fixtureOmissions);
    }

    private static string RequireHeaderValue(IDictionary<string, string> headerValues, string key)
    {
        if (!headerValues.TryGetValue(key, out var value))
        {
            throw new GoldenFormatException($"必須のヘッダ項目 '{key}' が見つかりません。");
        }

        return value;
    }

    private static int ParseInt(string value, string key)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            throw new GoldenFormatException($"ヘッダ項目 '{key}' の値 '{value}' を整数として解釈できません。");
        }

        return result;
    }

    private static long ParseLong(string value, string key)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
        {
            throw new GoldenFormatException($"ヘッダ項目 '{key}' の値 '{value}' を整数として解釈できません。");
        }

        return result;
    }

    private static bool ParseBool(string value, string key)
    {
        if (value == BoolTrueToken)
        {
            return true;
        }

        if (value == BoolFalseToken)
        {
            return false;
        }

        throw new GoldenFormatException($"ヘッダ項目 '{key}' の値 '{value}' を真偽値として解釈できません（'{BoolTrueToken}' または '{BoolFalseToken}' である必要があります）。");
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                DateTimeRoundtripFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset result))
        {
            throw new GoldenFormatException($"ヘッダ項目 '{HeaderKeyGeneratedAt}' の値 '{value}' を日時として解釈できません。");
        }

        return result;
    }

    /// <summary>
    /// "種別\t相対パス\tバイトサイズ" 形式の1行を解析する。
    /// フィールド数が3でない場合は、区切り文字（タブ）の破損とみなして失敗させる。
    /// </summary>
    private static GoldenEntry ParseEntryLine(string line)
    {
        string[] fields = line.Split('\t');
        if (fields.Length != 3)
        {
            throw new GoldenFormatException(
                $"エントリ行の区切り（タブ）が破損しています。フィールド数は3である必要がありますが{fields.Length}個でした: '{line}'");
        }

        GoldenEntryKind kind = TokenToEntryKind(fields[0]);
        string relativePath = fields[1];
        long sizeInBytes = ParseLong(fields[2], "SizeInBytes");

        return new GoldenEntry(relativePath, kind, sizeInBytes);
    }
}
