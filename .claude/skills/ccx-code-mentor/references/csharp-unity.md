# C#/Unity — GC回避と毎フレームFPS安定化

毎フレーム動くアプリ(ゲーム、リアルタイム描画)で「たまにカクつく」原因の筆頭が **GC(ガベージコレクション)** 。この文書は C#/Unity での教え方の材料集。

## 目次

1. GCの仕組みを初学者にどう教えるか
2. ヒープ確保が起きる「うっかりポイント」
3. 確保ゼロにする定石
4. struct と class の使い分け
5. Unity固有の注意点
6. 計測方法

---

## 1. GCの仕組みを初学者にどう教えるか

- **スタック**: 関数のローカル変数が置かれる領域。関数を抜けたら自動で消える。タダ同然に速い。
- **ヒープ**: `new` で確保する領域。誰がいつまで使うか分からないので、**GCという掃除係が定期的に全体を止めて片付ける**。
- 比喩: ヒープにゴミ(使い終わったオブジェクト)を撒き続けると、掃除係がゲームを**一時停止して**掃除を始める。これがカクつき(スパイク)の正体。
- 60FPSでは1フレームの持ち時間は**16.6ms**。フルGCは数ms〜数十msかかることがあり、1回走るだけでフレーム落ちする。
- 重要な帰結: **平均FPSは高いのにたまにカクつく**という症状は、ほぼ「毎フレームの小さなヒープ確保の蓄積 → 周期的なGC」を疑う。

## 2. ヒープ確保が起きる「うっかりポイント」

初学者が気づかずにループ/毎フレーム処理に書きがちなもの:

| 書きがちなコード | 何が起きるか | 代替 |
|---|---|---|
| `new List<T>()` を Update 内で | 毎フレーム確保 | フィールドに持って `Clear()` で再利用 |
| 文字列連結 `"HP: " + hp` | 毎回新しい string を確保 | 値が変わったときだけ更新 / StringBuilder 再利用 |
| LINQ (`Where`, `Select` 等) | 列挙子とラムダのクロージャを確保 | ホットパスでは普通の for に書き換え |
| ラムダで外の変数を掴む(クロージャ) | 隠れた class が確保される | 変数を掴まない static ラムダ、または for |
| ボクシング: `object obj = 42;` / 構造体を interface 経由で渡す | 値型がヒープに箱詰めされる | ジェネリクスで型を保つ |
| `foreach`(一部の古いコレクション) | 列挙子が class だと確保 | List<T> や配列の foreach は struct 列挙子なのでOK |
| params 配列、`string.Format` | 配列と文字列を確保 | ホットパスでは避ける |
| Unity: `GetComponent`、`Camera.main`(旧版)、`Instantiate/Destroy` 連発 | 検索コスト+確保 | Start でキャッシュ / オブジェクトプール |

教えるときは「`new` と書いた場所」だけでなく、**見た目に new がない隠れ確保**(文字列連結・LINQ・クロージャ・ボクシング)こそ落とし穴だと強調する。

## 3. 確保ゼロにする定石

1. **再利用**: コレクションはフィールドに持ち、毎フレーム `Clear()` して使い回す(`Clear` は容量を保持するので再確保されない)
2. **オブジェクトプール**: 弾・エフェクトなど頻繁に生成/破棄するものは、最初にまとめて作って使い回す(`Instantiate/Destroy` をやめる)。Unity には `UnityEngine.Pool.ObjectPool<T>` が標準である
3. **事前確保**: `new List<T>(capacity)` で最終サイズを指定し、成長時の内部再確保を防ぐ
4. **struct 化**: 小さくて寿命が短いデータは struct にすればスタック(または親の中)に置かれ、GC対象にならない
5. **Span<T> / stackalloc**: 一時バッファはヒープでなくスタックに取る(`Span<int> buf = stackalloc int[64];`)
6. **文字列は変化時のみ生成**: UIのスコア表示などは値が変わったフレームだけ文字列を作る

## 4. struct と class の使い分け

- **class**: ヒープに置かれ、参照で渡る。GC対象。
- **struct**: 値そのものがその場(スタック、配列の中)に置かれる。GC対象にならず、**配列にしたときメモリが連続する**のでキャッシュにも乗りやすい(→ optimization-fundamentals.md の局所性)。

指針: 16バイト程度以下・不変・寿命が短いデータは struct 向き。大きな struct のコピーコストや、ボクシングの罠(interface に入れると台無し)も併せて教える。

## 5. Unity固有の注意点

- **Update内での検索系API禁止**: `GetComponent`、`FindObjectOfType` は Start/Awake で1回だけ呼んで結果をフィールドにキャッシュ
- **物理は FixedUpdate、描画依存は Update**: フレームレート非依存の処理の置き場所を教える
- **距離比較は sqrMagnitude**: `Vector3.Distance` は平方根(重い)を含む。大小比較だけなら `(a-b).sqrMagnitude < r*r` で十分
- **さらに上のステップ**(+1の知識として紹介する候補): Jobs System + Burst Compiler(自動SIMD化までやってくれる)、ECS/DoTS(SoAレイアウトの徹底)、Incremental GC設定

## 6. 計測方法

- **Unity Profiler** の「GC Alloc」列: どの関数が毎フレーム何バイト確保しているかが見える。まずここで確保箇所を特定させる
- **Profile Analyzer** パッケージ: フレームスパイクの統計比較
- 純C#なら **BenchmarkDotNet** の `[MemoryDiagnoser]` で確保量を数値化
- 教える順番: 「1フレームあたりのGC Allocを0バイトに近づける」という明確なゴールを与えると初学者にも進捗が見えやすい

## 参考リンク集(回答に添えてよい実在URL)

- Unity マニュアル(日本語): https://docs.unity3d.com/ja/current/Manual/ — Profiler や最適化の公式解説
- Unity Learn パフォーマンス最適化: https://learn.unity.com/ — 公式チュートリアル
- .NET のガベージコレクション(Microsoft公式): https://learn.microsoft.com/ja-jp/dotnet/standard/garbage-collection/
- .NET のパフォーマンスTips(Microsoft公式): https://learn.microsoft.com/ja-jp/dotnet/framework/performance/performance-tips
- BenchmarkDotNet: https://benchmarkdotnet.org/
