from __future__ import annotations

import json
import os

import pandas as pd
import plotly.express as px
import streamlit as st

from ai_crm.config import setting
from ai_crm.data import data_quality_report
from ai_crm.llm import generate_recommendation
from ai_crm.models import recommended_action
from ai_crm.service import get_customer_intelligence, get_retriever


st.set_page_config(
    page_title="Customer Intelligence Lab",
    page_icon="✦",
    layout="wide",
    initial_sidebar_state="expanded",
)


@st.cache_data(show_spinner="Building customer features and model scores…")
def load_customer_data(cluster_count: int):
    scored, model_metrics, tables = get_customer_intelligence(cluster_count)
    return scored, model_metrics, data_quality_report(tables), tables


@st.cache_resource(show_spinner="Loading local embedding and reranker models…")
def load_retriever():
    return get_retriever()


def format_currency(value: float) -> str:
    return f"₹{value:,.0f}"


st.title("Customer Intelligence Lab")
st.caption("AI research prototype · Segmentation + propensity models + evidence-grounded CRM RAG")

with st.sidebar:
    st.subheader("Research controls")
    cluster_count = st.slider("Number of customer clusters", 2, 8, 4)
    decision_threshold = st.slider("Review threshold", 0.50, 0.90, 0.65, 0.01)
    st.markdown("---")
    st.markdown("**Data mode**")
    st.info("Generated demo data only. No names, email addresses, phone numbers or payment details.")

scored, model_metrics, quality, tables = load_customer_data(cluster_count)
scored = scored.copy()
scored["recommended_action"] = [
    recommended_action(churn, purchase, decision_threshold)
    for churn, purchase in zip(scored["churn_probability"], scored["purchase_probability"])
]

tabs = st.tabs(["Overview", "Customer book", "RAG assistant", "Research lab", "Architecture"])

with tabs[0]:
    total_revenue = float(scored["spend_365d"].sum())
    at_risk_count = int((scored["churn_probability"] >= decision_threshold).sum())
    purchase_ready = int((scored["purchase_probability"] >= decision_threshold).sum())
    high_risk_rate = at_risk_count / max(1, len(scored))
    col1, col2, col3, col4 = st.columns(4)
    col1.metric("Customers", f"{len(scored):,}")
    col2.metric("Observed 365-day sales", format_currency(total_revenue))
    col3.metric("Retention review queue", f"{at_risk_count:,}", f"{high_risk_rate:.1%} of base")
    col4.metric("High purchase propensity", f"{purchase_ready:,}")

    st.warning("All customers, outcomes and model scores are synthetic. Scores are research signals, not business decisions.")
    left, right = st.columns([1, 1.4])
    segment_summary = (
        scored.groupby("segment", as_index=False)
        .agg(customers=("customer_id", "count"), sales=("spend_365d", "sum"))
        .sort_values("customers", ascending=False)
    )
    with left:
        st.subheader("Customer segments")
        st.plotly_chart(
            px.bar(segment_summary, x="customers", y="segment", orientation="h", color="sales", color_continuous_scale="teal"),
            use_container_width=True,
        )
    with right:
        st.subheader("Recency and spend profile")
        st.plotly_chart(
            px.scatter(
                scored,
                x="recency_days",
                y="spend_365d",
                color="segment",
                size="orders_365d",
                hover_data=["customer_id", "churn_probability", "purchase_probability"],
                labels={"recency_days": "Days since last purchase", "spend_365d": "Sales in last 365 days (₹)"},
            ),
            use_container_width=True,
        )

    metric_cols = st.columns(2)
    for col, score_name, title in [
        (metric_cols[0], "churn_probability", "Churn model · held-out ROC-AUC"),
        (metric_cols[1], "purchase_probability", "Repeat-purchase model · held-out ROC-AUC"),
    ]:
        score_metrics = model_metrics[score_name]
        with col:
            st.metric(title, f"{score_metrics['roc_auc']:.3f}")
            st.caption(
                f"Precision {score_metrics['precision_at_0_5']:.2f} · Recall {score_metrics['recall_at_0_5']:.2f} · "
                f"F1 {score_metrics['f1_at_0_5']:.2f} · Positive rate {score_metrics['positive_rate']:.1%}"
            )

