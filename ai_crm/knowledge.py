from __future__ import annotations

import logging
import re
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

from pypdf import PdfReader

from .config import project_path


logger = logging.getLogger(__name__)
SUPPORTED_EXTENSIONS = {".md", ".markdown", ".txt", ".pdf"}
MAX_DOCUMENT_BYTES = 20 * 1024 * 1024
MAX_PDF_PAGES = 1_000
MAX_EXTRACTED_CHARACTERS = 2_000_000
PAGE_MARKER = re.compile(r"\[PDF PAGE (\d+)\]")


@dataclass(frozen=True)
class KnowledgeIngestion:
    chunks: list[dict[str, object]]
    sources: list[dict[str, object]]
    warnings: list[str]


def load_knowledge_base(directory: Path | None = None) -> KnowledgeIngestion:
    """Read local Markdown, text, and text-based PDF sources and create cited chunks."""
    root = directory or project_path("knowledge_base")
    chunks: list[dict[str, object]] = []
    sources: list[dict[str, object]] = []
    warnings: list[str] = []

    files = sorted(
        path for path in root.rglob("*")
        if path.is_file() and not path.is_symlink() and path.suffix.lower() in SUPPORTED_EXTENSIONS
    )
    base_ids = [path.relative_to(root).with_suffix("").as_posix() for path in files]
    duplicate_base_ids = {source_id for source_id, count in Counter(base_ids).items() if count > 1}
    for path in files:
        relative_name = path.relative_to(root).as_posix()
        if path.stat().st_size > MAX_DOCUMENT_BYTES:
            warnings.append(f"Skipped {relative_name}: exceeds the 20 MB document limit.")
            continue
        try:
            content, page_count = _read_source(path)
        except Exception as exc:
            warnings.append(f"Skipped {relative_name}: {type(exc).__name__} while extracting text.")
            logger.warning("Could not read knowledge document %s: %s", relative_name, exc)
            continue
        if not content.strip():
            warnings.append(f"Skipped {relative_name}: no extractable text was found.")
            continue
        if len(content) > MAX_EXTRACTED_CHARACTERS:
            content = content[:MAX_EXTRACTED_CHARACTERS]
            warnings.append(f"Truncated {relative_name} at the 2,000,000 character limit.")

        base_source_id = path.relative_to(root).with_suffix("").as_posix()
        extension_suffix = f".{path.suffix.lower().lstrip('.')}" if base_source_id in duplicate_base_ids else ""
        source_id = re.sub(r"[^A-Za-z0-9._/-]+", "-", base_source_id + extension_suffix)
        title = _title(content, path.stem)
        source_chunks = _split_document(content)
        sources.append(
            {
                "source_id": source_id,
                "title": title,
                "file_name": relative_name,
                "document_type": path.suffix.lower().lstrip("."),
                "page_count": page_count,
                "chunk_count": len(source_chunks),
            }
        )
        for index, text in enumerate(source_chunks):
            page_numbers = sorted({int(value) for value in PAGE_MARKER.findall(text)})
            chunks.append(
                {
                    "chunk_id": f"{source_id}#{index + 1}",
                    "source_id": source_id,
                    "title": title,
                    "file_name": relative_name,
                    "document_type": path.suffix.lower().lstrip("."),
                    "page_numbers": page_numbers,
                    "text": text,
                }
            )

    if not chunks:
        raise FileNotFoundError(
            f"No supported, readable knowledge documents found in {root}. Add an .md, .txt, or text-based .pdf file."
        )
    return KnowledgeIngestion(chunks=chunks, sources=sources, warnings=warnings)


def _read_source(path: Path) -> tuple[str, int | None]:
    if path.suffix.lower() != ".pdf":
        return path.read_text(encoding="utf-8-sig"), None

    reader = PdfReader(path, strict=False)
    if reader.is_encrypted:
        raise ValueError("Password-protected PDFs are not supported")
    if len(reader.pages) > MAX_PDF_PAGES:
        raise ValueError("PDF exceeds the 1,000 page limit")
    pages = []
    for page_number, page in enumerate(reader.pages, start=1):
        page_text = page.extract_text() or ""
        if page_text.strip():
            pages.append(f"[PDF PAGE {page_number}]\n{page_text.strip()}")
    return "\n\n".join(pages), len(reader.pages)


def _title(content: str, fallback: str) -> str:
    for line in content.splitlines():
        stripped = line.strip()
        if stripped:
            if PAGE_MARKER.fullmatch(stripped):
                continue
            if stripped.startswith("#"):
                return stripped.lstrip("# ").strip()[:160] or fallback
            return stripped[:160]
    return fallback


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
    return chunks or [content]
