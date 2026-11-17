from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from rank_bm25 import BM25Okapi
from sentence_transformers import CrossEncoder, SentenceTransformer

from .config import project_path, setting


@dataclass(frozen=True)
class RetrievedChunk:
    chunk_id: str
    source_id: str
    title: str
    text: str
    score: float
    retrieval_method: str


def _tokens(text: str) -> list[str]:
    return re.findall(r"[a-z0-9]+", text.lower())


def _split_document(text: str, max_chars: int = 1700, overlap_chars: int = 220) -> list[str]:
    content = text.strip()
    chunks: list[str] = []
    start = 0
    while start < len(content):
        end = min(start + max_chars, len(content))
        if end < len(content):
            boundary = content.rfind("\n\n", start, end)
            if boundary > start + max_chars // 2:
                end = boundary
        chunk = content[start:end].strip()
        if chunk:
            chunks.append(chunk)
        if end >= len(content):
            break
        start = max(start + 1, end - overlap_chars)
    return chunks or [content[:max_chars]]


class HybridRetriever:
    """Dense + BM25 candidate generation, RRF fusion, then a CrossEncoder rerank."""

    def __init__(self, knowledge_dir: Path | None = None):
        directory = knowledge_dir or project_path("knowledge_base")
        self.chunks: list[dict[str, str]] = []
        for path in sorted(directory.glob("*.md")):
            text = path.read_text(encoding="utf-8")
            title = next((line.lstrip("# ").strip() for line in text.splitlines() if line.startswith("#")), path.stem)
            for index, chunk in enumerate(_split_document(text)):
                self.chunks.append(
                    {
                        "chunk_id": f"{path.stem}#{index + 1}",
                        "source_id": path.stem,
                        "title": title,
                        "text": chunk,
                    }
                )
        if not self.chunks:
            raise FileNotFoundError(f"No Markdown documents found in {directory}")

        self.tokens = [_tokens(chunk["text"]) for chunk in self.chunks]
        self.bm25 = BM25Okapi(self.tokens)
        self.embedding_model = SentenceTransformer(
            setting("EMBEDDING_MODEL", "sentence-transformers/all-MiniLM-L6-v2")
        )
        self.document_vectors = self.embedding_model.encode(
            [chunk["text"] for chunk in self.chunks],
            normalize_embeddings=True,
            show_progress_bar=False,
        )
        self.reranker: CrossEncoder | None = None
        self.reranker_enabled = setting("RERANKER_ENABLED", "true").lower() == "true"

    def _dense_rank(self, query: str, limit: int) -> list[int]:
        query_vector = self.embedding_model.encode(
            [query], normalize_embeddings=True, show_progress_bar=False
        )[0]
        scores = self.document_vectors @ query_vector
        return np.argsort(-scores)[:limit].astype(int).tolist()

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
                self.reranker = CrossEncoder(
                    setting("RERANKER_MODEL", "cross-encoder/ms-marco-MiniLM-L-6-v2")
                )
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

        return [
            RetrievedChunk(
                chunk_id=self.chunks[index]["chunk_id"],
                source_id=self.chunks[index]["source_id"],
                title=self.chunks[index]["title"],
                text=self.chunks[index]["text"],
                score=score,
                retrieval_method=method,
            )
            for index, score in ranked[:top_k]
        ]

    def rank_for_evaluation(self, query: str, mode: str, top_k: int = 5) -> list[str]:
        results = self.retrieve(query, top_k=top_k, mode=mode)
        seen: set[str] = set()
        source_ids: list[str] = []
        for chunk in results:
            if chunk.source_id not in seen:
                seen.add(chunk.source_id)
                source_ids.append(chunk.source_id)
        return source_ids

