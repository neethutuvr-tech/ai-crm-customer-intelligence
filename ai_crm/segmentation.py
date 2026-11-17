from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

import numpy as np
import pandas as pd
from sklearn.cluster import KMeans
from sklearn.decomposition import PCA
from sklearn.impute import SimpleImputer
from sklearn.metrics import silhouette_score
from sklearn.preprocessing import StandardScaler


CLUSTER_FEATURES = [
    "recency_days",
    "orders_365d",
    "spend_365d",
    "avg_order_value",
    "sessions_30d",
    "cart_adds_30d",
    "campaign_click_rate",
    "support_cases_90d",
    "tenure_days",
]

LOG_FEATURES = {
    "recency_days",
    "orders_365d",
    "spend_365d",
    "avg_order_value",
    "sessions_30d",
    "cart_adds_30d",
    "support_cases_90d",
    "tenure_days",
}


def _segment_labels(profile: pd.DataFrame) -> dict[int, str]:
    spend_mid = float(profile["spend_365d"].median())
    spend_high = float(profile["spend_365d"].quantile(0.75))
    order_low = float(profile["orders_365d"].quantile(0.25))
    recency_high = max(60.0, float(profile["recency_days"].quantile(0.75)))
    labels: dict[int, str] = {}
    for cluster_id, row in profile.iterrows():
        if row["recency_days"] >= recency_high and row["spend_365d"] <= spend_mid:
            label = "At risk"
        elif row["spend_365d"] >= spend_high:
            label = "High value"
        elif row["orders_365d"] <= order_low:
            label = "Occasional"
        else:
            label = "Steady"
        labels[int(cluster_id)] = f"{label} · C{int(cluster_id)}"
    return labels


def analyze_customer_segments(records: list[dict[str, Any]], clusters: int = 4) -> dict[str, Any]:
    """Fit an auditable K-Means model to a single authorized tenant cohort."""
    if not 2 <= clusters <= 8:
        raise ValueError("clusters must be between 2 and 8")
    if len(records) < clusters + 1:
        raise ValueError(f"At least {clusters + 1} customers are required for {clusters} clusters")

    data = pd.DataFrame(records)
    if "customer_id" not in data or data["customer_id"].isna().any():
        raise ValueError("Every customer record must have a customer_id")
    if data["customer_id"].duplicated().any():
        raise ValueError("customer_id values must be unique")
    for feature in CLUSTER_FEATURES:
        if feature not in data:
            data[feature] = 0.0
        data[feature] = pd.to_numeric(data[feature], errors="coerce")
    data[CLUSTER_FEATURES] = data[CLUSTER_FEATURES].replace([np.inf, -np.inf], np.nan)
    if data[CLUSTER_FEATURES].isna().all(axis=None):
        raise ValueError("At least one numeric feature must be present")

    raw = data[CLUSTER_FEATURES].copy().fillna(0.0)
    transformed = raw.copy()
    for feature in LOG_FEATURES:
        transformed[feature] = np.log1p(transformed[feature].clip(lower=0))
    matrix = SimpleImputer(strategy="median").fit_transform(transformed)
    scaled = StandardScaler().fit_transform(matrix)
    if np.unique(scaled, axis=0).shape[0] < clusters:
        raise ValueError("The stored customer features do not contain enough distinct profiles for this cluster count")

    model = KMeans(n_clusters=clusters, random_state=42, n_init=20)
    cluster_ids = model.fit_predict(scaled)
    distinct_clusters = np.unique(cluster_ids)
    if len(distinct_clusters) < 2:
        raise ValueError("K-Means found fewer than two distinct customer groups")

    profile_frame = raw.assign(cluster_id=cluster_ids).groupby("cluster_id").mean(numeric_only=True)
    names = _segment_labels(profile_frame)
    profile_rows = []
    for cluster_id, row in profile_frame.iterrows():
        profile_rows.append(
            {
                "cluster_id": int(cluster_id),
                "segment": names[int(cluster_id)],
                "customer_count": int((cluster_ids == cluster_id).sum()),
                "mean_features": {key: float(value) for key, value in row.items()},
                "scaled_centroid": [float(value) for value in model.cluster_centers_[int(cluster_id)]],
            }
        )

    elbow = []
    for candidate_k in range(2, min(8, len(data) - 1) + 1):
        candidate = KMeans(n_clusters=candidate_k, random_state=42, n_init=10)
        candidate.fit(scaled)
        elbow.append({"clusters": candidate_k, "inertia": float(candidate.inertia_)})

    coordinates = PCA(n_components=2, random_state=42).fit_transform(scaled)
    silhouette = (
        float(
            silhouette_score(
                scaled,
                cluster_ids,
                sample_size=min(2_000, len(data)),
                random_state=42,
            )
        )
        if 1 < len(distinct_clusters) < len(data)
        else None
    )
    return {
        "method": "KMeans",
        "cluster_count": clusters,
        "customer_count": len(data),
        "evaluation": {
            "silhouette_score": silhouette,
            "inertia": float(model.inertia_),
            "elbow_analysis": elbow,
            "metric_scope": "descriptive fit on the submitted tenant cohort; not predictive model performance",
        },
        "clusters": profile_rows,
        "membership": [
            {
                "customer_id": str(customer_id),
                "cluster_id": int(cluster_id),
                "segment": names[int(cluster_id)],
            }
            for customer_id, cluster_id in zip(data["customer_id"], cluster_ids)
        ],
        "pca_projection": [
            {"customer_id": str(customer_id), "x": float(point[0]), "y": float(point[1]), "cluster_id": int(cluster_id)}
            for customer_id, point, cluster_id in zip(data["customer_id"], coordinates, cluster_ids)
        ],
        "features": CLUSTER_FEATURES,
        "transform": "log1p for nonnegative count/value features, then StandardScaler; campaign_click_rate remains on [0,1]",
        "model_version": "tenant-kmeans-v1",
        "random_state": 42,
        "trained_at_utc": datetime.now(timezone.utc).isoformat(),
        "segment_names_are_heuristics": True,
    }
