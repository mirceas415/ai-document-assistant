# Fact Check and Completion Register

This file is the authoritative checklist for information that must not be guessed. A checked item should be marked complete only after confirmation from the student, scientific supervisor, Faculty, current runtime configuration, or final measured evaluation, as appropriate.

## Pre-final cleanup pass

- [x] Insert the confirmed candidate name, scientific supervisor, study programme, Romanian and English titles, submission month/year, location, and repository URL where required.
- [x] Update both project summaries with the completed evaluation results and complete the Romanian language pass requested for the summary.
- [x] Remove obsolete evaluation-pending and document-drafting language from Chapters 1, 2, and 5 and from Conclusions.
- [x] Remove the unused dedication, acknowledgements, foreword, project-website, and media-support pages from the compiled thesis.
- [x] Rewrite the source-code appendix around the confirmed GitHub repository without claiming an unverified Faculty submission mechanism.
- [x] Replace development-style placeholders in mandatory official forms with confirmed values or clean blank fields for later authorized completion.
- [x] Add a modest set of technical index entries at existing explanatory locations.
- [x] Complete the separate final Astra whole-thesis audit (6 September 2026); see [`../FINAL_THESIS_AUDIT.md`](../FINAL_THESIS_AUDIT.md) for findings, corrections, validation, and remaining administrative items.

## Identity, programme, and titles

- [x] Candidate name confirmed and inserted as **Mircea Smărăndăchescu**.
- [x] Scientific supervisor confirmed and inserted as **Prof. dr. ing. Nicolae Iulian Enescu**.
- [x] Official study field confirmed as **Calculatoare și Tehnologia Informației**; the title-page `În domeniul de studii` field retains this value.
- [x] Official study programme confirmed as **Calculatoare (în limba engleză)**; the Declaration of Originality now uses this programme after `absolvent al programului de studii`.
- [x] Review administrative occurrences individually: preserve field/programme/department distinctions and introduce no unnecessary English programme translation.
- [x] Romanian title confirmed and inserted as **Asistent inteligent pentru analiza și interogarea semantică a documentelor**.
- [x] English title confirmed as **Intelligent Assistant for Semantic Document Analysis and Querying** and kept consistent throughout the front matter.
- [ ] Confirm whether the Romanian forms require the English title as a secondary line and whether punctuation/capitalization must follow a Faculty register.
- [x] Preserve the FACE template department wording **Calculatoare și Tehnologia Informației** in Department fields; do not substitute the study programme.
- [ ] Obtain the department director's official name and title; the field remains blank and no sample name has been restored.
- [ ] Check gender-dependent Romanian administrative wording such as `Subsemnatul`, `absolvent`, and `susținută` against the candidate and the official form.

## Dates, session, signatures, and administrative forms

- [x] Submission session confirmed and inserted as **Septembrie 2026**.
- [ ] Obtain the exact assignment-release, department-approval, estimated-delivery, actual-delivery, declaration, review, and signature dates; their form fields remain blank.
- [ ] Obtain the candidate, supervisor, and department-representative signatures; their form fields remain blank.
- [ ] Confirm the official consultation schedule; the project-sheet field remains blank.
- [ ] Complete the project-sheet initial data, concise project contents, and mandatory graphical-material requirements with the supervisor; these fields remain blank.
- [ ] Confirm the documentation/practice location checkboxes in the supervisor report. The sample pre-selected `În facultate` box was cleared.
- [ ] Leave all supervisor-evaluation checkboxes and comments for the supervisor; verify whether this page is completed before or after binding.
- [x] Remove the optional dedication, acknowledgements, and foreword pages because no genuine text was supplied.
- [ ] Verify the Romanian originality-declaration wording against the final 2026 Faculty form before signing; confirmed fields are filled and the date/signature lines remain blank.
- [x] Preserve both summaries in the supplied order: Romanian first, English second.
- [x] Complete the pre-final Romanian-language review of `ace-summary.tex`, including evaluation wording and diacritics.

## Repository, website, and submission media

- [x] Insert the confirmed repository URL: <https://github.com/mirceas415/ai-document-assistant>.
- [ ] Confirm the approved electronic source-code submission mechanism and update `ace-sourcecode.tex`.
- [x] Remove the empty optional project-website page; no public deployment URL was supplied and the GitHub repository is not presented as one.
- [x] Remove the empty optional media-support page rather than invent a delivery medium.
- [ ] Confirm whether the Faculty ultimately requires a separate media-support statement or page and, if so, supply the approved medium.
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
- [x] Verify the five screenshot entries in the generated List of Figures during the final LaTeX build.

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
- [x] Correct the obsolete evaluation-pending statements in Conclusions; broader Conclusions and summary integration were subsequently reviewed in the final whole-thesis audit.

