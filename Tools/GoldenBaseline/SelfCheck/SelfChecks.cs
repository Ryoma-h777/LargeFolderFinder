using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LargeFolderFinder.GoldenBaseline.Io;
using LargeFolderFinder.GoldenBaseline.Model;

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
        RegisterLongPathChecks(runner);
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
                formatVersion: 1,
                baseFolderLabel: "fixture-v1",
                generatedAt: generatedAt,
                usePhysicalSize: true,
                clusterSizeInBytes: 4096L,
                fixtureComplete: false,
                fixtureOmissions: omissions);

            SelfAssert.That(header.FormatVersion == 1, "FormatVersion が設定した値と一致しません。");
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
            var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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
            var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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

        runner.Add("変換を通すと260文字を超えるフォルダの作成に成功し、変換を通さないと失敗する（要件3.1）", () =>
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

                // 2. 変換を通さないプレーンなパスでは、同じ深さのフォルダ作成が失敗する（対比）
                string plainName = new string('b', segmentLength);
                string plainPath = Path.Combine(root, plainName);
                SelfAssert.That(plainPath.Length > 260, $"対比用パスが260文字を超えていません（{plainPath.Length}文字）。");

                bool plainCreationFailed = false;
                try
                {
                    Directory.CreateDirectory(plainPath);
                }
                catch (PathTooLongException)
                {
                    plainCreationFailed = true;
                }
                catch (DirectoryNotFoundException)
                {
                    plainCreationFailed = true;
                }
                catch (IOException)
                {
                    plainCreationFailed = true;
                }

                SelfAssert.That(plainCreationFailed, "変換を通さないプレーンなパスでの260文字超フォルダ作成が、失敗せず成功してしまいました。");
                SelfAssert.That(!Directory.Exists(plainPath), "変換を通さないプレーンなパスにもかかわらずフォルダが実際に作成されています。");
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
}
