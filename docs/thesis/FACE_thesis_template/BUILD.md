# Building the FACE Thesis on Windows

The drafting machine did not have `latexmk`, `pdflatex`, `bibtex`, or `makeindex` on `PATH`. No TeX distribution was installed automatically. The existing `ace-thesis.pdf` is the 32-page reference PDF supplied with the official template; it is **not** a compiled version of this thesis draft.

## Prerequisites

Use an existing current MiKTeX or TeX Live installation that provides:

- pdfLaTeX;
- `latexmk` (preferred);
- BibTeX and `IEEEtran.bst`;
- MakeIndex;
- all packages loaded by `ace-thesis.cls`, including the UCS support needed by `inputenc[utf8x]`.

Keep every `.tex` and `.bib` file encoded as UTF-8. Build with pdfLaTeX unless the Faculty supplies a different instruction; the official class is configured for the pdfLaTeX/fontenc/inputenc route. Do not replace or redesign `ace-thesis.cls` to solve a local package-installation problem.

## Preferred final command

Open PowerShell and run:

```powershell
Set-Location 'C:\Users\Mircea\source\repos\AI.DocumentAssistant\docs\thesis\FACE_thesis_template'
latexmk -pdf -interaction=nonstopmode -file-line-error ace-thesis.tex
```

`latexmk` should perform the required LaTeX, BibTeX, index, and cross-reference passes. The expected output is:

```text
C:\Users\Mircea\source\repos\AI.DocumentAssistant\docs\thesis\FACE_thesis_template\ace-thesis.pdf
```

Do not treat that path as a new thesis result until the timestamp changes and the title page contains **Intelligent Assistant for Semantic Document Analysis and Querying**.

## Manual multi-pass fallback

If `latexmk` is unavailable but the standard tools are installed, run from the same directory:

```powershell
pdflatex -interaction=nonstopmode -file-line-error ace-thesis.tex
bibtex ace-thesis
makeindex ace-thesis.idx
pdflatex -interaction=nonstopmode -file-line-error ace-thesis.tex
pdflatex -interaction=nonstopmode -file-line-error ace-thesis.tex
```

The current first draft has no verified bibliography entries and deliberately contains visible citation-needed markers. BibTeX may report that there are no citations until the dedicated verified bibliography pass is complete. For a temporary prose-only preview, omit the BibTeX command, run MakeIndex after the first LaTeX pass, and run pdfLaTeX twice more. The final submitted build must include the verified bibliography pass.

## Rerendering Mermaid diagrams

The ten committed conceptual figures are vector PDFs under `img/diagrams/`. Mermaid CLI is not an application dependency. The diagrams in this draft were rendered with Mermaid CLI 11.17.0 through a temporary `npx` invocation and the existing local Chrome installation; this does not modify either application package manifest.

From the thesis-template directory, rerender one diagram with:

```powershell
$env:PUPPETEER_SKIP_DOWNLOAD = 'true'
$env:PUPPETEER_EXECUTABLE_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
npx.cmd --yes @mermaid-js/mermaid-cli@11.17.0 -i 'diagrams/DIA-01-high-level-architecture.mmd' -o 'img/diagrams/DIA-01-high-level-architecture.pdf' -e pdf -b white --pdfFit
```

Rerender all conceptual diagrams with:

```powershell
$env:PUPPETEER_SKIP_DOWNLOAD = 'true'
$env:PUPPETEER_EXECUTABLE_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
Get-ChildItem -LiteralPath 'diagrams' -Filter 'DIA-*.mmd' | Sort-Object Name | ForEach-Object {
    $outputName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name) + '.pdf'
    npx.cmd --yes @mermaid-js/mermaid-cli@11.17.0 -i $_.FullName -o (Join-Path 'img/diagrams' $outputName) -e pdf -b white --pdfFit
}
```

If `mmdc` is already installed, it can replace the `npx.cmd --yes @mermaid-js/mermaid-cli@11.17.0` prefix. Adjust `PUPPETEER_EXECUTABLE_PATH` if Chrome is installed elsewhere.

## Inserting remaining graphical assets before the final build

1. Rerender a conceptual PDF only when its Mermaid source changes.
2. Capture the five real application screenshots according to `FIGURE_CAPTURE_PLAN.md` and place them under `img/screenshots/`.
3. Create the three evaluation plots only after verified measurements exist.
4. Replace only the corresponding remaining UI or evaluation `\thesisfigureplaceholder` invocation after its real asset exists, preserving its `\label`.
5. Rebuild and inspect the Table of Contents, List of Figures, List of Tables, and List of Algorithms.

## Final log checks

Inspect `ace-thesis.log` and the console output for:

- undefined references or citations;
- multiply defined labels;
- missing images or packages;
- overfull/underfull boxes that affect readability;
- MakeIndex or BibTeX errors;
- duplicate hyperlink destinations;
- unsupported Unicode characters;
- checkbox-symbol errors on the Romanian forms.

The faculty class defines checkbox commands using `\checkmark` and `\square` but does not explicitly load `amssymb`. The supplied reference PDF proves that its original environment compiled, but a different distribution may report undefined symbols. If that occurs, record the exact error in `FACT_CHECK_AND_TODO.md` and prefer a small document-preamble compatibility import such as `\usepackage{amssymb}` over modifying `ace-thesis.cls`.

After the final build, verify visually that preliminary pages use lower-case Roman numerals and Chapter 1 restarts with Arabic page 1, and that every placeholder has either been intentionally retained for review or replaced with confirmed information.
