using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using LargeFolderFinder.GoldenBaseline.Compare;
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
        RegisterGoldenSerializerChecks(runner);
        RegisterBaselineComparerChecks(runner);
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
}
