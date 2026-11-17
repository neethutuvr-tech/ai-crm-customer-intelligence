from __future__ import annotations

from pathlib import Path

import pandas as pd

from .config import project_path


TABLE_FILES = {
    "customers": "customers.csv",
    "transactions": "transactions.csv",
    "interactions": "interactions.csv",
    "activity": "activity.csv",
    "outcomes": "outcomes.csv",
}


def load_tables(data_dir: Path | None = None) -> dict[str, pd.DataFrame]:
    root = data_dir or project_path("data", "demo")
    missing = [name for name in TABLE_FILES.values() if not (root / name).exists()]
    if missing:
        raise FileNotFoundError(
            f"Demo data is incomplete in {root}. Run: python scripts/generate_demo_data.py"
        )
    return {table: pd.read_csv(root / filename) for table, filename in TABLE_FILES.items()}


def data_quality_report(tables: dict[str, pd.DataFrame]) -> pd.DataFrame:
    rows: list[dict[str, object]] = []
    for table_name, frame in tables.items():
        id_column = "transaction_id" if table_name == "transactions" else (
            "interaction_id" if table_name == "interactions" else "customer_id"
        )
        duplicate_count = int(frame[id_column].duplicated().sum()) if id_column in frame else 0
        rows.append(
            {
                "table": table_name,
                "rows": int(len(frame)),
                "columns": int(frame.shape[1]),
                "missing_cells": int(frame.isna().sum().sum()),
                "duplicate_keys": duplicate_count,
            }
        )
    return pd.DataFrame(rows)

