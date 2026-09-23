using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LargeFolderFinder
{
    /// <summary>
    /// 詳しい人向けの高度な設定を管理するクラス (Config.yaml)
    /// </summary>
    public class Config
    {
        public int MaxDepthForCount { get; set; } = 3;
        public bool UseParallelScan { get; set; } = true;

        /// <summary>
        /// 走査の並列度（ワーカー数）。0 は自動で、対象がネットワークかローカルかで既定値が変わる。
        /// 1 以上ならその値に固定する（大きすぎる値は上限に丸める）。
        /// <see cref="UseParallelScan"/> が false のときは、この値に関わらず逐次で走査する。
        /// </summary>
        public int ScanThreads { get; set; } = 0;
        public bool SkipFolderCount { get; set; } = false;
        public bool UsePhysicalSize { get; set; } = true;
        public int OldDataThresholdDays { get; set; } = 30;

        private static Config? _instance;
        public static Config Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        // 読み込みの失敗の記録を守る鍵（起動時・走査開始時・バインディング経由の読み込みが別のスレッドから来うる）
        private static readonly object _loadErrorLock = new object();

        // 直近の読み込みの失敗の理由。成功なら null
        private static string? _lastLoadError;

        // 利用者に通知済みの失敗の理由。同じ内容の失敗を繰り返し通知しないために使う。読み込みに成功したら消す
        private static string? _notifiedLoadError;

        private static string ConfigFilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.ConfigFileName);

        /// <summary>
        /// 直近の読み込みの失敗の理由。直近の読み込みが成功していれば null。
        /// </summary>
        public static string? LastLoadError
        {
            get
            {
                lock (_loadErrorLock)
                {
                    return _lastLoadError;
                }
            }
        }

        /// <summary>
        /// 未通知の読み込みの失敗があれば、その理由を返して通知済みにする。
        /// 同じ理由の失敗は、読み込みに成功するまで再び取り出せない。
        /// </summary>
        /// <param name="error">未通知の失敗の理由。無ければ空文字列。</param>
        /// <returns>未通知の失敗があれば true。</returns>
        public static bool TryTakeUnnotifiedError(out string error)
        {
            lock (_loadErrorLock)
            {
                if (_lastLoadError != null && !string.Equals(_lastLoadError, _notifiedLoadError, StringComparison.Ordinal))
                {
                    _notifiedLoadError = _lastLoadError;
                    error = _lastLoadError;
                    return true;
                }
            }

            error = string.Empty;
            return false;
        }

        /// <summary>
        /// 既定のパス（実行ファイルと同じ場所の Config.txt）から設定を読み込む。
        /// </summary>
        public static Config Load() => LoadFrom(ConfigFilePath);

        /// <summary>
        /// 指定したパスから設定を読み込む。
        /// 解析に失敗したら既定の設定を返し、内容と理由をログに記録して直近の失敗として保持する（壊れたファイルは上書きしない）。
        /// ファイルが無いときは、そのパスに既定の設定を書き出して返す。これは失敗として扱わない。
        /// 静的な現在の設定（<see cref="Instance"/>）は差し替えない。
        /// </summary>
        /// <param name="path">読み込む設定ファイルのパス。</param>
        public static Config LoadFrom(string path)
        {
            if (!File.Exists(path))
            {
                var defaultConfig = new Config();
                defaultConfig.SaveTo(path);
                RecordLoadSuccess();
                return defaultConfig;
            }

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(PascalCaseNamingConvention.Instance)
                        .Build();
                    var config = deserializer.Deserialize<Config>(sr);

                    // 空のファイルは解析できる空の文書なので、従来どおり既定の設定で成功として扱う
                    RecordLoadSuccess();
                    return config ?? new Config();
                }
            }
            catch (Exception ex)
            {
                string reason = DescribeLoadError(ex);
                Logger.Log($"設定ファイルを解析できないため、既定の設定で続けます: {path}（{reason}）", ex);
                lock (_loadErrorLock)
                {
                    _lastLoadError = reason;
                }
                return new Config();
            }
        }

        /// <summary>
        /// 読み込みの失敗の理由を、利用者が直す箇所を探せる文字列にする。
        /// YAML の解析の失敗なら位置を添え、外側の例外の文言だけでは中身が分からないときは内側の例外の文言も添える。
        /// </summary>
        /// <param name="ex">読み込みで捕捉した例外。</param>
        private static string DescribeLoadError(Exception ex)
        {
            string reason = ex.Message;

            Exception innermost = ex;
            while (innermost.InnerException != null)
            {
                innermost = innermost.InnerException;
            }
            if (!ReferenceEquals(innermost, ex) && !string.IsNullOrWhiteSpace(innermost.Message))
            {
                reason += $" ({innermost.Message})";
            }

            if (ex is YamlDotNet.Core.YamlException yamlException)
            {
                reason = $"{yamlException.Start.Line}行目 {yamlException.Start.Column}列目: {reason}";
            }

            return reason;
        }

        /// <summary>
        /// 読み込みの成功を記録する。直近の失敗と通知済みの記録を消す（次に同じ失敗が起きたら再び通知する）。
        /// </summary>
        private static void RecordLoadSuccess()
        {
            lock (_loadErrorLock)
            {
                _lastLoadError = null;
                _notifiedLoadError = null;
            }
        }

        /// <summary>
        /// 設定を既定のパス（実行ファイルと同じ場所の Config.txt）に保存する
        /// </summary>
        public void Save() => SaveTo(ConfigFilePath);

        /// <summary>
        /// 設定を指定したパスに保存する。失敗しても例外は外に出さず、ログに記録する。
        /// </summary>
        /// <param name="path">保存先のパス。</param>
        private void SaveTo(string path)
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .Build();
                string yaml = serializer.Serialize(this);
                File.WriteAllText(path, yaml);
            }
            catch (Exception ex)
            {
                Logger.Log($"設定ファイルを保存できませんでした: {path}", ex);
            }
        }
    }
}
