from __future__ import annotations

import json
import sys
from pathlib import Path

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ai_crm.service import get_retriever  # noqa: E402


def ndcg_at_k(ranked: list[str], relevant: set[str], k: int) -> float:
    gains = [1.0 if source in relevant else 0.0 for source in ranked[:k]]
    dcg = sum(value / __import__("math").log2(index + 2) for index, value in enumerate(gains))
    ideal = sum(1.0 / __import__("math").log2(index + 2) for index in range(min(len(relevant), k)))
    return dcg / ideal if ideal else 0.0


def main() -> None:
    eval_path = ROOT / "research" / "retrieval_eval.jsonl"
    rows = [json.loads(line) for line in eval_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    retriever = get_retriever()
    modes = ["bm25", "dense", "hybrid", "hybrid_reranked"]
    results: list[dict[str, object]] = []
    k = 5
    for row in rows:
        relevant = set(row["relevant_sources"])
        for mode in modes:
            ranked = retriever.rank_for_evaluation(row["query"], mode=mode, top_k=k)
            first_relevant = next((index + 1 for index, source in enumerate(ranked) if source in relevant), None)
            results.append(
                {
                    "query_id": row["query_id"],
                    "mode": mode,
                    "recall_at_5": len(set(ranked[:k]) & relevant) / max(1, len(relevant)),
                    "mrr_at_5": 1.0 / first_relevant if first_relevant else 0.0,
                    "ndcg_at_5": ndcg_at_k(ranked, relevant, k),
                    "ranked_sources": " | ".join(ranked),
                }
            )

    frame = pd.DataFrame(results)
    output_path = ROOT / "research" / "retrieval_benchmark_results.csv"
    frame.to_csv(output_path, index=False)
    summary = frame.groupby("mode")[["recall_at_5", "mrr_at_5", "ndcg_at_5"]].mean().sort_values("ndcg_at_5", ascending=False)
    print(summary.round(3).to_string())
    print(f"\nPer-query results written to {output_path}")


if __name__ == "__main__":
    main()