Unmeasured extensions and reproduction details remain open; none is represented as a completed experiment:

- [ ] Recover and record the evaluated repository revision, database/model versions, and complete runtime configuration from contemporaneous evidence; do not substitute current defaults.
- [ ] Independently review the gold expectations and answer/citation judgements; no second scorer is documented in the workbook.
- [ ] Extend Document Understanding evaluation to numerical title/subject and metadata scoring, sampling limits, and explicit skip/failure cases.
- [ ] Extend technical PDF evaluation to a page-level confusion matrix, ImageBased/Unknown cases, and threshold-boundary experiments.
- [ ] Extend OCR evaluation to independent transcription accuracy, degraded scans, empty/partial/failed results, and page-limit cases; isolate scanned sources from equivalent native controls.
- [ ] Score backend citation mapping and semantic claim support separately, and evaluate historical-source behavior.
- [ ] Extend experimental security coverage to empty collections, cross-owner/workspace cases, unknown citation markers, and additional attacks using synthetic data.
- [ ] Record latency, per-case reranking applied/fallback state, and provider usage for descriptive discussion.
- [x] Integrate the completed results into both summaries and remove obsolete pending-evaluation wording from the surrounding thesis prose.
- [x] Review the whole thesis comprehensively in the final Astra audit, including Introduction-to-Conclusions alignment, both summaries, methodology, denominators, case interpretations, and limitations; retain all verified evaluation values.

## Bibliography and academic integrity

- [x] Complete the dedicated verified bibliography pass described in `CITATIONS_NEEDED.md`.
- [x] Verify the 25 retained bibliography entries before inclusion.
- [x] Remove all visible citation-needed markers after replacing them with verified citations.
- [x] Confirm appropriate use of the existing original RAG/RRF papers, information-retrieval sources, standards, and official technology documentation; retain the 25-source bibliography and legitimate access dates without restarting research.
- [x] Check that standard methods are attributed as existing concepts and that the personal contribution is design, implementation, integration, constraints, diagnostics, and evaluation in this application.
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

- [x] Keep `ace-thesis.cls` unchanged unless a confirmed compilation issue requires a documented document-level compatibility fix first.
- [x] Install/use a TeX environment and follow `BUILD.md`. The first real build used a per-user MiKTeX 25.12 installation from the official `MiKTeX.MiKTeX` winget package.
- [x] Run a final multi-pass build after bibliography entries and graphical assets exist.
- [x] Check for unresolved references, undefined citations, meaningful overfull boxes, duplicate destinations, index warnings, and missing image files.
- [x] Verify Roman numbering for preliminary pages and Arabic numbering beginning at Chapter 1.
- [x] Verify the Table of Contents, List of Figures, List of Tables, List of Algorithms, bibliography, index, and official appendix pages.
- [ ] Confirm whether the Faculty accepts the added List of Algorithms; it was enabled because five pseudocode algorithms are included.
- [x] Check whether the class's checkbox symbols compile in the final TeX distribution; they compile under MiKTeX 25.12 without a compatibility import.
- [x] Confirm the title/front-matter pages and official logo layout render as expected in the current 2026 toolchain. The final logo page no longer resets the PDF page label.
- [x] Replace the supplied reference `ace-thesis.pdf` with the newly compiled thesis output and create a clearly named review copy under `docs/thesis/output/`.
- [x] Perform the first real page-count/layout adjustment after real figures, citations, and evaluation results were inserted, without padding prose.
- [x] Remove visible development placeholders from the compiled thesis; confirmed data is inserted and unresolved official-form fields are clean blank lines.

First real build record (5 September 2026): the manual `pdflatex`, `bibtex`, `makeindex`, `pdflatex`, `pdflatex` sequence completed successfully from this directory. The converged PDF has 104 physical pages; Chapter 1 begins on physical page 18 with Arabic page 1, and the main content runs through physical page 96 / Arabic page 79. The build has 25 resolved bibliography items, 18 figure-list entries, 29 table-list entries, and 5 algorithm-list entries. No undefined citations/references, duplicate destinations, missing images, or overfull boxes remain. Harmless underfull boxes and two longtable glue-shrink diagnostics remain after visual inspection. Administrative placeholders, the manual Romanian-language check, Faculty approval of the List of Algorithms, and the separate whole-thesis review remain open; this draft is not marked submission-ready.