with tabs[1]:
    st.subheader("Customer book and human review queue")
    filter_left, filter_right = st.columns([1, 2])
    segment_options = sorted(scored["segment"].unique())
    with filter_left:
        selected_segments = st.multiselect("Segments", segment_options, default=segment_options)
    with filter_right:
        search = st.text_input("Find customer ID, city or channel", placeholder="C00042")
    view = scored[scored["segment"].isin(selected_segments)].copy()
    if search.strip():
        needle = search.strip().lower()
        mask = (
            view["customer_id"].str.lower().str.contains(needle, regex=False)
            | view["city"].str.lower().str.contains(needle, regex=False)
            | view["acquisition_channel"].str.lower().str.contains(needle, regex=False)
        )
        view = view[mask]
    display_columns = [
        "customer_id", "city", "segment", "recency_days", "orders_365d", "spend_365d",
        "churn_probability", "purchase_probability", "recommended_action",
    ]
    st.dataframe(
        view[display_columns].sort_values("priority_score", ascending=False),
        use_container_width=True,
        hide_index=True,
        column_config={
            "spend_365d": st.column_config.NumberColumn("Sales (365d)", format="₹%.0f"),
            "churn_probability": st.column_config.ProgressColumn("Churn probability", min_value=0, max_value=1, format="%.0%"),
            "purchase_probability": st.column_config.ProgressColumn("Repeat purchase", min_value=0, max_value=1, format="%.0%"),
        },
    )
    task_queue = view[view["recommended_action"] != "Monitor"].sort_values("priority_score", ascending=False)
    st.download_button(
        "Download human review queue (CSV)",
        data=task_queue[display_columns].to_csv(index=False).encode("utf-8"),
        file_name="crm_human_review_queue.csv",
        mime="text/csv",
    )

with tabs[2]:
    st.subheader("Evidence-grounded customer briefing")
    st.write("Ask a CRM question. The assistant combines customer features with retrieved playbook passages and returns source IDs for review.")
    customer_ids = sorted(scored["customer_id"].astype(str).tolist())
    chosen_id = st.selectbox("Customer", customer_ids, index=0)
    question = st.text_area(
        "Question",
        value="What should a human CRM owner review before choosing the next action for this customer?",
        height=88,
    )
    generation_col, provider_col = st.columns([1, 3])
    provider = setting("LLM_PROVIDER", "auto")
    provider_col.caption(
        f"Generation mode: {provider}. Set OPENAI_API_KEY or run a local OpenAI-compatible model for generated prose; "
        "otherwise the app returns a cited, score-aware fallback."
    )
    if generation_col.button("Retrieve evidence and draft briefing", type="primary", use_container_width=True):
        customer_row = scored.loc[scored["customer_id"].eq(chosen_id)].iloc[0].to_dict()
        try:
            with st.spinner("Retrieving, reranking and drafting…"):
                answer = generate_recommendation(question, customer_row, load_retriever())
            st.markdown(f"### {answer['summary']}")
            sources = answer.get("sources", [])
            retrieval_method = sources[0].get("retrieval_method", "none") if sources else "none"
            st.caption(f"LLM mode: {answer.get('llm_provider', 'unknown')} · Retrieval: {retrieval_method}")
            st.markdown("**Evidence**")
            for item in answer.get("evidence", []):
                refs = ", ".join(f"`{source_id}`" for source_id in item.get("citations", [])) or "No source citation"
                st.markdown(f"- {item.get('claim', '')}  \n  Sources: {refs}")
            st.markdown("**Recommended human review**")
            for action in answer.get("recommended_actions", []):
                st.markdown(f"- {action}")
            st.info(f"Uncertainty: {answer.get('uncertainty', 'Not specified')}")
            with st.expander("Inspect retrieved passages and ranking scores"):
                for source in answer.get("sources", []):
                    st.markdown(f"**{source['source_id']} · {source['title']}** · score {source['score']}")
                    st.write(source["text"])
            with st.expander("Inspect structured response"):
                st.code(json.dumps(answer, indent=2, ensure_ascii=False), language="json")
        except Exception as exc:
            st.error(f"The RAG assistant could not complete this request: {exc}")

