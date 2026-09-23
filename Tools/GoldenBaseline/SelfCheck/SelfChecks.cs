using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using LargeFolderFinder.GoldenBaseline.Compare;
using LargeFolderFinder.GoldenBaseline.Fixture;
using LargeFolderFinder.GoldenBaseline.Io;
using LargeFolderFinder.GoldenBaseline.Model;
using LargeFolderFinder.GoldenBaseline.Scan;

namespace LargeFolderFinder.GoldenBaseline.SelfCheck;

/// <summary>
/// 自己検証項目の登録をまとめる場所。
/// Model / Io / Fixture / Scan / Compare の各層が実装され次第、対応する検証項目をここに追加していく。
/// </summary>
internal static class SelfChecks
{
    public static void Register(SelfCheckRunner runner)
    {
        runner.Add("自己検証ハーネスが成功を正しく判定できる", () =>
        {
            SelfAssert.That(1 + 1 == 2, "基本的な算術検証に失敗しました。");
        });

        RegisterModelChecks(runner);
        RegisterModelInvariantEdgeCaseChecks(runner);
        RegisterLongPathChecks(runner);
        RegisterGoldenSerializerChecks(runner);
        RegisterGoldenSerializerHeaderNewlineChecks(runner);
        RegisterHandWrittenGoldenFileChecks(runner);
        RegisterGoldenSerializerDependencyBoundaryChecks(runner);
        RegisterBaselineComparerChecks(runner);
        RegisterFixtureSpecChecks(runner);
        RegisterAccessControlGateChecks(runner);
        RegisterFixtureBuilderChecks(runner);
        RegisterFixtureOmissionTypeNameChecks(runner);
        RegisterScanRunnerChecks(runner);
        RegisterGoldenProjectorChecks(runner);
        RegisterKnownIssueAnalyzerChecks(runner);
        RegisterKnownIssueBoundaryChecks(runner);
        RegisterProgramChecks(runner);
        RegisterIncompleteFixtureRecordChecks(runner);
        RegisterBaseFolderPathLengthChecks(runner);
        RegisterPathLengthBoundaryChecks(runner);
        RegisterScanReportParityChecks(runner);
        RegisterFolderCounterChecks(runner);
        RegisterFinalProgressChecks(runner);
        RegisterWin32DeclarationChecks(runner);
        RegisterConcurrentTreeReadChecks(runner);
        RegisterRenderCancellationChecks(runner);
        RegisterConfigLoadErrorChecks(runner);
        RegisterScanParallelismChecks(runner);
    }

    /// <summary>
    /// 並列度の決め方（scan-performance 要件3.1, 3.2）の検証項目を登録する（タスク2.2）。
    /// 実際にネットワークへ接続せず、パスの文字列と一時フォルダだけで確かめる。
    /// </summary>
    private static void RegisterScanParallelismChecks(SelfCheckRunner runner)
    {
        runner.Add("並列度の決め方: 逐次の設定・設定値・UNC パス・ローカルのパスで期待どおりの値になる（scan-performance 要件3.1, 3.2）", () =>
        {
            // 検証ツールから直接確かめられるよう、公開の静的クラスであること（design.md: ScanParallelism）
            Type type = typeof(ScanParallelism);
            SelfAssert.That(type.IsPublic, "ScanParallelism が public ではありません（検証ツールから直接確かめられません）。");
            SelfAssert.That(type.IsAbstract && type.IsSealed, "ScanParallelism が静的クラスではありません。");

            // 実在しないホストの UNC パス。判定だけを行い、接続はしない
            const string uncPath = @"\\unlikely-host-name-for-test\share\folder";
            string localPath = Path.GetTempPath();

            // 逐次の設定（要件3.2）: 設定値や対象に関わらず 1
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, false, 0) == 1,
                "逐次の設定でローカルのパスのワーカー数が 1 になりません。");
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, false, 8) == 1,
                "逐次の設定で、設定値 8 がワーカー数に反映されました（逐次が優先されるはずです）。");
            SelfAssert.That(
                ScanParallelism.Resolve(uncPath, false, 0) == 1,
                "逐次の設定で UNC パスのワーカー数が 1 になりません。");

            // 設定値が正のとき（要件3.1）: その値。上限 64 を超えたら 64 に丸める
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, 1) == 1,
                "設定値 1 がそのままワーカー数になりません。");
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, 3) == 3,
                "設定値 3 がそのままワーカー数になりません。");
            SelfAssert.That(
                ScanParallelism.Resolve(uncPath, true, 3) == 3,
                "UNC パスでも設定値 3 がそのままワーカー数になりません（設定値は自動より優先されるはずです）。");
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, 64) == 64,
                "上限と同じ設定値 64 がそのままワーカー数になりません。");
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, 65) == 64,
                "上限を超える設定値 65 が 64 に丸められません。");
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, int.MaxValue) == 64,
                "上限を大きく超える設定値が 64 に丸められません。");

            // 負の設定値は範囲外として自動と同じ扱いにする（判定の失敗で走査を止めない）
            SelfAssert.That(
                ScanParallelism.Resolve(uncPath, true, -1) == ScanParallelism.Resolve(uncPath, true, 0),
                "負の設定値が自動と同じ扱いになりません。");

            // ネットワークの判定（要件3.1）: UNC パスは接続せずにネットワークとして扱う
            SelfAssert.That(
                ScanParallelism.IsNetworkPath(uncPath),
                "実在しないホストの UNC パスがネットワークと判定されません。");
            SelfAssert.That(
                ScanParallelism.IsNetworkPath(@"\\?\UNC\unlikely-host-name-for-test\share\folder"),
                @"\\?\UNC\ 形式の UNC パスがネットワークと判定されません。");
            SelfAssert.That(
                ScanParallelism.Resolve(uncPath, true, 0) == 16,
                $"UNC パスの自動のワーカー数がネットワークの既定値 16 になりません（{ScanParallelism.Resolve(uncPath, true, 0)}）。");

            // ローカルの判定: 検証ツールの一時フォルダ。自動は論理プロセッサ数を 4〜16 に丸めた値
            SelfAssert.That(
                !ScanParallelism.IsNetworkPath(localPath),
                "一時フォルダのパスがネットワークと判定されました（この検証はローカルの一時フォルダを前提にしています）。");
            int expectedLocal = Math.Clamp(Environment.ProcessorCount, 4, 16);
            SelfAssert.That(
                ScanParallelism.Resolve(localPath, true, 0) == expectedLocal,
                $"ローカルのパスの自動のワーカー数が、論理プロセッサ数を 4〜16 に丸めた値（{expectedLocal}）になりません（{ScanParallelism.Resolve(localPath, true, 0)}）。");
            SelfAssert.That(
                expectedLocal >= 4 && expectedLocal <= 16,
                $"ローカルの自動のワーカー数が 4〜16 の範囲から外れています（{expectedLocal}）。");

            // 判定できないパスはローカルとして扱い、例外を外に出さない
            foreach (string undecidable in new[] { string.Empty, "   ", "relative\\path", @"Z:\not-existing-drive", "\0invalid" })
            {
                SelfAssert.That(
                    !ScanParallelism.IsNetworkPath(undecidable),
                    $"判定できないパス '{undecidable}' がネットワークと判定されました（ローカルとして扱うはずです）。");
                SelfAssert.That(
                    ScanParallelism.Resolve(undecidable, true, 0) == expectedLocal,
                    $"判定できないパス '{undecidable}' の自動のワーカー数がローカルの既定値になりません。");
            }

            // null を渡しても例外を外に出さない（走査を止めない）
            SelfAssert.That(
                !ScanParallelism.IsNetworkPath(null!),
                "null のパスがネットワークと判定されました。");
            SelfAssert.That(
                ScanParallelism.Resolve(null!, true, 0) == expectedLocal,
                "null のパスの自動のワーカー数がローカルの既定値になりません。");

            // 戻り値は常に 1 以上（DirectoryWalker の前提: WorkerCount >= 1）
            foreach (int configured in new[] { int.MinValue, -1, 0, 1, 64, 65, int.MaxValue })
            {
                SelfAssert.That(
                    ScanParallelism.Resolve(localPath, true, configured) >= 1,
                    $"設定値 {configured} でワーカー数が 1 未満になりました。");
            }
        });
    }

    /// <summary>
    /// Model 層（GoldenEntry / GoldenHeader / GoldenDocument）の検証項目を登録する（タスク1.2）。
    /// </summary>
    private static void RegisterModelChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenEntry が相対パス・種別・バイトサイズの3項目を保持する", () =>
        {
            var entry = new GoldenEntry(@"foo\bar.txt", GoldenEntryKind.File, 1234L);

            SelfAssert.That(entry.RelativePath == @"foo\bar.txt", "RelativePath が設定した値と一致しません。");
            SelfAssert.That(entry.Kind == GoldenEntryKind.File, "Kind が設定した値と一致しません。");
            SelfAssert.That(entry.SizeInBytes == 1234L, "SizeInBytes が設定した値と一致しません。");
        });

        runner.Add("GoldenEntry.SizeInBytes が64ビット整数（long）で表現される", () =>
        {
            var property = typeof(GoldenEntry).GetProperty(nameof(GoldenEntry.SizeInBytes));

            SelfAssert.That(property != null, "SizeInBytes プロパティが見つかりません。");
            SelfAssert.That(property!.PropertyType == typeof(long), "SizeInBytes の型が long（Int64）ではありません。");
        });

        runner.Add("GoldenEntry が更新日時と所有者を保持する手段を型として持たない", () =>
        {
            // リフレクションで公開プロパティを列挙し、日時型・日時らしき名前・所有者らしき名前が
            // 存在しないことを機械的に確認する（要件1.5: 更新日時および所有者を含めない）。
            var properties = typeof(GoldenEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            SelfAssert.That(
                properties.Length == 3,
                $"GoldenEntry の公開プロパティ数が想定外です（{properties.Length} 件）。相対パス・種別・サイズの3項目のみのはずです。");

            foreach (var property in properties)
            {
                bool looksLikeTimestamp =
                    property.PropertyType == typeof(DateTime) ||
                    property.PropertyType == typeof(DateTime?) ||
                    property.PropertyType == typeof(DateTimeOffset) ||
                    property.PropertyType == typeof(DateTimeOffset?) ||
                    property.Name.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.Name.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.Name.IndexOf("Modified", StringComparison.OrdinalIgnoreCase) >= 0;

                SelfAssert.That(
                    !looksLikeTimestamp,
                    $"GoldenEntry に更新日時を保持しうるプロパティ '{property.Name}' が存在します（要件1.5違反）。");

                bool looksLikeOwner = property.Name.IndexOf("Owner", StringComparison.OrdinalIgnoreCase) >= 0;

                SelfAssert.That(
                    !looksLikeOwner,
                    $"GoldenEntry に所有者を保持しうるプロパティ '{property.Name}' が存在します（要件1.5違反）。");
            }
        });

        runner.Add("GoldenHeader が走査条件のメタデータを保持する", () =>
        {
            var generatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var omissions = new List<string> { @"deep\path" };
            var header = new GoldenHeader(
                formatVersion: 2,
                baseFolderLabel: "fixture-v1",
                baseFolderPathLength: 80,
                generatedAt: generatedAt,
                usePhysicalSize: true,
                clusterSizeInBytes: 4096L,
                fixtureComplete: false,
                fixtureOmissions: omissions);

            SelfAssert.That(header.FormatVersion == 2, "FormatVersion が設定した値と一致しません。");
            SelfAssert.That(header.BaseFolderLabel == "fixture-v1", "BaseFolderLabel が設定した値と一致しません。");
            SelfAssert.That(header.GeneratedAt == generatedAt, "GeneratedAt が設定した値と一致しません。");
            SelfAssert.That(header.UsePhysicalSize, "UsePhysicalSize が設定した値と一致しません。");
            SelfAssert.That(header.ClusterSizeInBytes == 4096L, "ClusterSizeInBytes が設定した値と一致しません。");
            SelfAssert.That(!header.FixtureComplete, "FixtureComplete が設定した値と一致しません。");
            SelfAssert.That(
                header.FixtureOmissions.Count == 1 && header.FixtureOmissions[0] == @"deep\path",
                "FixtureOmissions が設定した値と一致しません。");
        });

        runner.Add("GoldenDocument がヘッダとエントリ集合を束ねる", () =>
        {
            var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var entries = new List<GoldenEntry>
            {
                new GoldenEntry(@"a", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"a\b.txt", GoldenEntryKind.File, 10L),
            };

            var document = new GoldenDocument(header, entries);

            SelfAssert.That(ReferenceEquals(document.Header, header), "Header が設定した値と一致しません。");
            SelfAssert.That(document.Entries.Count == 2, "Entries の件数が設定した値と一致しません。");
        });

        runner.Add("GoldenDocument が相対パスの重複を許容しない", () =>
        {
            var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var duplicated = new List<GoldenEntry>
            {
                new GoldenEntry(@"a\b.txt", GoldenEntryKind.File, 10L),
                new GoldenEntry(@"a\b.txt", GoldenEntryKind.File, 20L),
            };

            bool threw = false;
            try
            {
                _ = new GoldenDocument(header, duplicated);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "相対パスが重複するエントリを与えても例外が発生しませんでした。");
        });
    }

    /// <summary>
    /// Model 層（GoldenEntry / GoldenHeader）の構築時不変条件について、異常系を証明する検証項目を登録する（タスク6.1・穴3）。
    /// タスク1.2のレビュー指摘（tasks.md Implementation Notes）を受けて追加する。
    /// 実装を読んで判明した不変条件（サイズが0以上、基準の論理名が空でない、クラスタサイズが0以上）を
    /// それぞれ違反する値で構築し、例外の型まで照合する。空白のみの値の扱いは要件・設計に定めがないため、
    /// 現状の挙動を固定する検証は置かない（タスク6.1のレビューで判断。tasks.md Implementation Notes に申し送り）。
    /// </summary>
    private static void RegisterModelInvariantEdgeCaseChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenEntry が負のサイズをArgumentOutOfRangeExceptionで拒否する（Invariant: SizeInBytesは0以上）", () =>
        {
            Exception? caught = CaptureConstructionException(() => { _ = new GoldenEntry(@"a\b.txt", GoldenEntryKind.File, -1L); });

            SelfAssert.That(
                caught is ArgumentOutOfRangeException,
                $"負のサイズを与えても ArgumentOutOfRangeException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
        });

        runner.Add("GoldenEntry が空の相対パスをArgumentExceptionで拒否する", () =>
        {
            Exception? caught = CaptureConstructionException(() => { _ = new GoldenEntry(string.Empty, GoldenEntryKind.File, 0L); });

            SelfAssert.That(
                caught is ArgumentException,
                $"空の相対パスを与えても ArgumentException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
        });

        runner.Add("GoldenHeader が空の基準論理名をArgumentExceptionで拒否する", () =>
        {
            Exception? caught = CaptureConstructionException(() =>
            {
                _ = new GoldenHeader(2, string.Empty, 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            });

            SelfAssert.That(
                caught is ArgumentException,
                $"空の基準論理名を与えても ArgumentException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
        });

        runner.Add("GoldenHeader が負のクラスタサイズをArgumentOutOfRangeExceptionで拒否する", () =>
        {
            Exception? caught = CaptureConstructionException(() =>
            {
                _ = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, true, -1L, true, Array.Empty<string>());
            });

            SelfAssert.That(
                caught is ArgumentOutOfRangeException,
                $"負のクラスタサイズを与えても ArgumentOutOfRangeException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
        });

        runner.Add("GoldenHeader が負の基準フォルダの実効絶対パス長をArgumentOutOfRangeExceptionで拒否する（Invariant: BaseFolderPathLengthは0以上、タスク7.1）", () =>
        {
            Exception? caught = CaptureConstructionException(() =>
            {
                _ = new GoldenHeader(2, "fixture-v1", -1, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            });

            SelfAssert.That(
                caught is ArgumentOutOfRangeException,
                $"負の基準フォルダの実効絶対パス長を与えても ArgumentOutOfRangeException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
        });
    }

    /// <summary>指定した構築処理を実行し、送出された例外を返す。例外が発生しなければ null を返す。</summary>
    private static Exception? CaptureConstructionException(Action construct)
    {
        try
        {
            construct();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>捕捉した例外の型名を報告メッセージ用に整形する。例外がなければその旨を返す。</summary>
    private static string DescribeCaughtException(Exception? exception)
    {
        return exception is null ? "例外なし" : (exception.GetType().FullName ?? exception.GetType().Name);
    }

    /// <summary>
    /// Io 層（LongPath）の検証項目を登録する（タスク1.3）。
    /// 260文字を超えるフォルダを実際にファイルシステム上へ作成し、変換の有無で成否が分かれることを確認する。
    /// </summary>
    private static void RegisterLongPathChecks(SelfCheckRunner runner)
    {
        runner.Add("LongPath.Extend が空のパスを拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = LongPath.Extend(string.Empty);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "空のパスを渡しても例外が発生しませんでした。");
        });

        runner.Add("LongPath.Extend が正規化のうえで \\\\?\\ プレフィクスを付与する", () =>
        {
            string relative = ".";
            string expected = @"\\?\" + Path.GetFullPath(relative);

            string result = LongPath.Extend(relative);

            SelfAssert.That(result.StartsWith(@"\\?\", StringComparison.Ordinal), $"戻り値が \\\\?\\ で始まっていません: '{result}'");
            SelfAssert.That(result == expected, $"正規化とプレフィクス付与の結果が想定と異なります: '{result}'");
        });

        runner.Add("LongPath.Extend が付与前に相対表記（..）を正規化する（順序の検証）", () =>
        {
            // \\?\ は正規化されないため、Path.GetFullPath を先に通さなければ ".." が解決されない。
            // 順序が逆であることを検出できるよう、".." を含む相対パスで確認する。
            string baseDir = Path.GetTempPath();
            string withDotDot = Path.Combine(baseDir, "gb_lp_dummy_subdir", "..", "gb_lp_dummy_file.txt");
            string expectedPlain = Path.GetFullPath(withDotDot);

            string result = LongPath.Extend(withDotDot);

            SelfAssert.That(!result.Contains(".."), $"拡張長パスに '..' が残っています（正規化が付与前に行われていません）: '{result}'");
            SelfAssert.That(result == @"\\?\" + expectedPlain, $"正規化後にプレフィクスを付与した結果と一致しません: '{result}'");
        });

        runner.Add("LongPath.Extend が既に拡張長形式のパスを二重に付与しない", () =>
        {
            string already = @"\\?\C:\already\extended\path";
            string result = LongPath.Extend(already);

            SelfAssert.That(result == already, $"既に拡張長形式のパスが変化しました: '{result}'");
            SelfAssert.That(!result.Contains(@"\\?\\\?\"), $"拡張長プレフィクスが二重に付与されています: '{result}'");
        });

        runner.Add("LongPath.Extend が既に拡張長UNC形式のパスを二重に付与しない", () =>
        {
            string already = @"\\?\UNC\server\share\folder";
            string result = LongPath.Extend(already);

            SelfAssert.That(result == already, $"既に拡張長UNC形式のパスが変化しました: '{result}'");
        });

        runner.Add("LongPath.Extend がUNCパスを \\\\?\\UNC\\ 形式に変換する", () =>
        {
            string uncPath = @"\\server\share\folder";
            string expected = @"\\?\UNC\server\share\folder";

            string result = LongPath.Extend(uncPath);

            SelfAssert.That(result == expected, $"UNCパスの変換結果が想定と異なります: '{result}'");
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 変換を通さないプレーンなパスでは260文字超のフォルダ作成が失敗する（.NET Framework の制約）。
        // 新: .NET 10 は長いパスをそのまま扱えるため、プレーンなパスでも作成に成功する。
        runner.Add("変換を通すと260文字を超えるフォルダの作成に成功し、変換を通さなくても成功する（要件3.1）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_lp_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                // 基準パス自体は短く保つ（design.md: FixtureBuilder.Build Preconditions と同じ考え方）。
                Directory.CreateDirectory(root);

                // 260文字を確実に超えるよう、実行環境の一時フォルダの長さから逆算する。
                int desiredTotalLength = 300;
                int segmentLength = Math.Max(desiredTotalLength - (root.Length + 1), 80);
                string longName = new string('a', segmentLength);
                string longPath = Path.Combine(root, longName);

                SelfAssert.That(longPath.Length > 260, $"検証用パスが260文字を超えていません（{longPath.Length}文字）。");

                // 1. 変換を通したパスでは260文字超のフォルダ作成が成功する
                Directory.CreateDirectory(LongPath.Extend(longPath));
                SelfAssert.That(Directory.Exists(LongPath.Extend(longPath)), "変換を通した260文字超のフォルダが作成されていません。");

                // 2. 変換を通さないプレーンなパスでも、同じ深さのフォルダ作成が成功する（.NET 10 移行後の事実）
                string plainName = new string('b', segmentLength);
                string plainPath = Path.Combine(root, plainName);
                SelfAssert.That(plainPath.Length > 260, $"対比用パスが260文字を超えていません（{plainPath.Length}文字）。");

                Exception? plainCreationError = null;
                try
                {
                    Directory.CreateDirectory(plainPath);
                }
                catch (Exception ex)
                {
                    plainCreationError = ex;
                }

                SelfAssert.That(
                    plainCreationError is null,
                    $"変換を通さないプレーンなパスでの260文字超フォルダ作成が失敗しました（{plainCreationError?.GetType().Name}: {plainCreationError?.Message}）。");
                SelfAssert.That(Directory.Exists(plainPath), "変換を通さないプレーンなパスで作成したフォルダが、プレーンなパスで存在すると判定されません。");
                SelfAssert.That(Directory.Exists(LongPath.Extend(plainPath)), "変換を通さないプレーンなパスで作成したフォルダが、変換を通したパスで存在すると判定されません。");
            }
            finally
            {
                // 後始末: 検証の成否に関わらず必ず行う。削除も変換を通した経路で行う。
                if (Directory.Exists(root) || Directory.Exists(LongPath.Extend(root)))
                {
                    Directory.Delete(LongPath.Extend(root), recursive: true);
                }
            }
        });

        runner.Add("日本語を含む260文字超のフォルダを変換を通して作成できる（要件3.2）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_lp_ja_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                Directory.CreateDirectory(root);

                // 数え方は文字数（UTF-16 の WCHAR）。日本語1文字は1コードユニットなので string.Length と一致する。
                int desiredTotalLength = 300;
                int segmentLength = Math.Max(desiredTotalLength - (root.Length + 1), 80);
                string longName = new string('日', segmentLength);
                string longPath = Path.Combine(root, longName);

                SelfAssert.That(longPath.Length > 260, $"検証用パスが260文字を超えていません（{longPath.Length}文字）。");

                Directory.CreateDirectory(LongPath.Extend(longPath));
                SelfAssert.That(Directory.Exists(LongPath.Extend(longPath)), "日本語を含む260文字超のフォルダが作成されていません。");
            }
            finally
            {
                if (Directory.Exists(root) || Directory.Exists(LongPath.Extend(root)))
                {
                    Directory.Delete(LongPath.Extend(root), recursive: true);
                }
            }
        });
    }

    /// <summary>
    /// Io 層（GoldenSerializer）の検証項目を登録する（タスク2.1）。
    /// 期待値データのテキスト形式での読み書きが、design.md の Service Interface と
    /// Data Models の仕様どおりに振る舞うことを確認する。
    /// </summary>
    private static void RegisterGoldenSerializerChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenSerializer が同一のGoldenDocumentから常にバイト単位で同一のファイルを書き出す（Invariants）", () =>
        {
            string path1 = CreateTempGoldenFilePath();
            string path2 = CreateTempGoldenFilePath();
            try
            {
                var document = BuildSampleDocument();
                var serializer = new GoldenSerializer();

                serializer.Write(document, path1);
                serializer.Write(document, path2);

                byte[] bytes1 = File.ReadAllBytes(path1);
                byte[] bytes2 = File.ReadAllBytes(path2);

                SelfAssert.That(bytes1.SequenceEqual(bytes2), "同一の GoldenDocument から書き出した2つのファイルがバイト単位で一致しません。");
            }
            finally
            {
                DeleteIfExists(path1);
                DeleteIfExists(path2);
            }
        });

        runner.Add("GoldenSerializer がエントリの投入順序に依存せず同一の出力を生成する（要件1.3）", () =>
        {
            string pathAscending = CreateTempGoldenFilePath();
            string pathDescending = CreateTempGoldenFilePath();
            try
            {
                var header = new GoldenHeader(2, "fixture-v1", 80, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, 4096L, true, Array.Empty<string>());

                // "co-op" / "coop" / "cop" は、序数(Ordinal)比較と既定のカルチャ依存比較とで
                // 明確に並び順が分かれる組み合わせ（'-' の扱いが異なるため）。
                // 投入順序をさらに変えて、並べ替え後の出力に影響しないことを確認する。
                var entriesAscending = BuildOrderDiscriminatingEntries(new[] { "cop", "co-op", "coop" });
                var entriesDescending = BuildOrderDiscriminatingEntries(new[] { "coop", "cop", "co-op" });

                var serializer = new GoldenSerializer();
                serializer.Write(new GoldenDocument(header, entriesAscending), pathAscending);
                serializer.Write(new GoldenDocument(header, entriesDescending), pathDescending);

                byte[] bytesAscending = File.ReadAllBytes(pathAscending);
                byte[] bytesDescending = File.ReadAllBytes(pathDescending);

                SelfAssert.That(bytesAscending.SequenceEqual(bytesDescending), "エントリの投入順序を変えると出力が変化しました。");

                // 出力そのものが「序数昇順」であることを、期待順序を文字列リテラルで列挙して照合する。
                // 並び順が単に自己無矛盾なだけでなく、実際に Ordinal 比較であることを保証するための検証。
                AssertOrdinalEntryOrder(serializer, pathAscending, "投入順序A");
                AssertOrdinalEntryOrder(serializer, pathDescending, "投入順序B");
            }
            finally
            {
                DeleteIfExists(pathAscending);
                DeleteIfExists(pathDescending);
            }
        });

        runner.Add("GoldenSerializer が Write の後に Read すると等価な文書が得られる（Postconditions）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                var document = BuildSampleDocument();
                var serializer = new GoldenSerializer();

                serializer.Write(document, path);
                var roundTripped = serializer.Read(path);

                SelfAssert.That(roundTripped.Header.FormatVersion == document.Header.FormatVersion, "往復後の FormatVersion が一致しません。");
                SelfAssert.That(roundTripped.Header.BaseFolderLabel == document.Header.BaseFolderLabel, "往復後の BaseFolderLabel が一致しません。");
                SelfAssert.That(roundTripped.Header.BaseFolderPathLength == document.Header.BaseFolderPathLength, "往復後の BaseFolderPathLength が一致しません。");
                SelfAssert.That(roundTripped.Header.GeneratedAt == document.Header.GeneratedAt, "往復後の GeneratedAt が一致しません。");
                SelfAssert.That(roundTripped.Header.UsePhysicalSize == document.Header.UsePhysicalSize, "往復後の UsePhysicalSize が一致しません。");
                SelfAssert.That(roundTripped.Header.ClusterSizeInBytes == document.Header.ClusterSizeInBytes, "往復後の ClusterSizeInBytes が一致しません。");
                SelfAssert.That(roundTripped.Header.FixtureComplete == document.Header.FixtureComplete, "往復後の FixtureComplete が一致しません。");
                SelfAssert.That(
                    roundTripped.Header.FixtureOmissions.SequenceEqual(document.Header.FixtureOmissions, StringComparer.Ordinal),
                    "往復後の FixtureOmissions が一致しません。");

                SelfAssert.That(roundTripped.Entries.Count == document.Entries.Count, "往復後のエントリ件数が一致しません。");
                var originalByPath = document.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
                foreach (var entry in roundTripped.Entries)
                {
                    SelfAssert.That(originalByPath.TryGetValue(entry.RelativePath, out var original), $"往復後のエントリ '{entry.RelativePath}' が元の文書に存在しません。");
                    SelfAssert.That(original!.Kind == entry.Kind, $"往復後のエントリ '{entry.RelativePath}' の Kind が一致しません。");
                    SelfAssert.That(original.SizeInBytes == entry.SizeInBytes, $"往復後のエントリ '{entry.RelativePath}' の SizeInBytes が一致しません。");
                }
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が未知の形式バージョンの読み取りに失敗する（要件7.3）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                string content =
                    "# FormatVersion: 9999\n" +
                    "# BaseFolderLabel: fixture-v1\n" +
                    "# BaseFolderPathLength: 80\n" +
                    "# GeneratedAt: 2026-01-01T00:00:00.0000000+00:00\n" +
                    "# UsePhysicalSize: true\n" +
                    "# ClusterSizeInBytes: 4096\n" +
                    "# FixtureComplete: true\n" +
                    "D\troot\t0\n";
                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));

                var serializer = new GoldenSerializer();

                bool threw = false;
                try
                {
                    _ = serializer.Read(path);
                }
                catch (GoldenFormatException)
                {
                    threw = true;
                }

                SelfAssert.That(threw, "未知の形式バージョンを読んでも失敗しませんでした。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が相対パスにタブ文字を含むエントリの書き込みに失敗する", () =>
        {
            var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var entries = new List<GoldenEntry>
            {
                new GoldenEntry("a\tb", GoldenEntryKind.File, 1L),
            };
            var document = new GoldenDocument(header, entries);
            string path = CreateTempGoldenFilePath();

            try
            {
                var serializer = new GoldenSerializer();

                bool threw = false;
                try
                {
                    serializer.Write(document, path);
                }
                catch (GoldenFormatException)
                {
                    threw = true;
                }

                SelfAssert.That(threw, "相対パスにタブ文字を含むエントリを書き込んでも失敗しませんでした。");
                SelfAssert.That(!File.Exists(path), "書き込みが失敗したにもかかわらずファイルが作成されています。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が壊れたエントリ行（区切りの破損）の読み取りに失敗する", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                string content =
                    "# FormatVersion: 2\n" +
                    "# BaseFolderLabel: fixture-v1\n" +
                    "# BaseFolderPathLength: 80\n" +
                    "# GeneratedAt: 2026-01-01T00:00:00.0000000+00:00\n" +
                    "# UsePhysicalSize: true\n" +
                    "# ClusterSizeInBytes: 4096\n" +
                    "# FixtureComplete: true\n" +
                    "F\ta\tb\tc\t1\n"; // タブが余分に含まれ、区切りが壊れている
                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));

                var serializer = new GoldenSerializer();

                bool threw = false;
                try
                {
                    _ = serializer.Read(path);
                }
                catch (GoldenFormatException)
                {
                    threw = true;
                }

                SelfAssert.That(threw, "区切りが壊れたエントリ行を読んでも失敗しませんでした。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer の出力がBOMなし・LF固定である（要件1.4の一部）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                var document = BuildSampleDocument();
                var serializer = new GoldenSerializer();
                serializer.Write(document, path);

                byte[] bytes = File.ReadAllBytes(path);

                bool startsWithBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                SelfAssert.That(!startsWithBom, "出力ファイルの先頭にUTF-8 BOMが付与されています。");

                string text = new UTF8Encoding(false).GetString(bytes);
                SelfAssert.That(!text.Contains("\r"), "出力に CR が含まれています。改行は LF に固定される必要があります。");
                SelfAssert.That(text.Contains("\n"), "出力に改行(LF)が含まれていません。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer の出力がカルチャに依存しない（数値・日時とも）", () =>
        {
            string pathInvariant = CreateTempGoldenFilePath();
            string pathGerman = CreateTempGoldenFilePath();
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                // BuildSampleDocument() の固定データ（root, root\a.txt 等）はたまたま
                // Ordinal と既定のカルチャ依存比較で並び順が一致してしまうため、
                // 数値・日時の表記に加えて、並び順でも比較規則の違いが必ず現れるデータを使う。
                var document = BuildOrderDiscriminatingDocument();
                var serializer = new GoldenSerializer();

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                serializer.Write(document, pathInvariant);

                // de-DE は小数点がカンマ、桁区切りがピリオドになるロケール。数値・日時の表記が変化しないことを確認する。
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                serializer.Write(document, pathGerman);

                byte[] bytesInvariant = File.ReadAllBytes(pathInvariant);
                byte[] bytesGerman = File.ReadAllBytes(pathGerman);

                SelfAssert.That(bytesInvariant.SequenceEqual(bytesGerman), "CurrentCulture を de-DE に切り替えると出力が変化しました。");

                // 「Invariant と de-DE の出力が一致する」だけでは、両者が同じ既定比較（カルチャ依存）に
                // 差し替わっていても検出できない（design.md: 序数昇順であること自体を照合する必要がある）。
                AssertOrdinalEntryOrder(serializer, pathInvariant, "InvariantCulture");
                AssertOrdinalEntryOrder(serializer, pathGerman, "de-DE");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                DeleteIfExists(pathInvariant);
                DeleteIfExists(pathGerman);
            }
        });
    }

    /// <summary>
    /// ヘッダの値の改行検査（design.md: GoldenSerializer の Risks、タスク6.4）の検証項目を登録する。
    /// ヘッダの値に CR または LF が含まれると行指向の形式が壊れ、書き出しは成功しても読み戻せなくなる
    /// （CR が行末にあると読み取り側の CR 除去で黙って値が変わる）。書き込み前に検出して
    /// GoldenFormatException で失敗させ、ファイルを書き出さないことを確認する。
    /// </summary>
    private static void RegisterGoldenSerializerHeaderNewlineChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenSerializer がヘッダの値（FixtureOmission・BaseFolderLabel のそれぞれ）に LF を含む文書の書き出しを GoldenFormatException で拒否し、ファイルを作らない（design.md GoldenSerializer Risks、タスク6.4）", () =>
        {
            AssertHeaderNewlineRejectedWithoutFile("\n", "LF");
        });

        runner.Add("GoldenSerializer がヘッダの値（FixtureOmission・BaseFolderLabel のそれぞれ）に CR を含む文書の書き出しを GoldenFormatException で拒否し、ファイルを作らない（design.md GoldenSerializer Risks、タスク6.4）", () =>
        {
            AssertHeaderNewlineRejectedWithoutFile("\r", "CR");
        });

        runner.Add("GoldenSerializer がヘッダの値に改行（LF・CR）を含む文書の書き出しに失敗したとき、既存の期待値ファイルの内容を変えない（タスク6.4）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                var serializer = new GoldenSerializer();
                serializer.Write(BuildHeaderValueDocument("fixture-v1", new[] { "normal: IOException" }), path);
                byte[] before = File.ReadAllBytes(path);

                var cases = BuildNewlineHeaderDocuments("\n", "LF").Concat(BuildNewlineHeaderDocuments("\r", "CR"));
                foreach (var (label, document) in cases)
                {
                    Exception? caught = CaptureConstructionException(() => serializer.Write(document, path));
                    SelfAssert.That(
                        caught is GoldenFormatException,
                        $"{label}: 既存ファイルへの書き出しが GoldenFormatException で失敗しませんでした（実際: {DescribeCaughtException(caught)}）。");
                    SelfAssert.That(
                        File.ReadAllBytes(path).SequenceEqual(before),
                        $"{label}: 書き出しに失敗したにもかかわらず、既存の期待値ファイルの内容が変化しています。");
                }
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が改行を含まないヘッダの値（「相対パス: 例外の型名」の形の FixtureOmission を含む）を従来どおり書き出し、読み戻すと一致する（タスク6.4）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                var omissions = new[] { "normal: IOException", @"日本語フォルダ\日本語ファイル.txt: DirectoryNotFoundException" };
                var document = BuildHeaderValueDocument("fixture-v1", omissions);
                var serializer = new GoldenSerializer();

                Exception? caught = CaptureConstructionException(() => serializer.Write(document, path));
                SelfAssert.That(
                    caught is null,
                    $"改行を含まないヘッダの値の書き出しが失敗しました（実際: {DescribeCaughtException(caught)}: {caught?.Message}）。");
                SelfAssert.That(File.Exists(path), "改行を含まないヘッダの値なのに期待値ファイルが書き出されていません。");

                string[] lines = new UTF8Encoding(false).GetString(File.ReadAllBytes(path)).Split('\n');
                foreach (var omission in omissions)
                {
                    string expectedLine = "# FixtureOmission: " + omission;
                    SelfAssert.That(
                        lines.Count(line => line == expectedLine) == 1,
                        $"期待値ファイルに行 '{expectedLine}' がちょうど1行ありません。");
                }

                var roundTripped = serializer.Read(path);
                SelfAssert.That(roundTripped.Header.BaseFolderLabel == "fixture-v1", $"読み戻した BaseFolderLabel が一致しません（実際: '{roundTripped.Header.BaseFolderLabel}'）。");
                SelfAssert.That(
                    roundTripped.Header.FixtureOmissions.SequenceEqual(omissions, StringComparer.Ordinal),
                    $"読み戻した FixtureOmissions が一致しません（実際: [{string.Join(" | ", roundTripped.Header.FixtureOmissions)}]）。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });
    }

    /// <summary>
    /// 指定した改行文字を含むヘッダの値を持つ文書を、値の種類（FixtureOmission / BaseFolderLabel）ごとに1件ずつ組み立てる。
    /// どちらか一方の値だけを検査する実装を見逃さないよう、改行はそれぞれの文書で1つの値にだけ含める。
    /// </summary>
    private static List<(string Label, GoldenDocument Document)> BuildNewlineHeaderDocuments(string newline, string newlineName)
    {
        return new List<(string Label, GoldenDocument Document)>
        {
            ($"FixtureOmission の値の末尾に {newlineName}", BuildHeaderValueDocument("fixture-v1", new[] { "empty_folder: IOException", "normal: IOException" + newline })),
            ($"BaseFolderLabel の値の途中に {newlineName}", BuildHeaderValueDocument("fixture" + newline + "v1", new[] { "normal: IOException" })),
        };
    }

    /// <summary>
    /// ヘッダの値の検証に用いる、エントリ1件の最小限の文書を組み立てる。
    /// </summary>
    private static GoldenDocument BuildHeaderValueDocument(string baseFolderLabel, IReadOnlyList<string> fixtureOmissions)
    {
        var header = new GoldenHeader(
            formatVersion: 2,
            baseFolderLabel: baseFolderLabel,
            baseFolderPathLength: 80,
            generatedAt: new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
            usePhysicalSize: false,
            clusterSizeInBytes: 0L,
            fixtureComplete: fixtureOmissions.Count == 0,
            fixtureOmissions: fixtureOmissions);

        return new GoldenDocument(header, new List<GoldenEntry> { new GoldenEntry("normal", GoldenEntryKind.File, 8L) });
    }

    /// <summary>
    /// 改行を含むヘッダの値の書き出しが GoldenFormatException で失敗し、出力先にファイルが作られないことを、
    /// 値の種類ごとに新しい出力先で確認する。
    /// </summary>
    private static void AssertHeaderNewlineRejectedWithoutFile(string newline, string newlineName)
    {
        var serializer = new GoldenSerializer();
        foreach (var (label, document) in BuildNewlineHeaderDocuments(newline, newlineName))
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                Exception? caught = CaptureConstructionException(() => serializer.Write(document, path));
                SelfAssert.That(
                    caught is GoldenFormatException,
                    $"{label}: 書き出しが GoldenFormatException で失敗しませんでした（実際: {DescribeCaughtException(caught)}）。");
                SelfAssert.That(!File.Exists(path), $"{label}: 書き出しが失敗したにもかかわらずファイルが作成されています。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }
    }

    /// <summary>
    /// 序数(Ordinal)比較と既定のカルチャ依存比較とで並び順が明確に分かれる相対パスの組み合わせ。
    /// Ordinal: "co-op" &lt; "coop" &lt; "cop"（'-' はコードポイント45、'o' や 'p' より小さいため）。
    /// 既定のカルチャ依存比較（InvariantCulture・de-DE のいずれも）: "coop" &lt; "co-op" &lt; "cop"
    /// （ハイフンを実質的に無視する単語ソートのため）。
    /// この差異により、GoldenSerializer の並び替えが StringComparer.Ordinal から
    /// 既定の比較規則へ差し替わった場合に、検証項目が確実に失敗する。
    /// </summary>
    private static readonly string[] OrdinalExpectedOrder = { "co-op", "coop", "cop" };

    /// <summary>
    /// <see cref="OrdinalExpectedOrder"/> と同じ集合を、指定した投入順序で GoldenEntry の一覧として組み立てる。
    /// </summary>
    private static List<GoldenEntry> BuildOrderDiscriminatingEntries(IReadOnlyList<string> relativePathsInInsertionOrder)
    {
        var entries = new List<GoldenEntry>();
        foreach (var relativePath in relativePathsInInsertionOrder)
        {
            entries.Add(new GoldenEntry(relativePath, GoldenEntryKind.Folder, 0L));
        }

        return entries;
    }

    /// <summary>
    /// カルチャ非依存性の検証で使う、並び順が比較規則によって分かれるエントリ集合を持つ GoldenDocument を組み立てる。
    /// ヘッダの数値・日時は BuildSampleDocument() と同様に代表的な値を使う。
    /// </summary>
    private static GoldenDocument BuildOrderDiscriminatingDocument()
    {
        var header = new GoldenHeader(
            formatVersion: 2,
            baseFolderLabel: "fixture-v1",
            baseFolderPathLength: 80,
            generatedAt: new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero),
            usePhysicalSize: true,
            clusterSizeInBytes: 4096L,
            fixtureComplete: false,
            fixtureOmissions: new List<string> { @"denied\folder" });

        var entries = BuildOrderDiscriminatingEntries(new[] { "cop", "co-op", "coop" });

        return new GoldenDocument(header, entries);
    }

    /// <summary>
    /// 指定した期待値ファイルを読み取り、エントリの並び順が <see cref="OrdinalExpectedOrder"/> と
    /// 文字列リテラルの並びとして厳密に一致することを照合する（要件1.3・design.md「序数昇順」）。
    /// 「並び順が自己無矛盾である」ことだけでなく、実際に Ordinal 比較であることを保証するための検証。
    /// </summary>
    private static void AssertOrdinalEntryOrder(GoldenSerializer serializer, string path, string context)
    {
        var document = serializer.Read(path);
        var actualOrder = document.Entries.Select(entry => entry.RelativePath).ToArray();

        SelfAssert.That(
            actualOrder.SequenceEqual(OrdinalExpectedOrder, StringComparer.Ordinal),
            $"{context}: エントリの並び順が期待する序数(Ordinal)昇順 [{string.Join(", ", OrdinalExpectedOrder)}] と一致しません" +
            $"（実際: [{string.Join(", ", actualOrder)}]）。比較規則が StringComparer.Ordinal でない可能性があります。");
    }

    /// <summary>
    /// GoldenSerializer の検証で共通して使う、代表的な GoldenDocument を組み立てる。
    /// </summary>
    private static GoldenDocument BuildSampleDocument()
    {
        var header = new GoldenHeader(
            formatVersion: 2,
            baseFolderLabel: "fixture-v1",
            baseFolderPathLength: 80,
            generatedAt: new DateTimeOffset(2026, 1, 1, 12, 34, 56, TimeSpan.Zero),
            usePhysicalSize: true,
            clusterSizeInBytes: 4096L,
            fixtureComplete: false,
            fixtureOmissions: new List<string> { @"denied\folder", @"deep\path\日本語" });

        var entries = new List<GoldenEntry>
        {
            new GoldenEntry(@"root", GoldenEntryKind.Folder, 0L),
            new GoldenEntry(@"root\日本語.txt", GoldenEntryKind.File, 123456789L),
            new GoldenEntry(@"root\empty", GoldenEntryKind.Folder, 0L),
            new GoldenEntry(@"root\a.txt", GoldenEntryKind.File, 0L),
        };

        return new GoldenDocument(header, entries);
    }

    /// <summary>
    /// 検証用の一時的な期待値ファイルパスを生成する。ファイル自体はまだ作成しない。
    /// </summary>
    private static string CreateTempGoldenFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "gb_ser_" + Guid.NewGuid().ToString("N") + ".golden.txt");
    }

    /// <summary>
    /// 検証用の一時ファイルが存在すれば削除する。存在しなくても例外にしない。
    /// </summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Io 層（GoldenSerializer）のうち、手書きの期待値テキストが正しく読み込めることを検証する項目を登録する（タスク6.1・穴1）。
    /// <see cref="GoldenSerializer.Write"/> を一切経由せず、文字列リテラルのみで組み立てたテキストを直接
    /// ファイルへ書き、<see cref="GoldenSerializer.Read"/> の結果をリテラルの期待値と1フィールドずつ照合する。
    /// CRLF 版も別途組み立て、Io/GoldenSerializer.cs の TrimTrailingCarriageReturn（159行付近）の経路を
    /// 実際に通すことまで確認する。
    /// </summary>
    private static void RegisterHandWrittenGoldenFileChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenSerializer が手書きの期待値テキスト（LF）を全フィールド一致で読み込む", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                string content = BuildHandWrittenGoldenText("\n");
                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));

                var serializer = new GoldenSerializer();
                var document = serializer.Read(path);

                AssertMatchesHandWrittenExpectation(document, "LF版");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が手書きの期待値テキスト（CRLF）を全フィールド一致で読み込む（CR除去の経路、要件7.4）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                string content = BuildHandWrittenGoldenText("\r\n");

                // 検証データ自体がCRLFを含むことを確認してから使う（形骸化防止）。
                SelfAssert.That(content.Contains("\r\n"), "検証用テキストにCRLFが含まれていません。");

                File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));

                var serializer = new GoldenSerializer();
                var document = serializer.Read(path);

                AssertMatchesHandWrittenExpectation(document, "CRLF版");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が手書きテキストのLF版とCRLF版から完全に同一の内容を読み込む", () =>
        {
            string pathLf = CreateTempGoldenFilePath();
            string pathCrLf = CreateTempGoldenFilePath();
            try
            {
                File.WriteAllBytes(pathLf, new UTF8Encoding(false).GetBytes(BuildHandWrittenGoldenText("\n")));
                File.WriteAllBytes(pathCrLf, new UTF8Encoding(false).GetBytes(BuildHandWrittenGoldenText("\r\n")));

                var serializer = new GoldenSerializer();
                var documentLf = serializer.Read(pathLf);
                var documentCrLf = serializer.Read(pathCrLf);

                SelfAssert.That(documentLf.Header.FormatVersion == documentCrLf.Header.FormatVersion, "LF版とCRLF版でFormatVersionが一致しません。");
                SelfAssert.That(documentLf.Header.BaseFolderPathLength == documentCrLf.Header.BaseFolderPathLength, "LF版とCRLF版でBaseFolderPathLengthが一致しません。");
                SelfAssert.That(documentLf.Header.BaseFolderLabel == documentCrLf.Header.BaseFolderLabel, "LF版とCRLF版でBaseFolderLabelが一致しません。");
                SelfAssert.That(documentLf.Header.GeneratedAt == documentCrLf.Header.GeneratedAt, "LF版とCRLF版でGeneratedAtが一致しません。");
                SelfAssert.That(documentLf.Header.UsePhysicalSize == documentCrLf.Header.UsePhysicalSize, "LF版とCRLF版でUsePhysicalSizeが一致しません。");
                SelfAssert.That(documentLf.Header.ClusterSizeInBytes == documentCrLf.Header.ClusterSizeInBytes, "LF版とCRLF版でClusterSizeInBytesが一致しません。");
                SelfAssert.That(documentLf.Header.FixtureComplete == documentCrLf.Header.FixtureComplete, "LF版とCRLF版でFixtureCompleteが一致しません。");
                SelfAssert.That(
                    documentLf.Header.FixtureOmissions.SequenceEqual(documentCrLf.Header.FixtureOmissions, StringComparer.Ordinal),
                    "LF版とCRLF版でFixtureOmissionsが一致しません。");

                SelfAssert.That(documentLf.Entries.Count == documentCrLf.Entries.Count, "LF版とCRLF版でエントリ件数が一致しません。");
                for (int i = 0; i < documentLf.Entries.Count; i++)
                {
                    SelfAssert.That(documentLf.Entries[i].RelativePath == documentCrLf.Entries[i].RelativePath, $"LF版とCRLF版でエントリ{i}のRelativePathが一致しません。");
                    SelfAssert.That(documentLf.Entries[i].Kind == documentCrLf.Entries[i].Kind, $"LF版とCRLF版でエントリ{i}のKindが一致しません。");
                    SelfAssert.That(documentLf.Entries[i].SizeInBytes == documentCrLf.Entries[i].SizeInBytes, $"LF版とCRLF版でエントリ{i}のSizeInBytesが一致しません。");
                }
            }
            finally
            {
                DeleteIfExists(pathLf);
                DeleteIfExists(pathCrLf);
            }
        });
    }

    private const string HandWrittenBaseFolderLabel = "手書き検証用基準";
    private const string HandWrittenBaseFolderPathLengthText = "97";
    private const int HandWrittenBaseFolderPathLengthExpected = 97;
    private const string HandWrittenGeneratedAtText = "2026-03-14T09:26:53.0000000+09:00";
    private static readonly DateTimeOffset HandWrittenGeneratedAtExpected = new DateTimeOffset(2026, 3, 14, 9, 26, 53, TimeSpan.FromHours(9));
    private const string HandWrittenFixtureOmission1 = @"除外候補\深い\1つ目";
    private const string HandWrittenFixtureOmission2 = @"除外候補\深い\2つ目";

    /// <summary>
    /// <see cref="GoldenSerializer.Write"/> を一切経由せず、文字列リテラルのみで手書きの期待値テキストを組み立てる。
    /// ヘッダの全キー（FormatVersion/BaseFolderLabel/BaseFolderPathLength/GeneratedAt/UsePhysicalSize/ClusterSizeInBytes/FixtureComplete）、
    /// FixtureOmission の複数行、D と F のエントリ（日本語を含む相対パス）を持つ。
    /// </summary>
    private static string BuildHandWrittenGoldenText(string newline)
    {
        var builder = new StringBuilder();
        void AppendLine(string line) => builder.Append(line).Append(newline);

        AppendLine("# FormatVersion: 2");
        AppendLine("# BaseFolderLabel: " + HandWrittenBaseFolderLabel);
        AppendLine("# BaseFolderPathLength: " + HandWrittenBaseFolderPathLengthText);
        AppendLine("# GeneratedAt: " + HandWrittenGeneratedAtText);
        AppendLine("# UsePhysicalSize: true");
        AppendLine("# ClusterSizeInBytes: 4096");
        AppendLine("# FixtureComplete: false");
        AppendLine("# FixtureOmission: " + HandWrittenFixtureOmission1);
        AppendLine("# FixtureOmission: " + HandWrittenFixtureOmission2);
        AppendLine("D\t資料\t0");
        AppendLine("F\t資料\\日本語ファイル.txt\t12345");
        AppendLine("F\t資料\\空ファイル.dat\t0");

        return builder.ToString();
    }

    /// <summary>
    /// <see cref="BuildHandWrittenGoldenText"/> で組み立てたテキストを読み込んだ結果を、
    /// リテラルの期待値と1フィールドずつ照合する。
    /// </summary>
    private static void AssertMatchesHandWrittenExpectation(GoldenDocument document, string context)
    {
        SelfAssert.That(document.Header.FormatVersion == 2, $"{context}: FormatVersion が2と一致しません（実際: {document.Header.FormatVersion}）。");
        SelfAssert.That(document.Header.BaseFolderPathLength == HandWrittenBaseFolderPathLengthExpected, $"{context}: BaseFolderPathLength が{HandWrittenBaseFolderPathLengthExpected}と一致しません（実際: {document.Header.BaseFolderPathLength}）。");
        SelfAssert.That(document.Header.BaseFolderLabel == HandWrittenBaseFolderLabel, $"{context}: BaseFolderLabel がリテラルと一致しません（実際: '{document.Header.BaseFolderLabel}'）。");
        SelfAssert.That(document.Header.GeneratedAt == HandWrittenGeneratedAtExpected, $"{context}: GeneratedAt がリテラルと一致しません（実際: {document.Header.GeneratedAt:o}）。");
        SelfAssert.That(document.Header.UsePhysicalSize, $"{context}: UsePhysicalSize が true と一致しません。");
        SelfAssert.That(document.Header.ClusterSizeInBytes == 4096L, $"{context}: ClusterSizeInBytes が4096と一致しません（実際: {document.Header.ClusterSizeInBytes}）。");
        SelfAssert.That(!document.Header.FixtureComplete, $"{context}: FixtureComplete が false と一致しません。");

        SelfAssert.That(document.Header.FixtureOmissions.Count == 2, $"{context}: FixtureOmissions の件数が2と一致しません（実際: {document.Header.FixtureOmissions.Count}）。");
        SelfAssert.That(document.Header.FixtureOmissions[0] == HandWrittenFixtureOmission1, $"{context}: FixtureOmissions[0] がリテラルと一致しません（実際: '{document.Header.FixtureOmissions[0]}'）。");
        SelfAssert.That(document.Header.FixtureOmissions[1] == HandWrittenFixtureOmission2, $"{context}: FixtureOmissions[1] がリテラルと一致しません（実際: '{document.Header.FixtureOmissions[1]}'）。");

        SelfAssert.That(document.Entries.Count == 3, $"{context}: エントリ件数が3と一致しません（実際: {document.Entries.Count}）。");

        var entry0 = document.Entries[0];
        SelfAssert.That(entry0.RelativePath == "資料", $"{context}: エントリ0のRelativePathが一致しません（実際: '{entry0.RelativePath}'）。");
        SelfAssert.That(entry0.Kind == GoldenEntryKind.Folder, $"{context}: エントリ0のKindがFolderと一致しません（実際: {entry0.Kind}）。");
        SelfAssert.That(entry0.SizeInBytes == 0L, $"{context}: エントリ0のSizeInBytesが0と一致しません（実際: {entry0.SizeInBytes}）。");

        var entry1 = document.Entries[1];
        SelfAssert.That(entry1.RelativePath == @"資料\日本語ファイル.txt", $"{context}: エントリ1のRelativePathが一致しません（実際: '{entry1.RelativePath}'）。");
        SelfAssert.That(entry1.Kind == GoldenEntryKind.File, $"{context}: エントリ1のKindがFileと一致しません（実際: {entry1.Kind}）。");
        SelfAssert.That(entry1.SizeInBytes == 12345L, $"{context}: エントリ1のSizeInBytesが12345と一致しません（実際: {entry1.SizeInBytes}）。");

        var entry2 = document.Entries[2];
        SelfAssert.That(entry2.RelativePath == @"資料\空ファイル.dat", $"{context}: エントリ2のRelativePathが一致しません（実際: '{entry2.RelativePath}'）。");
        SelfAssert.That(entry2.Kind == GoldenEntryKind.File, $"{context}: エントリ2のKindがFileと一致しません（実際: {entry2.Kind}）。");
        SelfAssert.That(entry2.SizeInBytes == 0L, $"{context}: エントリ2のSizeInBytesが0と一致しません（実際: {entry2.SizeInBytes}）。");
    }

    /// <summary>
    /// Io 層（GoldenSerializer / LongPath / Model）の読み書き経路が、被テストアプリ（アセンブリ名
    /// LargeFolderFinder）や MessagePack / YamlDotNet といった型に依存しないことを検証する項目を登録する
    /// （タスク6.1・穴2、要件7.2・1.6）。
    /// ツールは本体をプロジェクト参照しているため、アセンブリ単位の参照関係では判定できない
    /// （ツールのアセンブリが本体を参照しているのは正常な状態）。そのため型・メソッド本体の単位で、
    /// フィールド型・引数と戻り値の型・基底型とインターフェース・ローカル変数の型・IL が参照する
    /// メンバーと型を機械的に走査する。コンパイラが生成する入れ子の型（ラムダやクロージャが変換される
    /// &lt;&gt;c や &lt;&gt;c__DisplayClass 等）も対象に含める。標準ライブラリの型のみで実現し、
    /// 検証コード自体が MessagePack 等へ新たなコンパイル時依存を持ち込まないようにする。
    /// </summary>
    private static void RegisterGoldenSerializerDependencyBoundaryChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenSerializerの読み書き経路（型・IL）が被テストアプリの型に依存しない（要件7.2, 1.6）", () =>
        {
            var rootTypes = new[]
            {
                typeof(GoldenSerializer),
                typeof(IGoldenSerializer),
                typeof(GoldenFormatException),
                typeof(LongPath),
                typeof(GoldenEntry),
                typeof(GoldenEntryKind),
                typeof(GoldenHeader),
                typeof(GoldenDocument),
            };

            Assembly toolAssembly = typeof(GoldenSerializer).Assembly;
            var visited = new HashSet<Type>();
            var toProcess = new Stack<Type>();
            var violations = new List<string>();

            foreach (var rootType in rootTypes)
            {
                if (visited.Add(rootType))
                {
                    toProcess.Push(rootType);
                }
            }

            while (toProcess.Count > 0)
            {
                Type current = toProcess.Pop();
                WalkTypeForDependencyViolations(current, toolAssembly, visited, toProcess, violations);
            }

            SelfAssert.That(
                violations.Count == 0,
                $"読み書き経路に禁止された依存が見つかりました（{violations.Count}件）:\n" + string.Join("\n", violations));
        });
    }

    /// <summary>読み書き経路の検証で、依存を許さないアセンブリの名前（アセンブリ名の文字列で判定する）。</summary>
    private static readonly string[] ForbiddenDependencyAssemblyNames =
    {
        "LargeFolderFinder",
        "MessagePack",
        "MessagePack.Annotations",
        "YamlDotNet",
    };

    private const BindingFlags DependencyMemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const BindingFlags DependencyNestedTypeFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    /// 1バイト命令の表（<see cref="OpCodes"/> のフィールドから機械的に構築する）。
    /// </summary>
    private static readonly OpCode[] SingleByteOpCodes = BuildSingleByteOpCodeTable();

    /// <summary>
    /// 0xFE 接頭の2バイト命令の表（第2バイトで引く）。
    /// </summary>
    private static readonly OpCode[] MultiByteOpCodes = BuildMultiByteOpCodeTable();

    private static OpCode[] BuildSingleByteOpCodeTable()
    {
        var table = new OpCode[256];
        foreach (var opCode in EnumerateOpCodesFromReflection())
        {
            ushort value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                table[value] = opCode;
            }
        }

        return table;
    }

    private static OpCode[] BuildMultiByteOpCodeTable()
    {
        var table = new OpCode[256];
        foreach (var opCode in EnumerateOpCodesFromReflection())
        {
            ushort value = unchecked((ushort)opCode.Value);
            if ((value & 0xFF00) == 0xFE00)
            {
                table[value & 0xFF] = opCode;
            }
        }

        return table;
    }

    /// <summary>
    /// <c>typeof(OpCodes).GetFields()</c> から、型が OpCode である公開静的フィールドの値を列挙する。
    /// </summary>
    private static IEnumerable<OpCode> EnumerateOpCodesFromReflection()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
            {
                continue;
            }

            object? value = field.GetValue(null);
            if (value is OpCode opCode)
            {
                yield return opCode;
            }
        }
    }

    /// <summary>
    /// メソッド本体のIL命令列を走査し、フィールド・メソッド・型への参照を持つ命令（InlineField /
    /// InlineMethod / InlineType / InlineTok）のメタデータトークンを抽出する。
    /// </summary>
    private static List<int> ExtractResolvableMetadataTokens(byte[] il)
    {
        var tokens = new List<int>();
        int position = 0;

        while (position < il.Length)
        {
            byte codeByte = il[position];
            OpCode opCode;

            if (codeByte == 0xFE)
            {
                byte secondByte = il[position + 1];
                opCode = MultiByteOpCodes[secondByte];
                position += 2;
            }
            else
            {
                opCode = SingleByteOpCodes[codeByte];
                position += 1;
            }

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;

                case OperandType.InlineVar:
                    position += 2;
                    break;

                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.ShortInlineR:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                    position += 4;
                    break;

                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    tokens.Add(BitConverter.ToInt32(il, position));
                    position += 4;
                    break;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;

                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, position);
                    position += 4 + (count * 4);
                    break;

                default:
                    throw new NotSupportedException($"未対応のILオペランド種別です: {opCode.OperandType}（依存境界の検証を正しく行えません）。");
            }
        }

        return tokens;
    }

    /// <summary>
    /// 1つの型について、入れ子の型・基底型・インターフェース・フィールド・メソッド（引数/戻り値/
    /// ローカル変数/IL参照）を走査し、禁止された依存先への参照を violations へ蓄積する。
    /// ツール自身のアセンブリに属する型は再帰的に走査対象へ積み、それ以外（標準ライブラリや
    /// 禁止対象を含む）はアセンブリ名の確認のみで走査を打ち切る。
    /// </summary>
    private static void WalkTypeForDependencyViolations(
        Type type,
        Assembly toolAssembly,
        HashSet<Type> visited,
        Stack<Type> toProcess,
        List<string> violations)
    {
        foreach (var nested in type.GetNestedTypes(DependencyNestedTypeFlags))
        {
            if (visited.Add(nested))
            {
                toProcess.Push(nested);
            }
        }

        CheckDependencyTypeReference(type.BaseType, toolAssembly, $"{type.FullName} の基底型", visited, toProcess, violations);
        foreach (var iface in type.GetInterfaces())
        {
            CheckDependencyTypeReference(iface, toolAssembly, $"{type.FullName} が実装するインターフェース", visited, toProcess, violations);
        }

        foreach (var field in type.GetFields(DependencyMemberFlags))
        {
            CheckDependencyTypeReference(field.FieldType, toolAssembly, $"{type.FullName}.{field.Name} のフィールド型", visited, toProcess, violations);
        }

        var methodBases = new List<MethodBase>();
        methodBases.AddRange(type.GetConstructors(DependencyMemberFlags));
        methodBases.AddRange(type.GetMethods(DependencyMemberFlags));

        foreach (var method in methodBases)
        {
            foreach (var parameter in method.GetParameters())
            {
                CheckDependencyTypeReference(parameter.ParameterType, toolAssembly, $"{type.FullName}.{method.Name} の引数 '{parameter.Name}'", visited, toProcess, violations);
            }

            if (method is MethodInfo methodInfo)
            {
                CheckDependencyTypeReference(methodInfo.ReturnType, toolAssembly, $"{type.FullName}.{method.Name} の戻り値", visited, toProcess, violations);
            }

            MethodBody? body;
            try
            {
                body = method.GetMethodBody();
            }
            catch (Exception)
            {
                body = null;
            }

            if (body is null)
            {
                continue;
            }

            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                CheckDependencyTypeReference(local.LocalType, toolAssembly, $"{type.FullName}.{method.Name} のローカル変数", visited, toProcess, violations);
            }

            byte[]? il;
            try
            {
                il = body.GetILAsByteArray();
            }
            catch (Exception)
            {
                il = null;
            }

            if (il is null)
            {
                continue;
            }

            foreach (int token in ExtractResolvableMetadataTokens(il))
            {
                MemberInfo? resolved;
                try
                {
                    resolved = method.Module.ResolveMember(token);
                }
                catch (Exception)
                {
                    continue;
                }

                string context = $"{type.FullName}.{method.Name} のIL参照";

                switch (resolved)
                {
                    case Type resolvedType:
                        CheckDependencyTypeReference(resolvedType, toolAssembly, context, visited, toProcess, violations);
                        break;

                    case FieldInfo resolvedField:
                        CheckDependencyTypeReference(resolvedField.DeclaringType, toolAssembly, context + "（フィールドの宣言型）", visited, toProcess, violations);
                        CheckDependencyTypeReference(resolvedField.FieldType, toolAssembly, context + "（フィールド型）", visited, toProcess, violations);
                        break;

                    case MethodBase resolvedMethod:
                        CheckDependencyTypeReference(resolvedMethod.DeclaringType, toolAssembly, context + "（メソッドの宣言型）", visited, toProcess, violations);
                        foreach (var parameter in resolvedMethod.GetParameters())
                        {
                            CheckDependencyTypeReference(parameter.ParameterType, toolAssembly, context + "（メソッド引数）", visited, toProcess, violations);
                        }

                        if (resolvedMethod is MethodInfo resolvedMethodInfo)
                        {
                            CheckDependencyTypeReference(resolvedMethodInfo.ReturnType, toolAssembly, context + "（メソッド戻り値）", visited, toProcess, violations);
                        }

                        break;
                }
            }
        }
    }

    /// <summary>
    /// 型への参照1件を検査する。配列・ジェネリックはそれぞれ要素型・型引数まで再帰的に確認する。
    /// 禁止されたアセンブリに属していれば violations へ追加し、ツール自身のアセンブリに属する型は
    /// 未訪問であれば走査対象へ積む。
    /// </summary>
    private static void CheckDependencyTypeReference(
        Type? type,
        Assembly toolAssembly,
        string context,
        HashSet<Type> visited,
        Stack<Type> toProcess,
        List<string> violations)
    {
        if (type is null || type.IsGenericParameter)
        {
            return;
        }

        if (type.HasElementType)
        {
            CheckDependencyTypeReference(type.GetElementType(), toolAssembly, context, visited, toProcess, violations);
            return;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            CheckDependencyTypeReference(type.GetGenericTypeDefinition(), toolAssembly, context, visited, toProcess, violations);
            foreach (var argument in type.GetGenericArguments())
            {
                CheckDependencyTypeReference(argument, toolAssembly, context, visited, toProcess, violations);
            }

            return;
        }

        string? assemblyName = type.Assembly.GetName().Name;
        if (assemblyName is not null && Array.IndexOf(ForbiddenDependencyAssemblyNames, assemblyName) >= 0)
        {
            violations.Add($"{context}: 型 '{type.FullName}'（アセンブリ '{assemblyName}'）への依存は禁止されています。");
        }

        if (ReferenceEquals(type.Assembly, toolAssembly) && visited.Add(type))
        {
            toProcess.Push(type);
        }
    }

    /// <summary>
    /// Compare 層（BaselineComparer / DiffReport）の検証項目を登録する（タスク2.2）。
    /// design.md の「比較の判定」フロー（走査条件の照合を突き合わせより先に行う）と、
    /// DiffKind の4分類・Match/Different/SettingsMismatch の3値判定を検証する。
    /// </summary>
    private static void RegisterBaselineComparerChecks(SelfCheckRunner runner)
    {
        runner.Add("BaselineComparer がサイズ不一致を分類し、期待値と実測値の双方を報告する（要件4.2）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 100L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 200L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"サイズ不一致があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 1, $"差分の件数が想定と異なります（実際: {report.Entries.Count}件）。");

            var diff = report.Entries[0];
            SelfAssert.That(diff.Kind == DiffKind.SizeMismatch, $"差分の種別が SizeMismatch ではありません（実際: {diff.Kind}）。");
            SelfAssert.That(diff.RelativePath == @"a\file.bin", $"RelativePath が想定と異なります（実際: '{diff.RelativePath}'）。");
            SelfAssert.That(diff.ExpectedValue == "100", $"ExpectedValue が期待されたサイズ '100' と一致しません（実際: '{diff.ExpectedValue}'）。");
            SelfAssert.That(diff.ActualValue == "200", $"ActualValue が実際のサイズ '200' と一致しません（実際: '{diff.ActualValue}'）。");
        });

        runner.Add("BaselineComparer が欠落(Missing)を分類する（要件4.3）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"gone\file.txt", GoldenEntryKind.File, 10L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: Array.Empty<GoldenEntry>());

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"欠落があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 1, $"差分の件数が想定と異なります（実際: {report.Entries.Count}件）。");

            var diff = report.Entries[0];
            SelfAssert.That(diff.Kind == DiffKind.Missing, $"差分の種別が Missing ではありません（実際: {diff.Kind}）。");
            SelfAssert.That(diff.RelativePath == @"gone\file.txt", $"RelativePath が想定と異なります（実際: '{diff.RelativePath}'）。");
        });

        runner.Add("BaselineComparer が新規(Unexpected)を分類する（要件4.4）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: Array.Empty<GoldenEntry>());
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"new\file.txt", GoldenEntryKind.File, 10L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"新規があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 1, $"差分の件数が想定と異なります（実際: {report.Entries.Count}件）。");

            var diff = report.Entries[0];
            SelfAssert.That(diff.Kind == DiffKind.Unexpected, $"差分の種別が Unexpected ではありません（実際: {diff.Kind}）。");
            SelfAssert.That(diff.RelativePath == @"new\file.txt", $"RelativePath が想定と異なります（実際: '{diff.RelativePath}'）。");
        });

        runner.Add("BaselineComparer が種別不一致(KindMismatch)を分類し、双方の種別を報告する（要件4.5）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"c", GoldenEntryKind.Folder, 0L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"c", GoldenEntryKind.File, 0L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"種別不一致があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 1, $"差分の件数が想定と異なります（実際: {report.Entries.Count}件）。");

            var diff = report.Entries[0];
            SelfAssert.That(diff.Kind == DiffKind.KindMismatch, $"差分の種別が KindMismatch ではありません（実際: {diff.Kind}）。");
            SelfAssert.That(diff.RelativePath == @"c", $"RelativePath が想定と異なります（実際: '{diff.RelativePath}'）。");
            SelfAssert.That(diff.ExpectedValue == "Folder", $"ExpectedValue が期待された種別 'Folder' と一致しません（実際: '{diff.ExpectedValue}'）。");
            SelfAssert.That(diff.ActualValue == "File", $"ActualValue が実際の種別 'File' と一致しません（実際: '{diff.ActualValue}'）。");
        });

        runner.Add("BaselineComparer が差分のない入力を明示的に一致(Match)と判定する（要件4.6）", () =>
        {
            var sharedEntries = new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 42L),
            };
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: sharedEntries);
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 42L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Match, $"差分がないのに Verdict が Match ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 0, $"差分がないのに Entries が空ではありません（実際: {report.Entries.Count}件）。");
        });

        runner.Add("BaselineComparer が4種の差分を同時に含む入力を正しく列挙する（観測可能な完了状態）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"common\same.txt", GoldenEntryKind.File, 10L),
                new GoldenEntry(@"size\file.bin", GoldenEntryKind.File, 500L),
                new GoldenEntry(@"missing\gone.txt", GoldenEntryKind.File, 7L),
                new GoldenEntry(@"kind\thing", GoldenEntryKind.Folder, 0L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"common\same.txt", GoldenEntryKind.File, 10L),
                new GoldenEntry(@"size\file.bin", GoldenEntryKind.File, 600L),
                new GoldenEntry(@"new\fresh.txt", GoldenEntryKind.File, 3L),
                new GoldenEntry(@"kind\thing", GoldenEntryKind.File, 0L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"4種の差分があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 4, $"差分の件数が想定（4件）と異なります（実際: {report.Entries.Count}件）。");

            AssertContainsDiff(report, DiffKind.SizeMismatch, @"size\file.bin", "500", "600");
            AssertContainsDiff(report, DiffKind.Missing, @"missing\gone.txt", "", "");
            AssertContainsDiff(report, DiffKind.Unexpected, @"new\fresh.txt", "", "");
            AssertContainsDiff(report, DiffKind.KindMismatch, @"kind\thing", "Folder", "File");
        });

        runner.Add("BaselineComparer が物理サイズ換算の設定差を、突き合わせより先に設定不一致として報告する（要件6.2, 6.3）", () =>
        {
            // 走査条件が異なるうえ、エントリ内容も明らかに食い違わせておく。
            // 「突き合わせを行わない」ことを、Entries が空であることまで確認して証明する
            // （判定値だけを見ると内部で突き合わせてしまっていても気づけないため）。
            var expected = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 100L),
                new GoldenEntry(@"only-in-expected", GoldenEntryKind.Folder, 0L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 999L),
                new GoldenEntry(@"only-in-actual", GoldenEntryKind.Folder, 0L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.SettingsMismatch, $"物理サイズ換算の設定が異なるのに Verdict が SettingsMismatch ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 0, $"設定不一致のとき、エントリの突き合わせが行われず Entries は空であるべきですが {report.Entries.Count}件の差分が報告されました。");
        });

        runner.Add("BaselineComparer が期待値=換算なし・実測=換算ありの逆向きの設定差も、突き合わせより先に設定不一致として報告する（要件6.2, 6.3）", () =>
        {
            // 設定不一致の検出が「期待値=換算あり・実測=換算なし」の一方向だけに偏っていないことを確認する。
            // 上の検証と同様に、エントリ内容も食い違わせて Entries が空であることまで確認する。
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 100L),
                new GoldenEntry(@"only-in-expected", GoldenEntryKind.Folder, 0L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 4096L),
                new GoldenEntry(@"only-in-actual", GoldenEntryKind.Folder, 0L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.SettingsMismatch, $"期待値=換算なし・実測=換算ありなのに Verdict が SettingsMismatch ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 0, $"設定不一致のとき、エントリの突き合わせが行われず Entries は空であるべきですが {report.Entries.Count}件の差分が報告されました。");
        });

        runner.Add("BaselineComparer が双方とも物理サイズ換算ありで差分のない入力を一致(Match)と判定する（要件6.3, 4.6）", () =>
        {
            // 設定が一致している間は判定を行う（要件6.3）。換算なし同士だけでなく、
            // 換算あり同士でも設定不一致として打ち切られないことを確認する。
            var expected = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 4096L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 4096L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Match, $"双方とも換算ありで差分がないのに Verdict が Match ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 0, $"双方とも換算ありで差分がないのに Entries が空ではありません（実際: {report.Entries.Count}件）。");
        });

        runner.Add("BaselineComparer が双方とも物理サイズ換算ありのとき突き合わせを行い、サイズ不一致を期待値と実測値の向きどおりに報告する（要件6.3, 4.2）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 4096L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"a\file.bin", GoldenEntryKind.File, 8192L),
            });

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict == BaselineVerdict.Different, $"双方とも換算ありでサイズ不一致があるのに Verdict が Different ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 1, $"差分の件数が想定（1件）と異なります（実際: {report.Entries.Count}件）。");
            AssertContainsDiff(report, DiffKind.SizeMismatch, @"a\file.bin", "4096", "8192");
        });

        runner.Add("BaselineComparer の設定照合が物理サイズ換算の「有無」を見ており、クラスタサイズの違いだけでは設定不一致にしない（要件6.2, 6.3, 6.4）", () =>
        {
            // design.md は「物理サイズ換算の有無が異なる場合は突き合わせを行わず」と有無を明記し、
            // 要件6.1（有無の記録）と要件6.4（クラスタサイズの記録）は別の記録として定義されている。
            // 照合がクラスタサイズの比較に置き換わっていないことを、有無は同じでクラスタサイズだけが異なる入力で確認する。
            var sharedEntries = new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 8192L),
            };
            var expected = BuildComparerDocument(usePhysicalSize: true, entries: sharedEntries, clusterSizeInBytes: 4096L);
            var actual = BuildComparerDocument(usePhysicalSize: true, entries: new[]
            {
                new GoldenEntry(@"same", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"same\file.txt", GoldenEntryKind.File, 8192L),
            }, clusterSizeInBytes: 8192L);

            // 入力の組み立てが意図どおり「有無は同じ・クラスタサイズだけが異なる」ことを先に確かめる。
            SelfAssert.That(
                expected.Header.UsePhysicalSize == actual.Header.UsePhysicalSize
                    && expected.Header.ClusterSizeInBytes != actual.Header.ClusterSizeInBytes,
                "検証の前提（換算の有無は同じでクラスタサイズだけが異なる）が成り立っていません。");

            var report = new BaselineComparer().Compare(expected, actual);

            SelfAssert.That(report.Verdict != BaselineVerdict.SettingsMismatch, "換算の有無が同じでクラスタサイズだけが異なる入力が SettingsMismatch と判定されました。設定の照合が換算の有無ではなくクラスタサイズを見ている可能性があります。");
            SelfAssert.That(report.Verdict == BaselineVerdict.Match, $"換算の有無が同じで差分がないのに Verdict が Match ではありません（実際: {report.Verdict}）。");
            SelfAssert.That(report.Entries.Count == 0, $"換算の有無が同じで差分がないのに Entries が空ではありません（実際: {report.Entries.Count}件）。");
        });

        runner.Add("BaselineComparer の比較が対称であり、入力の順序に依存しない（Invariants）", () =>
        {
            var expected = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"only-expected\file.txt", GoldenEntryKind.File, 10L),
                new GoldenEntry(@"size\file.bin", GoldenEntryKind.File, 100L),
            });
            var actual = BuildComparerDocument(usePhysicalSize: false, entries: new[]
            {
                new GoldenEntry(@"only-actual\file.txt", GoldenEntryKind.File, 5L),
                new GoldenEntry(@"size\file.bin", GoldenEntryKind.File, 200L),
            });

            var comparer = new BaselineComparer();
            var forward = comparer.Compare(expected, actual);
            var swapped = comparer.Compare(actual, expected);

            // Missing と Unexpected が入れ替わることを確認する。
            AssertContainsDiff(forward, DiffKind.Missing, @"only-expected\file.txt", "", "");
            AssertContainsDiff(forward, DiffKind.Unexpected, @"only-actual\file.txt", "", "");
            AssertContainsDiff(swapped, DiffKind.Missing, @"only-actual\file.txt", "", "");
            AssertContainsDiff(swapped, DiffKind.Unexpected, @"only-expected\file.txt", "", "");

            // SizeMismatch の期待値・実測値も入れ替わることを確認する。
            AssertContainsDiff(forward, DiffKind.SizeMismatch, @"size\file.bin", "100", "200");
            AssertContainsDiff(swapped, DiffKind.SizeMismatch, @"size\file.bin", "200", "100");

            SelfAssert.That(forward.Verdict == swapped.Verdict, "入力を入れ替えると Verdict が変化しました。");
            SelfAssert.That(forward.Entries.Count == swapped.Entries.Count, "入力を入れ替えると差分の件数が変化しました。");
        });
    }

    /// <summary>
    /// BaselineComparer の検証で使う、代表的な GoldenHeader を持つ GoldenDocument を組み立てる。
    /// </summary>
    /// <param name="usePhysicalSize">物理サイズ換算の有無。</param>
    /// <param name="entries">エントリの一覧。</param>
    /// <param name="clusterSizeInBytes">
    /// クラスタサイズを明示する場合に指定する。省略時は換算の有無に連動させる（換算あり: 4096、換算なし: 0）。
    /// 換算の有無とクラスタサイズを独立に変えた入力を作るために使う。
    /// </param>
    /// <param name="baseFolderPathLength">
    /// 基準フォルダの実効絶対パス長。省略時は固定値（80）。長さだけが異なる入力を作るために使う（タスク7.1）。
    /// </param>
    private static GoldenDocument BuildComparerDocument(
        bool usePhysicalSize,
        IReadOnlyList<GoldenEntry> entries,
        long? clusterSizeInBytes = null,
        int baseFolderPathLength = 80)
    {
        var header = new GoldenHeader(
            formatVersion: 2,
            baseFolderLabel: "fixture-v1",
            baseFolderPathLength: baseFolderPathLength,
            generatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            usePhysicalSize: usePhysicalSize,
            clusterSizeInBytes: clusterSizeInBytes ?? (usePhysicalSize ? 4096L : 0L),
            fixtureComplete: true,
            fixtureOmissions: Array.Empty<string>());

        return new GoldenDocument(header, entries);
    }

    /// <summary>
    /// DiffReport が、指定した種別・相対パス・期待値・実測値を持つ差分をちょうど1件含むことを確認する。
    /// </summary>
    private static void AssertContainsDiff(DiffReport report, DiffKind kind, string relativePath, string expectedValue, string actualValue)
    {
        var matches = report.Entries
            .Where(e => e.Kind == kind && e.RelativePath == relativePath)
            .ToList();

        SelfAssert.That(matches.Count == 1, $"種別 {kind}・相対パス '{relativePath}' の差分がちょうど1件見つかりません（実際: {matches.Count}件）。");

        var diff = matches[0];
        SelfAssert.That(diff.ExpectedValue == expectedValue, $"種別 {kind}・相対パス '{relativePath}' の ExpectedValue が想定と異なります（期待: '{expectedValue}', 実際: '{diff.ExpectedValue}'）。");
        SelfAssert.That(diff.ActualValue == actualValue, $"種別 {kind}・相対パス '{relativePath}' の ActualValue が想定と異なります（期待: '{expectedValue}', 実際: '{diff.ActualValue}'）。");
    }

    /// <summary>
    /// Fixture 層（FixtureSpec / FixtureItem / FixtureTrait）の検証項目を登録する（タスク3.1）。
    /// design.md の State Management の型定義と Invariants、および
    /// requirements.md 3.1〜3.4・3.5・5.2 が求める境界条件の網羅を検証する。
    /// 「Trait のラベルと実体（実際の文字数・実際の文字種）を分けて検証する」ことを重視する
    /// （tasks.md Implementation Notes: ラベルだけを見る検証は定義を短くしても通過してしまうため）。
    /// </summary>
    private static void RegisterFixtureSpecChecks(SelfCheckRunner runner)
    {
        runner.Add("FixtureItem が相対パス・種別・内容サイズ・境界条件の一覧を保持する", () =>
        {
            var item = new FixtureItem(@"a\b.txt", GoldenEntryKind.File, 123L, new[] { FixtureTrait.Ordinary });

            SelfAssert.That(item.RelativePath == @"a\b.txt", "RelativePath が設定した値と一致しません。");
            SelfAssert.That(item.Kind == GoldenEntryKind.File, "Kind が設定した値と一致しません。");
            SelfAssert.That(item.ContentSizeInBytes == 123L, "ContentSizeInBytes が設定した値と一致しません。");
            SelfAssert.That(item.Traits.Count == 1 && item.Traits[0] == FixtureTrait.Ordinary, "Traits が設定した値と一致しません。");
        });

        runner.Add("FixtureItem が空の相対パスを拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureItem(string.Empty, GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "空の相対パスを渡しても例外が発生しませんでした。");
        });

        runner.Add("FixtureItem が負の内容サイズを拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureItem(@"a", GoldenEntryKind.File, -1L, new[] { FixtureTrait.Ordinary });
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "負の内容サイズを渡しても例外が発生しませんでした。");
        });

        runner.Add("FixtureItem がフォルダに対する非ゼロの内容サイズを拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureItem(@"a", GoldenEntryKind.Folder, 1L, new[] { FixtureTrait.Ordinary });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "フォルダに非ゼロの内容サイズを渡しても例外が発生しませんでした。");
        });

        runner.Add("FixtureItem が境界条件を1件も持たない定義を拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureItem(@"a", GoldenEntryKind.Folder, 0L, Array.Empty<FixtureTrait>());
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "境界条件が空のTraitsを渡しても例外が発生しませんでした。");
        });

        runner.Add("FixtureSpec が空の論理名を拒否する", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureSpec(string.Empty, new List<FixtureItem>
                {
                    new FixtureItem(@"a", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "空の論理名を渡しても例外が発生しませんでした。");
        });

        runner.Add("FixtureSpec が相対パスの重複を拒否する（Invariants）", () =>
        {
            bool threw = false;
            try
            {
                _ = new FixtureSpec("dup-spec", new List<FixtureItem>
                {
                    new FixtureItem(@"dup", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                    new FixtureItem(@"dup", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "相対パスが重複する項目を与えても例外が発生しませんでした。");
        });

        runner.Add("FixtureSpec が親フォルダを持たない項目を拒否する（Invariants: 親フォルダの包含）", () =>
        {
            bool threw = false;
            try
            {
                // "a\b\c.txt" の親である "a" と "a\b" を一切含めない不正な定義。
                _ = new FixtureSpec("no-parent-spec", new List<FixtureItem>
                {
                    new FixtureItem(@"a\b\c.txt", GoldenEntryKind.File, 1L, new[] { FixtureTrait.Ordinary }),
                });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "親フォルダを持たない項目を与えても例外が発生しませんでした。");
        });

        runner.Add("FixtureSpec が親フォルダとして定義されていない同名項目（フォルダでない）を拒否する（Invariants）", () =>
        {
            bool threw = false;
            try
            {
                // "a" が File として定義されており、"a\b.txt" の親としてフォルダの資格を持たない。
                _ = new FixtureSpec("parent-not-folder-spec", new List<FixtureItem>
                {
                    new FixtureItem(@"a", GoldenEntryKind.File, 1L, new[] { FixtureTrait.Ordinary }),
                    new FixtureItem(@"a\b.txt", GoldenEntryKind.File, 1L, new[] { FixtureTrait.Ordinary }),
                });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            SelfAssert.That(threw, "親がフォルダでない項目を与えても例外が発生しませんでした。");
        });

        runner.Add("FixtureSpec.CreateStandard を2回呼び出しても同一の項目列と内容が得られる（要件3.5）", () =>
        {
            var first = FixtureSpec.CreateStandard();
            var second = FixtureSpec.CreateStandard();

            SelfAssert.That(first.Name == second.Name, "2回の呼び出しで Name が一致しません。");
            SelfAssert.That(first.Items.Count == second.Items.Count, $"2回の呼び出しで項目数が一致しません（{first.Items.Count} 件 vs {second.Items.Count} 件）。");

            for (int i = 0; i < first.Items.Count; i++)
            {
                var a = first.Items[i];
                var b = second.Items[i];

                SelfAssert.That(a.RelativePath == b.RelativePath, $"インデックス{i}: RelativePath が一致しません（'{a.RelativePath}' vs '{b.RelativePath}'）。");
                SelfAssert.That(a.Kind == b.Kind, $"インデックス{i}: Kind が一致しません（相対パス '{a.RelativePath}'）。");
                SelfAssert.That(a.ContentSizeInBytes == b.ContentSizeInBytes, $"インデックス{i}: ContentSizeInBytes が一致しません（相対パス '{a.RelativePath}'）。");
                SelfAssert.That(a.Traits.SequenceEqual(b.Traits), $"インデックス{i}: Traits が一致しません（相対パス '{a.RelativePath}'）。");
            }
        });

        runner.Add("FixtureSpec.Standard の相対パスが重複しない（機械的検証）", () =>
        {
            var spec = FixtureSpec.Standard;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in spec.Items)
            {
                SelfAssert.That(seen.Add(item.RelativePath), $"相対パス '{item.RelativePath}' が重複しています。");
            }
        });

        runner.Add("FixtureSpec.Standard の全項目について、親フォルダのパスが定義内に存在する（機械的検証、design.md Invariants）", () =>
        {
            var spec = FixtureSpec.Standard;

            // FixtureSpec のコンストラクタとは独立に、この検証自身の力で祖先パスを再構築して照合する。
            var byPath = new Dictionary<string, GoldenEntryKind>(StringComparer.Ordinal);
            foreach (var item in spec.Items)
            {
                byPath[item.RelativePath] = item.Kind;
            }

            foreach (var item in spec.Items)
            {
                var segments = item.RelativePath.Split('\\');
                for (int depth = 1; depth < segments.Length; depth++)
                {
                    string ancestorPath = string.Join("\\", segments, 0, depth);

                    SelfAssert.That(byPath.ContainsKey(ancestorPath), $"項目 '{item.RelativePath}' の親フォルダ '{ancestorPath}' が定義に存在しません。");
                    SelfAssert.That(byPath[ancestorPath] == GoldenEntryKind.Folder, $"項目 '{item.RelativePath}' の親 '{ancestorPath}' がフォルダとして定義されていません。");
                }
            }
        });

        runner.Add("FixtureSpec.Standard が FixtureTrait の全種類を少なくとも1件含む（要件3.1〜3.4の網羅）", () =>
        {
            var spec = FixtureSpec.Standard;

            foreach (FixtureTrait trait in Enum.GetValues(typeof(FixtureTrait)))
            {
                bool exists = spec.Items.Any(item => item.Traits.Contains(trait));
                SelfAssert.That(exists, $"境界条件 '{trait}' を持つ項目が1件も存在しません。");
            }
        });

        runner.Add("FixtureSpec.Standard が相対パス248文字を超えるフォルダを実際の文字数として持つ（要件3.1。248はフィクスチャ設計上の長さであり、走査の境界は「親フォルダの絶対パスが258文字以上」の1つだけ）", () =>
        {
            var spec = FixtureSpec.Standard;

            // ラベル(Trait)だけでなく、RelativePath.Length という実体を数えて照合する。
            // 「LongPath の Trait が付いている」だけの検証では、長さが足りない定義でも通過してしまう。
            var longPathFolders = spec.Items
                .Where(item => item.Kind == GoldenEntryKind.Folder && item.Traits.Contains(FixtureTrait.LongPath))
                .ToList();

            SelfAssert.That(longPathFolders.Count >= 1, "LongPath トレイトを持つフォルダ項目が1件も存在しません。");

            foreach (var folder in longPathFolders)
            {
                SelfAssert.That(
                    folder.RelativePath.Length > 248,
                    $"LongPath トレイトを持つフォルダ '{folder.RelativePath}' の実際の文字数（{folder.RelativePath.Length}文字）が、フィクスチャ設計上の長さ248文字を超えていません。");
            }
        });

        runner.Add("FixtureSpec.Standard が相対パス260文字を超えるファイルを実際の文字数として持つ（要件3.1。260はフィクスチャ設計上の長さであり、走査の境界は「親フォルダの絶対パスが258文字以上」の1つだけ）", () =>
        {
            var spec = FixtureSpec.Standard;

            var longPathFiles = spec.Items
                .Where(item => item.Kind == GoldenEntryKind.File && item.Traits.Contains(FixtureTrait.LongPath))
                .ToList();

            SelfAssert.That(longPathFiles.Count >= 1, "LongPath トレイトを持つファイル項目が1件も存在しません。");

            foreach (var file in longPathFiles)
            {
                SelfAssert.That(
                    file.RelativePath.Length > 260,
                    $"LongPath トレイトを持つファイル '{file.RelativePath}' の実際の文字数（{file.RelativePath.Length}文字）が、フィクスチャ設計上の長さ260文字を超えていません。");
            }
        });

        runner.Add("FixtureSpec.Standard の Japanese トレイトを持つ項目が実際に日本語（ASCII以外の文字）を含む（要件3.2）", () =>
        {
            var spec = FixtureSpec.Standard;

            var japaneseItems = spec.Items.Where(item => item.Traits.Contains(FixtureTrait.Japanese)).ToList();
            SelfAssert.That(japaneseItems.Count >= 1, "Japanese トレイトを持つ項目が1件も存在しません。");

            foreach (var item in japaneseItems)
            {
                // ラベルだけでなく、実際に非ASCII文字（コードポイント127超）を含むことを照合する。
                bool containsNonAscii = item.RelativePath.Any(c => c > 127);
                SelfAssert.That(containsNonAscii, $"Japanese トレイトを持つ項目 '{item.RelativePath}' に非ASCII文字が含まれていません。");
            }
        });

        runner.Add("FixtureSpec.Standard の Empty/ZeroByte/AccessDenied トレイトが種別と整合する", () =>
        {
            var spec = FixtureSpec.Standard;

            foreach (var item in spec.Items.Where(i => i.Traits.Contains(FixtureTrait.Empty)))
            {
                SelfAssert.That(item.Kind == GoldenEntryKind.Folder, $"Empty トレイトを持つ項目 '{item.RelativePath}' がフォルダではありません。");
                SelfAssert.That(item.ContentSizeInBytes == 0L, $"Empty トレイトを持つ項目 '{item.RelativePath}' の内容サイズが0ではありません。");
            }

            foreach (var item in spec.Items.Where(i => i.Traits.Contains(FixtureTrait.ZeroByte)))
            {
                SelfAssert.That(item.Kind == GoldenEntryKind.File, $"ZeroByte トレイトを持つ項目 '{item.RelativePath}' がファイルではありません。");
                SelfAssert.That(item.ContentSizeInBytes == 0L, $"ZeroByte トレイトを持つ項目 '{item.RelativePath}' の内容サイズが0ではありません。");
            }

            foreach (var item in spec.Items.Where(i => i.Traits.Contains(FixtureTrait.AccessDenied)))
            {
                SelfAssert.That(item.Kind == GoldenEntryKind.Folder, $"AccessDenied トレイトを持つ項目 '{item.RelativePath}' がフォルダではありません。");
            }
        });

        runner.Add("FixtureSpec.Standard が対比用の通常のフォルダとファイルを含む（境界条件だけでなく普通の項目も検証できるように）", () =>
        {
            var spec = FixtureSpec.Standard;

            bool hasOrdinaryFolder = spec.Items.Any(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.Ordinary));
            bool hasOrdinaryFile = spec.Items.Any(i => i.Kind == GoldenEntryKind.File && i.Traits.Contains(FixtureTrait.Ordinary));

            SelfAssert.That(hasOrdinaryFolder, "Ordinary トレイトを持つフォルダ項目が存在しません。");
            SelfAssert.That(hasOrdinaryFile, "Ordinary トレイトを持つファイル項目が存在しません。");
        });
    }

    /// <summary>
    /// Fixture 層（AccessControlGate）の検証項目を登録する（タスク3.2）。
    /// design.md の Service Interface（DenyRead / RestoreRead）の Postconditions と Invariants、
    /// および research.md「権限のないフォルダの生成と後始末」で実測済みの2段階後始末が
    /// 実際に機能していることを検証する。
    /// すべての検証項目は finally で「解除 → 削除」の2段階の後始末を必ず行い、一時領域
    /// （%TEMP% 配下、"gb_acl_" 接頭辞）にのみフォルダを作成する。
    /// </summary>
    private static void RegisterAccessControlGateChecks(SelfCheckRunner runner)
    {
        runner.Add("DenyRead を付与すると実行ユーザーによる列挙が UnauthorizedAccessException で拒否される（要件3.4）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string target = Path.Combine(root, "denied");
            var gate = new AccessControlGate();

            try
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "marker.txt"), "x");

                gate.DenyRead(target);

                // 「例外が飛べば何でもよい」にしないため、拒否対象のフォルダ自体が実在することを先に確認する。
                // 存在しないパスでも例外は飛ぶため、これを確認しないと権限起因の例外と取り違える恐れがある。
                SelfAssert.That(Directory.Exists(target), "拒否設定を付与した対象フォルダが実在しません（存在しないパスとの混同を避けるための前提）。");

                bool deniedAsExpected = false;
                try
                {
                    Directory.GetFileSystemEntries(target);
                }
                catch (UnauthorizedAccessException)
                {
                    deniedAsExpected = true;
                }

                SelfAssert.That(deniedAsExpected, "DenyRead を付与したのに列挙が UnauthorizedAccessException になりませんでした。");
            }
            finally
            {
                // 後始末: 解除 → 削除の2段階（research.md）。例外の有無に関わらず必ず実行する。
                gate.RestoreRead(target);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });

        runner.Add("RestoreRead で解除すると再び列挙できるようになる（要件3.4 / design.md: DenyRead の対）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string target = Path.Combine(root, "denied");
            var gate = new AccessControlGate();

            try
            {
                Directory.CreateDirectory(target);
                string markerPath = Path.Combine(target, "marker.txt");
                File.WriteAllText(markerPath, "x");

                gate.DenyRead(target);
                gate.RestoreRead(target);

                string[] entries = Directory.GetFileSystemEntries(target);
                SelfAssert.That(
                    entries.Length == 1 && string.Equals(entries[0], markerPath, StringComparison.OrdinalIgnoreCase),
                    "解除後の列挙結果に、生成しておいたファイルが想定どおり含まれていません。");
            }
            finally
            {
                // 既に解除済みだが、検証失敗時の保険として再度解除を試みてから削除する。
                gate.RestoreRead(target);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });

        runner.Add("RestoreRead は DenyRead を適用していないフォルダに対しても安全に呼べる（design.md: AccessControlGate の Invariants）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var gate = new AccessControlGate();

            try
            {
                Directory.CreateDirectory(root);
                string markerPath = Path.Combine(root, "marker.txt");
                File.WriteAllText(markerPath, "x");

                // DenyRead を一度も呼んでいない状態で RestoreRead を呼ぶ。例外が飛ばないことが期待される。
                gate.RestoreRead(root);

                string[] entries = Directory.GetFileSystemEntries(root);
                SelfAssert.That(
                    entries.Length == 1 && string.Equals(entries[0], markerPath, StringComparison.OrdinalIgnoreCase),
                    "未付与フォルダへの RestoreRead 呼び出し後、列挙結果が想定と異なります（安全に無視できていない可能性があります）。");
            }
            finally
            {
                gate.RestoreRead(root);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });

        runner.Add("Deny ACE を付けたフォルダは解除なしでは削除に失敗し、解除後は確実に削除できる（research.md: 2段階の後始末）", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "gb_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var gate = new AccessControlGate();
            bool cleanedUpAlready = false;

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "child.txt"), "x");

                gate.DenyRead(root);

                // 1段階目（解除なしでの再帰削除）は、子の列挙ができず失敗するはず（research.md実測）。
                bool deleteWithoutRestoreFailed = false;
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (UnauthorizedAccessException)
                {
                    deleteWithoutRestoreFailed = true;
                }

                SelfAssert.That(
                    deleteWithoutRestoreFailed,
                    "拒否設定を解除せずに削除した際、想定どおり UnauthorizedAccessException になりませんでした（1段階では失敗するという前提を再現できていません）。");
                SelfAssert.That(Directory.Exists(root), "1段階目の削除試行後、フォルダが実際には削除されずに残っているはずですが存在しません。");

                // 2段階目: 解除してから削除する。これが確実に成功することを確認する。
                gate.RestoreRead(root);
                Directory.Delete(root, recursive: true);

                SelfAssert.That(!Directory.Exists(root), "解除後に削除を行ったのに、フォルダが残っています。");
                cleanedUpAlready = true;
            }
            finally
            {
                // 検証が失敗して2段階目まで到達しなかった場合の保険。解除してから削除する。
                if (!cleanedUpAlready && Directory.Exists(root))
                {
                    gate.RestoreRead(root);
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }
        });

        runner.Add("Deny ACE の付与・解除は管理者権限を必要としない（要件3.4）", () =>
        {
            // この自己検証全体が非昇格で実行されていることを前提とする。
            // 昇格状態では「非昇格でも成功する」ことを証明できないため、まずそれ自体を確認する。
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            SelfAssert.That(!isAdmin, "この自己検証は非昇格実行を前提としています。現在の実行が管理者権限のため、非昇格での成功を証明できません。");

            string root = Path.Combine(Path.GetTempPath(), "gb_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var gate = new AccessControlGate();

            try
            {
                Directory.CreateDirectory(root);

                // 非昇格のまま付与・解除が例外なく成功することを確認する。
                gate.DenyRead(root);
                gate.RestoreRead(root);

                string[] entries = Directory.GetFileSystemEntries(root);
                SelfAssert.That(entries.Length == 0, "解除後のフォルダの列挙結果が想定と異なります。");
            }
            finally
            {
                gate.RestoreRead(root);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    /// <summary>
    /// Fixture 層（FixtureBuilder）の検証項目を登録する（タスク3.3）。
    /// design.md の Service Interface（Build / TearDown）の Postconditions と Invariants、
    /// および要件3.1〜3.3・3.5・3.6 が求める「定義どおりの構造が生成され、後始末の実行後に
    /// 残留物がない」ことを検証する。
    /// すべての検証項目は finally で TearDown（および残留時の強制除去）を必ず呼び出し、
    /// 一時領域（%TEMP% 配下、"gb_fix_" 接頭辞）にのみフォルダを作成する。
    /// </summary>
    private static void RegisterFixtureBuilderChecks(SelfCheckRunner runner)
    {
        runner.Add("FixtureBuilder が FixtureSpec.Standard の全項目を拡張長パス経由で実在確認できる形で生成し、ファイルサイズが定義と一致する（Postconditions、要件3.1〜3.3）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var result = builder.Build(FixtureSpec.Standard, root);

                SelfAssert.That(
                    result.IsComplete,
                    $"生成が完了しませんでした。未生成: {string.Join(", ", result.Omissions.Select(o => $"{o.RelativePath} ({o.Reason})"))}");
                SelfAssert.That(result.Omissions.Count == 0, "未生成の項目が存在するのに Omissions が空ではありません。");

                int maxFolderLength = 0;
                int maxFileLength = 0;

                foreach (var item in FixtureSpec.Standard.Items)
                {
                    // 実在確認は必ず拡張長パス経由で行う。プレーンなパスでは260文字超の項目が
                    // 「存在しない」と誤判定されるため（タスクの検証観点）。
                    string extendedPath = LongPath.Extend(Path.Combine(root, item.RelativePath));

                    if (item.Kind == GoldenEntryKind.Folder)
                    {
                        SelfAssert.That(Directory.Exists(extendedPath), $"フォルダ '{item.RelativePath}' が生成されていません（拡張長パス経由で確認）。");
                        maxFolderLength = Math.Max(maxFolderLength, item.RelativePath.Length);
                    }
                    else
                    {
                        SelfAssert.That(File.Exists(extendedPath), $"ファイル '{item.RelativePath}' が生成されていません（拡張長パス経由で確認）。");

                        var info = new FileInfo(extendedPath);
                        SelfAssert.That(
                            info.Length == item.ContentSizeInBytes,
                            $"ファイル '{item.RelativePath}' のサイズが定義（{item.ContentSizeInBytes}バイト）と一致しません（実際: {info.Length}バイト）。");
                        maxFileLength = Math.Max(maxFileLength, item.RelativePath.Length);
                    }
                }

                // 248 / 260 はフィクスチャ設計上の長さであり、走査の境界ではない（走査の境界は「親フォルダの絶対パスが258文字以上」）。
                SelfAssert.That(maxFolderLength > 248, $"検証対象の最長フォルダの相対パスが248文字を超えていません（実際: {maxFolderLength}文字）。");
                SelfAssert.That(maxFileLength > 260, $"検証対象の最長ファイルの相対パスが260文字を超えていません（実際: {maxFileLength}文字）。");

                // 対比: プレーンなパスでも絶対パス260文字以上のフォルダの実在確認ができ、拡張長パス経由と同じ結論になることを確認する。
                // dotnet10-migration タスク2.4 で期待を改めた。旧: プレーンなパスでは実在確認自体ができない（.NET Framework の制約）。
                // 新: .NET 10 は長いパスをそのまま扱えるため、プレーンなパスでも「存在する」と判定される。
                var longFolderItem = FixtureSpec.Standard.Items
                    .First(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.LongPath) && i.RelativePath.Length > 248);
                string plainLongFolderPath = Path.Combine(root, longFolderItem.RelativePath);
                SelfAssert.That(
                    Directory.Exists(plainLongFolderPath),
                    "対比検証: プレーンなパスで長いパスのフォルダが「存在しない」と判定されました（.NET 10 では拡張長パス経由と同じく存在すると判定されるはずです）。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // 後始末自体が失敗しても、以下の強制除去へフォールバックする。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("FixtureBuilder.TearDown の後、基準フォルダを含めて残留物が一切ない（観測可能な完了状態）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var result = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(result.IsComplete, "前提となる生成が完了しませんでした。");

                builder.TearDown(FixtureSpec.Standard, root);
                cleanedUp = true;

                SelfAssert.That(!Directory.Exists(LongPath.Extend(root)), "TearDown の後も基準フォルダが（拡張長パス経由で見て）存在しています。");
                SelfAssert.That(!Directory.Exists(root), "TearDown の後も基準フォルダが（プレーンパスで見て）存在しています。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("FixtureBuilder が一部の生成に失敗しても中断せず残りの項目の生成を継続し、Omissions に項目と理由を記録する（要件3.6）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                Directory.CreateDirectory(root);

                // "normal" フォルダの生成先に、あらかじめ同名のファイルを置いておく。
                // Directory.CreateDirectory は同名のファイルが存在すると失敗するため、
                // FixtureBuilder の実装を一切改変せずに「一部が生成できない」状況を実際に再現できる。
                string blockedPath = Path.Combine(root, "normal");
                File.WriteAllText(blockedPath, "blocker");

                var result = builder.Build(FixtureSpec.Standard, root);

                SelfAssert.That(!result.IsComplete, "意図的に生成を妨げたのに IsComplete が真のままです。");
                SelfAssert.That(result.Omissions.Count > 0, "意図的に生成を妨げたのに Omissions が空です。");

                var blockedOmission = result.Omissions.FirstOrDefault(o => o.RelativePath == "normal");
                SelfAssert.That(blockedOmission != null, "妨げた項目 'normal' が Omissions に含まれていません。");
                // IsNullOrEmpty だけでは空白のみの「実質的に空の理由」を見逃す（変異テストで確認済み）。
                // 意味のある内容が入っていることまで、最低限の長さで機械的に照合する。
                SelfAssert.That(
                    !string.IsNullOrWhiteSpace(blockedOmission!.Reason) && blockedOmission.Reason.Trim().Length >= 5,
                    $"'normal' の Omission に意味のある理由が記録されていません（実際: '{blockedOmission!.Reason}'）。");

                // "normal" の子は親フォルダが（ファイルに阻まれて）存在しないため、これも生成できず
                // Omissions に含まれるはず。これにより「中断せず残りの項目の生成を継続した」ことを、
                // 妨げた項目の周辺でも確認する。
                var childOmission = result.Omissions.FirstOrDefault(o => o.RelativePath == @"normal\file_small.txt");
                SelfAssert.That(childOmission != null, "妨げたフォルダの子 'normal\\file_small.txt' が Omissions に含まれていません。");
                SelfAssert.That(
                    !string.IsNullOrWhiteSpace(childOmission!.Reason) && childOmission.Reason.Trim().Length >= 5,
                    $"子の Omission に意味のある理由が記録されていません（実際: '{childOmission!.Reason}'）。");

                // "normal" とは無関係な項目は、妨げの影響を受けず生成が継続されているはず
                // （中断せず残りの項目の生成を継続することの直接的な確認）。
                string unrelatedFolder = LongPath.Extend(Path.Combine(root, "empty_folder"));
                SelfAssert.That(Directory.Exists(unrelatedFolder), "妨げた項目と無関係な 'empty_folder' が生成されていません（生成が中断されている可能性があります）。");

                string unrelatedJapaneseFile = LongPath.Extend(Path.Combine(root, @"日本語フォルダ\日本語ファイル.txt"));
                SelfAssert.That(File.Exists(unrelatedJapaneseFile), "妨げた項目と無関係な日本語ファイルが生成されていません（生成が中断されている可能性があります）。");

                var unrelatedOmission = result.Omissions.FirstOrDefault(o => o.RelativePath == "empty_folder");
                SelfAssert.That(unrelatedOmission == null, "無関係な 'empty_folder' が誤って Omissions に含まれています。");

                // TearDown が Build の部分的な失敗の後でも呼び出せることを、この状態のまま確認する（Invariants）。
                builder.TearDown(FixtureSpec.Standard, root);
                cleanedUp = true;

                SelfAssert.That(!Directory.Exists(LongPath.Extend(root)), "部分的な失敗の後の TearDown で基準フォルダが削除されていません。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("FixtureBuilder が AccessDenied トレイトの項目に読み取り拒否を適用し、TearDown が2段階の後始末で確実に取り除く（要件3.4、research.md）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var result = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(
                    result.IsComplete,
                    $"前提となる生成が完了しませんでした。未生成: {string.Join(", ", result.Omissions.Select(o => o.RelativePath))}");

                string deniedFolder = Path.Combine(root, "access_denied_folder");
                SelfAssert.That(Directory.Exists(deniedFolder), "access_denied_folder が生成されていません。");

                bool deniedAsExpected = false;
                try
                {
                    Directory.GetFileSystemEntries(deniedFolder);
                }
                catch (UnauthorizedAccessException)
                {
                    deniedAsExpected = true;
                }

                SelfAssert.That(deniedAsExpected, "Build が AccessDenied トレイトの項目に読み取り拒否を適用していません。");

                // 1段階（解除なし）での削除は失敗するはず（research.md「権限のないフォルダの生成と後始末」の再現）。
                bool deleteWithoutRestoreFailed = false;
                try
                {
                    Directory.Delete(LongPath.Extend(root), recursive: true);
                }
                catch (UnauthorizedAccessException)
                {
                    deleteWithoutRestoreFailed = true;
                }

                SelfAssert.That(
                    deleteWithoutRestoreFailed,
                    "拒否設定を解除せずに削除した際、想定どおり UnauthorizedAccessException になりませんでした（1段階では削除が失敗するという前提を再現できていません）。");
                SelfAssert.That(Directory.Exists(LongPath.Extend(root)), "1段階目の削除試行後も基準フォルダが残っているはずですが、既に削除されています。");

                // 2段階目: TearDown（解除→削除）で確実に取り除けることを確認する。
                builder.TearDown(FixtureSpec.Standard, root);
                cleanedUp = true;

                SelfAssert.That(!Directory.Exists(LongPath.Extend(root)), "TearDown の後も基準フォルダが残っています。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });
    }

    /// <summary>
    /// FixtureOmission の例外の型名（ExceptionTypeName）の検証項目を登録する（タスク6.4）。
    /// 期待値への記録に用いる型名と、標準出力への報告に用いる詳細な理由（Reason）を分けて持つことを、
    /// FixtureBuilder.Build の3つの生成箇所（各項目の生成失敗・基準フォルダの生成失敗・読み取り拒否設定の付与失敗）で確認する。
    /// </summary>
    private static void RegisterFixtureOmissionTypeNameChecks(SelfCheckRunner runner)
    {
        runner.Add("FixtureBuilder が項目の生成に失敗したとき、FixtureOmission.ExceptionTypeName に実際の例外の型名（名前空間なし）を、Reason に例外メッセージを含む詳細な理由を記録する（design.md FixtureBuilder、要件3.6, 3.7、タスク6.4）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();

            try
            {
                var observed = BlockFixtureFolderAndObserveFailures(root);
                var result = builder.Build(FixtureSpec.Standard, root);

                // 型名は文字列リテラルで照合する（名前空間付きの FullName や、別の項目の型名の流用を見逃さないため）。
                // 同名のファイルがある位置へのフォルダ生成は IOException、親がファイルのため存在しないパスへの
                // ファイル生成は DirectoryNotFoundException になる（変更前の実測で確認）。
                var expectedTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [BlockedFixtureFolderRelativePath] = "IOException",
                    [BlockedFixtureFolderRelativePath + @"\file_small.txt"] = "DirectoryNotFoundException",
                };

                foreach (var kv in expectedTypeNames)
                {
                    var omission = result.Omissions.FirstOrDefault(o => o.RelativePath == kv.Key);
                    SelfAssert.That(omission != null, $"妨げた項目 '{kv.Key}' が Omissions に含まれていません。");
                    SelfAssert.That(
                        omission!.ExceptionTypeName == kv.Value,
                        $"'{kv.Key}' の ExceptionTypeName が '{kv.Value}' ではありません（実際: '{omission.ExceptionTypeName}'）。");
                }

                // 妨げの影響を受けるすべての項目について、独立に観測した例外と照合する。
                SelfAssert.That(
                    result.Omissions.Count == observed.Count,
                    $"Omissions の件数が妨げた項目の件数と一致しません（想定: {observed.Count} 件、実際: {result.Omissions.Count} 件）。");

                foreach (var o in observed)
                {
                    var omission = result.Omissions.FirstOrDefault(x => x.RelativePath == o.RelativePath);
                    SelfAssert.That(omission != null, $"妨げた項目 '{o.RelativePath}' が Omissions に含まれていません。");
                    SelfAssert.That(
                        omission!.ExceptionTypeName == o.TypeName,
                        $"'{o.RelativePath}' の ExceptionTypeName が観測した例外の型名 '{o.TypeName}' と一致しません（実際: '{omission.ExceptionTypeName}'）。");
                    SelfAssert.That(
                        omission.Reason.Contains(o.Message),
                        $"'{o.RelativePath}' の Reason に例外メッセージを含む詳細な理由が記録されていません（実際: '{omission.Reason}'）。");
                }
            }
            finally
            {
                try
                {
                    builder.TearDown(FixtureSpec.Standard, root);
                }
                catch
                {
                    // 後始末自体が失敗しても、以下の強制除去へフォールバックする。
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("FixtureBuilder が基準フォルダの生成に失敗したとき、全項目の FixtureOmission.ExceptionTypeName に実際の例外の型名を、Reason に例外メッセージを含む詳細な理由を記録する（タスク6.4）", () =>
        {
            string conflictingRoot = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();

            try
            {
                // 基準フォルダが作られるべき位置にファイルを置き、基準フォルダの生成を失敗させる。
                File.WriteAllText(conflictingRoot, "this is a file, not a directory");

                Exception? observed = CaptureConstructionException(() => Directory.CreateDirectory(LongPath.Extend(conflictingRoot)));
                SelfAssert.That(observed != null, "前提: ファイルと衝突する基準フォルダの生成が、独立した試行で成功してしまいました。");

                var result = builder.Build(FixtureSpec.Standard, conflictingRoot);

                SelfAssert.That(
                    result.Omissions.Count == FixtureSpec.Standard.Items.Count,
                    $"基準フォルダの生成に失敗したのに、全項目が Omissions に含まれていません（想定: {FixtureSpec.Standard.Items.Count} 件、実際: {result.Omissions.Count} 件）。");

                foreach (var omission in result.Omissions)
                {
                    // 型名はリテラルで固定せず、独立した試行で観測した例外の型名と突き合わせる。
                    // リテラルで固定すると、本番コードが同じ文字列を直接書いてしまう変異を検出できない（タスク6.4のレビュー指摘）。
                    SelfAssert.That(
                        omission.ExceptionTypeName == observed!.GetType().Name,
                        $"'{omission.RelativePath}' の ExceptionTypeName が、独立に観測した例外の型名 '{observed!.GetType().Name}' と一致しません（実際: '{omission.ExceptionTypeName}'）。");
                    SelfAssert.That(
                        omission.Reason.Contains(observed!.Message),
                        $"'{omission.RelativePath}' の Reason に例外メッセージを含む詳細な理由が記録されていません（実際: '{omission.Reason}'）。");
                }
            }
            finally
            {
                DeleteIfExists(conflictingRoot);
            }
        });

        runner.Add("FixtureBuilder が基準フォルダの生成に失敗したとき、IOException 以外の例外でもその型名を記録する（タスク6.4）", () =>
        {
            // ファイルとの衝突で失敗する経路だけでは、型名を実際の例外から採っているのか、
            // 'IOException' という文字列を直接書いているのかを区別できない（タスク6.4のレビューで実証）。
            // 存在しないドライブを基準フォルダにすると別の型名になるため、その区別が付く。
            const char NoFreeDriveLetter = char.MinValue;
            var usedDriveLetters = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
            char freeDriveLetter = NoFreeDriveLetter;
            for (char candidate = 'Z'; candidate >= 'D'; candidate--)
            {
                if (!usedDriveLetters.Contains(candidate))
                {
                    freeDriveLetter = candidate;
                    break;
                }
            }

            SelfAssert.That(freeDriveLetter != NoFreeDriveLetter, "未使用のドライブ文字が見つからないため、この検証を実行できません。");

            string missingDriveRoot = freeDriveLetter + ":" + Path.DirectorySeparatorChar + "gb_fix_missing_drive";

            Exception? observed = CaptureConstructionException(() => Directory.CreateDirectory(LongPath.Extend(missingDriveRoot)));
            SelfAssert.That(observed != null, $"前提: 存在しないドライブ '{freeDriveLetter}:' 上の基準フォルダの生成が、独立した試行で成功してしまいました。");
            SelfAssert.That(
                observed!.GetType().Name != "IOException",
                $"前提: 観測した例外の型名が 'IOException' では、リテラルで直接書いた実装と区別できません（実際: '{observed.GetType().Name}'）。");

            var result = new FixtureBuilder().Build(FixtureSpec.Standard, missingDriveRoot);

            SelfAssert.That(
                result.Omissions.Count == FixtureSpec.Standard.Items.Count,
                $"基準フォルダの生成に失敗したのに、全項目が Omissions に含まれていません（想定: {FixtureSpec.Standard.Items.Count} 件、実際: {result.Omissions.Count} 件）。");

            foreach (var omission in result.Omissions)
            {
                SelfAssert.That(
                    omission.ExceptionTypeName == observed.GetType().Name,
                    $"'{omission.RelativePath}' の ExceptionTypeName が、独立に観測した例外の型名 '{observed.GetType().Name}' と一致しません（実際: '{omission.ExceptionTypeName}'）。型名をリテラルで直接書いている可能性があります。");
            }
        });

        runner.Add("FixtureBuilder が読み取り拒否設定の付与に失敗したとき、その項目の FixtureOmission.ExceptionTypeName に例外の型名を、Reason に例外メッセージを含む詳細な理由を記録する（タスク6.4）", () =>
        {
            string root = CreateTempFixtureRoot();

            // 実際の ACL には触れない（付与は常に失敗し、解除は何もしない）ため、削除できない拒否設定は残らない。
            var builder = new FixtureBuilder(new FailingDenyAccessControlGate());

            try
            {
                var result = builder.Build(FixtureSpec.Standard, root);

                var deniedItems = FixtureSpec.Standard.Items
                    .Where(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.AccessDenied))
                    .ToList();
                SelfAssert.That(deniedItems.Count > 0, "FixtureSpec.Standard に AccessDenied トレイトのフォルダが見つかりません。");
                SelfAssert.That(
                    result.Omissions.Count == deniedItems.Count,
                    $"Omissions の件数が拒否設定の付与に失敗した項目の件数と一致しません（想定: {deniedItems.Count} 件、実際: {result.Omissions.Count} 件）。");

                foreach (var item in deniedItems)
                {
                    var omission = result.Omissions.FirstOrDefault(o => o.RelativePath == item.RelativePath);
                    SelfAssert.That(omission != null, $"拒否設定の付与に失敗した '{item.RelativePath}' が Omissions に含まれていません。");
                    SelfAssert.That(
                        omission!.ExceptionTypeName == "InvalidOperationException",
                        $"'{item.RelativePath}' の ExceptionTypeName が 'InvalidOperationException' ではありません（実際: '{omission.ExceptionTypeName}'）。");
                    SelfAssert.That(
                        omission.Reason.Contains(FailingDenyAccessControlGate.FailureMessage),
                        $"'{item.RelativePath}' の Reason に例外メッセージを含む詳細な理由が記録されていません（実際: '{omission.Reason}'）。");
                }
            }
            finally
            {
                try
                {
                    builder.TearDown(FixtureSpec.Standard, root);
                }
                catch
                {
                    // 後始末自体が失敗しても、以下の強制除去へフォールバックする。
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("FixtureOmission が空または null の例外の型名を ArgumentException で拒否し、指定した型名を保持する（タスク6.4）", () =>
        {
            foreach (var typeName in new string?[] { string.Empty, null })
            {
                Exception? caught = CaptureConstructionException(() => { _ = new FixtureOmission("normal", "生成に失敗しました", typeName!); });
                SelfAssert.That(
                    caught is ArgumentException,
                    $"例外の型名に{(typeName is null ? " null " : "空文字")}を与えても ArgumentException が発生しませんでした（実際: {DescribeCaughtException(caught)}）。");
            }

            var omission = new FixtureOmission("normal", "生成に失敗しました: 詳細", "IOException");
            SelfAssert.That(omission.RelativePath == "normal", $"RelativePath が指定した値と一致しません（実際: '{omission.RelativePath}'）。");
            SelfAssert.That(omission.Reason == "生成に失敗しました: 詳細", $"Reason が指定した値と一致しません（実際: '{omission.Reason}'）。");
            SelfAssert.That(omission.ExceptionTypeName == "IOException", $"ExceptionTypeName が指定した値と一致しません（実際: '{omission.ExceptionTypeName}'）。");
        });
    }

    /// <summary>
    /// 読み取り拒否設定の付与を常に失敗させる、検証用のアクセス制御ゲート。解除は何もしない。
    /// </summary>
    private sealed class FailingDenyAccessControlGate : IAccessControlGate
    {
        /// <summary>付与の失敗として送出する例外のメッセージ。</summary>
        public const string FailureMessage = "検証のために読み取り拒否設定の付与を失敗させました。";

        public void DenyRead(string directoryPath)
        {
            throw new InvalidOperationException(FailureMessage);
        }

        public void RestoreRead(string directoryPath)
        {
            // 付与していないため、解除することはない。
        }
    }

    /// <summary>
    /// Scan 層（ScanRunner）の検証項目を登録する（タスク4.1）。
    /// 被テストアプリの Scanner を実際に呼び出す、初めての検証項目である。
    /// FixtureBuilder で生成した FixtureSpec.Standard を走査対象として用いる。
    /// </summary>
    private static void RegisterScanRunnerChecks(SelfCheckRunner runner)
    {
        runner.Add("ScanRunner が同一のディスク状態に対して2回走査しても同一の集計結果を返す（要件2.3）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(
                    buildResult.IsComplete,
                    $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

                var scanRunner = new ScanRunner();

                var outcome1 = scanRunner.Run(root, usePhysicalSize: true);
                var outcome2 = scanRunner.Run(root, usePhysicalSize: true);

                var map1 = FlattenScanTree(outcome1.Root);
                var map2 = FlattenScanTree(outcome2.Root);

                SelfAssert.That(
                    outcome1.Root.Size == outcome2.Root.Size,
                    $"ルートの集計サイズが2回の走査で異なります（1回目: {outcome1.Root.Size}, 2回目: {outcome2.Root.Size}）。");
                SelfAssert.That(
                    map1.Count == map2.Count,
                    $"走査結果のエントリ数が2回の走査で異なります（1回目: {map1.Count}, 2回目: {map2.Count}）。");

                foreach (var kv in map1)
                {
                    SelfAssert.That(map2.TryGetValue(kv.Key, out var other), $"1回目に存在した '{kv.Key}' が2回目に存在しません。");
                    SelfAssert.That(other.IsFile == kv.Value.IsFile, $"'{kv.Key}' の種別が2回の走査で異なります。");
                    SelfAssert.That(
                        other.Size == kv.Value.Size,
                        $"'{kv.Key}' のサイズが2回の走査で異なります（1回目: {kv.Value.Size}, 2回目: {other.Size}）。");
                }

                SelfAssert.That(outcome1.ClusterSizeInBytes == outcome2.ClusterSizeInBytes, "クラスタサイズが2回の走査で異なります。");
                SelfAssert.That(outcome1.SkippedPaths.Count == outcome2.SkippedPaths.Count, "スキップされた対象の件数が2回の走査で異なります。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("ScanRunner が表示条件を適用せず、サイズ0のファイルを含むすべてのファイルが走査結果に現れる（要件2.2）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);
                var map = FlattenScanTree(outcome.Root);

                SelfAssert.That(
                    map.TryGetValue(@"normal\file_zero_byte.dat", out var zeroByte),
                    "サイズ0のファイル 'normal\\file_zero_byte.dat' が走査結果に現れていません（抽出サイズ閾値が適用されている可能性があります）。");
                SelfAssert.That(zeroByte.IsFile, "'normal\\file_zero_byte.dat' がファイルとして記録されていません。");
                SelfAssert.That(zeroByte.Size == 0L, $"'normal\\file_zero_byte.dat' のサイズが0ではありません（実際: {zeroByte.Size}）。");

                SelfAssert.That(map.TryGetValue(@"normal\file_small.txt", out var small), "'normal\\file_small.txt' が走査結果に現れていません。");
                SelfAssert.That(small.Size == 10L, $"'normal\\file_small.txt' のサイズが定義（10バイト）と一致しません（実際: {small.Size}）。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 長いパスの項目は既知の不具合により走査結果に現れない（.NET Framework では列挙できない）。
        // 新: .NET 10 は長いパスを列挙できるため、長いパスの項目も走査結果に現れる。
        runner.Add("ScanRunner が走査結果に手を加えず、長いパスの項目も走査結果に現れる（要件5.1, 5.5）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);
                var map = FlattenScanTree(outcome.Root);

                // 長いパスの項目（LongPath トレイト かつ 実際の相対パスが設計上の長さを超える）を、FixtureSpec の定義から
                // 機械的に抽出する。手作業の列挙はしない（tasks.md「手作業の注釈に頼らない」の趣旨に合わせる）。
                // 248 / 260 はフィクスチャ設計上の長さであり、走査の境界ではない。
                var overBoundaryItems = FixtureSpec.Standard.Items
                    .Where(i => i.Traits.Contains(FixtureTrait.LongPath))
                    .Where(i => (i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248)
                             || (i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260))
                    .ToList();

                SelfAssert.That(overBoundaryItems.Count > 0, "検証対象となる境界超過項目が定義から見つかりません（FixtureSpec.Standard の想定が変わった可能性があります）。");

                foreach (var item in overBoundaryItems)
                {
                    SelfAssert.That(
                        map.ContainsKey(item.RelativePath),
                        $"長いパスの項目 '{item.RelativePath}'（{item.RelativePath.Length}文字）が走査結果に現れていません。" +
                        ".NET 10 では列挙できるはずであり、本体の挙動が想定と異なるため報告が必要です。");
                }

                // 対比: 境界を超えない通常の項目（同じ長いパス連鎖の浅い階層）も正しく現れることを確認する。
                string shallowAsciiFolder = new string('a', 50);
                SelfAssert.That(map.ContainsKey(shallowAsciiFolder), $"境界を超えない通常のフォルダ '{shallowAsciiFolder}' が走査結果に現れていません。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("ScanRunner が物理サイズ換算の有無を指定でき、結果とクラスタサイズに反映される（要件6.1, 6.4）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();

                var logical = scanRunner.Run(root, usePhysicalSize: false);
                var physical = scanRunner.Run(root, usePhysicalSize: true);

                SelfAssert.That(logical.ClusterSizeInBytes == 0L, $"物理サイズ換算なしのときクラスタサイズが0ではありません（実際: {logical.ClusterSizeInBytes}）。");
                SelfAssert.That(physical.ClusterSizeInBytes > 0L, "物理サイズ換算ありのときクラスタサイズが0のままです。");

                var logicalMap = FlattenScanTree(logical.Root);
                var physicalMap = FlattenScanTree(physical.Root);

                long clusterSize = physical.ClusterSizeInBytes;

                SelfAssert.That(logicalMap.TryGetValue(@"normal\file_small.txt", out var logicalSmall), "論理サイズ走査結果に 'normal\\file_small.txt' が見つかりません。");
                SelfAssert.That(physicalMap.TryGetValue(@"normal\file_small.txt", out var physicalSmall), "物理サイズ走査結果に 'normal\\file_small.txt' が見つかりません。");

                SelfAssert.That(logicalSmall.Size == 10L, $"論理サイズが定義（10バイト）と一致しません（実際: {logicalSmall.Size}）。");

                long expectedPhysicalSmall = ((10L + clusterSize - 1) / clusterSize) * clusterSize;
                SelfAssert.That(
                    physicalSmall.Size == expectedPhysicalSmall,
                    $"物理サイズ換算の結果が想定（クラスタサイズ {clusterSize} バイトへの切り上げ = {expectedPhysicalSmall} バイト）と一致しません（実際: {physicalSmall.Size}）。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("ScanRunner がアクセスできずスキップされた対象を結果から確認できる形で記録する（要件2.4）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);

                string deniedFolder = Path.Combine(root, "access_denied_folder");
                SelfAssert.That(
                    outcome.SkippedPaths.Any(p => string.Equals(p, deniedFolder, StringComparison.OrdinalIgnoreCase)),
                    $"読み取り拒否フォルダ '{deniedFolder}' が SkippedPaths に記録されていません。実際の SkippedPaths: {string.Join(", ", outcome.SkippedPaths)}");

                // 走査自体は完了しており、拒否フォルダ自身は（中身が空の状態で）ツリーに現れているはず。
                var map = FlattenScanTree(outcome.Root);
                SelfAssert.That(
                    map.TryGetValue("access_denied_folder", out var deniedNode),
                    "access_denied_folder 自体が走査結果のツリーに現れていません（走査が完了していない可能性があります）。");
                SelfAssert.That(!deniedNode.IsFile, "access_denied_folder がファイルとして記録されています。");

                // 拒否対象と無関係な項目が正しく走査されていることを確認し、走査が中断していないことを示す。
                SelfAssert.That(map.ContainsKey("empty_folder"), "拒否対象と無関係な 'empty_folder' が走査結果に現れていません（走査が中断している可能性があります）。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });
    }

    /// <summary>
    /// GoldenProjector（タスク4.2）の検証項目を登録する。
    /// </summary>
    private static void RegisterGoldenProjectorChecks(SelfCheckRunner runner)
    {
        runner.Add("GoldenProjector が実フィクスチャの走査結果から相対パス・種別・バイトサイズを正しく射影する（要件1.1, 1.2）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(
                    buildResult.IsComplete,
                    $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);

                var projector = new GoldenProjector();
                var header = new GoldenHeader(
                    formatVersion: 2,
                    baseFolderLabel: "fixture-v1",
                    baseFolderPathLength: 80,
                    generatedAt: DateTimeOffset.UtcNow,
                    usePhysicalSize: false,
                    clusterSizeInBytes: 0L,
                    fixtureComplete: true,
                    fixtureOmissions: Array.Empty<string>());

                var document = projector.Project(outcome, header);
                var byPath = document.Entries.ToDictionary(e => e.RelativePath, e => e, StringComparer.Ordinal);

                // 相対パス・種別・サイズが正しく射影されていること（バイト値そのまま、整形経路を通さない）。
                SelfAssert.That(byPath.TryGetValue(@"normal\file_small.txt", out var small), "'normal\\file_small.txt' が射影結果に見つかりません。");
                SelfAssert.That(small!.Kind == GoldenEntryKind.File, "'normal\\file_small.txt' が File として射影されていません。");
                SelfAssert.That(small.SizeInBytes == 10L, $"'normal\\file_small.txt' のサイズがバイト値そのまま（定義: 10）と一致しません（実際: {small.SizeInBytes}）。");

                SelfAssert.That(byPath.TryGetValue(@"normal\file_one_cluster.bin", out var oneCluster), "'normal\\file_one_cluster.bin' が射影結果に見つかりません。");
                SelfAssert.That(oneCluster!.SizeInBytes == 4096L, $"'normal\\file_one_cluster.bin' のサイズが定義（4096）と一致しません（実際: {oneCluster.SizeInBytes}）。");

                SelfAssert.That(byPath.TryGetValue(@"normal\file_two_clusters.bin", out var twoClusters), "'normal\\file_two_clusters.bin' が射影結果に見つかりません。");
                SelfAssert.That(twoClusters!.SizeInBytes == 4097L, $"'normal\\file_two_clusters.bin' のサイズが定義（4097）と一致しません（実際: {twoClusters.SizeInBytes}）。");

                SelfAssert.That(byPath.TryGetValue(@"normal", out var normalFolder), "'normal' フォルダが射影結果に見つかりません。");
                SelfAssert.That(normalFolder!.Kind == GoldenEntryKind.Folder, "'normal' が Folder として射影されていません。");

                SelfAssert.That(byPath.TryGetValue(@"empty_folder", out var emptyFolder), "'empty_folder' が射影結果に見つかりません。");
                SelfAssert.That(emptyFolder!.SizeInBytes == 0L, "'empty_folder' のサイズが0ではありません。");

                // ルートノード自身は絶対パスを Name に持つため（Scanner.cs: new FolderInfo(dir.FullName, ...)）、
                // エントリとして射影結果に現れてはならない。
                SelfAssert.That(!byPath.ContainsKey(root), "ルートノード自身（絶対パス）がエントリとして射影結果に含まれています。");

                foreach (var path in byPath.Keys)
                {
                    SelfAssert.That(!System.IO.Path.IsPathRooted(path), $"相対パス '{path}' が絶対パスとして現れています。");
                    SelfAssert.That(!path.StartsWith(@"\\?\", StringComparison.Ordinal), $"相対パス '{path}' に拡張長プレフィクスが混入しています。");
                    SelfAssert.That(!path.Contains('/'), $"相対パス '{path}' の区切りが '\\' に統一されていません。");
                }

                // 重複した相対パスが生じない（GoldenDocument のコンストラクタでも拒否されるが、ここでも独立に確認する）。
                SelfAssert.That(
                    document.Entries.Count == document.Entries.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count(),
                    "射影結果に重複した相対パスが存在します。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 走査結果に現れない長いパスの項目は、射影結果にも現れない（補完しない）。
        // 新: .NET 10 では走査結果に現れる長いパスの項目が、射影結果にもそのまま現れる（落とさない）。
        runner.Add("GoldenProjector が走査結果に手を加えず、長いパスの項目は射影結果にもそのまま現れる（要件5.1）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);

                var projector = new GoldenProjector();
                var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = projector.Project(outcome, header);
                var paths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

                // 長いパスの項目（LongPath トレイト かつ 実際の相対パスが設計上の長さを超える）を、FixtureSpec の定義から機械的に抽出する。
                // 248 / 260 はフィクスチャ設計上の長さであり、走査の境界ではない。
                var overBoundaryItems = FixtureSpec.Standard.Items
                    .Where(i => i.Traits.Contains(FixtureTrait.LongPath))
                    .Where(i => (i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248)
                             || (i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260))
                    .ToList();

                SelfAssert.That(overBoundaryItems.Count > 0, "検証対象となる境界超過項目が定義から見つかりません（FixtureSpec.Standard の想定が変わった可能性があります）。");

                var scanMap = FlattenScanTree(outcome.Root);

                foreach (var item in overBoundaryItems)
                {
                    // 射影の前提として、走査結果に現れていることを先に確かめる（射影が補完したのではないことの切り分け）。
                    SelfAssert.That(
                        scanMap.ContainsKey(item.RelativePath),
                        $"長いパスの項目 '{item.RelativePath}'（{item.RelativePath.Length}文字）が走査結果に現れていません（射影の検証の前提が崩れています）。");
                    SelfAssert.That(
                        paths.Contains(item.RelativePath),
                        $"走査結果に現れた長いパスの項目 '{item.RelativePath}'（{item.RelativePath.Length}文字）が射影結果に現れません。" +
                        "GoldenProjector が走査結果に手を加えている（項目を落としている）可能性があります。");
                }

                // 対比: 境界を超えない通常の項目（同じ長いパス連鎖の浅い階層）も射影結果に正しく現れることを確認する。
                string shallowAsciiFolder = new string('a', 50);
                SelfAssert.That(paths.Contains(shallowAsciiFolder), $"境界を超えない通常のフォルダ '{shallowAsciiFolder}' が射影結果に現れていません。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("GoldenEntry が更新日時・所有者を保持する手段を持たず、GoldenProjector もそれらを参照しない（要件1.5, 5.1）", () =>
        {
            var entryType = typeof(GoldenEntry);
            var memberNames = entryType.GetProperties()
                .Select(p => p.Name)
                .Concat(entryType.GetFields().Select(f => f.Name))
                .ToList();

            SelfAssert.That(
                !memberNames.Any(n => n.IndexOf("Modified", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Owner", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0
                                    || n.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0),
                $"GoldenEntry に更新日時・所有者らしきメンバーが含まれています: {string.Join(", ", memberNames)}");

            var expected = new[] { "RelativePath", "Kind", "SizeInBytes" };
            SelfAssert.That(
                memberNames.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal)),
                $"GoldenEntry の公開メンバーが想定（{string.Join(", ", expected)}）と一致しません（実際: {string.Join(", ", memberNames)}）。");
        });
    }

    /// <summary>
    /// KnownIssueAnalyzer（タスク4.3）の検証項目を登録する。
    /// FixtureSpec が持つ真値と観測結果（GoldenDocument）の差から、既知の不具合に由来する欠落を
    /// 手作業の注釈なしに識別できることを確認する（要件5.2）。
    /// </summary>
    private static void RegisterKnownIssueAnalyzerChecks(SelfCheckRunner runner)
    {
        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 実フィクスチャの走査で長いパスの項目が欠落し、KnownIssueAnalyzer がそれらを根拠 LongPath の既知の欠落として列挙する。
        // 新: .NET 10 では長いパスの項目も観測されるため欠落はなく、KnownIssueAnalyzer は何も列挙しない。
        //     LongPath の欠落を列挙する規則そのものは、人工的な定義を使う検証項目（手作業の注釈に頼らない…）が引き続き守る。
        runner.Add("KnownIssueAnalyzer が実フィクスチャの走査結果に対し、長いパスの項目も観測されるため既知の欠落を列挙しない（要件5.2）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(
                    buildResult.IsComplete,
                    $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);

                var projector = new GoldenProjector();
                var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = projector.Project(outcome, header);

                var observedPaths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

                // 走査で実際に観測されなかった項目を、FixtureSpec の定義から機械的に抽出する（手作業の列挙はしない）。
                var actuallyMissingItems = FixtureSpec.Standard.Items
                    .Where(i => !observedPaths.Contains(i.RelativePath))
                    .ToList();

                SelfAssert.That(
                    actuallyMissingItems.Count == 0,
                    $"走査で観測されなかった項目があります（.NET 10 では長いパスを含めてすべて観測されるはずです）: {string.Join(", ", actuallyMissingItems.Select(i => i.RelativePath))}");

                // 観測された項目に、フィクスチャ設計上の長さを超える LongPath の項目（フォルダ・ファイルの両方）が実際に含まれること
                // （相対パスの実測値で確認し、タスク3.1の教訓に合わせトレイトのラベルだけでなく実体も見る）。
                var observedLongPathItems = FixtureSpec.Standard.Items
                    .Where(i => i.Traits.Contains(FixtureTrait.LongPath) && observedPaths.Contains(i.RelativePath))
                    .ToList();
                SelfAssert.That(
                    observedLongPathItems.Any(i => i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248),
                    "相対パス248文字（フィクスチャ設計上の長さ）を超えるフォルダが観測されていません。");
                SelfAssert.That(
                    observedLongPathItems.Any(i => i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260),
                    "相対パス260文字（フィクスチャ設計上の長さ）を超えるファイルが観測されていません。");

                var analyzer = new KnownIssueAnalyzer();
                var findings = analyzer.Analyze(FixtureSpec.Standard, document);

                // 完了状態: 欠落がないので、既知の欠落として列挙されるものもない。
                SelfAssert.That(
                    findings.Count == 0,
                    $"欠落がないにもかかわらず既知の欠落が列挙されました（{findings.Count}件）: {string.Join(", ", findings.Select(f => f.RelativePath))}");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("KnownIssueAnalyzer が観測されている項目を既知の欠落に含めない（誤検出なし）", () =>
        {
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(buildResult.IsComplete, "前提となるフィクスチャ生成が完了しませんでした。");

                var scanRunner = new ScanRunner();
                var outcome = scanRunner.Run(root, usePhysicalSize: false);

                var projector = new GoldenProjector();
                var header = new GoldenHeader(2, "fixture-v1", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = projector.Project(outcome, header);

                var analyzer = new KnownIssueAnalyzer();
                var findings = analyzer.Analyze(FixtureSpec.Standard, document);
                var findingPaths = new HashSet<string>(findings.Select(f => f.RelativePath), StringComparer.Ordinal);

                foreach (var observedEntry in document.Entries)
                {
                    SelfAssert.That(
                        !findingPaths.Contains(observedEntry.RelativePath),
                        $"観測されている項目 '{observedEntry.RelativePath}' が既知の欠落として誤って列挙されました。");
                }
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("KnownIssueAnalyzer が手作業の注釈に頼らず、人工的な FixtureSpec の定義変更に自動的に追随する", () =>
        {
            // FixtureSpec.Standard に依存しない、この検証項目専用の人工的な定義を組み立てる。
            // 各項目は境界条件（Trait）の組み合わせだけが異なり、KnownIssueAnalyzer 側には
            // これらの相対パスを個別に知る手段（ハードコードされた注釈）が一切存在しないことを示す。
            var syntheticItems = new List<FixtureItem>
            {
                new FixtureItem("longpath_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.LongPath }),
                new FixtureItem("ordinary_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                new FixtureItem("access_denied_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.AccessDenied }),
                new FixtureItem("japanese_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Japanese }),
                new FixtureItem("longpath_and_japanese", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.LongPath, FixtureTrait.Japanese }),
                new FixtureItem("present_ordinary", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
            };
            var syntheticSpec = new FixtureSpec("synthetic-known-issue-test", syntheticItems);

            // 観測結果には "present_ordinary" のみを含める。他の5項目はすべて「欠落」として扱われる。
            var header = new GoldenHeader(2, "synthetic-known-issue-test", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var observedEntries = new List<GoldenEntry>
            {
                new GoldenEntry("present_ordinary", GoldenEntryKind.Folder, 0L),
            };
            var syntheticObserved = new GoldenDocument(header, observedEntries);

            var analyzer = new KnownIssueAnalyzer();
            var findings = analyzer.Analyze(syntheticSpec, syntheticObserved);
            var findingsByPath = findings.ToDictionary(f => f.RelativePath, f => f.Trait, StringComparer.Ordinal);

            // LongPath トレイトを持つ欠落項目（単独・複合トレイトの両方）は既知の欠落として列挙される。
            SelfAssert.That(findingsByPath.TryGetValue("longpath_only", out var t1), "'longpath_only' が既知の欠落として列挙されていません。");
            SelfAssert.That(t1 == FixtureTrait.LongPath, $"'longpath_only' の根拠が LongPath ではありません（実際: {t1}）。");

            SelfAssert.That(findingsByPath.TryGetValue("longpath_and_japanese", out var t2), "'longpath_and_japanese' が既知の欠落として列挙されていません。");
            SelfAssert.That(t2 == FixtureTrait.LongPath, $"'longpath_and_japanese' の根拠が LongPath ではありません（実際: {t2}）。");

            // Ordinary のみ・AccessDenied のみ・Japanese のみの欠落は、既知の不具合として説明できないため列挙されない
            // （tasks.md: 既知の不具合では説明できない欠落はむしろ重要な発見であり、この部品の責務外）。
            SelfAssert.That(!findingsByPath.ContainsKey("ordinary_only"), "'ordinary_only'（既知の不具合で説明できない欠落）が誤って既知の欠落として列挙されました。");
            SelfAssert.That(!findingsByPath.ContainsKey("access_denied_only"), "'access_denied_only' が誤って既知の欠落として列挙されました（アクセス拒否は既知の不具合ではなく意図されたスキップ挙動である）。");
            SelfAssert.That(!findingsByPath.ContainsKey("japanese_only"), "'japanese_only'（日本語であることのみを理由とする欠落）が誤って既知の欠落として列挙されました。");

            // 観測済みの項目は列挙されない。
            SelfAssert.That(!findingsByPath.ContainsKey("present_ordinary"), "観測されている 'present_ordinary' が既知の欠落として列挙されました。");

            // 列挙された既知の欠落は longpath_only と longpath_and_japanese の2件のみ。
            SelfAssert.That(findings.Count == 2, $"既知の欠落として列挙された件数が想定（2件）と一致しません（実際: {findings.Count}件）。");

            // --- フィクスチャ定義の変更への追随を確認する ---
            // 上と同じ観測結果に対し、"ordinary_only" に LongPath トレイトを追加しただけの定義を新たに組み立てる。
            // KnownIssueAnalyzer のコード自体は一切変更していないのに、定義の変更だけで結果が追随することを示す。
            var mutatedItems = new List<FixtureItem>
            {
                new FixtureItem("longpath_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.LongPath }),
                new FixtureItem("ordinary_only", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary, FixtureTrait.LongPath }),
                new FixtureItem("present_ordinary", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
            };
            var mutatedSpec = new FixtureSpec("synthetic-known-issue-test-mutated", mutatedItems);
            var mutatedFindings = analyzer.Analyze(mutatedSpec, syntheticObserved);
            var mutatedFindingsByPath = mutatedFindings.ToDictionary(f => f.RelativePath, f => f.Trait, StringComparer.Ordinal);

            SelfAssert.That(
                mutatedFindingsByPath.ContainsKey("ordinary_only") && mutatedFindingsByPath["ordinary_only"] == FixtureTrait.LongPath,
                "フィクスチャ定義に LongPath トレイトを追加したにもかかわらず、'ordinary_only' が既知の欠落として追随して列挙されませんでした。");
        });

        runner.Add("KnownIssueAnalyzer は走査結果の正しさを判定しない。全項目が観測されていれば識別結果は空になる（要件5.5）", () =>
        {
            var items = new List<FixtureItem>
            {
                new FixtureItem("all_present", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.LongPath }),
            };
            var spec = new FixtureSpec("synthetic-all-present", items);

            var header = new GoldenHeader(2, "synthetic-all-present", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var entries = new List<GoldenEntry>
            {
                // LongPath トレイトを持つ項目であっても、実際に観測されていれば「不具合の判定」ではなく
                // 単なる「欠落の説明」に徹する本部品は、何の主張も行わない（=空の結果）。
                new GoldenEntry("all_present", GoldenEntryKind.Folder, 0L),
            };
            var observed = new GoldenDocument(header, entries);

            var analyzer = new KnownIssueAnalyzer();
            var findings = analyzer.Analyze(spec, observed);

            SelfAssert.That(findings.Count == 0, $"すべての項目が観測されているにもかかわらず既知の欠落が列挙されました（{findings.Count}件）。KnownIssueAnalyzer が欠落の説明を超えて正しさの判定を行っている可能性があります。");
        });
    }

    /// <summary>
    /// LongPath トレイトを持つフォルダが備えるべき、フィクスチャ設計上の相対パスの長さ（文字数で数える）。
    /// 走査で項目が欠落する境界そのものではない。実測（2026-09-16）の境界は「親フォルダの絶対パスが
    /// 258文字以上だと、その直下を一覧できない」の1つだけで、フォルダとファイルに差はない。
    /// </summary>
    private const int LongPathFolderDesignLength = 248;

    /// <summary>
    /// LongPath トレイトを持つファイルが備えるべき、フィクスチャ設計上の相対パスの長さ（文字数で数える）。
    /// フォルダとの値の違いは、この定義がフィクスチャ設計の名残であることによるもので、走査の境界の違いではない。
    /// </summary>
    private const int LongPathFileDesignLength = 260;

    /// <summary>
    /// 項目の相対パスが、種別に応じたフィクスチャ設計上の長さを実際に超えているかを返す。
    /// トレイトのラベルではなく実体（文字数）で判定する（タスク3.1の教訓）。
    /// </summary>
    private static bool IsOverLongPathDesignLength(FixtureItem item)
    {
        return (item.Kind == GoldenEntryKind.Folder && item.RelativePath.Length > LongPathFolderDesignLength)
            || (item.Kind == GoldenEntryKind.File && item.RelativePath.Length > LongPathFileDesignLength);
    }

    /// <summary>文字列が非ASCII文字（コードポイント127超）を実際に含むかを返す。</summary>
    private static bool ContainsNonAscii(string text)
    {
        return text.Any(c => c > 127);
    }

    /// <summary>
    /// 境界条件の欠落が既知の不具合として識別されることの検証項目を登録する（タスク6.3）。
    /// 既存の KnownIssueAnalyzer の検証は「LongPath の項目全体」を対象にしており、日本語を含む長いパスが
    /// 定義から消えても（例: 日本語の長い連鎖を ASCII に置き換えても）通過してしまうことを変異テストで確認したため、
    /// 日本語を含む長いパスを明示的に対象とする。あわせて、コミット済みの期待値に現行版の挙動として
    /// 記録されていることを確認する（完了状態「現行版の挙動として記録されている」）。
    /// </summary>
    private static void RegisterKnownIssueBoundaryChecks(SelfCheckRunner runner)
    {
        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 日本語を含む長いパスの項目は生成されているのに走査で観測されず、KnownIssueAnalyzer が根拠 LongPath の既知の欠落として列挙する。
        // 新: .NET 10 では生成された日本語を含む長いパスの項目が走査・射影の両方で観測され、既知の欠落として列挙されない。
        runner.Add("日本語を含む長いパス（相対パスがフィクスチャ設計上の長さを超える）の項目が、生成され、走査と射影の両方で観測され、KnownIssueAnalyzer が既知の欠落として列挙しない（要件3.1, 3.2, 5.1, 5.2、タスク6.3）", () =>
        {
            var spec = FixtureSpec.Standard;

            // 定義から導出する（相対パスをハードコードしない）。ラベル（Japanese と LongPath の両トレイト）だけでなく、
            // 実体（非ASCII文字を含むこと・種別ごとの文字数境界を超えること）も満たす項目だけを対象にする。
            var japaneseLongLabeled = spec.Items
                .Where(i => i.Traits.Contains(FixtureTrait.Japanese) && i.Traits.Contains(FixtureTrait.LongPath))
                .ToList();
            foreach (var item in japaneseLongLabeled)
            {
                SelfAssert.That(ContainsNonAscii(item.RelativePath), $"Japanese と LongPath を持つ項目 '{item.RelativePath}' に非ASCII文字が含まれていません。");
                SelfAssert.That(
                    IsOverLongPathDesignLength(item),
                    $"Japanese と LongPath を持つ項目 '{item.RelativePath}'（{item.Kind}、{item.RelativePath.Length}文字）が種別ごとの文字数境界を超えていません。");
            }

            var japaneseLongFolders = japaneseLongLabeled.Where(i => i.Kind == GoldenEntryKind.Folder).ToList();
            var japaneseLongFiles = japaneseLongLabeled.Where(i => i.Kind == GoldenEntryKind.File).ToList();
            SelfAssert.That(japaneseLongFolders.Count >= 1, "日本語を含み相対パス248文字（フィクスチャ設計上の長さ）を超えるフォルダ（Japanese と LongPath を持つ）が定義に1件もありません。");
            SelfAssert.That(japaneseLongFiles.Count >= 1, "日本語を含み相対パス260文字（フィクスチャ設計上の長さ）を超えるファイル（Japanese と LongPath を持つ）が定義に1件もありません。");

            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(spec, root);
                SelfAssert.That(
                    buildResult.IsComplete,
                    $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

                // 観測の検証の前提として、ディスク上に生成されていることを先に確認する。
                foreach (var item in japaneseLongLabeled)
                {
                    string extendedPath = LongPath.Extend(Path.Combine(root, item.RelativePath));
                    bool exists = item.Kind == GoldenEntryKind.Folder ? Directory.Exists(extendedPath) : File.Exists(extendedPath);
                    SelfAssert.That(exists, $"日本語を含む長いパスの項目 '{item.RelativePath}' がディスク上に生成されていません。");
                }

                var outcome = new ScanRunner().Run(root, usePhysicalSize: false);
                var scanMap = FlattenScanTree(outcome.Root);

                var header = new GoldenHeader(2, spec.Name, 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = new GoldenProjector().Project(outcome, header);
                var observedPaths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

                foreach (var item in japaneseLongLabeled)
                {
                    SelfAssert.That(
                        scanMap.ContainsKey(item.RelativePath),
                        $"日本語を含む長いパス '{item.RelativePath}'（{item.RelativePath.Length}文字）が走査結果に現れません（.NET 10 では観測されるはずです）。");
                    SelfAssert.That(
                        observedPaths.Contains(item.RelativePath),
                        $"日本語を含む長いパス '{item.RelativePath}'（{item.RelativePath.Length}文字）が射影結果に現れません（.NET 10 では観測されるはずです）。");

                    // 同じ連鎖の最上位の日本語フォルダ（境界内）も観測される。
                    string topSegment = item.RelativePath.Split('\\')[0];
                    SelfAssert.That(ContainsNonAscii(topSegment), $"'{item.RelativePath}' の最上位フォルダ '{topSegment}' が日本語を含んでいません。");
                    SelfAssert.That(
                        scanMap.ContainsKey(topSegment) && observedPaths.Contains(topSegment),
                        $"境界内の日本語フォルダ '{topSegment}' が走査結果または射影結果に現れていません。");
                }

                var findings = new KnownIssueAnalyzer().Analyze(spec, document);
                var findingPaths = new HashSet<string>(findings.Select(f => f.RelativePath), StringComparer.Ordinal);

                foreach (var item in japaneseLongLabeled)
                {
                    SelfAssert.That(
                        !findingPaths.Contains(item.RelativePath),
                        $"観測されている日本語を含む長いパス '{item.RelativePath}' が既知の欠落として列挙されました。");
                }
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(spec, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("コミット済みの期待値 baselines/fixture-v1.golden.txt が形式バージョン2・基準フォルダの実効絶対パス長80文字で記録されており、そこに文字数境界を超える項目（日本語を含む長いパスのフォルダ・ファイルを含む）が移行後の挙動どおり定義の種別とサイズで記録され、それらを含む祖先フォルダのサイズにも算入されており、その記録から KnownIssueAnalyzer が根拠 LongPath の既知の欠落を列挙しない（要件2.3, 5.1, 5.2, 7.1、タスク6.3, 7.3）", () =>
        {
            // 期待値の内容は基準フォルダの長さに左右されるため、コミット済みの期待値が「固定した長さ」で
            // 生成されていることまで照合する。ここが固定値でなければ、以下の欠落の照合は環境によって結論が変わる。
            const int ExpectedFormatVersion = 2;
            const int ExpectedBaseFolderPathLength = 80;

            var spec = FixtureSpec.Standard;

            string? goldenPath = FindCommittedGoldenFile(spec.Name);
            SelfAssert.That(
                goldenPath != null,
                $"コミット済みの期待値 'baselines\\{spec.Name}.golden.txt' を、実行ファイルの位置（{AppDomain.CurrentDomain.BaseDirectory}）から親方向に辿って見つけられませんでした。");

            // 読み取りのみ。コミット済みのファイルは書き換えない。
            // タスク7.3で形式バージョン2の期待値へ作り直したため、通常の読み取り経路でそのまま読む。
            var document = new GoldenSerializer().Read(goldenPath!);

            // 生のテキストでも照合する。読み取り経路は未知のバージョンを拒否するが、記録された値そのものが
            // 固定値であることは、読み取り後の値と生のテキストの双方で見ておく（環境非依存であることの記録）。
            string goldenText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(goldenPath!));
            SelfAssert.That(
                CountOccurrences(goldenText, $"# FormatVersion: {ExpectedFormatVersion}\n") == 1,
                $"コミット済みの期待値に '# FormatVersion: {ExpectedFormatVersion}' の行がちょうど1行ありません（形式バージョン{ExpectedFormatVersion}へ作り直されていません）。");
            SelfAssert.That(
                document.Header.FormatVersion == ExpectedFormatVersion,
                $"コミット済みの期待値の形式バージョンが{ExpectedFormatVersion}ではありません（実際: {document.Header.FormatVersion}）。");
            SelfAssert.That(
                CountOccurrences(goldenText, $"# BaseFolderPathLength: {ExpectedBaseFolderPathLength}\n") == 1,
                $"コミット済みの期待値に '# BaseFolderPathLength: {ExpectedBaseFolderPathLength}' の行がちょうど1行ありません（固定した長さの基準フォルダで生成されていません）。");
            SelfAssert.That(
                document.Header.BaseFolderPathLength == ExpectedBaseFolderPathLength,
                $"コミット済みの期待値の基準フォルダの実効絶対パス長が固定値と一致しません（期待値: {document.Header.BaseFolderPathLength}文字、固定値: {ExpectedBaseFolderPathLength}文字）。");
            // 固定値そのものが実装（generate の既定の基準フォルダ）と食い違っていれば、期待値は作り直しが必要になる。
            SelfAssert.That(
                Program.FixedBaseFolderPathLength == ExpectedBaseFolderPathLength,
                $"generate が固定する基準フォルダの実効絶対パス長（{Program.FixedBaseFolderPathLength}文字）が、コミット済みの期待値の前提（{ExpectedBaseFolderPathLength}文字）と一致しません（期待値の作り直しが必要です）。");

            SelfAssert.That(
                document.Header.BaseFolderLabel == spec.Name,
                $"期待値の基準の論理名が定義と一致しません（期待値: {document.Header.BaseFolderLabel}、定義: {spec.Name}）。");
            // 欠落が「フィクスチャを生成できなかった」ためではないことを保証する。
            SelfAssert.That(document.Header.FixtureComplete, "コミット済みの期待値が不完全なフィクスチャから生成されています（欠落の原因を境界条件に切り分けられません）。");

            var itemsByPath = spec.Items.ToDictionary(i => i.RelativePath, StringComparer.Ordinal);
            var recordedPaths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

            var overBoundaryItems = spec.Items
                .Where(i => i.Traits.Contains(FixtureTrait.LongPath) && IsOverLongPathDesignLength(i))
                .ToList();
            var japaneseOverBoundaryItems = overBoundaryItems
                .Where(i => i.Traits.Contains(FixtureTrait.Japanese) && ContainsNonAscii(i.RelativePath))
                .ToList();

            SelfAssert.That(overBoundaryItems.Any(i => i.Kind == GoldenEntryKind.Folder), "相対パス248文字（フィクスチャ設計上の長さ）を超えるフォルダ（LongPath）が定義に1件もありません。");
            SelfAssert.That(overBoundaryItems.Any(i => i.Kind == GoldenEntryKind.File), "相対パス260文字（フィクスチャ設計上の長さ）を超えるファイル（LongPath）が定義に1件もありません。");
            SelfAssert.That(japaneseOverBoundaryItems.Any(i => i.Kind == GoldenEntryKind.Folder), "日本語を含み相対パス248文字を超えるフォルダ（Japanese と LongPath）が定義に1件もありません。");
            SelfAssert.That(japaneseOverBoundaryItems.Any(i => i.Kind == GoldenEntryKind.File), "日本語を含み相対パス260文字を超えるファイル（Japanese と LongPath）が定義に1件もありません。");

            // 以下のサイズの照合は論理サイズを前提にする（物理サイズ換算ありで記録されていれば、定義のサイズとは一致しない）。
            SelfAssert.That(!document.Header.UsePhysicalSize, "コミット済みの期待値が物理サイズ換算ありで記録されています（定義の論理サイズと照合できません）。");

            var entriesByPath = document.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);

            // 定義から導く、期待値に記録されるべきサイズ。ファイルは定義の内容サイズ、フォルダは配下のファイルの内容サイズの合計。
            long ExpectedRecordedSize(FixtureItem target)
            {
                if (target.Kind == GoldenEntryKind.File)
                {
                    return target.ContentSizeInBytes;
                }

                string prefix = target.RelativePath + "\\";
                return spec.Items
                    .Where(i => i.Kind == GoldenEntryKind.File && i.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
                    .Sum(i => i.ContentSizeInBytes);
            }

            // 記録されていること、種別とサイズが定義から導いた値と一致することを照合する。
            void AssertRecordedAsDefined(FixtureItem target, string label)
            {
                SelfAssert.That(
                    entriesByPath.TryGetValue(target.RelativePath, out var entry),
                    $"{label} '{target.RelativePath}'（{target.RelativePath.Length}文字）がコミット済みの期待値に記録されていません（移行後の挙動と異なります）。");
                SelfAssert.That(
                    entry!.Kind == target.Kind,
                    $"{label} '{target.RelativePath}' の種別が定義と一致しません（期待値: {entry.Kind}、定義: {target.Kind}）。");
                long expectedSize = ExpectedRecordedSize(target);
                SelfAssert.That(
                    entry.SizeInBytes == expectedSize,
                    $"{label} '{target.RelativePath}' のサイズが定義から導いた値と一致しません（期待値: {entry.SizeInBytes}バイト、定義: {expectedSize}バイト）。");
            }

            foreach (var item in overBoundaryItems)
            {
                string kindLabel = japaneseOverBoundaryItems.Contains(item) ? "日本語を含む長いパスの項目" : "文字数境界を超える項目";
                AssertRecordedAsDefined(item, kindLabel);

                // 境界を超える項目のサイズが、それを含む祖先フォルダ（境界の内外を問わず連鎖のすべて）のサイズにも
                // 算入されていることを確かめる。旧い期待値ではここが 0 で記録されていた（research.md 移行の記録 2.6）。
                string[] segments = item.RelativePath.Split('\\');
                bool hasAncestorWithinBoundary = false;
                for (int depth = segments.Length - 1; depth >= 1; depth--)
                {
                    string ancestor = string.Join("\\", segments, 0, depth);
                    SelfAssert.That(
                        itemsByPath.TryGetValue(ancestor, out var ancestorItem),
                        $"'{item.RelativePath}' の祖先フォルダ '{ancestor}' が定義にありません。");
                    hasAncestorWithinBoundary |= !IsOverLongPathDesignLength(ancestorItem!);
                    AssertRecordedAsDefined(ancestorItem!, $"'{item.RelativePath}' の祖先フォルダ");
                    SelfAssert.That(
                        entriesByPath[ancestor].SizeInBytes >= ExpectedRecordedSize(item),
                        $"'{item.RelativePath}' のサイズが祖先フォルダ '{ancestor}' のサイズに算入されていません。");
                }

                SelfAssert.That(hasAncestorWithinBoundary, $"'{item.RelativePath}' に境界を超えない祖先フォルダが定義されていません。");
            }

            var findings = new KnownIssueAnalyzer().Analyze(spec, document);
            var findingsByPath = findings.ToDictionary(f => f.RelativePath, f => f.Trait, StringComparer.Ordinal);

            foreach (var item in overBoundaryItems)
            {
                string kindLabel = japaneseOverBoundaryItems.Contains(item) ? "日本語を含む長いパス" : "長いパス";
                SelfAssert.That(
                    !findingsByPath.TryGetValue(item.RelativePath, out var trait),
                    $"記録されている{kindLabel} '{item.RelativePath}' が、コミット済みの期待値に対する既知の欠落として列挙されました（根拠: {trait}）。");
            }

            int longPathFindingCount = findings.Count(f => f.Trait == FixtureTrait.LongPath);
            SelfAssert.That(
                longPathFindingCount == 0,
                $"コミット済みの期待値に対して根拠 LongPath の既知の欠落が列挙されました（{longPathFindingCount}件。0件のはずです）。");
        });
    }

    /// <summary>
    /// 実行ファイルの位置から親方向へ辿り、コミット済みの期待値 <c>baselines\&lt;論理名&gt;.golden.txt</c> を探す。
    /// ツールはリポジトリ内の <c>Tools\GoldenBaseline\bin\...</c> から実行されるため、祖先のいずれかがリポジトリの
    /// ルートになる。見つからなければ null を返す（呼び出し側で失敗として扱い、黙って通過させない）。
    /// </summary>
    private static string? FindCommittedGoldenFile(string baseFolderLabel)
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "baselines", baseFolderLabel + ".golden.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// ScanRunner の走査結果ツリーを、基準フォルダからの相対パスをキーとする平坦なマップへ変換する。
    /// テスト専用のヘルパーであり、GoldenProjector（タスク4.2）の実装ではない
    /// （相対パスの区切り文字は本ヘルパー内で \ に統一しているのみで、射影の正式な契約は別途 4.2 で定める）。
    /// </summary>
    private static Dictionary<string, (bool IsFile, long Size)> FlattenScanTree(FolderInfo root)
    {
        var map = new Dictionary<string, (bool IsFile, long Size)>(StringComparer.Ordinal);

        void Walk(FolderInfo node, string relativePath)
        {
            if (!string.IsNullOrEmpty(relativePath))
            {
                map[relativePath] = (node.IsFile, node.Size);
            }

            foreach (var child in node.Children)
            {
                string childRelativePath = string.IsNullOrEmpty(relativePath) ? child.Name : relativePath + "\\" + child.Name;
                Walk(child, childRelativePath);
            }
        }

        Walk(root, string.Empty);
        return map;
    }

    // ==================================================================
    // タスク7.4: generate / compare / update が同じ走査の報告を出すことの検証
    // ==================================================================

    /// <summary>
    /// 走査の報告を構成する4区画の件数行の接頭辞（タスク7.4）。
    /// generate だけでなく compare / update も、走査で観測した事実をこの4区画で報告する。
    /// </summary>
    private static readonly string[] ScanReportSectionPrefixes = new[]
    {
        "スキップされた対象: ",
        "長さのせいで列挙できなかった対象: ",
        "既知の欠落（境界条件に由来）: ",
        "説明できない欠落（既知の不具合では説明できない未観測の項目）: ",
    };

    /// <summary>
    /// <see cref="ScanReportSectionPrefixes"/> の並びにおける、スキップされた対象の区画の位置。
    /// </summary>
    private const int SkippedSectionIndex = 0;

    /// <summary>長さのせいで列挙できなかった対象の区画の位置。</summary>
    private const int UnenumerableSectionIndex = 1;

    /// <summary>既知の欠落の区画の位置。</summary>
    private const int KnownIssueSectionIndex = 2;

    /// <summary>説明できない欠落の区画の位置。</summary>
    private const int UnexplainedSectionIndex = 3;

    /// <summary>走査の報告の1区画（件数行と、その直後に続く明細行）。</summary>
    private sealed class ScanReportSection
    {
        public ScanReportSection(string prefix, string countLine, int countLineIndex, int count, List<string> details)
        {
            Prefix = prefix;
            CountLine = countLine;
            CountLineIndex = countLineIndex;
            Count = count;
            Details = details;
        }

        /// <summary>この区画を見分ける件数行の接頭辞。</summary>
        public string Prefix { get; }

        /// <summary>件数行そのもの（3経路の内容の一致は、この行の文字列で照合する）。</summary>
        public string CountLine { get; }

        /// <summary>標準出力（空行を除いた行の並び）における件数行の位置。順序の照合に用いる。</summary>
        public int CountLineIndex { get; }

        /// <summary>件数行が示す件数。</summary>
        public int Count { get; }

        /// <summary>件数行に続く明細（先頭の "  - " を除いた本体）。</summary>
        public List<string> Details { get; }
    }

    /// <summary>
    /// 標準出力から走査の報告の4区画を取り出す。区画が欠けていれば失敗し、
    /// 件数行の件数と明細の行数が食い違っていても失敗する
    /// （件数だけを固定値へ潰す変異と、明細だけを落とす変異の双方を落とすため）。
    /// </summary>
    private static List<ScanReportSection> ParseScanReport(string context, string stdOut)
    {
        string[] lines = SplitStdOutLines(stdOut);
        var sections = new List<ScanReportSection>();

        foreach (var prefix in ScanReportSectionPrefixes)
        {
            var indices = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    indices.Add(i);
                }
            }

            SelfAssert.That(
                indices.Count == 1,
                $"{context}: 走査の報告の区画「{prefix}」の件数行がちょうど1行ではありません（実際: {indices.Count} 行）。標準出力:\n{stdOut}");

            int countLineIndex = indices[0];
            string countLine = lines[countLineIndex];
            string countText = countLine.Substring(prefix.Length);

            const string CountSuffix = " 件";
            SelfAssert.That(
                countText.EndsWith(CountSuffix, StringComparison.Ordinal),
                $"{context}: 区画「{prefix}」の件数行が「N 件」の形ではありません: '{countLine}'");

            int count;
            bool parsed = int.TryParse(
                countText.Substring(0, countText.Length - CountSuffix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out count);
            SelfAssert.That(parsed, $"{context}: 区画「{prefix}」の件数を数値として読み取れません: '{countLine}'");

            const string DetailPrefix = "  - ";
            var details = new List<string>();
            for (int i = countLineIndex + 1; i < lines.Length && lines[i].StartsWith(DetailPrefix, StringComparison.Ordinal); i++)
            {
                details.Add(lines[i].Substring(DetailPrefix.Length));
            }

            SelfAssert.That(
                details.Count == count,
                $"{context}: 区画「{prefix}」の件数（{count} 件）と明細の行数（{details.Count} 行）が一致しません。標準出力:\n{stdOut}");

            sections.Add(new ScanReportSection(prefix, countLine, countLineIndex, count, details));
        }

        return sections;
    }

    /// <summary>標準出力を、空行を除いた行の並びへ分解する（報告の順序を行番号で照合するために共通化する）。</summary>
    private static string[] SplitStdOutLines(string stdOut)
    {
        return stdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>説明できない欠落として報告されるべき明細の形を、フィクスチャ定義の項目から組み立てる。</summary>
    private static string FormatExpectedUnexplainedDetail(FixtureItem item)
    {
        return $"{item.RelativePath}（境界条件: {string.Join("、", item.Traits.Select(t => t.ToString()))}）";
    }

    /// <summary>
    /// 2つの明細の集合が（順序を問わず）過不足なく一致することを照合する。
    /// </summary>
    private static void AssertDetailsMatch(string context, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        SelfAssert.That(
            actual.OrderBy(d => d, StringComparer.Ordinal)
                .SequenceEqual(expected.OrderBy(d => d, StringComparer.Ordinal), StringComparer.Ordinal),
            $"{context}: 明細が想定と一致しません。想定（{expected.Count} 件）: [{string.Join(" | ", expected)}] 実際（{actual.Count} 件）: [{string.Join(" | ", actual)}]");
    }

    /// <summary>
    /// 走査の報告の4区画が、generate の報告と同じ件数行になっていることを照合する。
    /// </summary>
    private static void AssertScanReportCountLinesMatch(string context, List<ScanReportSection> expected, List<ScanReportSection> actual)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            SelfAssert.That(
                actual[i].CountLine == expected[i].CountLine,
                $"{context}: 区画「{expected[i].Prefix}」の件数行が generate と一致しません（generate: '{expected[i].CountLine}' / {context}: '{actual[i].CountLine}'）。");
        }
    }

    /// <summary>
    /// 走査の報告が「この実行の走査から得た事実」であることを、基準フォルダ由来の明細で照合する。
    /// generate の出力をそのまま写すような実装では成立しない。
    /// </summary>
    private static void AssertScanReportReflectsThisRun(string context, List<ScanReportSection> sections, string root)
    {
        var deniedItems = FixtureSpec.Standard.Items
            .Where(item => item.Traits.Contains(FixtureTrait.AccessDenied))
            .ToList();
        SelfAssert.That(deniedItems.Count > 0, "FixtureSpec.Standard に AccessDenied トレイトの項目が見つかりません。");

        var skipped = sections[SkippedSectionIndex];
        SelfAssert.That(
            skipped.Count == deniedItems.Count,
            $"{context}: スキップされた対象の件数（{skipped.Count} 件）が FixtureSpec.Standard の AccessDenied 項目数（{deniedItems.Count} 件）と一致しません。");

        foreach (var item in deniedItems)
        {
            string deniedFullPath = Path.Combine(root, item.RelativePath);
            SelfAssert.That(
                skipped.Details.Any(d => string.Equals(d, deniedFullPath, StringComparison.OrdinalIgnoreCase)),
                $"{context}: スキップされた対象に、この実行の基準フォルダ配下の拒否フォルダ '{deniedFullPath}' が報告されていません: [{string.Join(" | ", skipped.Details)}]");
        }

        // dotnet10-migration タスク2.4 で期待を改めた（この補助関数を使う compare / update の2項目が対象）。
        // 旧: 境界を超える階層があるため「長さのせいで列挙できなかった対象」が1件以上あり、
        //     既知の欠落の明細が FixtureSpec.Standard の LongPath 項目と過不足なく一致する。
        // 新: .NET 10 は長いパスを列挙できるため、どちらも0件になる。
        var unenumerable = sections[UnenumerableSectionIndex];
        SelfAssert.That(
            unenumerable.Count == 0,
            $"{context}: 長さのせいで列挙できなかった対象が0件ではありません（.NET 10 では長いパスも列挙できるはずです）: [{string.Join(" | ", unenumerable.Details)}]");

        AssertDetailsMatch(
            $"{context}: 既知の欠落",
            new List<string>(),
            sections[KnownIssueSectionIndex].Details);
    }

    /// <summary>
    /// 走査の報告の4区画が、指定した文言の行より前に出力されていることを照合する
    /// （update は期待値ファイルを書き換える経路であり、書き換える前に開発者が気づける順序でなければならない）。
    /// </summary>
    private static void AssertScanReportPrecedesLine(string context, List<ScanReportSection> sections, string stdOut, string laterLineToken)
    {
        string[] lines = SplitStdOutLines(stdOut);
        int laterIndex = Array.FindIndex(lines, line => line.Contains(laterLineToken));
        SelfAssert.That(laterIndex >= 0, $"{context}: 標準出力に '{laterLineToken}' を含む行がありません: {stdOut}");

        foreach (var section in sections)
        {
            SelfAssert.That(
                section.CountLineIndex < laterIndex,
                $"{context}: 区画「{section.Prefix}」の件数行（{section.CountLineIndex}行目）が '{laterLineToken}' の行（{laterIndex}行目）より後に現れています。" +
                $"書き換えの前に報告するという責務に反します: {stdOut}");
        }
    }

    /// <summary>
    /// compare / update も、generate と同じ走査の報告（4区画）を出すことを検証する項目を登録する（タスク7.4）。
    /// </summary>
    /// <remarks>
    /// 報告の内容は判定にも終了コードにも影響しない（要件5.5）。とくに update は期待値ファイルを
    /// 書き換える経路であり、説明のつかない欠落が生じたまま更新すると、その区別が失われたまま
    /// 新しい期待値が書かれる。書き換えの前に報告が出ることまで照合する。
    /// </remarks>
    private static void RegisterScanReportParityChecks(SelfCheckRunner runner)
    {
        runner.Add("compare が、走査で得た4区画（スキップされた対象・長さのせいで列挙できなかった対象・既知の欠落・説明できない欠落）を generate と同じ件数行で報告し、明細はこの実行の走査に由来する（要件2.4, 5.2、タスク7.4）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", cmpRoot);
                SelfAssert.That(cmpResult.ExitCode == 0, $"compare（一致想定）の終了コードが0ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("判定: 一致"), $"compare の標準出力に一致の判定が含まれません: {cmpResult.StdOut}");

                var genSections = ParseScanReport("generate", genResult.StdOut);
                var cmpSections = ParseScanReport("compare", cmpResult.StdOut);

                AssertScanReportCountLinesMatch("compare", genSections, cmpSections);
                AssertScanReportReflectsThisRun("compare", cmpSections, cmpRoot);

                // 既知の欠落が0件と報告されたことが、実際に「欠落していない」ことと整合するかを見る。
                // dotnet10-migration タスク2.4 で期待を改めた。旧: LongPath の項目は走査結果（この期待値ファイルのエントリ）に現れない。
                // 新: .NET 10 では LongPath の項目もすべて走査結果に現れる。
                var goldenDocument = new GoldenSerializer().Read(goldenPath);
                foreach (var item in FixtureSpec.Standard.Items.Where(i => i.Traits.Contains(FixtureTrait.LongPath)))
                {
                    SelfAssert.That(
                        goldenDocument.Entries.Any(e => e.RelativePath == item.RelativePath),
                        $"LongPath の項目 '{item.RelativePath}' が走査結果のエントリに存在しません（.NET 10 では観測されるはずです）。");
                }

                SelfAssert.That(
                    !Directory.Exists(cmpRoot) && !Directory.Exists(LongPath.Extend(cmpRoot)),
                    $"compare の実行後にフィクスチャが残留しています: {cmpRoot}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("update が、走査で得た4区画を generate と同じ件数行で報告し、その報告が期待値ファイルの書き換え（「更新しました」）より前に出る（要件2.4, 5.2, 5.3, 5.4、タスク7.4）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string updRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                byte[] before = File.ReadAllBytes(goldenPath);

                var updResult = RunGoldenBaselineProcess("update", "--golden", goldenPath, "--root", updRoot);
                SelfAssert.That(updResult.ExitCode == 0, $"update（一致想定）の終了コードが0ではありません（実際: {updResult.ExitCode}）。標準出力: {updResult.StdOut} 標準エラー: {updResult.StdErr}");

                var genSections = ParseScanReport("generate", genResult.StdOut);
                var updSections = ParseScanReport("update", updResult.StdOut);

                AssertScanReportCountLinesMatch("update", genSections, updSections);
                AssertScanReportReflectsThisRun("update", updSections, updRoot);

                // 書き換えの前に報告が出ること（差分の提示と同じく、書き換える前に開発者が気づける順序であること）。
                AssertScanReportPrecedesUpdateLine(updSections, updResult.StdOut);

                // 「更新しました」の行が実際の書き換えを伴っていること（順序の照合が空振りしないための裏取り）。
                byte[] after = File.ReadAllBytes(goldenPath);
                SelfAssert.That(!before.SequenceEqual(after), "update を実行しても期待値ファイルの内容が変化していません（順序の照合の前提が崩れています）。");

                SelfAssert.That(
                    !Directory.Exists(updRoot) && !Directory.Exists(LongPath.Extend(updRoot)),
                    $"update の実行後にフィクスチャが残留しています: {updRoot}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(updRoot);
            }
        });

        runner.Add("走査でだけ生じた（期待値ファイルには記録されていない）説明できない欠落を、compare と update が自分の走査から報告し、終了コードは突き合わせの判定だけで決まる（要件5.2, 5.5、タスク7.4）", () =>
        {
            // 報告が「期待値ファイルの中身」ではなく「この実行の走査」に由来することを確かめる。
            // 期待値の側と走査の側を同じ状態にすると両者が一致してしまい、観測値と期待値の取り違え
            // （報告へ Actual ではなく Expected を渡す誤り）を検出できない。そこで期待値は妨げのない
            // フィクスチャから作り、compare / update の側だけを妨げて、期待値には無い説明できない欠落を作る。
            // これはタスク7.4 が存在する理由そのもの（説明のつかない欠落が生じたまま期待値を書き換える事故）を
            // 直接の対象にした検証である。
            string genRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string updRoot = CreateTempFixtureRoot();
            string verifyRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                // 基準フォルダの実効絶対パス長が違うと、エントリの突き合わせの前に設定不一致で打ち切られ、
                // 判定も報告も別の経路になってしまう（要件6.2、タスク7.1）。長さが揃っていることを前提として明示する。
                int genLength = Program.MeasureEffectivePathLength(genRoot);
                foreach (var root in new[] { cmpRoot, updRoot, verifyRoot })
                {
                    int rootLength = Program.MeasureEffectivePathLength(root);
                    SelfAssert.That(
                        rootLength == genLength,
                        $"基準フォルダの実効絶対パス長が揃っていません（期待値の生成側: {genLength} 文字、{root}: {rootLength} 文字）。" +
                        $"設定不一致になり、この検証の前提が崩れます。");
                }

                var blockedItems = FixtureSpec.Standard.Items
                    .Where(i => i.RelativePath.StartsWith(BlockedFixtureFolderRelativePath + "\\", StringComparison.Ordinal))
                    .ToList();
                SelfAssert.That(
                    blockedItems.Count > 0,
                    $"FixtureSpec.Standard に '{BlockedFixtureFolderRelativePath}' の配下の項目が見つかりません。");
                SelfAssert.That(
                    blockedItems.All(i => !i.Traits.Contains(FixtureTrait.LongPath)),
                    $"'{BlockedFixtureFolderRelativePath}' の配下に LongPath トレイトの項目があります。" +
                    $"既知の欠落として説明されてしまうため、この検証の前提が崩れます。");

                var expectedUnexplainedDetails = blockedItems.Select(FormatExpectedUnexplainedDetail).ToList();

                // 期待値は妨げのないフィクスチャから作る。この期待値には説明できない欠落が記録されていない。
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準出力: {genResult.StdOut} 標準エラー: {genResult.StdErr}");

                var genSections = ParseScanReport("generate（妨げなし）", genResult.StdOut);
                SelfAssert.That(
                    genSections[UnexplainedSectionIndex].Count == 0,
                    $"前提が崩れています: 妨げのないフィクスチャの generate が説明できない欠落を {genSections[UnexplainedSectionIndex].Count} 件報告しました。" +
                    $"期待値の側と走査の側で説明できない欠落が食い違う状況を作れません。");

                // compare: 走査の側だけを妨げる。期待値ファイルには無い説明できない欠落を、自分の走査から報告すること。
                BlockFixtureFolderAndObserveFailures(cmpRoot);
                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", cmpRoot);

                var cmpSections = ParseScanReport("compare（走査の側だけ妨げた）", cmpResult.StdOut);
                AssertDetailsMatch(
                    "compare（走査の側だけ妨げた）: 説明できない欠落",
                    expectedUnexplainedDetails,
                    cmpSections[UnexplainedSectionIndex].Details);

                // 突き合わせが打ち切られていないこと（設定不一致だと報告も判定も別の経路になる）。
                SelfAssert.That(
                    cmpResult.StdOut.Contains("判定: 差分あり"),
                    $"compare の判定が差分ありではありません（妨げた項目が欠落するため差分ありになるはずです）: {cmpResult.StdOut}");

                // 終了コードは判定から決まる値と一致すること。報告の件数では動かない（要件5.5）。
                AssertExitCodeMatchesVerdict("compare（走査の側だけ妨げた）", cmpResult.ExitCode, cmpResult.StdOut);

                // update: 同じ状況で、報告が書き換えより前に出て、終了コードも判定どおりであること。
                byte[] before = File.ReadAllBytes(goldenPath);

                BlockFixtureFolderAndObserveFailures(updRoot);
                var updResult = RunGoldenBaselineProcess("update", "--golden", goldenPath, "--root", updRoot);

                var updSections = ParseScanReport("update（走査の側だけ妨げた）", updResult.StdOut);
                AssertDetailsMatch(
                    "update（走査の側だけ妨げた）: 説明できない欠落",
                    expectedUnexplainedDetails,
                    updSections[UnexplainedSectionIndex].Details);
                AssertScanReportPrecedesUpdateLine(updSections, updResult.StdOut);
                SelfAssert.That(
                    updResult.StdOut.Contains("判定: 差分あり"),
                    $"update の判定が差分ありではありません: {updResult.StdOut}");
                AssertExitCodeMatchesVerdict("update（走査の側だけ妨げた）", updResult.ExitCode, updResult.StdOut);

                byte[] after = File.ReadAllBytes(goldenPath);
                SelfAssert.That(!before.SequenceEqual(after), "update を実行しても期待値ファイルの内容が変化していません（順序の照合の前提が崩れています）。");

                // 報告だけが正しくても、書き換えの中身が走査結果でなければ意味がない。
                // 妨げによって観測されなかった項目が、新しい期待値から実際に消えていることを確かめる。
                var updatedDocument = new GoldenSerializer().Read(goldenPath);
                foreach (var item in blockedItems)
                {
                    SelfAssert.That(
                        !updatedDocument.Entries.Any(e => e.RelativePath == item.RelativePath),
                        $"update 後の期待値に、走査で観測されなかったはずの '{item.RelativePath}' が残っています。");
                }

                // 仕上げ: 「判定: 一致」かつ「説明できない欠落が0件でない」局面を作る。
                // ここまでの compare / update はいずれも判定が差分あり（終了コード1）であり、
                // 「説明できない欠落の件数で終了コードを 1 に倒す」変異は、変異が強制する値と
                // 判定から決まる値が偶然一致してしまうため捕まえられない（要件5.5 の守りが空く）。
                // update 後の goldenPath は「妨げた状態の期待値」になっているので、もう1つ妨げた
                // 基準フォルダで compare すれば、判定は一致のまま説明できない欠落が4件報告される。
                // ここに終了コードの照合を当てることで、リテラルを書かずに 0 が固定される。
                BlockFixtureFolderAndObserveFailures(verifyRoot);
                var verifyResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", verifyRoot);

                var verifySections = ParseScanReport("compare（妨げた期待値との一致）", verifyResult.StdOut);
                AssertDetailsMatch(
                    "compare（妨げた期待値との一致）: 説明できない欠落",
                    expectedUnexplainedDetails,
                    verifySections[UnexplainedSectionIndex].Details);
                SelfAssert.That(
                    verifySections[UnexplainedSectionIndex].Count > 0,
                    $"前提が崩れています: 説明できない欠落が0件のため、終了コードとの独立性を確かめられません: {verifyResult.StdOut}");
                SelfAssert.That(
                    verifyResult.StdOut.Contains("判定: 一致"),
                    $"妨げた状態の期待値との compare の判定が一致ではありません（説明できない欠落があっても判定には影響しないはずです）: {verifyResult.StdOut}");
                AssertExitCodeMatchesVerdict("compare（妨げた期待値との一致）", verifyResult.ExitCode, verifyResult.StdOut);

                foreach (var root in new[] { genRoot, cmpRoot, updRoot, verifyRoot })
                {
                    SelfAssert.That(
                        !Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)),
                        $"実行後にフィクスチャが残留しています: {root}");
                }
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(cmpRoot);
                ForceCleanupFixtureResidue(updRoot);
                ForceCleanupFixtureResidue(verifyRoot);
            }
        });
    }

    /// <summary>
    /// 終了コードが、標準出力に現れた判定から決まる値と一致することを照合する。
    /// 期待する終了コードをリテラルで固定しないため、「報告の内容で終了コードを動かす」変異
    /// （要件5.5 に反する方向）を、判定がどの値であっても落とせる。
    /// </summary>
    private static void AssertExitCodeMatchesVerdict(string context, int exitCode, string stdOut)
    {
        // design.md / Program: 一致は0、差分ありは1、設定不一致は2。
        var verdicts = new[]
        {
            ("判定: 一致", 0),
            ("判定: 差分あり", 1),
            ("判定: 設定不一致", 2),
        };

        var matched = verdicts.Where(v => stdOut.Contains(v.Item1)).ToList();
        SelfAssert.That(
            matched.Count == 1,
            $"{context}: 判定の行がちょうど1種類ではありません（実際: {matched.Count} 種類）。標準出力: {stdOut}");
        SelfAssert.That(
            exitCode == matched[0].Item2,
            $"{context}: 終了コード（{exitCode}）が判定「{matched[0].Item1}」から決まる値（{matched[0].Item2}）と一致しません。" +
            $"報告の内容は判定にも終了コードにも影響してはなりません（要件5.5）。標準出力: {stdOut}");
    }

    /// <summary>
    /// update の走査の報告が、書き換えの報告（「更新しました」）より前に出ていることを照合する。
    /// </summary>
    private static void AssertScanReportPrecedesUpdateLine(List<ScanReportSection> sections, string stdOut)
    {
        AssertScanReportPrecedesLine("update", sections, stdOut, "更新しました");
    }

    /// <summary>
    /// 本体の事前カウント（Scanner.CountFoldersAsync）の検証項目を登録する（scan-correctness タスク2.1）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="ScanRunner.CountFolders"/> を通す。
    /// </summary>
    private static void RegisterFolderCounterChecks(SelfCheckRunner runner)
    {
        runner.Add("事前カウントが、深さの上限6で FixtureSpec.Standard から導いたフォルダ数（ASCII と日本語の長い連鎖の全階層を含む）と一致する（scan-correctness 要件1.1, 1.2, 1.5, 2.1）", () =>
        {
            const int MaxDepth = 6;
            string root = CreateTempFixtureRoot();
            var builder = new FixtureBuilder();
            bool cleanedUp = false;

            try
            {
                var buildResult = builder.Build(FixtureSpec.Standard, root);
                SelfAssert.That(
                    buildResult.IsComplete,
                    $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

                int expected = CountExpectedFolders(FixtureSpec.Standard, MaxDepth);
                int actual = new ScanRunner().CountFolders(root, MaxDepth);

                SelfAssert.That(
                    actual == expected,
                    $"事前カウントの数が定義から導いた数と一致しません（期待: {expected}, 実際: {actual}, 深さの上限: {MaxDepth}）。");
            }
            finally
            {
                if (!cleanedUp)
                {
                    try
                    {
                        builder.TearDown(FixtureSpec.Standard, root);
                        cleanedUp = true;
                    }
                    catch
                    {
                        // フォールバックへ進む。
                    }
                }

                ForceCleanupFixtureResidue(root);
            }
        });
    }

    /// <summary>
    /// 本体の走査の最後の報告（ScanProgress.IsFinal）の検証項目を登録する（scan-correctness タスク2.3）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="ScanRunner.RunForFinalProgress"/> と <see cref="ScanRunner.CountFolders"/> を通す。
    /// </summary>
    private static void RegisterFinalProgressChecks(SelfCheckRunner runner)
    {
        foreach (int maxDepth in new[] { 3, 6 })
        {
            foreach (bool useParallel in new[] { false, true })
            {
                int depth = maxDepth;
                bool parallel = useParallel;
                string mode = parallel ? "並列" : "逐次";

                runner.Add($"深さの上限{depth}（{mode}）で、事前カウントの数と走査の最後の報告の数が一致する（access_denied_folder を含む。scan-correctness 要件1.4, 1.5）", () =>
                {
                    WithStandardFixture(root =>
                    {
                        var scanRunner = new ScanRunner();
                        int counted = scanRunner.CountFolders(root, depth);
                        var outcome = scanRunner.RunForFinalProgress(root, depth, parallel);

                        SelfAssert.That(outcome.Root != null, "走査が結果の木を返しませんでした。");
                        SelfAssert.That(
                            outcome.FinalReportCount == 1,
                            $"最後の報告（IsFinal）がちょうど1回届きませんでした（届いた回数: {outcome.FinalReportCount}）。");

                        var final = outcome.FinalProgress!;
                        SelfAssert.That(
                            final.ProcessedFolders == counted,
                            $"事前カウントの数と最後の報告の数が一致しません（事前カウント: {counted}, 最後の報告: {final.ProcessedFolders}, 深さの上限: {depth}）。");

                        // 深さの上限より深い階層が数に入らない（打ち切り）ことを、定義から導いた数とも照合する
                        int expected = CountExpectedFolders(FixtureSpec.Standard, depth);
                        SelfAssert.That(
                            final.ProcessedFolders == expected,
                            $"最後の報告の数が定義から導いた数と一致しません（期待: {expected}, 実際: {final.ProcessedFolders}, 深さの上限: {depth}）。");

                        // 深さの上限3では、フィクスチャのより深い階層が実際に数から外れていること（打ち切りが効く条件であること）を確かめる
                        int unlimited = CountExpectedFolders(FixtureSpec.Standard, int.MaxValue);
                        SelfAssert.That(
                            depth >= 6 || expected < unlimited,
                            $"深さの上限{depth}で打ち切られる階層がフィクスチャにありません（上限あり: {expected}, 上限なし: {unlimited}）。");
                    });
                });
            }
        }

        runner.Add("access_denied_folder を含むフィクスチャの走査が完了し、最後の報告のスキップの一覧にそのパスが AccessDenied で入る（scan-correctness 要件3.4, 5.3）", () =>
        {
            WithStandardFixture(root =>
            {
                string deniedPath = Path.Combine(
                    root,
                    FixtureSpec.Standard.Items
                        .Single(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.AccessDenied))
                        .RelativePath);

                var outcome = new ScanRunner().RunForFinalProgress(root, 6, useParallel: false);

                SelfAssert.That(outcome.Root != null, "走査が結果の木を返しませんでした（完了していません）。");
                SelfAssert.That(
                    outcome.FinalProgress != null && outcome.FinalProgress.IsFinal,
                    "走査の最後の報告（IsFinal）が届きませんでした。");

                var skipped = outcome.FinalProgress!.Skipped;
                var match = skipped.Where(s => string.Equals(s.Path, deniedPath, StringComparison.OrdinalIgnoreCase)).ToList();
                SelfAssert.That(
                    match.Count == 1 && match[0].Kind == global::LargeFolderFinder.ScanSkipKind.AccessDenied,
                    $"スキップの一覧にアクセス拒否のフォルダが AccessDenied で1件入っていません（対象: {deniedPath}, 一覧: {string.Join(", ", skipped.Select(s => $"[{s.Kind}] {s.Path}"))}）。");

                // 拒否されたフォルダ自身は結果の木に残る（スキップしても走査は止まらない）
                SelfAssert.That(
                    outcome.Root!.Children.Any(c => !c.IsFile && string.Equals(c.Name, Path.GetFileName(deniedPath), StringComparison.OrdinalIgnoreCase)),
                    "アクセス拒否のフォルダが結果の木にありません。");
            });
        });
    }

    /// <summary>
    /// FixtureSpec.Standard のフィクスチャを一時領域に作り、<paramref name="action"/> を実行してから後始末する。
    /// </summary>
    private static void WithStandardFixture(Action<string> action)
    {
        string root = CreateTempFixtureRoot();
        var builder = new FixtureBuilder();

        try
        {
            var buildResult = builder.Build(FixtureSpec.Standard, root);
            SelfAssert.That(
                buildResult.IsComplete,
                $"前提となるフィクスチャ生成が完了しませんでした。未生成: {string.Join(", ", buildResult.Omissions.Select(o => o.RelativePath))}");

            action(root);
        }
        finally
        {
            try
            {
                builder.TearDown(FixtureSpec.Standard, root);
            }
            catch
            {
                // フォールバックへ進む。
            }

            // 通常経路では実質的に何もしない最終手段（既存の項目と同じ後始末）
            ForceCleanupFixtureResidue(root);
        }
    }

    /// <summary>
    /// フィクスチャの定義から、本スキャンと同じ規則で数えたときのフォルダ数を導く。
    /// 起点（基準フォルダ）を深さ0として1と数え、深さが <paramref name="maxDepth"/> 以下のフォルダを数える。
    /// 読み取り拒否（<see cref="FixtureTrait.AccessDenied"/>）のフォルダは自身を数え、配下は数えない。
    /// </summary>
    private static int CountExpectedFolders(FixtureSpec spec, int maxDepth)
    {
        var deniedFolders = spec.Items
            .Where(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.AccessDenied))
            .Select(i => i.RelativePath)
            .ToList();

        int count = 1; // 起点
        foreach (var item in spec.Items)
        {
            if (item.Kind != GoldenEntryKind.Folder)
            {
                continue;
            }

            int depth = item.RelativePath.Split('\\').Length;
            if (depth > maxDepth)
            {
                continue;
            }

            bool underDenied = deniedFolders.Any(d => item.RelativePath.StartsWith(d + "\\", StringComparison.Ordinal));
            if (underDenied)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// FixtureBuilder の検証で使う、一時領域の基準フォルダパスを組み立てる。
    /// フォルダ自体はまだ作成しない。既存規約に合わせ "gb_fix_" 接頭辞を用いる。
    /// </summary>
    private static string CreateTempFixtureRoot()
    {
        return Path.Combine(Path.GetTempPath(), "gb_fix_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    }

    /// <summary>
    /// FixtureBuilder の検証の後始末が何らかの理由で失敗した場合の最終手段。
    /// icacls で ACL をリセットしてから再度削除を試みる
    /// （tasks.md Implementation Notes: タスク3.2で4件の残留が発生した際の除去手順と同じ）。
    /// 通常経路（TearDown が正しく機能している場合）では実質的に何もしない。
    /// </summary>
    private static void ForceCleanupFixtureResidue(string root)
    {
        string extendedRoot = LongPath.Extend(root);
        bool existsPlain = Directory.Exists(root);
        bool existsExtended = Directory.Exists(extendedRoot);

        if (!existsPlain && !existsExtended)
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("icacls.exe", $"\"{extendedRoot}\" /reset /T /C /Q")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (var process = Process.Start(psi))
            {
                process?.WaitForExit(30000);
            }
        }
        catch
        {
            // icacls 自体が使えない環境でも、削除の再試行だけは行う。
        }

        try
        {
            if (Directory.Exists(extendedRoot))
            {
                Directory.Delete(extendedRoot, recursive: true);
            }
        }
        catch
        {
            // ここで失敗した場合は、呼び出し側（自己検証の最終確認）が残留として検出する。
        }
    }

    /// <summary>
    /// 本体の OS 呼び出しの宣言の整理の検証項目を登録する（scan-correctness タスク2.4）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="ScanRunner.GetWin32DeclaredMemberNames"/> を通す。
    /// </summary>
    private static void RegisterWin32DeclarationChecks(SelfCheckRunner runner)
    {
        runner.Add("本体の OS 呼び出しの宣言の型に、事前カウント用の宣言と未使用の宣言が無く、クラスタサイズの取得とメモリの切り詰めの宣言だけが残っている（scan-correctness 要件2.1, 2.2）", () =>
        {
            IReadOnlySet<string> declared = ScanRunner.GetWin32DeclaredMemberNames();

            // 除いたはずの宣言（事前カウント用の列挙の API・構造体・定数・列挙と、未使用の宣言）
            string[] removed =
            {
                "FindFirstFileEx",
                "FindNextFile",
                "FindClose",
                "WIN32_FIND_DATA",
                "FINDEX_INFO_LEVELS",
                "FINDEX_SEARCH_OPS",
                "FILE_ATTRIBUTE_DIRECTORY",
                "FILE_ATTRIBUTE_REPARSE_POINT",
                "FIND_FIRST_EX_LARGE_FETCH",
                "ShowWindow",
                "GetCompressedFileSize",
            };

            string[] remaining = removed.Where(declared.Contains).ToArray();
            SelfAssert.That(
                remaining.Length == 0,
                $"除いたはずの宣言が残っています: {string.Join(", ", remaining)}");

            // 反射が空振りしていないことを、残すべき宣言が見えることで確かめる。
            string[] kept = { "GetDiskFreeSpace", "SetProcessWorkingSetSize" };
            string[] missing = kept.Where(name => !declared.Contains(name)).ToArray();
            SelfAssert.That(
                missing.Length == 0,
                $"残すべき宣言が見つかりません: {string.Join(", ", missing)}（宣言されているメンバー: {string.Join(", ", declared.OrderBy(n => n, StringComparer.Ordinal))}）");
        });
    }

    /// <summary>
    /// 最新の要求だけを有効にする描画の取り消しの部品と、それを持つタブのデータの保存の検証項目を登録する
    /// （scan-correctness タスク2.6、要件3.3, 6.1, 6.2, 6.3）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="RenderCancellationProbe"/> を通す。
    /// </summary>
    private static void RegisterRenderCancellationChecks(SelfCheckRunner runner)
    {
        runner.Add("描画の取り消しの部品で2回目の開始が1回目を取り消し、1回目の通知が最新でなくなる（scan-correctness 要件3.3, 6.1, 6.3）", () =>
        {
            SequentialBeginOutcome outcome = RenderCancellationProbe.RunTwoBegins();

            SelfAssert.That(outcome.FirstLatestBeforeSecond, "2回目を始める前の1回目の通知が最新と判定されません。");
            SelfAssert.That(!outcome.FirstCanceledBeforeSecond, "2回目を始める前に1回目の通知が取り消されています。");
            SelfAssert.That(outcome.FirstCanceledAfterSecond, "2回目を始めても1回目の通知が取り消されていません。");
            SelfAssert.That(!outcome.FirstLatestAfterSecond, "2回目を始めた後も1回目の通知が最新と判定されます。");
            SelfAssert.That(!outcome.SecondCanceled, "2回目の通知が取り消されています。");
            SelfAssert.That(outcome.SecondLatest, "2回目の通知が最新と判定されません。");
            SelfAssert.That(!outcome.DefaultTokenLatest, "取り消しの通知の既定値（None）が最新と判定されます。");
        });

        runner.Add("描画の取り消しの部品の別のインスタンスどうしは、開始しても互いの通知を取り消さず、相手の通知を最新と判定しない（scan-correctness 要件6.2）", () =>
        {
            IndependentInstancesOutcome outcome = RenderCancellationProbe.RunTwoInstances();

            SelfAssert.That(!outcome.ACanceled, "別のインスタンスの開始で、1つ目のインスタンスの通知が取り消されました。");
            SelfAssert.That(outcome.ALatestInA, "1つ目のインスタンスの通知が、別のインスタンスの開始の後に最新と判定されません。");
            SelfAssert.That(!outcome.BCanceled, "2つ目のインスタンスの通知が取り消されています。");
            SelfAssert.That(outcome.BLatestInB, "2つ目のインスタンスの通知が最新と判定されません。");
            SelfAssert.That(!outcome.ALatestInB, "2つ目のインスタンスが1つ目の通知を最新と判定しました。");
            SelfAssert.That(!outcome.BLatestInA, "1つ目のインスタンスが2つ目の通知を最新と判定しました。");
        });

        runner.Add("描画の取り消しの部品でまとめて取り消すと最新の通知も取り消され、その後の開始は通常どおり使える（scan-correctness 要件6.1）", () =>
        {
            CancelAllOutcome outcome = RenderCancellationProbe.RunCancelAll();

            SelfAssert.That(outcome.LatestCanceled, "まとめて取り消した後も、最新の通知が取り消されていません。");
            SelfAssert.That(!outcome.LatestStillLatest, "まとめて取り消した後も、最新だった通知が最新と判定されます。");
            SelfAssert.That(!outcome.NextCanceled, "まとめて取り消した後の開始で得た通知が取り消されています。");
            SelfAssert.That(outcome.NextLatest, "まとめて取り消した後の開始で得た通知が最新と判定されません。");
        });

        runner.Add("描画の取り消しの部品を多数のスレッドから一斉に開始しても、取り消されていない通知と最新と判定される通知がちょうど1つになる（scan-correctness 要件6.1, 6.3）", () =>
        {
            const int ThreadCount = 8;
            const int BeginsPerThread = 2000;

            ConcurrentBeginOutcome outcome = RenderCancellationProbe.RunConcurrentBegins(ThreadCount, BeginsPerThread);

            SelfAssert.That(
                outcome.TotalTokens == ThreadCount * BeginsPerThread,
                $"得た通知の数が想定と違います（想定 {ThreadCount * BeginsPerThread}、実際 {outcome.TotalTokens}）。");
            SelfAssert.That(
                outcome.UncanceledCount == 1,
                $"取り消されていない通知が1つではありません（{outcome.UncanceledCount} 個）。");
            SelfAssert.That(
                outcome.LatestCount == 1,
                $"最新と判定される通知が1つではありません（{outcome.LatestCount} 個）。");
        });

        runner.Add("タブのデータを本体の保存と同じ設定で保存して読み戻すと、描画の取り消しの部品は保存されず、読み戻した後も使える（scan-correctness 要件6.1, 6.2）", () =>
        {
            SessionRoundTripOutcome outcome = RenderCancellationProbe.RoundTripSession();

            SelfAssert.That(outcome.PropertyFound, "タブのデータに描画の取り消しの部品（RenderCancellation）が見つかりません。");
            SelfAssert.That(outcome.HasIgnoreMember, "描画の取り消しの部品に保存の対象外の印（IgnoreMember）が付いていません。");
            SelfAssert.That(!outcome.HasKey, "描画の取り消しの部品に保存の番号（Key）が付いています。");
            SelfAssert.That(
                outcome.BytesIndependentOfRenderState,
                "描画の取り消しを進行中にしたデータと未使用のデータで、保存した中身が一致しません（取り消しの状態が保存されています）。");
            SelfAssert.That(!outcome.RestoredGateIsNull, "読み戻したタブのデータの描画の取り消しの部品が null です。");
            SelfAssert.That(outcome.RestoredUsable, "読み戻したタブのデータの描画の取り消しの部品で、開始した通知が最新と判定されません。");
            SelfAssert.That(!outcome.InFlightLatestInRestored, "読み戻した部品が、保存前の部品の通知を最新と判定しました。");
            SelfAssert.That(
                outcome.RestoredPath == @"C:\probe" && outcome.RestoredFilterText == "probe",
                $"保存する欄が読み戻せていません（Path={outcome.RestoredPath}、FilterText={outcome.RestoredFilterText}）。");
        });
    }

    /// <summary>
    /// 設定ファイル（<c>Config.txt</c>）の解析の失敗の記録・保持・一度だけの通知の検証項目を登録する
    /// （scan-correctness タスク2.7、要件5.2, 5.4）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="ConfigLoadProbe"/> を通し、読み込み元は一時フォルダのファイルに限る。
    /// </summary>
    private static void RegisterConfigLoadErrorChecks(SelfCheckRunner runner)
    {
        runner.Add("壊れた設定ファイルを読むと既定の設定が返り、理由が保持・記録され、未通知の失敗が一度だけ取り出せ、成功で記録が消える（scan-correctness 要件5.2, 5.4）", () =>
        {
            ConfigLoadOutcome o = ConfigLoadProbe.Run();

            SelfAssert.That(!o.PendingAtStart, "正しい設定を読んだ直後に未通知の失敗が取り出せました。");

            // 壊れた設定: 既定の設定が返り、理由が保持され、ファイルは上書きされない
            SelfAssert.That(o.BrokenValues == o.DefaultValues, $"壊れた設定を読んだ結果が既定の設定と違います（{o.BrokenValues}、既定 {o.DefaultValues}）。");
            SelfAssert.That(!string.IsNullOrWhiteSpace(o.BrokenError), "壊れた設定を読んだ後も、直近の失敗の理由が保持されていません。");
            SelfAssert.That(o.BrokenFileUnchanged, "壊れた設定ファイルが書き換えられました（利用者が直せるよう残す必要があります）。");
            SelfAssert.That(o.LogMentionsBrokenPath, "壊れた設定ファイルのパスがログに記録されていません。");

            // 一度だけ取り出せる
            SelfAssert.That(o.FirstTake, "壊れた設定を読んだ後に、未通知の失敗が取り出せません。");
            SelfAssert.That(o.FirstTakeError == o.BrokenError, $"取り出した失敗が保持している理由と違います（取り出し {o.FirstTakeError}、保持 {o.BrokenError}）。");
            SelfAssert.That(!o.SecondTake, "同じ失敗が2回取り出せました。");

            // 同じ内容の失敗は再び通知しない
            SelfAssert.That(o.RepeatedError == o.BrokenError, $"同じ壊れ方で理由が変わりました（1回目 {o.BrokenError}、2回目 {o.RepeatedError}）。");
            SelfAssert.That(!o.TakeAfterRepeat, "同じ内容の失敗が、読み直しただけで再び通知の対象になりました。");

            // 内容の違う失敗は通知する
            SelfAssert.That(!string.IsNullOrWhiteSpace(o.OtherError) && o.OtherError != o.BrokenError, $"別の壊れ方の理由が保持されていないか、最初の理由と同じです（{o.OtherError}）。");
            SelfAssert.That(o.TakeAfterOther && o.OtherTakeError == o.OtherError, "内容の違う失敗が取り出せません。");

            // 成功で記録が消え、その後の同じ失敗は再び通知する
            SelfAssert.That(o.ValidValues == new ConfigValues(7, false, true, false, 12), $"正しい設定の値が読めていません（{o.ValidValues}）。");
            SelfAssert.That(o.ErrorAfterValid == null, $"正しい設定を読んだ後も失敗の理由が残っています（{o.ErrorAfterValid}）。");
            SelfAssert.That(!o.TakeAfterValid, "正しい設定を読んだ後に未通知の失敗が取り出せました。");
            SelfAssert.That(o.TakeAfterSuccessThenBroken && o.ReTakeError == o.BrokenError, "成功の後に同じ壊れ方を読んでも、再び通知の対象になりません。");

            // ファイルが無いときは失敗ではなく、そのパスに既定の設定を書き出す
            SelfAssert.That(o.MissingValues == o.DefaultValues, $"ファイルが無いときの結果が既定の設定と違います（{o.MissingValues}）。");
            SelfAssert.That(o.ErrorAfterMissing == null && !o.TakeAfterMissing, $"ファイルが無いことが失敗として扱われました（{o.ErrorAfterMissing}）。");
            SelfAssert.That(o.MissingFileWritten, "ファイルが無いときに、指定したパスへ既定の設定が書き出されていません。");
            SelfAssert.That(o.WrittenDefaultValues == o.DefaultValues, $"書き出した既定の設定を読み戻した値が既定の設定と違います（{o.WrittenDefaultValues}）。");

            // 書き出しの失敗は例外を外に出さず、読み込みの失敗にもせず、ログに記録する
            SelfAssert.That(o.UnwritableExceptionType == null, $"書き出せないパスで例外が外に出ました（{o.UnwritableExceptionType}）。");
            SelfAssert.That(o.ErrorAfterUnwritable == null, $"書き出しの失敗が読み込みの失敗として扱われました（{o.ErrorAfterUnwritable}）。");
            SelfAssert.That(o.LogMentionsUnwritablePath, "設定の書き出しの失敗がログに記録されていません。");

            // 後始末と、既定のパスに触れていないこと
            SelfAssert.That(!o.PendingAtEnd && o.ErrorAtEnd == null, "検証の終わりに失敗の記録が成功の状態へ戻っていません。");
            SelfAssert.That(o.DefaultPathUnchanged, "任意のパスからの読み込みで、既定のパスの設定ファイルが作られたか書き換えられました。");
        });
    }

    /// <summary>
    /// 走査中の途中のツリーを結果の整形が並行に読んでも壊れないことの検証項目を登録する（scan-correctness タスク2.5）。
    /// 本体に触れる呼び出しは Scan 層の <see cref="ScanRunner.RunWithConcurrentFilterReads"/> を通す。
    /// </summary>
    private static void RegisterConcurrentTreeReadChecks(SelfCheckRunner runner)
    {
        runner.Add("大きめの合成の木を走査しながら、進捗で渡る途中のツリーに結果の整形のフィルタの前処理を繰り返しかけても例外が起きない（逐次・並列。scan-correctness 要件3.1, 3.2）", () =>
        {
            WithSyntheticTree(SyntheticTreeFanOut, SyntheticTreeDepth, SyntheticTreeFilesPerFolder, SyntheticTreeWideFolderFiles, root =>
            {
                foreach (bool useParallel in new[] { false, true })
                {
                    string mode = useParallel ? "並列" : "逐次";
                    int readsWhileScanning = 0;

                    // 走査が速すぎて並行の読み取りが一度も起きない回に備え、決めた回数まで走査をやり直す
                    for (int round = 0; round < ConcurrentReadMaxRounds && readsWhileScanning < ConcurrentReadMinReads; round++)
                    {
                        var outcome = new ScanRunner().RunWithConcurrentFilterReads(root, useParallel);

                        SelfAssert.That(
                            outcome.ReadFailures.Count == 0,
                            $"走査中の途中のツリーの読み取りで例外が起きました（{mode}、{outcome.ReadFailures.Count}件）: {string.Join(" / ", outcome.ReadFailures.Select(e => $"{e.GetType().Name}: {e.Message}"))}");
                        SelfAssert.That(outcome.Root != null, $"走査が結果の木を返しませんでした（{mode}）。");

                        // 読み取りが途中のツリーを見ていたことを、走査の後の木と同じノードであることで確かめる
                        SelfAssert.That(
                            outcome.ReadCount == 0 || ReferenceEquals(outcome.ReadRoot, outcome.Root),
                            $"進捗で渡った途中のツリーが走査の結果の木と別のものでした（{mode}）。");

                        readsWhileScanning += outcome.ReadsWhileScanning;
                    }

                    // 空振り（一度も走査と並行に読めなかった）で通らないようにする
                    SelfAssert.That(
                        readsWhileScanning >= ConcurrentReadMinReads,
                        $"走査と並行に読み取れた回数が足りません（{mode}、回数: {readsWhileScanning}, 必要: {ConcurrentReadMinReads}）。合成の木を大きくしてください。");
                }
            });
        });
    }

    /// <summary>合成の木の各フォルダの子フォルダの数</summary>
    private const int SyntheticTreeFanOut = 6;

    /// <summary>合成の木の深さ（起点を深さ0とし、この深さまで子フォルダを作る）</summary>
    private const int SyntheticTreeDepth = 4;

    /// <summary>合成の木の各フォルダに置くファイルの数</summary>
    private const int SyntheticTreeFilesPerFolder = 2;

    /// <summary>
    /// 合成の木の起点の直下に置く「幅の広いフォルダ」のファイルの数。
    /// 走査がこのフォルダの子の一覧に長く書き込み続けるあいだに読み取りが重なるようにし、読み書きの競合が起きる窓を広げる
    /// </summary>
    private const int SyntheticTreeWideFolderFiles = 3000;

    /// <summary>並行の読み取りの検証で走査をやり直す回数の上限</summary>
    private const int ConcurrentReadMaxRounds = 5;

    /// <summary>並行の読み取りの検証で、走査と並行に読み取れたとみなすのに必要な回数</summary>
    private const int ConcurrentReadMinReads = 3;

    /// <summary>
    /// 一時領域に、各フォルダが <paramref name="fanOut"/> 個の子フォルダと <paramref name="filesPerFolder"/> 個のファイルを持つ
    /// 深さ <paramref name="depth"/> の合成の木と、起点の直下に <paramref name="wideFolderFiles"/> 個のファイルを持つ幅の広いフォルダを作り、
    /// <paramref name="action"/> を実行してから後始末する。
    /// フィクスチャ（FixtureSpec）とは別の、走査の量を稼ぐための木である。
    /// </summary>
    private static void WithSyntheticTree(int fanOut, int depth, int filesPerFolder, int wideFolderFiles, Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "gb_syn_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        try
        {
            var current = new List<string> { root };
            Directory.CreateDirectory(root);

            for (int level = 0; level <= depth; level++)
            {
                var next = new List<string>();
                foreach (string folder in current)
                {
                    // ファイルごとに大きさを変え、並べ替えの鍵に差が出るようにする
                    for (int f = 0; f < filesPerFolder; f++)
                    {
                        File.WriteAllBytes(Path.Combine(folder, $"file_{f}.bin"), new byte[(f + 1) * (level + 1)]);
                    }

                    if (level == depth)
                    {
                        continue;
                    }

                    for (int c = 0; c < fanOut; c++)
                    {
                        string child = Path.Combine(folder, $"d{c}");
                        Directory.CreateDirectory(child);
                        next.Add(child);
                    }
                }

                current = next;
            }

            string wideFolder = Path.Combine(root, "wide");
            Directory.CreateDirectory(wideFolder);
            for (int f = 0; f < wideFolderFiles; f++)
            {
                File.WriteAllBytes(Path.Combine(wideFolder, $"w_{f}.bin"), new byte[f % 97]);
            }

            action(root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // ここで失敗した場合は、次の後始末の手段へ進む（既存の項目と同じ最終手段）
            }

            ForceCleanupFixtureResidue(root);
        }
    }

    /// <summary>
    /// CLI（Program）層の検証項目を登録する（タスク5.1）。
    /// design.md の「終了コードの決定ロジックが検証対象に含まれること」という要求を満たすため、
    /// ビルド済みの GoldenBaseline.exe を実際に別プロセスとして起動し、標準出力と終了コードの
    /// 両方を観測する。内部の実行経路を直接呼ぶのではなく、Main のディスパッチも含めて検証する。
    /// selfcheck サブコマンド自体はここでは起動しない（selfcheck の中で selfcheck を起動すると
    /// 無限にプロセスが増殖するため）。selfcheck 自体の回帰確認は、実装後に手動で
    /// `GoldenBaseline.exe selfcheck` を直接実行することで別途行う。
    /// </summary>
    private static void RegisterProgramChecks(SelfCheckRunner runner)
    {
        runner.Add("build-fixture がプロセスとして完走し、終了コード0を返し、実行後に基準フォルダが残留しない（要件3.6）", () =>
        {
            string root = CreateTempFixtureRoot();

            try
            {
                var result = RunGoldenBaselineProcess("build-fixture", "--root", root);

                SelfAssert.That(result.ExitCode == 0, $"build-fixture の終了コードが0ではありません（実際: {result.ExitCode}）。標準エラー: {result.StdErr}");
                SelfAssert.That(result.StdOut.Contains("[フィクスチャの生成]"), $"build-fixture の標準出力に見出しが含まれません: {result.StdOut}");
                SelfAssert.That(result.StdOut.Contains(root), $"build-fixture の標準出力に基準フォルダのパスが含まれません: {result.StdOut}");
                SelfAssert.That(
                    !Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)),
                    $"build-fixture の実行後にフィクスチャが残留しています: {root}");
            }
            finally
            {
                ForceCleanupFixtureResidue(root);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 既知の欠落（原因: LongPath）が4件報告される（.NET Framework では長いパスを列挙できない）。
        // 新: .NET 10 では長いパスも観測されるため、既知の欠落は0件と報告され、LongPath の項目は期待値ファイルに記録される。
        runner.Add("generate がプロセスとして完走し、終了コード0を返し、既知の欠落（長いパス由来）が0件と報告され、期待値ファイルが書き出される（要件2.1, 3.7, 5.2）", () =>
        {
            string root = CreateTempFixtureRoot();
            string outPath = CreateTempCliGoldenFilePath();

            try
            {
                var result = RunGoldenBaselineProcess("generate", "--out", outPath, "--root", root);

                SelfAssert.That(result.ExitCode == 0, $"generate の終了コードが0ではありません（実際: {result.ExitCode}）。標準エラー: {result.StdErr}");
                SelfAssert.That(File.Exists(outPath), $"generate が期待値ファイルを書き出していません: {outPath}");
                SelfAssert.That(result.StdOut.Contains("[期待値の生成]"), $"generate の標準出力に見出しが含まれません: {result.StdOut}");

                // 既知の欠落の識別結果は期待値の生成の報告に含める（tasks.md 5.1）。
                // Standard フィクスチャは境界越えの長いパス項目を4件含むが、.NET 10 では走査から漏れない。
                SelfAssert.That(
                    result.StdOut.Contains("既知の欠落"),
                    $"generate の標準出力に既知の欠落の見出しが含まれません: {result.StdOut}");
                int longPathMentionCount = CountOccurrences(result.StdOut, "原因: LongPath");
                SelfAssert.That(
                    longPathMentionCount == 0,
                    $"既知の欠落として報告された LongPath 由来の件数が想定と異なります（実際: {longPathMentionCount} 件、想定: 0 件）。標準出力: {result.StdOut}");

                var document = new GoldenSerializer().Read(outPath);
                SelfAssert.That(document.Entries.Count > 0, "generate が書き出した期待値ファイルにエントリが1件もありません。");

                // 報告が0件であることと整合して、LongPath の項目が期待値ファイルに記録されていること。
                foreach (var item in FixtureSpec.Standard.Items.Where(i => i.Traits.Contains(FixtureTrait.LongPath)))
                {
                    SelfAssert.That(
                        document.Entries.Any(e => e.RelativePath == item.RelativePath),
                        $"LongPath の項目 '{item.RelativePath}' が generate の期待値ファイルに記録されていません。");
                }

                SelfAssert.That(
                    !Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)),
                    $"generate の実行後にフィクスチャが残留しています: {root}");
            }
            finally
            {
                DeleteIfExists(outPath);
                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("generate --physical-size が、期待値のヘッダに UsePhysicalSize: true と ScanRunner が実測したクラスタサイズそのものを記録し、エントリのサイズが論理サイズではなく物理サイズへ切り上がる（要件6.1, 6.4）", () =>
        {
            // 既存の --physical-size を使う項目は「換算の有無の食い違い＝設定不一致」しか見ておらず、
            // Program が outcome.ClusterSizeInBytes をヘッダへ渡す配線（要件6.4 の記録）を守る項目がなかった。
            // ここでは CLI 経由で実際に期待値ファイルを書き出させ、その本文に実測値が載ることまで照合する。
            string root = CreateTempFixtureRoot();
            string outPath = CreateTempCliGoldenFilePath();
            var entriesBefore = SnapshotTempGbEntries();

            try
            {
                // 検証側が独立にクラスタサイズを実測する。リテラル（4096）で決め打ちすると、
                // 環境によってクラスタサイズが異なる場合に壊れるうえ、「配線が実測値を運んでいるか」も
                // 確かめられない（定数を埋め込む実装と区別がつかない）。
                long measuredClusterSize = MeasureClusterSizeThroughScanRunner();
                SelfAssert.That(
                    measuredClusterSize > 0L,
                    $"検証側でクラスタサイズを実測できませんでした（実際: {measuredClusterSize}）。この検証は実測値との突き合わせを前提とする。");

                var result = RunGoldenBaselineProcess("generate", "--out", outPath, "--root", root, "--physical-size");

                SelfAssert.That(result.ExitCode == 0, $"generate --physical-size の終了コードが0ではありません（実際: {result.ExitCode}）。標準出力: {result.StdOut} 標準エラー: {result.StdErr}");
                SelfAssert.That(File.Exists(outPath), $"generate --physical-size が期待値ファイルを書き出していません: {outPath}");

                // ヘッダは期待値ファイルの本文そのものを行単位で照合する（形式は design.md のとおり
                // 「# キー: 値」。読み取り経路を通すだけでは、書き出しの形が崩れても気付けない）。
                string[] goldenLines = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    .GetString(File.ReadAllBytes(outPath))
                    .Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .ToArray();

                SelfAssert.That(
                    goldenLines.Count(line => line == "# UsePhysicalSize: true") == 1,
                    $"期待値ファイルに '# UsePhysicalSize: true' の行がちょうど1行ありません: {string.Join(" | ", goldenLines.Take(10))}");

                const string ClusterSizeLinePrefix = "# ClusterSizeInBytes: ";
                var clusterSizeLines = goldenLines.Where(line => line.StartsWith(ClusterSizeLinePrefix, StringComparison.Ordinal)).ToList();
                SelfAssert.That(
                    clusterSizeLines.Count == 1,
                    $"期待値ファイルに '{ClusterSizeLinePrefix}' で始まる行がちょうど1行ありません（実際: {clusterSizeLines.Count}行）。");

                string recordedClusterSizeText = clusterSizeLines[0].Substring(ClusterSizeLinePrefix.Length);

                // 配線を 0L に潰す変異を落とすための明示的な照合（「0ではない」）。
                SelfAssert.That(
                    recordedClusterSizeText != "0",
                    $"物理サイズ換算を有効にしたにもかかわらず、記録されたクラスタサイズが0です（実測値がヘッダへ渡っていません）: {clusterSizeLines[0]}");

                // 配線を実測値以外（定数など）へ差し替える変異を落とすための照合。
                SelfAssert.That(
                    recordedClusterSizeText == measuredClusterSize.ToString(CultureInfo.InvariantCulture),
                    $"記録されたクラスタサイズが実測値と一致しません（記録: {recordedClusterSizeText}、実測: {measuredClusterSize}）。");

                var document = new GoldenSerializer().Read(outPath);
                SelfAssert.That(document.Header.UsePhysicalSize, "読み取り経由でも UsePhysicalSize が true ではありません。");
                SelfAssert.That(
                    document.Header.ClusterSizeInBytes == measuredClusterSize,
                    $"読み取り経由のクラスタサイズが実測値と一致しません（記録: {document.Header.ClusterSizeInBytes}、実測: {measuredClusterSize}）。");

                // 記録した値だけでなく、物理サイズ換算が実際にエントリへ効いていることまで確かめる。
                // クラスタ未満の論理サイズを持つ項目は、フィクスチャ定義から導く（リテラルで決め打ちしない）。
                var smallFileItem = FixtureSpec.Standard.Items
                    .FirstOrDefault(i => i.Kind == GoldenEntryKind.File
                        && i.ContentSizeInBytes > 0L
                        && i.ContentSizeInBytes < measuredClusterSize);
                SelfAssert.That(
                    smallFileItem != null,
                    $"前提が崩れています: クラスタサイズ（{measuredClusterSize} バイト）未満の論理サイズを持つファイル項目が FixtureSpec.Standard にありません。");

                var smallEntry = document.Entries
                    .FirstOrDefault(e => e.RelativePath == smallFileItem!.RelativePath && e.Kind == GoldenEntryKind.File);
                SelfAssert.That(smallEntry != null, $"期待値に '{smallFileItem!.RelativePath}' がファイルとして記録されていません。");

                long expectedPhysicalSize = ((smallFileItem!.ContentSizeInBytes + measuredClusterSize - 1L) / measuredClusterSize) * measuredClusterSize;
                SelfAssert.That(
                    smallEntry!.SizeInBytes != smallFileItem.ContentSizeInBytes,
                    $"'{smallFileItem.RelativePath}' のサイズが論理サイズ（{smallFileItem.ContentSizeInBytes} バイト）のままです。物理サイズ換算が効いていません。");
                SelfAssert.That(
                    smallEntry.SizeInBytes == expectedPhysicalSize,
                    $"'{smallFileItem.RelativePath}' の物理サイズが想定（クラスタサイズ {measuredClusterSize} バイトへの切り上げ = {expectedPhysicalSize} バイト）と一致しません（実際: {smallEntry.SizeInBytes}）。");

                SelfAssert.That(
                    !Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)),
                    $"generate --physical-size の実行後にフィクスチャが残留しています: {root}");
                AssertNoNewTempGbEntries("generate --physical-size", entriesBefore, outPath);
            }
            finally
            {
                DeleteIfExists(outPath);
                ForceCleanupFixtureResidue(root);
            }
        });

        runner.Add("compare が一致判定で終了コード0を返す（要件4.1, 4.6, 4.7）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // 同一の標準フィクスチャは何度生成しても同一の構造になる（要件3.5）ため、
                // 生成し直した走査結果は元の期待値と一致するはずである。
                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", cmpRoot);

                SelfAssert.That(cmpResult.ExitCode == 0, $"compare（一致想定）の終了コードが0ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("判定: 一致"), $"compare の標準出力に一致の判定が含まれません: {cmpResult.StdOut}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("compare が差分ありで終了コード1を返し、差分明細が期待値と実際を正しい向きで同一行に示す（要件4.2, 4.7）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();
            string tamperedPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // 生成された期待値データの1件を意図的に改ざんし、確実にサイズ不一致を発生させる。
                // 差分の幅は、期待値と実際を取り違えたときに必ず値が食い違うよう、他の項目と紛れない大きさにする
                // （+1 のままでは向きの取り違えが「偶然どちらでも通る」形にはならないものの、
                // 出力中の別の数値と紛れて誤って一致する余地が残る）。
                var serializer = new GoldenSerializer();
                var original = serializer.Read(goldenPath);
                var targetEntry = original.Entries.First(e => e.Kind == GoldenEntryKind.File);
                long realSize = targetEntry.SizeInBytes;
                long tamperedSize = realSize + 54321L;
                var tamperedEntries = original.Entries
                    .Select(e => ReferenceEquals(e, targetEntry)
                        ? new GoldenEntry(e.RelativePath, e.Kind, tamperedSize)
                        : e)
                    .ToList();
                var tamperedDocument = new GoldenDocument(original.Header, tamperedEntries);
                serializer.Write(tamperedDocument, tamperedPath);

                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", tamperedPath, "--root", cmpRoot);

                SelfAssert.That(cmpResult.ExitCode == 1, $"compare（差分あり想定）の終了コードが1ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("判定: 差分あり"), $"compare の標準出力に差分ありの判定が含まれません: {cmpResult.StdOut}");
                SelfAssert.That(cmpResult.StdOut.Contains(targetEntry.RelativePath), $"compare の標準出力に改ざんした相対パスが含まれません: {cmpResult.StdOut}");
                SelfAssert.That(cmpResult.StdOut.Contains("SizeMismatch"), $"compare の標準出力にサイズ不一致の種別が含まれません: {cmpResult.StdOut}");

                // 要件4.2（期待されたサイズと実際のサイズの双方を報告する）の「向き」を守る。
                // 件数と種別だけを見ていると、期待値と実際を入れ替える変異も、片方を落とす変異も素通りする。
                // 期待値は改ざん後の値、実際は走査で得られる本来の値であり、両者は入れ替えられない。
                string expectedValueToken = "期待値=" + tamperedSize.ToString(CultureInfo.InvariantCulture);
                string actualValueToken = "実際=" + realSize.ToString(CultureInfo.InvariantCulture);

                // トークンが別々の行に偶然現れて通過する形骸化を防ぐため、種別・相対パス・両方の値が
                // すべて同一行に現れることを行単位で照合する。
                string[] stdOutLines = cmpResult.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool hasCorrectlyOrientedDetail = stdOutLines.Any(line =>
                    line.Contains("[SizeMismatch]") &&
                    line.Contains(targetEntry.RelativePath) &&
                    line.Contains(expectedValueToken) &&
                    line.Contains(actualValueToken));
                SelfAssert.That(
                    hasCorrectlyOrientedDetail,
                    $"改ざんした相対パス（{targetEntry.RelativePath}）・改ざん後の期待値（{expectedValueToken}）・本来の実際値（{actualValueToken}）を" +
                    $"すべて含む [SizeMismatch] の明細行が見つかりません（期待値と実際が入れ替わっているか、片方が欠けています）: {cmpResult.StdOut}");

                // 向きが逆の明細行が出力に現れていないこと（正しい行を出したうえで逆向きの行も併記する実装を排除する）。
                string reversedExpectedToken = "期待値=" + realSize.ToString(CultureInfo.InvariantCulture);
                string reversedActualToken = "実際=" + tamperedSize.ToString(CultureInfo.InvariantCulture);
                bool hasReversedDetail = stdOutLines.Any(line =>
                    line.Contains("[SizeMismatch]") &&
                    line.Contains(targetEntry.RelativePath) &&
                    line.Contains(reversedExpectedToken) &&
                    line.Contains(reversedActualToken));
                SelfAssert.That(
                    !hasReversedDetail,
                    $"期待値と実際を取り違えた明細行（{reversedExpectedToken} / {reversedActualToken}）が compare の標準出力に含まれます: {cmpResult.StdOut}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                DeleteIfExists(tamperedPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("compare が設定不一致で終了コード2を返し、エントリの突き合わせを行わない（要件6.2, 6.3, 4.7）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                // --physical-size を付けずに生成する（UsePhysicalSize=false の期待値）。
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // --physical-size を付けて比較する（UsePhysicalSize=true）ことで、走査条件を意図的に食い違わせる。
                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", cmpRoot, "--physical-size");

                SelfAssert.That(cmpResult.ExitCode == 2, $"compare（設定不一致想定）の終了コードが2ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("設定不一致"), $"compare の標準出力に設定不一致の判定が含まれません: {cmpResult.StdOut}");

                // 判定値だけでなく、実際にエントリの突き合わせが行われていないことまで確認する
                // （tasks.md Implementation Notes: タスク2.2のレビュー教訓「短絡することが仕様の検証では
                // Entries が空であることまで照合する必要がある」を、CLI 層の出力でも同じ観点で確認する）。
                SelfAssert.That(!cmpResult.StdOut.Contains("判定: 差分あり"), $"設定不一致にもかかわらず差分ありの判定が出力されています: {cmpResult.StdOut}");
                SelfAssert.That(!cmpResult.StdOut.Contains("SizeMismatch"), $"設定不一致にもかかわらずサイズ不一致の個別差分が出力されています: {cmpResult.StdOut}");
                SelfAssert.That(!cmpResult.StdOut.Contains("Missing"), $"設定不一致にもかかわらず欠落の個別差分が出力されています: {cmpResult.StdOut}");
                SelfAssert.That(!cmpResult.StdOut.Contains("Unexpected"), $"設定不一致にもかかわらず新規の個別差分が出力されています: {cmpResult.StdOut}");
                SelfAssert.That(!cmpResult.StdOut.Contains("KindMismatch"), $"設定不一致にもかかわらず種別不一致の個別差分が出力されています: {cmpResult.StdOut}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("update が設定不一致のとき終了コード2を返し、期待値ファイルへ書き込まない（要件5.3, 5.4, 6.2, 6.3）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string updRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                // --physical-size を付けずに生成する（UsePhysicalSize=false の期待値）。
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // 書き込み前の期待値ファイルのバイト列を保持しておき、update 実行後と比較する。
                byte[] before = File.ReadAllBytes(goldenPath);

                // --physical-size を付けて更新する（UsePhysicalSize=true）ことで、走査条件を意図的に食い違わせる。
                var updResult = RunGoldenBaselineProcess("update", "--golden", goldenPath, "--root", updRoot, "--physical-size");

                SelfAssert.That(updResult.ExitCode == 2, $"update（設定不一致想定）の終了コードが2ではありません（実際: {updResult.ExitCode}）。標準出力: {updResult.StdOut} 標準エラー: {updResult.StdErr}");
                SelfAssert.That(updResult.StdOut.Contains("更新しませんでした"), $"update の標準出力に更新を見送った旨の文言が含まれません: {updResult.StdOut}");

                // 終了コードや文言だけでなく、実際に期待値ファイルが書き換わっていないことまで
                // バイト列の完全一致で確認する（design.md が名指しで警告する退行経路そのものの回帰保護）。
                byte[] after = File.ReadAllBytes(goldenPath);
                SelfAssert.That(before.SequenceEqual(after), "設定不一致にもかかわらず update の実行後に期待値ファイルの内容が変化しています。");

                SelfAssert.That(
                    !Directory.Exists(updRoot) && !Directory.Exists(LongPath.Extend(updRoot)),
                    $"update（設定不一致）の実行後にフィクスチャが残留しています: {updRoot}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(updRoot);
            }
        });

        runner.Add("update が更新前の差分を提示したうえで期待値ファイルを更新し、後始末が行われる（要件5.3, 5.4）", () =>
        {
            string genRoot = CreateTempFixtureRoot();
            string updRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                byte[] before = File.ReadAllBytes(goldenPath);

                var updResult = RunGoldenBaselineProcess("update", "--golden", goldenPath, "--root", updRoot);

                SelfAssert.That(updResult.ExitCode == 0, $"update（一致想定）の終了コードが0ではありません（実際: {updResult.ExitCode}）。標準出力: {updResult.StdOut} 標準エラー: {updResult.StdErr}");
                SelfAssert.That(updResult.StdOut.Contains("更新前の差分"), $"update の標準出力に更新前の差分の見出しが含まれません: {updResult.StdOut}");
                SelfAssert.That(updResult.StdOut.Contains("判定: 一致"), $"update の標準出力に一致の判定が含まれません: {updResult.StdOut}");
                SelfAssert.That(updResult.StdOut.Contains("更新しました"), $"update の標準出力に更新完了の報告が含まれません: {updResult.StdOut}");

                byte[] after = File.ReadAllBytes(goldenPath);
                SelfAssert.That(!before.SequenceEqual(after), "update を実行しても期待値ファイルの内容（GeneratedAt を含む）が変化していません。");

                SelfAssert.That(
                    !Directory.Exists(updRoot) && !Directory.Exists(LongPath.Extend(updRoot)),
                    $"update の実行後にフィクスチャが残留しています: {updRoot}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(updRoot);
            }
        });

        runner.Add("update が差分ありのとき、更新前に改ざんした内容の差分明細を提示したうえで期待値ファイルを書き換え、書き換え後は compare で一致する（要件5.3, 5.4）", () =>
        {
            // タスク5.2で未検証だった3点（明細・順序・再比較）を、このタスクで埋める（既存項目は「一致」経路のみを通り、
            // 差分ありの経路で PrintDiffReport の Different 分岐を一度も通っていなかった）。
            string genRoot = CreateTempFixtureRoot();
            string updRoot = CreateTempFixtureRoot();
            string cmpRoot = CreateTempFixtureRoot();
            string goldenPath = CreateTempCliGoldenFilePath();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", genRoot);
                SelfAssert.That(genResult.ExitCode == 0, $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // 生成された期待値データの1件（ファイル）を意図的に改ざんし、update が差分ありの経路を通るようにする。
                // 改ざんした相対パスと値は変数に保持し、以降の照合はすべてこの変数を用いて行う
                // （tasks.md Implementation Notes: タスク2.1のレビュー教訓「検証データが偶然に依存して形骸化」を踏まえ、
                // ハードコードした文字列リテラルでは照合しない）。
                var serializer = new GoldenSerializer();
                var generated = serializer.Read(goldenPath);
                var targetEntry = generated.Entries.First(e => e.Kind == GoldenEntryKind.File);
                long tamperedSize = targetEntry.SizeInBytes + 12345L;
                long realSize = targetEntry.SizeInBytes;
                string relativePath = targetEntry.RelativePath;

                var tamperedEntries = generated.Entries
                    .Select(e => ReferenceEquals(e, targetEntry)
                        ? new GoldenEntry(e.RelativePath, e.Kind, tamperedSize)
                        : e)
                    .ToList();
                var tamperedDocument = new GoldenDocument(generated.Header, tamperedEntries);

                // update は --golden で指定したファイル「そのもの」を読み込んで更新するため、
                // 改ざんは別ファイルではなく goldenPath 自体へ上書きする。
                serializer.Write(tamperedDocument, goldenPath);

                var updResult = RunGoldenBaselineProcess("update", "--golden", goldenPath, "--root", updRoot);

                // 穴1（順序の前提となる終了コードと判定文言）: 差分ありの update は終了コード1を返す
                // （design.md Program「一致は0、差分ありは1」。タスク5.1のレビューで承認済みの挙動として、
                // update は書き換えに成功しても更新前の判定結果を終了コードで示す。この挙動は修正の対象ではない）。
                SelfAssert.That(updResult.ExitCode == 1, $"update（差分あり想定）の終了コードが1ではありません（実際: {updResult.ExitCode}）。標準出力: {updResult.StdOut} 標準エラー: {updResult.StdErr}");
                SelfAssert.That(updResult.StdOut.Contains("判定: 差分あり"), $"update の標準出力に差分ありの判定が含まれません: {updResult.StdOut}");

                // 穴1（明細）: 改ざんした相対パスを含む [SizeMismatch] の明細行が、
                // 改ざん後の値（期待値）と本来の値（実際）の双方とともに出力される。
                string expectedValueToken = "期待値=" + tamperedSize.ToString(CultureInfo.InvariantCulture);
                string actualValueToken = "実際=" + realSize.ToString(CultureInfo.InvariantCulture);

                // トークンが別々の場所に偶然現れて通過する形骸化を防ぐため、
                // 種別・相対パス・両方の値がすべて同一行に現れることを行単位で照合する。
                string[] stdOutLines = updResult.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int diffDetailLineIndex = Array.FindIndex(stdOutLines, line =>
                    line.Contains("[SizeMismatch]") &&
                    line.Contains(relativePath) &&
                    line.Contains(expectedValueToken) &&
                    line.Contains(actualValueToken));
                SelfAssert.That(
                    diffDetailLineIndex >= 0,
                    $"改ざんした相対パス（{relativePath}）・改ざん後の期待値（{expectedValueToken}）・本来の実際値（{actualValueToken}）を" +
                    $"すべて含む [SizeMismatch] の明細行が見つかりません: {updResult.StdOut}");

                // 穴2（順序）: 差分明細の行が「更新しました」の行より前に現れる
                // （design.md Program「update は期待値を再生成する前に、現行の期待値との差分を提示する」）。
                // Contains だけの照合では、差分表示を書き込みの後ろへ移しても通過してしまうため、
                // 標準出力内の出現位置（行番号）そのものを比較する。
                int updatedLineIndex = Array.FindIndex(stdOutLines, line => line.Contains("更新しました"));
                SelfAssert.That(updatedLineIndex >= 0, $"update の標準出力に更新完了の報告が含まれません: {updResult.StdOut}");
                SelfAssert.That(
                    diffDetailLineIndex < updatedLineIndex,
                    $"差分明細の行（{diffDetailLineIndex}行目）が更新完了の報告（{updatedLineIndex}行目）より後に現れています。" +
                    $"更新前に差分を提示するという design.md の責務に反します: {updResult.StdOut}");

                // 穴3（再比較による書き換え内容の証明）: 「バイト列が変化した」だけでは GeneratedAt の変化のみでも
                // 成立してしまう弱い照合になるため、書き換え後の期待値ファイルに対して compare を実行し、
                // 新しい走査結果が正しく書き込まれたことを終了コード0（一致）で証明する。
                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath, "--root", cmpRoot);
                SelfAssert.That(cmpResult.ExitCode == 0, $"update 後の期待値ファイルに対する compare の終了コードが0ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("判定: 一致"), $"update 後の期待値ファイルに対する compare の標準出力に一致の判定が含まれません: {cmpResult.StdOut}");

                SelfAssert.That(
                    !Directory.Exists(updRoot) && !Directory.Exists(LongPath.Extend(updRoot)),
                    $"update（差分あり）の実行後にフィクスチャが残留しています: {updRoot}");
                SelfAssert.That(
                    !Directory.Exists(cmpRoot) && !Directory.Exists(LongPath.Extend(cmpRoot)),
                    $"update 後の再比較（compare）の実行後にフィクスチャが残留しています: {cmpRoot}");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(genRoot);
                ForceCleanupFixtureResidue(updRoot);
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("不正な入力（存在しない期待値ファイル）で compare が終了コード2を返す（Error Handling: 入力の誤り）", () =>
        {
            string cmpRoot = CreateTempFixtureRoot();
            string missingGoldenPath = Path.Combine(Path.GetTempPath(), "gb_cli_" + Guid.NewGuid().ToString("N") + "_missing.golden.txt");

            try
            {
                var result = RunGoldenBaselineProcess("compare", "--golden", missingGoldenPath, "--root", cmpRoot);

                SelfAssert.That(result.ExitCode == 2, $"存在しない期待値ファイルを指定しても終了コードが2になりません（実際: {result.ExitCode}）。標準出力: {result.StdOut} 標準エラー: {result.StdErr}");
                SelfAssert.That(result.StdErr.Contains("エラー"), $"標準エラーにエラー内容が出力されていません: {result.StdErr}");

                // 期待値ファイルの読み込みはフィクスチャの組み立てより前に行うため、
                // このシナリオではフィクスチャが一切作られていないはずである。
                SelfAssert.That(!Directory.Exists(cmpRoot), $"存在しない期待値ファイルの指定にもかかわらずフィクスチャが作られています: {cmpRoot}");
            }
            finally
            {
                ForceCleanupFixtureResidue(cmpRoot);
            }
        });

        runner.Add("不正な入力（必須オプションの未指定）で generate / compare / update が終了コード2を返し、原因が必須オプション不足であると分かるメッセージを示す（Error Handling: 入力の誤り）", () =>
        {
            // 終了コードだけでなく、エラーメッセージが「オプション未指定」であることを明示しているかまで確認する。
            // ここを終了コードだけで確認すると、必須オプションの検証（RequireOption）自体を外しても、
            // 空文字のパスが別の層（出力先ディレクトリの検証やファイル読み込み）でたまたま拒否されて
            // 終了コード2になってしまい、検証が意味をなさなくなる（実際に変異テストで確認した：
            // RequireOption の検証を外すと、終了コードは2のままだが、メッセージが「パスの形式が
            // 無効です」のような無関係な内容に変わる）。

            var generateResult = RunGoldenBaselineProcess("generate");
            SelfAssert.That(generateResult.ExitCode == 2, $"--out を指定しない generate の終了コードが2になりません（実際: {generateResult.ExitCode}）。");
            SelfAssert.That(
                generateResult.StdErr.Contains("オプション '--out' の指定が必要です"),
                $"--out を指定しない generate のエラーメッセージが、必須オプション不足を明示していません: {generateResult.StdErr}");

            var compareResult = RunGoldenBaselineProcess("compare");
            SelfAssert.That(compareResult.ExitCode == 2, $"--golden を指定しない compare の終了コードが2になりません（実際: {compareResult.ExitCode}）。");
            SelfAssert.That(
                compareResult.StdErr.Contains("オプション '--golden' の指定が必要です"),
                $"--golden を指定しない compare のエラーメッセージが、必須オプション不足を明示していません: {compareResult.StdErr}");

            var updateResult = RunGoldenBaselineProcess("update");
            SelfAssert.That(updateResult.ExitCode == 2, $"--golden を指定しない update の終了コードが2になりません（実際: {updateResult.ExitCode}）。");
            SelfAssert.That(
                updateResult.StdErr.Contains("オプション '--golden' の指定が必要です"),
                $"--golden を指定しない update のエラーメッセージが、必須オプション不足を明示していません: {updateResult.StdErr}");
        });

        runner.Add("不正な基準パス（既存のファイルと衝突する）を指定すると generate が終了コード2を返し、期待値ファイルを書き出さない", () =>
        {
            string conflictingRoot = Path.Combine(Path.GetTempPath(), "gb_fix_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string outPath = CreateTempCliGoldenFilePath();

            try
            {
                // 基準フォルダが存在するべき場所に、あらかじめファイルを置いて衝突させる。
                File.WriteAllText(conflictingRoot, "this is a file, not a directory");

                var result = RunGoldenBaselineProcess("generate", "--out", outPath, "--root", conflictingRoot);

                SelfAssert.That(result.ExitCode == 2, $"基準パスがファイルと衝突しているのに generate の終了コードが2になりません（実際: {result.ExitCode}）。標準出力: {result.StdOut} 標準エラー: {result.StdErr}");
                SelfAssert.That(!File.Exists(outPath), $"基準パスの衝突で失敗したにもかかわらず期待値ファイルが書き出されています: {outPath}");
                SelfAssert.That(
                    result.StdOut.Contains("未生成の項目"),
                    $"基準フォルダの生成に失敗した際の未生成項目の報告（要件3.6）が標準出力に含まれません: {result.StdOut}");
            }
            finally
            {
                DeleteIfExists(conflictingRoot);
                DeleteIfExists(outPath);
            }
        });

        runner.Add("generate を2回実行すると、'# GeneratedAt:' の行を除いて出力がバイト単位で完全に一致する（要件2.3、2026-09-15決定: 反復生成の同一性から生成日時を除く）", () =>
        {
            string root1 = CreateTempFixtureRoot();
            string root2 = CreateTempFixtureRoot();
            string outPath1 = CreateTempCliGoldenFilePath();
            string outPath2 = CreateTempCliGoldenFilePath();

            try
            {
                var result1 = RunGoldenBaselineProcess("generate", "--out", outPath1, "--root", root1);
                SelfAssert.That(result1.ExitCode == 0, $"1回目の generate が失敗しました（終了コード: {result1.ExitCode}）。標準エラー: {result1.StdErr}");

                var result2 = RunGoldenBaselineProcess("generate", "--out", outPath2, "--root", root2);
                SelfAssert.That(result2.ExitCode == 0, $"2回目の generate が失敗しました（終了コード: {result2.ExitCode}）。標準エラー: {result2.StdErr}");

                byte[] bytes1 = File.ReadAllBytes(outPath1);
                byte[] bytes2 = File.ReadAllBytes(outPath2);

                var excluded1 = ExcludeGeneratedAtLine(bytes1);
                var excluded2 = ExcludeGeneratedAtLine(bytes2);

                // 形骸化を防ぐ照合その1: 除外対象の行が各ファイルにちょうど1行だけ存在すること。
                // 0行なら生成日時の記録自体が失われている可能性があり、2行以上ならヘッダ形式が壊れている。
                SelfAssert.That(
                    excluded1.GeneratedAtLineCount == 1,
                    $"1回目の出力に '# GeneratedAt:' で始まる行がちょうど1行ではありません（実際: {excluded1.GeneratedAtLineCount}行）。");
                SelfAssert.That(
                    excluded2.GeneratedAtLineCount == 1,
                    $"2回目の出力に '# GeneratedAt:' で始まる行がちょうど1行ではありません（実際: {excluded2.GeneratedAtLineCount}行）。");

                // 形骸化を防ぐ照合その2: GeneratedAt以外のヘッダ6行（FormatVersion/BaseFolderLabel/
                // BaseFolderPathLength/UsePhysicalSize/ClusterSizeInBytes/FixtureComplete）が除外後も残っていること。
                // 除外対象を「行頭が # の行すべて」へ広げる変異では、この件数が0になり検出できる。
                SelfAssert.That(
                    excluded1.OtherHeaderLineCount == 6,
                    $"1回目の出力の除外後ヘッダ行数が想定（6行）と異なります（実際: {excluded1.OtherHeaderLineCount}行）。除外範囲が広すぎる可能性があります。");
                SelfAssert.That(
                    excluded2.OtherHeaderLineCount == 6,
                    $"2回目の出力の除外後ヘッダ行数が想定（6行）と異なります（実際: {excluded2.OtherHeaderLineCount}行）。除外範囲が広すぎる可能性があります。");

                // 形骸化を防ぐ照合その3: 除外後にエントリ行が1行以上残っていること。
                SelfAssert.That(excluded1.EntryLineCount >= 1, "1回目の出力の除外後にエントリ行が1件も残っていません。");
                SelfAssert.That(excluded2.EntryLineCount >= 1, "2回目の出力の除外後にエントリ行が1件も残っていません。");

                // 本題: 除外後の内容がバイト単位で完全に一致すること。
                SelfAssert.That(
                    excluded1.FilteredBytes.SequenceEqual(excluded2.FilteredBytes),
                    "generate を2回実行した際、'# GeneratedAt:' 行を除いた内容がバイト単位で一致しません。");

                // 対比: 除外前の生バイト列は生成日時が異なるため一致しないはず。この検証自体が意味を持つこと
                // （GeneratedAt が実際に記録され続けており、固定値に潰れていないこと）を確認する。
                SelfAssert.That(
                    !bytes1.SequenceEqual(bytes2),
                    "生成日時を除外する前のバイト列が2回の生成で一致しました（GeneratedAt が記録されていないか、常に固定値になっている可能性があります）。");
            }
            finally
            {
                DeleteIfExists(outPath1);
                DeleteIfExists(outPath2);
                ForceCleanupFixtureResidue(root1);
                ForceCleanupFixtureResidue(root2);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: LongPath トレイトを持つ項目が過不足なく「相対パス（原因: LongPath）」の形で列挙され、件数行がその件数になる。
        // 新: .NET 10 では LongPath の項目も観測されるため、明細行は1件も出ず、件数行は「0 件」になる。
        runner.Add("generate の標準出力で、FixtureSpec.Standard の LongPath トレイトを持つ項目が観測されるため「相対パス（原因: LongPath）」の明細が1件も列挙されず、既知の欠落の件数行が0件になる（要件5.2、3.7）", () =>
        {
            string root = CreateTempFixtureRoot();
            string outPath = CreateTempCliGoldenFilePath();

            try
            {
                var result = RunGoldenBaselineProcess("generate", "--out", outPath, "--root", root);
                SelfAssert.That(result.ExitCode == 0, $"generate が失敗しました（終了コード: {result.ExitCode}）。標準エラー: {result.StdErr}");

                // ハードコードせず、FixtureSpec.Standard から LongPath トレイトを持つ項目の相対パスを導出する
                // （tasks.md Implementation Notes: タスク2.1・4.3の教訓「検証データが偶然に依存して形骸化」）。
                var expectedRelativePaths = FixtureSpec.Standard.Items
                    .Where(item => item.Traits.Contains(FixtureTrait.LongPath))
                    .Select(item => item.RelativePath)
                    .ToList();

                SelfAssert.That(expectedRelativePaths.Count > 0, "検証対象となる LongPath トレイトの項目が FixtureSpec.Standard に見つかりません。");

                foreach (var relativePath in expectedRelativePaths)
                {
                    string unexpectedLine = $"  - {relativePath}（原因: LongPath）";
                    SelfAssert.That(
                        !result.StdOut.Contains(unexpectedLine),
                        $"generate の標準出力に、観測されているはずの項目の既知の欠落の明細行があります: '{unexpectedLine}'");
                }

                const string expectedCountLine = "既知の欠落（境界条件に由来）: 0 件";
                SelfAssert.That(
                    result.StdOut.Contains(expectedCountLine),
                    $"generate の標準出力の件数行が想定と異なります（想定: '{expectedCountLine}'）。標準出力: {result.StdOut}");

                // 「（原因: LongPath）」が1件も出ないこと。
                int actualLongPathMentionCount = CountOccurrences(result.StdOut, "（原因: LongPath）");
                SelfAssert.That(
                    actualLongPathMentionCount == 0,
                    $"「（原因: LongPath）」の出現回数（{actualLongPathMentionCount}）が0ではありません。");
            }
            finally
            {
                DeleteIfExists(outPath);
                ForceCleanupFixtureResidue(root);
            }
        });
    }

    /// <summary>
    /// 生成を妨げる対象として用いるフォルダの相対パス。既存の FixtureBuilder の部分失敗の検証と同じ手法
    /// （同名のファイルを先に置くと Directory.CreateDirectory が失敗する）を CLI の結合検証でも用いる。
    /// </summary>
    private const string BlockedFixtureFolderRelativePath = "normal";

    /// <summary>
    /// 不完全なフィクスチャの記録と、読み取り拒否を含むフィクスチャの生成から後始末までの完走を、
    /// CLI（generate）を実際に起動して確認する検証項目を登録する（タスク6.4）。
    /// </summary>
    private static void RegisterIncompleteFixtureRecordChecks(SelfCheckRunner runner)
    {
        runner.Add("生成を妨げたフィクスチャで基準フォルダを変えて generate を2回実行すると、どちらも終了コード0で、期待値に FixtureComplete: false と「相対パス: 例外の型名」の FixtureOmission が妨げた項目ぶん記録され、生成日時の行を除いてバイト単位で一致し、絶対パス・ユーザー名・例外メッセージを含まず、詳細な理由は標準出力に出て、実行後に残留物がない（要件2.3, 3.6, 3.7、タスク6.4）", () =>
        {
            string root1 = CreateTempFixtureRoot();
            string root2 = CreateTempFixtureRoot();
            string outPath1 = CreateTempCliGoldenFilePath();
            string outPath2 = CreateTempCliGoldenFilePath();
            var gbEntriesBefore = SnapshotTempGbEntries();

            try
            {
                // 長いパスの欠落は基準フォルダを含む絶対パスの長さで変わる（tasks.md Implementation Notes）。
                // 2回の生成の違いを基準フォルダの「名前」だけに絞るため、長さは揃え、名前は変える。
                SelfAssert.That(root1.Length == root2.Length, $"2回の基準フォルダの長さが揃っていません（{root1.Length}文字 / {root2.Length}文字）。");
                SelfAssert.That(!string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase), $"2回の基準フォルダが同じ名前です: {root1}");

                var observed1 = BlockFixtureFolderAndObserveFailures(root1);
                var result1 = RunGoldenBaselineProcess("generate", "--out", outPath1, "--root", root1);
                var observed2 = BlockFixtureFolderAndObserveFailures(root2);
                var result2 = RunGoldenBaselineProcess("generate", "--out", outPath2, "--root", root2);

                // (a) 一部が生成できなくても生成は継続し、完走する（要件3.6）。
                SelfAssert.That(result1.ExitCode == 0, $"1回目の generate の終了コードが0ではありません（実際: {result1.ExitCode}）。標準出力: {result1.StdOut} 標準エラー: {result1.StdErr}");
                SelfAssert.That(result2.ExitCode == 0, $"2回目の generate の終了コードが0ではありません（実際: {result2.ExitCode}）。標準出力: {result2.StdOut} 標準エラー: {result2.StdErr}");
                SelfAssert.That(File.Exists(outPath1) && File.Exists(outPath2), "generate が期待値ファイルを書き出していません。");

                byte[] bytes1 = File.ReadAllBytes(outPath1);
                byte[] bytes2 = File.ReadAllBytes(outPath2);
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                string text1 = utf8.GetString(bytes1);
                string text2 = utf8.GetString(bytes2);

                // (b) 不完全である旨と、妨げた項目ぶんの「相対パス: 例外の型名」が記録される（要件3.7）。
                AssertIncompleteFixtureHeader(text1, observed1, "1回目");
                AssertIncompleteFixtureHeader(text2, observed2, "2回目");

                // (c) 基準フォルダを変えても、生成日時の行を除いてバイト単位で一致する（要件2.3）。
                var excluded1 = ExcludeGeneratedAtLine(bytes1);
                var excluded2 = ExcludeGeneratedAtLine(bytes2);
                SelfAssert.That(excluded1.GeneratedAtLineCount == 1 && excluded2.GeneratedAtLineCount == 1, "'# GeneratedAt:' の行がそれぞれちょうど1行ではありません。");
                // GeneratedAt 以外のヘッダ6行（FormatVersion / BaseFolderLabel / BaseFolderPathLength /
                // UsePhysicalSize / ClusterSizeInBytes / FixtureComplete）＋ 未生成項目の行数。
                int expectedOtherHeaderLineCount = 6 + observed1.Count;
                SelfAssert.That(
                    excluded1.OtherHeaderLineCount == expectedOtherHeaderLineCount && excluded2.OtherHeaderLineCount == expectedOtherHeaderLineCount,
                    $"生成日時の行を除いたヘッダ行数が想定（{expectedOtherHeaderLineCount}行）と異なります（実際: {excluded1.OtherHeaderLineCount}行 / {excluded2.OtherHeaderLineCount}行）。");
                // エントリ部が空に退行しても「一致」してしまわないよう、エントリ行が残っていることも見る。
                SelfAssert.That(
                    excluded1.EntryLineCount >= 1 && excluded2.EntryLineCount >= 1,
                    $"生成日時の行を除いた出力にエントリ行が残っていません（実際: {excluded1.EntryLineCount}行 / {excluded2.EntryLineCount}行）。");
                SelfAssert.That(
                    excluded1.FilteredBytes.SequenceEqual(excluded2.FilteredBytes),
                    $"基準フォルダを変えて生成した2つの期待値が、生成日時の行を除いてバイト単位で一致しません。\n1回目:\n{text1}\n2回目:\n{text2}");

                // (d) 期待値に環境に依存する情報（絶対パス・ユーザー名・例外メッセージ）が含まれない。
                string userName = Environment.UserName;
                SelfAssert.That(!string.IsNullOrWhiteSpace(userName), "照合に用いるユーザー名を取得できませんでした。");
                var forbiddenTokens = new List<(string What, string Token)>
                {
                    ("1回目の基準フォルダの絶対パス", root1),
                    ("2回目の基準フォルダの絶対パス", root2),
                    ("1回目の基準フォルダ名", Path.GetFileName(root1)),
                    ("2回目の基準フォルダ名", Path.GetFileName(root2)),
                    ("ユーザー名", userName),
                };
                forbiddenTokens.AddRange(observed1.Concat(observed2).Select(o => ($"'{o.RelativePath}' の例外メッセージ", o.Message)));

                foreach (var (label, text) in new[] { ("1回目", text1), ("2回目", text2) })
                {
                    foreach (var (what, token) in forbiddenTokens)
                    {
                        SelfAssert.That(
                            text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0,
                            $"{label}の期待値に{what}が含まれています: '{token}'");
                    }
                }

                // (e) 詳細な理由（例外メッセージ）は標準出力への報告には出る（要件3.6）。
                foreach (var (label, stdOut, observed) in new[] { ("1回目", result1.StdOut, observed1), ("2回目", result2.StdOut, observed2) })
                {
                    foreach (var o in observed)
                    {
                        SelfAssert.That(
                            stdOut.Contains(o.RelativePath) && stdOut.Contains(o.Message),
                            $"{label}の標準出力に '{o.RelativePath}' の詳細な理由（例外メッセージ '{o.Message}'）が報告されていません。標準出力: {stdOut}");
                    }
                }

                // (f) 実行後に基準フォルダも gb_* の残留物もない。
                foreach (var root in new[] { root1, root2 })
                {
                    SelfAssert.That(!Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)), $"generate の実行後に基準フォルダが残留しています: {root}");
                }

                AssertNoNewTempGbEntries("生成を妨げたフィクスチャでの generate", gbEntriesBefore, outPath1, outPath2);
            }
            finally
            {
                DeleteIfExists(outPath1);
                DeleteIfExists(outPath2);
                ForceCleanupFixtureResidue(root1);
                ForceCleanupFixtureResidue(root2);
            }
        });

        runner.Add("読み取り拒否フォルダを含むフィクスチャで generate が終了コード0で完走し、拒否フォルダがスキップされた対象として標準出力に報告され、期待値は完全で拒否フォルダ自身も記録され、実行後に基準フォルダも gb_* の残留物もない（要件2.4, 3.4、タスク6.4）", () =>
        {
            string root = CreateTempFixtureRoot();
            string outPath = CreateTempCliGoldenFilePath();
            var gbEntriesBefore = SnapshotTempGbEntries();

            try
            {
                var deniedItems = FixtureSpec.Standard.Items.Where(i => i.Traits.Contains(FixtureTrait.AccessDenied)).ToList();
                SelfAssert.That(deniedItems.Count > 0, "FixtureSpec.Standard に AccessDenied トレイトの項目が見つかりません。");

                var result = RunGoldenBaselineProcess("generate", "--out", outPath, "--root", root);
                SelfAssert.That(result.ExitCode == 0, $"generate の終了コードが0ではありません（実際: {result.ExitCode}）。標準出力: {result.StdOut} 標準エラー: {result.StdErr}");

                var document = new GoldenSerializer().Read(outPath);

                // 拒否設定の付与に失敗すると未生成として記録されるため、期待値が完全であることは、
                // 拒否設定が実際に付与された状態で走査されたことの前提になる。
                SelfAssert.That(
                    document.Header.FixtureComplete,
                    $"期待値が不完全として記録されています（未生成: {string.Join(" | ", document.Header.FixtureOmissions)}）。");

                foreach (var item in deniedItems)
                {
                    string deniedFullPath = Path.Combine(root, item.RelativePath);
                    SelfAssert.That(
                        result.StdOut.IndexOf(deniedFullPath, StringComparison.OrdinalIgnoreCase) >= 0,
                        $"スキップされた対象として拒否フォルダ '{deniedFullPath}' が標準出力に報告されていません: {result.StdOut}");
                    SelfAssert.That(
                        document.Entries.Any(e => e.RelativePath == item.RelativePath && e.Kind == GoldenEntryKind.Folder),
                        $"拒否フォルダ '{item.RelativePath}' 自身がフォルダとして期待値に記録されていません（走査が完了していない可能性があります）。");
                }

                // 拒否フォルダと無関係な項目も記録されており、走査が拒否フォルダで中断していないこと。
                var emptyItems = FixtureSpec.Standard.Items.Where(i => i.Traits.Contains(FixtureTrait.Empty)).ToList();
                SelfAssert.That(emptyItems.Count > 0, "FixtureSpec.Standard に Empty トレイトの項目が見つかりません。");
                foreach (var item in emptyItems)
                {
                    SelfAssert.That(
                        document.Entries.Any(e => e.RelativePath == item.RelativePath),
                        $"拒否フォルダと無関係な '{item.RelativePath}' が期待値に記録されていません（走査が中断している可能性があります）。");
                }

                SelfAssert.That(!Directory.Exists(root) && !Directory.Exists(LongPath.Extend(root)), $"generate の実行後に基準フォルダが残留しています: {root}");
                AssertNoNewTempGbEntries("読み取り拒否を含むフィクスチャでの generate", gbEntriesBefore, outPath);
            }
            finally
            {
                DeleteIfExists(outPath);
                ForceCleanupFixtureResidue(root);
            }
        });
    }

    /// <summary>
    /// 基準フォルダを作り、<see cref="BlockedFixtureFolderRelativePath"/> の位置に同名のファイルを置いて生成を妨げる。
    /// 妨げの影響を受ける項目（そのフォルダ自身と配下）を FixtureSpec.Standard から導出し、
    /// FixtureBuilder を介さずに同じ生成操作を拡張長パスで独立に試みて、実際に起きる例外の型名とメッセージを観測する。
    /// 例外の型名やメッセージを文字列リテラルで固定しないため、OS の表示言語やパスによらず照合できる。
    /// </summary>
    private static List<(string RelativePath, string TypeName, string Message)> BlockFixtureFolderAndObserveFailures(string root)
    {
        var affectedItems = FixtureSpec.Standard.Items
            .Where(i => i.RelativePath == BlockedFixtureFolderRelativePath
                     || i.RelativePath.StartsWith(BlockedFixtureFolderRelativePath + "\\", StringComparison.Ordinal))
            .ToList();
        SelfAssert.That(
            affectedItems.Any(i => i.RelativePath == BlockedFixtureFolderRelativePath && i.Kind == GoldenEntryKind.Folder),
            $"FixtureSpec.Standard に妨げる対象のフォルダ '{BlockedFixtureFolderRelativePath}' が見つかりません。");

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, BlockedFixtureFolderRelativePath), "blocker");

        var observed = new List<(string RelativePath, string TypeName, string Message)>();
        foreach (var item in affectedItems)
        {
            string extendedPath = LongPath.Extend(Path.Combine(root, item.RelativePath));
            Exception? caught = CaptureConstructionException(() =>
            {
                if (item.Kind == GoldenEntryKind.Folder)
                {
                    Directory.CreateDirectory(extendedPath);
                }
                else
                {
                    using (new FileStream(extendedPath, FileMode.Create, FileAccess.Write))
                    {
                    }
                }
            });

            SelfAssert.That(caught != null, $"生成を妨げたはずの '{item.RelativePath}' が、独立した試行で生成できてしまいました。");
            SelfAssert.That(
                !string.IsNullOrWhiteSpace(caught!.Message) && caught.Message.Trim().Length >= 5,
                $"'{item.RelativePath}' の生成失敗で観測した例外メッセージが短すぎて照合に使えません（実際: '{caught.Message}'）。");
            observed.Add((item.RelativePath, caught.GetType().Name, caught.Message));
        }

        return observed;
    }

    /// <summary>
    /// 期待値ファイルのテキストのヘッダが、不完全である旨（FixtureComplete: false）と、
    /// 観測した未生成項目ぶんの「# FixtureOmission: 相対パス: 例外の型名」の行を過不足なく持つことを照合する。
    /// 未生成項目の並び順は仕様に定められていないため、順序は問わない。
    /// </summary>
    private static void AssertIncompleteFixtureHeader(string text, IReadOnlyList<(string RelativePath, string TypeName, string Message)> observed, string context)
    {
        string[] lines = text.Split('\n');

        var completeLines = lines.Where(line => line.StartsWith("# FixtureComplete: ", StringComparison.Ordinal)).ToList();
        SelfAssert.That(
            completeLines.Count == 1 && completeLines[0] == "# FixtureComplete: false",
            $"{context}: ヘッダに 'FixtureComplete: false' がちょうど1行記録されていません（実際: [{string.Join(" | ", completeLines)}]）。");

        var actualOmissionLines = lines
            .Where(line => line.StartsWith("# FixtureOmission: ", StringComparison.Ordinal))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        var expectedOmissionLines = observed
            .Select(o => $"# FixtureOmission: {o.RelativePath}: {o.TypeName}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        SelfAssert.That(
            actualOmissionLines.Count == expectedOmissionLines.Count,
            $"{context}: FixtureOmission の行数が妨げた項目の件数と一致しません（想定: {expectedOmissionLines.Count} 件、実際: {actualOmissionLines.Count} 件）。実際の行: [{string.Join(" | ", actualOmissionLines)}]");
        SelfAssert.That(
            actualOmissionLines.SequenceEqual(expectedOmissionLines, StringComparer.Ordinal),
            $"{context}: FixtureOmission の行が「相対パス: 例外の型名」の形で妨げた項目と一致しません。想定: [{string.Join(" | ", expectedOmissionLines)}] 実際: [{string.Join(" | ", actualOmissionLines)}]");
    }

    /// <summary>
    /// 基準フォルダの実効絶対パス長を固定し、期待値に記録して照合する（タスク7.1）ための検証項目を登録する。
    /// 走査で項目が欠落する境界は「親フォルダの絶対パスが258文字以上だと直下を一覧できない」ことだけであり、
    /// 期待値の内容は基準フォルダの長さに左右される。長さの固定（Program）・記録（GoldenHeader / GoldenSerializer）・
    /// 照合（BaselineComparer）の3点を、単体とコマンド経由の双方で確認する。
    /// </summary>
    private static void RegisterBaseFolderPathLengthChecks(SelfCheckRunner runner)
    {
        runner.Add("既定の基準フォルダの実効絶対パス長が、正規化後の文字数でちょうど80文字に固定される（乱数は保たれ、正規化で消える冗長な表記を含む置き場でも同じ）（タスク7.1）", () =>
        {
            string temp = Path.GetTempPath();

            // 置き場2は、正規化で消える冗長な表記（<フォルダ>\..）を含む %TEMP% 相当のパス。
            // 文字列そのままの長さと正規化後の長さが必ず食い違うため、長さを Path.GetFullPath を
            // 通してから測っているかどうかを判別できる（8.3短縮名の展開と同じ性質を、環境に依存せず再現する）。
            string redundantPlacement = Path.Combine(temp, "gb_len_" + Guid.NewGuid().ToString("N").Substring(0, 8), "..");

            foreach (var placement in new[] { temp, redundantPlacement })
            {
                string first = Program.BuildDefaultRoot(placement);
                string second = Program.BuildDefaultRoot(placement);

                foreach (var root in new[] { first, second })
                {
                    int effectiveLength = Path.GetFullPath(root).Length;
                    SelfAssert.That(
                        effectiveLength == 80,
                        $"既定の基準フォルダ（置き場: {placement}）の実効絶対パス長が80文字ではありません（実際: {effectiveLength}文字、パス: {root}）。");
                    SelfAssert.That(
                        Path.GetFileName(Path.GetFullPath(root)).StartsWith("gb_fix_", StringComparison.Ordinal),
                        $"既定の基準フォルダ名が既存規約の接頭辞 'gb_fix_' で始まりません: {root}");
                }

                SelfAssert.That(
                    !string.Equals(first, second, StringComparison.Ordinal),
                    $"既定の基準フォルダが2回とも同一でした（乱数が失われ、同時実行や後始末漏れと衝突します）: {first}");
            }

            // --root を指定しない場合の解決経路（ResolveRoot）も、既定の組み立てを通って固定の長さになる。
            string resolvedDefault = Program.ResolveRoot(new Dictionary<string, string>(StringComparer.Ordinal));
            SelfAssert.That(
                Path.GetFullPath(resolvedDefault).Length == 80,
                $"--root を指定しないときの基準フォルダの実効絶対パス長が80文字ではありません（実際: {Path.GetFullPath(resolvedDefault).Length}文字、パス: {resolvedDefault}）。");

            // --root を明示した場合は、その値をそのまま尊重する（長さは強制しない）。
            string explicitRoot = Path.Combine(Path.GetTempPath(), "gb_fix_explicit");
            string resolvedExplicit = Program.ResolveRoot(
                new Dictionary<string, string>(StringComparer.Ordinal) { { "--root", explicitRoot } });
            SelfAssert.That(
                string.Equals(resolvedExplicit, explicitRoot, StringComparison.Ordinal),
                $"--root で明示した基準フォルダが尊重されていません（指定: {explicitRoot}、実際: {resolvedExplicit}）。");

            // この検証が「正規化後で測っているか」を判別できる入力になっていること自体を確かめる。
            string redundantRoot = Program.BuildDefaultRoot(redundantPlacement);
            SelfAssert.That(
                redundantRoot.Length != Path.GetFullPath(redundantRoot).Length,
                $"冗長な表記を含むはずの置き場で、文字列の長さと正規化後の長さが一致しています（測り方を判別できない入力です）: {redundantRoot}");
        });

        runner.Add("既定の置き場が長すぎて実効絶対パス長を80文字に収められない場合、原因と対処が分かるメッセージで失敗する（入力の誤りと同じ扱い）（タスク7.1）", () =>
        {
            // 実際にフォルダは作らない。パスの長さだけが問題になる経路である。
            string tooLongPlacement = Path.Combine(Path.GetTempPath(), new string('L', 80));

            Exception? caught = null;
            try
            {
                string root = Program.BuildDefaultRoot(tooLongPlacement);
                SelfAssert.That(
                    false,
                    $"長すぎる置き場でも失敗せず、基準フォルダ '{root}'（正規化後 {Path.GetFullPath(root).Length} 文字）を返しました。");
            }
            catch (Program.GoldenCliArgumentException ex)
            {
                // 入力の誤りと同じ例外型であること（＝終了コード2として扱われること）まで含めて確認する。
                caught = ex;
            }

            SelfAssert.That(caught != null, "長すぎる置き場で、入力の誤りと同じ例外型（GoldenCliArgumentException）で失敗しませんでした。");

            string message = caught!.Message;
            SelfAssert.That(
                !string.IsNullOrWhiteSpace(message) && message.Trim().Length >= 20,
                $"失敗のメッセージが短すぎて原因を伝えられません（実際: '{message}'）。");
            SelfAssert.That(
                message.Contains("80"),
                $"失敗のメッセージに固定する長さ（80文字）が示されていません: {message}");
            SelfAssert.That(
                message.Contains("--root"),
                $"失敗のメッセージに対処（--root に長さ80のパスを渡す）が示されていません: {message}");
        });

        runner.Add("GoldenSerializer が BaseFolderPathLength を形式バージョン2のヘッダとして書き出し、読み戻しても値が保たれる（タスク7.1、要件6.1相当の記録）", () =>
        {
            string path = CreateTempGoldenFilePath();
            try
            {
                // 固定値（80）とも、他のヘッダの数値（クラスタサイズ0）とも異なる値を使い、
                // 値の取り違えや固定値の書き込みで通過しないようにする。
                var header = new GoldenHeader(
                    formatVersion: 2,
                    baseFolderLabel: "fixture-v1",
                    baseFolderPathLength: 123,
                    generatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    usePhysicalSize: false,
                    clusterSizeInBytes: 0L,
                    fixtureComplete: true,
                    fixtureOmissions: Array.Empty<string>());
                var document = new GoldenDocument(header, new List<GoldenEntry> { new GoldenEntry("normal.bin", GoldenEntryKind.File, 10L) });

                var serializer = new GoldenSerializer();
                serializer.Write(document, path);

                string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(path));
                SelfAssert.That(
                    CountOccurrences(text, "# FormatVersion: 2\n") == 1,
                    $"書き出したヘッダに '# FormatVersion: 2' の行がちょうど1行ありません: {text}");
                SelfAssert.That(
                    CountOccurrences(text, "# BaseFolderPathLength: 123\n") == 1,
                    $"書き出したヘッダに '# BaseFolderPathLength: 123' の行がちょうど1行ありません: {text}");

                var roundTripped = serializer.Read(path);
                SelfAssert.That(
                    roundTripped.Header.BaseFolderPathLength == 123,
                    $"往復後の BaseFolderPathLength が書き出した値と一致しません（想定: 123、実際: {roundTripped.Header.BaseFolderPathLength}）。");
                SelfAssert.That(
                    roundTripped.Header.FormatVersion == 2,
                    $"往復後の FormatVersion が2ではありません（実際: {roundTripped.Header.FormatVersion}）。");
            }
            finally
            {
                DeleteIfExists(path);
            }
        });

        runner.Add("GoldenSerializer が形式バージョン1の期待値ファイルと、BaseFolderPathLength を欠く期待値ファイルの読み取りを拒否する（タスク7.1）", () =>
        {
            string versionOnePath = CreateTempGoldenFilePath();
            string missingKeyPath = CreateTempGoldenFilePath();
            try
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

                // 形式バージョン1（タスク7.1より前の形式）。ヘッダに項目が増えたため読めなくなる。
                string versionOneText =
                    "# FormatVersion: 1\n" +
                    "# BaseFolderLabel: fixture-v1\n" +
                    "# GeneratedAt: 2026-01-01T00:00:00.0000000+00:00\n" +
                    "# UsePhysicalSize: false\n" +
                    "# ClusterSizeInBytes: 0\n" +
                    "# FixtureComplete: true\n" +
                    "F\tnormal.bin\t10\n";
                File.WriteAllBytes(versionOnePath, utf8.GetBytes(versionOneText));

                // 形式バージョン2だが、必須になった BaseFolderPathLength を欠く。
                string missingKeyText =
                    "# FormatVersion: 2\n" +
                    "# BaseFolderLabel: fixture-v1\n" +
                    "# GeneratedAt: 2026-01-01T00:00:00.0000000+00:00\n" +
                    "# UsePhysicalSize: false\n" +
                    "# ClusterSizeInBytes: 0\n" +
                    "# FixtureComplete: true\n" +
                    "F\tnormal.bin\t10\n";
                File.WriteAllBytes(missingKeyPath, utf8.GetBytes(missingKeyText));

                var serializer = new GoldenSerializer();

                GoldenFormatException? versionOneFailure = null;
                try
                {
                    _ = serializer.Read(versionOnePath);
                }
                catch (GoldenFormatException ex)
                {
                    versionOneFailure = ex;
                }

                SelfAssert.That(
                    versionOneFailure != null,
                    "形式バージョン1の期待値ファイルを読んでも失敗しませんでした（未知のバージョンを拒否する経路が働いていません）。");
                SelfAssert.That(
                    versionOneFailure!.Message.Contains("1") && versionOneFailure.Message.Contains("2"),
                    $"形式バージョン1を拒否したメッセージに、読んだ版（1）とこのツールが扱える版（2）が示されていません: {versionOneFailure.Message}");

                bool missingKeyRejected = false;
                try
                {
                    _ = serializer.Read(missingKeyPath);
                }
                catch (GoldenFormatException)
                {
                    missingKeyRejected = true;
                }

                SelfAssert.That(
                    missingKeyRejected,
                    "BaseFolderPathLength を欠く形式バージョン2の期待値ファイルを読んでも失敗しませんでした（必須のヘッダ項目として扱われていません）。");
            }
            finally
            {
                DeleteIfExists(versionOnePath);
                DeleteIfExists(missingKeyPath);
            }
        });

        runner.Add("BaselineComparer が基準フォルダの実効絶対パス長の違いを、突き合わせより先に設定不一致として報告する（両方向・片側が0の場合も含む）（要件6.2, 6.3、タスク7.1）", () =>
        {
            // 長さ以外は必ず差分が出る入力にしておく。判定値だけでなく Entries が空であることまで
            // 照合することで、内部で突き合わせつつ判定値だけ差し替える実装を検出する
            // （tasks.md Implementation Notes: タスク2.2のレビュー教訓）。
            var expectedEntries = new List<GoldenEntry>
            {
                new GoldenEntry("keep.bin", GoldenEntryKind.File, 10L),
                new GoldenEntry("size.bin", GoldenEntryKind.File, 100L),
                new GoldenEntry("only-expected.bin", GoldenEntryKind.File, 5L),
            };
            var actualEntries = new List<GoldenEntry>
            {
                new GoldenEntry("keep.bin", GoldenEntryKind.File, 10L),
                new GoldenEntry("size.bin", GoldenEntryKind.File, 200L),
                new GoldenEntry("only-actual.bin", GoldenEntryKind.File, 7L),
            };

            var comparer = new BaselineComparer();

            // 前提: 長さが等しければ、この入力は差分ありとして3件の明細が出る。
            var sameLength = comparer.Compare(
                BuildComparerDocument(false, expectedEntries, baseFolderPathLength: 80),
                BuildComparerDocument(false, actualEntries, baseFolderPathLength: 80));
            SelfAssert.That(
                sameLength.Verdict == BaselineVerdict.Different,
                $"長さの等しい入力が差分ありと判定されません（実際: {sameLength.Verdict}）。この検証の前提が崩れています。");
            SelfAssert.That(
                sameLength.Entries.Count == 3,
                $"長さの等しい入力の差分件数が3件ではありません（実際: {sameLength.Entries.Count} 件）。この検証の前提が崩れています。");

            foreach (var pair in new[] { (Expected: 80, Actual: 104), (Expected: 104, Actual: 80), (Expected: 0, Actual: 80), (Expected: 80, Actual: 0) })
            {
                var report = comparer.Compare(
                    BuildComparerDocument(false, expectedEntries, baseFolderPathLength: pair.Expected),
                    BuildComparerDocument(false, actualEntries, baseFolderPathLength: pair.Actual));

                SelfAssert.That(
                    report.Verdict == BaselineVerdict.SettingsMismatch,
                    $"実効絶対パス長が期待値{pair.Expected}・実測{pair.Actual}で食い違うのに設定不一致と判定されません（実際: {report.Verdict}）。");
                SelfAssert.That(
                    report.Entries.Count == 0,
                    $"実効絶対パス長が期待値{pair.Expected}・実測{pair.Actual}で食い違うのに、エントリの突き合わせが行われています（差分 {report.Entries.Count} 件）。");
            }
        });

        runner.Add("BaselineComparer が実効絶対パス長の等しい入力では従来どおり判定まで進む（一致・差分ありの双方、固定値以外の長さでも）（要件6.3、タスク7.1）", () =>
        {
            var entries = new List<GoldenEntry>
            {
                new GoldenEntry("keep.bin", GoldenEntryKind.File, 10L),
                new GoldenEntry("size.bin", GoldenEntryKind.File, 100L),
            };
            var changedEntries = new List<GoldenEntry>
            {
                new GoldenEntry("keep.bin", GoldenEntryKind.File, 10L),
                new GoldenEntry("size.bin", GoldenEntryKind.File, 200L),
            };

            var comparer = new BaselineComparer();

            // 固定値（80）以外の長さでも、双方が等しければ照合を通過して判定まで進む。
            var match = comparer.Compare(
                BuildComparerDocument(false, entries, baseFolderPathLength: 104),
                BuildComparerDocument(false, entries, baseFolderPathLength: 104));
            SelfAssert.That(
                match.Verdict == BaselineVerdict.Match,
                $"実効絶対パス長の等しい同一内容の入力が一致と判定されません（実際: {match.Verdict}）。");

            var different = comparer.Compare(
                BuildComparerDocument(false, entries, baseFolderPathLength: 104),
                BuildComparerDocument(false, changedEntries, baseFolderPathLength: 104));
            SelfAssert.That(
                different.Verdict == BaselineVerdict.Different,
                $"実効絶対パス長の等しい入力の差分が判定まで進みません（実際: {different.Verdict}）。");
            AssertContainsDiff(different, DiffKind.SizeMismatch, "size.bin", "100", "200");
        });

        runner.Add("generate が --root なしのとき既定の基準フォルダの実効絶対パス長が80文字になり、期待値に形式バージョン2と BaseFolderPathLength: 80 が記録され、パスそのものは記録されない（要件2.3、タスク7.1）", () =>
        {
            string goldenPath = CreateTempCliGoldenFilePath();
            var entriesBefore = SnapshotTempGbEntries();

            try
            {
                var result = RunGoldenBaselineProcess("generate", "--out", goldenPath);
                SelfAssert.That(
                    result.ExitCode == 0,
                    $"generate（--root なし）の終了コードが0ではありません（実際: {result.ExitCode}）。標準出力: {result.StdOut} 標準エラー: {result.StdErr}");

                string reportedRoot = ExtractReportedBaseFolder(result.StdOut);
                int effectiveLength = Path.GetFullPath(reportedRoot).Length;
                SelfAssert.That(
                    effectiveLength == 80,
                    $"generate が使った既定の基準フォルダの実効絶対パス長が80文字ではありません（実際: {effectiveLength}文字、パス: {reportedRoot}）。");

                string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(goldenPath));
                SelfAssert.That(
                    CountOccurrences(text, "# FormatVersion: 2\n") == 1,
                    $"期待値ファイルに '# FormatVersion: 2' の行がちょうど1行ありません。");
                SelfAssert.That(
                    CountOccurrences(text, "# BaseFolderPathLength: 80\n") == 1,
                    $"期待値ファイルに '# BaseFolderPathLength: 80' の行がちょうど1行ありません。");

                // パスそのもの（環境差・ユーザー名を持ち込む情報）は記録しない。
                SelfAssert.That(
                    !text.Contains(reportedRoot),
                    "期待値ファイルに基準フォルダの絶対パスが記録されています。");
                SelfAssert.That(
                    !text.Contains(Path.GetFileName(reportedRoot)),
                    "期待値ファイルに基準フォルダの名前が記録されています。");
            }
            finally
            {
                DeleteIfExists(goldenPath);
                AssertNoNewTempGbEntries("generate（--root なし）", entriesBefore);
            }
        });

        runner.Add("置き場の異なる2つの基準フォルダ（どちらも実効絶対パス長80文字）で generate した期待値が、生成日時の行を除いてバイト単位で一致する（要件2.3, 7.1、タスク7.1）", () =>
        {
            string defaultRootGoldenPath = CreateTempCliGoldenFilePath();
            string altRootGoldenPath = CreateTempCliGoldenFilePath();
            var entriesBefore = SnapshotTempGbEntries();

            // %TEMP% 直下ではない別の置き場を用意し、そこに実効80文字の基準フォルダを組み立てる。
            // 長さの調整はこの検証コード自身が Path.GetFullPath で行い、本番実装には依存しない。
            string altPlacement = Path.Combine(Path.GetTempPath(), "gb_alt_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string altRoot = BuildRootWithEffectiveLength(altPlacement, 80);

            try
            {
                Directory.CreateDirectory(altPlacement);

                var defaultResult = RunGoldenBaselineProcess("generate", "--out", defaultRootGoldenPath);
                SelfAssert.That(
                    defaultResult.ExitCode == 0,
                    $"generate（既定の基準フォルダ）の終了コードが0ではありません（実際: {defaultResult.ExitCode}）。標準エラー: {defaultResult.StdErr}");

                var altResult = RunGoldenBaselineProcess("generate", "--out", altRootGoldenPath, "--root", altRoot);
                SelfAssert.That(
                    altResult.ExitCode == 0,
                    $"generate（別の置き場の基準フォルダ）の終了コードが0ではありません（実際: {altResult.ExitCode}）。標準エラー: {altResult.StdErr}");

                string defaultReportedRoot = ExtractReportedBaseFolder(defaultResult.StdOut);
                SelfAssert.That(
                    !string.Equals(
                        Path.GetDirectoryName(Path.GetFullPath(defaultReportedRoot)),
                        Path.GetDirectoryName(Path.GetFullPath(altRoot)),
                        StringComparison.OrdinalIgnoreCase),
                    "2回の generate が同じ置き場の基準フォルダを使っており、置き場の違いを確かめられていません。");

                byte[] defaultBytes = File.ReadAllBytes(defaultRootGoldenPath);
                byte[] altBytes = File.ReadAllBytes(altRootGoldenPath);

                var defaultFiltered = ExcludeGeneratedAtLine(defaultBytes);
                var altFiltered = ExcludeGeneratedAtLine(altBytes);

                SelfAssert.That(defaultFiltered.GeneratedAtLineCount == 1, $"既定の置き場の期待値から除外した生成日時の行が1行ではありません（実際: {defaultFiltered.GeneratedAtLineCount} 行）。");
                SelfAssert.That(altFiltered.GeneratedAtLineCount == 1, $"別の置き場の期待値から除外した生成日時の行が1行ではありません（実際: {altFiltered.GeneratedAtLineCount} 行）。");
                SelfAssert.That(defaultFiltered.EntryLineCount > 0, "既定の置き場の期待値にエントリ行がありません。");
                SelfAssert.That(
                    defaultFiltered.FilteredBytes.SequenceEqual(altFiltered.FilteredBytes),
                    "置き場の異なる2つの基準フォルダ（どちらも実効80文字）で生成した期待値が、生成日時の行を除いてバイト単位で一致しません。" +
                    $"（既定の置き場: {defaultFiltered.EntryLineCount} エントリ / 別の置き場: {altFiltered.EntryLineCount} エントリ）");
            }
            finally
            {
                DeleteIfExists(defaultRootGoldenPath);
                DeleteIfExists(altRootGoldenPath);
                ForceCleanupFixtureResidue(altRoot);
                if (Directory.Exists(altPlacement))
                {
                    Directory.Delete(altPlacement, recursive: true);
                }

                AssertNoNewTempGbEntries("置き場を変えた generate", entriesBefore);
            }
        });

        runner.Add("実効絶対パス長だけが異なる期待値との compare が設定不一致として終了コード2を返し、エントリの突き合わせを行わない（長さが同じなら一致する）（要件6.2, 6.3, 4.7、タスク7.1）", () =>
        {
            string goldenPath = CreateTempCliGoldenFilePath();
            string tamperedPath = CreateTempCliGoldenFilePath();
            var entriesBefore = SnapshotTempGbEntries();

            try
            {
                var genResult = RunGoldenBaselineProcess("generate", "--out", goldenPath);
                SelfAssert.That(
                    genResult.ExitCode == 0,
                    $"前提となる generate が失敗しました（終了コード: {genResult.ExitCode}）。標準エラー: {genResult.StdErr}");

                // 対比: 長さが同じ（どちらも既定の基準フォルダ = 実効80文字）なら判定まで進み、一致になる。
                var matchResult = RunGoldenBaselineProcess("compare", "--golden", goldenPath);
                SelfAssert.That(
                    matchResult.ExitCode == 0,
                    $"長さの等しい compare の終了コードが0ではありません（実際: {matchResult.ExitCode}）。標準出力: {matchResult.StdOut} 標準エラー: {matchResult.StdErr}");
                SelfAssert.That(
                    matchResult.StdOut.Contains("判定: 一致"),
                    $"長さの等しい compare の標準出力に一致の判定が含まれません: {matchResult.StdOut}");

                // 期待値の実効絶対パス長だけを書き換える（エントリには一切手を触れない）。
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                string original = utf8.GetString(File.ReadAllBytes(goldenPath));
                string tampered = ReplaceGoldenHeaderValue(original, "BaseFolderPathLength", "104");
                File.WriteAllBytes(tamperedPath, utf8.GetBytes(tampered));

                var mismatchResult = RunGoldenBaselineProcess("compare", "--golden", tamperedPath);
                SelfAssert.That(
                    mismatchResult.ExitCode == 2,
                    $"実効絶対パス長の異なる compare の終了コードが2ではありません（実際: {mismatchResult.ExitCode}）。標準出力: {mismatchResult.StdOut} 標準エラー: {mismatchResult.StdErr}");
                SelfAssert.That(
                    mismatchResult.StdOut.Contains("設定不一致"),
                    $"実効絶対パス長の異なる compare の標準出力に設定不一致の判定が含まれません: {mismatchResult.StdOut}");

                // 判定値だけでなく、実際にエントリの突き合わせが行われていないことまで確認する。
                foreach (var forbidden in new[] { "判定: 差分あり", "SizeMismatch", "Missing", "Unexpected", "KindMismatch" })
                {
                    SelfAssert.That(
                        !mismatchResult.StdOut.Contains(forbidden),
                        $"設定不一致にもかかわらず '{forbidden}' が出力されています: {mismatchResult.StdOut}");
                }
            }
            finally
            {
                DeleteIfExists(goldenPath);
                DeleteIfExists(tamperedPath);
                AssertNoNewTempGbEntries("長さの異なる期待値との compare", entriesBefore);
            }
        });

        runner.Add("--root を明示して実効絶対パス長が固定値（80文字）以外の基準フォルダで generate すると、期待値にその実測値が記録され、既定の基準フォルダとの compare が設定不一致で止まる（要件6.2, 6.3, 7.1、タスク7.1）", () =>
        {
            // この検証の要点は「ヘッダに記録される値が、実際に使った基準フォルダの実測値であること」。
            // --root を明示したときは長さを強制しない仕様なので、固定値（80）を書き込むだけの実装だと
            // 長さの違う環境との比較が設定不一致で止まらず、黙って差分ありや一致と誤判定してしまう
            // （research.md の Rationale「記録と照合を併せることで、長さを固定し損ねたときに黙って
            // 差分ありと誤判定せず、設定不一致として止まる」）。既定の基準フォルダも、検証側が自前で
            // 80文字に作った --root も、どちらも実効80文字なので固定値との区別がつかない。
            // そのため、ここでは固定値と必ず異なる長さの --root を使う。
            //
            // 長さは固定値より短い側から選ぶ。走査結果が同じになる範囲は実測で56〜104文字だが、
            // 長い側を選ぶと（将来その範囲が変わったときに）走査結果そのものが変わり、
            // 設定不一致で止まった理由が「長さの記録違い」なのか「走査結果の違い」なのか混ざるため。
            // 値は70。60だと、この環境では最短の基準フォルダが59文字のため余裕が1文字しかなく、
            // ユーザー名が1文字長い環境で検証自体が組み立てられなくなる（レビュー指摘）。
            const int ShortRootLength = 70;

            string shortRootGoldenPath = CreateTempCliGoldenFilePath();
            var entriesBefore = SnapshotTempGbEntries();

            // 長さの調整はこの検証コード自身が Path.GetFullPath で行い、本番実装には依存しない。
            string shortRoot = BuildRootWithEffectiveLength(Path.GetTempPath(), ShortRootLength);
            string anotherShortRoot = BuildRootWithEffectiveLength(Path.GetTempPath(), ShortRootLength);

            // --root には、同じ場所を指しつつ正規化で消える冗長な表記（<フォルダ>\..）を含む綴りを渡す。
            // 文字列そのままの長さと正規化後の長さが必ず食い違うため、記録される値が
            // 「Path.GetFullPath を通した後の文字数」で測られているかどうかまで判別できる
            // （タスク7.1「8.3短縮名が展開されて長さが変わるため」と同じ性質を、環境に依存せず再現する）。
            string redundantSpelling = BuildRedundantSpelling(shortRoot);

            try
            {
                // 前提: 用意した --root の実効絶対パス長が、固定値（80）と実際に異なること。
                int measuredShortLength = Path.GetFullPath(shortRoot).Length;
                SelfAssert.That(
                    measuredShortLength == ShortRootLength,
                    $"検証用の基準フォルダの実効絶対パス長が{ShortRootLength}文字ではありません（実際: {measuredShortLength}文字、パス: {shortRoot}）。この検証の前提が崩れています。");
                SelfAssert.That(
                    measuredShortLength != Program.FixedBaseFolderPathLength,
                    $"検証用の基準フォルダの実効絶対パス長が固定値（{Program.FixedBaseFolderPathLength}文字）と同じです。固定値を書き込むだけの実装と区別できません。");

                // 前提: 冗長な表記が同じ場所を指し、かつ文字列の長さが正規化後と食い違うこと（測り方を判別できる入力であること）。
                SelfAssert.That(
                    string.Equals(Path.GetFullPath(redundantSpelling), Path.GetFullPath(shortRoot), StringComparison.OrdinalIgnoreCase),
                    $"冗長な表記が同じ基準フォルダを指していません（冗長表記: {redundantSpelling}、実体: {shortRoot}）。この検証の前提が崩れています。");
                SelfAssert.That(
                    redundantSpelling.Length != ShortRootLength,
                    $"冗長な表記の文字列の長さが正規化後の長さ（{ShortRootLength}文字）と一致しています（測り方を判別できない入力です）: {redundantSpelling}");

                var genResult = RunGoldenBaselineProcess("generate", "--out", shortRootGoldenPath, "--root", redundantSpelling);
                SelfAssert.That(
                    genResult.ExitCode == 0,
                    $"固定値以外の長さの --root を指定した generate の終了コードが0ではありません（実際: {genResult.ExitCode}）。標準出力: {genResult.StdOut} 標準エラー: {genResult.StdErr}");

                // 実際に使われた基準フォルダが、指定したものと同じ（＝長さを強制されていない）こと。
                string reportedRoot = ExtractReportedBaseFolder(genResult.StdOut);
                SelfAssert.That(
                    Path.GetFullPath(reportedRoot).Length == ShortRootLength,
                    $"generate が使った基準フォルダの実効絶対パス長が{ShortRootLength}文字ではありません（実際: {Path.GetFullPath(reportedRoot).Length}文字、パス: {reportedRoot}）。--root の長さが強制されています。");

                // (a) 記録された値が、その --root の実測値であること。固定値でも、他の値の取り違えでもないこと。
                string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(shortRootGoldenPath));
                SelfAssert.That(
                    CountOccurrences(text, $"# BaseFolderPathLength: {ShortRootLength}\n") == 1,
                    $"期待値ファイルに '# BaseFolderPathLength: {ShortRootLength}'（実際に使った基準フォルダの実測値）の行がちょうど1行ありません。" +
                    $"記録されているヘッダ: {ExtractGoldenHeaderValue(text, "BaseFolderPathLength")}");
                SelfAssert.That(
                    CountOccurrences(text, $"# BaseFolderPathLength: {Program.FixedBaseFolderPathLength}\n") == 0,
                    $"期待値ファイルに固定値（{Program.FixedBaseFolderPathLength}文字）が記録されています。実際に使った基準フォルダは{ShortRootLength}文字です。");
                SelfAssert.That(
                    CountOccurrences(text, $"# BaseFolderPathLength: {redundantSpelling.Length}\n") == 0,
                    $"期待値ファイルに --root の文字列そのままの長さ（{redundantSpelling.Length}文字）が記録されています。Path.GetFullPath を通した後の文字数で測られていません。");

                // (b) その期待値と、既定の基準フォルダ（実効80文字）との compare が設定不一致で止まること。
                // 長さを記録し損ねていると、この比較が設定不一致にならず、黙って一致や差分ありと誤判定される。
                var mismatchResult = RunGoldenBaselineProcess("compare", "--golden", shortRootGoldenPath);
                SelfAssert.That(
                    mismatchResult.ExitCode == 2,
                    $"長さの異なる基準フォルダで作った期待値との compare の終了コードが2ではありません（実際: {mismatchResult.ExitCode}）。" +
                    $"標準出力: {mismatchResult.StdOut} 標準エラー: {mismatchResult.StdErr}");
                SelfAssert.That(
                    mismatchResult.StdOut.Contains("設定不一致"),
                    $"長さの異なる基準フォルダで作った期待値との compare の標準出力に設定不一致の判定が含まれません: {mismatchResult.StdOut}");

                // 判定値だけでなく、実際に差分明細が出ていないこと（＝突き合わせが行われていないこと）まで確認する。
                foreach (var forbidden in new[] { "判定: 差分あり", "判定: 一致", "SizeMismatch", "Missing", "Unexpected", "KindMismatch" })
                {
                    SelfAssert.That(
                        !mismatchResult.StdOut.Contains(forbidden),
                        $"設定不一致にもかかわらず '{forbidden}' が出力されています: {mismatchResult.StdOut}");
                }

                // 対比: 同じ長さ（実効70文字）の別の基準フォルダとなら、照合を通過して一致まで進む。
                // これにより (b) の設定不一致が「長さの違い」だけを理由にしていることを確かめる。
                var matchResult = RunGoldenBaselineProcess("compare", "--golden", shortRootGoldenPath, "--root", anotherShortRoot);
                SelfAssert.That(
                    matchResult.ExitCode == 0,
                    $"同じ長さ（{ShortRootLength}文字）の別の基準フォルダとの compare の終了コードが0ではありません（実際: {matchResult.ExitCode}）。" +
                    $"標準出力: {matchResult.StdOut} 標準エラー: {matchResult.StdErr}");
                SelfAssert.That(
                    matchResult.StdOut.Contains("判定: 一致"),
                    $"同じ長さ（{ShortRootLength}文字）の別の基準フォルダとの compare の標準出力に一致の判定が含まれません: {matchResult.StdOut}");
            }
            finally
            {
                DeleteIfExists(shortRootGoldenPath);
                ForceCleanupFixtureResidue(shortRoot);
                ForceCleanupFixtureResidue(anotherShortRoot);
                AssertNoNewTempGbEntries("固定値以外の長さの --root での generate と compare", entriesBefore);
            }
        });
    }


    /// <summary>
    /// 基準フォルダの絶対パス長に由来する境界に関する検証項目を登録する（タスク7.2）。
    /// 実測の境界は「親フォルダの絶対パスが258文字以上だと、その直下を一覧できない」の1つだけであり
    /// （2026-09-16 実測）、フォルダとファイルに差はない。ここでは次の2点を守る。
    /// 1. スキップ検出が境界の長さのフォルダで例外を外へ漏らさないこと（要件2.4）
    /// 2. 既知の不具合では説明できない欠落が、黙って消えずに報告されること（要件5.2, 5.5）
    /// </summary>
    private static void RegisterPathLengthBoundaryChecks(SelfCheckRunner runner)
    {
        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 絶対258〜261文字のフォルダは「長さのせいで列挙できなかった対象」としてちょうど4件記録され、257文字は記録されない。
        // 新: .NET 10 はそれらの直下も列挙できるため、「長さのせいで列挙できなかった対象」は0件で、直下のファイルが走査結果に現れる。
        //     例外を漏らさないこと、スキップが読み取り拒否フォルダだけであることの期待は変えていない。
        runner.Add("ScanRunner が、絶対パスが258・259・260・261文字ちょうどのフォルダを含む基準フォルダでも例外を外へ漏らさず走査を完了し、それらの直下を列挙でき、アクセス拒否によるスキップと取り違えない（要件2.4、タスク7.2）", () =>
        {
            var entriesBefore = SnapshotTempGbEntries();
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gb_len_" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            string deniedFolder = Path.Combine(root, "denied");
            var gate = new AccessControlGate();
            var boundaryFolders = new Dictionary<int, string>();

            try
            {
                Directory.CreateDirectory(LongPath.Extend(root));

                // 絶対パスの長さがちょうど目的の値になるフォルダを作る。長さは実効（正規化後）の文字数で数える。
                foreach (int length in new[] { 258, 259, 260, 261 })
                {
                    int nameLength = length - root.Length - 1;
                    SelfAssert.That(
                        nameLength >= 1 && nameLength <= 255,
                        $"基準フォルダ '{root}'（{root.Length}文字）の下に絶対パス{length}文字のフォルダを作れません（必要な名前の長さ: {nameLength}文字）。");

                    string folder = root + "\\" + new string('n', nameLength);
                    SelfAssert.That(
                        Path.GetFullPath(folder).Length == length,
                        $"検証用フォルダの絶対パスが{length}文字になりません（実際: {Path.GetFullPath(folder).Length}文字）。");

                    // 生成は拡張長パス経由で行う（.NET Framework ではプレーンなパスで260文字以上を作成できなかった。生成の経路は移行後も変えない）。
                    Directory.CreateDirectory(LongPath.Extend(folder));
                    SelfAssert.That(Directory.Exists(LongPath.Extend(folder)), $"絶対パス{length}文字のフォルダを作成できませんでした。");

                    // 「列挙できていれば見えるはず」の対比として直下に1件置く。
                    File.WriteAllText(LongPath.Extend(folder + "\\child.txt"), "x");

                    boundaryFolders[length] = folder;
                }

                // 下側の対比: 絶対257文字のフォルダは一覧できる（境界が258文字であることを示す）。
                int enumerableNameLength = 257 - root.Length - 1;
                SelfAssert.That(
                    enumerableNameLength >= 1 && enumerableNameLength <= 255,
                    $"基準フォルダ '{root}'（{root.Length}文字）の下に絶対パス257文字のフォルダを作れません（必要な名前の長さ: {enumerableNameLength}文字）。");
                string enumerableFolder = root + "\\" + new string('s', enumerableNameLength);
                SelfAssert.That(
                    Path.GetFullPath(enumerableFolder).Length == 257,
                    $"対比用フォルダの絶対パスが257文字になりません（実際: {Path.GetFullPath(enumerableFolder).Length}文字）。");
                Directory.CreateDirectory(LongPath.Extend(enumerableFolder));
                File.WriteAllText(LongPath.Extend(enumerableFolder + "\\child.txt"), "x");

                // アクセス拒否によるスキップの対比。長さ由来の失敗と取り違えていないことを示すために同居させる。
                Directory.CreateDirectory(LongPath.Extend(deniedFolder));
                gate.DenyRead(deniedFolder);

                var scanRunner = new ScanRunner();

                ScanOutcome outcome;
                try
                {
                    outcome = scanRunner.Run(root, usePhysicalSize: false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"境界の長さ（258・259・260・261文字）のフォルダを含む基準フォルダの走査で例外が外へ漏れました: {ex.GetType().Name}: {ex.Message}",
                        ex);
                }

                // 走査自体は完了している（要件2.4: スキップを記録して継続する）。
                SelfAssert.That(outcome.Root != null, "走査結果のルートが得られていません。");

                // スキップとして記録されるのはアクセス拒否だけであり、長さのせいで列挙できない対象は含まない。
                SelfAssert.That(
                    outcome.SkippedPaths.Count == 1,
                    $"スキップされた対象がちょうど1件（読み取り拒否フォルダのみ）ではありません（実際: {outcome.SkippedPaths.Count} 件）。実際の内容: {string.Join(" / ", outcome.SkippedPaths)}");
                SelfAssert.That(
                    string.Equals(outcome.SkippedPaths[0], deniedFolder, StringComparison.OrdinalIgnoreCase),
                    $"スキップされた対象が読み取り拒否フォルダではありません（実際: {outcome.SkippedPaths[0]}）。");

                foreach (var pair in boundaryFolders)
                {
                    SelfAssert.That(
                        !outcome.SkippedPaths.Any(p => string.Equals(p, pair.Value, StringComparison.OrdinalIgnoreCase)),
                        $"絶対パス{pair.Key}文字のフォルダが「アクセス拒否によるスキップ」として記録されています。長さのせいで列挙できないことはアクセス拒否とは別の事象であり、取り違えてはならない。");
                }

                // .NET 10 では長いパスも列挙できるため、「長さのせいで列挙できなかった対象」は1件もない。
                SelfAssert.That(
                    outcome.UnenumerablePaths.Count == 0,
                    $"長さのせいで列挙できなかった対象が0件ではありません（実際: {outcome.UnenumerablePaths.Count} 件）。実際の内容: {string.Join(" / ", outcome.UnenumerablePaths)}");

                // 列挙できたことを、直下に置いた1件が走査結果に現れることで確かめる（257文字の対比用フォルダも同じ）。
                var scanMap = FlattenScanTree(outcome.Root!);
                var enumeratedFolders = boundaryFolders
                    .Select(pair => (Length: pair.Key, Folder: pair.Value))
                    .Concat(new[] { (Length: 257, Folder: enumerableFolder) });
                foreach (var (length, folder) in enumeratedFolders)
                {
                    string childRelativePath = folder.Substring(root.Length + 1) + "\\child.txt";
                    SelfAssert.That(
                        scanMap.ContainsKey(childRelativePath),
                        $"絶対パス{length}文字のフォルダの直下のファイルが走査結果に現れません（.NET 10 では列挙できるはずです）。");
                }

                // 257文字の対比用フォルダもスキップとして記録されない。
                SelfAssert.That(
                    !outcome.SkippedPaths.Any(p => string.Equals(p, enumerableFolder, StringComparison.OrdinalIgnoreCase)),
                    "絶対パス257文字のフォルダが「アクセス拒否によるスキップ」として記録されています。");
            }
            finally
            {
                try
                {
                    gate.RestoreRead(deniedFolder);
                }
                catch
                {
                    // 後始末の再試行（icacls 経由）へ進む。
                }

                ForceCleanupFixtureResidue(root);
                AssertNoNewTempGbEntries("境界の長さのフォルダを含む走査", entriesBefore);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 既知の欠落（LongPath 由来）4件・説明できない欠落2件・長さのせいで列挙できなかった対象2件が、区別されて列挙される。
        // 新: .NET 10 では、旧境界（258文字）を超えて観測されなかった項目もすべて観測されるため、3区画はいずれも0件で、
        //     それらの項目は期待値ファイルに記録される。スキップが読み取り拒否フォルダだけであることの期待は変えていない。
        runner.Add("実効絶対パス長105文字の基準フォルダで generate が完走して終了コード0を返し、旧境界を超える項目も観測されるため、既知の欠落・説明できない欠落・長さのせいで列挙できなかった対象がいずれも0件と報告される（要件2.4, 5.2, 5.5、タスク7.2）", () =>
        {
            // 実効絶対パス長105文字を選ぶ理由: FixtureSpec.Standard の長い連鎖の3階層目（相対152文字）の
            // 絶対パスがちょうど258文字になり、.NET Framework では (a) スキップ検出が例外を漏らす境界と、
            // (b) 印（LongPath）の付いていない項目まで観測されなくなる状況とを同時に再現できたため。
            // .NET 10 では、その同じ状況でも全項目が観測されることを確かめる。
            const int RootLength = 105;

            // .NET Framework での実測の境界（2026-09-16）: 親フォルダの絶対パスが258文字以上だと、その直下を一覧できなかった。
            // 本番コードの実装には依存せず、この検証コード自身が独立に「旧境界なら観測されなかった項目」を導く。
            const int EnumerationBoundary = 258;

            var entriesBefore = SnapshotTempGbEntries();
            string root = BuildRootWithEffectiveLength(Path.GetTempPath(), RootLength);
            string goldenPath = CreateTempCliGoldenFilePath();

            var spec = FixtureSpec.Standard;

            bool IsUnobservable(FixtureItem item)
            {
                return EnumerateAncestorRelativePaths(item.RelativePath)
                    .Any(ancestor => RootLength + 1 + ancestor.Length >= EnumerationBoundary);
            }

            var unobservable = spec.Items.Where(IsUnobservable).ToList();
            var expectedKnown = unobservable
                .Where(i => i.Traits.Contains(FixtureTrait.LongPath))
                .Select(i => i.RelativePath)
                .ToList();
            var expectedUnexplained = unobservable
                .Where(i => !i.Traits.Contains(FixtureTrait.LongPath))
                .Select(i => i.RelativePath)
                .ToList();

            // 一覧できないフォルダ自身（その親までは一覧できる）。
            var expectedUnenumerable = spec.Items
                .Where(i => i.Kind == GoldenEntryKind.Folder)
                .Where(i => RootLength + 1 + i.RelativePath.Length >= EnumerationBoundary && !IsUnobservable(i))
                .Select(i => root + "\\" + i.RelativePath)
                .ToList();

            SelfAssert.That(
                expectedKnown.Count == 4,
                $"前提が崩れています: 実効{RootLength}文字の基準フォルダで観測されなくなる LongPath の項目が4件ではありません（実際: {expectedKnown.Count} 件）。");
            SelfAssert.That(
                expectedUnexplained.Count == 2,
                $"前提が崩れています: 実効{RootLength}文字の基準フォルダで観測されなくなる「LongPath の印がない」項目が2件ではありません（実際: {expectedUnexplained.Count} 件）。この検証は旧境界で「LongPath の印がない」項目まで観測されなくなる状況を必要とする。");
            SelfAssert.That(
                expectedUnenumerable.Count == 2,
                $"前提が崩れています: 長さのせいで一覧できないフォルダが2件ではありません（実際: {expectedUnenumerable.Count} 件）。");

            try
            {
                var result = RunGoldenBaselineProcess("generate", "--out", goldenPath, "--root", root);

                SelfAssert.That(
                    result.ExitCode == 0,
                    $"実効{RootLength}文字の基準フォルダでの generate の終了コードが0ではありません（実際: {result.ExitCode}）。" +
                    $"標準出力: {result.StdOut} 標準エラー: {result.StdErr}");

                // 旧境界で観測されなかった項目（既知の欠落・説明できない欠落の候補）も、長さのせいで一覧できなかったフォルダも、
                // .NET 10 では報告されない。
                var reportedKnown = ExtractReportItems(result.StdOut, "既知の欠落（境界条件に由来）: ");
                AssertReportedSetEquals(
                    "既知の欠落",
                    new List<string>(),
                    reportedKnown,
                    result.StdOut);

                var reportedUnexplained = ExtractReportItems(result.StdOut, "説明できない欠落（既知の不具合では説明できない未観測の項目）: ");
                AssertReportedSetEquals(
                    "説明できない欠落",
                    new List<string>(),
                    reportedUnexplained,
                    result.StdOut);

                var reportedUnenumerable = ExtractReportItems(result.StdOut, "長さのせいで列挙できなかった対象: ");
                AssertReportedSetEquals(
                    "長さのせいで列挙できなかった対象",
                    new List<string>(),
                    reportedUnenumerable,
                    result.StdOut);

                // スキップとして報告されるのは読み取り拒否フォルダだけであり、長さ由来の失敗を含めない。
                var reportedSkipped = ExtractReportItems(result.StdOut, "スキップされた対象: ");
                AssertReportedSetEquals(
                    "スキップされた対象",
                    new List<string> { root + "\\access_denied_folder" },
                    reportedSkipped,
                    result.StdOut);

                SelfAssert.That(File.Exists(goldenPath), $"期待値ファイルが書き出されていません: {goldenPath}");

                // 報告が0件であることと整合して、旧境界で観測されなかった項目が期待値ファイルに記録されていること。
                var document = new GoldenSerializer().Read(goldenPath);
                var recordedPaths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);
                foreach (var relativePath in expectedKnown.Concat(expectedUnexplained))
                {
                    SelfAssert.That(
                        recordedPaths.Contains(relativePath),
                        $"旧境界では観測されなかった項目 '{relativePath}' が期待値ファイルに記録されていません（.NET 10 では観測されるはずです）。");
                }

                // 旧境界で一覧できなかったフォルダ自身も記録されていること。
                foreach (var fullPath in expectedUnenumerable)
                {
                    string relativePath = fullPath.Substring(root.Length + 1);
                    SelfAssert.That(
                        recordedPaths.Contains(relativePath),
                        $"旧境界では一覧できなかったフォルダ '{relativePath}' が期待値ファイルに記録されていません。");
                }
            }
            finally
            {
                DeleteIfExists(goldenPath);
                ForceCleanupFixtureResidue(root);
                AssertNoNewTempGbEntries($"実効{RootLength}文字の基準フォルダでの generate", entriesBefore);
            }
        });

        // dotnet10-migration タスク2.4 で期待を改めた項目。
        // 旧: 前提として、プレーンなパスでは絶対261文字のフォルダが「存在しない」と判定される（.NET Framework の制約）。
        //     これにより実在確認が拡張長パス経由であることまで見分けていた。
        // 新: .NET 10 ではプレーンなパスでも「存在する」と判定される（拡張長パス経由と同じ結論）。
        //     拡張長パス経由かどうかはこの項目では見分けられなくなったため、名前からその主張を外した。
        //     分類の規則（長さ由来・アクセス拒否・想定外の振り分け、実在しないパスの扱い）の期待は変えていない。
        runner.Add("ScanRunner の失敗分類が、長さ由来の判定に実在確認を用い、実在しないパス・別の型の例外・アクセス拒否を長さ由来と取り違えない（要件2.4、タスク7.2）", () =>
        {
            // 実行経路（DetectSkippedPaths）だけでは、実在確認を省いた実装・プレーンなパスで確認する実装と
            // 正しい実装を区別できない（絶対258・259文字の経路ではプレーンな実在確認も真になるため）。
            // そこで分類規則そのものを直接呼び出して照合する。
            var entriesBefore = SnapshotTempGbEntries();
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gb_len_" + Guid.NewGuid().ToString("N").Substring(0, 8)));

            try
            {
                Directory.CreateDirectory(LongPath.Extend(root));

                int nameLength = 261 - root.Length - 1;
                SelfAssert.That(
                    nameLength >= 1 && nameLength <= 255,
                    $"基準フォルダ '{root}'（{root.Length}文字）の下に絶対パス261文字のフォルダを作れません（必要な名前の長さ: {nameLength}文字）。");

                string existingLong = root + "\\" + new string('e', nameLength);
                string missingLong = root + "\\" + new string('m', nameLength);
                string missingShort = root + "\\missing";

                Directory.CreateDirectory(LongPath.Extend(existingLong));

                // 前提の確認。
                SelfAssert.That(
                    Path.GetFullPath(existingLong).Length == 261,
                    $"検証用フォルダの絶対パスが261文字になりません（実際: {Path.GetFullPath(existingLong).Length}文字）。");
                SelfAssert.That(
                    Directory.Exists(LongPath.Extend(existingLong)),
                    "検証用の絶対パス261文字のフォルダが、拡張長パス経由で実在すると判定されません。");
                SelfAssert.That(
                    Directory.Exists(existingLong),
                    "前提が崩れています: プレーンなパスで絶対261文字のフォルダが「存在しない」と判定されました" +
                    "（.NET 10 では拡張長パス経由と同じく存在すると判定されるはずです）。");
                SelfAssert.That(
                    !Directory.Exists(LongPath.Extend(missingLong)) && !Directory.Exists(LongPath.Extend(missingShort)),
                    "検証用の実在しないパスが、拡張長パス経由で実在すると判定されました。");

                // (a) 実在する絶対261文字のフォルダ × 長さ由来になりうる例外 → 長さ由来。
                SelfAssert.That(
                    ScanRunner.IsPathLengthFailure(new DirectoryNotFoundException(), existingLong),
                    "実在する絶対261文字のフォルダに対する DirectoryNotFoundException が、長さ由来と判定されません。" +
                    "実在確認が拡張長パス経由でない可能性があります。");
                SelfAssert.That(
                    ScanRunner.IsPathLengthFailure(new PathTooLongException(), existingLong),
                    "実在する絶対261文字のフォルダに対する PathTooLongException が、長さ由来と判定されません。");

                // (b) 同じ長さの実在しないパス → 長さ由来ではない（実在確認を省く実装を検出する）。
                SelfAssert.That(
                    !ScanRunner.IsPathLengthFailure(new DirectoryNotFoundException(), missingLong),
                    "実在しない絶対261文字のパスに対する DirectoryNotFoundException が、長さ由来と判定されました。" +
                    "実在確認を省いている可能性があります（本当に存在しない場合は例外をそのまま送出しなければならない）。");

                // (c) 実在しない短いパス → 長さ由来ではない。
                SelfAssert.That(
                    !ScanRunner.IsPathLengthFailure(new DirectoryNotFoundException(), missingShort),
                    "実在しない短いパスに対する DirectoryNotFoundException が、長さ由来と判定されました。");

                // 型が違えば、実在していても長さ由来ではない。
                SelfAssert.That(
                    !ScanRunner.IsPathLengthFailure(new IOException("別の入出力エラー"), existingLong),
                    "長さ由来になりえない型（IOException）の例外が、長さ由来と判定されました。");

                // 分類の3系統。アクセス拒否はパスの長さや実在に関わらず常に「スキップ」に分類される。
                SelfAssert.That(
                    ScanRunner.ClassifyEnumerationFailure(new UnauthorizedAccessException(), existingLong) == EnumerationFailureKind.AccessDenied,
                    "実在する絶対261文字のフォルダに対するアクセス拒否が、AccessDenied に分類されません（長さ由来と取り違えてはならない）。");
                SelfAssert.That(
                    ScanRunner.ClassifyEnumerationFailure(new UnauthorizedAccessException(), missingShort) == EnumerationFailureKind.AccessDenied,
                    "アクセス拒否の分類がパスに依存しています（常に AccessDenied でなければならない）。");
                SelfAssert.That(
                    ScanRunner.ClassifyEnumerationFailure(new DirectoryNotFoundException(), existingLong) == EnumerationFailureKind.PathLength,
                    "実在する絶対261文字のフォルダに対する DirectoryNotFoundException が、PathLength に分類されません。");
                SelfAssert.That(
                    ScanRunner.ClassifyEnumerationFailure(new DirectoryNotFoundException(), missingLong) == EnumerationFailureKind.Unexpected,
                    "実在しない絶対261文字のパスに対する DirectoryNotFoundException が、Unexpected に分類されません（握りつぶしてはならない）。");
                SelfAssert.That(
                    ScanRunner.ClassifyEnumerationFailure(new IOException("別の入出力エラー"), existingLong) == EnumerationFailureKind.Unexpected,
                    "想定していない型の例外が Unexpected に分類されません（握りつぶしてはならない）。");

                // 実在確認そのもの（拡張長パス経由の実装）が、実在するものと実在しないものを正しく見分ける。
                SelfAssert.That(
                    ScanRunner.DirectoryExistsThroughExtendedPath(existingLong),
                    "拡張長パス経由の実在確認が、実在する絶対261文字のフォルダを見つけられません。");
                SelfAssert.That(
                    !ScanRunner.DirectoryExistsThroughExtendedPath(missingLong),
                    "拡張長パス経由の実在確認が、実在しないパスを「実在する」と判定しました。");

                // 属性の取得が -1（INVALID_FILE_ATTRIBUTES）で失敗したときの判定も、例外経路と同じ実在確認を通す。
                // (a) 実在する絶対261文字のフォルダ → 長さ由来として受理する（送出しない）。
                string accepted;
                try
                {
                    accepted = ScanRunner.EnsureAttributeFailureIsLengthInduced(existingLong);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"実在する絶対261文字のフォルダに対する属性取得の失敗が、長さ由来として受理されません: {ex.GetType().Name}: {ex.Message}",
                        ex);
                }

                SelfAssert.That(
                    accepted == existingLong,
                    $"長さ由来として受理された値が、検査した対象と一致しません（実際: {accepted}）。");

                // (b) 同じ長さの実在しないパス → 長さ由来と決めつけず DirectoryNotFoundException を送出する。
                bool attributeGuardThrew = false;
                try
                {
                    ScanRunner.EnsureAttributeFailureIsLengthInduced(missingLong);
                }
                catch (DirectoryNotFoundException)
                {
                    attributeGuardThrew = true;
                }

                SelfAssert.That(
                    attributeGuardThrew,
                    "属性を取得できず、拡張長パス経由でも実在しないパスが DirectoryNotFoundException で拒否されません。" +
                    "このガードがないと、列挙の直後に消えた対象まで「長さのせいで列挙できなかった対象」として記録される（取り違え）。");

                // 振り分け: 分類に応じて記録先が分かれ、想定外は記録せずそのまま送出される。
                var skipped = new List<string>();
                var unenumerable = new List<string>();

                ScanRunner.RecordEnumerationFailure(new UnauthorizedAccessException(), existingLong, skipped, unenumerable);
                SelfAssert.That(
                    skipped.Count == 1 && skipped[0] == existingLong && unenumerable.Count == 0,
                    $"アクセス拒否が「スキップされた対象」へ記録されません（スキップ: {skipped.Count} 件、列挙できなかった対象: {unenumerable.Count} 件）。");

                ScanRunner.RecordEnumerationFailure(new DirectoryNotFoundException(), existingLong, skipped, unenumerable);
                SelfAssert.That(
                    unenumerable.Count == 1 && unenumerable[0] == existingLong && skipped.Count == 1,
                    $"長さ由来の失敗が「長さのせいで列挙できなかった対象」へ記録されません（スキップ: {skipped.Count} 件、列挙できなかった対象: {unenumerable.Count} 件）。");

                // 目印のメソッドから実際に送出させた例外を使う。これにより「同一インスタンスか」だけでなく
                // 「元のスタックのままか」まで照合できる（throw ex; はスタックを送出地点で切り捨てる）。
                IOException unexpected = CaptureMarkerIoException();
                SelfAssert.That(
                    unexpected.StackTrace != null && unexpected.StackTrace.Contains(MarkerThrowMethodName),
                    "前提が崩れています: 捕捉した例外のスタックトレースに送出元のメソッド名が含まれません（スタックの保持を照合できません）。");

                bool rethrown = false;
                bool stackPreserved = false;
                try
                {
                    ScanRunner.RecordEnumerationFailure(unexpected, existingLong, skipped, unenumerable);
                }
                catch (IOException ex)
                {
                    rethrown = ReferenceEquals(ex, unexpected);
                    stackPreserved = ex.StackTrace != null && ex.StackTrace.Contains(MarkerThrowMethodName);
                }

                SelfAssert.That(
                    rethrown,
                    "想定していない理由の失敗が、元の例外のまま送出されません（握りつぶしてはならない。design.md Error Handling）。");
                SelfAssert.That(
                    stackPreserved,
                    "想定していない理由の失敗が、元のスタックを失って送出されています（送出地点で切り捨てる throw ex; ではなく、" +
                    "ExceptionDispatchInfo.Capture(ex).Throw() で送出すること）。");
                SelfAssert.That(
                    skipped.Count == 1 && unenumerable.Count == 1,
                    $"想定していない理由の失敗が、いずれかの一覧へ記録されました（スキップ: {skipped.Count} 件、列挙できなかった対象: {unenumerable.Count} 件）。");
            }
            finally
            {
                ForceCleanupFixtureResidue(root);
                AssertNoNewTempGbEntries("失敗分類の検証", entriesBefore);
            }
        });

        runner.Add("属性を取得できないフォルダが長さ由来でないとき、列挙が「長さのせいで列挙できなかった対象」として記録せず DirectoryNotFoundException を送出する（要件2.4、タスク7.2）", () =>
        {
            // -1（INVALID_FILE_ATTRIBUTES）の分岐が実在確認のガードを実際に通ることを、実行経路で確かめる。
            // 末尾に空白を持つ名前のフォルダを使う。拡張長プレフィクス付きでは作成できるが、
            // プレーンな表記は .NET の正規化で末尾の空白が取り除かれ、別の（存在しない）場所を指すようになる。
            // これにより「列挙には現れるが属性を取得できず、拡張長パス経由でも実在しない」状況を
            // 競合状態に頼らず決定的に作れる。
            var entriesBefore = SnapshotTempGbEntries();
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gb_len_" + Guid.NewGuid().ToString("N").Substring(0, 8)));

            // このフォルダだけは LongPath.Extend を使えない。Extend は正規化を通すため、
            // 末尾の空白が取り除かれて目的の状況を作れなくなる。削除も同じ組み立て方で行う。
            string oddName = "vanishing_name ";
            string oddPlain = root + @"\" + oddName;
            string oddExtended = @"\\?\" + root + @"\" + oddName;

            try
            {
                Directory.CreateDirectory(LongPath.Extend(root));
                Directory.CreateDirectory(oddExtended);

                // 前提の確認。いずれかが崩れると、この検証は目的の分岐を通らなくなる。
                SelfAssert.That(
                    Directory.Exists(oddExtended),
                    "前提が崩れています: 末尾に空白を持つ名前のフォルダを作成できませんでした。");
                SelfAssert.That(
                    Directory.GetDirectories(root).Any(d => string.Equals(d, oddPlain, StringComparison.Ordinal)),
                    $"前提が崩れています: 列挙結果に末尾の空白を保った表記が現れません（実際: {string.Join(" / ", Directory.GetDirectories(root))}）。");
                SelfAssert.That(
                    new DirectoryInfo(oddPlain).Attributes == (FileAttributes)(-1),
                    $"前提が崩れています: 属性の取得が -1 になりません（実際: {(int)new DirectoryInfo(oddPlain).Attributes}）。この検証は -1 の分岐を通る必要があります。");
                SelfAssert.That(
                    !Directory.Exists(LongPath.Extend(oddPlain)),
                    "前提が崩れています: 正規化を通したパスが実在すると判定されました（長さ由来でないことを表現できません）。");

                var skipped = new List<string>();
                var unenumerable = new List<string>();
                bool threw = false;

                try
                {
                    ScanRunner.DetectSkippedPaths(root, skipped, unenumerable);
                }
                catch (DirectoryNotFoundException)
                {
                    threw = true;
                }

                SelfAssert.That(
                    threw,
                    "属性を取得できず、長さ由来でもない対象に対して DirectoryNotFoundException が送出されません。" +
                    "-1 の分岐が実在確認のガードを通っていない可能性があります。");
                SelfAssert.That(
                    unenumerable.Count == 0,
                    $"長さ由来でない対象が「長さのせいで列挙できなかった対象」として記録されました（{string.Join(" / ", unenumerable)}）。");
                SelfAssert.That(
                    skipped.Count == 0,
                    $"長さ由来でない対象が「スキップされた対象」として記録されました（{string.Join(" / ", skipped)}）。");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(oddExtended))
                    {
                        Directory.Delete(oddExtended, recursive: true);
                    }
                }
                catch
                {
                    // 残留の除去は ForceCleanupFixtureResidue の再帰削除に委ねる。
                }

                ForceCleanupFixtureResidue(root);
                AssertNoNewTempGbEntries("属性を取得できないフォルダの検証", entriesBefore);
            }
        });

        runner.Add("KnownIssueAnalyzer が、既知の不具合（LongPath）で説明できる欠落と説明できない欠落を排他に分け、説明できない欠落を境界条件とともに列挙する（要件5.2, 5.5、タスク7.2）", () =>
        {
            // 人工的なフィクスチャ定義で、欠落の3系統（LongPath あり / Ordinary のみ / Japanese+Empty）と、
            // 観測されている項目（LongPath を持つが欠落していない）を同時に用意する。
            var items = new List<FixtureItem>
            {
                new FixtureItem(@"observed", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                new FixtureItem(@"observed\long_but_present.txt", GoldenEntryKind.File, 1L, new[] { FixtureTrait.LongPath }),
                new FixtureItem(@"missing_long", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.LongPath }),
                new FixtureItem(@"missing_ordinary", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Ordinary }),
                new FixtureItem(@"missing_japanese", GoldenEntryKind.Folder, 0L, new[] { FixtureTrait.Japanese, FixtureTrait.Empty }),
            };
            var spec = new FixtureSpec("unexplained-omission-fixture", items);

            var header = new GoldenHeader(2, "unexplained-omission-fixture", 80, DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
            var observed = new GoldenDocument(header, new[]
            {
                new GoldenEntry(@"observed", GoldenEntryKind.Folder, 0L),
                new GoldenEntry(@"observed\long_but_present.txt", GoldenEntryKind.File, 1L),
            });

            var analyzer = new KnownIssueAnalyzer();

            // 既知の欠落の判定は LongPath を根拠とする（この検証では変えない前提）。
            var findings = analyzer.Analyze(spec, observed);
            SelfAssert.That(
                findings.Count == 1,
                $"既知の欠落が1件ではありません（実際: {findings.Count} 件）。実際の内容: {string.Join(" / ", findings.Select(f => f.RelativePath + "/" + f.Trait))}");
            SelfAssert.That(
                findings[0].RelativePath == "missing_long" && findings[0].Trait == FixtureTrait.LongPath,
                $"既知の欠落の内容が想定と異なります（実際: {findings[0].RelativePath} / {findings[0].Trait}）。");

            // 説明できない欠落は、既知の欠落と排他に、定義にあって観測されなかった残りを列挙する。
            var unexplained = analyzer.FindUnexplainedOmissions(spec, observed);
            var unexplainedPaths = unexplained.Select(o => o.RelativePath).ToList();

            SelfAssert.That(
                unexplained.Count == 2,
                $"説明できない欠落が2件ではありません（実際: {unexplained.Count} 件）。実際の内容: {string.Join(" / ", unexplainedPaths)}");
            SelfAssert.That(
                unexplainedPaths.Contains("missing_ordinary"),
                $"Ordinary のみを持つ欠落が説明できない欠落として列挙されていません。実際の内容: {string.Join(" / ", unexplainedPaths)}");
            SelfAssert.That(
                unexplainedPaths.Contains("missing_japanese"),
                $"Japanese/Empty のみを持つ欠落が説明できない欠落として列挙されていません。実際の内容: {string.Join(" / ", unexplainedPaths)}");
            SelfAssert.That(
                !unexplainedPaths.Contains("missing_long"),
                "既知の欠落（LongPath 由来）が説明できない欠落にも含まれています。両者は排他でなければならない。");
            SelfAssert.That(
                !unexplainedPaths.Contains("observed") && !unexplainedPaths.Contains(@"observed\long_but_present.txt"),
                $"観測されている項目が説明できない欠落として列挙されています。実際の内容: {string.Join(" / ", unexplainedPaths)}");

            // 識別できる情報として、その項目が持っていた境界条件を添える（原因の断定ではない）。
            var japaneseOmission = unexplained.Single(o => o.RelativePath == "missing_japanese");
            SelfAssert.That(
                japaneseOmission.Traits.Contains(FixtureTrait.Japanese) && japaneseOmission.Traits.Contains(FixtureTrait.Empty),
                $"説明できない欠落に、その項目が持つ境界条件が添えられていません（実際: {string.Join("、", japaneseOmission.Traits.Select(t => t.ToString()))}）。");

            // 既知の欠落と説明できない欠落を合わせると、定義にあって観測されなかった項目の全体になる。
            var union = findings.Select(f => f.RelativePath).Concat(unexplainedPaths).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var expectedUnion = new[] { "missing_japanese", "missing_long", "missing_ordinary" };
            SelfAssert.That(
                union.SequenceEqual(expectedUnion, StringComparer.Ordinal),
                $"既知の欠落と説明できない欠落の和が、定義にあって観測されなかった項目の全体と一致しません（実際: {string.Join(" / ", union)}）。");
        });
    }

    /// <summary>スタックの保持を照合するための目印となるメソッド名。</summary>
    private const string MarkerThrowMethodName = "ThrowMarkerIoExceptionForStackTrace";

    /// <summary>
    /// スタックトレースに名前が残る目印として、実際に例外を送出するだけのメソッド。
    /// インライン化されると名前が消えるため、明示的に抑止する。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowMarkerIoExceptionForStackTrace()
    {
        throw new IOException("想定していない入出力エラー");
    }

    /// <summary>
    /// 目印のメソッドから実際に送出された例外を捕捉して返す。
    /// <c>new IOException(...)</c> と違い、スタックトレースに送出元が記録された状態で得られる。
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IOException CaptureMarkerIoException()
    {
        try
        {
            ThrowMarkerIoExceptionForStackTrace();
        }
        catch (IOException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("目印の例外が送出されませんでした。");
    }

    /// <summary>
    /// 相対パスから、その直上の親までの祖先パスを浅い順に列挙する（項目自身は含まない）。
    /// FixtureSpec の同名の処理と同じ規則だが、本番コードの実装に依存しないよう検証側で独立に持つ。
    /// </summary>
    private static IEnumerable<string> EnumerateAncestorRelativePaths(string relativePath)
    {
        var segments = relativePath.Split('\\');
        for (int depth = 1; depth < segments.Length; depth++)
        {
            yield return string.Join("\\", segments, 0, depth);
        }
    }

    /// <summary>
    /// 標準出力から「&lt;見出し&gt;: N 件」の行と、それに続く「  - &lt;値&gt;（…）」の明細を取り出す。
    /// 見出しがちょうど1行あること、件数の表記と明細の件数が一致することまで確かめる。
    /// 明細の値は、末尾の丸括弧（補足情報）を取り除いた部分とする。
    /// </summary>
    private static List<string> ExtractReportItems(string stdOut, string headerPrefix)
    {
        var lines = stdOut.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var headerIndexes = Enumerable.Range(0, lines.Count)
            .Where(i => lines[i].StartsWith(headerPrefix, StringComparison.Ordinal))
            .ToList();

        SelfAssert.That(
            headerIndexes.Count == 1,
            $"標準出力に '{headerPrefix}' で始まる行がちょうど1行ありません（実際: {headerIndexes.Count} 行）。標準出力: {stdOut}");

        string countText = lines[headerIndexes[0]].Substring(headerPrefix.Length).Replace(" 件", string.Empty).Trim();
        SelfAssert.That(
            int.TryParse(countText, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int declaredCount),
            $"'{headerPrefix}' の行から件数を読み取れません（実際の表記: '{countText}'）。標準出力: {stdOut}");

        var items = new List<string>();
        for (int i = headerIndexes[0] + 1; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("  - ", StringComparison.Ordinal))
            {
                break;
            }

            string body = lines[i].Substring("  - ".Length);
            int parenIndex = body.LastIndexOf('（');
            items.Add(parenIndex >= 0 ? body.Substring(0, parenIndex) : body);
        }

        SelfAssert.That(
            declaredCount == items.Count,
            $"'{headerPrefix}' の件数の表記（{declaredCount} 件）と明細の件数（{items.Count} 件）が一致しません。標準出力: {stdOut}");

        return items;
    }

    /// <summary>
    /// 報告された一覧が、期待する一覧と過不足なく一致することを照合する。
    /// 余分（別の分類の項目を混ぜている）と不足（黙って落としている）の双方を検出する。
    /// </summary>
    private static void AssertReportedSetEquals(string label, IReadOnlyList<string> expected, IReadOnlyList<string> reported, string stdOut)
    {
        var expectedSet = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
        var reportedSet = new HashSet<string>(reported, StringComparer.OrdinalIgnoreCase);

        var missing = expectedSet.Where(p => !reportedSet.Contains(p)).ToList();
        var extra = reportedSet.Where(p => !expectedSet.Contains(p)).ToList();

        SelfAssert.That(
            missing.Count == 0,
            $"{label}に列挙されるべき項目が報告されていません: {string.Join(" / ", missing)}。標準出力: {stdOut}");
        SelfAssert.That(
            extra.Count == 0,
            $"{label}に、そこへ含めてはならない項目が報告されています: {string.Join(" / ", extra)}。標準出力: {stdOut}");
        SelfAssert.That(
            reported.Count == expectedSet.Count,
            $"{label}の件数が期待と一致しません（期待: {expectedSet.Count} 件、実際: {reported.Count} 件）。標準出力: {stdOut}");
    }

    /// <summary>
    /// 与えられたパスと同じ場所を指しつつ、正規化で消える冗長な表記（&lt;実在しないフォルダ&gt;\..）を
    /// 途中に挟んだ綴りを組み立てる。文字列そのままの長さと正規化後の長さを必ず食い違わせるためのヘルパー。
    /// 挟むフォルダは正規化の時点で消えるため、実際に作成されることはない。
    /// </summary>
    private static string BuildRedundantSpelling(string path)
    {
        string parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"親フォルダを取り出せないパスです: {path}");
        string name = Path.GetFileName(path);

        return Path.Combine(parent, "gb_red_" + Guid.NewGuid().ToString("N").Substring(0, 8), "..", name);
    }

    /// <summary>
    /// 期待値テキストのヘッダ項目「# キー: 値」の値を取り出す。失敗のメッセージで実際の記録内容を示すために使う。
    /// 該当行がなければ、その旨を表す文字列を返す（この関数自体は検証を失敗させない）。
    /// </summary>
    private static string ExtractGoldenHeaderValue(string text, string key)
    {
        string prefix = "# " + key + ": ";

        var values = text
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line.Substring(prefix.Length))
            .ToList();

        return values.Count == 0 ? $"（'{key}' の行がありません）" : string.Join(" / ", values);
    }

    /// <summary>
    /// 標準出力の「基準フォルダ: &lt;パス&gt;」の行から、実際に使われた基準フォルダのパスを取り出す。
    /// 行が1行だけ存在することまで確かめ、取り違えを防ぐ。
    /// </summary>
    private static string ExtractReportedBaseFolder(string stdOut)
    {
        const string Marker = "基準フォルダ: ";

        var values = stdOut
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith(Marker, StringComparison.Ordinal))
            .Select(line => line.Substring(Marker.Length))
            .ToList();

        SelfAssert.That(
            values.Count == 1,
            $"標準出力に '基準フォルダ: ' の行がちょうど1行ありません（実際: {values.Count} 行）。標準出力: {stdOut}");
        SelfAssert.That(
            !string.IsNullOrWhiteSpace(values[0]),
            $"標準出力の '基準フォルダ: ' の値が空です。標準出力: {stdOut}");

        return values[0];
    }

    /// <summary>
    /// 指定した置き場の下に、実効絶対パス長（Path.GetFullPath 後の文字数）がちょうど targetLength になる
    /// 基準フォルダのパスを組み立てる。検証側で独立に長さを調整するためのヘルパーであり、
    /// 本番実装（Program.BuildDefaultRoot）には依存しない。フォルダ自体はまだ作成しない。
    /// </summary>
    private static string BuildRootWithEffectiveLength(string placementDirectory, int targetLength)
    {
        string probe = Path.Combine(placementDirectory, "gb_fix_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        int padding = targetLength - Path.GetFullPath(probe).Length;

        SelfAssert.That(
            padding >= 0,
            $"置き場 '{placementDirectory}' では実効絶対パス長を{targetLength}文字に収められません（最短 {Path.GetFullPath(probe).Length} 文字）。");

        string root = probe + new string('x', padding);
        SelfAssert.That(
            Path.GetFullPath(root).Length == targetLength,
            $"検証用の基準フォルダの実効絶対パス長が{targetLength}文字になりません（実際: {Path.GetFullPath(root).Length} 文字）。");

        return root;
    }

    /// <summary>
    /// 期待値テキストのヘッダ項目「# キー: 値」の値だけを差し替える。該当行がちょうど1行あることを確かめる。
    /// </summary>
    private static string ReplaceGoldenHeaderValue(string text, string key, string newValue)
    {
        string prefix = "# " + key + ": ";
        string[] lines = text.Split('\n');
        int replaced = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                lines[i] = prefix + newValue;
                replaced++;
            }
        }

        SelfAssert.That(
            replaced == 1,
            $"期待値テキストのヘッダ項目 '{key}' の行がちょうど1行見つかりませんでした（実際: {replaced} 行）。");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// %TEMP% 直下の gb_* の項目（検証用の一時フォルダ・ファイルの規約上の接頭辞）を列挙する。
    /// </summary>
    private static List<string> SnapshotTempGbEntries()
    {
        return Directory.GetFileSystemEntries(Path.GetTempPath(), "gb_*").ToList();
    }

    /// <summary>
    /// 事前に取った gb_* の一覧と比べて、許可した出力先以外に新たな gb_* の項目が残っていないことを照合する。
    /// 他の検証が残した既存の残留物で誤って失敗しないよう、差分だけを見る。
    /// </summary>
    private static void AssertNoNewTempGbEntries(string context, IReadOnlyCollection<string> entriesBefore, params string[] allowedPaths)
    {
        var before = new HashSet<string>(entriesBefore, StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(allowedPaths, StringComparer.OrdinalIgnoreCase);
        var leftovers = SnapshotTempGbEntries()
            .Where(p => !before.Contains(p) && !allowed.Contains(p))
            .ToList();

        SelfAssert.That(leftovers.Count == 0, $"{context}: 実行後に %TEMP% に gb_* の残留物があります: {string.Join(", ", leftovers)}");
    }

    /// <summary>
    /// ビルド済みの GoldenBaseline.exe を子プロセスとして起動し、終了コードと標準出力・標準エラーを取得する。
    /// 現在実行中のプロセス自身の実行ファイルパスを再利用するため、ビルド構成（Debug/Release）や
    /// 起動方法（直接実行 / dotnet run 経由）によらず、常に実際に動いている実行ファイルを起動できる。
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunGoldenBaselineProcess(params string[] arguments)
    {
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        SelfAssert.That(!string.IsNullOrWhiteSpace(exePath), "現在実行中の実行ファイルのパスを取得できませんでした。");

        var psi = new ProcessStartInfo(exePath!)
        {
            // net48 には ProcessStartInfo.ArgumentList が存在しないため、Arguments に
            // 引数ごとに二重引用符で囲んだ文字列を組み立てて渡す（引数はパスのみで
            // 二重引用符自体を含まないため、単純な囲み方で安全に扱える）。
            Arguments = string.Join(" ", arguments.Select(a => "\"" + a + "\"")),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // 子プロセス（Program.Main）は標準出力をUTF-8（BOMなし）に固定している。
            // 読み取り側のエンコーディングをそれに合わせないと、既定のコードページ解決に
            // 依存して文字化けする（実測で確認済み）。
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var process = Process.Start(psi))
        {
            SelfAssert.That(process != null, "GoldenBaseline のサブプロセスを起動できませんでした。");

            string stdOut = process!.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            bool exited = process.WaitForExit(60000);

            SelfAssert.That(exited, "GoldenBaseline のサブプロセスが60秒以内に終了しませんでした。");

            return (process.ExitCode, stdOut, stdErr);
        }
    }

    /// <summary>
    /// 期待値ファイルに記録されるべきクラスタサイズを、検証側が独立に実測する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 空の一時フォルダを %TEMP% 直下に作り（期待値生成で使う基準フォルダと同一ボリュームになる）、
    /// <see cref="ScanRunner"/> に物理サイズ換算ありで走査させて、その実測値を取り出す。
    /// リテラル（4096）で決め打ちすると、クラスタサイズの異なるボリュームでは壊れるうえ、
    /// 「Program が実測値をヘッダへ渡しているか」という配線そのものを確かめられない。
    /// </para>
    /// <para>
    /// 測定用のフォルダは呼び出しの内側で必ず削除するため、残留物の照合（<see cref="AssertNoNewTempGbEntries"/>）
    /// には影響しない。
    /// </para>
    /// </remarks>
    private static long MeasureClusterSizeThroughScanRunner()
    {
        string probeRoot = Path.Combine(Path.GetTempPath(), "gb_clu_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(probeRoot);

        try
        {
            return new ScanRunner().Run(probeRoot, usePhysicalSize: true).ClusterSizeInBytes;
        }
        finally
        {
            try
            {
                Directory.Delete(probeRoot, recursive: true);
            }
            catch
            {
                // 削除に失敗した場合は、呼び出し側の残留物の照合が検出する。
            }
        }
    }

    /// <summary>
    /// CLI 検証用の一時的な期待値ファイルパスを組み立てる。ファイル自体はまだ作成しない。
    /// </summary>
    private static string CreateTempCliGoldenFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "gb_cli_" + Guid.NewGuid().ToString("N") + ".golden.txt");
    }

    /// <summary>
    /// 出現回数を数える単純なヘルパー（正規表現を使わず、既存の依存方針を保つ）。
    /// </summary>
    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    /// <summary>
    /// 反復生成の同一性（要件2.3、2026-09-15決定）を検証するための除外結果。
    /// </summary>
    private readonly struct GeneratedAtExclusionResult
    {
        /// <summary>'# GeneratedAt:' の行を除いた残りの内容（UTF-8・BOMなし）。</summary>
        public byte[] FilteredBytes { get; }

        /// <summary>除外した '# GeneratedAt:' で始まる行の件数。</summary>
        public int GeneratedAtLineCount { get; }

        /// <summary>除外後に残った、GeneratedAt以外のヘッダ行（'# ' で始まる行）の件数。</summary>
        public int OtherHeaderLineCount { get; }

        /// <summary>除外後に残った、ヘッダでも空行でもないエントリ行の件数。</summary>
        public int EntryLineCount { get; }

        public GeneratedAtExclusionResult(byte[] filteredBytes, int generatedAtLineCount, int otherHeaderLineCount, int entryLineCount)
        {
            FilteredBytes = filteredBytes;
            GeneratedAtLineCount = generatedAtLineCount;
            OtherHeaderLineCount = otherHeaderLineCount;
            EntryLineCount = entryLineCount;
        }
    }

    /// <summary>
    /// 期待値ファイルのバイト列から、行頭が '# GeneratedAt:' である行だけを取り除く
    /// （design.md Data Models「Consistency &amp; Integrity」: 生成日時の行を除いて反復生成の同一性を判定する）。
    /// 本体（GoldenSerializer / Program）の実装には一切依存せず、この検証コード自身が独立に除外処理を行う。
    /// 除外の範囲が広すぎないことを呼び出し側で照合できるよう、除外行数・残存ヘッダ行数・残存エントリ行数も返す。
    /// </summary>
    private static GeneratedAtExclusionResult ExcludeGeneratedAtLine(byte[] originalBytes)
    {
        const string GeneratedAtLinePrefix = "# GeneratedAt:";
        const string HeaderLinePrefix = "# ";

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        string content = utf8.GetString(originalBytes);

        // 書き出しはLF固定（要件1.4・design.md Technology Stack）だが、比較対象の内部処理としては
        // 保守的に扱い、行末の断片化のみで判定が揺れないよう単純に '\n' で分割する。
        string[] lines = content.Split('\n');

        var keptLines = new List<string>();
        int generatedAtLineCount = 0;
        int otherHeaderLineCount = 0;
        int entryLineCount = 0;

        foreach (var line in lines)
        {
            // 行の除外は、行頭が '# GeneratedAt:' の行だけを対象にする（他のヘッダ行やエントリ行を
            // 巻き込まないようにするための唯一の判定条件）。
            if (line.StartsWith(GeneratedAtLinePrefix, StringComparison.Ordinal))
            {
                generatedAtLineCount++;
                continue;
            }

            keptLines.Add(line);

            if (line.StartsWith(HeaderLinePrefix, StringComparison.Ordinal))
            {
                otherHeaderLineCount++;
            }
            else if (line.Length > 0)
            {
                entryLineCount++;
            }
        }

        byte[] filteredBytes = utf8.GetBytes(string.Join("\n", keptLines));
        return new GeneratedAtExclusionResult(filteredBytes, generatedAtLineCount, otherHeaderLineCount, entryLineCount);
    }
}
