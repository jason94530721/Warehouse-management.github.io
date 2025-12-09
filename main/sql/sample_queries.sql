
--1. WHERE 條件查詢
-- 查詢員工管理哪些倉庫
SELECT w.*
FROM 倉庫 w
WHERE w.員工編號 = 1;

-- 查詢倉庫內的產品
SELECT p.*
FROM 庫存 i
JOIN 產品 p ON i.產品編號 = p.產品編號
WHERE i.倉庫編號 = 1;

-- 查詢產品的庫存數量
SELECT *
FROM 庫存
WHERE 產品編號 = 1;

-- 查詢入庫訂單裡的明細資料
SELECT *
FROM 入庫明細
WHERE 入庫訂單編號 = 1;

-- 查詢出庫訂單裡的明細資料
SELECT *
FROM 出庫明細
WHERE 出庫訂單編號 = 1;

--2. JOIN 多表查詢

-- 員工跟倉庫
SELECT e.姓名, w.*
FROM 員工 e
JOIN 倉庫 w ON e.員工編號 = w.員工編號;

-- 倉庫跟庫存
SELECT w.名稱 AS 倉庫名稱, i.*
FROM 倉庫 w
JOIN 庫存 i ON w.倉庫編號 = i.倉庫編號;

-- 庫存跟產品
SELECT p.名稱 AS 產品名稱, i.數量
FROM 庫存 i
JOIN 產品 p ON i.產品編號 = p.產品編號;

-- 出庫訂單跟出庫明細
SELECT o.*, d.*
FROM 出庫訂單 o
JOIN 出庫明細 d ON o.出庫訂單編號 = d.出庫訂單編號;

-- 入庫訂單跟入庫明細
SELECT o.*, d.*
FROM 入庫訂單 o
JOIN 入庫明細 d ON o.入庫訂單編號 = d.入庫訂單編號;


--3. GROUP BY 分組查詢

-- 員工管理的倉庫數量
SELECT e.姓名, COUNT(w.倉庫編號) AS 管理倉庫數
FROM 員工 e
LEFT JOIN 倉庫 w ON e.員工編號 = w.員工編號
GROUP BY e.姓名;

-- 每個倉庫的庫存統計
SELECT 倉庫編號, SUM(數量) AS 總庫存數量
FROM 庫存
GROUP BY 倉庫編號;

-- 每種產品的總庫存量
SELECT 產品編號, SUM(數量) AS 產品總庫存
FROM 庫存
GROUP BY 產品編號;

-- 每張出庫訂單的總出貨量
SELECT 出庫訂單編號, SUM(數量) AS 出貨總量
FROM 出庫明細
GROUP BY 出庫訂單編號;

-- 每張入庫訂單的總入庫量
SELECT 入庫訂單編號, SUM(數量) AS 入庫總量
FROM 入庫明細
GROUP BY 入庫訂單編號;


--4. ORDER BY 排序查詢

SELECT * FROM 員工 ORDER BY 姓名;
SELECT * FROM 倉庫 ORDER BY 名稱;
SELECT * FROM 產品 ORDER BY 名稱;
SELECT * FROM 庫存 ORDER BY 倉庫編號, 產品編號;
SELECT * FROM 入庫訂單 ORDER BY 收貨日期 DESC;
SELECT * FROM 出庫訂單 ORDER BY 出貨日期 DESC;
SELECT * FROM 入庫明細 ORDER BY 入庫訂單編號;
SELECT * FROM 出庫明細 ORDER BY 出庫訂單編號;


--5. INSERT 新增資料

