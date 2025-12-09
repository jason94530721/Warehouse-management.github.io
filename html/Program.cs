// ==============================================================
// 1. 所有 using 語句放在檔案最頂端
// ==============================================================
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json; 
using System.Text.Json.Serialization; 
using Microsoft.AspNetCore.Mvc; 

// MongoDB 相關的 using 語句
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

var builder = WebApplication.CreateBuilder(args);

// 設定 JSON 序列化選項 (保持 PascalCase)
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = null);

// ==============================================================
// 2. MongoDB 服務初始化與註冊
// ==============================================================
var mongoConnStr = builder.Configuration.GetConnectionString("MongoConnection"); 

if (string.IsNullOrWhiteSpace(mongoConnStr))
{
    throw new InvalidOperationException("未找到 'MongoConnection' 連線字串。請在 appsettings.json 中設定正確的 MongoDB 連線字串。");
}

var mongoClient = new MongoClient(mongoConnStr);
var database = mongoClient.GetDatabase("AuditLogDB"); 

builder.Services.AddSingleton<IMongoDatabase>(database);
// ==============================================================

var app = builder.Build(); 
string connStr = builder.Configuration.GetConnectionString("DefaultConnection")!; // SQL 連線字串


// =========================================================================
// ===== 稽核日誌輔助函式
// =========================================================================

// Helper function to handle audit logging asynchronously
async Task LogAudit(IMongoDatabase db, int empId, string actionType, string endpoint, object? data)
{
    // 如果 empId 為 0 (通常是匿名或未提供)，則不記錄
    if (empId == 0) return; 

    try
    {
        var auditLogsCollection = db.GetCollection<AuditLog>("auditLogs");
        var logEntry = new AuditLog
        {
            EmpId = empId,
            ActionType = actionType,
            Endpoint = endpoint,
            Data = data ?? new { message = "No detailed data provided" }
        };
        // 非同步寫入，不阻塞主流程
        await auditLogsCollection.InsertOneAsync(logEntry);
    }
    catch (Exception ex)
    {
        // 記錄日誌失敗不應影響主要業務邏輯
        Console.WriteLine($"Audit logging failed: {ex.Message}");
    }
}
// =========================================================================
// ===== 容量檢查輔助函式
// =========================================================================

// 傳回 (倉庫容量, 目前已佔用尺寸總和)
async Task<(decimal? Capacity, decimal CurrentSize)> GetWarehouseCapacityAndCurrentSizeAsync(
    int warehouseId, 
    SqlConnection conn, 
    SqlTransaction transaction)
{
    // SQL: 取得倉庫容量及計算目前總尺寸
    // 使用 LEFT JOIN 確保即使倉庫沒有庫存，也能取得倉庫容量 (W.儲存空間)
    string sql = @"
        SELECT 
            W.儲存空間,
            -- 計算目前的總尺寸: 庫存數量 * 產品尺寸
            COALESCE(SUM(S.數量 * P.尺寸), 0) AS CurrentTotalSize
        FROM 
            倉庫 W
        LEFT JOIN 
            庫存 S ON W.倉庫編號 = S.倉庫編號
        LEFT JOIN
            產品 P ON S.產品編號 = P.產品編號
        WHERE 
            W.倉庫編號 = @Wid
        GROUP BY 
            W.儲存空間;
    ";
    
    using var cmd = new SqlCommand(sql, conn, transaction);
    cmd.Parameters.AddWithValue("@Wid", warehouseId);

    using var reader = await cmd.ExecuteReaderAsync();
    
    if (!reader.Read())
    {
        reader.Close();
        // 如果找不到倉庫 (應在 API 路由中檢查，此處先返回 null)
        return (null, 0); 
    }
    
    object capacityObj = reader["儲存空間"];
    // 倉庫容量可能為 NULL (代表沒有容量限制)
    decimal? capacity = capacityObj == DBNull.Value ? null : Convert.ToDecimal(capacityObj);
    
    // 目前總尺寸
    decimal currentSize = Convert.ToDecimal(reader["CurrentTotalSize"]);
    
    reader.Close();

    return (capacity, currentSize);
}

// =========================================================================
// ===== 員工與倉庫相關 API
// =========================================================================

// 登入
// 【優化】: 使用自動模型綁定取代 ReadFromJsonAsync
app.MapPost("/api/login", async (LoginRequest req, IMongoDatabase db) =>
{
    if (req == null || string.IsNullOrWhiteSpace(req.EmpId))
        return Results.Problem("請輸入員工編號", statusCode: 400);

    if (!int.TryParse(req.EmpId, out int empId))
        return Results.Problem("員工編號格式錯誤", statusCode: 400);
        
    // 1. 執行 SQL Server 驗證
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = "SELECT 員工編號 AS EmpId, 姓名 FROM 員工 WHERE 員工編號 = @EmpId";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@EmpId", req.EmpId);

    using var reader = await cmd.ExecuteReaderAsync();
    if (!reader.Read())
        return Results.Problem("員工編號不存在", statusCode: 401);

    var empName = reader["姓名"]?.ToString() ?? "未知員工";
    int loggedInEmpId = Convert.ToInt32(reader["EmpId"]);
    reader.Close(); 
    
    // 2. 登入成功：新增 NoSQL 稽核記錄 (MongoDB)
    await LogAudit(db, loggedInEmpId, "LOGIN", "/api/login", new { message = $"員工 {empName} 成功登入系統" });
    
    // 3. 回傳登入成功結果
    return Results.Json(new {
        success = true,
        empId = loggedInEmpId,
        name = empName,
        redirect = $"/dashboard.html?empId={loggedInEmpId}"
    });
});

// 取得員工倉庫
app.MapGet("/api/warehouse/{empId:int}", async (int empId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    
    string sql = "SELECT 倉庫編號 AS id, 名稱 AS name FROM 倉庫 WHERE 員工編號 = @EmpId";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@EmpId", empId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new { id = reader["id"], name = reader["name"] });

    return Results.Json(list);
});

// =========================================================================
// ===== 稽核記錄 API (MongoDB 讀取)
// =========================================================================
app.MapGet("/api/audit/logs", async (IMongoDatabase db, [FromQuery] int limit = 50) =>
{
    try
    {
        var auditLogsCollection = db.GetCollection<AuditLog>("auditLogs");
        
        var logs = await auditLogsCollection.Find(_ => true) 
                                            .SortByDescending(log => log.Timestamp)
                                            .Limit(limit)
                                            .ToListAsync();
        
        return Results.Json(logs);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to retrieve audit logs: {ex.Message}");
        return Results.Problem("無法取得稽核記錄，請檢查 MongoDB 連線或資料庫。", statusCode: 500);
    }
});


// =========================================================================
// ===== 庫存相關 API
// =========================================================================

