# Figure Capture Plan

This plan covers every manual application screenshot referenced by the LaTeX draft. Only synthetic or explicitly redistributable evaluation documents may be used. Before capturing, use a dedicated demonstration account with a neutral display name, close developer tools, clear unrelated conversations, and verify that no API key, connection string, local storage path, private filename, browser autofill value, notification, bookmark, or unrelated personal information is visible.

## Conceptual diagrams — GENERATED / RENDERED

The ten conceptual Mermaid sources were reviewed, rendered to vector PDF, and integrated into the LaTeX chapters. Their editable sources remain under `diagrams/`, and their thesis-ready assets are under `img/diagrams/`.

- **DIA-01 — GENERATED / RENDERED:** high-level system architecture.
- **DIA-02 — GENERATED / RENDERED:** workspace/domain model.
- **DIA-03 — GENERATED / RENDERED:** document-ingestion pipeline.
- **DIA-04 — GENERATED / RENDERED:** technical PDF classification flow.
- **DIA-05 — GENERATED / RENDERED:** selective OCR flow.
- **DIA-06 — GENERATED / RENDERED:** chunking with bounded overlap.
- **DIA-07 — GENERATED / RENDERED:** hybrid retrieval pipeline.
- **DIA-08 — GENERATED / RENDERED:** weighted RRF concept.
- **DIA-09 — GENERATED / RENDERED:** model-based reranking flow.
- **DIA-10 — GENERATED / RENDERED:** grounded citation/source flow.

The five manual screenshots below are complete and integrated into Chapter 6. The original capture guidance is retained as an audit of what each figure was intended to demonstrate.

The five screenshots below are intentionally the minimum useful UI set. Other suggested IDs (`UI-01`, `UI-05`, `UI-06`, `UI-08`, `UI-09`, and `UI-11`) are not referenced in the first draft because their information is already represented by conceptual diagrams or would substantially overlap these figures. They may be added later only if the final editorial pass identifies a concrete explanatory gap.

The approved captures are stored under `img/screenshots/` as lossless PNG files named by figure ID. Their Chapter 6 figures use normal centered `\includegraphics` blocks and retain the original `\label` values.

## UI-02 — Grounded answer with multiple citations — COMPLETE / INTEGRATED

- **Chapter / section:** Chapter 6, Section 6.3, Chat-First Interface; also supports Section 6.5.
- **Integrated asset:** `img/screenshots/UI-02.png`.
- **Integrated result:** the evaluation workspace shows the NovaTel/BlueGrid comparison question, a grounded answer with two inline citations, two authoritative source cards, and the workspace-scoped composer.
- **Purpose:** show the principal user workflow in one image: active workspace, persistent conversation, user question, grounded assistant answer, inline citation markers, source cards, and composer.
- **Exact application screen:** chat workspace with one completed assistant response; conversation navigation visible at the left and the advanced retrieval panel collapsed.
- **Preparation steps:**
  1. Sign in with a synthetic demonstration account.
  2. Create or select a workspace named `Multilingual evaluation`.
  3. Upload and fully process two synthetic contracts whose facts are complementary and whose filenames contain no personal information.
  4. Start a new chat and ask the comparison question below.
  5. Wait until the answer and source cards are fully rendered; collapse transient status elements and dismiss toasts.
- **Recommended synthetic documents:** `contract_CN-2026-00491.pdf` and `contract_CN-2026-00512.pdf`, each containing one clearly labelled fictional notice or early-termination rule. Use invented organizations and amounts.
- **Question to ask:** `Compare the notice period and early-termination consequences in contracts CN-2026-00491 and CN-2026-00512. Cite each contract.`
- **UI elements that should be visible:** application brand; synthetic workspace name; selected conversation title; the complete question; a concise answer; at least two valid inline source markers; corresponding source cards; document/page or heading summaries; composer; `Documents` and `New Chat` navigation.
- **Elements that should not appear:** account email if it contains a real name; browser chrome where avoidable; developer tools; loading indicators; errors; toasts covering content; advanced retrieval details; private chat history; provider names or credentials.
- **Recommended crop:** application viewport only, landscape orientation, from the left navigation through the full answer width. Crop vertically around one complete exchange while retaining the composer and source cards. Target a readable width suitable for approximately `0.95\textwidth`.
- **Integrated caption:** `Grounded answer with multiple citations in the active workspace.`
- **Thesis sentence referencing it:** `Figure 6.x shows the final chat layout in the evaluation workspace with an answer containing multiple source markers.`

## UI-03 — Source-details modal — COMPLETE / INTEGRATED

