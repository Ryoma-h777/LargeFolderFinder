using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MessagePack;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 走査の結果の保存と復元（scan-performance 要件7.1, 7.2）を、本体の保存の設定と本体の読み込みで
/// 実際に行い、観測した値だけを返す入口。判定は呼び出し側（自己検証）が行う。
/// 本体に触れる呼び出しを Scan 層に閉じ込めるためのものであり、本体は一切変えない。
/// </summary>
/// <remarks>
/// <para>
/// **利用者の保存データには触れない。** 本体の <see cref="global::LargeFolderFinder.SessionFileManager.Save"/> は
/// 利用者のアプリデータの Sessions フォルダへ書くため使わず、本体と同じ保存の設定
/// （<c>SessionFileManager</c> の private な <c>LZ4Options</c>。反射で取り出す）で一時領域のファイルへ書く。
/// </para>
/// <para>
/// 読み戻しは本体の <see cref="global::LargeFolderFinder.SessionFileManager.Load"/> をそのまま使う。
/// <c>Load</c> は <c>Path.Combine(Sessions フォルダ, ファイル名)</c> でパスを組み立てるが、
/// .NET の <see cref="Path.Combine(string, string)"/> は後ろがルート付きの絶対パスならそれをそのまま返すため、
/// 一時領域の絶対パスを渡すと Sessions フォルダではなくそのファイルが読まれる。
/// <c>Load</c> は読み取りしかせず（存在の確認と <c>File.ReadAllBytes</c> のみ）、フォルダの作成も書き込みも削除も行わないため、
/// 利用者のアプリデータには影響しない。この経路が本当に一時領域のファイルを読んでいることは、
/// 正常な内容の控えを同じ経路で読めること（<see cref="CorruptSessionLoadOutcome.ValidFileLoaded"/>）で確かめる。
/// </para>
/// </remarks>
public static class SessionPersistenceProbe
{
    /// <summary>
    /// 走査した木をセッションのデータに入れ、本体と同じ保存の設定で一時領域のファイルへ書き、
    /// 本体の読み込み（<c>SessionFileManager.Load</c>）で読み戻して、読み戻した木と観測値を返す。
    /// 一時領域のファイルは必ず消す。
    /// </summary>
    /// <param name="root">保存する走査の結果の木の根。</param>
    /// <exception cref="MissingFieldException">本体に保存の設定の欄が見つからないとき。</exception>
    public static SessionRestoreOutcome SaveAndLoadTree(global::LargeFolderFinder.FolderInfo root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        MessagePackSerializerOptions options = GetSessionFileOptions();

        // 保存する欄には、走査の後にタブが持つ値と同じ形の値を入れる
        var session = new global::LargeFolderFinder.SessionData
        {
            CreatedAt = new DateTime(2026, 9, 23, 10, 11, 12, DateTimeKind.Local),
            Path = root.Name,
            FilterText = "probe",
            LastScanDuration = TimeSpan.FromMilliseconds(1234),
            TotalFilesScanned = 56789,
            Result = root,
        };

        string filePath = CreateTempSessionFilePath();
        byte[] bytes;
        global::LargeFolderFinder.SessionData? restored;

        try
        {
            bytes = MessagePackSerializer.Serialize(session, options);
            File.WriteAllBytes(filePath, bytes);

            restored = global::LargeFolderFinder.SessionFileManager.Load(filePath);
        }
        finally
        {
            TryDelete(filePath);
        }

        global::LargeFolderFinder.FolderInfo? restoredRoot = restored?.Result;

        return new SessionRestoreOutcome(
            SavedByteCount: bytes.Length,
            SavedPath: session.Path,
            SavedCreatedAt: session.CreatedAt,
            SavedLastScanDuration: session.LastScanDuration,
            SavedTotalFilesScanned: session.TotalFilesScanned,
            Restored: restored != null,
            RestoredRoot: restoredRoot,
            RestoredPath: restored?.Path,
            RestoredCreatedAt: restored?.CreatedAt,
            RestoredLastScanDuration: restored?.LastScanDuration,
            RestoredTotalFilesScanned: restored?.TotalFilesScanned,
            RestoredRootParentIsNull: restoredRoot != null && restoredRoot.Parent == null,
            ParentLinkProblem: restoredRoot == null ? "読み戻せませんでした。" : FindParentLinkProblem(restoredRoot, root.Name));
    }