// 取得庫存
// 【修正】: 新增尺寸、重量、價格欄位至回傳結果
app.MapGet("/api/stock/{warehouseId:int}", async (int warehouseId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"SELECT s.產品編號 AS productId, p.名稱 AS productName, s.數量 AS quantity, 
                    s.最後更新時間 AS lastUpdated, p.尺寸 AS size, p.重量 AS weight, p.價格 AS price
                    FROM 庫存 s
                    JOIN 產品 p ON s.產品編號 = p.產品編號
                    WHERE s.倉庫編號 = @Wid";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Wid", warehouseId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new {
            productId = reader["productId"],
            productName = reader["productName"],
            quantity = reader["quantity"],
            lastUpdated = reader["lastUpdated"],
            size = reader["size"],
            weight = reader["weight"],
            price = reader["price"]
        });

    return Results.Json(list);
});

// 【新增或替換 app.MapPut("/api/stock/{warehouseId:int}/{productId:int}") 的內容】

app.MapPut("/api/stock/{warehouseId:int}/{productId:int}", async (int warehouseId, int productId, HttpContext http, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (empId == 0) return Results.Problem("缺少有效的員工編號 (empId)", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    try
    {
        var req = await http.Request.ReadFromJsonAsync<UpdateStockRequest>();
        if (req == null) return Results.Problem("缺少數量資料", statusCode: 400);
        
        int newQuantity = req.quantity;

        if (newQuantity < 0)
        {
             return Results.Problem("庫存數量不能為負數。", statusCode: 400);
        }

        // 1. 取得舊數量、產品尺寸和產品名稱
        string sqlGetData = @"
            SELECT 
                S.數量 AS OldQuantity, 
                P.尺寸 AS ProductSize,
                P.名稱 AS ProductName
            FROM 
                庫存 S
            JOIN 
                產品 P ON S.產品編號 = P.產品編號
            WHERE 
                S.倉庫編號 = @Wid AND S.產品編號 = @Pid";

        using var cmdGetData = new SqlCommand(sqlGetData, conn, transaction);
        cmdGetData.Parameters.AddWithValue("@Wid", warehouseId);
        cmdGetData.Parameters.AddWithValue("@Pid", productId);

        using var reader = await cmdGetData.ExecuteReaderAsync();
        if (!reader.Read())
        {
            reader.Close();
            transaction.Rollback();
            return Results.NotFound("庫存記錄或產品不存在。");
        }
        
        int oldQuantity = Convert.ToInt32(reader["OldQuantity"]);
        object sizeObj = reader["ProductSize"];
        int productSize = sizeObj == DBNull.Value ? 0 : Convert.ToInt32(sizeObj);
        string productName = reader["ProductName"].ToString()!;
        reader.Close();
        
        // ----------------------------------------------------
        // **容量檢查邏輯 - START**
        // ----------------------------------------------------

        // 只有在產品有設定尺寸 (尺寸 > 0) 且數量有變動時，才需要檢查
        if (productSize > 0 && newQuantity != oldQuantity)
        {
            // 取得倉庫總容量和目前總尺寸 (CurrentTotalSize 已包含舊的這筆庫存貢獻)
            var (capacity, currentTotalSize) = await GetWarehouseCapacityAndCurrentSizeAsync(warehouseId, conn, transaction);
            
            // 計算舊庫存的尺寸貢獻 (需要從總尺寸中排除)
            decimal oldContribution = (decimal)oldQuantity * productSize;
            
            // 計算新的庫存的尺寸貢獻
            decimal newContribution = (decimal)newQuantity * productSize;
            
            // 計算排除舊貢獻後的總尺寸 (即倉庫其他貨品的總尺寸)
            decimal sizeExcludingProduct = currentTotalSize - oldContribution;
            
            // 計算預計的新總尺寸 (其他貨品 + 新庫存貢獻)
            decimal projectedTotalSize = sizeExcludingProduct + newContribution;

            // 檢查是否超載
            if (capacity.HasValue && projectedTotalSize > capacity.Value)
            {
                transaction.Rollback();
                return Results.Problem(
                    $"容量檢查失敗: 更新產品『{productName}』數量從 {oldQuantity} 變更為 {newQuantity}，將導致總尺寸達到 {projectedTotalSize}，超過倉庫容量 {capacity.Value}。", 
                    statusCode: 400
                );
            }
        }
        
        // ----------------------------------------------------
        // **容量檢查邏輯 - END**
        // ----------------------------------------------------
        
        // 2. 執行更新
        string sqlUpdate = "UPDATE 庫存 SET 數量 = @NewQty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdUpdate = new SqlCommand(sqlUpdate, conn, transaction);
        cmdUpdate.Parameters.AddWithValue("@NewQty", newQuantity);
        cmdUpdate.Parameters.AddWithValue("@Wid", warehouseId);
        cmdUpdate.Parameters.AddWithValue("@Pid", productId);
        await cmdUpdate.ExecuteNonQueryAsync();

        // 3. 稽核記錄與 Commit
        await LogAudit(db, empId, "UPDATE_STOCK_QUANTITY", http.Request.Path, new { warehouseId, productId, oldQuantity, newQuantity });
        transaction.Commit();

        return Results.Json(new { success = true, message = $"庫存數量已更新為 {newQuantity}" });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        Console.WriteLine($"Error during stock update: {ex.Message}");
        return Results.Problem($"更新庫存數量失敗: {ex.Message}", statusCode: 500);
    }
});

// 【請找到並替換這個區塊】
app.MapPost("/api/stock/initialize/{warehouseId:int}", async (int warehouseId, IMongoDatabase db, HttpContext http) => // <--- 修正路徑與新增 warehouseId 參數
{
    // ⚠ 注意：此處已將 warehouseId 從 URL 取得，因此 req.warehouseId 欄位不再使用。
    // InitializeStockRequest 的 Record 定義也需修正 (見下方第 2 點)。
    
    var req = await http.Request.ReadFromJsonAsync<InitializeStockRequest>(); 
    if (req == null || string.IsNullOrWhiteSpace(req.productName))
        return Results.Problem("缺少產品名稱、倉庫 ID 或數量", statusCode: 400);

    // 取得 empId (來自 query string)
    if (!http.Request.Query.TryGetValue("empId", out var empIdStr) || !int.TryParse(empIdStr, out int empId))
        return Results.Problem("缺少有效的員工編號 (empId)", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    int productId = 0;
    int productSize = req.size ?? 0; // 暫定尺寸，如果 req.size 有值，則用於新產品或更新舊產品

    try
    {
        // 1. 檢查產品資料表 (Products) 中是否存在該產品
        string sqlSelectProduct = "SELECT 產品編號, 尺寸 FROM 產品 WHERE 名稱 = @ProductName"; 
        using var cmdSelect = new SqlCommand(sqlSelectProduct, conn, transaction);
        cmdSelect.Parameters.AddWithValue("@ProductName", req.productName);

        using var reader = await cmdSelect.ExecuteReaderAsync();
        
        if (reader.Read())
        {
            // 產品已存在
            productId = Convert.ToInt32(reader["產品編號"]);
            object dbSizeObj = reader["尺寸"];
            int dbProductSize = dbSizeObj == DBNull.Value ? 0 : Convert.ToInt32(dbSizeObj);
            reader.Close();
            
            // 如果請求中帶有 size，則表示要更新產品尺寸，以 req.size 為準
            if (req.size.HasValue) {
                 productSize = req.size.Value;
                 // 更新產品表中的尺寸
                 string sqlUpdateProductSize = "UPDATE 產品 SET 尺寸 = @Size WHERE 產品編號 = @Pid";
                 using var cmdUpdateSize = new SqlCommand(sqlUpdateProductSize, conn, transaction);
                 cmdUpdateSize.Parameters.AddWithValue("@Size", req.size.Value);
                 cmdUpdateSize.Parameters.AddWithValue("@Pid", productId);
                 await cmdUpdateSize.ExecuteNonQueryAsync();
            } else {
                 productSize = dbProductSize; // 否則沿用 DB 中的尺寸
            }
        }
        else
        {
            reader.Close();
            // 產品不存在，新增一筆產品記錄，同時寫入尺寸、重量、價格
            string sqlInsertProduct = @"
                INSERT INTO 產品 (名稱, 尺寸, 重量, 價格) 
                OUTPUT INSERTED.產品編號 
                VALUES (@ProductName, @Size, @Weight, @Price)";
                
            using var cmdInsert = new SqlCommand(sqlInsertProduct, conn, transaction);
            cmdInsert.Parameters.AddWithValue("@ProductName", req.productName);
            cmdInsert.Parameters.AddWithValue("@Size", (object?)req.size ?? DBNull.Value);
            cmdInsert.Parameters.AddWithValue("@Weight", (object?)req.weight ?? DBNull.Value);
            cmdInsert.Parameters.AddWithValue("@Price", (object?)req.price ?? DBNull.Value);

            var newId = await cmdInsert.ExecuteScalarAsync();
            if (newId == null) throw new Exception("新增產品失敗，無法取得產品編號。");
            productId = Convert.ToInt32(newId);
        }
        
        // ----------------------------------------------------
        // **容量檢查邏輯**
        // ----------------------------------------------------

        // 1. 取得現有總尺寸和倉庫容量
        var (capacity, currentSize) = await GetWarehouseCapacityAndCurrentSizeAsync(warehouseId, conn, transaction); // <--- 使用從 URL 取得的 warehouseId
        
        // 2. 計算新增數量對容量的影響
        decimal projectedIncrease = (decimal)req.quantity * productSize;
        decimal newTotalSize = currentSize + projectedIncrease;
        
        // 3. 檢查是否超載
        if (capacity.HasValue && newTotalSize > capacity.Value)
        {
            transaction.Rollback();
            return Results.Problem($"容量檢查失敗: 產品『{req.productName}』預計新增 {req.quantity} 個 (單個尺寸: {productSize})，總尺寸將從 {currentSize} 增加到 {newTotalSize}，超過倉庫容量 {capacity.Value}。", statusCode: 400);
        }
        
        // ----------------------------------------------------
        // **容量檢查邏輯 - END**
        // ----------------------------------------------------


        // 2. 庫存處理 (保持不變，使用從 URL 取得的 warehouseId)
        string sqlCheckStock = "SELECT COUNT(*) FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdCheckStock = new SqlCommand(sqlCheckStock, conn, transaction);
        cmdCheckStock.Parameters.AddWithValue("@Wid", warehouseId); // <--- 使用從 URL 取得的 warehouseId
        cmdCheckStock.Parameters.AddWithValue("@Pid", productId);
        
        int count = Convert.ToInt32(await cmdCheckStock.ExecuteScalarAsync());

        if (count > 0)
        {
            // 庫存已存在，更新數量
            string sqlUpdateStock = "UPDATE 庫存 SET 數量 = 數量 + @Qty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
            using var cmdUpdateStock = new SqlCommand(sqlUpdateStock, conn, transaction);
            cmdUpdateStock.Parameters.AddWithValue("@Qty", req.quantity);
            cmdUpdateStock.Parameters.AddWithValue("@Wid", warehouseId); // <--- 使用從 URL 取得的 warehouseId
            cmdUpdateStock.Parameters.AddWithValue("@Pid", productId);
            await cmdUpdateStock.ExecuteNonQueryAsync();
        }
        else
        {
            // 庫存不存在，新增一筆庫存記錄
            string sqlInsertStock = "INSERT INTO 庫存 (倉庫編號, 產品編號, 數量, 最後更新時間) VALUES (@Wid, @Pid, @Qty, GETDATE())";
            using var cmdInsertStock = new SqlCommand(sqlInsertStock, conn, transaction);
            cmdInsertStock.Parameters.AddWithValue("@Wid", warehouseId); // <--- 使用從 URL 取得的 warehouseId
            cmdInsertStock.Parameters.AddWithValue("@Pid", productId);
            cmdInsertStock.Parameters.AddWithValue("@Qty", req.quantity);
            await cmdInsertStock.ExecuteNonQueryAsync();
        }

        // 3. 稽核記錄與 Commit
        await LogAudit(db, empId, "INITIALIZE_STOCK", http.Request.Path, new 
    { 
        warehouseId, 
        productId, 
        productName = req.productName, 
        quantity = req.quantity,
        size = req.size,
        weight = req.weight.HasValue ? (double?)req.weight.Value : null,
        price = req.price.HasValue ? (double?)req.price.Value : null
    });
        transaction.Commit();
        return Results.Json(new { success = true, productId = productId, message = "產品及庫存初始化成功" });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        Console.WriteLine($"Error during stock initialization: {ex.Message}");
        return Results.Problem($"初始化產品/庫存失敗: {ex.Message}", statusCode: 500);
    }
});

// 刪除庫存中的產品 (DELETE)
app.MapDelete("/api/stock/{warehouseId:int}/{productId:int}", async (int warehouseId, int productId, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    try
    {
        // 1. 檢查數量是否為 0
        string sqlCheckQty = "SELECT 數量 FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdCheckQty = new SqlCommand(sqlCheckQty, conn, transaction);
        cmdCheckQty.Parameters.AddWithValue("@Wid", warehouseId);
        cmdCheckQty.Parameters.AddWithValue("@Pid", productId);
        
        var currentQtyObj = await cmdCheckQty.ExecuteScalarAsync();
        
        if (currentQtyObj == null) 
        {
            transaction.Rollback();
            return Results.NotFound(new { message = $"產品編號 {productId} 不存在於倉庫 {warehouseId} 的庫存中。" });
        }

        int currentQty = Convert.ToInt32(currentQtyObj);
        if (currentQty != 0)
        {
            transaction.Rollback();
            return Results.Problem($"產品編號 {productId} 仍有庫存數量 {currentQty}，請先將數量調整為 0 才能刪除。", statusCode: 400);
        }

        // 2. 刪除庫存記錄
        string sqlDeleteStock = "DELETE FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdDeleteStock = new SqlCommand(sqlDeleteStock, conn, transaction);
        cmdDeleteStock.Parameters.AddWithValue("@Wid", warehouseId);
        cmdDeleteStock.Parameters.AddWithValue("@Pid", productId);
        await cmdDeleteStock.ExecuteNonQueryAsync();
        
        transaction.Commit();

        await LogAudit(db, empId, "STOCK_DELETE", $"/api/stock/{warehouseId}/{productId}", new { 
            warehouseId, 
            productId, 
        });

        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.Problem($"刪除庫存失敗: {ex.Message}", statusCode: 500);
    }
});


// =========================================================================
// ===== 入庫訂單相關 API
// =========================================================================

// 取得入庫訂單
app.MapGet("/api/inbound/{warehouseId:int}", async (int warehouseId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"SELECT i.入庫訂單編號 AS inboundId, i.供應商名稱 AS supplier, 
                    CONVERT(VARCHAR, i.收貨日期, 23) AS receivedDate /* 格式化為 YYYY-MM-DD */
                    FROM 入庫訂單 i
                    WHERE i.倉庫編號 = @Wid";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Wid", warehouseId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new {
            inboundId = reader["inboundId"],
            supplier = reader["supplier"],
            receivedDate = reader["receivedDate"]
        });

    return Results.Json(list);
});

// 更新入庫訂單 (PUT /api/inbound/{inboundId})
// 【優化】: 使用自動模型綁定取代 ReadFromJsonAsync
app.MapPut("/api/inbound/{inboundId:int}", async (int inboundId, UpdateInboundOrderRequest req, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (req == null) return Results.Problem("缺少資料", statusCode: 400);

    using var conn = new SqlConnection(connStr);    
    await conn.OpenAsync();

    string sql = "UPDATE 入庫訂單 SET 供應商名稱=@Supplier, 收貨日期=@ReceivedDate WHERE 入庫訂單編號=@Id";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Supplier", req.supplier);
    cmd.Parameters.AddWithValue("@ReceivedDate", req.receivedDate.Date); 
    cmd.Parameters.AddWithValue("@Id", inboundId);

    int affected = await cmd.ExecuteNonQueryAsync();
    
    if (affected > 0)
    {
        await LogAudit(db, empId, "INBOUND_ORDER_UPDATE", $"/api/inbound/{inboundId}", new { 
            inboundId, 
            newSupplier = req.supplier, 
            newDate = req.receivedDate 
        });
    }

    if (affected == 0) return Results.NotFound(new { message = $"入庫訂單 ID {inboundId} 不存在" });
    
    return Results.Json(new { success = true });
});

// Program.cs - 完整的入庫訂單處理 (FullInboundRequest) 修正區塊
app.MapPost("/api/inbound/full/{warehouseId:int}", async (int warehouseId, HttpContext http, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    var req = await http.Request.ReadFromJsonAsync<FullInboundRequest>();
    if (req == null || req.details == null || !req.details.Any()) return Results.Problem("缺少訂單或明細資料", statusCode: 400);

    // 檢查 empId
    if (empId == 0) return Results.Problem("缺少有效的員工編號 (empId)", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction(); 

    try
    {
        // ----------------------------------------------------
        // **容量檢查邏輯 - START** (此處邏輯保持不變)
        // ----------------------------------------------------

        // 1. **計算所有入庫項目的總尺寸增量**
        decimal totalInboundSizeIncrease = 0;
        var productIds = req.details.Select(d => d.productId).Distinct().ToList();
        
        if (productIds.Any())
        {
            var productSizes = new Dictionary<int, int>();
            
            // 透過 WHERE IN 一次查出所有產品尺寸
            string sqlGetSizes = $@"
                SELECT 產品編號, 尺寸 
                FROM 產品 
                WHERE 產品編號 IN ({string.Join(",", productIds)})"; 
                
            using (var cmdGetSizes = new SqlCommand(sqlGetSizes, conn, transaction))
            {
                using var reader = await cmdGetSizes.ExecuteReaderAsync();
                while (reader.Read())
                {
                    object dbSizeObj = reader["尺寸"];
                    int productSize = dbSizeObj == DBNull.Value ? 0 : Convert.ToInt32(dbSizeObj);
                    productSizes.Add(Convert.ToInt32(reader["產品編號"]), productSize);
                }
            }
            
            // 計算總增量
            foreach (var detail in req.details)
            {
                if (detail.quantity < 0) throw new Exception("入庫數量不能為負數。"); 

                if (productSizes.TryGetValue(detail.productId, out int productSize))
                {
                    totalInboundSizeIncrease += (decimal)(detail.quantity * productSize);
                }
            }
        }
        
        // 2. **取得目前容量並檢查是否超載**
        var (capacity, currentSize) = await GetWarehouseCapacityAndCurrentSizeAsync(warehouseId, conn, transaction);
        
        decimal newTotalSize = currentSize + totalInboundSizeIncrease;

        if (capacity.HasValue && newTotalSize > capacity.Value)
        {
            transaction.Rollback();
            return Results.Problem($"容量檢查失敗: 本次入庫總尺寸增量為 {totalInboundSizeIncrease}，將導致總尺寸從 {currentSize} 增加到 {newTotalSize}，超過倉庫容量 {capacity.Value}。", statusCode: 400);
        }

        // ----------------------------------------------------
        // **容量檢查邏輯 - END**
        // ----------------------------------------------------

        // 1. 建立入庫訂單
        string sqlOrder = "INSERT INTO 入庫訂單 (倉庫編號, 供應商名稱, 收貨日期) VALUES (@Wid, @Supplier, @ReceivedDate); SELECT SCOPE_IDENTITY();";
        using var cmdOrder = new SqlCommand(sqlOrder, conn, transaction);
        cmdOrder.Parameters.AddWithValue("@Wid", warehouseId);
        
        // ⭐⭐⭐ 修正處：補上遺失的參數綁定 ⭐⭐⭐
        cmdOrder.Parameters.AddWithValue("@Supplier", req.supplier);
        cmdOrder.Parameters.AddWithValue("@ReceivedDate", req.receivedDate);
        // ⭐⭐⭐ 修正結束 ⭐⭐⭐
        
        int inboundId = Convert.ToInt32(await cmdOrder.ExecuteScalarAsync());

        // 2. 迭代明細，建立明細並更新庫存
        string sqlDetail = "INSERT INTO 入庫明細 (入庫訂單編號, 產品編號, 數量) VALUES (@Iid, @Pid, @Qty)";
        string sqlUpdateStock = "IF EXISTS (SELECT 1 FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid) " +
                                "    UPDATE 庫存 SET 數量 = 數量 + @Qty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid " +
                                "ELSE " +
                                "    INSERT INTO 庫存 (倉庫編號, 產品編號, 數量, 最後更新時間) VALUES (@Wid, @Pid, @Qty, GETDATE());";
        
        foreach (var detail in req.details)
        {
            // 寫入明細
            using (var cmdDetail = new SqlCommand(sqlDetail, conn, transaction))
            {
                cmdDetail.Parameters.AddWithValue("@Iid", inboundId);
                cmdDetail.Parameters.AddWithValue("@Pid", detail.productId);
                cmdDetail.Parameters.AddWithValue("@Qty", detail.quantity);
                await cmdDetail.ExecuteNonQueryAsync();
            }

            // 更新庫存
            using (var cmdUpdateStock = new SqlCommand(sqlUpdateStock, conn, transaction))
            {
                cmdUpdateStock.Parameters.AddWithValue("@Wid", warehouseId);
                cmdUpdateStock.Parameters.AddWithValue("@Pid", detail.productId);
                cmdUpdateStock.Parameters.AddWithValue("@Qty", detail.quantity);
                await cmdUpdateStock.ExecuteNonQueryAsync();
            }
        }
        
        // 3. 交易提交
        transaction.Commit(); 

        // 4. 稽核記錄 (NoSQL)
        await LogAudit(db, empId, "INBOUND_ORDER", http.Request.Path, new { 
            inboundId, 
            warehouseId, 
            supplier = req.supplier,
            receivedDate = req.receivedDate,
            details = req.details 
        });

        return Results.Json(new { success = true, inboundId = inboundId });
    }
    catch (Exception ex)
    {
        transaction.Rollback(); 
        Console.WriteLine($"Error during full inbound order: {ex.Message}");
        return Results.Problem($"建立入庫訂單失敗: {ex.Message}", statusCode: 500);
    }
});
// 取得入庫明細
app.MapGet("/api/inbound/detail/{inboundId:int}", async (int inboundId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"SELECT d.入庫明細編號 AS detailId, d.產品編號 AS productId, p.名稱 AS productName, d.數量 AS quantity
                    FROM 入庫明細 d
                    JOIN 產品 p ON d.產品編號 = p.產品編號
                    WHERE d.入庫訂單編號 = @Id";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Id", inboundId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new {
            detailId = reader["detailId"],
            productId = reader["productId"],
            productName = reader["productName"],
            quantity = reader["quantity"]
        });

    return Results.Json(list);
});


// 更新入庫明細數量 (PUT /api/inbound/detail/{detailId})
app.MapPut("/api/inbound/detail/{detailId:int}", async (int detailId, UpdateInboundDetailFieldsRequest req, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (req == null) return Results.Problem("缺少產品編號或數量", statusCode: 400);

    int newQuantity = req.quantity;
    if (newQuantity < 0) return Results.Problem("入庫明細數量不能為負數。", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    try
    {
        // 1. 取得舊明細的資訊 (oldProductId, oldQuantity, warehouseId)
        string sqlSelectOld = @"SELECT d.產品編號 AS oldProductId, d.數量 AS oldQuantity, o.倉庫編號 AS warehouseId 
                                 FROM 入庫明細 d JOIN 入庫訂單 o ON d.入庫訂單編號 = o.入庫訂單編號 WHERE d.入庫明細編號 = @Did";
        using var cmdSelectOld = new SqlCommand(sqlSelectOld, conn, transaction);
        cmdSelectOld.Parameters.AddWithValue("@Did", detailId);

        using var reader = await cmdSelectOld.ExecuteReaderAsync();
        if (!reader.Read())
        {
            transaction.Rollback();
            return Results.NotFound(new { message = $"入庫明細 ID {detailId} 不存在" });
        }
        int oldProductId = Convert.ToInt32(reader["oldProductId"]);
        int oldQuantity = Convert.ToInt32(reader["oldQuantity"]);
        int warehouseId = Convert.ToInt32(reader["warehouseId"]);
        reader.Close();
        
        int newProductId = req.productId;

        // 檢查新產品是否存在於庫存
        string sqlCheckStock = "SELECT COUNT(1) FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdCheckStock = new SqlCommand(sqlCheckStock, conn, transaction);
        cmdCheckStock.Parameters.AddWithValue("@Wid", warehouseId);
        cmdCheckStock.Parameters.AddWithValue("@Pid", newProductId);
        if (Convert.ToInt32(await cmdCheckStock.ExecuteScalarAsync()) == 0)
        {
            throw new Exception($"新產品編號 {newProductId} 不存在於倉庫 {warehouseId} 的庫存中，無法更新。");
        }
        
        // 2. 庫存調整 - 撤銷舊的變更
        if (oldQuantity > 0)
        {
            string sqlCheckRevertStock = "SELECT 數量 FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @OldPid";
            using var cmdCheckRevertStock = new SqlCommand(sqlCheckRevertStock, conn, transaction);
            cmdCheckRevertStock.Parameters.AddWithValue("@Wid", warehouseId);
            cmdCheckRevertStock.Parameters.AddWithValue("@OldPid", oldProductId);
            var currentStockObj = await cmdCheckRevertStock.ExecuteScalarAsync();
            int currentStock = currentStockObj == null ? 0 : Convert.ToInt32(currentStockObj);

            if (currentStock < oldQuantity) 
            {
                 transaction.Rollback();
                 return Results.Problem($"無法更新明細。回溯舊數量 {oldQuantity} 將導致產品 {oldProductId} 庫存變為負數。當前庫存: {currentStock}", statusCode: 400);
            }
            
            string sqlStockRevert = "UPDATE 庫存 SET 數量 = 數量 - @OldQty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @OldPid";
            using var cmdStockRevert = new SqlCommand(sqlStockRevert, conn, transaction);
            cmdStockRevert.Parameters.AddWithValue("@OldQty", oldQuantity);
            cmdStockRevert.Parameters.AddWithValue("@Wid", warehouseId);
            cmdStockRevert.Parameters.AddWithValue("@OldPid", oldProductId);
            await cmdStockRevert.ExecuteNonQueryAsync();
        }

        // 3. 庫存調整 - 應用新的變更
        if (newQuantity > 0)
        {
            string sqlStockApply = "UPDATE 庫存 SET 數量 = 數量 + @NewQty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @NewPid";
            using var cmdStockApply = new SqlCommand(sqlStockApply, conn, transaction);
            cmdStockApply.Parameters.AddWithValue("@NewQty", newQuantity);
            cmdStockApply.Parameters.AddWithValue("@Wid", warehouseId);
            cmdStockApply.Parameters.AddWithValue("@NewPid", newProductId);
            await cmdStockApply.ExecuteNonQueryAsync();
        }
        
        // 4. 更新明細記錄
        string sqlUpdateDetail = "UPDATE 入庫明細 SET 產品編號=@NewPid, 數量=@NewQty WHERE 入庫明細編號=@Did";
        using var cmdUpdateDetail = new SqlCommand(sqlUpdateDetail, conn, transaction);
        cmdUpdateDetail.Parameters.AddWithValue("@NewPid", newProductId);
        cmdUpdateDetail.Parameters.AddWithValue("@NewQty", newQuantity);
        cmdUpdateDetail.Parameters.AddWithValue("@Did", detailId);
        await cmdUpdateDetail.ExecuteNonQueryAsync();

        transaction.Commit();

        await LogAudit(db, empId, "INBOUND_DETAIL_UPDATE", $"/api/inbound/detail/{detailId}", new { 
            detailId, 
            warehouseId, 
            oldProductId,
            newProductId,
            oldQuantity,
            newQuantity
        });
        
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.Problem($"更新入庫明細失敗: {ex.Message}", statusCode: 500);
    }
});


// 刪除入庫明細並回溯庫存 (DELETE /api/inbound/detail/{detailId}) 
app.MapDelete("/api/inbound/detail/{detailId:int}", async (int detailId, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    try
    {
        // 1. 取得明細的資訊
        string sqlSelect = @"SELECT d.產品編號, d.數量, o.倉庫編號, d.入庫訂單編號 
                             FROM 入庫明細 d JOIN 入庫訂單 o ON d.入庫訂單編號 = o.入庫訂單編號 WHERE d.入庫明細編號 = @Did";
        using var cmdSelect = new SqlCommand(sqlSelect, conn, transaction);
        cmdSelect.Parameters.AddWithValue("@Did", detailId);

        using var reader = await cmdSelect.ExecuteReaderAsync();
        if (!reader.Read()) { transaction.Rollback(); return Results.NotFound(new { message = $"入庫明細 ID {detailId} 不存在" }); }

        int productId = Convert.ToInt32(reader["產品編號"]);
        int quantity = Convert.ToInt32(reader["數量"]);
        int warehouseId = Convert.ToInt32(reader["倉庫編號"]);
        int inboundId = Convert.ToInt32(reader["入庫訂單編號"]); 
        reader.Close(); 
        
        // 庫存回溯安全檢查
        string sqlCheckCurrentStock = "SELECT 數量 FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdCheckCurrentStock = new SqlCommand(sqlCheckCurrentStock, conn, transaction);
        cmdCheckCurrentStock.Parameters.AddWithValue("@Wid", warehouseId);
        cmdCheckCurrentStock.Parameters.AddWithValue("@Pid", productId);
        var currentStockObj = await cmdCheckCurrentStock.ExecuteScalarAsync();
        int currentStock = currentStockObj == null ? 0 : Convert.ToInt32(currentStockObj);

        if (currentStock < quantity)
        {
            transaction.Rollback();
            return Results.Problem($"無法刪除明細。回溯數量 {quantity} 將導致庫存變為負數。當前庫存: {currentStock}", statusCode: 400);
        }

        // 2. 刪除明細
        string sqlDeleteDetail = "DELETE FROM 入庫明細 WHERE 入庫明細編號 = @Did";
        using var cmdDeleteDetail = new SqlCommand(sqlDeleteDetail, conn, transaction);
        cmdDeleteDetail.Parameters.AddWithValue("@Did", detailId);
        await cmdDeleteDetail.ExecuteNonQueryAsync();

        // 3. 回溯庫存 (減去數量)
        string sqlStockUpdate = "UPDATE 庫存 SET 數量 = 數量 - @Qty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdStockUpdate = new SqlCommand(sqlStockUpdate, conn, transaction);
        cmdStockUpdate.Parameters.AddWithValue("@Qty", quantity);
        cmdStockUpdate.Parameters.AddWithValue("@Wid", warehouseId);
        cmdStockUpdate.Parameters.AddWithValue("@Pid", productId);
        await cmdStockUpdate.ExecuteNonQueryAsync();

        // 4. 檢查是否還有其他明細，若無則刪除訂單
        string sqlCheckDetails = "SELECT COUNT(1) FROM 入庫明細 WHERE 入庫訂單編號 = @IId";
        using var cmdCheckDetails = new SqlCommand(sqlCheckDetails, conn, transaction);
        cmdCheckDetails.Parameters.AddWithValue("@IId", inboundId);
        int remainingDetails = Convert.ToInt32(await cmdCheckDetails.ExecuteScalarAsync());

        bool orderDeleted = false;
        if (remainingDetails == 0)
        {
            string sqlDeleteOrder = "DELETE FROM 入庫訂單 WHERE 入庫訂單編號 = @IId";
            using var cmdDeleteOrder = new SqlCommand(sqlDeleteOrder, conn, transaction);
            cmdDeleteOrder.Parameters.AddWithValue("@IId", inboundId);
            await cmdDeleteOrder.ExecuteNonQueryAsync();
            orderDeleted = true;
        }

        transaction.Commit();

        await LogAudit(db, empId, "INBOUND_DETAIL_DELETE", $"/api/inbound/detail/{detailId}", new { 
            detailId, 
            inboundId,
            warehouseId,
            productId, 
            quantityReverted = quantity, 
            orderDeleted 
        });

        return Results.Json(new { success = true, inboundId = orderDeleted ? inboundId : (int?)null });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.Problem($"刪除入庫明細及回溯庫存失敗: {ex.Message}", statusCode: 500);
    }
});


// =========================================================================
// ===== 出庫訂單相關 API 
// =========================================================================

// 取得出庫訂單
app.MapGet("/api/outbound/{warehouseId:int}", async (int warehouseId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"SELECT o.出庫訂單編號 AS outboundId, 
                    CONVERT(VARCHAR, o.出貨日期, 23) AS shippedDate, /* 格式化為 YYYY-MM-DD */
                    o.送達地址 AS address
                    FROM 出庫訂單 o
                    WHERE o.倉庫編號 = @Wid";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Wid", warehouseId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new {
            outboundId = reader["outboundId"],
            shippedDate = reader["shippedDate"],
            address = reader["address"]
        });

    return Results.Json(list);
});

// 新增出庫訂單、明細及更新庫存 (POST /api/outbound/full/{warehouseId})
// 【優化】: 使用自動模型綁定取代 ReadFromJsonAsync
app.MapPost("/api/outbound/full/{warehouseId:int}", async (int warehouseId, FullOutboundRequest req, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (req == null || req.details == null || !req.details.Any()) return Results.Problem("缺少訂單或明細資料", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction(); 

    try
    {
        // 1. 建立出庫訂單
        string sqlOrder = "INSERT INTO 出庫訂單 (倉庫編號, 出貨日期, 送達地址) VALUES (@Wid, @ShippedDate, @Address); SELECT SCOPE_IDENTITY();";
        using var cmdOrder = new SqlCommand(sqlOrder, conn, transaction);
        cmdOrder.Parameters.AddWithValue("@Wid", warehouseId);
        cmdOrder.Parameters.AddWithValue("@ShippedDate", req.shippedDate);
        cmdOrder.Parameters.AddWithValue("@Address", req.address);
        int outboundId = Convert.ToInt32(await cmdOrder.ExecuteScalarAsync());

        // 2. 迭代明細，建立明細並更新庫存 (減去數量)
        foreach (var detail in req.details)
        {
            if (detail.quantity <= 0) 
            {
                 throw new Exception($"產品編號 {detail.productId} 的出庫數量必須大於 0。");
            }

            // 2a. 檢查庫存數量是否足夠
            string sqlCheckStock = "SELECT 數量 FROM 庫存 WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
            using var cmdCheckStock = new SqlCommand(sqlCheckStock, conn, transaction);
            cmdCheckStock.Parameters.AddWithValue("@Wid", warehouseId);
            cmdCheckStock.Parameters.AddWithValue("@Pid", detail.productId);
            
            var currentQtyObj = await cmdCheckStock.ExecuteScalarAsync();
            if (currentQtyObj == null)
            {
                throw new Exception($"產品編號 {detail.productId} 不存在於倉庫 {warehouseId} 的庫存中，無法出庫。");
            }
            
            int currentQty = Convert.ToInt32(currentQtyObj);
            if (currentQty < detail.quantity)
            {
                throw new Exception($"產品編號 {detail.productId} 庫存不足。目前庫存: {currentQty}, 請求出庫: {detail.quantity}。");
            }


            // 2b. 建立出庫明細
            string sqlDetail = "INSERT INTO 出庫明細 (出庫訂單編號, 產品編號, 數量) VALUES (@OutboundId, @Pid, @Qty)";
            using var cmdDetail = new SqlCommand(sqlDetail, conn, transaction);
            cmdDetail.Parameters.AddWithValue("@OutboundId", outboundId);
            cmdDetail.Parameters.AddWithValue("@Pid", detail.productId);
            cmdDetail.Parameters.AddWithValue("@Qty", detail.quantity);
            await cmdDetail.ExecuteNonQueryAsync();

            // 2c. 更新庫存 (減去數量)
            string sqlStockUpdate = "UPDATE 庫存 SET 數量 = 數量 - @Qty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
            using var cmdStockUpdate = new SqlCommand(sqlStockUpdate, conn, transaction);
            cmdStockUpdate.Parameters.AddWithValue("@Qty", detail.quantity);
            cmdStockUpdate.Parameters.AddWithValue("@Wid", warehouseId);
            cmdStockUpdate.Parameters.AddWithValue("@Pid", detail.productId);
            await cmdStockUpdate.ExecuteNonQueryAsync();
        }

        transaction.Commit(); 
        
        await LogAudit(db, empId, "OUTBOUND_ORDER_CREATE", $"/api/outbound/full/{warehouseId}", new { 
            outboundId, 
            warehouseId, 
            address = req.address, 
            itemCount = req.details.Count 
        });

        return Results.Json(new { success = true, outboundId = outboundId });
    }
    catch (Exception ex)
    {
        transaction.Rollback(); 
        return Results.Problem($"建立出庫訂單失敗: {ex.Message}", statusCode: 500);
    }
});


// 取得出庫明細
app.MapGet("/api/outbound/detail/{outboundId:int}", async (int outboundId) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = @"SELECT d.出庫明細編號 AS detailId, d.產品編號 AS productId, p.名稱 AS productName, d.數量 AS quantity
                    FROM 出庫明細 d
                    JOIN 產品 p ON d.產品編號 = p.產品編號
                    WHERE d.出庫訂單編號 = @Id";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Id", outboundId);

    var list = new List<object>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (reader.Read())
        list.Add(new {
            detailId = reader["detailId"],
            productId = reader["productId"],
            productName = reader["productName"],
            quantity = reader["quantity"]
        });

    return Results.Json(list);
});

