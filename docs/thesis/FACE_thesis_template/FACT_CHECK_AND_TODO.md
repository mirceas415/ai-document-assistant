# Fact Check and Completion Register

This file is the authoritative checklist for information that must not be guessed. A checked item should be marked complete only after confirmation from the student, scientific supervisor, Faculty, current runtime configuration, or final measured evaluation, as appropriate.

## Identity, programme, and titles

- [ ] Replace every `[CANDIDATE FULL NAME]` with the candidate's official full name, preserving the spelling and capitalization required by FACE.
- [ ] Confirm `[SCIENTIFIC SUPERVISOR NAME AND TITLE]`, including academic and engineering titles and official ordering.
- [ ] Confirm `[OFFICIAL STUDY PROGRAM]`. The student is in the English-taught Computers programme, but the exact diploma/form wording must come from an official source.
- [ ] Confirm the official Romanian thesis title and replace `[ROMANIAN TITLE TO BE CONFIRMED]` everywhere it appears.
- [ ] Obtain final approval for the working English title, **Intelligent Assistant for Semantic Document Analysis and Querying**.
- [ ] Confirm whether the Romanian forms require the English title as a secondary line and whether punctuation/capitalization must follow a Faculty register.
- [ ] Confirm the current official department name already printed by the template.
- [ ] Confirm `[DEPARTMENT DIRECTOR NAME AND TITLE]`; do not restore the sample director from the extracted template without verification.
- [ ] Check gender-dependent Romanian administrative wording such as `Subsemnatul`, `absolvent`, and `susținută` against the candidate and the official form.

## Dates, session, signatures, and administrative forms

- [ ] Replace `[SUBMISSION MONTH AND YEAR]` with the official diploma session wording.
- [ ] Complete every `[DATE TO BE COMPLETED]`: assignment release, department approval, estimated submission, actual submission, originality declaration, supervisor report, and any signature dates.
- [ ] Obtain every `[SIGNATURE]` required from the candidate, supervisor, and department representative.
- [ ] Confirm the official consultation schedule in `[CONSULTATION SCHEDULE]`.
- [ ] Complete the project-form initial data, concise project contents, and mandatory graphical material fields marked `[TO COMPLETE WITH SUPERVISOR]`.
- [ ] Confirm the documentation/practice location checkboxes in the supervisor report. The sample pre-selected `În facultate` box was cleared.
- [ ] Leave all supervisor-evaluation checkboxes and comments for the supervisor; verify whether this page is completed before or after binding.
- [ ] Confirm whether the optional dedication, acknowledgements, and foreword pages must be completed, retained as blank pages, or omitted with Faculty approval. Their template positions are currently preserved with explicit placeholders.
- [ ] Verify the Romanian originality-declaration wording against the final 2026 Faculty form before signing; content architecture was preserved but placeholders were made explicit.
- [ ] Confirm whether the separate English and Romanian summary order must remain exactly as in the supplied template (Romanian first, English second).
- [ ] Perform a final manual Romanian-language review of `ace-summary.tex`, including terminology for RAG, reranking, source provenance, and prompt injection.

## Repository, website, and submission media

- [ ] Replace `[PROJECT REPOSITORY URL]` only after the repository location and access policy are confirmed.
- [ ] Confirm the approved electronic source-code submission mechanism and update `ace-sourcecode.tex`.
- [ ] Confirm whether a public project website or deployment URL exists. Do not invent one; update `ace-proj-website.tex` or state approved non-applicability.
- [ ] Confirm whether the Faculty requires a CD, DVD, archive, cloud upload, or another medium; update `ace-media-support.tex` accordingly.
- [ ] Confirm whether the application will be demonstrated only locally or deployed before submission. The current thesis does not claim an existing production deployment.

## Figures and visual material

- [x] Render all ten Mermaid sources under `diagrams/` to vector PDF using a consistent thesis style.
- [x] Replace DIA-01 placeholder: high-level system architecture.
- [x] Replace DIA-02 placeholder: domain/workspace model.
- [x] Replace DIA-03 placeholder: complete ingestion pipeline.
- [x] Replace DIA-04 placeholder: technical PDF classification flow.
- [x] Replace DIA-05 placeholder: selective OCR flow.
- [x] Replace DIA-06 placeholder: chunk overlap and provenance.
- [x] Replace DIA-07 placeholder: hybrid retrieval pipeline.
- [x] Replace DIA-08 placeholder: weighted RRF fusion concept.
- [x] Replace DIA-09 placeholder: reranking and fail-open fallback.
- [x] Replace DIA-10 placeholder: grounded citation/source flow.
- [x] Capture and integrate UI-02: grounded answer with multiple citations.
- [x] Capture and integrate UI-03: source-details modal.
- [x] Capture and integrate UI-04: document management with real processing states.
- [x] Capture and integrate UI-07: local OCR diagnostics for a scanned PDF.
- [x] Capture and integrate UI-10: Advanced Retrieval Details for the BlueGrid Q11 query.
- [x] Confirm the captures use thesis evaluation documents and expose no credentials or unrelated confidential information; the student's own visible account identity is acceptable.
- [x] After inserting real assets, check screenshot legibility, caption consistency, surrounding references, and privacy-sensitive content.
- [ ] Verify the five screenshot entries in the generated List of Figures during the final LaTeX build.

