from __future__ import annotations

import csv
import math
import random
from datetime import date, timedelta
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "data" / "demo"
SNAPSHOT = date(2026, 10, 31)
RNG = random.Random(20261117)

CITY_NAMES = ["Kochi", "Kozhikode", "Bengaluru", "Chennai", "Hyderabad", "Mumbai", "Pune", "Delhi"]
CHANNELS = ["Organic", "Search", "Referral", "Paid social", "Email"]
GROUPS = ["loyal", "active", "occasional", "at_risk"]


def sigmoid(value: float) -> float:
    return 1.0 / (1.0 + math.exp(-value))


def write_csv(filename: str, columns: list[str], rows: list[dict[str, object]]) -> None:
    with (OUTPUT / filename).open("w", newline="", encoding="utf-8") as file:
        writer = csv.DictWriter(file, fieldnames=columns)
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    customers: list[dict[str, object]] = []
    transactions: list[dict[str, object]] = []
    interactions: list[dict[str, object]] = []
    activity: list[dict[str, object]] = []
    outcomes: list[dict[str, object]] = []

    for number in range(1, 1501):
        customer_id = f"C{number:05d}"
        group = RNG.choices(GROUPS, weights=[0.22, 0.36, 0.25, 0.17], k=1)[0]
        signup_date = SNAPSHOT - timedelta(days=RNG.randint(100, 1200))
        recency_ranges = {
            "loyal": (0, 40),
            "active": (5, 100),
            "occasional": (20, 240),
            "at_risk": (100, 360),
        }
        recency = RNG.randint(*recency_ranges[group])
        order_ranges = {"loyal": (8, 22), "active": (3, 10), "occasional": (1, 4), "at_risk": (2, 8)}
        orders = RNG.randint(*order_ranges[group])
        median_spend = {"loyal": 5200, "active": 3200, "occasional": 1800, "at_risk": 2200}[group]
        session_means = {"loyal": 22, "active": 16, "occasional": 8, "at_risk": 5}
        click_means = {"loyal": 0.34, "active": 0.24, "occasional": 0.12, "at_risk": 0.07}
        support_means = {"loyal": 0.2, "active": 0.3, "occasional": 0.25, "at_risk": 0.7}

        customers.append(
            {
                "customer_id": customer_id,
                "signup_date": signup_date.isoformat(),
                "city": RNG.choice(CITY_NAMES),
                "acquisition_channel": RNG.choice(CHANNELS),
            }
        )

        purchase_dates = {recency}
        while len(purchase_dates) < orders:
            purchase_dates.add(RNG.randint(max(recency + 1, 1), 365))
        for order_number, days_ago in enumerate(sorted(purchase_dates)):
            amount = max(250.0, min(45000.0, RNG.lognormvariate(math.log(median_spend), 0.58)))
            transactions.append(
                {
                    "transaction_id": f"T{number:05d}-{order_number:02d}",
                    "customer_id": customer_id,
                    "order_date": (SNAPSHOT - timedelta(days=days_ago)).isoformat(),
                    "amount": round(amount, 2),
                    "category": RNG.choice(["Home", "Apparel", "Beauty", "Electronics", "Grocery"]),
                    "channel": RNG.choice(["Web", "App"]),
                }
            )

        sessions = max(0, round(RNG.gauss(session_means[group], 7)))
        views = max(0, round(RNG.gauss(sessions * 2.7, 8)))
        carts = max(0, round(RNG.gauss(sessions * (0.24 if group in ("loyal", "active") else 0.12), 2)))
        emails = RNG.randint(2, 12)
        click_rate = max(0.0, min(0.85, RNG.gauss(click_means[group], 0.1)))
        clicks = sum(1 for _ in range(emails) if RNG.random() < click_rate)
        support_count = sum(1 for _ in range(RNG.randint(0, 3)) if RNG.random() < support_means[group])
        enquiry_count = sum(1 for _ in range(RNG.randint(0, 3)) if RNG.random() < 0.28)

        activity.append(
            {
                "customer_id": customer_id,
                "snapshot_date": SNAPSHOT.isoformat(),
                "sessions_30d": sessions,
                "product_views_30d": views,
                "cart_adds_30d": carts,
                "campaign_emails_30d": emails,
                "campaign_clicks_30d": clicks,
            }
        )
        for kind, count in [("support_case", support_count), ("enquiry", enquiry_count)]:
            for event_number in range(count):
                interactions.append(
                    {
                        "interaction_id": f"I{number:05d}-{kind[:1]}-{event_number}",
                        "customer_id": customer_id,
                        "interaction_date": (SNAPSHOT - timedelta(days=RNG.randint(0, 180))).isoformat(),
                        "interaction_type": kind,
                    }
                )
        for event_number in range(clicks):
            interactions.append(
                {
                    "interaction_id": f"I{number:05d}-c-{event_number}",
                    "customer_id": customer_id,
                    "interaction_date": (SNAPSHOT - timedelta(days=RNG.randint(0, 30))).isoformat(),
                    "interaction_type": "campaign_click",
                }
            )

        purchase_probability = sigmoid(
            1.10 - recency / 100 + math.log1p(orders) * 0.38 + min(carts, 5) * 0.16 + click_rate * 0.9
        )
        churn_probability = sigmoid(
            -1.45 + recency / 82 - math.log1p(orders) * 0.42 - click_rate * 1.0 + support_count * 0.34
        )
        outcomes.append(
            {
                "customer_id": customer_id,
                "churn_next_30d": int(RNG.random() < churn_probability),
                "purchase_next_30d": int(RNG.random() < purchase_probability),
            }
        )

    write_csv("customers.csv", ["customer_id", "signup_date", "city", "acquisition_channel"], customers)
    write_csv("transactions.csv", ["transaction_id", "customer_id", "order_date", "amount", "category", "channel"], transactions)
    write_csv("interactions.csv", ["interaction_id", "customer_id", "interaction_date", "interaction_type"], interactions)
    write_csv("activity.csv", ["customer_id", "snapshot_date", "sessions_30d", "product_views_30d", "cart_adds_30d", "campaign_emails_30d", "campaign_clicks_30d"], activity)
    write_csv("outcomes.csv", ["customer_id", "churn_next_30d", "purchase_next_30d"], outcomes)
    print(f"Generated {len(customers):,} synthetic customers in {OUTPUT}")


if __name__ == "__main__":
    main()

