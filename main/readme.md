## 📦 倉儲管理系統資料庫文件 (Warehouse Management System Database Documentation)

本文件概述了倉儲管理系統所使用的 **關聯式資料庫結構 (SQL)** 及其相關操作查詢範例，以及獨立的 **MongoDB 稽核日誌資料庫** 查詢範例。

---

### 1. 關聯式資料庫結構 (SQL Schema)

根據 `Warehouse management.sql` 文件，資料庫設計包含以下表格，用於管理員工、倉庫、產品、庫存、入庫和出庫訂單。

| 表格名稱 | 描述 | 主要欄位 | 關聯/外鍵 |
| :--- | :--- | :--- | :--- |
| **員工** | 負責管理倉庫的人員資訊。 | `員工編號` (PK, IDENTITY), `姓名` | |
| **倉庫** | 儲存產品的實體位置。 | `倉庫編號` (PK, IDENTITY), `名稱` | `員工編號` (FK) -> 員工 |
| **產品** | 系統中所有可庫存的物品。 | `產品編號` (PK, IDENTITY), `名稱` | |
| **庫存** | 追蹤特定產品在特定倉庫中的數量。 | `產品編號` (PK, FK), `倉庫編號` (PK, FK) | `產品編號` (FK) -> 產品, `倉庫編號` (FK) -> 倉庫 |
| **入庫訂單** | 記錄產品進入倉庫的單據。 | `入庫訂單編號` (PK, IDENTITY), `收貨日期` | `倉庫編號` (FK) -> 倉庫 |
| **入庫明細** | 記錄單筆入庫訂單中各產品的數量。 | `入庫明細編號` (PK, IDENTITY) | `入庫訂單編號` (FK) -> 入庫訂單, `產品編號` (FK) -> 產品 |
| **出庫訂單** | 記錄產品離開倉庫的單據。 | `出庫訂單編號` (PK, IDENTITY), `出貨日期` | `倉庫編號` (FK) -> 倉庫 |
| **出庫明細** | 記錄單筆出庫訂單中各產品的數量。 | `出庫明細編號` (PK, IDENTITY) | `出庫訂單編號` (FK) -> 出庫訂單, `產品編號` (FK) -> 產品 |

---

### 2. SQL 查詢範例 (Sample SQL Queries)

以下查詢範例來自 `sample_queries.sql`，涵蓋了常見的資料庫操作：

#### 2.1. 條件查詢 (WHERE)

* **查詢員工編號為 1 的員工所管理的倉庫：**
    ```sql
    SELECT w.*
    FROM 倉庫 w
    WHERE w.員工編號 = 1;
    ```
* **查詢產品編號為 1 的庫存數量：**
    ```sql
    SELECT *
    FROM 庫存
    WHERE 產品編號 = 1;
    ```

#### 2.2. 多表連接查詢 (JOIN)

* **員工與倉庫的關聯查詢：**
    ```sql
    SELECT e.姓名, w.*
    FROM 員工 e
    JOIN 倉庫 w ON e.員工編號 = w.員工編號;
    ```
* **庫存與產品的詳細資訊：**
    ```sql
    SELECT p.名稱 AS 產品名稱, i.數量
    FROM 庫存 i
    JOIN 產品 p ON i.產品編號 = p.產品編號;
    ```

#### 2.3. 分組聚合查詢 (GROUP BY)

* **每位員工管理的倉庫數量：**
    ```sql
    SELECT e.姓名, COUNT(w.倉庫編號) AS 管理倉庫數
    FROM 員工 e
    LEFT JOIN 倉庫 w ON e.員工編號 = w.員工編號
    GROUP BY e.姓名;
    ```
* **每種產品的總庫存量：**
    ```sql
    SELECT 產品編號, SUM(數量) AS 產品總庫存
    FROM 庫存
    GROUP BY 產品編號;
    ```

#### 2.4. 資料操作 (DML) 範例

| 操作 | 範例 (以員工表為例) |
| :--- | :--- |
| **INSERT** (新增) | `INSERT INTO 員工 (姓名, 聯絡資訊) VALUES (N'新員工', N'0900-111-222');` |
| **UPDATE** (更新) | `UPDATE 員工 SET 聯絡資訊 = N'0900-999-000' WHERE 員工編號 = 1;` |
| **DELETE** (刪除) | `DELETE FROM 員工 WHERE 員工編號 = 5;` |

---

### 3. MongoDB 查詢範例 (Audit Logs)

稽核日誌 (Audit Logs) 儲存在 MongoDB 的 `auditLogs` Collection 中，用於追蹤系統內的操作行為。

#### 3.1. Find 查詢範例

| 查詢名稱 | 描述 | 查詢條件 (query) | 排序/限制 (options) |
| :--- | :--- | :--- | :--- |
| **Find\_Latest\_100\_Records** | 查找最近發生的 100 筆稽核記錄。 | `{}` (所有記錄) | `sort: { "timestamp": -1 }, limit: 100` |
| **Find\_Stock\_Initialization\_By\_Warehouse** | 查找特定倉庫 (ID=1) 的產品初始化記錄。 | `{"actionType": "INITIALIZE_STOCK", "data.warehouseId": 1}` | `sort: { "timestamp": -1 }` |

#### 3.2. Aggregate 聚合查詢範例

* **Aggregate\_Action\_Counts: 計算每種動作類型發生的總次數**
    ```json
    [
      {
        "$group": {
          "_id": "$actionType",
          "count": { "$sum": 1 }
        }
      },
      {
        "$sort": {
          "count": -1
        }
      }
    ]
    ```

* **Aggregate\_Employee\_Activity\_By\_Date: 計算某一天特定員工 (ID=1001) 的活動總數**
    ```json
    [
      {
        "$match": {
          "empId": 1001,
          "timestamp": {
            "$gte": { "$date": "2025-12-01T00:00:00.000Z" },
            "$lt": { "$date": "2025-12-02T00:00:00.000Z" }
          }
        }
      },
      {
        "$group": {
          "_id": "$actionType",
          "count": { "$sum": 1 }
        }
      }
    ]
    ```