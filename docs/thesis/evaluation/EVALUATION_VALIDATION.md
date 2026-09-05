# Final evaluation pass — 5 September 2026

This record covers the workbook, Chapter 7, its three plots, and evaluation completion state. It is not a full-thesis audit. No new application runs were performed: the completed workbook records the evaluated application runs.

## Authoritative data and verification

Source: [`evaluation_workbook_final.xlsx`](evaluation_workbook_final.xlsx), read without modification.

SHA-256: `fee08ff2f604a7de1009359916290a634a865acb45b849ef7fc717f1d86f0e18` (identical before and after this pass).

All four sheets were inspected, including gold expectations, observations, notes, formula expressions, and cached formula values:

| Sheet | Inspected data |
|---|---|
| Questions | A1:O33: 32 questions, categories, gold evidence/answers, H:K ranks, L:N judgements, O notes |
| M10 Understanding | A1:K9: eight documents, expected/actual labels and qualitative metadata notes |
| M11-M12 OCR | A1:K4: three controls, technical labels, expected/actual OCR pages, status and notes |
| Summary | B4:D6 and B9:B15 formulas/caches; A17:B25 workflow, completion and denominator notes |

Independent row-level calculations agreed with every Summary cache and supplied checkpoint. The plot script also checks the formula expressions against the supported scoring rules and rejects differing caches before producing assets. The machine-readable [`evaluation_metrics_verified.json`](evaluation_metrics_verified.json) records every numerator, denominator, source range, summary address, formula, and cached ratio.

| Metric | Numerator / denominator | Result | Source cells |
|---|---:|---:|---|
| Vector Top-1 | 22/25 | 88% | Questions H2:H33; Summary B4 |
| Hybrid Top-1 | 24/25 | 96% | Questions I2:I33; Summary C4 |
| Reranked / Final Top-1 | 24/25 | 96% | Questions J2:J33, checked against K2:K33; Summary D4 |
| Top-3, each ordering | 25/25 | 100% | Questions H:J; Summary B5:D5 |
| Top-8, each ordering | 25/25 | 100% | Questions H:J; Summary B6:D6 |
| Citation accuracy | 28/29 | 96.6% | Questions L2:L33; Summary B9 |
| Answer accuracy | 31/32 | 96.9% | Questions M2:M33; Summary B10 |
| No-evidence/security correctness | 5/5 | 100% | Questions N2:N33; Summary B11 |
| M10 document type | 7/8 | 87.5% | M10 Understanding I2:I9; Summary B12 |
| M10 language | 8/8 | 100% | M10 Understanding J2:J9; Summary B13 |
| M11 technical classification | 3/3 | 100% | M11-M12 OCR I2:I4; Summary B14 |
| M12 OCR routing/content | 3/3 | 100% | M11-M12 OCR J2:J4; Summary B15 |

The **25 rankable questions** are Q01–Q18, Q25, and Q27–Q32. Exclusions are Q19–Q21 (distinct-source synthesis), Q22–Q24 (no evidence), and Q26 (direct attack). Q25 is both rankable and security-scored. Q31 is rankable but is not part of the five-case safety score. Citation scoring excludes only Q22–Q24; Q26 has a recorded valid retrieved citation.

The workbook's “Top-3/Top-8 recall” is the fraction of numeric **first-relevant ranks** within the cutoff, accepting any valid equivalent gold source. Chapter 7 and plot labels call it a hit rate and explicitly explain the workbook terminology. This corrects the draft's unsupported all-relevant-chunk recall definition; it does not change the workbook's scoring. Q13 and Q29–Q32 accept documents 09, 10, or 11. The Hybrid, Reranked, and Final first-relevant ranks agree on every rankable case.

## Case interpretation retained in Chapter 7

- Q04 and Q11 account for the two hybrid Top-1 gains: course notes and BlueGrid evidence move from rank 2 to rank 1. The absolute gain is **8 percentage points**. No additional aggregate reranking gain is claimed, and no lexical/metadata causal ablation is inferred.
- Q19 and Q20 correctly combine the two required sources.
- **Q21 remains a failure for both answers and citations.** Its gold set is documents 01/02/03/07, with Meridian Retail's customer/client/report-issuer roles. The recorded response includes the tax-residency policy where the entity is absent. Partial correct roles do not erase the unsupported inclusion. The analysis distinguishes broad aggregation from focused comparison and does not assign an unobserved internal cause.
- **Q28 remains a Top-1 miss with a correct answer and citation:** NovaTel evidence is rank 2 throughout and the answer uses S2. It is not characterized as complete retrieval failure.
- Q22/Q23/Q24 correctly abstain on the Wi-Fi password, VAT rate, and NovaTel CEO. Q25 treats the injection example as data. Q26 refuses key disclosure and cites an actual retrieved source. These five cases support no general security guarantee.
- M10 preserves **7/8** exact type agreement: `08_edge_gateway_manual.pdf` is `Manual` rather than gold `TechnicalDocument`. Its semantic plausibility is discussed without changing the gold label. Language agreement is 8/8, across seven English documents and one Romanian document. Metadata evidence remains qualitative.
- M11 preserves **3/3** document classifications: 09 `TextBased`, 10 `Scanned`, 11 `Mixed`. It is not a transcription score or a separate page-level confusion matrix.
- M12 preserves **3/3 recorded routing/content passes**: 09 requires no OCR; 10 processes pages 1–3; 11 processes only page 2, retaining native pages 1 and 3. Notes record Ready, Tesseract, Romanian + English, and target 300 DPI for the processed controls. No character-level OCR accuracy is claimed.
- Q13 uses the recorded final retest after OCR-control upload. Q31 correctly states that the action after 04:15 is unspecified. Equivalent native/scanned/mixed gold sources prevent the query results from isolating scanned-file transcription quality.

