-- Reference SQL Server schema for replacing the generated CSV demo tables.
CREATE TABLE dbo.Customers (
    CustomerId varchar(32) NOT NULL PRIMARY KEY,
    SignupDate date NOT NULL,
    City nvarchar(80) NULL,
    AcquisitionChannel nvarchar(40) NULL
);

CREATE TABLE dbo.Transactions (
    TransactionId varchar(40) NOT NULL PRIMARY KEY,
    CustomerId varchar(32) NOT NULL,
    OrderDate date NOT NULL,
    Amount decimal(12, 2) NOT NULL CHECK (Amount >= 0),
    Category nvarchar(80) NULL,
    Channel nvarchar(40) NULL,
    CONSTRAINT FK_Transactions_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);
CREATE INDEX IX_Transactions_Customer_OrderDate ON dbo.Transactions(CustomerId, OrderDate);

CREATE TABLE dbo.CustomerActivity (
    CustomerId varchar(32) NOT NULL PRIMARY KEY,
    SnapshotDate date NOT NULL,
    Sessions30d int NOT NULL DEFAULT 0,
    ProductViews30d int NOT NULL DEFAULT 0,
    CartAdds30d int NOT NULL DEFAULT 0,
    CampaignEmails30d int NOT NULL DEFAULT 0,
    CampaignClicks30d int NOT NULL DEFAULT 0,
    CONSTRAINT FK_CustomerActivity_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);

CREATE TABLE dbo.Interactions (
    InteractionId varchar(40) NOT NULL PRIMARY KEY,
    CustomerId varchar(32) NOT NULL,
    InteractionDate date NOT NULL,
    InteractionType varchar(40) NOT NULL,
    CONSTRAINT FK_Interactions_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);
CREATE INDEX IX_Interactions_Customer_InteractionDate ON dbo.Interactions(CustomerId, InteractionDate);

-- Outcomes are future labels for offline model training. Keep future outcome data
-- out of the feature query used to score the same snapshot.
CREATE TABLE dbo.CustomerOutcomes (
    CustomerId varchar(32) NOT NULL PRIMARY KEY,
    ChurnNext30d bit NOT NULL,
    PurchaseNext30d bit NOT NULL,
    CONSTRAINT FK_CustomerOutcomes_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(CustomerId)
);

