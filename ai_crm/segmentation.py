from __future__ import annotations

from datetime import datetime, timezone
from time import perf_counter
from typing import Any

import numpy as np
import pandas as pd
import sklearn
from sklearn.cluster import AgglomerativeClustering, DBSCAN, KMeans
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
    distinct_profile_count = int(np.unique(scaled, axis=0).shape[0])
    if distinct_profile_count < clusters:
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
    for candidate_k in range(2, min(8, len(data) - 1, distinct_profile_count) + 1):
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


def analyze_segmentation_research(
    records: list[dict[str, Any]],
    clusters: int = 4,
    dbscan_eps: float = 0.9,
    dbscan_min_samples: int = 5,
) -> dict[str, Any]:
    """Compare three descriptive clustering methods on one authorized cohort."""
    if not 2 <= clusters <= 8:
        raise ValueError("clusters must be between 2 and 8")
    if not np.isfinite(dbscan_eps) or not 0.1 <= dbscan_eps <= 5.0:
        raise ValueError("dbscan_eps must be between 0.1 and 5.0")
    if not 2 <= dbscan_min_samples <= 50:
        raise ValueError("dbscan_min_samples must be between 2 and 50")
    if len(records) > 5_000:
        raise ValueError("The clustering benchmark accepts at most 5,000 customers because hierarchical clustering is quadratic")
    if len(records) < max(clusters + 1, dbscan_min_samples):
        raise ValueError(
            "The cohort must contain at least clusters + 1 customers and at least dbscan_min_samples customers"
        )

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
    distinct_profile_count = int(np.unique(scaled, axis=0).shape[0])
    if distinct_profile_count < clusters:
        raise ValueError("The stored customer features do not contain enough distinct profiles for this cluster count")
    if distinct_profile_count < 2:
        raise ValueError("The stored customer features do not contain at least two distinct profiles")

    pca_coordinates = PCA(n_components=2, random_state=42).fit_transform(scaled)
    configurations = [
        (
            "KMeans",
            KMeans(n_clusters=clusters, random_state=42, n_init=20),
            {"n_clusters": clusters, "random_state": 42, "n_init": 20},
        ),
        (
            "AgglomerativeClustering",
            AgglomerativeClustering(n_clusters=clusters, linkage="ward"),
            {"n_clusters": clusters, "linkage": "ward"},
        ),
        (
            "DBSCAN",
            DBSCAN(eps=dbscan_eps, min_samples=dbscan_min_samples),
            {"eps": dbscan_eps, "min_samples": dbscan_min_samples},
        ),
    ]

    algorithms: list[dict[str, Any]] = []
    for method, model, parameters in configurations:
        started = perf_counter()
        labels = model.fit_predict(scaled)
        fit_latency_ms = (perf_counter() - started) * 1000
        cluster_ids = sorted(int(value) for value in np.unique(labels) if value >= 0)
        non_noise_mask = labels >= 0
        non_noise_labels = labels[non_noise_mask]
        silhouette = None
        if 1 < len(np.unique(non_noise_labels)) < len(non_noise_labels):
            try:
                silhouette = float(
                    silhouette_score(
                        scaled[non_noise_mask],
                        non_noise_labels,
                        sample_size=min(2_000, int(non_noise_mask.sum())),
                        random_state=42,
                    )
                )
            except ValueError:
                silhouette = None

        profile_frame = raw.assign(cluster_id=labels).loc[lambda frame: frame["cluster_id"] >= 0]
        profile_frame = profile_frame.groupby("cluster_id").mean(numeric_only=True)
        names = _segment_labels(profile_frame) if not profile_frame.empty else {}
        profiles = [
            {
                "cluster_id": cluster_id,
                "segment": names[cluster_id],
                "customer_count": int(np.count_nonzero(labels == cluster_id)),
                "mean_features": {
                    key: float(value) for key, value in profile_frame.loc[cluster_id].items()
                },
            }
            for cluster_id in cluster_ids
        ]
        membership = [
            {
                "customer_id": str(customer_id),
                "cluster_id": int(cluster_id),
                "segment": "Noise" if cluster_id < 0 else names[int(cluster_id)],
            }
            for customer_id, cluster_id in zip(data["customer_id"], labels)
        ]
        evaluation: dict[str, Any] = {
            "silhouette_score": silhouette,
            "silhouette_scope": "non-noise customers only; null when fewer than two evaluable clusters exist",
            "cluster_count": len(cluster_ids),
            "noise_count": int(np.count_nonzero(labels < 0)),
            "fit_latency_ms": round(fit_latency_ms, 3),
            "metric_scope": "descriptive fit on this tenant cohort; not predictive model performance",
        }
        if method == "KMeans":
            evaluation["inertia"] = float(model.inertia_)
            profiles = [
                {
                    **profile,
                    "scaled_centroid": [float(value) for value in model.cluster_centers_[profile["cluster_id"]]],
                }
                for profile in profiles
            ]
        algorithms.append(
            {
                "method": method,
                "parameters": parameters,
                "evaluation": evaluation,
                "clusters": profiles,
                "membership": membership,
            }
        )

    elbow_analysis = []
    for candidate_k in range(2, min(8, len(data) - 1, distinct_profile_count) + 1):
        candidate = KMeans(n_clusters=candidate_k, random_state=42, n_init=10)
        candidate.fit(scaled)
        elbow_analysis.append({"clusters": candidate_k, "inertia": float(candidate.inertia_)})

    return {
        "customer_count": len(data),
        "distinct_profile_count": distinct_profile_count,
        "features": CLUSTER_FEATURES,
        "transform": "log1p for nonnegative count/value features, then StandardScaler; campaign_click_rate remains on [0,1]",
        "pca_projection": [
            {
                "customer_id": str(customer_id),
                "x": float(point[0]),
                "y": float(point[1]),
            }
            for customer_id, point in zip(data["customer_id"], pca_coordinates)
        ],
        "kmeans_elbow_analysis": elbow_analysis,
        "algorithms": algorithms,
        "model_version": "tenant-clustering-benchmark-v1",
        "scikit_learn_version": sklearn.__version__,
        "random_state": 42,
        "trained_at_utc": datetime.now(timezone.utc).isoformat(),
        "segment_names_are_heuristics": True,
    }