    /// <summary>
    /// 壊れた内容の保存ファイルの読み込みを確かめる（要件7.2）。
    /// 一時領域に正常な控えと壊れた内容のファイルを作り、
    /// （1）壊れた内容を直に復元すると例外になること、（2）本体の <c>Load</c> は例外を外に出さず null を返すこと、
    /// （3）存在しないファイルでも null を返すことを観測して返す。作ったファイルは必ず消す。
    /// </summary>
    /// <exception cref="MissingFieldException">本体に保存の設定の欄が見つからないとき。</exception>
    public static CorruptSessionLoadOutcome LoadCorruptedSessions()
    {
        MessagePackSerializerOptions options = GetSessionFileOptions();

        var session = new global::LargeFolderFinder.SessionData
        {
            CreatedAt = new DateTime(2026, 9, 23, 10, 11, 12, DateTimeKind.Local),
            Path = @"\\probe\valid",
            FilterText = "valid",
            Result = new global::LargeFolderFinder.FolderInfo("root", 0)
            {
                Children = { new global::LargeFolderFinder.FolderInfo("child.bin", 42, true) },
            },
        };

        byte[] validBytes = MessagePackSerializer.Serialize(session, options);

        // 壊し方は2通り。どちらも MessagePack として復元できない内容であることが決まっている
        var corruptContents = new List<(string Label, byte[] Bytes)>
        {
            // 0xC1 は MessagePack で「使われない」と定められた印。並べた内容は必ず復元に失敗する
            ("使われない印だけの内容", CreateBytes(0xC1, 64)),

            // 途中で切れた保存ファイル（書き込みが中断した場合に相当）
            ("途中で切れた内容", Truncate(validBytes)),
        };

        string validPath = CreateTempSessionFilePath();
        var corruptPaths = new List<string>();
        var cases = new List<CorruptSessionCase>();
        bool validLoaded;
        string? validLoadedPath;
        bool nonExistentThrew = false;
        bool nonExistentReturnedNull;

        try
        {
            File.WriteAllBytes(validPath, validBytes);
            global::LargeFolderFinder.SessionData? valid = global::LargeFolderFinder.SessionFileManager.Load(validPath);
            validLoaded = valid != null;
            validLoadedPath = valid?.Path;

            foreach ((string label, byte[] content) in corruptContents)
            {
                string corruptPath = CreateTempSessionFilePath();
                corruptPaths.Add(corruptPath);
                File.WriteAllBytes(corruptPath, content);

                // 1. 保存の設定のまま直に復元すると例外になること（壊れた内容であることの裏づけ）
                bool directThrew = false;
                string directExceptionType = string.Empty;
                try
                {
                    MessagePackSerializer.Deserialize<global::LargeFolderFinder.SessionData>(content, options);
                }
                catch (Exception ex)
                {
                    directThrew = true;
                    directExceptionType = ex.GetType().Name;
                }

                // 2. 本体の読み込みは例外を外に出さず null を返すこと
                bool loadThrew = false;
                string loadExceptionType = string.Empty;
                bool loadReturnedNull = false;
                try
                {
                    loadReturnedNull = global::LargeFolderFinder.SessionFileManager.Load(corruptPath) == null;
                }
                catch (Exception ex)
                {
                    loadThrew = true;
                    loadExceptionType = ex.GetType().Name;
                }

                cases.Add(new CorruptSessionCase(
                    label,
                    directThrew,
                    directExceptionType,
                    loadThrew,
                    loadExceptionType,
                    loadReturnedNull));
            }

            // 3. 存在しないファイルでも例外を外に出さず null を返すこと
            string missingPath = CreateTempSessionFilePath();
            bool missingReturnedNull = false;
            try
            {
                missingReturnedNull = global::LargeFolderFinder.SessionFileManager.Load(missingPath) == null;
            }
            catch (Exception)
            {
                nonExistentThrew = true;
            }

            nonExistentReturnedNull = missingReturnedNull;
        }
        finally
        {
            TryDelete(validPath);
            foreach (string path in corruptPaths)
            {
                TryDelete(path);
            }
        }

        return new CorruptSessionLoadOutcome(
            validLoaded,
            validLoadedPath,
            ExpectedValidPath: session.Path,
            nonExistentReturnedNull,
            nonExistentThrew,
            cases);
    }

    /// <summary>
    /// 本体のセッションの保存（<see cref="global::LargeFolderFinder.SessionFileManager"/>）が使う MessagePack の設定を、
    /// 反射で本体から取り出して返す。設定を写し書きすると本体の変更に追従できないため、本体の値そのものを使う
    /// （<see cref="RenderCancellationProbe"/> と同じ取り方）。
    /// </summary>
    /// <exception cref="MissingFieldException">本体に設定の欄が見つからないとき。</exception>
    private static MessagePackSerializerOptions GetSessionFileOptions()
    {
        FieldInfo? field = typeof(global::LargeFolderFinder.SessionFileManager).GetField(
            "LZ4Options",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is not MessagePackSerializerOptions options)
        {
            throw new MissingFieldException("SessionFileManager に保存の設定（LZ4Options）が見つかりません。");
        }

        return options;
    }