## Final experimental evaluation

Final evaluation pass completed on 5 September 2026. The read-only source is [`../evaluation/evaluation_workbook_final.xlsx`](../evaluation/evaluation_workbook_final.xlsx). See [`../evaluation/EVALUATION_VALIDATION.md`](../evaluation/EVALUATION_VALIDATION.md) for cell ranges, reproducible plot generation, scope limits, and validation results. The original broader evaluation plan is distinguished below from the cases actually recorded.

- [x] Confirm the evaluated corpus and workbook gold expectations: eight primary PDFs, three equivalent OCR controls, and 32 questions.
- [x] Verify completion of Q01--Q32 and M10--M12 from the recorded application runs; independently recalculate all Summary metrics without modifying the workbook.
- [x] Validate the three recorded retrieval orderings for the same applicable questions. These are stage diagnostics, not separately scored answer-generation experiments; uploads were staged and Q13 was retested.
- [x] Populate retrieval metrics over 25 rankable questions: Top-1 22/25, 24/25, 24/25; Top-3 and Top-8 25/25 at every stage. Correct the draft's metric definition to the workbook's first-relevant hit rate, accepting equivalent gold sources.
- [x] Populate question-level citation correctness (28/29) and answer correctness (31/32), retaining Q21 as the sole failure in both.
- [x] Record no-evidence/security correctness for Q22--Q26 (5/5), with claims limited to these cases.
- [x] Complete M10 exact type (7/8) and language (8/8) reporting; preserve the Manual versus TechnicalDocument mismatch and describe metadata only qualitatively.
- [x] Complete M11 technical PDF classification reporting (3/3 control documents).
- [x] Complete M12 recorded OCR routing/content reporting (3/3 controls); preserve the distinction from character-level transcription accuracy.
- [x] Populate all six Chapter 7 result tables with measured outcomes and explicit assessment units.
- [x] Generate and integrate EVAL-01: `img/evaluation/evaluation_retrieval_metrics.pdf`.
- [x] Generate and integrate EVAL-02: `img/evaluation/evaluation_answer_metrics.pdf`. The former OCR-only slot now presents answer/grounding results; measured M12 outcomes are included in EVAL-03.
- [x] Generate and integrate EVAL-03: `img/evaluation/evaluation_document_processing_metrics.pdf`, covering M10, M11, and M12 with separate denominators.
- [x] Complete the Chapter 7 evaluation write-up, qualitative failure analysis, and threats to validity; remove all evaluation placeholders and discussion TODO markers.
- [x] Correct only the obsolete evaluation-pending statements in Conclusions; broader conclusions and summary integration remain for the later full-thesis audit.

Unmeasured extensions and reproduction details remain open; none is represented as a completed experiment:

- [ ] Recover and record the evaluated repository revision, database/model versions, and complete runtime configuration from contemporaneous evidence; do not substitute current defaults.
- [ ] Independently review the gold expectations and answer/citation judgements; no second scorer is documented in the workbook.
- [ ] Extend Document Understanding evaluation to numerical title/subject and metadata scoring, sampling limits, and explicit skip/failure cases.
- [ ] Extend technical PDF evaluation to a page-level confusion matrix, ImageBased/Unknown cases, and threshold-boundary experiments.
- [ ] Extend OCR evaluation to independent transcription accuracy, degraded scans, empty/partial/failed results, and page-limit cases; isolate scanned sources from equivalent native controls.
- [ ] Score backend citation mapping and semantic claim support separately, and evaluate historical-source behavior.
- [ ] Extend experimental security coverage to empty collections, cross-owner/workspace cases, unknown citation markers, and additional attacks using synthetic data.
- [ ] Record latency, per-case reranking applied/fallback state, and provider usage for descriptive discussion.
- [ ] Review English/Romanian summaries and broader conclusions during the separate final full-thesis audit.

