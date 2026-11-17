from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ai_crm.llm import generate_recommendation  # noqa: E402
from ai_crm.service import get_customer_intelligence, get_retriever  # noqa: E402


def main() -> None:
    cases = [
        json.loads(line)
        for line in (ROOT / "research" / "rag_eval.jsonl").read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    scored, _, _ = get_customer_intelligence()
    customer_by_id = scored.set_index("customer_id")
    retriever = get_retriever()
    rows: list[dict[str, object]] = []

    for case in cases:
        customer = customer_by_id.loc[case["customer_id"]].to_dict()
        started = time.perf_counter()
        answer = generate_recommendation(case["question"], customer, retriever)
        elapsed_ms = (time.perf_counter() - started) * 1000
        expected = set(case["relevant_sources"])
        retrieved = {source["source_id"] for source in answer.get("sources", [])}
        audit = answer.get("citation_audit", {})
        rows.append(
            {
                "case_id": case["case_id"],
                "llm_provider": answer.get("llm_provider", "none"),
                "latency_ms": round(elapsed_ms, 1),
                "relevant_source_recall": len(expected & retrieved) / max(1, len(expected)),
                "citation_validity_rate": audit.get("citation_validity_rate"),
                "invalid_citation_count": audit.get("invalid_citation_count", 0),
                "recommended_action_count": len(answer.get("recommended_actions", [])),
                "answer_summary": answer.get("summary", ""),
                "retrieved_sources": " | ".join(sorted(retrieved)),
            }
        )

    output_path = ROOT / "research" / "rag_benchmark_results.csv"
    frame = pd.DataFrame(rows)
    frame.to_csv(output_path, index=False)
    print(frame[["case_id", "llm_provider", "latency_ms", "relevant_source_recall", "citation_validity_rate"]].to_string(index=False))
    print(f"\nHuman review is still needed for citation support and answer quality. Results: {output_path}")


if __name__ == "__main__":
    main()

