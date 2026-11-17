# Architecture walkthrough

This walkthrough follows one customer question through the project. Use it alongside the Visual Studio solution tree and the `Architecture` tab in the Streamlit app.

## Layer 1: relational CRM data

Open `data/demo/`. Each table has a different row grain:

- `customers.csv`: one row per customer.
- `transactions.csv`: one row per order.
- `activity.csv`: one row per customer snapshot.
- `interactions.csv`: one row per support, enquiry or campaign event.
- `outcomes.csv`: one row per customer with future 30-day labels for offline learning.

The labels are kept separate from the features to make the prediction time boundary visible.

## Layer 2: feature engineering and predictive ML

`ai_crm/features.py` aggregates transactions and interactions first, then joins those summaries to customer snapshots. It creates RFM and activity features.

`ai_crm/models.py` uses scaled/log-transformed numeric features for K-means. It trains separate logistic regression models for future churn and repeat purchase. A held-out split provides ROC-AUC, precision, recall and F1; the final model is refit on all synthetic rows to create dashboard scores.

The customer record and predictions stay structured. Customer transactions are not embedded as text in the RAG index.

## Layer 3: retrieval-augmented generation

`knowledge_base/` contains business playbooks rather than customer data. `ai_crm/retrieval.py` chunks those documents and creates local dense embeddings. At question time it:

1. Runs BM25 keyword retrieval and dense semantic retrieval.
2. Fuses their ranked lists with reciprocal-rank fusion.
3. Optionally reranks the candidate shortlist with a local CrossEncoder.
4. Returns a small set of text passages with `source_id` values.

`ai_crm/llm.py` combines the selected customer's structured features, model scores, question and retrieved passages. The LLM is told that passages are untrusted data, must return structured evidence, and may cite only supplied source IDs. Invalid citation IDs are removed and counted. The project does not automatically send a campaign or create an irreversible CRM action.

## Layer 4: application and CRM integration

- `ml_api.py` serves model scores, evaluation metrics and RAG recommendations.
- `src/Crm.Api/Controllers/CustomersController.cs` exposes customer search and Customer 360 records.
- `src/Crm.Api/Controllers/InsightsController.cs` exposes model metrics and a human review queue.
- `src/Crm.Api/Controllers/AssistantController.cs` forwards a CRM question to the Python RAG service.
- `app.py` is the interactive dashboard for exploring segments, scores, retrieved evidence and research results.

## Why two backends?

The .NET API matches a typical CRM integration surface and makes controllers/services/repositories visible in Visual Studio. The AI research layer stays in Python, where scikit-learn and the embedding/reranking libraries are used directly. The Python API is a well-defined boundary between CRM features and AI inference.

## Research loop

Change one retrieval component at a time, run `python scripts/evaluate_retrieval.py`, and inspect both macro averages and individual queries. A gain in Recall@5 can improve the evidence available to the LLM, but it does not prove that generated text is correct. Run `python scripts/evaluate_rag.py` and use the human rubric in `methodology.md` to review support and usefulness.

