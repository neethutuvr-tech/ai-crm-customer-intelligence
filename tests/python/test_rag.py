from __future__ import annotations

import hashlib
import tempfile
import unittest
from pathlib import Path

import numpy as np
from pypdf import PdfWriter
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject
from qdrant_client import QdrantClient

from ai_crm.knowledge import _split_document, load_knowledge_base
from ai_crm.retrieval import HybridRetriever
from ai_crm.vector_store import QdrantVectorStore


class FakeEmbeddingModel:
    def encode(self, texts, normalize_embeddings=True, show_progress_bar=False):
        vectors = np.zeros((len(texts), 64), dtype=np.float32)
        for row, text in enumerate(texts):
            for token in text.lower().split():
                slot = int.from_bytes(hashlib.sha256(token.encode("utf-8")).digest()[:2], "big") % 64
                vectors[row, slot] += 1.0
        if normalize_embeddings:
            norms = np.linalg.norm(vectors, axis=1, keepdims=True)
            vectors = vectors / np.maximum(norms, 1e-12)
        return vectors


def _write_pdf(path: Path, text: str | None = None) -> None:
    writer = PdfWriter()
    page = writer.add_blank_page(width=612, height=792)
    if text is not None:
        font = DictionaryObject(
            {
                NameObject("/Type"): NameObject("/Font"),
                NameObject("/Subtype"): NameObject("/Type1"),
                NameObject("/BaseFont"): NameObject("/Helvetica"),
            }
        )
        font_reference = writer._add_object(font)
        page[NameObject("/Resources")] = DictionaryObject(
            {NameObject("/Font"): DictionaryObject({NameObject("/F1"): font_reference})}
        )
        content = DecodedStreamObject()
        content.set_data(f"BT /F1 12 Tf 72 720 Td ({text}) Tj ET".encode("ascii"))
        page[NameObject("/Contents")] = writer._add_object(content)
    with path.open("wb") as output:
        writer.write(output)


class KnowledgeIngestionTests(unittest.TestCase):
    def test_reads_markdown_text_and_pdf_with_page_metadata(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "retention.md").write_text("# Retention Playbook\n\nReview renewal timing.", encoding="utf-8")
            (root / "notes.txt").write_text("Call guidance\nUse a human review.", encoding="utf-8")
            _write_pdf(root / "policy.pdf", "Renewal policy evidence.")
            result = load_knowledge_base(root)

        self.assertEqual({"retention", "notes", "policy"}, {source["source_id"] for source in result.sources})
        self.assertEqual("Retention Playbook", next(source["title"] for source in result.sources if source["source_id"] == "retention"))
        pdf_chunk = next(chunk for chunk in result.chunks if chunk["source_id"] == "policy")
        self.assertEqual([1], pdf_chunk["page_numbers"])
        self.assertIn("PDF PAGE 1", pdf_chunk["text"])
        self.assertEqual([], result.warnings)

    def test_skips_scanned_pdf_and_reports_why(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "playbook.txt").write_text("A usable policy.", encoding="utf-8")
            _write_pdf(root / "scan.pdf")
            result = load_knowledge_base(root)

        self.assertEqual(1, len(result.sources))
        self.assertIn("no extractable text", result.warnings[0])

    def test_source_ids_disambiguate_files_with_the_same_stem(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "policy.md").write_text("# Markdown policy\nUse the review process.", encoding="utf-8")
            (root / "policy.txt").write_text("Text policy\nUse the review process.", encoding="utf-8")
            result = load_knowledge_base(root)

        self.assertEqual({"policy.md", "policy.txt"}, {source["source_id"] for source in result.sources})

    def test_chunking_preserves_overlap(self):
        text = ("First paragraph. " * 80) + "\n\n" + ("Second paragraph. " * 80)
        chunks = _split_document(text, max_chars=300, overlap_chars=50)

        self.assertGreater(len(chunks), 2)
        self.assertTrue(all(len(chunk) <= 300 for chunk in chunks))
        self.assertIn(chunks[0][-40:], chunks[1])


class QdrantVectorStoreTests(unittest.TestCase):
    def test_vectors_persist_and_reindex_removes_stale_points(self):
        with tempfile.TemporaryDirectory() as temporary:
            store_path = Path(temporary) / "qdrant"
            store = QdrantVectorStore(storage_path=store_path, collection_name="test_knowledge")
            first = {
                "chunk_id": "retention#1",
                "source_id": "retention",
                "title": "Retention",
                "file_name": "retention.md",
                "document_type": "md",
                "page_numbers": [],
                "text": "Renewal guidance",
            }
            second = {**first, "chunk_id": "campaign#1", "source_id": "campaign", "title": "Campaign"}
            store.replace_documents([first, second], np.array([[1.0, 0.0], [0.0, 1.0]]), "test-embedder")
            self.assertEqual("retention#1", store.search(np.array([1.0, 0.0]), 1)[0][0]["chunk_id"])

            store.replace_documents([second], np.array([[0.0, 1.0]]), "test-embedder")
            self.assertEqual(1, store.point_count())
            store.client.close()

            reopened = QdrantVectorStore(storage_path=store_path, collection_name="test_knowledge")
            self.assertEqual(1, reopened.point_count())
            self.assertEqual("campaign#1", reopened.search(np.array([0.0, 1.0]), 1)[0][0]["chunk_id"])
            reopened.client.close()

    def test_hybrid_retriever_uses_the_vector_index_and_reports_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "retention.md").write_text("# Retention\n\nrenewal retention human review", encoding="utf-8")
            (root / "campaign.txt").write_text("campaign launch audience message", encoding="utf-8")
            vector_store = QdrantVectorStore(client=QdrantClient(":memory:"), collection_name="retriever_test")
            retriever = HybridRetriever(
                knowledge_dir=root,
                embedding_model=FakeEmbeddingModel(),
                vector_store=vector_store,
                reranker_enabled=False,
            )

            dense = retriever.retrieve("renewal retention review", mode="dense")
            status = retriever.status()
            vector_store.client.close()

        self.assertEqual("retention", dense[0].source_id)
        self.assertEqual("qdrant_local", status["vector_store"])
        self.assertEqual(2, status["document_count"])
        self.assertEqual(status["chunk_count"], status["indexed_vector_count"])


if __name__ == "__main__":
    unittest.main()
