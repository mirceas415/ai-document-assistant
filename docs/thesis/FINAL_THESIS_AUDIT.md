# Final Whole-Thesis Academic Audit

Completed: 6 September 2026. Recommendation: **READY FOR SUPERVISOR/SUBMISSION REVIEW**, subject to the administrative completion listed below. This is an academic and production-readiness assessment, not Faculty approval or authorization to sign official forms.

## Scope and method

The complete thesis was reviewed before prose corrections: the root and all 17 included files, official front matter, both summaries, Chapters 1–8, bibliography use, source-code appendix, index, `FACT_CHECK_AND_TODO.md`, and `THESIS_OUTLINE.md`. The existing PDF was reviewed throughout, including all 18 figures, 29 tables, and 5 algorithms. Following the user's continuation instruction, completed reading was retained and only unresolved details and changed pages were revisited. Corrections were surgical; chapter structure, technical depth, verified results, bibliography metadata, and graphical assets were preserved.

## Prioritized findings and disposition

### P0 — corrected before handoff

1. **Study programme confused with study field.** The Declaration of Originality identified the candidate as a graduate of the field rather than the programme. It now says `absolvent al programului de studii \textsc{Calculatoare (în limba engleză)}`. The completion register's corresponding incorrect programme entry was also corrected. Domain and department fields were not globally replaced.
2. **Residual thesis-preparation language.** Chapter 6 still instructed the writer to check versions before final submission; that sentence was removed after selective repository verification. Chapter 4's instruction-like statement about retaining a Tesseract label until runtime verification was replaced by a factual limitation: wrapper/fallback versions do not establish the exact native engine used in the experiment.

No inconsistent evaluation value or concealed recorded failure was found.

### P1 — high-confidence corrections applied

- **Ingestion order:** Chapter 4's opening sequence now places technical PDF analysis before extraction, matching the processing service and pipeline explanation. Its organizational sentence no longer asserts that the section order is the execution order.
- **Semantic eligibility:** Chapter 5 no longer claims that the SQL query independently validates every sibling chunk before admitting any document chunk. It accurately distinguishes document aggregate checks from candidate-level model, dimension, timestamp, and content-hash checks.
- **OCR pseudocode:** Algorithm 4.2 now states the configured upper rendering limit of 300 DPI as well as the 25,000,000-pixel constraint.
- **Claim strength:** Chapter 2 describes conversation-history handling as a model instruction, not a guarantee of factual isolation, and removes the unsupported superlative “safest” for fail-open reranking. Chapter 5 states that limits bound request size and waiting time, without implying measured or predictable provider cost.
- **Filename versus evidence:** The metadata table now distinguishes a filename's source-identification role from factual support; it no longer implies that source filenames never appear in the answer context.
- **Navigation:** Added the missing Bibliography entry/link to the Contents and made the supervisor-form Contents entry match its actual heading, “Referatul coordonatorului științific”.
- **Romanian summary:** Replaced the awkward legal-sounding ownership phrase with access control according to the workspace owner, preserving the intended technical meaning and alignment with the English summary.

### P2 — small fixes applied; larger changes deliberately omitted

Applied only a Romanian typo correction (`sfărşitul` → `sfârşitul`), expansion of GIN at its first explanatory use instead of repeating the expansion later, and a controlled Chapter 2 heading break. After visual rebuilding, a page break before Chapter 6's Authentication and Workspaces section kept the preceding technology table with its section and removed an isolated section opening.

Not applied: chapter-wide stylistic rewrites; systematic removal of “bounded”, “deterministic”, or “provenance”; broad capitalization/spelling normalization; index expansion; and aesthetic page-count reduction. These would add churn without a material academic benefit. The five screenshots remain useful overview illustrations, although their smallest interface labels benefit from PDF zoom; an actual-size print proof is advisable. No screenshot, diagram, or plot was altered. Historical planning notes and dated build records remain historical, not statements of the current thesis state.

## Administrative terminology: occurrence-by-occurrence result

| File / field | Semantic role | Final disposition |
| --- | --- | --- |
| `ace-firstpage.tex`: “În domeniul de studii” | Study field | Preserved **Calculatoare și Tehnologia Informației**. |
| `ace-coverpage.tex`: commented-out domain line | Study field, not rendered | Same correct field retained; not activated. |
| `ace-originality.tex`: “absolvent al programului de studii” | Study programme | Corrected to **Calculatoare (în limba engleză)**. |
| `ace-coverpage.tex` and `ace-firstpage.tex`: Department headings | Department | Preserved **Calculatoare și Tehnologia Informației**. |
| `ace-project-timetable.tex`: “Departamentul (de)” | Department | Existing FACE department wording preserved. No programme field added. |
| `ace-supervisor-remarks.tex`: Department field | Department | Existing FACE department wording preserved. No programme field added. |

Thus, two domain-source occurrences (one inactive), one programme occurrence, and four department occurrences were checked individually. No existing English-specific programme field required translation; **no English programme label was introduced**. Searches for programme/specialization wording found no additional compiled administrative programme field requiring correction.

