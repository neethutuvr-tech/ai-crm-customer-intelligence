from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

import numpy as np
import pandas as pd
import sklearn
from sklearn.calibration import CalibratedClassifierCV, calibration_curve
from sklearn.ensemble import RandomForestClassifier
from sklearn.cluster import KMeans
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import average_precision_score, brier_score_loss, confusion_matrix
from sklearn.metrics import (
    f1_score,
    precision_score,
    recall_score,
    roc_auc_score,
)
from sklearn.model_selection import StratifiedKFold, train_test_split
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler

from .config import setting
from .features import CLUSTER_FEATURES, NUMERIC_FEATURES


TARGET_COLUMNS = {
    "churn_probability": "churn_next_30d",
    "purchase_probability": "purchase_next_30d",
}
MODEL_NAMES = ("logistic_regression", "random_forest")
RANDOM_STATE = 42
EVALUATION_THRESHOLD = 0.5


def _baseline_pipeline(model_name: str) -> Pipeline:
    if model_name == "logistic_regression":
        return Pipeline(
            [
                ("imputer", SimpleImputer(strategy="median")),
                ("scaler", StandardScaler()),
                (
                    "classifier",
                    LogisticRegression(
                        max_iter=1500,
                        class_weight="balanced",
                        random_state=RANDOM_STATE,
                    ),
                ),
            ]
        )
    if model_name == "random_forest":
        return Pipeline(
            [
                ("imputer", SimpleImputer(strategy="median")),
                (
                    "classifier",
                    RandomForestClassifier(
                        n_estimators=120,
                        min_samples_leaf=3,
                        class_weight="balanced_subsample",
                        random_state=RANDOM_STATE,
                        n_jobs=1,
                    ),
                ),
            ]
        )
    raise ValueError(f"Unsupported propensity model: {model_name}")


def _calibrated_model(model_name: str) -> CalibratedClassifierCV:
    return CalibratedClassifierCV(
        estimator=_baseline_pipeline(model_name),
        method="sigmoid",
        cv=StratifiedKFold(n_splits=3, shuffle=True, random_state=RANDOM_STATE),
        ensemble=True,
        n_jobs=1,
    )


def _feature_importance(model_name: str, X: pd.DataFrame, y: pd.Series) -> list[dict[str, float | str]]:
    estimator = _baseline_pipeline(model_name)
    estimator.fit(X, y)
    classifier = estimator.named_steps["classifier"]
    if model_name == "logistic_regression":
        importance = np.abs(classifier.coef_[0])
    else:
        importance = classifier.feature_importances_
    order = np.argsort(importance)[::-1][:10]
    return [
        {"feature": str(X.columns[index]), "importance": float(importance[index])}
        for index in order
    ]


