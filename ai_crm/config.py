from __future__ import annotations

import os
from pathlib import Path

from dotenv import load_dotenv


PROJECT_ROOT = Path(__file__).resolve().parents[1]
load_dotenv(PROJECT_ROOT / ".env")


def project_path(*parts: str) -> Path:
    return PROJECT_ROOT.joinpath(*parts)


def setting(name: str, default: str) -> str:
    return os.getenv(name, default).strip()

