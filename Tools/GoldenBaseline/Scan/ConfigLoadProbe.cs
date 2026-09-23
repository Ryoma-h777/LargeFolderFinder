using System;
using System.IO;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 本体の設定の読み込み（<see cref="global::LargeFolderFinder.Config"/>）を、一時フォルダの設定ファイルに対して
/// 決まった手順で呼び、観測した値だけを返す入口（scan-correctness 要件5.2, 5.4）。
/// 本体に触れる呼び出しを Scan 層に閉じ込めるためのものであり、判定は呼び出し側（自己検証）が行う。
/// 読み込み元は常に一時フォルダの中のパスを渡し、利用者の <c>Config.txt</c> には触れない。
/// </summary>
public static class ConfigLoadProbe
{
    /// <summary>壊れた内容（閉じていない配列）。YAML として解析できない。</summary>
    public const string BrokenContent = "MaxDepthForCount: [1, 2\nUseParallelScan: true\n";

    /// <summary>別の理由で壊れた内容（数値の欄に数値でない値）。</summary>
    public const string OtherBrokenContent = "MaxDepthForCount: not-a-number\n";

    /// <summary>正しい内容。既定値と異なる値を入れ、実際に読めたことを見分けられるようにする。</summary>
    public const string ValidContent =
        "MaxDepthForCount: 7\nUseParallelScan: false\nSkipFolderCount: true\nUsePhysicalSize: false\nOldDataThresholdDays: 12\n";

    /// <summary>
    /// 一時フォルダに壊れた設定・別の壊れ方の設定・正しい設定を置き、次の順に <c>Config.LoadFrom</c> と
    /// <c>Config.TryTakeUnnotifiedError</c> を呼んで、各段の観測値を返す。
    /// 最後に正しい設定を読み、静的な失敗の記録を成功の状態に戻してから返す（同じプロセスの他の項目に影響を残さない）。
    /// </summary>
    public static ConfigLoadOutcome Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gb_cfg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, global::LargeFolderFinder.AppConstants.ConfigFileName);
        FileSnapshot defaultBefore = FileSnapshot.Take(defaultPath);

        try
        {
            Directory.CreateDirectory(dir);
            string brokenPath = Path.Combine(dir, "broken.txt");
            string otherBrokenPath = Path.Combine(dir, "other_broken.txt");
            string validPath = Path.Combine(dir, "valid.txt");
            string missingPath = Path.Combine(dir, "missing.txt");
            string unwritablePath = Path.Combine(dir, "no_such_dir", "Config.txt");
            File.WriteAllText(brokenPath, BrokenContent);
            File.WriteAllText(otherBrokenPath, OtherBrokenContent);
            File.WriteAllText(validPath, ValidContent);

            // 前の状態を持ち越さないよう、まず成功の状態にそろえる
            global::LargeFolderFinder.Config.LoadFrom(validPath);
            bool pendingAtStart = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);

            // 1. 壊れた設定を読む
            ConfigValues brokenValues = ConfigValues.From(global::LargeFolderFinder.Config.LoadFrom(brokenPath));
            string? brokenError = global::LargeFolderFinder.Config.LastLoadError;
            bool brokenFileUnchanged = File.ReadAllText(brokenPath) == BrokenContent;
            bool firstTake = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out string firstTakeError);
            bool secondTake = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);

            // 2. 同じ壊れ方をもう一度読む（同じ内容の失敗は再び通知しない）
            global::LargeFolderFinder.Config.LoadFrom(brokenPath);
            string? repeatedError = global::LargeFolderFinder.Config.LastLoadError;
            bool takeAfterRepeat = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);

            // 3. 別の壊れ方を読む（内容が違えば通知する）
            global::LargeFolderFinder.Config.LoadFrom(otherBrokenPath);
            string? otherError = global::LargeFolderFinder.Config.LastLoadError;
            bool takeAfterOther = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out string otherTakeError);

            // 4. 正しい設定を読む（成功で記録が消える）
            ConfigValues validValues = ConfigValues.From(global::LargeFolderFinder.Config.LoadFrom(validPath));
            string? errorAfterValid = global::LargeFolderFinder.Config.LastLoadError;
            bool takeAfterValid = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);

            // 5. 成功の後に、最初と同じ壊れ方をもう一度読む（通知済みの記録が消えているので再び通知する）
            global::LargeFolderFinder.Config.LoadFrom(brokenPath);
            bool takeAfterSuccessThenBroken = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out string reTakeError);

            // 6. ファイルが無いパスを読む（失敗ではなく、そのパスに既定の設定を書き出す）
            ConfigValues missingValues = ConfigValues.From(global::LargeFolderFinder.Config.LoadFrom(missingPath));
            string? errorAfterMissing = global::LargeFolderFinder.Config.LastLoadError;
            bool takeAfterMissing = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);
            bool missingFileWritten = File.Exists(missingPath);
            ConfigValues? writtenDefaultValues = null;
            if (missingFileWritten)
            {
                writtenDefaultValues = ConfigValues.From(global::LargeFolderFinder.Config.LoadFrom(missingPath));
            }

            // 7. 書き出せないパスを読む（書き出しの失敗は読み込みの失敗ではなく、例外も外に出さない）
            Exception? unwritableException = null;
            string? errorAfterUnwritable = null;
            try
            {
                global::LargeFolderFinder.Config.LoadFrom(unwritablePath);
                errorAfterUnwritable = global::LargeFolderFinder.Config.LastLoadError;
            }
            catch (Exception ex)
            {
                unwritableException = ex;
            }

            // 最後に成功の状態へ戻す
            global::LargeFolderFinder.Config.LoadFrom(validPath);
            bool pendingAtEnd = global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);
            string? errorAtEnd = global::LargeFolderFinder.Config.LastLoadError;

            string logText = ReadLogText();

            return new ConfigLoadOutcome(
                pendingAtStart,
                brokenValues,
                brokenError,
                brokenFileUnchanged,
                firstTake,
                firstTakeError,
                secondTake,
                repeatedError,
                takeAfterRepeat,
                otherError,
                takeAfterOther,
                otherTakeError,
                validValues,
                errorAfterValid,
                takeAfterValid,
                takeAfterSuccessThenBroken,
                reTakeError,
                missingValues,
                errorAfterMissing,
                takeAfterMissing,
                missingFileWritten,
                writtenDefaultValues,
                unwritableException?.GetType().Name,
                errorAfterUnwritable,
                pendingAtEnd,
                errorAtEnd,
                LogMentionsBrokenPath: logText.Contains(brokenPath, StringComparison.Ordinal),
                LogMentionsUnwritablePath: logText.Contains(unwritablePath, StringComparison.Ordinal),
                DefaultPathUnchanged: defaultBefore.Equals(FileSnapshot.Take(defaultPath)),
                DefaultValues: ConfigValues.From(new global::LargeFolderFinder.Config()));
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // 意図して無視: 一時フォルダの後始末の失敗は検証の結果に関係しない
            }
        }
    }

    /// <summary>
    /// 同梱の設定ファイル（実行ファイルと同じ場所の <c>Config.txt</c>）を読み、観測した値だけを返す
    /// （scan-performance 要件3.3）。ファイルは既にあるので既定の設定の書き出しは起きず、読むだけで書き換えない。
    /// </summary>
    public static BundledConfigOutcome ReadBundled()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, global::LargeFolderFinder.AppConstants.ConfigFileName);
        FileSnapshot before = FileSnapshot.Take(path);

        string text = before.Exists ? File.ReadAllText(path) : string.Empty;
        ConfigValues values = ConfigValues.From(global::LargeFolderFinder.Config.LoadFrom(path));
        string? error = global::LargeFolderFinder.Config.LastLoadError;

        // 読み込みの失敗の記録を他の項目へ持ち越さない
        global::LargeFolderFinder.Config.TryTakeUnnotifiedError(out _);

        return new BundledConfigOutcome(
            before.Exists,
            path,
            text,
            values,
            error,
            before.Equals(FileSnapshot.Take(path)));
    }

    /// <summary>本体の現在のログファイルの中身を、書き込み中でも読める共有の指定で読む。読めなければ空文字列を返す。</summary>
    private static string ReadLogText()
    {
        string path = global::LargeFolderFinder.Logger.CurrentLogFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    /// <summary>既定のパスのファイルの有無・更新時刻・大きさの控え。読み込みの前後で変わっていないことを確かめるために使う。</summary>
    private readonly record struct FileSnapshot(bool Exists, DateTime LastWriteTimeUtc, long Length)
    {
        public static FileSnapshot Take(string path)
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new FileSnapshot(true, info.LastWriteTimeUtc, info.Length)
                : new FileSnapshot(false, default, 0);
        }
    }
}

