using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;

namespace LargeFolderFinder.GoldenBaseline.Scan;

/// <summary>
/// 本体の描画の取り消しの部品（<see cref="global::LargeFolderFinder.LatestOnlyCancellation"/>）と、
/// それを持つタブのデータ（<see cref="global::LargeFolderFinder.SessionData"/>）の保存を、決まった手順で呼んで
/// 観測した値だけを返す入口（scan-correctness 要件3.3, 6.1, 6.2, 6.3）。
/// 本体に触れる呼び出しを Scan 層に閉じ込めるためのものであり、判定は呼び出し側（自己検証）が行う。
/// </summary>
public static class RenderCancellationProbe
{
    /// <summary>
    /// 1つのインスタンスで <c>Begin</c> を2回呼び、1回目と2回目の通知の状態を返す。
    /// </summary>
    public static SequentialBeginOutcome RunTwoBegins()
    {
        var gate = new global::LargeFolderFinder.LatestOnlyCancellation();

        CancellationToken first = gate.Begin();
        bool firstLatestBeforeSecond = gate.IsLatest(first);
        bool firstCanceledBeforeSecond = first.IsCancellationRequested;

        CancellationToken second = gate.Begin();

        return new SequentialBeginOutcome(
            firstLatestBeforeSecond,
            firstCanceledBeforeSecond,
            FirstCanceledAfterSecond: first.IsCancellationRequested,
            FirstLatestAfterSecond: gate.IsLatest(first),
            SecondCanceled: second.IsCancellationRequested,
            SecondLatest: gate.IsLatest(second),
            DefaultTokenLatest: gate.IsLatest(CancellationToken.None));
    }

    /// <summary>
    /// 2つのインスタンスで交互に <c>Begin</c> を呼び、互いの通知が取り消されていないか、
    /// 相手の通知を最新と判定しないかを返す。
    /// </summary>
    public static IndependentInstancesOutcome RunTwoInstances()
    {
        var gateA = new global::LargeFolderFinder.LatestOnlyCancellation();
        var gateB = new global::LargeFolderFinder.LatestOnlyCancellation();

        CancellationToken tokenA = gateA.Begin();
        CancellationToken tokenB = gateB.Begin();

        return new IndependentInstancesOutcome(
            ACanceled: tokenA.IsCancellationRequested,
            ALatestInA: gateA.IsLatest(tokenA),
            BCanceled: tokenB.IsCancellationRequested,
            BLatestInB: gateB.IsLatest(tokenB),
            ALatestInB: gateB.IsLatest(tokenA),
            BLatestInA: gateA.IsLatest(tokenB));
    }

    /// <summary>
    /// <c>Begin</c> の後に <c>CancelAll</c> を呼び、最新の通知の状態を返す。さらにその後の <c>Begin</c> が使えるかも返す。
    /// </summary>
    public static CancelAllOutcome RunCancelAll()
    {
        var gate = new global::LargeFolderFinder.LatestOnlyCancellation();

        CancellationToken latest = gate.Begin();
        gate.CancelAll();
        bool latestCanceled = latest.IsCancellationRequested;
        bool latestStillLatest = gate.IsLatest(latest);

        // まとめて取り消した後も、次の要求は通常どおり始められること
        CancellationToken next = gate.Begin();

        return new CancelAllOutcome(
            latestCanceled,
            latestStillLatest,
            NextCanceled: next.IsCancellationRequested,
            NextLatest: gate.IsLatest(next));
    }