## Bibliography and academic integrity

- [ ] Perform the dedicated verified bibliography pass described in `CITATIONS_NEEDED.md`.
- [ ] Verify every author, title, venue/publisher, year, edition, DOI, and URL before adding a BibTeX entry.
- [ ] Replace each visible `[CITATION NEEDED: ...]` marker with an actual citation only after its entry is verified.
- [ ] Prefer original academic papers for RAG and RRF, authoritative textbooks/surveys for information retrieval, standards for PDF/Open XML, and official project/vendor documentation for implementation technologies.
- [ ] Check that standard methods are attributed as existing concepts and that the personal contribution is limited to design, implementation, integration, and evaluation in this application.
- [ ] The 26 sample bibliography/example entries supplied with the FACE template were removed; none was treated as a verified thesis source.

## Runtime and implementation facts requiring final confirmation

- [ ] Record the actual runtime configuration used for final evaluation. Values in the thesis are checked-in defaults and can be overridden by ASP.NET configuration.
- [ ] Confirm the native Tesseract runtime version used for screenshots/evaluation. The project references the `TesseractOCR` wrapper package version 5.5.2, while one service fallback identifier is `5.5.1`; the thesis deliberately says `Tesseract 5` rather than asserting an unverified native version.
- [ ] Confirm model availability and the actual evaluated identifiers at evaluation time: embedding, Document Understanding, reranking, and answer models.
- [ ] Confirm OCR language data availability for `ron+eng` on the evaluation machine.
- [ ] Confirm PostgreSQL and pgvector versions used for final evaluation if they are reported in the thesis.
- [ ] Preserve the accurate statement that explicit embedding rebuild regenerates embeddings; content hashes detect currentness but do not implement a provider-call cache.
- [ ] Preserve the accurate statement that only pages classified exactly as `Scanned` are automatically OCR-routed.
- [ ] Preserve the accurate statement that OCR failure is non-fatal only when useful text remains from other native or successfully recognized pages.
- [ ] Preserve the accurate statement that document metadata can boost only the vector/lexical chunk union and is not answer evidence.
- [ ] Preserve the accurate statement that backend citation validation proves identifier membership, not semantic support for every claim.
- [ ] Preserve the accurate statement that no HNSW or IVFFlat approximate vector index is configured in the current implementation.
- [ ] Verify that no application functionality changes between this draft and final submission without updating the affected thesis claims.

## LaTeX/template and final production checks

- [ ] Keep `ace-thesis.cls` unchanged unless a confirmed compilation issue requires a documented document-level compatibility fix first.
- [ ] Install/use an existing TeX environment and follow `BUILD.md`; no TeX distribution was installed during drafting.
- [ ] Run a final multi-pass build after bibliography entries and graphical assets exist.
- [ ] Check for unresolved references, undefined citations, overfull boxes, duplicate destinations, index warnings, and missing image files.
- [ ] Verify Roman numbering for preliminary pages and Arabic numbering beginning at Chapter 1.
- [ ] Verify the Table of Contents, List of Figures, List of Tables, List of Algorithms, bibliography, index, and official appendix pages.
- [ ] Confirm whether the Faculty accepts the added List of Algorithms; it was enabled because five pseudocode algorithms are included.
- [ ] Check whether the class's checkbox symbols compile in the final TeX distribution; if not, document the issue before adding `amssymb` in the document preamble rather than editing the class.
- [ ] Confirm the supplied nested title-page environment and official logo layout render as expected in the current 2026 toolchain.
- [ ] Do not mistake the supplied `ace-thesis.pdf` reference/template PDF for the newly compiled thesis output; archive or rename final output according to Faculty rules after verification.
- [ ] Perform final page-count adjustment only after real figures, citations, and evaluation results are inserted. Do not pad prose artificially.
- [ ] Replace all remaining visible placeholders and re-run a repository-wide placeholder search before submission.

## Verified items already complete

- [x] Official 2026 FACE template selected as the only thesis base.
- [x] Official logos, class, cover/page architecture, front-matter order, numbering split, bibliography mechanism, appendices, and index preserved.
- [x] English working title applied to the title page and thesis documentation.
- [x] Main Chapters 1--7 and separate Chapter 8 conclusions drafted in English.
- [x] Romanian and English project summaries drafted without invented experimental results.
- [x] Ten conceptual Mermaid diagram sources created.
- [x] Five UI screenshots and three measured evaluation plots integrated; the original placeholders have been replaced.
- [x] One full backend test run completed: 345 passed, 0 failed, 0 skipped. It must not be repeated for this drafting task.
