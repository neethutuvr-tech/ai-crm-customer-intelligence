from __future__ import annotations

from functools import lru_cache
from typing import Any

from .data import load_tables
from .features import build_customer_features
from .models import recommended_action, score_customers
from .retrieval import HybridRetriever


@lru_cache(maxsize=7)
def _build_customer_intelligence(clusters: int):
    tables = load_tables()
    features = build_customer_features(tables)
    scored, model_metrics = score_customers(features, tables["outcomes"], clusters=clusters)
    return scored, model_metrics, tables


def get_customer_intelligence(clusters: int = 4) -> tuple[Any, dict[str, dict[str, float]], dict[str, Any]]:
    return _build_customer_intelligence(clusters)


@lru_cache(maxsize=1)
def get_retriever() -> HybridRetriever:
    return HybridRetriever()


def add_recommendations(scored, threshold: float = 0.65):
    result = scored.copy()
    result["recommended_action"] = [
        recommended_action(churn, purchase, threshold)
        for churn, purchase in zip(result["churn_probability"], result["purchase_probability"])
    ]
    return result


def customer_by_id(scored, customer_id: str) -> dict[str, Any] | None:
    rows = scored.loc[scored["customer_id"].eq(customer_id)]
    if rows.empty:
        return None
    return rows.iloc[0].to_dict()