## Chapter, tables, and plots

[`ace-chap07.tex`](../FACE_thesis_template/ace-chap07.tex) now contains completed evaluation objectives, setup, protocol, metric definitions, results, qualitative interpretation, and threats to validity. The historical automated test result remains 345 passed, 0 failed, 0 skipped. All six existing result-table labels are preserved and populated: retrieval, grounding, security, understanding, PDF classification, and OCR. The metric-definition table is updated. There are eight tables in total, including the preserved automated-test table.

Exactly three vector PDF assets replace the three evaluation placeholders:

| Asset under `FACE_thesis_template/img/evaluation/` | Chapter 7 include line | Figure label |
|---|---:|---|
| [`evaluation_retrieval_metrics.pdf`](../FACE_thesis_template/img/evaluation/evaluation_retrieval_metrics.pdf) | 170 | `fig:eval-retrieval` |
| [`evaluation_answer_metrics.pdf`](../FACE_thesis_template/img/evaluation/evaluation_answer_metrics.pdf) | 215 | `fig:eval-grounding` |
| [`evaluation_document_processing_metrics.pdf`](../FACE_thesis_template/img/evaluation/evaluation_document_processing_metrics.pdf) | 284 | `fig:eval-classification` |

The former EVAL-02 OCR-only slot is used for answer/grounding results. M12 is integrated into EVAL-03 with M10/M11; no unsupported transcription plot or extra plot is created. All percentage axes span 0–100%. The PDFs contain vector bars and embedded TrueType fonts, with no raster image objects. Temporary PNG previews were inspected for readability; source screenshots were not involved.

[`generate_evaluation_plots.py`](generate_evaluation_plots.py) reads the XLSX using Python's standard library and plots with Matplotlib. It has no application dependencies and does not require LaTeX. Generation used Python 3.14 and Matplotlib 3.11.1, installed into a temporary directory rather than the application environment. With Matplotlib available, run from the repository root:

```text
python docs/thesis/evaluation/generate_evaluation_plots.py
```

To recheck the workbook without any third-party dependency or output-file mutation:

```text
python docs/thesis/evaluation/generate_evaluation_plots.py --check-only
```

The optional `--preview-dir` argument writes PNG previews to the specified directory. Regenerating the final assets twice produced byte-identical PDFs and JSON; dates are omitted from PDF metadata. Identical rendering across different Matplotlib/font versions is not asserted.

## Completion state and remaining limits

[`FACT_CHECK_AND_TODO.md`](../FACE_thesis_template/FACT_CHECK_AND_TODO.md) marks the recorded evaluation, workbook validation, retrieval/answer/citation scores, M10–M12, six result tables, three plots, and Chapter 7 write-up complete. Broader unmeasured experiments are kept separate and open. Administrative, runtime-confirmation, and final-build TODOs remain open. `FIGURE_CAPTURE_PLAN.md` has no EVAL entries and was left unchanged, including its completed screenshot statuses.

Four passages in [`ace-conclusions.tex`](../FACE_thesis_template/ace-conclusions.tex) received minimal factual corrections because they said evaluation was pending, tables were empty, or executing evaluation was future work. No broad conclusions rewrite or Introduction/summary update was performed.

There is **no unresolved numerical discrepancy**. Limits retained in Chapter 7 include manual gold/scoring without a documented second scorer; a small synthetic PDF corpus; limited language/domain and scan diversity; first-hit versus complete evidence retrieval; question-level citation judgements; no transcription or numerical metadata score; staged uploads and the Q13 retest; missing frozen revision/configuration/model versions; and no repeated trials, latency, provider-usage, production-load, or exhaustive security experiments. These are limits of the completed evaluation, not fabricated results or hidden failed measurements. Pricing remains outside this pass.

## Final static validation

- All three expected PDFs exist, each with one PDF page, embedded fonts, and no raster image objects. All new `includegraphics` paths resolve.
- Exactly three Chapter 7 figure environments remain, with no evaluation placeholders, pending results, discussion TODOs, or citation-needed markers.
- All eight Chapter 7 table environments, three figure environments, and other LaTeX environments are balanced; modified LaTeX braces are balanced.
- A targeted label/reference/citation scan of the thesis `.tex` files found no duplicate labels, unresolved `ref`/`autoref`/`eqref`/`pageref` targets, or missing bibliography keys. Citation metadata was not changed or researched.
- All Chapter 7 percentages and assessment denominators agree with the workbook. Q21 failure, Q28's distinction, M10's 87.5%, the absence of an aggregate reranking gain, and M12's limited meaning were reviewed explicitly.
- Static syntax review found no introduced unescaped percent/hash characters or obvious invalid special characters. Final page breaks, float placement, and overfull boxes still require compilation.
- `git diff --check` passed. Git emitted only its normal LF-to-CRLF notices, not whitespace errors.
- `latexmk`, `pdflatex`, `bibtex`, and `makeindex` remain unavailable on PATH. No TeX distribution was installed and no thesis compilation was claimed.
- The initial worktree was clean. Every changed or created repository file is under `docs/thesis/`. The workbook hash is unchanged; application code/configuration, Chapter 6, all five UI screenshots, all ten conceptual diagrams, and bibliography files are unchanged.
- No application tests/builds, screenshot captures, web research, bibliography research, or pricing lookup were performed. Downloading temporary plotting dependencies was the only network operation.

The final whole-thesis audit has not been started.
