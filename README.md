# AI Customer Intelligence Lab

An AI R&D prototype for turning relational CRM data into customer segments, predictive scores and source-grounded next-action briefings. The main research feature is a hybrid RAG pipeline: BM25 keyword search + dense embeddings → reciprocal-rank fusion → cross-encoder reranking → LLM response with source IDs.

The ASP.NET Core API is the CRM-facing adapter. Python owns the machine-learning and RAG workflows. A Streamlit app makes the research visible and lets a CRM owner inspect evidence before acting.

> The included customer data and future labels are generated for demonstration. They contain no names, emails, phone numbers or payment details. Do not treat sample model metrics as business results.

## Architecture

```mermaid
flowchart LR
    A[Customers / transactions / activity / interactions] --> B[Grain checks + customer-level feature engineering]
    B --> C[RFM + behavioral features]
    C --> D[K-means segments]
    C --> E[Churn + repeat-purchase models]
    D --> F[Customer context]
    E --> F
    G[CRM playbooks and policy docs] --> H[BM25 + dense retrieval]
    H --> I[RRF fusion]
    I --> J[Cross-encoder reranker]
    F --> K[LLM decision-support prompt]
    J --> K
    K --> L[Cited recommendation + uncertainty]
    L --> M[Streamlit review UI / ASP.NET controllers]
    M --> N[Human review before a CRM action]
```

### Why the project is structured this way

1. **Feature layer:** transactions and interactions are aggregated to one row per customer before joining. That avoids duplicated customer rows and makes the time window explicit.
2. **Predictive layer:** K-means discovers groups; separate classifiers estimate the next-30-day churn and purchase outcomes. Holdout metrics are reported separately from scores.
3. **Retrieval layer:** BM25 catches exact terminology, dense embeddings catch paraphrases, reciprocal-rank fusion combines candidate lists, and a CrossEncoder reorders a smaller shortlist.
4. **Generation layer:** the LLM receives only the selected customer features and retrieved excerpts. It must return source IDs; the service drops citations that were not retrieved and exposes a citation audit.
5. **Decision layer:** the API and dashboard surface a review queue. Sending an offer or contacting a customer stays with a human.

## Project map

| Path | Purpose |
|---|---|
| `AI_Customer_Intelligence.slnx` | Open this solution in Visual Studio 2026 |
| `src/Crm.Api/Controllers/` | CRM endpoints for customers, insights and the assistant |
| `src/Crm.Api/Services/` | CSV profile repository and Python AI-service client |
| `ai_crm/` | Feature engineering, models, retrieval and LLM orchestration |
| `knowledge_base/` | Small CRM playbooks indexed by the RAG pipeline |
| `research/` | Retrieval and answer evaluation sets and methodology |
| `research/architecture_walkthrough.md` | Layer-by-layer guide to trace one CRM question |
| `scripts/` | Synthetic-data generation and evaluation commands |
| `app.py` | Streamlit customer intelligence and RAG research UI |
| `ml_api.py` | FastAPI scoring and RAG service |
| `sql/schema.sql` | SQL Server schema reference for the relational grain |

## Run locally on Windows

### 1. Generate the sample CRM

From the project root:

```powershell
python scripts/generate_demo_data.py
```

This creates 1,500 synthetic customers plus separate transaction, interaction, activity and future-outcome tables in `data/demo/`.

### 2. Create the Python environment

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
Copy-Item .env.example .env
```

The first RAG request downloads the local embedding model. If enabled, it also downloads the local CrossEncoder reranker. The model weights stay in the machine's Hugging Face cache, outside this repository.

### 3. Choose LLM generation

The sample app works without an API key: `LLM_PROVIDER=auto` uses an evidence-and-score fallback when no model provider is configured. For generated prose, configure one of the following in `.env`:

- **OpenAI Responses API:** set `LLM_PROVIDER=openai`, `OPENAI_API_KEY`, and optionally `OPENAI_MODEL`. The starter model is `gpt-6.1-terra`; change it to a model enabled for your account.
- **Local OpenAI-compatible server:** set `LLM_PROVIDER=ollama`, `OLLAMA_BASE_URL`, and `OLLAMA_MODEL` to the model tag you have installed locally.
- **No LLM call:** set `LLM_PROVIDER=extractive`.

Never commit `.env`. Only synthetic data is supplied. Before sending real CRM fields to a hosted model, confirm the data-processing rules and minimize prompt fields.

### 4. Start the Python AI service

In terminal 1:

```powershell
uvicorn ml_api:app --reload --port 8000
```

FastAPI docs are available at `http://127.0.0.1:8000/docs` while the service is running.

