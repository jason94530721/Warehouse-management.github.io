## 📦 簡易倉儲管理系統 (WMS)
[倉儲管理系統介面 HTML 程式碼](https://github.com/jason94530721/Warehouse-management.github.io/tree/main/html)
此專案是一個簡化的倉儲管理系統後端服務，使用 **ASP.NET Core Minimal APIs** 構建，結合 **SQL Server** 處理主要業務數據（如庫存、訂單、明細），並利用 **MongoDB** 記錄所有關鍵的稽核日誌。

### 🛠️ 技術棧

| 類別 | 技術 | 備註 |
| :--- | :--- | :--- |
| **後端** | ASP.NET Core (.NET 8+) | 使用 Minimal APIs 模式 |
| **主要資料庫** | SQL Server | 庫存、訂單等業務資料 |
| **日誌/稽核資料庫** | MongoDB | 專門用於儲存稽核日誌 (Audit Logs) |
| **前端** | HTML, JavaScript | 基礎網頁介面，使用 Tailwind CSS 樣式 |
| **C# 函式庫** | Dapper, MongoDB.Driver | 用於資料庫操作 |

---

### 🚀 專案結構概覽

* **`Program.cs`**: 後端服務的核心檔案，包含 Minimal API 的路由定義、SQL Server 和 MongoDB 的連線配置、資料模型 (Models/Records) 定義，以及稽核日誌的寫入邏輯。
* **`index.html`**: 員工登入頁面。
* **`dashboard.html`**: 員工登入後的倉儲儀表板，顯示倉庫、庫存、入庫/出庫訂單明細等資訊。
* **`audit.html`**: 專門用於顯示 MongoDB 稽核日誌的儀表板。
* **`styles.css`**: 前端頁面的通用樣式檔案。

---

### ⚙️ 環境設定與運行

#### 1. 前置條件

在運行此專案之前，您需要準備以下環境：

* **[.NET 8 SDK](https://dotnet.microsoft.com/download)** 或更高版本
* **SQL Server 實例** (LocalDB, Express, 或 Docker 實例皆可)
* **MongoDB 實例** (本地安裝或 MongoDB Atlas 雲端實例)

#### 2. 資料庫配置

* **`appsettings.json`**: 在此檔案中設定您的資料庫連線字串。

```json
{
  "ConnectionStrings": {
    // 替換為您的 SQL Server 連線字串
    "DefaultConnection": "Server=...;Database=WMS_DB;User Id=...;Password=...;",
    
    // 替換為您的 MongoDB 連線字串 (用於 AuditLogDB)
    "MongoConnection": "mongodb://localhost:27017" 
  }
}
