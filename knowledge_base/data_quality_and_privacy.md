# CRM data quality and privacy notes

## Grain and joins

The customer table has one row per `customer_id`. Transactions and interactions have many rows per customer. Aggregate each event table to customer grain before joining it to a customer snapshot. Check key uniqueness, referential integrity, observation windows and timezone/date assumptions.

## Feature timing

Features must be available at the scoring date. Labels describe a later outcome window. Do not let future purchases, later campaign responses or post-outcome support events leak into the feature table. Evaluate with a time-based split when production history is available.

## Data minimization

This demo uses generated customer IDs and does not contain names, email addresses, phone numbers or payment details. Keep real contact data out of embeddings and model prompts. Send only the minimum approved fields to an LLM provider, and confirm the organization's retention and data-processing terms before using real CRM data.

## Human review

Scores and generated text are decision support. Keep campaign sends and CRM task creation behind a human review step. Log model version, score date, prompt version and cited sources for auditability.