// 更新出庫訂單 (PUT /api/outbound/{outboundId})
// 【優化】: 使用自動模型綁定取代 ReadFromJsonAsync
app.MapPut("/api/outbound/{outboundId:int}", async (int outboundId, OutboundRequest req, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (req == null) return Results.Problem("缺少資料", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    string sql = "UPDATE 出庫訂單 SET 出貨日期=@ShippedDate, 送達地址=@Address WHERE 出庫訂單編號=@Id";
    using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@ShippedDate", req.shippedDate.Date); 
    cmd.Parameters.AddWithValue("@Address", req.address);
    cmd.Parameters.AddWithValue("@Id", outboundId);

    int affected = await cmd.ExecuteNonQueryAsync();
    
    if (affected > 0)
    {
        await LogAudit(db, empId, "OUTBOUND_ORDER_UPDATE", $"/api/outbound/{outboundId}", new { 
            outboundId, 
            newDate = req.shippedDate, 
            newAddress = req.address 
        });
    }

    if (affected == 0) return Results.NotFound(new { message = $"出庫訂單 ID {outboundId} 不存在" });
    
    return Results.Json(new { success = true });
});


// 更新出庫明細數量 (此路由已連動更新庫存)
app.MapPut("/api/outbound/detail/{detailId:int}", async (int detailId, UpdateStockRequest req, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    if (req == null) return Results.Problem("缺少數量", statusCode: 400);

    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    // 確保所有變數在 try 區塊內外都可存取
    int oldProductId = 0;
    int oldQuantity = 0;
    int warehouseId = 0;
    string productName = string.Empty;
    int quantityDifference = 0; 
    
    try
    {
        // 1. 取得舊明細的資訊 (產品編號, 數量, 倉庫編號, 產品名稱)
        string sqlSelectOld = @"
            SELECT d.產品編號 AS oldProductId, d.數量 AS oldQuantity, 
                   o.倉庫編號 AS warehouseId, p.名稱 AS productName
            FROM 出庫明細 d
            JOIN 出庫訂單 o ON d.出庫訂單編號 = o.出庫訂單編號
            JOIN 產品 p ON d.產品編號 = p.產品編號
            WHERE d.出庫明細編號 = @Did";
        using var cmdSelectOld = new SqlCommand(sqlSelectOld, conn, transaction);
        cmdSelectOld.Parameters.AddWithValue("@Did", detailId);

        using (var reader = await cmdSelectOld.ExecuteReaderAsync())
        {
            if (!reader.Read())
            {
                transaction.Rollback();
                return Results.NotFound(new { message = $"出庫明細 ID {detailId} 不存在" });
            }
            oldProductId = Convert.ToInt32(reader["oldProductId"]);
            oldQuantity = Convert.ToInt32(reader["oldQuantity"]);
            warehouseId = Convert.ToInt32(reader["warehouseId"]);
            productName = reader["productName"].ToString() ?? string.Empty;
        }

        int newQuantity = req.quantity;
        if (newQuantity < 0) throw new Exception("數量不能為負數。");

        // 庫存變動邏輯：
        // 差異 = 舊出庫數量 - 新出庫數量 (正數代表庫存增加，負數代表庫存減少)
        quantityDifference = oldQuantity - newQuantity;
        
        // 2. 調整庫存
        string sqlStockUpdate = "UPDATE 庫存 SET 數量 = 數量 + @Diff, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdStockUpdate = new SqlCommand(sqlStockUpdate, conn, transaction);
        cmdStockUpdate.Parameters.AddWithValue("@Diff", quantityDifference); 
        cmdStockUpdate.Parameters.AddWithValue("@Wid", warehouseId);
        cmdStockUpdate.Parameters.AddWithValue("@Pid", oldProductId);
        
        int stockAffected = await cmdStockUpdate.ExecuteNonQueryAsync();
        if (stockAffected == 0) throw new Exception("庫存調整失敗，產品可能已被刪除。");
        
        // 3. 更新明細記錄
        string sqlUpdateDetail = "UPDATE 出庫明細 SET 數量=@NewQty WHERE 出庫明細編號=@Did";
        using var cmdUpdateDetail = new SqlCommand(sqlUpdateDetail, conn, transaction);
        cmdUpdateDetail.Parameters.AddWithValue("@NewQty", newQuantity);
        cmdUpdateDetail.Parameters.AddWithValue("@Did", detailId);
        await cmdUpdateDetail.ExecuteNonQueryAsync();

        // 4. 稽核記錄 (MongoDB)
        await LogAudit(db, empId, "UPDATE_OUTBOUND_DETAIL", $"/api/outbound/detail/{detailId}", new 
        { 
            DetailId = detailId, 
            WarehouseId = warehouseId,
            ProductId = oldProductId,
            ProductName = productName,
            OldQuantity = oldQuantity,
            NewQuantity = newQuantity,
            StockChange = quantityDifference 
        });

        transaction.Commit();
        return Results.Json(new { success = true });
    }
    catch (SqlException sqlex)
    {
        transaction.Rollback();
        // 針對庫存不足的錯誤提供更友善的提示
        if (sqlex.Message.Contains("數量") && quantityDifference < 0) 
        {
             return Results.Problem($"更新出庫明細失敗：庫存數量不足以進行此變動。", statusCode: 400);
        }
        return Results.Problem($"更新出庫明細失敗 (SQL 錯誤): {sqlex.Message}", statusCode: 500);
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.Problem($"更新出庫明細失敗: {ex.Message}", statusCode: 500);
    }
});


// 刪除出庫明細並回溯庫存 (DELETE /api/outbound/detail/{detailId:int})
app.MapDelete("/api/outbound/detail/{detailId:int}", async (int detailId, IMongoDatabase db, [FromQuery] int empId = 0) =>
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var transaction = conn.BeginTransaction();

    try
    {
        // 1. 取得明細的資訊 
        string sqlSelect = @"SELECT d.產品編號, d.數量, o.倉庫編號, d.出庫訂單編號 
                              FROM 出庫明細 d JOIN 出庫訂單 o ON d.出庫訂單編號 = o.出庫訂單編號 WHERE d.出庫明細編號 = @Did";
        using var cmdSelect = new SqlCommand(sqlSelect, conn, transaction);
        cmdSelect.Parameters.AddWithValue("@Did", detailId);

        using var reader = await cmdSelect.ExecuteReaderAsync();
        if (!reader.Read())
        {
            transaction.Rollback();
            return Results.NotFound(new { message = $"出庫明細 ID {detailId} 不存在" });
        }

        int productId = Convert.ToInt32(reader["產品編號"]);
        int quantity = Convert.ToInt32(reader["數量"]); 
        int warehouseId = Convert.ToInt32(reader["倉庫編號"]);
        int outboundId = Convert.ToInt32(reader["出庫訂單編號"]); 
        reader.Close(); 

        // 2. 刪除明細
        string sqlDeleteDetail = "DELETE FROM 出庫明細 WHERE 出庫明細編號 = @Did";
        using var cmdDeleteDetail = new SqlCommand(sqlDeleteDetail, conn, transaction);
        cmdDeleteDetail.Parameters.AddWithValue("@Did", detailId);
        await cmdDeleteDetail.ExecuteNonQueryAsync();

        // 3. 回溯庫存 (增加數量)
        string sqlStockUpdate = "UPDATE 庫存 SET 數量 = 數量 + @Qty, 最後更新時間 = GETDATE() WHERE 倉庫編號 = @Wid AND 產品編號 = @Pid";
        using var cmdStockUpdate = new SqlCommand(sqlStockUpdate, conn, transaction);
        cmdStockUpdate.Parameters.AddWithValue("@Qty", quantity);
        cmdStockUpdate.Parameters.AddWithValue("@Wid", warehouseId);
        cmdStockUpdate.Parameters.AddWithValue("@Pid", productId);
        await cmdStockUpdate.ExecuteNonQueryAsync();

        // 4. 檢查是否還有其他明細，若無則刪除訂單
        string sqlCheckDetails = "SELECT COUNT(1) FROM 出庫明細 WHERE 出庫訂單編號 = @OId";
        using var cmdCheckDetails = new SqlCommand(sqlCheckDetails, conn, transaction);
        cmdCheckDetails.Parameters.AddWithValue("@OId", outboundId);
        int remainingDetails = Convert.ToInt32(await cmdCheckDetails.ExecuteScalarAsync());

        bool orderDeleted = false;
        if (remainingDetails == 0)
        {
            string sqlDeleteOrder = "DELETE FROM 出庫訂單 WHERE 出庫訂單編號 = @OId";
            using var cmdDeleteOrder = new SqlCommand(sqlDeleteOrder, conn, transaction);
            cmdDeleteOrder.Parameters.AddWithValue("@OId", outboundId);
            await cmdDeleteOrder.ExecuteNonQueryAsync();
            orderDeleted = true;
        }

        transaction.Commit();
        
        await LogAudit(db, empId, "OUTBOUND_DETAIL_DELETE", $"/api/outbound/detail/{detailId}", new { 
            detailId, 
            outboundId,
            warehouseId,
            productId, 
            quantityReverted = quantity, 
            orderDeleted 
        });

        return Results.Json(new { success = true, outboundId = orderDeleted ? outboundId : (int?)null });
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        return Results.Problem($"刪除出庫明細及回溯庫存失敗: {ex.Message}", statusCode: 500);
    }
});


