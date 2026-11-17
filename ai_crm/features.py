from __future__ import annotations

import numpy as np
import pandas as pd


NUMERIC_FEATURES = [
    "recency_days",
    "orders_365d",
    "spend_365d",
    "avg_order_value",
    "sessions_30d",
    "product_views_30d",
    "cart_adds_30d",
    "campaign_click_rate",
    "support_cases_90d",
    "enquiries_90d",
    "tenure_days",
]

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


def build_customer_features(tables: dict[str, pd.DataFrame]) -> pd.DataFrame:
    """Create one customer row after aggregating every many-side table first."""
    customers = tables["customers"].copy()
    activity = tables["activity"].copy()
    transactions = tables["transactions"].copy()
    interactions = tables["interactions"].copy()

    if customers["customer_id"].duplicated().any():
        raise ValueError("customers.csv must have one row per customer_id")
    if activity["customer_id"].duplicated().any():
        raise ValueError("activity.csv must have one row per customer_id snapshot")

    customers["signup_date"] = pd.to_datetime(customers["signup_date"], errors="coerce")
    activity["snapshot_date"] = pd.to_datetime(activity["snapshot_date"], errors="coerce")
    transactions["order_date"] = pd.to_datetime(transactions["order_date"], errors="coerce")
    interactions["interaction_date"] = pd.to_datetime(
        interactions["interaction_date"], errors="coerce"
    )

    # Aggregate transaction grain to customer grain before joining. A direct join
    # to customer records would multiply rows for customers with several orders.
    snapshot = activity[["customer_id", "snapshot_date"]]
    transactions = transactions.merge(snapshot, on="customer_id", how="inner", validate="many_to_one")
    transactions = transactions[
        (transactions["order_date"] <= transactions["snapshot_date"])
        & ((transactions["snapshot_date"] - transactions["order_date"]).dt.days <= 365)
    ]
    order_summary = (
        transactions.groupby("customer_id", as_index=False)
        .agg(
            orders_365d=("transaction_id", "nunique"),
            spend_365d=("amount", "sum"),
            last_purchase_date=("order_date", "max"),
        )
    )

    # Interactions are also many-side records, so reduce them before the merge.
    interactions = interactions.merge(snapshot, on="customer_id", how="inner", validate="many_to_one")
    interactions = interactions[
        (interactions["interaction_date"] <= interactions["snapshot_date"])
        & ((interactions["snapshot_date"] - interactions["interaction_date"]).dt.days <= 90)
    ]
    interactions["is_support"] = interactions["interaction_type"].eq("support_case").astype(int)
    interactions["is_enquiry"] = interactions["interaction_type"].eq("enquiry").astype(int)
    interaction_summary = (
        interactions.groupby("customer_id", as_index=False)
        .agg(support_cases_90d=("is_support", "sum"), enquiries_90d=("is_enquiry", "sum"))
    )

    result = customers.merge(activity, on="customer_id", how="left", validate="one_to_one")
    result = result.merge(order_summary, on="customer_id", how="left", validate="one_to_one")
    result = result.merge(interaction_summary, on="customer_id", how="left", validate="one_to_one")
    result["orders_365d"] = result["orders_365d"].fillna(0).astype(int)
    result["spend_365d"] = result["spend_365d"].fillna(0.0)
    result["support_cases_90d"] = result["support_cases_90d"].fillna(0).astype(int)
    result["enquiries_90d"] = result["enquiries_90d"].fillna(0).astype(int)
    result["recency_days"] = (
        result["snapshot_date"] - result["last_purchase_date"]
    ).dt.days.fillna(365).clip(lower=0, upper=365)
    result["avg_order_value"] = np.where(
        result["orders_365d"] > 0,
        result["spend_365d"] / result["orders_365d"].clip(lower=1),
        0.0,
    )
    result["tenure_days"] = (result["snapshot_date"] - result["signup_date"]).dt.days

    for column in [
        "sessions_30d",
        "product_views_30d",
        "cart_adds_30d",
        "campaign_emails_30d",
        "campaign_clicks_30d",
    ]:
        result[column] = pd.to_numeric(result[column], errors="coerce").fillna(0)
    result["campaign_click_rate"] = np.where(
        result["campaign_emails_30d"] > 0,
        result["campaign_clicks_30d"] / result["campaign_emails_30d"].clip(lower=1),
        0.0,
    ).clip(0, 1)
    result[NUMERIC_FEATURES] = result[NUMERIC_FEATURES].replace([np.inf, -np.inf], 0).fillna(0)
    return result