INSERT INTO 員工 (姓名, 聯絡資訊) VALUES (N'新員工', N'0900-111-222');
INSERT INTO 倉庫 (員工編號, 名稱, 儲存空間) VALUES (1, N'新倉庫', 500.00);
INSERT INTO 產品 (名稱, 尺寸, 重量, 價格) VALUES (N'新產品', 10, 0.5, 100);
INSERT INTO 庫存 (產品編號, 倉庫編號, 數量) VALUES (1, 6, 50);
INSERT INTO 入庫訂單 (倉庫編號, 供應商名稱, 收貨日期) VALUES (1, N'供應商A', '2025-01-01');
INSERT INTO 出庫訂單 (倉庫編號, 出貨日期, 送達地址) VALUES (1, '2025-01-02', N'台北市');
INSERT INTO 入庫明細 (入庫訂單編號, 產品編號, 數量) VALUES (1, 1, 30);
INSERT INTO 出庫明細 (出庫訂單編號, 產品編號, 數量) VALUES (1, 1, 20);


--6. UPDATE 更新資料

UPDATE 員工 SET 聯絡資訊 = N'0900-999-000' WHERE 員工編號 = 1;
UPDATE 倉庫 SET 名稱 = N'更新倉庫' WHERE 倉庫編號 = 1;
UPDATE 產品 SET 價格 = 2000 WHERE 產品編號 = 1;
UPDATE 庫存 SET 數量 = 999 WHERE 產品編號 = 1 AND 倉庫編號 = 1;
UPDATE 入庫訂單 SET 供應商名稱 = N'更新供應商' WHERE 入庫訂單編號 = 1;
UPDATE 出庫訂單 SET 送達地址 = N'台中市' WHERE 出庫訂單編號 = 1;
UPDATE 入庫明細 SET 數量 = 800 WHERE 入庫明細編號 = 1;
UPDATE 出庫明細 SET 數量 = 27 WHERE 出庫明細編號 = 1;

--7. DELETE 刪除資料

DELETE FROM 員工 WHERE 員工編號 = 5;
DELETE FROM 倉庫 WHERE 倉庫編號 = 99;
DELETE FROM 產品 WHERE 產品編號 = 99;
DELETE FROM 庫存 WHERE 產品編號 = 1 AND 倉庫編號 = 2;
DELETE FROM 入庫訂單 WHERE 入庫訂單編號 = 99;
DELETE FROM 出庫訂單 WHERE 出庫訂單編號 = 99;
DELETE FROM 入庫明細 WHERE 入庫明細編號 = 99;
DELETE FROM 出庫明細 WHERE 出庫明細編號 = 99;

--8索引
--倉庫.負責員工編號
CREATE INDEX IX_倉庫_負責員工編號 ON 倉庫([員工編號]);

--庫存.產品編號
CREATE INDEX IX_庫存_產品編號 ON 庫存([產品編號]);

--庫存.倉庫編號
CREATE INDEX IX_庫存_倉庫編號 ON 庫存([倉庫編號]);
--入庫訂單.倉庫編號
CREATE INDEX IX_入庫訂單_倉庫編號 ON 入庫訂單([倉庫編號]);
--入庫明細.入庫訂單編號
CREATE INDEX IX_入庫明細_入庫訂單編號 ON 入庫明細([入庫訂單編號]);
--入庫明細.產品編號
CREATE INDEX IX_入庫明細_產品編號 ON 入庫明細([產品編號]);

--出庫訂單.倉庫編號
CREATE INDEX IX_出庫訂單_倉庫編號 ON 出庫訂單([倉庫編號]);
--出庫明細.出庫訂單編號
CREATE INDEX IX_出庫明細_出庫訂單編號 ON 出庫明細([出庫訂單編號]);
--出庫明細.產品編號
CREATE INDEX IX_出庫明細_產品編號 ON 出庫明細([產品編號]);

--9基本查詢

SELECT * FROM 員工 ORDER BY 姓名;
SELECT * FROM 倉庫 ORDER BY 名稱;
SELECT * FROM 產品 ORDER BY 名稱;
SELECT * FROM 庫存 ORDER BY 倉庫編號,產品編號;
SELECT * FROM 入庫訂單 ORDER BY 收貨日期 DESC;
SELECT * FROM 出庫訂單 ORDER BY 出貨日期 DESC;
SELECT * FROM 入庫明細 ORDER BY 入庫訂單編號;
SELECT * FROM 出庫明細 ORDER BY 出庫訂單編號;