### 5. Start the dashboard

In terminal 2:

```powershell
streamlit run app.py
```

The dashboard has an overview, a scored customer book with a CSV review queue, a cited RAG assistant, a retrieval research lab and an architecture/data-quality view.

### 6. Open the CRM API in Visual Studio

Open `AI_Customer_Intelligence.slnx` from this folder in Visual Studio. The CRM-facing ASP.NET Core adapter targets .NET 10 LTS. Then run `Crm.Api` with terminal 3:

```powershell
dotnet run --project src/Crm.Api
```

The ASP.NET API expects the Python service at `http://127.0.0.1:8000/`. Change `PythonService:BaseUrl` in `src/Crm.Api/appsettings.json` if the service runs elsewhere.

## API routes

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/health` | ASP.NET API health |
| `GET` | `/api/customers?clusters=4` | Customer profiles with segment and scores |
| `GET` | `/api/customers/{customerId}?clusters=4` | Customer 360 row |
| `GET` | `/api/insights?clusters=4` | Segment sizes and held-out model metrics |
| `GET` | `/api/insights/follow-ups?threshold=0.65` | Candidate human review queue |
| `POST` | `/api/assistant/recommendations` | RAG response for a customer and question |

Example assistant request:

```json
{
  "customerId": "C00042",
  "question": "What should the CRM owner review before choosing a next action?",
  "clusters": 4
}
```

Python service routes are `GET /health`, `GET /customers`, `GET /metrics` and `POST /assistant/recommendations`.

## AI research plan

**Research question:** Does hybrid retrieval plus a second-stage reranker retrieve more relevant CRM playbook evidence than BM25 or dense search alone?

**Baselines:** BM25, dense semantic retrieval, BM25+dense with RRF, and RRF followed by a CrossEncoder.

**Metrics:** Recall@5 (whether useful evidence reaches the model), MRR@5 (rank of the first relevant source), and NDCG@5 (ranking quality across relevant sources). The curated query set is in `research/retrieval_eval.jsonl`.

Run the retrieval benchmark:

```powershell
python scripts/evaluate_retrieval.py
```

It writes per-query results to `research/retrieval_benchmark_results.csv`. Compare the methods on the same query set; do not tune a fusion weight on the final evaluation queries.

For answer quality, `research/methodology.md` includes a human review rubric for factual support, citation correctness, usefulness, uncertainty and safe action language. Retrieval metrics alone do not establish answer quality.

Run the small RAG answer study with the configured provider:

```powershell
python scripts/evaluate_rag.py
```

This records response latency, retrieved-source recall and citation-ID validity. It does not automatically claim that a valid citation supports a claim; use the human rubric for that review.

## Current technical choices

- New OpenAI integrations use the Responses API; the model name is configurable because availability depends on account access. See the [OpenAI API overview](https://platform.openai.com/overview).
- The OpenAI path requests schema-constrained JSON using [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs).
- Hybrid retrieval and rank fusion follow the retrieval pattern described in the [Qdrant hybrid search documentation](https://qdrant.tech/documentation/search/hybrid-queries/).
- The second-stage reranker uses the `CrossEncoder` interface documented by [Sentence Transformers](https://www.sbert.net/docs/package_reference/cross_encoder/model.html).

## Limits and next research steps

- Synthetic labels are generated from observable feature patterns. This can make the learning problem easier than real churn and conversion work.
- The classifiers use a stratified random holdout to keep this demo compact. Replace it with time-based validation on historical snapshots before drawing conclusions.
- Logistic regression scores are ranking aids; calibrate probabilities and select operating thresholds against real campaign costs.
- Segment labels are derived from cluster profiles for readability. Review each cluster's feature distributions before using the label.
- Add production connectors only after a data contract, consent model, access control and retention policy are defined.
- Next experiments: temporal validation, calibration curves, drift monitoring, prompt/version ablations, retrieval-latency/cost measurements and a blinded human review of generated recommendations.