with tabs[3]:
    st.subheader("RAG research lab")
    st.markdown("Compare lexical retrieval, dense semantic search, reciprocal-rank fusion and hybrid retrieval with a cross-encoder reranker.")
    st.markdown(
        "**Hypothesis:** BM25 should help exact CRM terms and IDs; dense retrieval should help paraphrases; "
        "RRF can combine their strengths; reranking may improve the final ordering at additional latency."
    )
    eval_queries_path = os.path.join(os.path.dirname(__file__), "research", "retrieval_eval.jsonl")
    eval_rows = [json.loads(line) for line in open(eval_queries_path, encoding="utf-8") if line.strip()]
    selected_eval = st.selectbox("Evaluation query", eval_rows, format_func=lambda item: f"{item['query_id']} · {item['query']}")
    mode = st.selectbox("Retrieval experiment", ["bm25", "dense", "hybrid", "hybrid_reranked"], index=3)
    if st.button("Run retrieval experiment"):
        with st.spinner("Searching local knowledge base…"):
            retrieved = load_retriever().retrieve(selected_eval["query"], top_k=5, mode=mode)
        relevant = set(selected_eval["relevant_sources"])
        ranked_ids = [item.source_id for item in retrieved]
        recall = len(set(ranked_ids) & relevant) / max(1, len(relevant))
        reciprocal_rank = next((1 / (index + 1) for index, value in enumerate(ranked_ids) if value in relevant), 0.0)
        c1, c2, c3 = st.columns(3)
        c1.metric("Recall@5", f"{recall:.2f}")
        c2.metric("MRR@5", f"{reciprocal_rank:.2f}")
        c3.metric("Relevant sources", f"{len(set(ranked_ids) & relevant)}/{len(relevant)}")
        st.markdown("**Ranked evidence**")
        for index, item in enumerate(retrieved, start=1):
            st.markdown(f"{index}. `{item.source_id}` · {item.title} · {item.retrieval_method} · {item.score:.4f}")
            st.caption(item.text[:500] + ("…" if len(item.text) > 500 else ""))
    st.code("python scripts/evaluate_retrieval.py", language="powershell")
    st.caption("The CLI writes per-query Recall@5, MRR@5 and NDCG@5 to research/retrieval_benchmark_results.csv.")

with tabs[4]:
    st.subheader("System architecture")
    st.code(
        "CRM tables (customer / transaction / activity / interaction)\n"
        "        ↓ grain checks + customer-level feature engineering\n"
        "K-means segments  +  churn / purchase propensity models\n"
        "        ↓\n"
        "Customer context + knowledge base\n"
        "        ↓ BM25 + dense embeddings → RRF → CrossEncoder reranker\n"
        "Retrieved evidence + scores → LLM answer with validated source IDs\n"
        "        ↓\n"
        "ASP.NET controllers / Streamlit CRM review queue → human review → CRM action",
        language="text",
    )
    st.subheader("Dataset quality and grain")
    st.dataframe(quality, use_container_width=True, hide_index=True)
    st.markdown("**Model limits**")
    st.markdown(
        "- Outcomes are generated from synthetic rules, so model metrics demonstrate a workflow rather than real business performance.\n"
        "- The demo uses a stratified random holdout; real temporal data should use a time-based validation split.\n"
        "- Clusters are descriptive. Review cluster profiles before using a business label.\n"
        "- The LLM can draft explanations; humans approve all customer contact and CRM tasks."
    )

