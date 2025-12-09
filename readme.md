## 📦 倉儲管理系統資料庫文件 (Warehouse Management System Database Documentation)

本文件概述了倉儲管理系統所使用的 **關聯式資料庫結構 (SQL)** 及其相關操作查詢範例，以及獨立的 **MongoDB 稽核日誌資料庫** 查詢範例。

---

### 🔗 相關專案連結

| 專案名稱 | 描述 | 連結 |
| :--- | :--- | :--- |
| **系統介面 (Frontend)** | 程式碼位於一個獨立的 GitHub Pages 專案中，包含前端的 HTML、CSS 和 JavaScript 文件。 | [倉儲管理系統介面 HTML 程式碼](https://github.com/jason94530721/Warehouse-management.github.io/tree/main/html) |

---

### 1. 關聯式資料庫結構 (SQL Schema)

這是系統的核心資料模型，管理員工、產品、倉庫及所有進出庫紀錄。

| 表格名稱 | 描述 | 主要欄位 | 關聯/外鍵 |
| :--- | :--- | :--- | :--- |
| **員工** | 負責管理倉庫的人員資訊。 | `員工編號` (PK), `姓名` | |
| **倉庫** | 儲存產品的實體位置。 | `倉庫編號` (PK), `名稱` | `員工編號` (FK) -> 員工 |
| **產品** | 系統中所有可庫存的物品。 | `產品編號` (PK), `名稱` | |
| **庫存** | 追蹤特定產品在特定倉庫中的數量。 | `產品編號` (PK, FK), `倉庫編號` (PK, FK) | `產品編號` (FK) -> 產品, `倉庫編號` (FK) -> 倉庫 |
| **入庫訂單** | 記錄產品進入倉庫的單據。 | `入庫訂單編號` (PK), `收貨日期` | `倉庫編號` (FK) -> 倉庫 |
| **出庫訂單** | 記錄產品離開倉庫的單據。 | `出庫訂單編號` (PK), `出貨日期` | `倉庫編號` (FK) -> 倉庫 |
| **入庫明細** | 記錄單筆入庫訂單中各產品的數量。 | `入庫明細編號` (PK) | `入庫訂單編號` (FK) -> 入庫訂單, `產品編號` (FK) -> 產品 |
| **出庫明細** | 記錄單筆出庫訂單中各產品的數量。 | `出庫明細編號` (PK) | `出庫訂單編號` (FK) -> 出庫訂單, `產品編號` (FK) -> 產品 |

---

### 2. SQL 查詢範例 (Sample Queries)

以下範例涵蓋資料庫的增、刪、改、查 (CRUD) 等基本操作。

#### 🔹 資料查詢 (SELECT)

| 類型 | 描述 | 範例 SQL |
| :--- | :--- | :--- |
| **條件查詢** | 查詢員工編號為 1 的員工所管理的倉庫。 | `SELECT w.* FROM 倉庫 w WHERE w.員工編號 = 1;` |
| **連接查詢** | 查詢員工姓名及其管理的倉庫。 | `SELECT e.姓名, w.* FROM 員工 e JOIN 倉庫 w ON e.員工編號 = w.員工編號;` |
| **分組聚合** | 計算每位員工管理的倉庫數量。 | `SELECT e.姓名, COUNT(w.倉庫編號) FROM 員工 e LEFT JOIN 倉庫 w ON e.員工編號 = w.員工編號 GROUP BY e.姓名;` |
| **庫存統計** | 每個倉庫的總庫存量。 | `SELECT 倉庫編號, SUM(數量) AS 總庫存數量 FROM 庫存 GROUP BY 倉庫編號;` |

#### 📝 資料操作 (DML) 範例

* **新增 (INSERT)：**
    ```sql
    INSERT INTO 員工 (姓名, 聯絡資訊) VALUES (N'新員工', N'0900-111-222');
    ```
* **更新 (UPDATE)：**
    ```sql
    UPDATE 員工 SET 聯絡資訊 = N'0900-999-000' WHERE 員工編號 = 1;
    ```
* **刪除 (DELETE)：**
    ```sql
    DELETE FROM 員工 WHERE 員工編號 = 5;
    ```

---

### 3. MongoDB 稽核日誌查詢

系統的非結構化資料，如操作日誌 (Audit Logs)，儲存在 MongoDB 的 `auditLogs` Collection 中。

#### 🔍 常用查詢

| 查詢名稱 | 目的 | 類型 | 關鍵查詢條件 |
| :--- | :--- | :--- | :--- |
| **Find\_Latest\_100\_Records** | 查找最近發生的 100 筆稽核記錄。 | `find` | 排序: `timestamp: -1`, 限制: `100` |
| **Find\_Stock\_Initialization** | 查找特定倉庫 (ID=1) 的產品初始化記錄。 | `find` | `actionType: "INITIALIZE_STOCK", data.warehouseId: 1` |

#### 📊 聚合 (Aggregate) 範例

* **計算每種動作類型發生的總次數 (Aggregate\_Action\_Counts)**
    ```json
    [
      { "$group": { "_id": "$actionType", "count": { "$sum": 1 } } },
      { "$sort": { "count": -1 } }
    ]
    ```
* **計算某一天特定員工 (ID=1001) 的活動總數**
    ```json
    [
      {
        "$match": {
          "empId": 1001,
          "timestamp": { "$gte": { "$date": "2025-12-01T00:00:00.000Z" } ... }
        }
      },
      { "$group": { "_id": "$actionType", "count": { "$sum": 1 } } }
    ]
    ```