- **Chapter / section:** Chapter 6, Section 6.5, Answers and Sources.
- **Integrated asset:** `img/screenshots/UI-03.png`.
- **Integrated result:** the `S1` dialog shows the NovaTel filename, page and chunk location, the authoritative-snapshot notice, and the saved excerpt.
- **Purpose:** demonstrate the authoritative provenance exposed for one citation and distinguish the bounded source snapshot from the generated answer.
- **Exact application screen:** source-details modal opened from the `S1` inline marker or source card in the UI-02 conversation.
- **Preparation steps:**
  1. Reuse the completed synthetic conversation prepared for UI-02.
  2. Select the citation whose supporting chunk has the clearest page range and heading.
  3. Open its source-details modal.
  4. Confirm that the displayed excerpt is safe, fictional, and legible and that no background toast or menu remains open.
- **Recommended synthetic document:** `contract_CN-2026-00491.pdf`, with a heading such as `Early Termination` and a short fictional clause on page 2.
- **Question to ask:** reuse the UI-02 comparison question; do not create a separate conversation solely for this capture.
- **UI elements that should be visible:** source label (`S1` or the selected valid ID); safe filename; page range; chunk number; heading; bounded excerpt; historical-snapshot note if displayed; close control.
- **Elements that should not appear:** full document body; raw GUIDs unless the normal UI deliberately shows them; local file paths; storage filename; embeddings; browser developer tools; any real contract or organization.
- **Recommended crop:** center the modal and retain a narrow dimmed margin around it to make the dialog boundary clear. Exclude most of the underlying conversation so the excerpt remains readable.
- **Integrated caption:** `Source-details dialog showing the authoritative excerpt and its document, page, and chunk provenance.`
- **Thesis sentence referencing it:** `Selecting an inline marker or source card opens the source-details dialog shown in Figure 6.x.`

## UI-04 — Document management and processing states — COMPLETE / INTEGRATED

- **Chapter / section:** Chapter 6, Section 6.4, Document Management.
- **Integrated asset:** `img/screenshots/UI-04.png`.
- **Integrated result:** the document view shows ready mixed and scanned PDFs, one versus three OCR pages processed, technical-analysis, OCR, and Document Intelligence states, plus document-level actions.
- **Purpose:** show that documents are workspace-level reusable assets and that asynchronous processing and independent diagnostic states are visible outside a conversation.
- **Exact application screen:** the selected workspace's `Documents` page, with at least one expanded document and another recently uploaded document in a different real state.
- **Preparation steps:**
  1. Use the same synthetic `Multilingual evaluation` workspace.
  2. Ensure at least one PDF and one DOCX are fully `Ready`.
  3. Prepare a moderately sized synthetic scanned PDF in advance.
  4. Upload that PDF immediately before capture so the list legitimately contains a waiting or processing item; do not simulate a status in browser tools.
  5. Expand the ready document summary sufficiently to show its processing, normalization, chunk, and embedding facts and the available rebuild/view actions.
  6. Dismiss any success toast before capture.
- **Recommended synthetic documents:** the two fictional contracts from UI-02, a short `course_policy_synthetic.docx`, and `scanned_form_synthetic.pdf` for the in-progress item.
- **Question to ask:** not applicable.
- **UI elements that should be visible:** workspace name; upload/drop area; safe filenames; actual status badges; ready-document derived counts; controls for text/chunks or rebuild operations; headings for Technical Analysis, Local OCR, and Document Intelligence if they fit without making text unreadable.
- **Elements that should not appear:** a manufactured `Failed` status; private filenames; a real user email; confirmation dialog; error stack; local paths; hashes if they dominate the frame; unrelated workspace cards.
- **Recommended crop:** application content from the `Documents` heading through two or three document rows. Retain enough navigation to identify the workspace, but prioritize readable status badges and actions.
- **Integrated caption:** `Document management view showing processing states and document-level actions.`
- **Thesis sentence referencing it:** `Figure 6.x shows two processed PDFs with different technical classifications and OCR outcomes.`

## UI-07 — Local OCR diagnostics for a scanned PDF — COMPLETE / INTEGRATED

