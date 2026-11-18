from __future__ import annotations

import unittest
from unittest.mock import patch

import numpy as np
import pandas as pd

from ai_crm.features import NUMERIC_FEATURES
from ai_crm.models import score_customers


class SupervisedModelResearchTests(unittest.TestCase):
    def _demo_cohort(self, size: int = 240):
        rng = np.random.default_rng(42)
        orders = rng.poisson(4, size)
        spend = np.maximum(0, orders * rng.uniform(35, 180, size) + rng.normal(0, 50, size))
        recency = rng.integers(0, 366, size)
        cart_adds = rng.poisson(1.5, size)
        click_rate = rng.uniform(0, 0.7, size)
        features = pd.DataFrame(
            {
                "customer_id": [f"demo-{index}" for index in range(size)],
                "recency_days": recency,
                "orders_365d": orders,
                "spend_365d": spend,
                "avg_order_value": np.divide(spend, np.maximum(orders, 1)),
                "sessions_30d": rng.poisson(8, size),
                "product_views_30d": rng.poisson(15, size),
                "cart_adds_30d": cart_adds,
                "campaign_click_rate": click_rate,
                "support_cases_90d": rng.poisson(0.6, size),
                "enquiries_90d": rng.poisson(0.8, size),
                "tenure_days": rng.integers(20, 1_500, size),
            }
        )
        churn_score = recency / 365 + rng.normal(0, 0.4, size)
        purchase_score = orders / 10 + cart_adds / 6 + click_rate + rng.normal(0, 0.3, size)
        outcomes = pd.DataFrame(
            {
                "customer_id": features["customer_id"],
                "churn_next_30d": (churn_score > np.quantile(churn_score, 0.62)).astype(int),
                "purchase_next_30d": (purchase_score > np.quantile(purchase_score, 0.63)).astype(int),
            }
        )
        self.assertEqual(len(NUMERIC_FEATURES), len([column for column in features if column in NUMERIC_FEATURES]))
        return features, outcomes

    def test_compares_calibrated_logistic_and_random_forest_baselines(self):
        features, outcomes = self._demo_cohort()
        with patch("ai_crm.models.setting", return_value="logistic_regression"):
            scored, metrics = score_customers(features, outcomes, clusters=3)

        self.assertEqual(len(features), len(scored))
        self.assertTrue(scored["churn_probability"].between(0, 1).all())
        self.assertTrue(scored["purchase_probability"].between(0, 1).all())
        for target in ("churn_probability", "purchase_probability"):
            result = metrics[target]
            self.assertEqual("synthetic-outcome-baselines-v2", result["model_version"])
            self.assertEqual("synthetic demonstration; not real business performance", result["evaluation_scope"])
            self.assertEqual("stratified random 75/25 holdout", result["split_strategy"].split(";")[0])
            self.assertEqual("logistic_regression", result["selected_model"])
            self.assertEqual({"logistic_regression", "random_forest"}, set(result["models"]))
            for model_name, model_result in result["models"].items():
                self.assertIn("roc_auc", model_result)
                self.assertIn("pr_auc", model_result)
                self.assertIn("brier_score", model_result)
                self.assertEqual(model_result["test_count"], sum(map(sum, model_result["confusion_matrix_at_0_5"])))
                self.assertTrue(model_result["calibration_curve"])
                self.assertEqual(10, len(result["feature_importance"][model_name]))


if __name__ == "__main__":
    unittest.main()
