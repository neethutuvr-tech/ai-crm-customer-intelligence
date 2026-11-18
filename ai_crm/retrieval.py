from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from rank_bm25 import BM25Okapi

from .config import project_path, setting
from .knowledge import load_knowledge_base
from .vector_store import QdrantVectorStore


@dataclass(frozen=True)
class RetrievedChunk:
    chunk_id: str
    source_id: str
    title: str
    text: str
    score: float
    retrieval_method: str
    file_name: str = ""
    document_type: str = ""
    page_numbers: tuple[int, ...] = ()


def _tokens(text: str) -> list[str]:
    return re.findall(r"[a-z0-9]+", text.lower())


class HybridRetriever:
    """Dense + BM25 candidate generation, RRF fusion, then a CrossEncoder rerank."""

    def __init__(
        self,
        knowledge_dir: Path | None = None,
        embedding_model: Any | None = None,
        vector_store: QdrantVectorStore | None = None,
        reranker_enabled: bool | None = None,
    ):
        directory = knowledge_dir or project_path("knowledge_base")
        ingestion = load_knowledge_base(directory)
        self.chunks = ingestion.chunks
        self.sources = ingestion.sources
        self.ingestion_warnings = ingestion.warnings

        self.tokens = [_tokens(chunk["text"]) for chunk in self.chunks]
        self.bm25 = BM25Okapi(self.tokens)
        self.embedding_model_name = setting("EMBEDDING_MODEL", "sentence-transformers/all-MiniLM-L6-v2")
        if embedding_model is None:
            from sentence_transformers import SentenceTransformer

            embedding_model = SentenceTransformer(self.embedding_model_name)
        self.embedding_model = embedding_model
        document_vectors = self.embedding_model.encode(
            [chunk["text"] for chunk in self.chunks],
            normalize_embeddings=True,
            show_progress_bar=False,
        )
        self.vector_store = vector_store or QdrantVectorStore()
        self.vector_store.replace_documents(self.chunks, document_vectors, self.embedding_model_name)
        self.chunk_indices = {chunk["chunk_id"]: index for index, chunk in enumerate(self.chunks)}
        self.reranker: Any | None = None
        self.reranker_enabled = (
            setting("RERANKER_ENABLED", "true").lower() == "true"
            if reranker_enabled is None
            else reranker_enabled
        )

    def _dense_rank(self, query: str, limit: int) -> list[int]:
        query_vector = self.embedding_model.encode(
            [query], normalize_embeddings=True, show_progress_bar=False
        )[0]
        results = self.vector_store.search(query_vector, limit)
        return [
            self.chunk_indices[str(payload["chunk_id"])]
            for payload, _ in results
            if str(payload.get("chunk_id", "")) in self.chunk_indices
        ]

    def _bm25_rank(self, query: str, limit: int) -> list[int]:
        scores = self.bm25.get_scores(_tokens(query))
        return np.argsort(-scores)[:limit].astype(int).tolist()

    @staticmethod
    def _rrf(rankings: list[list[int]], k: int = 60) -> list[tuple[int, float]]:
        scores: dict[int, float] = {}
        for ranking in rankings:
            for rank, index in enumerate(ranking):
                scores[index] = scores.get(index, 0.0) + 1.0 / (k + rank + 1)
        return sorted(scores.items(), key=lambda pair: pair[1], reverse=True)

    def retrieve(
        self,
        query: str,
        top_k: int = 4,
        mode: str = "hybrid_reranked",
    ) -> list[RetrievedChunk]:
        candidate_count = min(max(top_k * 3, 8), len(self.chunks))
        if mode == "bm25":
            ranked = [(index, float(len(self.chunks) - rank)) for rank, index in enumerate(self._bm25_rank(query, top_k))]
        elif mode == "dense":
            ranked = [(index, float(len(self.chunks) - rank)) for rank, index in enumerate(self._dense_rank(query, top_k))]
        else:
            dense = self._dense_rank(query, candidate_count)
            lexical = self._bm25_rank(query, candidate_count)
            ranked = self._rrf([dense, lexical])

        method = "hybrid_rrf"
        if mode == "hybrid_reranked" and self.reranker_enabled:
            if self.reranker is None:
                from sentence_transformers import CrossEncoder

                self.reranker = CrossEncoder(setting("RERANKER_MODEL", "cross-encoder/ms-marco-MiniLM-L-6-v2"))
            candidates = ranked[:candidate_count]
            pairs = [(query, self.chunks[index]["text"]) for index, _ in candidates]
            scores = self.reranker.predict(pairs, show_progress_bar=False)
            ranked = sorted(
                [(index, float(score)) for (index, _), score in zip(candidates, scores)],
                key=lambda pair: pair[1],
                reverse=True,
            )
            method = "hybrid_rrf+cross_encoder"
        elif mode == "bm25":
            method = "bm25"
        elif mode == "dense":
            method = "dense"

        results = []
        for index, score in ranked[:top_k]:
            chunk = self.chunks[index]
            results.append(RetrievedChunk(
                chunk_id=self.chunks[index]["chunk_id"],
                source_id=self.chunks[index]["source_id"],
                title=self.chunks[index]["title"],
                text=self.chunks[index]["text"],
                score=score,
                retrieval_method=method,
                file_name=str(chunk["file_name"]),
                document_type=str(chunk["document_type"]),
                page_numbers=tuple(int(value) for value in chunk["page_numbers"]),
            ))
        return results

    def rank_for_evaluation(self, query: str, mode: str, top_k: int = 5) -> list[str]:
        results = self.retrieve(query, top_k=top_k, mode=mode)
        seen: set[str] = set()
        source_ids: list[str] = []
        for chunk in results:
            if chunk.source_id not in seen:
                seen.add(chunk.source_id)
                source_ids.append(chunk.source_id)
        return source_ids

    def status(self) -> dict[str, object]:
        return {
            "document_count": len(self.sources),
            "chunk_count": len(self.chunks),
            "indexed_vector_count": self.vector_store.point_count(),
            "vector_store": self.vector_store.provider,
            "embedding_model": self.embedding_model_name,
            "reranker_enabled": self.reranker_enabled,
            "sources": self.sources,
            "warnings": self.ingestion_warnings,
        }