- **Chapter / section:** Chapter 6, Section 6.4, Document Management; supports Chapter 4, Section 4.5.
- **Integrated asset:** `img/screenshots/UI-07.png`.
- **Integrated result:** the expanded view shows a scanned three-page PDF, OCR Ready status, three processed pages, Tesseract, Romanian and English languages, and a target DPI of 300. The integrated screenshot does not show a per-page diagnostics table, so the thesis caption and figure-specific sentence are limited to the visible aggregate information.
- **Purpose:** make selective page routing and OCR provenance visible through a real application result.
- **Exact application screen:** expanded `Local OCR` panel for a successfully processed synthetic scan, including the aggregate summary and page diagnostics table.
- **Preparation steps:**
  1. Create a two- or three-page synthetic PDF in which each intended scan page is a page-sized raster image and has no meaningful native text layer.
  2. Include clear Romanian and English printed text so the configured `ron+eng` language set is exercised without using personal material.
  3. Upload the file and wait for ordinary processing to finish.
  4. Open the document, expand `Local OCR`, and expand advanced/audit details if required to expose the page table.
  5. Confirm the actual technical source type is `Scanned` and the status is the real recorded outcome; do not alter rows for appearance.
- **Recommended synthetic document:** `scanned_bilingual_notice_synthetic.pdf`, with fictional notices on page-sized scan images.
- **Question to ask:** not applicable.
- **UI elements that should be visible:** aggregate OCR status; candidate/success totals; configured languages; requested DPI or limits where the UI exposes them; page number; page OCR status; recognized character count; confidence when available; source technical type; effective DPI; `Used in extraction` information.
- **Elements that should not appear:** tessdata filesystem path; native-library path; raw exception; temporary rendered image; real scanned signature; personal identification number; provider key; unrelated document details.
- **Recommended crop:** crop tightly around the expanded OCR card and page table, retaining the synthetic filename and document status as context. Use enough horizontal width to avoid truncating diagnostic columns.
- **Integrated caption:** `OCR processing information for a scanned PDF.`
- **Thesis sentence referencing it:** `Figure 6.x shows the scanned technical classification together with the aggregate OCR outcome and processing configuration.`

## UI-10 — Advanced Retrieval Details — COMPLETE / INTEGRATED

- **Chapter / section:** Chapter 6, Section 6.6, Advanced Retrieval Diagnostics; also supports Chapter 5.
- **Integrated asset:** `img/screenshots/UI-10.png`.
- **Integrated result:** the BlueGrid Q11 case visibly places `03_bluegrid_support_agreement.pdf` at vector rank 2 and at hybrid, reranked, and final rank 1. This is documented only as one inspectable case, not as aggregate evaluation evidence.
- **Purpose:** show how semantic, lexical, metadata, fused, reranked, and final positions can be inspected without presenting the panel as an end-user requirement or an aggregate experiment.
- **Exact application screen:** chat workspace with `Retrieval details` expanded after submitting an exact-identifier diagnostic query.
- **Preparation steps:**
  1. Use the synthetic workspace containing `contract_CN-2026-00491.pdf` and at least one similar distractor contract.
  2. Ensure Document Intelligence is `Ready` and contains the fictional identifier for the target document.
  3. Open the advanced retrieval panel and submit the query below with the default TopK.
  4. Wait for results and confirm reranking was either genuinely applied or clearly marked as fallback; capture the real state.
  5. Expand or position the panel so the target result and at least one competing result are legible.
- **Recommended synthetic documents:** `contract_CN-2026-00491.pdf`, `contract_CN-2026-00887.pdf`, and an invoice containing similar termination vocabulary but no matching identifier.
- **Question to ask:** `What does contract CN-2026-00491 state about early termination?`
- **UI elements that should be visible:** query and TopK input; final rank; hybrid rank; rerank rank and relevance when applied; fused score; vector rank/distance; lexical rank/score; metadata-document rank and matched identifier signal; document name; bounded chunk excerpt; applied/fallback notice.
- **Elements that should not appear:** embedding vectors; API configuration; prompt text; raw provider response; private documents; misleading edited ranks; unrelated answer content that crowds the panel.
- **Recommended crop:** focus on the expanded diagnostics panel with the query controls and first two or three results. Include a small amount of chat chrome to establish that the panel belongs to the conversation workspace.
- **Integrated caption:** `Advanced retrieval diagnostics for the BlueGrid case, showing vector, hybrid, reranked, and final positions.`
- **Thesis sentence referencing it:** `Figure 6.x shows one BlueGrid case in which the relevant evidence moves from vector rank 2 to hybrid and final rank 1.`

## Final capture checklist

- Capture at a consistent browser size, zoom level, color theme, and synthetic account across all figures.
- Verify that every displayed status and rank is produced by the running application; do not edit screenshots to manufacture outcomes.
- Keep text readable when placed at thesis width; recapture rather than rely on excessive digital zoom.
- Remove cursor tooltips and transient hover states unless the tooltip itself is the subject.
- Record the application revision and capture date in the evaluation notes, not inside the image.
- After insertion, update the List of Figures through the normal LaTeX build and verify every figure is referenced in surrounding prose.
