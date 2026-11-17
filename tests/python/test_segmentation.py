import unittest

from ai_crm.segmentation import analyze_customer_segments


class CustomerSegmentationTests(unittest.TestCase):
    def test_returns_customer_membership_and_research_metrics(self):
        rows = []
        for index in range(12):
            high_value = index >= 6
            rows.append(
                {
                    "customer_id": f"customer-{index}",
                    "recency_days": 15 if high_value else 160,
                    "orders_365d": 12 if high_value else 1,
                    "spend_365d": 2400 if high_value else 40,
                    "avg_order_value": 200 if high_value else 40,
                    "sessions_30d": 18 if high_value else 2,
                    "cart_adds_30d": 5 if high_value else 0,
                    "campaign_click_rate": 0.4 if high_value else 0.02,
                    "support_cases_90d": 1 if high_value else 0,
                    "tenure_days": 400 if high_value else 90,
                }
            )

        result = analyze_customer_segments(rows, clusters=2)

        self.assertEqual(12, result["customer_count"])
        self.assertEqual(12, len(result["membership"]))
        self.assertIsNotNone(result["evaluation"]["silhouette_score"])
        self.assertEqual(2, len(result["clusters"]))
        self.assertEqual("tenant-kmeans-v1", result["model_version"])

    def test_rejects_duplicate_customer_ids(self):
        rows = [{"customer_id": "same"}, {"customer_id": "same"}, {"customer_id": "other"}]
        with self.assertRaisesRegex(ValueError, "unique"):
            analyze_customer_segments(rows, clusters=2)

    def test_rejects_identical_profiles(self):
        rows = [{"customer_id": f"customer-{index}"} for index in range(5)]
        with self.assertRaisesRegex(ValueError, "distinct profiles"):
            analyze_customer_segments(rows, clusters=2)


if __name__ == "__main__":
    unittest.main()