def _evaluate_probabilities(y_true: pd.Series, probability: np.ndarray, positive_rate: float) -> dict[str, Any]:
    predicted = (probability >= EVALUATION_THRESHOLD).astype(int)
    observed_fraction, mean_probability = calibration_curve(
        y_true,
        probability,
        n_bins=min(5, max(2, len(y_true) // 10)),
        strategy="quantile",
    )
    return {
        "roc_auc": float(roc_auc_score(y_true, probability)),
        "pr_auc": float(average_precision_score(y_true, probability)),
        "precision_at_0_5": float(precision_score(y_true, predicted, zero_division=0)),
        "recall_at_0_5": float(recall_score(y_true, predicted, zero_division=0)),
        "f1_at_0_5": float(f1_score(y_true, predicted, zero_division=0)),
        "brier_score": float(brier_score_loss(y_true, probability)),
        "confusion_matrix_at_0_5": confusion_matrix(y_true, predicted, labels=[0, 1]).tolist(),
        "calibration_curve": [
            {"mean_predicted_probability": float(prediction), "observed_positive_rate": float(observed)}
            for prediction, observed in zip(mean_probability, observed_fraction)
        ],
        "positive_rate": positive_rate,
        "holdout_positive_rate": float(y_true.mean()),
        "threshold": EVALUATION_THRESHOLD,
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
) -> tuple[pd.DataFrame, dict[str, dict[str, Any]]]:
    """Fit descriptive segments and compare calibrated synthetic outcome baselines."""
    if not 2 <= clusters <= min(8, len(features) - 1):
        raise ValueError("clusters must be between 2 and 8 and below the customer count")

    matrix = _cluster_matrix(features)
    labels = KMeans(n_clusters=clusters, random_state=42, n_init=20).fit_predict(matrix)
    name_map = _segment_names(features, pd.Series(labels, index=features.index))

    outcome_data = outcomes.set_index("customer_id")
    aligned = features.set_index("customer_id").join(outcome_data, how="inner", validate="one_to_one")
    if len(aligned) != len(features):
        raise ValueError("Each customer must have one future outcome row")
    aligned = aligned.loc[features["customer_id"].to_numpy()]

    X = aligned[NUMERIC_FEATURES].apply(pd.to_numeric, errors="coerce").astype(float)
    result = features.copy()
    result["cluster_id"] = labels
    result["segment"] = [name_map[int(value)] for value in labels]
    selected_model_name = setting("PROPENSITY_MODEL", "logistic_regression").lower()
    if selected_model_name not in MODEL_NAMES:
        raise ValueError(f"PROPENSITY_MODEL must be one of: {', '.join(MODEL_NAMES)}")
    metrics: dict[str, dict[str, Any]] = {}

    for score_column, target_column in TARGET_COLUMNS.items():
        y = aligned[target_column].astype(int)
        if y.nunique() != 2 or y.value_counts().min() < 6:
            raise ValueError(f"{target_column} needs at least 6 examples of each class for calibrated holdout evaluation")
        X_train, X_test, y_train, y_test = train_test_split(
            X, y, test_size=0.25, random_state=RANDOM_STATE, stratify=y
        )
        model_comparisons: dict[str, dict[str, Any]] = {}
        feature_importances: dict[str, list[dict[str, float | str]]] = {}
        final_probabilities: dict[str, np.ndarray] = {}
        for model_name in MODEL_NAMES:
            validation_model = _calibrated_model(model_name)
            validation_model.fit(X_train, y_train)
            holdout_probability = validation_model.predict_proba(X_test)[:, 1]
            model_comparisons[model_name] = {
                **_evaluate_probabilities(y_test, holdout_probability, float(y.mean())),
                "train_count": int(len(X_train)),
                "test_count": int(len(X_test)),
                "calibration_method": "sigmoid calibration with three-fold cross-validation on training rows",
                "class_imbalance_strategy": (
                    "balanced class weights" if model_name == "logistic_regression"
                    else "balanced_subsample class weights"
                ),
            }
            feature_importances[model_name] = _feature_importance(model_name, X_train, y_train)

            final_model = _calibrated_model(model_name)
            final_model.fit(X, y)
            final_probabilities[model_name] = final_model.predict_proba(X)[:, 1]

        selected_metrics = model_comparisons[selected_model_name]
        metrics[score_column] = {
            **selected_metrics,
            "models": model_comparisons,
            "feature_importance": feature_importances,
            "selected_model": selected_model_name,
            "model_version": "synthetic-outcome-baselines-v2",
            "trained_at_utc": datetime.now(timezone.utc).isoformat(),
            "scikit_learn_version": sklearn.__version__,
            "random_state": RANDOM_STATE,
            "split_strategy": "stratified random 75/25 holdout; use time-based evaluation for real temporal data",
            "evaluation_scope": "synthetic demonstration; not real business performance",
        }
        result[score_column] = final_probabilities[selected_model_name]

    result["priority_score"] = result[["churn_probability", "purchase_probability"]].max(axis=1)
    return result, metrics


def recommended_action(churn_probability: float, purchase_probability: float, threshold: float) -> str:
    if churn_probability >= threshold:
        return "Retention review"
    if purchase_probability >= threshold:
        return "Personalized offer review"
    return "Monitor"