Mircea Smărăndăchescu; Prof. dr. ing. Nicolae Iulian Enescu; both confirmed thesis titles; Septembrie 2026; Craiova, România; and `https://github.com/mirceas415/ai-document-assistant` remain intact. The official forms' layout and substantive template wording were preserved.

## Whole-work academic assessment

### Introduction to Conclusions: promise and evidence

The important objectives in Chapter 1 are accounted for in the implementation, verification/evaluation, and Conclusions. No objective was found to disappear or to be claimed as experimentally established without the appropriate qualification.

| Objective group | Architecture / implementation | Evidence and concluding treatment |
| --- | --- | --- |
| Authenticated upload, workspace ownership, document lifecycle | Chapters 3, 4, 6 | Chapter 7 automated verification is distinguished from the five recorded no-evidence/security cases; Chapter 8 retains security limitations. |
| PDF/DOCX extraction, raw/normalized provenance, selective OCR | Chapter 4 | Automated checks and M11/M12 controls support the recorded behavior; Chapter 8 acknowledges narrow OCR coverage. |
| Document Understanding, token-aware chunking, embeddings | Chapter 4 | Automated verification plus M10 type/language results; Conclusions retain the classification mismatch and integration contribution. |
| Hybrid retrieval, metadata soft boosting, optional fail-open reranking | Chapter 5 | Chapter 7 compares the three recorded orderings; Chapter 8 credits hybrid retrieval with the gain and reports no aggregate reranking gain. |
| Grounded answers, citations, persistent source snapshots | Chapters 3, 5, 6 | Automated verification and answer/citation evaluation are separated; Q21 remains explicit. Historical snapshots are an implemented feature, not a separately quantified experiment. |
| Usable interaction and diagnostics | Chapter 6 | Screenshots and functional/test discussion support implementation; no formal frontend, accessibility, latency, or cost evaluation is claimed. |

Chapters 1.6 and 8.2 distinguish established OCR, embeddings, RAG, RRF, reranking, PostgreSQL full-text search, and structured model output from the student's architecture, implementation, constraints, integration, diagnostics, and evaluation. The contribution is substantial engineering work, not invention of these methods. Future work follows documented limitations: broader evaluation, harder OCR, frontend/accessibility testing, latency/cost measurement, durable infrastructure, vector indexing, and alternative deployment.

### Evaluation and summaries

The workbook and verified traceability JSON were treated as read-only authorities. No experiment was rerun or redesigned. All reported counts and percentages remain:

| Measure | Verified result retained |
| --- | --- |
| Backend automated tests | 345 passed; 0 failed; 0 skipped |
| Vector Top-1 | 22/25 = 88% |
| Hybrid Top-1; final/reranked Top-1 | 24/25 = 96% each |
| Top-3; Top-8 | 25/25 = 100% at all three stages |
| Citation correctness | 28/29 = 96.6% |
| Answer correctness | 31/32 = 96.9% |
| No-evidence/security correctness | 5/5 = 100% |
| M10 document type; language exact match | 7/8 = 87.5%; 8/8 = 100% |
| M11 technical classification | 3/3 = 100% |
| M12 routing/content controls | 3/3 = 100%, not character-level OCR accuracy |

The 25 rankable cases, multi-source exclusions, equivalent OCR gold sources, staged uploads, and Q13 retest are disclosed. Q04 and Q11 improve from vector rank 2 to hybrid rank 1: **8 percentage points**, with **no additional aggregate Top-1 gain from reranking**. Q21 remains the principal answer/citation failure: an unsupported document was included although Meridian Retail was absent from it. Q28 remains a Top-1 miss with a correct answer using S2. M10's Manual prediction against TechnicalDocument remains a strict error despite semantic plausibility. Five passing security cases are not a universal security guarantee.

The Romanian and English summaries are semantically aligned on the problem, implementation, personal contribution, principal outcomes, and limited scope. Romanian diacritics were checked visually, not judged from PDF text-extraction artifacts.

### Terminology, citations, and prose

Project (backend/domain) and Workspace (interface) remain intentionally distinct. Document Understanding and the UI label Document Intelligence are explained, as are technical PDF classes, semantic document types, source sections, chunks, candidates, stage ranks, and citations. Important abbreviations are introduced adequately; GIN's first-use expansion was corrected.

Chapter 2 principally supplies established concepts, with limited implementation pointers; Chapters 4–5 carry concrete mechanics. No aggressive shortening was warranted. Citation use is appropriate to theoretical and external claims, and personal implementation claims are not attributed to literature as inventions supplied by those sources. All 25 retained bibliography entries are cited. No bibliography metadata or legitimate access dates were changed, and no web/bibliography research was performed. Repeated technical qualifications generally express real limitations; only clear overstatements and conspicuous process language were changed.

## Selective technical verification

Read-only checks were limited to concrete thesis claims:

- Server `.csproj` and client `package.json`: .NET 10, React 19, TypeScript/Vite, and the cited extraction, tokenizer, OpenAI, pgvector, rendering, and Tesseract wrapper dependencies.
- `appsettings.json`, `EmbeddingArchitecture.cs`, `Cl100kDocumentTokenizer.cs`, and `RetrievalRerankingLimits.cs`: `cl100k_base`; chunk target/max/overlap 700/900/100; 1536-dimensional embeddings; model roles; TopK 8; vector/lexical/metadata candidate limits 30/30/20; RRF constant 60 and weights 1/1/0.35; reranker defaults and limits; OCR `ron+eng`, 300 DPI, 200-page and pixel limits.
- `ApplicationDbContext.cs` and `PgvectorSemanticChunkSearch.cs`: vector dimension, generated full-text column/GIN role, exact semantic query and candidate eligibility. `SemanticRetrievalService.cs` confirmed the relevant ownership/query path.
- `DocumentProcessingService.cs`, `PdfTechnicalAnalysisHeuristics.cs`, `OcrRoutingPolicy.cs`, `PdfRenderSafety.cs`, and `TesseractOcrService.cs`: processing order, technical thresholds, Scanned-only OCR routing, DPI/pixel behavior, and wrapper/native-version distinction.

These checks verify checked-in implementation/defaults, **not a recovered experimental runtime snapshot**. The exact evaluated revision, complete runtime overrides, native Tesseract version, and database/model versions cannot be established from the recorded evaluation alone. The thesis discloses that limitation. The authoritative backend test result was accepted without rerunning tests.

## Figures, tables, algorithms, and final production

All 18 figures (ten conceptual diagrams, UI-02/UI-03/UI-04/UI-07/UI-10, and three evaluation plots), 29 tables, and five algorithms were reviewed for explanation, references, caption/header meaning, numbers, and visible fit. The plots agree with the tables; pseudocode agrees with the described implementation after the OCR limit correction. No missing asset, broken cross-reference, or material caption defect remains. The 18-entry alphabetical index is primarily technical and useful; the source appendix is concise and retains the confirmed repository without inventing deployment or submission details.

The documented `pdflatex → bibtex → makeindex → pdflatex → pdflatex` workflow completed successfully, with additional passes as needed for layout and reference convergence. The final source PDF and review copy are byte-identical.

- **99 physical A4 pages**, unchanged from the audited input (the approximate 98-page expectation was not an exact count).
- Preliminary matter: physical pages 1–14; two title pages followed by Roman i–xii.
- **Main Chapters 1–8: Arabic pages 1–79**, physical pages 15–93.
- Bibliography: Arabic 80–82; source-code appendix: 83; index: 84; closing logo page: physical 99.
- Contents, List of Figures (18), List of Tables (29), List of Algorithms (5), bibliography (25), and index (18) converge and match the document.
- No undefined citations/references, duplicate labels/destinations, missing images, overfull boxes, visible clipping, or stale drafting placeholders remain. Blank official-form lines are intentional. Existing underfull notices, two longtable glue-shrink diagnostics, and class command-change warnings have no observed visible defect and were not chased.
- Final PDF: `C:\Users\Mircea\source\repos\AI.DocumentAssistant\docs\thesis\output\AI_Document_Assistant_Thesis_Draft.pdf`.
- Final PDF SHA-256: `51AB77590DCB4E1F7E4E7802C71B565CF5157CEA6C389D344E9E301FE9ACF3E1`.
- `git diff --check`: passed.

## Remaining administrative/manual items

The candidate/supervisor/Faculty must supply or confirm signing and assignment/delivery/approval dates, signatures, department-director details, consultation schedule, project-sheet specifications, supervisor checkboxes/ratings/comments/decision, the applicable final Faculty form and binding requirements, acceptance of the List of Algorithms, and the actual electronic source-code submission mechanism. None was invented or marked complete. An actual-size print proof remains prudent, particularly for screenshot detail. These are handoff items, not grounds for claiming the thesis has already been formally accepted.

## Files changed and safety verification

Edited thesis sources: `ace-originality.tex`, `ace-supervisor-remarks.tex`, `ace-summary.tex`, `ace-chap02.tex`, `ace-chap04.tex`, `ace-chap05.tex`, `ace-chap06.tex`, and `ace-thesis.tex`, all under `docs/thesis/FACE_thesis_template/`. Updated `FACT_CHECK_AND_TODO.md`; created this audit; regenerated `FACE_thesis_template/ace-thesis.pdf` and `output/AI_Document_Assistant_Thesis_Draft.pdf`. No entire chapter was rewritten.

Temporary PDF-review rasters, extracted text, and rendering helpers remain untracked under `output/final_audit_review/`. They were generated solely for this audit; the environment rejected their cleanup command, so they were left in place. They are not thesis image assets or required submission files and can be regenerated from the PDFs.

Protected evaluation hashes match their pre-edit values:

- Workbook: `FEE08FF2F604A7DE1009359916290A634A865ACB45B849EF7FC717F1D86F0E18`.
- Verified JSON: `800C590E77F24AAF2AC32A2D673ADD7F766DB999BCA98512FDEC4735C8CACF90`.

Application code/configuration, tests, evaluation data/corpus/results/plots, screenshots, diagrams, bibliography metadata, and the template class were untouched. No application builds/tests, new experiments, installations, web browsing, Git configuration changes, commits, or pushes were performed. Final changes are confined to `docs/thesis`.
