using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
        RegisterLongPathChecks(runner);
        RegisterGoldenSerializerChecks(runner);
        RegisterBaselineComparerChecks(runner);
        RegisterFixtureSpecChecks(runner);
        RegisterAccessControlGateChecks(runner);
        RegisterFixtureBuilderChecks(runner);
        RegisterScanRunnerChecks(runner);
        RegisterGoldenProjectorChecks(runner);
        RegisterKnownIssueAnalyzerChecks(runner);
        RegisterProgramChecks(runner);
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
                var header = new GoldenHeader(1, "fixture-v1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, 4096L, true, Array.Empty<string>());

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
            var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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
                    "# FormatVersion: 1\n" +
                    "# BaseFolderLabel: fixture-v1\n" +
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
            formatVersion: 1,
            baseFolderLabel: "fixture-v1",
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
            formatVersion: 1,
            baseFolderLabel: "fixture-v1",
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
    private static GoldenDocument BuildComparerDocument(bool usePhysicalSize, IReadOnlyList<GoldenEntry> entries)
    {
        var header = new GoldenHeader(
            formatVersion: 1,
            baseFolderLabel: "fixture-v1",
            generatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            usePhysicalSize: usePhysicalSize,
            clusterSizeInBytes: usePhysicalSize ? 4096L : 0L,
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

        runner.Add("FixtureSpec.Standard が248文字を超えるフォルダを実際の文字数として持つ（要件3.1、ディレクトリ境界248文字）", () =>
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
                    $"LongPath トレイトを持つフォルダ '{folder.RelativePath}' の実際の文字数（{folder.RelativePath.Length}文字）が、ディレクトリ境界248文字を超えていません。");
            }
        });

        runner.Add("FixtureSpec.Standard が260文字を超えるファイルパスを実際の文字数として持つ（要件3.1、ファイルパス境界260文字）", () =>
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
                    $"LongPath トレイトを持つファイル '{file.RelativePath}' の実際の文字数（{file.RelativePath.Length}文字）が、ファイルパス境界260文字を超えていません。");
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

                SelfAssert.That(maxFolderLength > 248, $"検証対象の最長フォルダの相対パスが248文字を超えていません（実際: {maxFolderLength}文字）。");
                SelfAssert.That(maxFileLength > 260, $"検証対象の最長ファイルの相対パスが260文字を超えていません（実際: {maxFileLength}文字）。");

                // 対比: プレーンなパスでは260文字超のフォルダの実在確認自体ができないことを確認する。
                // これにより、上記の確認が拡張長パス経由でなければ通らない検証であることを保証する。
                var longFolderItem = FixtureSpec.Standard.Items
                    .First(i => i.Kind == GoldenEntryKind.Folder && i.Traits.Contains(FixtureTrait.LongPath) && i.RelativePath.Length > 248);
                string plainLongFolderPath = Path.Combine(root, longFolderItem.RelativePath);
                SelfAssert.That(
                    !Directory.Exists(plainLongFolderPath),
                    "対比検証: プレーンなパスで248文字超のフォルダが「存在する」と判定されました（拡張長パス経由の確認でなければ意味を持たない検証になっています）。");
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

        runner.Add("ScanRunner が走査結果に手を加えず、260文字を超える項目は現行版のまま走査結果に現れない（要件5.1, 5.5）", () =>
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

                // 境界を超える項目（LongPath トレイト かつ 実際の文字数境界超過）を、FixtureSpec の定義から
                // 機械的に抽出する。手作業の列挙はしない（tasks.md「手作業の注釈に頼らない」の趣旨に合わせる）。
                var overBoundaryItems = FixtureSpec.Standard.Items
                    .Where(i => i.Traits.Contains(FixtureTrait.LongPath))
                    .Where(i => (i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248)
                             || (i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260))
                    .ToList();

                SelfAssert.That(overBoundaryItems.Count > 0, "検証対象となる境界超過項目が定義から見つかりません（FixtureSpec.Standard の想定が変わった可能性があります）。");

                foreach (var item in overBoundaryItems)
                {
                    SelfAssert.That(
                        !map.ContainsKey(item.RelativePath),
                        $"既知の不具合により現れないはずの項目 '{item.RelativePath}'（{item.RelativePath.Length}文字）が走査結果に現れました。" +
                        "本体の挙動が変わった可能性があり、想定と異なるため報告が必要です。");
                }

                // 対比: 境界を超えない通常の項目（同じ長いパス連鎖の浅い階層）は正しく現れることを確認する。
                // これにより、上の不在確認が「そもそも何も走査できていない」誤りでないことを保証する。
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
                    formatVersion: 1,
                    baseFolderLabel: "fixture-v1",
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

        runner.Add("GoldenProjector が走査結果に手を加えず、260文字を超える項目は射影結果にも現れない（要件5.1）", () =>
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
                var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = projector.Project(outcome, header);
                var paths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

                // 境界を超える項目（LongPath トレイト かつ 実際の文字数境界超過）を、FixtureSpec の定義から機械的に抽出する。
                var overBoundaryItems = FixtureSpec.Standard.Items
                    .Where(i => i.Traits.Contains(FixtureTrait.LongPath))
                    .Where(i => (i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248)
                             || (i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260))
                    .ToList();

                SelfAssert.That(overBoundaryItems.Count > 0, "検証対象となる境界超過項目が定義から見つかりません（FixtureSpec.Standard の想定が変わった可能性があります）。");

                foreach (var item in overBoundaryItems)
                {
                    SelfAssert.That(
                        !paths.Contains(item.RelativePath),
                        $"既知の不具合により走査結果に現れないはずの項目 '{item.RelativePath}'（{item.RelativePath.Length}文字）が射影結果に現れました。" +
                        "GoldenProjector が走査結果に手を加えている（欠落を補完している）可能性があります。");
                }

                // 対比: 境界を超えない通常の項目（同じ長いパス連鎖の浅い階層）は射影結果に正しく現れることを確認する。
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
        runner.Add("KnownIssueAnalyzer が実フィクスチャの走査結果に対し、長いパスの項目を境界条件を理由とした既知の欠落として列挙する（要件5.2）", () =>
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
                var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
                var document = projector.Project(outcome, header);

                var observedPaths = new HashSet<string>(document.Entries.Select(e => e.RelativePath), StringComparer.Ordinal);

                // 走査で実際に観測されなかった項目を、FixtureSpec の定義から機械的に抽出する（手作業の列挙はしない）。
                var actuallyMissingItems = FixtureSpec.Standard.Items
                    .Where(i => !observedPaths.Contains(i.RelativePath))
                    .ToList();

                SelfAssert.That(actuallyMissingItems.Count > 0, "検証対象となる欠落項目が見つかりません（走査環境が想定と異なる可能性があります）。");

                // 欠落項目のうち LongPath トレイトを持つものが実在すること（248/260文字境界超過の実測、タスク3.1の教訓に合わせトレイトのラベルだけでなく実体も確認する）。
                var missingLongPathItems = actuallyMissingItems.Where(i => i.Traits.Contains(FixtureTrait.LongPath)).ToList();
                SelfAssert.That(missingLongPathItems.Count > 0, "欠落項目の中に LongPath トレイトを持つものが見つかりません。");
                SelfAssert.That(
                    missingLongPathItems.Any(i => i.Kind == GoldenEntryKind.Folder && i.RelativePath.Length > 248),
                    "248文字境界を超えるフォルダの欠落が見つかりません。");
                SelfAssert.That(
                    missingLongPathItems.Any(i => i.Kind == GoldenEntryKind.File && i.RelativePath.Length > 260),
                    "260文字境界を超えるファイルの欠落が見つかりません。");

                var analyzer = new KnownIssueAnalyzer();
                var findings = analyzer.Analyze(FixtureSpec.Standard, document);
                var findingsByPath = findings.ToDictionary(f => f.RelativePath, f => f.Trait, StringComparer.Ordinal);

                // 完了状態: 長いパスの項目が観測されなかった場合に、その境界条件（LongPath）を理由として既知の欠落として列挙される。
                foreach (var item in missingLongPathItems)
                {
                    SelfAssert.That(
                        findingsByPath.TryGetValue(item.RelativePath, out var trait),
                        $"長いパスの欠落項目 '{item.RelativePath}' が既知の欠落として列挙されていません。");
                    SelfAssert.That(
                        trait == FixtureTrait.LongPath,
                        $"'{item.RelativePath}' の既知の欠落の根拠が LongPath ではありません（実際: {trait}）。");
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
                var header = new GoldenHeader(1, "fixture-v1", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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
            var header = new GoldenHeader(1, "synthetic-known-issue-test", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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

            var header = new GoldenHeader(1, "synthetic-all-present", DateTimeOffset.UtcNow, false, 0L, true, Array.Empty<string>());
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

        runner.Add("generate がプロセスとして完走し、終了コード0を返し、既知の欠落（長いパス由来）が報告に含まれ、期待値ファイルが書き出される（要件2.1, 3.7, 5.2）", () =>
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
                // Standard フィクスチャは境界越えの長いパス項目を4件含み、現行版のバグにより
                // 走査から漏れることが tasks.md Implementation Notes（タスク4.3）で確定済み。
                SelfAssert.That(
                    result.StdOut.Contains("既知の欠落"),
                    $"generate の標準出力に既知の欠落の見出しが含まれません: {result.StdOut}");
                int longPathMentionCount = CountOccurrences(result.StdOut, "原因: LongPath");
                SelfAssert.That(
                    longPathMentionCount == 4,
                    $"既知の欠落として報告された LongPath 由来の件数が想定と異なります（実際: {longPathMentionCount} 件、想定: 4 件）。標準出力: {result.StdOut}");

                var document = new GoldenSerializer().Read(outPath);
                SelfAssert.That(document.Entries.Count > 0, "generate が書き出した期待値ファイルにエントリが1件もありません。");

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

        runner.Add("compare が差分ありで終了コード1を返す（要件4.2, 4.7）", () =>
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
                var serializer = new GoldenSerializer();
                var original = serializer.Read(goldenPath);
                var targetEntry = original.Entries.First(e => e.Kind == GoldenEntryKind.File);
                var tamperedEntries = original.Entries
                    .Select(e => ReferenceEquals(e, targetEntry)
                        ? new GoldenEntry(e.RelativePath, e.Kind, e.SizeInBytes + 1L)
                        : e)
                    .ToList();
                var tamperedDocument = new GoldenDocument(original.Header, tamperedEntries);
                serializer.Write(tamperedDocument, tamperedPath);

                var cmpResult = RunGoldenBaselineProcess("compare", "--golden", tamperedPath, "--root", cmpRoot);

                SelfAssert.That(cmpResult.ExitCode == 1, $"compare（差分あり想定）の終了コードが1ではありません（実際: {cmpResult.ExitCode}）。標準出力: {cmpResult.StdOut} 標準エラー: {cmpResult.StdErr}");
                SelfAssert.That(cmpResult.StdOut.Contains("判定: 差分あり"), $"compare の標準出力に差分ありの判定が含まれません: {cmpResult.StdOut}");
                SelfAssert.That(cmpResult.StdOut.Contains(targetEntry.RelativePath), $"compare の標準出力に改ざんした相対パスが含まれません: {cmpResult.StdOut}");
                SelfAssert.That(cmpResult.StdOut.Contains("SizeMismatch"), $"compare の標準出力にサイズ不一致の種別が含まれません: {cmpResult.StdOut}");
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
}
