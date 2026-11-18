from __future__ import annotations

import unittest
from unittest.mock import Mock, patch

from ai_crm.llm import generate_recommendation
from ai_crm.retrieval import RetrievedChunk


class FakeRetriever:
    def __init__(self):
        self.chunk = RetrievedChunk(
            chunk_id="retention_playbook#1",
            source_id="retention_playbook",
            title="Retention Playbook",
            text="Review renewal timing and recent support outcomes with a human owner.",
            score=0.83,
            retrieval_method="hybrid_rrf+cross_encoder",
            file_name="retention_playbook.pdf",
            document_type="pdf",
            page_numbers=(2, 3),
        )

    def retrieve(self, question: str, top_k: int, mode: str):
        return [self.chunk]


class RecommendationGroundingTests(unittest.TestCase):
    def test_extract_fallback_labels_sources_and_model_limitations(self):
        with patch("ai_crm.llm._provider", return_value="extractive"):
            answer = generate_recommendation(
                "What should be reviewed before renewal?",
                {"orders_365d": 2, "spend_365d": 140.0},
                FakeRetriever(),
            )

        self.assertEqual("extractive", answer["llm_provider"])
        self.assertIn("LLM generation is not configured", answer["uncertainty"])
        self.assertEqual("retention_playbook", answer["evidence"][-1]["citations"][0])
        self.assertEqual([2, 3], answer["sources"][0]["page_numbers"])
        self.assertEqual({"orders_365d": 2, "spend_365d": 140.0}, answer["provenance"]["observed_crm_aggregates"])

    def test_generated_answer_drops_citations_outside_retrieved_sources(self):
        openai_client = Mock()
        openai_client.responses.create.return_value.output_text = (
            '{"summary":"Review renewal timing.",'
            '"evidence":[{"claim":"The source recommends a human review.",'
            '"citations":["retention_playbook","invented_source"]}],'
            '"recommended_actions":["Review the follow-up."],"uncertainty":"Prediction unavailable."}'
        )
        with patch("ai_crm.llm._provider", return_value="openai"), patch(
            "ai_crm.llm.OpenAI", return_value=openai_client
        ), patch("ai_crm.llm.setting", side_effect=lambda name, default: "test-key" if name == "OPENAI_API_KEY" else default):
            answer = generate_recommendation(
                "What should be reviewed?",
                {"crm_status": "Active", "churn_probability": None},
                FakeRetriever(),
            )

        self.assertEqual(["retention_playbook"], answer["evidence"][0]["citations"])
        self.assertEqual(1, answer["citation_audit"]["invalid_citation_count"])
        self.assertEqual(0.5, answer["citation_audit"]["citation_validity_rate"])
        self.assertEqual(["retention_playbook"], answer["provenance"]["retrieved_knowledge_source_ids"])
        self.assertIsNone(answer["provenance"]["model_predictions"]["churn_probability"])
        request = openai_client.responses.create.call_args.kwargs
        self.assertIn("pages: 2, 3", request["input"])
        self.assertIn("retention_playbook#1", request["input"])


if __name__ == "__main__":
    unittest.main()
