# C/C++/Rust — 低レイヤ最適化とコンパイル後コードの確認

ネイティブ言語での最適化指導の材料集。原理は optimization-fundamentals.md にあり、ここは言語固有の道具と書き方。

## 目次

1. コンパイル後のコードを見る(godbolt)
2. 最適化フラグの基本
3. ベクトル化を助ける書き方
4. 分岐ヒント(likely/unlikely)
5. メモリ確保のコントロール
6. Rust固有の要点

---

## 1. コンパイル後のコードを見る(godbolt)

**Compiler Explorer (https://godbolt.org)** は、書いたコードがどんなアセンブリに変換されたかをブラウザで即座に確認できる。初学者に「コンパイル後のコードを意識する」習慣をつけさせる最良の道具。

教えるときの最小の読み方(アセンブリ全部を読める必要はない):

- **ループがSIMD化されたか**: `xmm`(128bit)/`ymm`(256bit)/`zmm`(512bit)レジスタと `addps`、`mulps`、`vfmadd` などの **p**(packed)付き命令が出ていればベクトル化されている。`addss` など **ss**(scalar)だけならされていない
- **関数がインライン化されたか**: 呼び出し(`call`)が消えているか
- **計算が消えたか**: 定数畳み込みや「結果未使用の削除」で、測ろうとした処理ごと消えることがある(ベンチマークの罠)
- コンパイラの最適化レポートも使える: Clang `-Rpass=loop-vectorize -Rpass-missed=loop-vectorize`、GCC `-fopt-info-vec-all`。「なぜベクトル化しなかったか」を理由付きで教えてくれる

## 2. 最適化フラグの基本

| フラグ | 意味 |
|---|---|
| `-O2` | 標準の最適化。計測は必ずこれ以上で |
| `-O3` | 積極的ベクトル化を含む。大抵はこれ |
| `-march=native` | 実行するCPUの全命令(AVX2等)を使ってよいと許可。配布バイナリでは注意 |
| `-ffast-math` | 浮動小数の結合則を許しSIMD化が進むが、**計算結果が変わりうる**。意味を教えてから |
| Rust: `--release` | debugビルドは数十倍遅いことがある。計測前の必須確認事項 |
| Rust: `RUSTFLAGS="-C target-cpu=native"` | `-march=native` 相当 |

## 3. ベクトル化を助ける書き方

fundamentals の条件(依存なし・分岐少・連続アクセス)に加えて、C/C++固有:

- **エイリアスを断つ**: `void f(float* a, float* b)` では a と b が同じメモリを指す可能性があり、コンパイラはベクトル化を諦めることがある。C99 `restrict`(C++では `__restrict`)で「重ならない」と約束する

```c
void add(float* restrict out, const float* restrict a, const float* restrict b, int n) {
    for (int i = 0; i < n; i++)
        out[i] = a[i] + b[i];   // restrict のおかげで安心してSIMD化できる
}
```

- **ループ内の関数呼び出しを避ける/インライン化させる**(ヘッダ定義、`static inline`)
- **浮動小数の総和**: 加算順序が変わるため、コンパイラは指示なしにfloat和をベクトル化しない。`-ffast-math` か、明示的に複数アキュムレータに分ける
- どうしても自動でされない場合の次の段階: OpenMP `#pragma omp simd` → intrinsics(`<immintrin.h>`)→ ISPC。初学者にはまず「自動ベクトル化を引き出す書き方」までで十分と伝え、intrinsics は+1の知識として名前だけ紹介

## 4. 分岐ヒント(likely/unlikely)

「この if はほぼ一方にしか行かない」とコンパイラに伝えると、よく通る側を直線的に配置し(命令キャッシュ効率↑)、稀な側を遠くに追いやる。

```cpp
// C++20 標準
if (error) [[unlikely]] {
    handle_error();   // 稀にしか通らない → コードの端に配置される
}

// GCC/Clang (C でも使える)
if (__builtin_expect(ptr == NULL, 0)) { ... }
```

教えるときの注意: これは**分岐予測器への指示ではなくコード配置の指示**(実行時の予測はハードウェアが学習する)。効果があるのはホットパスのエラーチェックなど明確に偏った分岐だけで、まず正しく書き、プロファイルで熱い場所に絞って使う。

## 5. メモリ確保のコントロール

- ループ内の `malloc/new`、`std::vector` の成長、`std::string` の連結は、C#のGCほど劇的ではないがやはり遅い(アロケータのロック・キャッシュミス)
- 定石は C# と同じ発想: **事前確保**(`vector::reserve`)、**再利用**(ループ外で確保して `clear()`)、**スタック利用**(固定長配列、`std::array`)
- +1の知識の候補: アリーナ/バンプアロケータ、`std::pmr`、SmallVector(小さいうちはスタック)

## 6. Rust固有の要点

- 借用検査のおかげで**エイリアス情報が型に含まれ**、`&mut` は重複しないことをコンパイラが知っている → C++より自動ベクトル化が効きやすい場面がある
- **イテレータチェーンはゼロコスト**: `a.iter().zip(b).map(..).sum()` は手書きループと同等の機械語になる(godboltで見せると初学者が驚く良い教材)
- **境界チェック**: `a[i]` は毎回チェックが入りベクトル化を阻むことがある。イテレータで書くとチェックが消える。`unsafe { get_unchecked }` は最後の手段として名前だけ
- 計測: `cargo bench` + criterion、`cargo flamegraph`

## 参考リンク集(回答に添えてよい実在URL)

- Compiler Explorer (godbolt): https://godbolt.org/
- Agner Fog の最適化マニュアル: https://www.agner.org/optimize/
- cppreference(`[[likely]]` 等の言語仕様): https://ja.cppreference.com/
- GCC 最適化オプション一覧: https://gcc.gnu.org/onlinedocs/gcc/Optimize-Options.html
- The Rust Performance Book: https://nnethercote.github.io/perf-book/
