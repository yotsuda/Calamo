# Third-party notices

Lily# Scan Import contains the following third-party components. Each is used under its own
license, reproduced or referenced below.

## ONNX Runtime

`bin/onnxruntime.dll`, `bin/Microsoft.ML.OnnxRuntime.dll` — runs the symbol detector.
<https://github.com/microsoft/onnxruntime>

MIT License

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

ONNX Runtime's own third-party notices:
<https://github.com/microsoft/onnxruntime/blob/main/ThirdPartyNotices.txt>

## Music fonts the reader learnt from

The symbol detector (`bin/detector.onnx`) and the shape classifier (`bin/glyph-train.tsv`) were
trained on pages of generated music engraved with these fonts. **No font file is included** in this
extension; the fonts are used as any document uses a font, and each of their licenses leaves such
documents, and what is made from them, unrestricted.

- **Emmentaler** (Feta), the LilyPond font — Copyright (C) 1997-2023 Han-Wen Nienhuys, Jan
  Nieuwenhuizen, Werner Lemberg and the LilyPond contributors. GNU GPL v3 or later, or the SIL Open
  Font License 1.1 (dual-licensed). <https://lilypond.org>
- **Leland**, **MuseJazz** — the MuseScore project. SIL Open Font License 1.1.
  <https://github.com/MuseScoreFonts/Leland>, <https://github.com/musescore/MuseScore/tree/master/fonts>
- **Bravura**, **Petaluma** — Copyright (c) Steinberg Media Technologies GmbH. SIL Open Font License 1.1.
  <https://www.smufl.org/fonts/>
- **Finale Maestro**, **Finale Broadway** — Copyright (c) MakeMusic, Inc. SIL Open Font License 1.1.
  <https://www.smufl.org/fonts/>
- **Gonville** (as MuseScore's Gootville) — Simon Tatham; the MuseScore version by Grzegorz
  Pruchniakowski, SIL Open Font License. <https://www.chiark.greenend.org.uk/~sgtatham/gonville/>

The SIL Open Font License is at <https://openfontlicense.org/open-font-license-official-text/>.

## .NET runtime, System.Drawing.Common, Windows SDK projection

The reader is published self-contained: the .NET runtime and libraries, `System.Drawing.Common`
and `Microsoft.Windows.SDK.NET` (C#/WinRT projection, used to draw PDF pages) are in `bin/`.
All are Copyright (c) .NET Foundation and Contributors / Microsoft Corporation, under the MIT
License (the text above, with that copyright line).
<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>,
<https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>,
<https://github.com/microsoft/CsWinRT/blob/master/LICENSE>