    /// <summary>
    /// 1つのインスタンスに対し、<paramref name="threadCount"/> 本のスレッドから一斉に
    /// <paramref name="beginsPerThread"/> 回ずつ <c>Begin</c> を呼び、得た全通知のうち
    /// 取り消されていないものの数と、最新と判定されたものの数を返す。
    /// </summary>
    public static ConcurrentBeginOutcome RunConcurrentBegins(int threadCount, int beginsPerThread)
    {
        var gate = new global::LargeFolderFinder.LatestOnlyCancellation();
        var tokens = new ConcurrentBag<CancellationToken>();
        using var start = new Barrier(threadCount);

        Task[] workers = Enumerable.Range(0, threadCount).Select(_ => Task.Factory.StartNew(() =>
        {
            // 全スレッドが揃ってから一斉に始め、Begin どうしを確実に競合させる
            start.SignalAndWait();
            for (int i = 0; i < beginsPerThread; i++)
            {
                tokens.Add(gate.Begin());
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(workers);

        List<CancellationToken> all = tokens.ToList();
        return new ConcurrentBeginOutcome(
            TotalTokens: all.Count,
            UncanceledCount: all.Count(t => !t.IsCancellationRequested),
            LatestCount: all.Count(gate.IsLatest));
    }

    /// <summary>
    /// タブのデータの描画の取り消しを進行中の状態にしてから、本体のセッションの保存と同じ設定で
    /// メモリ上に保存して読み戻し、保存の中身と読み戻した後の取り消しの部品の状態を返す。
    /// 利用者の保存ファイルには触れない。
    /// </summary>
    public static SessionRoundTripOutcome RoundTripSession()
    {
        MessagePackSerializerOptions options = GetSessionFileOptions();
        var createdAt = new DateTime(2026, 9, 22, 12, 34, 56, DateTimeKind.Local);

        // 描画の取り消しを進行中にしたデータと、一度も使っていないデータ。保存する欄は同じ値にそろえる
        var used = new global::LargeFolderFinder.SessionData { CreatedAt = createdAt, Path = @"C:\probe", FilterText = "probe" };
        CancellationToken inFlight = used.RenderCancellation.Begin();
        var fresh = new global::LargeFolderFinder.SessionData { CreatedAt = createdAt, Path = @"C:\probe", FilterText = "probe" };

        byte[] usedBytes = MessagePackSerializer.Serialize(used, options);
        byte[] freshBytes = MessagePackSerializer.Serialize(fresh, options);

        global::LargeFolderFinder.SessionData restored =
            MessagePackSerializer.Deserialize<global::LargeFolderFinder.SessionData>(usedBytes, options);

        PropertyInfo? property = typeof(global::LargeFolderFinder.SessionData).GetProperty("RenderCancellation");
        bool hasIgnoreMember = property?.GetCustomAttribute<IgnoreMemberAttribute>() != null;
        bool hasKey = property?.GetCustomAttribute<KeyAttribute>() != null;

        global::LargeFolderFinder.LatestOnlyCancellation? restoredGate = restored.RenderCancellation;
        bool restoredUsable = false;
        bool inFlightLatestInRestored = false;
        if (restoredGate != null)
        {
            // 読み戻した部品は元の部品と別物であり、元の通知を最新と判定しないこと
            inFlightLatestInRestored = restoredGate.IsLatest(inFlight);
            CancellationToken token = restoredGate.Begin();
            restoredUsable = !token.IsCancellationRequested && restoredGate.IsLatest(token);
        }

        return new SessionRoundTripOutcome(
            PropertyFound: property != null,
            hasIgnoreMember,
            hasKey,
            BytesIndependentOfRenderState: usedBytes.AsSpan().SequenceEqual(freshBytes),
            RestoredGateIsNull: restoredGate == null,
            restoredUsable,
            inFlightLatestInRestored,
            RestoredPath: restored.Path,
            RestoredFilterText: restored.FilterText);
    }

    /// <summary>
    /// 本体のセッションの保存（<see cref="global::LargeFolderFinder.SessionFileManager"/>）が使う MessagePack の設定を、
    /// 反射で本体から取り出して返す。設定を写し書きすると本体の変更に追従できないため、本体の値そのものを使う。
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
}

/// <summary><see cref="RenderCancellationProbe.RunTwoBegins"/> の結果。</summary>
public sealed record SequentialBeginOutcome(
    bool FirstLatestBeforeSecond,
    bool FirstCanceledBeforeSecond,
    bool FirstCanceledAfterSecond,
    bool FirstLatestAfterSecond,
    bool SecondCanceled,
    bool SecondLatest,
    bool DefaultTokenLatest);

/// <summary><see cref="RenderCancellationProbe.RunTwoInstances"/> の結果。</summary>
public sealed record IndependentInstancesOutcome(
    bool ACanceled,
    bool ALatestInA,
    bool BCanceled,
    bool BLatestInB,
    bool ALatestInB,
    bool BLatestInA);

/// <summary><see cref="RenderCancellationProbe.RunCancelAll"/> の結果。</summary>
public sealed record CancelAllOutcome(
    bool LatestCanceled,
    bool LatestStillLatest,
    bool NextCanceled,
    bool NextLatest);

/// <summary><see cref="RenderCancellationProbe.RunConcurrentBegins"/> の結果。</summary>
public sealed record ConcurrentBeginOutcome(
    int TotalTokens,
    int UncanceledCount,
    int LatestCount);

/// <summary><see cref="RenderCancellationProbe.RoundTripSession"/> の結果。</summary>
public sealed record SessionRoundTripOutcome(
    bool PropertyFound,
    bool HasIgnoreMember,
    bool HasKey,
    bool BytesIndependentOfRenderState,
    bool RestoredGateIsNull,
    bool RestoredUsable,
    bool InFlightLatestInRestored,
    string RestoredPath,
    string RestoredFilterText);
