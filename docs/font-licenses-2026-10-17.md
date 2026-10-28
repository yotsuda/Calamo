# 学習データのフォントのライセンス（2026-10-17 に確認）

ロードマップ §6 の「公開の配布の前に、学習データのフォントのライセンスとモデルへの及び方を確かめる」の答え。法律の助言ではない。

## 何を配るか

配布物（VS Code 拡張 `yotsuda.lilysharp-omr`）に**フォントのファイルは入らない**。入るのは、フォントで描いた生成譜から学んだもの:

| 物 | 中身 | フォントとの関係 |
|---|---|---|
| `bin/detector.onnx` | 検出器の重み | 描いたページの画素から学習 |
| `bin/glyph-train.tsv` | k-NN の標本（大きさ・塗り・8×8 のインクの割合） | 描いたグリフの低解像度の像 |
| `bin/emmentaler-bbox.tsv` | Emmentaler のグリフの外接枠の寸法表 | フォントの寸法（数値） |
| `bin/smufl-bbox-{Bravura,Leland,Petaluma}.tsv` | SMuFL の 3 書体のグリフの外接枠の寸法表 | フォントの寸法（数値） |

（`data/ms-glyphs.tsv`・`ms-frets.tsv` は学習データ作りの表で、配布物には入らない。梱包した `bin/` にフォントファイル（otf・ttf・woff）が無いことは `out/extension/stage/bin` で確かめた。）

## 各フォントのライセンス

| フォント | 使った所 | ライセンス | 出典 |
|---|---|---|---|
| Emmentaler（Feta） | Lily# の全生成譜（主の学習データ）、MuseScore の 1 書体 | **GPL v3+ と SIL OFL 1.1 のデュアル**（Reserved Font Name: Emmentaler、Feta） | Lily# 同梱の `Fonts/Emmentaler-LICENSE.txt`（フォントのソースの文言を引用）、[Debian の copyright](https://metadata.ftp-master.debian.org/changelogs/main/l/lilypond/stable_copyright)、[lilyglyphs の FONTLOG](https://tug.ctan.org/macros/unicodetex/latex/lilyglyphs/fonts/FONTLOG) |
| Leland | MuseScore の学習データ | SIL OFL 1.1 | [MuseScoreFonts/Leland](https://github.com/MuseScoreFonts/Leland) |
| Bravura | 同上 | SIL OFL 1.1（RFN: Bravura） | [smufl.org/fonts](https://www.smufl.org/fonts/) |
| Petaluma | 同上 | SIL OFL 1.1 | [smufl.org/fonts](https://www.smufl.org/fonts/) |
| MuseJazz | 同上 | SIL OFL（リポジトリの `fonts/musejazz/OFL.txt`） | [musescore/MuseScore fonts/musejazz](https://github.com/musescore/MuseScore/tree/master/fonts/musejazz) |
| Finale Maestro | 同上 | SIL OFL 1.1（`fonts/finalemaestro/OFL.txt`） | [musescore/MuseScore fonts/finalemaestro](https://github.com/musescore/MuseScore/tree/master/fonts/finalemaestro)、[smufl.org/fonts](https://www.smufl.org/fonts/)、[Fedora の spec（OFL-1.1-RFN）](https://copr-be.cloud.fedoraproject.org/results/thrnciar/python-pip-2ebe6bf4/fedora-42-x86_64/07076747-makemusic-finale-fonts/makemusic-finale-fonts.spec) |
| Finale Broadway | 同上 | SIL OFL 1.1（`fonts/finalebroadway/OFL.txt`） | 同上 |
| Gonville（MuseScore では Gootville） | 同上 | 元の Gonville: フォントファイルは制限なし（ソースは MIT）。MuseScore の Gootville: SIL OFL（`readme.txt`） | [Simon Tatham の Gonville](https://www.chiark.greenend.org.uk/~sgtatham/gonville/)、[musescore/MuseScore fonts/gootville](https://github.com/musescore/MuseScore/tree/master/fonts/gootville) |
| Windows の 17 書体（TAB の数字、`ms_frets.py`） | MuseScore の TAB の学習データ | Windows に付属する書体を文書の描画に使っただけ。配らない | — |

## モデルへの及び方（判断）

- **OFL** は「フォントソフトウェア」の改変・再配布に条件を付けるが、**フォントで作った文書には及ばない**（OFL 1.1 第 5 条の末尾: "The requirement for fonts to remain under this license does not apply to any document created using the Font Software"）。
  描いたページ、そこから学んだ検出器の重み、8×8 の像の標本は文書から作ったもので、フォントソフトウェアでも、その改変版でもない。配布に条件は付かない。
- **GPL**（Emmentaler のもう一方の枝）は、プログラムの出力にはプログラムの一部が含まれない限り及ばない。描いた画像にグリフのアウトラインは含まれない。
  疑いが残るとしても Emmentaler は OFL でも使えるので、OMR 側は OFL の枝で考えればよい（Lily# が GPL の枝で同梱していることとは別）。
- **寸法表**（`emmentaler-bbox.tsv`）はグリフの外接枠の数値で、字形の表現ではない。念のため配布物の告知に Emmentaler の名を挙げる。
- **Reserved Font Name** は改変したフォントに元の名前を付けることの禁止で、学習データの話には関係しない。
- 配布物の `THIRD-PARTY-NOTICES.md` に「学習に使った音楽フォント」の節を足し、名前・著作権者・ライセンスと「フォントファイルは含まない」を書いた（義務ではなく礼儀）。

**結論**: 学習データのフォントは全部 OFL（Emmentaler は GPL/OFL のデュアル）で、配布物にフォントファイルは入らず、モデルと表は文書から作ったものなので、**公開の配布に支障はない**。
