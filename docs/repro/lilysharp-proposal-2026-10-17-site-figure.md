# OMR から Lily# へ（2026-10-17・お願い 1 件: サイトに Scan Import の図を 1 枚置いてほしい）

## 1. 何のため

Scan Import 拡張の Marketplace の readme（`extension/README.md`）に、スキャンと読み結果の実例の図を載せたい。
Marketplace の readme の画像は絶対 URL でないと出ず、OMR のリポジトリは非公開なので、公開されている Lily# のサイト
（`https://yotsuda.github.io/LilySharp/`）に置くのが一番近い。readme はもう次の URL を指している:

- 図: `https://yotsuda.github.io/LilySharp/scan-import.png`
- 説明へのリンク: `https://yotsuda.github.io/LilySharp/commands.html#import-image`（既にある節）

## 2. お願い

1. [`docs/figures/scan-import.png`](../figures/scan-import.png)（2200×2013、約 620 KB）を `site/scan-import.png` に置く。
   中身: 上がチャイコフスキー「木の兵隊の行進」（子供のアルバム op.39-5、G. Schirmer 1904、IMSLP #07996、Public Domain）の
   スキャンの最初の 2 段、下が OMR の読み結果を lysc の `png --todo` で描いたもの（`@todo` の赤い印 2 つ付き）。
2. `site/commands-body.html` の `<section id="import-image">` に `<img src="scan-import.png" …>` を 1 つ足す
   （`build-site.ps1` はページが参照する画像だけ `_site/` に写すので、参照が無いと公開されない）。案:

   ```html
   <figure>
     <img src="scan-import.png" alt="A scanned page and the same bars as the reader wrote them, with two places marked in red" width="880">
     <figcaption>A 1904 scan (above) and the first bars as the reader wrote them, drawn by the preview with its marks (below).</figcaption>
   </figure>
   ```

3. push は Lily# 側（オーナー）。サイトが出るまで Marketplace の readme の図は切れた画像になるので、
   **OMR の公開は Lily# のサイトの push の後**にしたい（readme 側は変えなくてよい）。

版・コードの変更は無い。
