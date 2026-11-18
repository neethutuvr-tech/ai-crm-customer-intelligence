import unittest

from ai_crm.segmentation import analyze_customer_segments, analyze_segmentation_research


class CustomerSegmentationTests(unittest.TestCase):
    @staticmethod
    def _two_segment_rows():
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
        return rows

    def test_returns_customer_membership_and_research_metrics(self):
        rows = self._two_segment_rows()

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

    def test_research_benchmark_compares_three_methods(self):
        result = analyze_segmentation_research(
            self._two_segment_rows(), clusters=2, dbscan_min_samples=4
        )

        self.assertEqual(12, result["customer_count"])
        self.assertEqual("tenant-clustering-benchmark-v1", result["model_version"])
        self.assertTrue(result["scikit_learn_version"])
        self.assertEqual(12, len(result["pca_projection"]))
        self.assertEqual(
            ["KMeans", "AgglomerativeClustering", "DBSCAN"],
            [algorithm["method"] for algorithm in result["algorithms"]],
        )
        for algorithm in result["algorithms"]:
            self.assertEqual(12, len(algorithm["membership"]))
            self.assertIn("silhouette_score", algorithm["evaluation"])
            self.assertGreaterEqual(algorithm["evaluation"]["fit_latency_ms"], 0)
        self.assertEqual(2, result["algorithms"][2]["evaluation"]["cluster_count"])
        self.assertEqual(0, result["algorithms"][2]["evaluation"]["noise_count"])

    def test_dbscan_reports_all_noise_without_fabricating_silhouette(self):
        rows = self._two_segment_rows()
        for index, row in enumerate(rows):
            row["spend_365d"] += index * 10
            row["sessions_30d"] += index * 3
            row["tenure_days"] += index * 20

        result = analyze_segmentation_research(
            rows, clusters=2, dbscan_eps=0.1, dbscan_min_samples=12
        )
        dbscan = result["algorithms"][2]

        self.assertEqual(0, dbscan["evaluation"]["cluster_count"])
        self.assertEqual(12, dbscan["evaluation"]["noise_count"])
        self.assertIsNone(dbscan["evaluation"]["silhouette_score"])
        self.assertTrue(all(row["segment"] == "Noise" for row in dbscan["membership"]))

    def test_research_benchmark_enforces_hierarchical_size_limit(self):
        rows = [{"customer_id": f"customer-{index}"} for index in range(5_001)]
        with self.assertRaisesRegex(ValueError, "at most 5,000"):
            analyze_segmentation_research(rows, clusters=2)


if __name__ == "__main__":
    unittest.main()
