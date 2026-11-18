from __future__ import annotations

from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

import numpy as np
from qdrant_client import QdrantClient, models

from .config import project_path, setting


class QdrantVectorStore:
    """Qdrant-backed dense index with local persistent storage by default."""

    def __init__(
        self,
        client: QdrantClient | None = None,
        collection_name: str | None = None,
        storage_path: Path | None = None,
    ) -> None:
        self.collection_name = collection_name or setting("VECTOR_COLLECTION", "crm_knowledge")
        self.is_remote = False
        if client is not None:
            self.client = client
            self.is_remote = not getattr(client, "is_local", True)
        elif setting("QDRANT_URL", ""):
            self.client = QdrantClient(
                url=setting("QDRANT_URL", ""),
                api_key=setting("QDRANT_API_KEY", "") or None,
            )
            self.is_remote = True
        else:
            configured_path = storage_path or Path(setting("VECTOR_DB_PATH", "data/index/qdrant"))
            if not configured_path.is_absolute():
                configured_path = project_path(*configured_path.parts)
            configured_path.mkdir(parents=True, exist_ok=True)
            self.client = QdrantClient(path=str(configured_path))

    def replace_documents(
        self,
        chunks: list[dict[str, object]],
        vectors: np.ndarray,
        embedding_model: str,
    ) -> None:
        vectors = np.asarray(vectors, dtype=np.float32)
        if vectors.ndim != 2 or vectors.shape[0] != len(chunks):
            raise ValueError("Each knowledge chunk must have one embedding vector")
        if not chunks:
            raise ValueError("At least one knowledge chunk is required")

        self._ensure_collection(int(vectors.shape[1]))
        points = []
        expected_ids: set[str] = set()
        for chunk, vector in zip(chunks, vectors):
            point_id = str(uuid5(NAMESPACE_URL, f"{self.collection_name}:{embedding_model}:{chunk['chunk_id']}"))
            expected_ids.add(point_id)
            points.append(
                models.PointStruct(
                    id=point_id,
                    vector=vector.tolist(),
                    payload={**chunk, "embedding_model": embedding_model},
                )
            )

        stale_ids = self._existing_point_ids() - expected_ids
        if stale_ids:
            self.client.delete(
                collection_name=self.collection_name,
                points_selector=models.PointIdsList(points=sorted(stale_ids)),
                wait=True,
            )
        for start in range(0, len(points), 128):
            self.client.upsert(
                collection_name=self.collection_name,
                points=points[start : start + 128],
                wait=True,
            )

    def search(self, vector: np.ndarray, limit: int) -> list[tuple[dict[str, object], float]]:
        response = self.client.query_points(
            collection_name=self.collection_name,
            query=np.asarray(vector, dtype=np.float32).tolist(),
            limit=limit,
            with_payload=True,
        )
        return [
            (dict(point.payload or {}), float(point.score))
            for point in response.points
            if point.payload
        ]

    def point_count(self) -> int:
        if not self.client.collection_exists(self.collection_name):
            return 0
        return int(self.client.count(collection_name=self.collection_name, exact=True).count)

    @property
    def provider(self) -> str:
        return "qdrant_server" if self.is_remote else "qdrant_local"

    def _ensure_collection(self, vector_size: int) -> None:
        if self.client.collection_exists(self.collection_name):
            info = self.client.get_collection(self.collection_name)
            existing_vectors = info.config.params.vectors
            existing_size = getattr(existing_vectors, "size", None)
            if existing_size == vector_size:
                return
            self.client.delete_collection(self.collection_name)

        self.client.create_collection(
            collection_name=self.collection_name,
            vectors_config=models.VectorParams(size=vector_size, distance=models.Distance.COSINE),
        )

    def _existing_point_ids(self) -> set[str]:
        if not self.client.collection_exists(self.collection_name):
            return set()
        point_ids: set[str] = set()
        offset = None
        while True:
            points, offset = self.client.scroll(
                collection_name=self.collection_name,
                limit=256,
                offset=offset,
                with_payload=False,
                with_vectors=False,
            )
            point_ids.update(str(point.id) for point in points)
            if offset is None:
                return point_ids
