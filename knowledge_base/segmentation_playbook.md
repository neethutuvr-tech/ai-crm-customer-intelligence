# Customer segmentation playbook

## Purpose

Use segmentation to describe patterns in observed purchase behaviour and help a marketing or sales analyst choose a relevant next step. A cluster is a statistical summary, not a permanent customer identity or a causal explanation.

## RFM features

- **Recency** is the number of days since the latest purchase in the observation window. Lower values mean more recent activity.
- **Frequency** is the count of purchases in the last 365 days.
- **Monetary** is the sum of purchase value in the same window.

Build one row per customer before fitting a model. Aggregate transaction rows first; a direct join between customer and transaction tables duplicates customers with multiple orders. Inspect missing data, duplicate keys, skew and outliers. Log-transform highly skewed positive values, scale numeric features, then compare candidate cluster counts with silhouette score and Davies–Bouldin index.

## Interpreting clusters

Review cluster centres and feature distributions before assigning names such as high value, steady, occasional or at risk. Show the segment size and defining signals alongside the label. Revisit segments when purchase mix, campaign policy or customer behaviour changes.

## Use in CRM

Segments can inform a campaign hypothesis, but a segment alone should not trigger a discount or contact. Check consent, campaign fatigue, current support context and the expected margin. Prefer a randomized holdout or controlled experiment when measuring campaign lift.

