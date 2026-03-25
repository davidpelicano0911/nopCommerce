USE nopcommerce_db;
GO

-- 1. CLEANUP: Remove product attributes and checkout requirements that block automated bots
DELETE FROM Product_ProductAttribute_Mapping WHERE ProductId IN (6, 7, 18, 20, 22, 16, 17);
DELETE FROM CheckoutAttributeValue;
DELETE FROM CheckoutAttribute;

-- 2. SUCCESS GROUP (Green Scenario): Infinite stock, no limits
UPDATE Product SET 
    StockQuantity = 999999, 
    ManageInventoryMethodId = 1, 
    OrderMinimumQuantity = 1, 
    OrderMaximumQuantity = 999999,
    Published = 1, 
    DisableBuyButton = 0
WHERE Id IN (6, 7);

-- 3. OUT OF STOCK GROUP (Red Scenario): Zero stock to trigger immediate OOS errors
UPDATE Product SET 
    StockQuantity = 0, 
    ManageInventoryMethodId = 1, 
    OrderMinimumQuantity = 1, 
    Published = 1, 
    DisableBuyButton = 0
WHERE Id IN (18, 22);

-- 4. MAX QUANTITY GROUP (Purple Scenario): High stock but purchase limit = 1 
-- (Triggers error when k6 tries to buy more than allowed)
UPDATE Product SET 
    StockQuantity = 999999, 
    ManageInventoryMethodId = 1, 
    OrderMinimumQuantity = 1, 
    OrderMaximumQuantity = 1,
    Published = 1, 
    DisableBuyButton = 0
WHERE Id IN (16, 17);
GO

-- 5. VERIFICATION: Display current state of the test products
-- This table appears in your terminal before the load test starts
SELECT
    p.Id,
    LEFT(p.Name, 30) AS ProductName,
    p.StockQuantity  AS [Stock],
    p.OrderMaximumQuantity AS [MaxQty],
    CASE
        WHEN p.Id IN (6, 7)   THEN 'SCENARIO: SUCCESS'
        WHEN p.Id IN (18, 22) THEN 'SCENARIO: OUT_OF_STOCK'
        WHEN p.Id IN (16, 17) THEN 'SCENARIO: MAX_LIMIT'
    END AS [TestGroup]
FROM Product p
WHERE p.Id IN (6, 7, 16, 17, 18, 22)
ORDER BY [TestGroup] ASC;

-- 6. EXTRA: Find other simple products for additional scaling if needed
-- (Shows products without complex attributes/variants)
SELECT TOP 5
    p.Id,
    p.Name AS [OtherAvailableProducts]
FROM Product p
WHERE p.Published = 1 AND p.Deleted = 0
AND p.Id NOT IN (6, 7, 16, 17, 18, 22)
ORDER BY p.Id;
GO