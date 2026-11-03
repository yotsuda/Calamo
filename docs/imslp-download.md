# IMSLP の譜面のダウンロード手順

IMSLP はスクリプトからの取得を受け付けないので、ブラウザーで手で落とす。
いま要るのは Schumann の Kinderszenen Op.15 No.1（HANDOFF の S55。ピアノ譜の 3 連符の書き方を確かめる）。

## 手順

1. 曲のページを開く。Op.15 は <https://imslp.org/wiki/Kinderszenen,_Op.15_(Schumann,_Robert)>。
   ほかの曲は IMSLP の検索で曲名を探す。
2. **Sheet Music** → **Scores** → **Complete** の版の一覧から、下の表の版を探す。ファイル番号（`#971693` など）で探すと早い（ページ内検索 Ctrl+F）。
3. 版の **Complete Score** か、ファイル番号のリンクを押す。
4. 著作権の確認のページが出たら、内容を読んで同意する。
   待ち時間や CAPTCHA が出たら、画面の指示に従う（会員でないと待たされることがある）。
5. PDF が開いたら保存する。保存先は「置き場所」のとおり。

手順 3〜5 の画面は、このメモを書いたときに確かめられなかった（スクリプトからは「Redirecting...」しか見えない）。実際の画面と違っていたら、画面に従う。

## Op.15 の版

ファイル番号と版の情報は、2026-11-03 に上のページで確かめた。一覧には全部で 27 の楽譜があり、下は先頭の 6 件。

| ファイル | 版 | 年 | ページ | 使い方 |
|---|---|---|---|---|
| **#971693** | G. Schirmer, Library of Musical Classics Vol.90, Max Vogrich 校訂 | 1893 | 13 | **勧め**。全 13 曲で、No.1 は最初のページにあるはず |
| #927865 | C.F. Peters, Dörffel・Schmidt 校訂 | 1887 頃 | 14 | Schirmer が読みにくいときの次 |
| #40336 | Litolff No.1701, Conrad Kühner 校訂 | 1887 頃 | 14 | 同上 |
| #971706 | Breitkopf und Härtel 初版 | 1838 頃 | 22 | 古い刷りの型も見たいとき |
| #03060 | Breitkopf und Härtel 初版の後の刷り | 1839 頃 | 19 | 同上 |
| #289791 | Breitkopf & Härtel, Schumanns Werke, Clara Schumann 校訂 | 1880 | 10 | **落とさなくてよいかもしれない**（下の注意） |

Schirmer を勧めるのは、評価台の版（Mutopia と RJE）とも、手元の Breitkopf 1880 とも別の版だから。

**注意**: `data/eval/schumann15-1/imslp/` に `breitkopf-1880.pdf`（#289791、`data/LICENSES.md`）がすでにある。`docs/eval-sets.tsv` に 15-1 のこの版の行はない（採点されているのは Mutopia と RJE）。ただし Mutopia の版の底本がこれかどうかは確かめていない。底本なら書き方が評価台と同じなので、別の版の Schirmer を落とす方がよい。

## 置き場所

- **`data/eval` の下には置かない**。評価台の版と混ざらないようにする。
- repo の外の `C:\MyProj\omr-compare\kinderszenen\` に置く（例: `schirmer-1893-971693.pdf`）。公開 repo に入れないため。
- 落とした版を公開 repo に入れることになったら、`data/LICENSES.md` に出所とライセンスを書く。

## 落としたあと

見るのは書き方の型だけで、学習には使わない。次のセッションに、ファイルの場所を伝える。

2026-11-03: 上の表の 6 版を `C:\MyProj\omr-compare\kinderszenen\` に落とし済み。
