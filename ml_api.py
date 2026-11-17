from __future__ import annotations

from datetime import date
from typing import Any

from fastapi import FastAPI, HTTPException, Query
from pydantic import BaseModel, Field

from ai_crm.llm import generate_recommendation
from ai_crm.segmentation import analyze_customer_segments
from ai_crm.service import get_customer_intelligence, get_retriever


app = FastAPI(
    title="AI Customer Intelligence API",
    version="0.1.0",
    description="Segmentation, prediction scores and evidence-grounded RAG recommendations.",
)


class CustomerScore(BaseModel):
    customer_id: str
    segment: str
    recency_days: int
    orders_365d: int
    spend_365d: float
    avg_order_value: float
    campaign_click_rate: float
    churn_probability: float
    purchase_probability: float
    priority_score: float


class RecommendationRequest(BaseModel):
    customer_id: str = Field(min_length=3, max_length=32)
    question: str = Field(min_length=5, max_length=800)
    clusters: int = Field(default=4, ge=2, le=8)


class SegmentCustomer(BaseModel):
    customer_id: str = Field(min_length=1, max_length=40)
    recency_days: float = Field(default=365, ge=0)
    orders_365d: float = Field(default=0, ge=0)
    spend_365d: float = Field(default=0, ge=0)
    avg_order_value: float = Field(default=0, ge=0)
    sessions_30d: float = Field(default=0, ge=0)
    product_views_30d: float = Field(default=0, ge=0)
    cart_adds_30d: float = Field(default=0, ge=0)
    campaign_click_rate: float = Field(default=0, ge=0, le=1)
    support_cases_90d: float = Field(default=0, ge=0)
    enquiries_90d: float = Field(default=0, ge=0)
    tenure_days: float = Field(default=0, ge=0)


class SegmentationRequest(BaseModel):
    customers: list[SegmentCustomer] = Field(min_length=3, max_length=10_000)
    clusters: int = Field(default=4, ge=2, le=8)


class TenantRecommendationRequest(BaseModel):
    question: str = Field(min_length=5, max_length=800)
    customer_context: dict[str, Any]


def _scores(clusters: int):
    scored, metrics, tables = get_customer_intelligence(clusters)
    return scored, metrics, tables


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok", "service": "ai-customer-intelligence"}


@app.get("/customers", response_model=list[CustomerScore])
def customers(clusters: int = Query(default=4, ge=2, le=8)) -> list[dict[str, Any]]:
    scored, _, _ = _scores(clusters)
    columns = [
        "customer_id",
        "segment",
        "recency_days",
        "orders_365d",
        "spend_365d",
        "avg_order_value",
        "campaign_click_rate",
        "churn_probability",
        "purchase_probability",
        "priority_score",
    ]
    return scored[columns].to_dict(orient="records")


@app.get("/metrics")
def metrics(clusters: int = Query(default=4, ge=2, le=8)) -> dict[str, Any]:
    scored, model_metrics, tables = _scores(clusters)
    snapshot = date.fromisoformat(str(tables["activity"]["snapshot_date"].iloc[0]))
    return {
        "customer_count": int(len(scored)),
        "transaction_count": int(len(tables["transactions"])),
        "snapshot_date": snapshot.isoformat(),
        "segments": scored["segment"].value_counts().to_dict(),
        "models": model_metrics,
    }


@app.post("/segmentation")
def segment_tenant_customers(request: SegmentationRequest) -> dict[str, Any]:
    """Analyze only minimized tenant rows already authorized by the CRM API."""
    try:
        records = [customer.model_dump() for customer in request.customers]
        return analyze_customer_segments(records, request.clusters)
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@app.post("/assistant/recommendations")
def assistant_recommendation(request: RecommendationRequest) -> dict[str, Any]:
    scored, _, _ = _scores(request.clusters)
    rows = scored.loc[scored["customer_id"].eq(request.customer_id)]
    if rows.empty:
        raise HTTPException(status_code=404, detail="Customer not found")
    customer = rows.iloc[0].to_dict()
    try:
        return generate_recommendation(request.question, customer, get_retriever())
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"RAG recommendation unavailable: {exc}") from exc


@app.post("/assistant/tenant-recommendations")
def tenant_assistant_recommendation(request: TenantRecommendationRequest) -> dict[str, Any]:
    """Generate a RAG answer from authorized CRM aggregates supplied by ASP.NET."""
    try:
        return generate_recommendation(request.question, request.customer_context, get_retriever())
    except Exception as exc:
        raise HTTPException(status_code=503, detail=f"RAG recommendation unavailable: {exc}") from exc

