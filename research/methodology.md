# RAG and CRM-model research methodology

## Research question

For CRM analyst questions over a small policy/playbook corpus, does combining dense semantic retrieval and BM25 improve relevant evidence retrieval over either method alone? Does reranking improve the order enough to justify its added latency?

## Retrieval experiment

1. Use the fixed query/source set in `retrieval_eval.jsonl`.
2. Compare `bm25`, `dense`, `hybrid` (RRF) and `hybrid_reranked`.
3. Report per-query and macro-average Recall@5, MRR@5 and NDCG@5.
4. Record corpus version, embedding model, reranker model, chunking configuration and environment with every result.
5. If tuning fusion weights, use a separate development set. Keep the final evaluation queries untouched.

## Answer review

Use a blinded human review for a small answer sample. Score each answer from 1 (poor) to 5 (strong):

| Dimension | Review question |
|---|---|
| Groundedness | Are factual claims supported by the retrieved passages or supplied customer features? |
| Citation correctness | Does each cited source actually support the associated claim? |
| Relevance | Does the response address the CRM question? |
| Uncertainty | Does it distinguish a model score from a certain outcome? |
| Safe action language | Does it leave contact and campaign choices for a human reviewer? |

Report citation validity separately from citation usefulness. A citation ID can be syntactically valid and still fail to support the claim. Avoid relying on a single LLM judge as the only quality measure.

## Predictive modeling

The demo predicts `churn_next_30d` and `purchase_next_30d` from features available at one fixed synthetic snapshot. Compare class-weighted logistic regression with a class-weighted random forest. Split 75/25 by customer using stratification; tune neither model on the holdout. Fit missing-value handling, scaling and three-fold sigmoid calibration inside the training split. Report ROC-AUC, average precision (PR-AUC), precision, recall and F1 at 0.5, confusion matrix, Brier score and calibration bins. Feature importance is descriptive and is not a causal explanation. XGBoost is not included in the dependency-light baseline.

This random split demonstrates the pipeline only. The generator has one snapshot per customer and draws labels from synthetic behavior rules, so it cannot support temporal validation or real business-performance claims. Production research needs multiple historical snapshots, future outcome windows, a time-based train/validation/test split, drift checks, threshold trade-offs under campaign costs and calibration against observed outcomes.

The data generator creates labels from synthetic behavioral patterns. Treat the resulting ROC-AUC, precision, recall and F1 values as pipeline demonstrations only.

## Responsible use

Use minimum-necessary data, exclude contact and payment identifiers from embeddings/prompts, monitor drift, preserve source citations, and require human review before CRM contact or offer actions.