Pre-final cleanup build record (6 September 2026): the documented multi-pass sequence completed successfully and produced 99 physical pages. The preliminary matter occupies 14 physical pages: two title pages followed by Roman pages i--xii. Chapter 1 begins on physical page 15 / Arabic page 1, and the main chapters conclude on physical page 93 / Arabic page 79. The bibliography occupies physical pages 94--96 / Arabic pages 80--82, the source-code appendix is physical page 97 / Arabic page 83, the index is physical page 98 / Arabic page 84, and the official logo page is physical page 99. The build contains 25 bibliography items, 18 figure-list entries, 29 table-list entries, 5 algorithm-list entries, and 18 alphabetical-index entries. No undefined citations or references, duplicate destinations, missing images, or overfull boxes remain. The existing underfull-box notices and two longtable glue-shrink diagnostics remain visually harmless. The optional empty pages were removed, administrative unknowns are presented as blank form fields, and the separate final Astra whole-thesis audit remains open.

## Final whole-thesis audit and build record — 6 September 2026

- [x] Complete the audit before substantial prose edits: all included thesis files, Chapters 1–8, front matter, both summaries, bibliography use, appendix, index, planning/register files, and the complete rendered PDF.
- [x] Apply the confirmed study-programme correction and high-confidence academic/technical corrections documented in [`../FINAL_THESIS_AUDIT.md`](../FINAL_THESIS_AUDIT.md), without a chapter-wide rewrite.
- [x] Verify precise checked-in technology/configuration claims through selective read-only repository inspection; remove remaining thesis-preparation instructions. The open experimental-runtime questions above remain open.
- [x] Verify Introduction-to-Conclusions promises, established-method versus personal-contribution attribution, terminology, citation use, summary alignment, and all authoritative evaluation results and interpretations.
- [x] Review all 18 figures, 29 tables, and 5 algorithms; preserve all screenshot, diagram, and plot assets.
- [x] Rebuild through the documented `pdflatex`, `bibtex`, `makeindex`, and repeated `pdflatex` workflow; inspect the rebuilt pages and confirm converged navigation and index.
- [x] Confirm final output: **99 physical pages**, with main Chapters 1–8 on **Arabic pages 1–79** (physical pages 15–93). Bibliography is Arabic 80–82, source-code appendix 83, and index 84; the closing logo page is physical page 99.
- [x] Confirm 25 resolved bibliography entries, 18 figure-list entries, 29 table-list entries, 5 algorithm-list entries, and 18 index entries; no undefined citations/references, duplicate labels/destinations, missing images, or visible overfull/clipped content.
- [x] Visually confirm Romanian diacritics and the corrected declaration, preserve the title-page study field and all Department fields, and retain blank authorized-completion fields.
- [x] Synchronize `ace-thesis.pdf` and `../output/AI_Document_Assistant_Thesis_Draft.pdf`; verify protected evaluation hashes and `git diff --check`.
- [x] Confirm no application code/configuration, tests, evaluation data, image assets, bibliography metadata, or class changes; no application builds/tests, web research, commits, or pushes.

Final assessment: **READY FOR SUPERVISOR/SUBMISSION REVIEW**. This supersedes the readiness status in the dated earlier build records, not their historical facts. Existing harmless underfull-box notices, two longtable glue-shrink diagnostics, and class command-change warnings were visually assessed and left alone. Signing dates, signatures, supervisor/Faculty completion and approvals, official submission details, and an actual-size print proof remain manual handoff items; they are not marked complete by this audit.

## Verified items already complete

- [x] Official 2026 FACE template selected as the only thesis base.
- [x] Official logos, class, cover/page architecture, front-matter order, numbering split, bibliography mechanism, appendices, and index preserved.
- [x] English working title applied to the title page and thesis documentation.
- [x] Main Chapters 1--7 and separate Chapter 8 conclusions drafted in English.
- [x] Romanian and English project summaries updated with the completed, verified experimental results.
- [x] Ten conceptual Mermaid diagram sources created.
- [x] Five UI screenshots and three measured evaluation plots integrated; the original placeholders have been replaced.
- [x] One full backend test run completed: 345 passed, 0 failed, 0 skipped. It must not be repeated for this drafting task.
