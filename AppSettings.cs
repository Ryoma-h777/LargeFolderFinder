using System;
using System.IO;
using MessagePack;

namespace LargeFolderFinder
{
    /// <summary>
    /// アプリケーション設定クラス（MessagePack形式で保存）
    /// </summary>
    [MessagePackObject]
    public class AppSettings
    {
        // LZ4圧縮を有効化（データサイズを50-70%削減）
        private static readonly MessagePackSerializerOptions LZ4Options =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        /// <summary>言語設定（空文字列の場合はOS言語を使用）</summary>
        [Key(0)]
        public string Language { get; set; } = "";

        /// <summary>レイアウトモード（Vertical/Horizontal）</summary>
        [Key(1)]
        public AppConstants.LayoutType LayoutMode { get; set; } = AppConstants.LayoutType.Vertical;

        /// <summary>アクティブなセッションファイル名のリスト</summary>
        [Key(2)]
        public string[] SessionFileNames { get; set; } = Array.Empty<string>();

        /// <summary>選択中のタブインデックス</summary>
        [Key(3)]
        public int SelectedIndex { get; set; } = 0;

        // ウィンドウ位置とサイズ
        [Key(4)]
        public double WindowTop { get; set; } = double.NaN;

        [Key(5)]
        public double WindowLeft { get; set; } = double.NaN;

        [Key(6)]
        public double WindowWidth { get; set; } = double.NaN;

        [Key(7)]
        public double WindowHeight { get; set; } = double.NaN;

        /// <summary>ウィンドウ状態（0: Normal, 1: Minimized, 2: Maximized）</summary>
        [Key(8)]
        public int WindowState { get; set; } = 0;

        /// <summary>フォントサイズ</summary>
        [Key(9)]
        public double FontSize { get; set; } = 16.0;

        private static string SettingsFilePath =>
            Path.Combine(AppConstants.AppDataDirectory, AppConstants.SettingsFileName);

        /// <summary>
        /// 設定ファイルを読み込む
        /// </summary>
        public static AppSettings? Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    byte[] bytes = File.ReadAllBytes(SettingsFilePath);
                    return MessagePackSerializer.Deserialize<AppSettings>(bytes, LZ4Options);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("設定ファイルの読み込みに失敗しました。", ex);
            }
            return null;
        }

        /// <summary>
        /// 設定ファイルを保存する
        /// </summary>
        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppConstants.AppDataDirectory))
                {
                    Directory.CreateDirectory(AppConstants.AppDataDirectory);
                }

                byte[] bytes = MessagePackSerializer.Serialize(this, LZ4Options);
                File.WriteAllBytes(SettingsFilePath, bytes);
            }
            catch (Exception ex)
            {
                Logger.Log("設定ファイルの保存に失敗しました。", ex);
            }
        }
    }
}
