using System.Collections.Generic;
using System.Threading;

namespace LargeFolderFinder
{
    /// <summary>
    /// 最新の要求だけを有効にする取り消しの部品。
    /// 新しい要求を始めると前の要求を取り消し、渡された取り消しの通知が最新の要求のものかを判定できる。
    /// スレッドセーフで、インスタンスどうしは互いに影響しない（タブごとに1つ持つ）
    /// </summary>
    /// <remarks>
    /// 不変条件: 最新の要求の通知だけが取り消されていない。
    /// 取り消した <see cref="CancellationTokenSource"/> は、取り消した直後ではなく次の <see cref="Begin"/> で破棄する。
    /// 取り消された要求がまだ通知を見ている間に破棄しないための猶予である
    /// </remarks>
    public sealed class LatestOnlyCancellation
    {
        /// <summary>状態の入れ替えを1つずつ行うための鍵</summary>
        private readonly object _sync = new object();

        /// <summary>最新の要求の取り消し元。進行中の要求が無ければ null</summary>
        private CancellationTokenSource? _current;

        /// <summary>取り消し済みで、次の Begin で破棄する取り消し元</summary>
        private readonly List<CancellationTokenSource> _canceled = new List<CancellationTokenSource>();

        /// <summary>
        /// 前の要求を取り消し、新しい要求の取り消しの通知を返す。スレッドセーフ
        /// </summary>
        /// <returns>新しい要求の取り消しの通知</returns>
        public CancellationToken Begin()
        {
            var next = new CancellationTokenSource();
            lock (_sync)
            {
                // 前回までに取り消した取り消し元を破棄する（取り消しの判定は破棄の後も読める）
                foreach (var source in _canceled)
                {
                    source.Dispose();
                }
                _canceled.Clear();

                // 取り消しと入れ替えを同じ鍵の下で行い、「最新が2つある」状態を作らない
                CancelCurrentLocked();
                _current = next;
                return next.Token;
            }
        }

        /// <summary>
        /// 通知が最新の要求のものであり、取り消されていなければ true を返す
        /// </summary>
        /// <param name="token">判定する取り消しの通知</param>
        /// <returns>最新で、かつ取り消されていなければ true</returns>
        public bool IsLatest(CancellationToken token)
        {
            lock (_sync)
            {
                // 同じ取り消し元から得た通知どうしは等しいと判定される
                return _current != null
                    && token == _current.Token
                    && !token.IsCancellationRequested;
            }
        }

        /// <summary>
        /// 進行中の要求を取り消す（タブを閉じるときなど）。この後の Begin は通常どおり使える
        /// </summary>
        public void CancelAll()
        {
            lock (_sync)
            {
                CancelCurrentLocked();
            }
        }

        /// <summary>
        /// 最新の要求を取り消し、破棄の待ちに回す。鍵を持った状態で呼ぶこと
        /// </summary>
        private void CancelCurrentLocked()
        {
            if (_current == null) return;

            // 取り消しの通知に登録された処理はここで同期的に走る。鍵は同じスレッドから再び取れるため、
            // 登録された処理からこの部品を呼んでも止まらない
            _current.Cancel();
            _canceled.Add(_current);
            _current = null;
        }
    }
}
