from __future__ import annotations

import unittest
from unittest.mock import patch

from pydantic import ValidationError

from ml_api import (
    SegmentationResearchRequest,
    app,
    compare_tenant_segmentation_methods,
    knowledge_status,
    reindex_knowledge,
)


class AiServiceApiTests(unittest.TestCase):
    @staticmethod
    def _customer_rows():
        return [
            {
                "customer_id": f"research-customer-{index}",
                "recency_days": 20 if index >= 6 else 180,
                "orders_365d": 9 if index >= 6 else 1,
                "spend_365d": 1_600 if index >= 6 else 40,
                "avg_order_value": 180 if index >= 6 else 40,
                "sessions_30d": 12 if index >= 6 else 2,
                "product_views_30d": 20 if index >= 6 else 3,
                "cart_adds_30d": 4 if index >= 6 else 0,
                "campaign_click_rate": 0.35 if index >= 6 else 0.02,
                "support_cases_90d": 0,
                "enquiries_90d": 1 if index >= 6 else 0,
                "tenure_days": 540 if index >= 6 else 100,
            }
            for index in range(12)
        ]

    def test_tenant_research_route_returns_three_algorithms(self):
        request = SegmentationResearchRequest(
            customers=self._customer_rows(),
            clusters=2,
            dbscan_eps=0.9,
            dbscan_min_samples=4,
        )
        body = compare_tenant_segmentation_methods(request)

        self.assertEqual(12, body["customer_count"])
        self.assertEqual(
            ["KMeans", "AgglomerativeClustering", "DBSCAN"],
            [algorithm["method"] for algorithm in body["algorithms"]],
        )
        self.assertEqual(12, len(body["algorithms"][0]["membership"]))

    def test_knowledge_status_and_reindex_routes_report_index_state(self):
        status = {"document_count": 2, "chunk_count": 3, "vector_store": "qdrant_local"}
        with patch("ml_api.get_retriever") as get_retriever:
            get_retriever.return_value.status.return_value = status
            response = knowledge_status()
        self.assertEqual(status, response)

        with patch("ml_api.reindex_retriever") as reindex_retriever:
            reindex_retriever.return_value.status.return_value = status
            response = reindex_knowledge()
        self.assertEqual(status, response)

    def test_research_route_rejects_cohorts_over_the_hierarchical_limit(self):
        routes = {route.path: route for route in app.routes if hasattr(route, "path")}
        self.assertIn("/segmentation/research", routes)
        with self.assertRaises(ValidationError):
            SegmentationResearchRequest.model_validate(
                {"customers": self._customer_rows() * 417, "clusters": 2}
            )


if __name__ == "__main__":
    unittest.main()