/// <summary>設定の各欄の値の控え。</summary>
public sealed record ConfigValues(
    int MaxDepthForCount,
    bool UseParallelScan,
    bool SkipFolderCount,
    bool UsePhysicalSize,
    int OldDataThresholdDays,
    int ScanThreads)
{
    /// <summary>本体の設定から各欄の値を写す。</summary>
    public static ConfigValues From(global::LargeFolderFinder.Config config) => new(
        config.MaxDepthForCount,
        config.UseParallelScan,
        config.SkipFolderCount,
        config.UsePhysicalSize,
        config.OldDataThresholdDays,
        config.ScanThreads);
}

/// <summary><see cref="ConfigLoadProbe.ReadBundled"/> の結果。</summary>
/// <param name="Exists">同梱の設定ファイルがあったか</param>
/// <param name="Path">読んだ設定ファイルのパス</param>
/// <param name="Text">設定ファイルの中身（行の有無と説明を確かめるために使う）</param>
/// <param name="Values">読み込めた設定の値</param>
/// <param name="Error">読み込みの失敗の理由。成功なら null</param>
/// <param name="Unchanged">読み込みの前後でファイルが変わっていないか</param>
public sealed record BundledConfigOutcome(
    bool Exists,
    string Path,
    string Text,
    ConfigValues Values,
    string? Error,
    bool Unchanged);

/// <summary><see cref="ConfigLoadProbe.Run"/> の結果。</summary>
public sealed record ConfigLoadOutcome(
    bool PendingAtStart,
    ConfigValues BrokenValues,
    string? BrokenError,
    bool BrokenFileUnchanged,
    bool FirstTake,
    string FirstTakeError,
    bool SecondTake,
    string? RepeatedError,
    bool TakeAfterRepeat,
    string? OtherError,
    bool TakeAfterOther,
    string OtherTakeError,
    ConfigValues ValidValues,
    string? ErrorAfterValid,
    bool TakeAfterValid,
    bool TakeAfterSuccessThenBroken,
    string ReTakeError,
    ConfigValues MissingValues,
    string? ErrorAfterMissing,
    bool TakeAfterMissing,
    bool MissingFileWritten,
    ConfigValues? WrittenDefaultValues,
    string? UnwritableExceptionType,
    string? ErrorAfterUnwritable,
    bool PendingAtEnd,
    string? ErrorAtEnd,
    bool LogMentionsBrokenPath,
    bool LogMentionsUnwritablePath,
    bool DefaultPathUnchanged,
    ConfigValues DefaultValues);