// =========================================================================
// ===== 執行與 Class/Record 定義
// =========================================================================

app.UseDefaultFiles();
app.UseStaticFiles();
app.Run();


// 員工 (employees 集合)
public class Employee
{
    [BsonId]
    [BsonRepresentation(BsonType.Int32)] 
    public int EmpId { get; set; } 

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("contactInfo")]
    public string ContactInfo { get; set; } = string.Empty;
}

// 稽核記錄 (auditLogs 集合)
public class AuditLog
{
    public AuditLog()
    {
        Id = string.Empty; 
        ActionType = string.Empty;
        Endpoint = string.Empty;
        Data = new object(); 
    }

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } 

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; 

    [BsonElement("empId")]
    public int EmpId { get; set; }

    [BsonElement("actionType")]
    public string ActionType { get; set; } 

    [BsonElement("endpoint")]
    public string Endpoint { get; set; } 

    [BsonElement("data")]
    public object Data { get; set; } 
}


// 請求資料模型 (Records)
record LoginRequest(string EmpId);
record UpdateStockRequest(int quantity);

record InboundDetailRequest(int productId, int quantity); 
record OutboundDetailRequest(int productId, int quantity); 

record InboundRequest(string supplier, DateTime receivedDate); 
record OutboundRequest(DateTime shippedDate, string address);

// 新：尺寸型態已變更為可為 Null 的整數 (int?)
record InitializeStockRequest(string productName, int quantity, int? size, decimal? weight, decimal? price);

record UpdateInboundOrderRequest(string supplier, DateTime receivedDate);

// 入庫明細的 Payload 結構
record InboundDetailPayload(int productId, int quantity);

// 完整的入庫請求 (訂單 + 明細)
record FullInboundRequest(string supplier, DateTime receivedDate, List<InboundDetailPayload> details);

// 出庫明細的 Payload 結構
record OutboundDetailPayload(int productId, int quantity); 

// 完整的出庫請求 (訂單 + 明細)
record FullOutboundRequest(DateTime shippedDate, string address, List<OutboundDetailPayload> details);

// 用於更新入庫明細的欄位請求
record UpdateInboundDetailFieldsRequest(int productId, int quantity);