from __future__ import annotations

import numpy as np
import pandas as pd
from sklearn.cluster import KMeans
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import (
    f1_score,
    precision_score,
    recall_score,
    roc_auc_score,
)
from sklearn.model_selection import train_test_split
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

from .features import CLUSTER_FEATURES, NUMERIC_FEATURES


TARGET_COLUMNS = {
    "churn_probability": "churn_next_30d",
    "purchase_probability": "purchase_next_30d",
}


def _cluster_matrix(features: pd.DataFrame) -> np.ndarray:
    matrix = features[CLUSTER_FEATURES].astype(float).copy()
    for column in [
        "recency_days",
        "orders_365d",
        "spend_365d",
        "avg_order_value",
        "sessions_30d",
        "cart_adds_30d",
        "support_cases_90d",
        "tenure_days",
    ]:
        matrix[column] = np.log1p(matrix[column].clip(lower=0))
    matrix = SimpleImputer(strategy="median").fit_transform(matrix)
    return StandardScaler().fit_transform(matrix)


def _segment_names(features: pd.DataFrame, cluster_ids: pd.Series) -> dict[int, str]:
    profile = features.assign(cluster=cluster_ids).groupby("cluster").agg(
        spend=("spend_365d", "mean"),
        orders=("orders_365d", "mean"),
        recency=("recency_days", "mean"),
    )
    spend_mid = float(profile["spend"].median())
    spend_high = float(profile["spend"].quantile(0.75))
    order_low = float(profile["orders"].quantile(0.25))
    recency_high = max(60.0, float(profile["recency"].quantile(0.75)))
    names: dict[int, str] = {}
    for cluster_id, row in profile.iterrows():
        if row["recency"] >= recency_high and row["spend"] <= spend_mid:
            label = "At risk"
        elif row["spend"] >= spend_high:
            label = "High value"
        elif row["orders"] <= order_low:
            label = "Occasional"
        else:
            label = "Steady"
        names[int(cluster_id)] = f"{label} · C{int(cluster_id)}"
    return names


def score_customers(
    features: pd.DataFrame,
    outcomes: pd.DataFrame,
    clusters: int = 4,
) -> tuple[pd.DataFrame, dict[str, dict[str, float]]]:
    """Fit unsupervised segments and two supervised 30-day outcome scores."""
    if not 2 <= clusters <= min(8, len(features) - 1):
        raise ValueError("clusters must be between 2 and 8 and below the customer count")

    matrix = _cluster_matrix(features)
    labels = KMeans(n_clusters=clusters, random_state=42, n_init=20).fit_predict(matrix)
    name_map = _segment_names(features, pd.Series(labels, index=features.index))

    outcome_data = outcomes.set_index("customer_id")
    aligned = features.set_index("customer_id").join(outcome_data, how="inner", validate="one_to_one")
    if len(aligned) != len(features):
        raise ValueError("Each customer must have one future outcome row")

    X = aligned[NUMERIC_FEATURES].astype(float)
    result = features.copy()
    result["cluster_id"] = labels
    result["segment"] = [name_map[int(value)] for value in labels]
    metrics: dict[str, dict[str, float]] = {}

    for score_column, target_column in TARGET_COLUMNS.items():
        y = aligned[target_column].astype(int)
        if y.nunique() != 2 or y.value_counts().min() < 2:
            raise ValueError(f"{target_column} needs examples of both classes")
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=0.25, random_state=42, stratify=y
        )
        model = make_pipeline(
            SimpleImputer(strategy="median"),
            StandardScaler(),
            LogisticRegression(max_iter=1500, class_weight="balanced", random_state=42),
        )
        model.fit(X_train, y_train)
        holdout_probability = model.predict_proba(X_test)[:, 1]
        holdout_label = (holdout_probability >= 0.5).astype(int)
        metrics[score_column] = {
            "roc_auc": float(roc_auc_score(y_test, holdout_probability)),
            "precision_at_0_5": float(precision_score(y_test, holdout_label, zero_division=0)),
            "recall_at_0_5": float(recall_score(y_test, holdout_label, zero_division=0)),
            "f1_at_0_5": float(f1_score(y_test, holdout_label, zero_division=0)),
            "positive_rate": float(y.mean()),
        }
        final_model = make_pipeline(
            SimpleImputer(strategy="median"),
            StandardScaler(),
            LogisticRegression(max_iter=1500, class_weight="balanced", random_state=42),
        )
        final_model.fit(X, y)
        result[score_column] = final_model.predict_proba(result[NUMERIC_FEATURES])[:, 1]

    result["priority_score"] = result[["churn_probability", "purchase_probability"]].max(axis=1)
    return result, metrics


def recommended_action(churn_probability: float, purchase_probability: float, threshold: float) -> str:
    if churn_probability >= threshold:
        return "Retention review"
    if purchase_probability >= threshold:
        return "Personalized offer review"
    return "Monitor"