    /// <summary>
    /// 読み戻した木の親の付け直し（<c>FolderInfo.RestoreParentReferences</c>）に漏れがないかを調べ、
    /// 最初に見つかった食い違いの説明を返す。問題が無ければ null を返す。
    /// </summary>
    /// <param name="root">読み戻した木の根。</param>
    /// <param name="rootName">根の名前（説明に使う）。</param>
    private static string? FindParentLinkProblem(global::LargeFolderFinder.FolderInfo root, string rootName)
    {
        if (root.Parent != null)
        {
            return $"根（{rootName}）に親が付いています。";
        }

        var stack = new Stack<global::LargeFolderFinder.FolderInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            global::LargeFolderFinder.FolderInfo node = stack.Pop();
            foreach (global::LargeFolderFinder.FolderInfo child in node.Children)
            {
                if (!ReferenceEquals(child.Parent, node))
                {
                    return child.Parent == null
                        ? $"'{child.Name}' に親が付いていません（親: {node.Name}）。"
                        : $"'{child.Name}' の親が違います（期待: {node.Name}, 実際: {child.Parent.Name}）。";
                }

                stack.Push(child);
            }
        }

        return null;
    }

    /// <summary>同じ値を並べた内容を作る。</summary>
    private static byte[] CreateBytes(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    /// <summary>内容を途中で切る（少なくとも1バイトは残す）。</summary>
    private static byte[] Truncate(byte[] bytes)
    {
        int length = Math.Max(1, bytes.Length / 3);
        var truncated = new byte[length];
        Array.Copy(bytes, truncated, length);
        return truncated;
    }

    /// <summary>一時領域の保存ファイルのパスを組み立てる。ファイル自体はまだ作らない。</summary>
    private static string CreateTempSessionFilePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "gb_ses_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".msgpack");
    }

    /// <summary>後始末。消せなくても検証の判定には影響させない。</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 意図して無視: 一時領域のファイルが消せなくても、観測した値の正しさには関わらない
        }
    }
}

/// <summary><see cref="SessionPersistenceProbe.SaveAndLoadTree"/> の結果。</summary>
/// <param name="SavedByteCount">保存した内容の大きさ（バイト）。</param>
/// <param name="SavedPath">保存した検索パス。</param>
/// <param name="SavedCreatedAt">保存した作成日時。</param>
/// <param name="SavedLastScanDuration">保存した前回の走査の所要時間。</param>
/// <param name="SavedTotalFilesScanned">保存した走査したファイル数。</param>
/// <param name="Restored">読み戻せたかどうか。</param>
/// <param name="RestoredRoot">読み戻した走査の結果の木の根。</param>
/// <param name="RestoredPath">読み戻した検索パス。</param>
/// <param name="RestoredCreatedAt">読み戻した作成日時。</param>
/// <param name="RestoredLastScanDuration">読み戻した前回の走査の所要時間。</param>
/// <param name="RestoredTotalFilesScanned">読み戻した走査したファイル数。</param>
/// <param name="RestoredRootParentIsNull">読み戻した木の根に親が付いていないかどうか。</param>
/// <param name="ParentLinkProblem">親の付け直しの食い違いの説明。問題が無ければ null。</param>
public sealed record SessionRestoreOutcome(
    int SavedByteCount,
    string SavedPath,
    DateTime SavedCreatedAt,
    TimeSpan SavedLastScanDuration,
    long SavedTotalFilesScanned,
    bool Restored,
    global::LargeFolderFinder.FolderInfo? RestoredRoot,
    string? RestoredPath,
    DateTime? RestoredCreatedAt,
    TimeSpan? RestoredLastScanDuration,
    long? RestoredTotalFilesScanned,
    bool RestoredRootParentIsNull,
    string? ParentLinkProblem);

/// <summary><see cref="SessionPersistenceProbe.LoadCorruptedSessions"/> の結果。</summary>
/// <param name="ValidFileLoaded">正常な内容の控えを同じ経路で読み戻せたかどうか（経路が一時領域のファイルを見ている裏づけ）。</param>
/// <param name="ValidLoadedPath">読み戻した控えの検索パス。</param>
/// <param name="ExpectedValidPath">控えに入れた検索パス。</param>
/// <param name="NonExistentReturnedNull">存在しないファイルで null が返ったかどうか。</param>
/// <param name="NonExistentThrew">存在しないファイルで例外が外に出たかどうか。</param>
/// <param name="Cases">壊し方ごとの観測。</param>
public sealed record CorruptSessionLoadOutcome(
    bool ValidFileLoaded,
    string? ValidLoadedPath,
    string ExpectedValidPath,
    bool NonExistentReturnedNull,
    bool NonExistentThrew,
    IReadOnlyList<CorruptSessionCase> Cases);

/// <summary>壊れた内容の保存ファイル1通りぶんの観測。</summary>
/// <param name="Label">壊し方の呼び名。</param>
/// <param name="DirectDeserializeThrew">保存の設定のまま直に復元して例外になったかどうか。</param>
/// <param name="DirectExceptionTypeName">そのときの例外の型名。</param>
/// <param name="LoadThrew">本体の読み込みから例外が外に出たかどうか。</param>
/// <param name="LoadExceptionTypeName">そのときの例外の型名。</param>
/// <param name="LoadReturnedNull">本体の読み込みが null を返したかどうか。</param>
public sealed record CorruptSessionCase(
    string Label,
    bool DirectDeserializeThrew,
    string DirectExceptionTypeName,
    bool LoadThrew,
    string LoadExceptionTypeName,
    bool LoadReturnedNull);
