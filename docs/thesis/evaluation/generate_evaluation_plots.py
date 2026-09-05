"""Validate workbook cells and generate the three Chapter 7 vector PDF plots.

Run with Python 3 and Matplotlib; --check-only needs only the standard library.
The workbook is opened read-only. No spreadsheet engine or LaTeX is required.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile


HERE = Path(__file__).resolve().parent
WORKBOOK = HERE / "evaluation_workbook_final.xlsx"
OUTPUT = HERE.parent / "FACE_thesis_template" / "img" / "evaluation"
NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def read_workbook():
    """Read OOXML values, formula text and cached results without changing XLSX."""
    sheets = {}
    with ZipFile(WORKBOOK) as archive:
        strings = []
        if "xl/sharedStrings.xml" in archive.namelist():
            strings = [
                "".join(item.itertext())
                for item in ET.fromstring(archive.read("xl/sharedStrings.xml"))
                .findall("s:si", NS)
            ]
        relationships = {
            item.attrib["Id"]: item.attrib["Target"]
            for item in ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        }
        for sheet in ET.fromstring(archive.read("xl/workbook.xml")).find("s:sheets", NS):
            relation = sheet.attrib[
                "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"
            ]
            target = relationships[relation]
            target = target.lstrip("/") if target.startswith("/") else "xl/" + target
            cells = {}
            for cell in ET.fromstring(archive.read(target)).findall(
                ".//s:sheetData/s:row/s:c", NS
            ):
                raw, inline, formula = (
                    cell.find("s:v", NS), cell.find("s:is", NS), cell.find("s:f", NS)
                )
                value = raw.text if raw is not None else None
                kind = cell.attrib.get("t", "n")
                if inline is not None:
                    value = "".join(inline.itertext())
                elif kind == "s":
                    value = strings[int(value)]
                elif kind == "n" and value is not None:
                    value = float(value)
                cells[cell.attrib["r"]] = {
                    "value": value, "formula": formula.text if formula is not None else None
                }
            sheets[sheet.attrib["name"]] = cells
    return sheets


def extract_metrics():
    sheets = read_workbook()

    def value(sheet, address):
        return sheets[sheet].get(address, {}).get("value")

    def rows(sheet):
        return sorted(
            int(address[1:]) for address in sheets[sheet]
            if re.fullmatch(r"A\d+", address) and int(address[1:]) > 1
            and value(sheet, address) is not None
        )

    questions = rows("Questions")
    metrics = {}

    def record(key, numerator, denominator, source_range, summary_cell, formula):
        summary = sheets["Summary"][summary_cell]
        if summary["formula"] != formula:
            raise ValueError(f"Review changed formula at Summary!{summary_cell}")
        ratio = numerator / denominator
        if not math.isclose(ratio, summary["value"], rel_tol=0, abs_tol=1e-12):
            raise ValueError(f"Row calculation differs from Summary!{summary_cell}")
        metrics[key] = {
            "numerator": numerator, "denominator": denominator,
            "percentage": 100 * ratio, "source_range": source_range,
            "summary_cell": "Summary!" + summary_cell,
            "formula": summary["formula"], "cached_ratio": summary["value"]
        }

    rank_rows = [r for r in questions if isinstance(value("Questions", f"H{r}"), float)]
    for column in "IJK":
        if rank_rows != [r for r in questions if isinstance(value("Questions", f"{column}{r}"), float)]:
            raise ValueError("Retrieval stages have different rankable question sets")
    if any(value("Questions", f"J{r}") != value("Questions", f"K{r}") for r in rank_rows):
        raise ValueError("Reranked and Final ranks differ; review combined reporting")
    for stage, column, summary_column in (
        ("vector", "H", "B"), ("hybrid", "I", "C"), ("final", "J", "D")
    ):
        ranks = [value("Questions", f"{column}{r}") for r in rank_rows]
        if any(rank < 1 or not rank.is_integer() for rank in ranks):
            raise ValueError("Ranks must be positive integers")
        source = f"Questions!{column}{questions[0]}:{column}{questions[-1]}"
        for cutoff, row in ((1, 4), (3, 5), (8, 6)):
            formula = (
                f'IFERROR(COUNTIFS({source},">=1",{source},"<={cutoff}")'
                f'/COUNT({source}),0)'
            )
            record(f"{stage}_top{cutoff}", sum(r <= cutoff for r in ranks),
                   len(ranks), source, f"{summary_column}{row}", formula)

    for key, column, summary_cell in (
        ("citation", "L", "B9"), ("answer", "M", "B10"), ("safety", "N", "B11")
    ):
        scores = [value("Questions", f"{column}{r}") for r in questions]
        if any(score not in ("Yes", "No", "N/A") for score in scores):
            raise ValueError(f"Missing or unexpected {key} score")
        source = f"Questions!{column}{questions[0]}:{column}{questions[-1]}"
        formula = (
            f'IFERROR(COUNTIF({source},"Yes")/(COUNTIF({source},"Yes")'
            f'+COUNTIF({source},"No")),0)'
        )
        record(key, scores.count("Yes"), scores.count("Yes") + scores.count("No"),
               source, summary_cell, formula)

    for key, sheet, column, summary_cell in (
        ("m10_type", "M10 Understanding", "I", "B12"),
        ("m10_language", "M10 Understanding", "J", "B13"),
        ("m11", "M11-M12 OCR", "I", "B14"),
        ("m12", "M11-M12 OCR", "J", "B15")
    ):
        doc_rows = rows(sheet)
        scores = [value(sheet, f"{column}{r}") for r in doc_rows]
        if any(score not in ("Yes", "No") for score in scores):
            raise ValueError(f"Missing or unexpected {key} score")
        # Recheck the exact-label and selected-page results against their gold cells.
        expected, actual = {"m10_type": ("B", "G"), "m10_language": ("C", "H"),
                            "m11": ("B", "F"), "m12": ("D", "H")}[key]
        for row, score in zip(doc_rows, scores):
            same = value(sheet, f"{expected}{row}") == value(sheet, f"{actual}{row}")
            if same != (score == "Yes"):
                raise ValueError(f"Review {key} gold/observed cells on row {row}")
        source = f"'{sheet}'!{column}{doc_rows[0]}:{column}{doc_rows[-1]}"
        total = len(doc_rows)
        formula = f'IF(COUNTA({source})<{total},"",COUNTIF({source},"Yes")/{total})'
        record(key, scores.count("Yes"), total, source, summary_cell, formula)

    return {
        "workbook": WORKBOOK.name,
        "workbook_sha256": hashlib.sha256(WORKBOOK.read_bytes()).hexdigest(),
        "sheets": list(sheets), "question_count": len(questions),
        "rankable_questions": [value("Questions", f"A{r}") for r in rank_rows],
        "excluded_from_ranking": {
            value("Questions", f"A{r}"): value("Questions", f"G{r}")
            for r in questions if r not in rank_rows
        },
        "safety_questions": [value("Questions", f"A{r}") for r in questions
                             if value("Questions", f"N{r}") in ("Yes", "No")],
        "retrieval_definition": "Fraction of numeric first-relevant ranks <= k; any valid gold source counts.",
        "final_rank_check": "Questions J and K agree for every rankable question.",
        "m12_scope": "Recorded document-level routing/content pass; not character accuracy. Page selection also checked against gold.",
        "metrics": metrics
    }


def generate_plots(data, preview_dir=None):
    os.environ.setdefault("MPLCONFIGDIR", str(Path(tempfile.gettempdir()) / "thesis-evaluation-mpl"))
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import PercentFormatter

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10,
                         "pdf.fonttype": 42, "axes.spines.top": False,
                         "axes.spines.right": False, "axes.spines.left": False,
                         "axes.spines.bottom": False, "axes.axisbelow": True})
    metrics = data["metrics"]
    colors = ("#315A7D", "#287D80", "#946821")

    def percentage(key):
        return metrics[key]["percentage"]

    def formatted(number):
        return f"{number:.1f}".rstrip("0").rstrip(".") + "%"

    def save(fig, stem):
        OUTPUT.mkdir(parents=True, exist_ok=True)
        fig.savefig(OUTPUT / (stem + ".pdf"), metadata={
            "Title": stem.replace("_", " "), "Creator": "Matplotlib " + matplotlib.__version__,
            "CreationDate": None, "ModDate": None
        })
        if preview_dir:
            preview_dir.mkdir(parents=True, exist_ok=True)
            fig.savefig(preview_dir / (stem + ".png"), dpi=180)
        plt.close(fig)

    fig, ax = plt.subplots(figsize=(7.0, 3.65))
    fig.subplots_adjust(left=0.11, right=0.985, bottom=0.19, top=0.85)
    for offset, (stage, label, color) in enumerate(zip(
        ("vector", "hybrid", "final"), ("Vector", "Hybrid", "Reranked / Final"), colors
    )):
        bars = ax.bar([i + (offset - 1) * 0.24 for i in range(3)],
                      [percentage(f"{stage}_top{k}") for k in (1, 3, 8)],
                      width=0.22, color=color, label=label)
        for bar in bars:
            ax.text(bar.get_x() + bar.get_width() / 2, bar.get_height() - 3.5,
                    formatted(bar.get_height()), ha="center", va="top", color="white", fontsize=9)
    ax.set(ylim=(0, 100), xticks=range(3),
           xticklabels=("Top-1 accuracy", "Top-3 hit rate", "Top-8 hit rate"), ylabel="Questions meeting cutoff")
    ax.yaxis.set_major_formatter(PercentFormatter(100, decimals=0))
    ax.grid(axis="y", color="#D9DEE3", linewidth=0.6)
    ax.tick_params(length=0)
    ax.legend(loc="lower center", bbox_to_anchor=(0.5, 1.02), ncol=3, frameon=False)
    fig.text(0.11, 0.035, f"n = {len(data['rankable_questions'])} rankable questions; first relevant evidence across the valid gold set.", fontsize=9)
    save(fig, "evaluation_retrieval_metrics")

    def horizontal(keys, labels, stem, footnote):
        fig, ax = plt.subplots(figsize=(7.0, 3.65))
        fig.subplots_adjust(left=0.40, right=0.955, bottom=0.23, top=0.97)
        display_labels = [f"{label}\n({metrics[key]['numerator']}/{metrics[key]['denominator']})"
                          for key, label in zip(keys, labels)]
        bars = ax.barh(range(len(keys)), [percentage(key) for key in keys],
                       color=colors[0], height=0.62)
        for bar in bars:
            ax.text(bar.get_width() - 2.5, bar.get_y() + bar.get_height() / 2,
                    formatted(bar.get_width()), ha="right", va="center", color="white", fontsize=10)
        ax.set(xlim=(0, 100), yticks=range(len(keys)), yticklabels=display_labels, xlabel="Recorded correctness")
        ax.invert_yaxis()
        ax.xaxis.set_major_formatter(PercentFormatter(100, decimals=0))
        ax.grid(axis="x", color="#D9DEE3", linewidth=0.6)
        ax.tick_params(length=0, pad=6)
        fig.text(0.025, 0.035, footnote, fontsize=9)
        save(fig, stem)

    horizontal(("citation", "answer", "safety"),
               ("Citation accuracy", "Answer accuracy", "No-evidence / security"),
               "evaluation_answer_metrics",
               "Question-level scores; N/A entries excluded from each metric's denominator.")
    horizontal(("m10_type", "m10_language", "m11", "m12"),
               ("M10 Document type", "M10 Language", "M11 Technical PDF type", "M12 OCR routing/content"),
               "evaluation_document_processing_metrics",
               "Document-level scores; M12 does not measure character-level OCR accuracy.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true", help="Validate and print metrics without writing files")
    parser.add_argument("--preview-dir", type=Path, help="Optional PNG previews outside the thesis assets")
    args = parser.parse_args()
    data = extract_metrics()
    serialized = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    if args.check_only:
        print(serialized, end="")
    else:
        generate_plots(data, args.preview_dir)
        (HERE / "evaluation_metrics_verified.json").write_text(serialized, encoding="utf-8")
        print(f"Validated {data['question_count']} questions; generated three PDF plots in {OUTPUT}")


if __name__ == "__main__":
    main()
