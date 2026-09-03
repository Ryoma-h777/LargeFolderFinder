using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LargeFolderFinder.GoldenBaseline.Fixture;

/// <summary>
/// 読み取り権限のないフォルダの作成（付与）と解除を行う契約（design.md: Fixture/AccessControlGate）。
/// FixtureBuilder（タスク3.3）が要件3.4の境界条件（読み取り権限のないフォルダ）を
/// フィクスチャとして生成する際に利用する。
/// </summary>
public interface IAccessControlGate
{
    /// <summary>
    /// 実行ユーザー自身に対して、指定フォルダの読み取り（列挙・読み取り）を拒否する Deny ACE を付与する。
    /// 付与後、実行ユーザーによる当該フォルダの列挙は拒否される
    /// （design.md: AccessControlGate の Postconditions）。
    /// </summary>
    /// <param name="directoryPath">対象フォルダのパス。実在している必要がある。</param>
    void DenyRead(string directoryPath);

    /// <summary>
    /// <see cref="DenyRead"/> が付与した Deny ACE を除去し、読み取りを再び可能にする。
    /// <see cref="DenyRead"/> を適用していないフォルダに対して呼び出しても安全に、
    /// 何も変更せずに戻る（design.md: AccessControlGate の Invariants）。
    /// </summary>
    /// <param name="directoryPath">対象フォルダのパス。実在している必要がある。</param>
    void RestoreRead(string directoryPath);
}

/// <summary>
/// <see cref="IAccessControlGate"/> の実装。
/// 実行ユーザー自身の SID に対する Deny ACE の付与・除去のみを行い、管理者権限を必要としない
/// （research.md「権限のないフォルダの生成と後始末」で非昇格ユーザーによる実測済み）。
/// アクセス制御 API（<see cref="System.Security.AccessControl"/> / <see cref="System.Security.Principal"/>）を
/// 直接扱うのはこのファイルに限定する。.NET Framework から .NET 10 への移行で
/// <c>DirectorySecurity</c> の取得・設定方法が <c>FileSystemAclExtensions</c> 経由に変わるが、
/// その差分をこの部品の内側だけに閉じ込めることが、本部品を独立させている理由である
/// （design.md: Migration Strategy）。
/// </summary>
public sealed class AccessControlGate : IAccessControlGate
{
    /// <summary>
    /// 拒否する権利の組み合わせ。列挙・読み取りに関わる5つの権利をすべて拒否する
    /// （research.md: ListDirectory / ReadData / ReadAttributes / ReadExtendedAttributes / ExecuteFile）。
    /// </summary>
    private const FileSystemRights DeniedRights =
        FileSystemRights.ListDirectory |
        FileSystemRights.ReadData |
        FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes |
        FileSystemRights.ExecuteFile;

    /// <inheritdoc />
    public void DenyRead(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new ArgumentException("フォルダのパスが空です。", nameof(directoryPath));
        }

        // DirectorySecurity を取得 → ルールを追加 → 書き戻す、という流れで付与する（research.md）。
        DirectorySecurity security = Directory.GetAccessControl(directoryPath);
        security.AddAccessRule(CreateDenyRule());
        Directory.SetAccessControl(directoryPath, security);
    }

    /// <inheritdoc />
    public void RestoreRead(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new ArgumentException("フォルダのパスが空です。", nameof(directoryPath));
        }

        // オブジェクトの所有者は DACL の内容に関わらず READ_CONTROL と WRITE_DAC を暗黙に持つため、
        // Deny ACE を付与していない（あるいは既に解除済みの）フォルダでも取得・書き戻しは常に成功する
        // （research.md実測）。
        DirectorySecurity security = Directory.GetAccessControl(directoryPath);

        // DenyRead が付与するルールと全く同じ内容のルールを組み立てて除去を試みる。
        // 一致するルールが存在しない場合、RemoveAccessRule は何も変更せず false を返すだけなので、
        // DenyRead を適用していないフォルダに対しても安全に呼び出せる（design.md: Invariants）。
        security.RemoveAccessRule(CreateDenyRule());
        Directory.SetAccessControl(directoryPath, security);
    }

    /// <summary>
    /// DenyRead が付与し、RestoreRead が除去する Deny ACE を組み立てる。
    /// フォルダ自身とその配下の両方に適用されるよう、コンテナ・オブジェクトの両方の
    /// 継承フラグを付与する（research.md: ContainerInherit | ObjectInherit）。
    /// </summary>
    private static FileSystemAccessRule CreateDenyRule()
    {
        SecurityIdentifier currentUserSid = GetCurrentUserSid();

        return new FileSystemAccessRule(
            currentUserSid,
            DeniedRights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny);
    }

    /// <summary>
    /// 現在実行中のユーザー自身の SID を取得する。管理者権限は不要
    /// （research.md「権限のないフォルダの生成と後始末」で非昇格ユーザーによる実測済み）。
    /// </summary>
    private static SecurityIdentifier GetCurrentUserSid()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            SecurityIdentifier? sid = identity.User;
            if (sid is null)
            {
                throw new InvalidOperationException("実行ユーザーの SID を取得できませんでした。");
            }

            return sid;
        }
    }
}
