-- 員工
CREATE TABLE 員工 (
    員工編號 INT PRIMARY KEY IDENTITY(1,1),
    姓名 NVARCHAR(50) NOT NULL,
    聯絡資訊 NVARCHAR(100)
);

-- 倉庫
CREATE TABLE 倉庫 (
    倉庫編號 INT PRIMARY KEY IDENTITY(1,1),
    員工編號 INT NOT NULL,
    名稱 NVARCHAR(50) NOT NULL,
    儲存空間 DECIMAL(10,2),
    FOREIGN KEY (員工編號) REFERENCES 員工(員工編號)
);

-- 產品
CREATE TABLE 產品 (
    產品編號 INT PRIMARY KEY IDENTITY(1,1),
    名稱 NVARCHAR(100) NOT NULL,
    尺寸 INT,
    重量 DECIMAL(10,2),
    價格 DECIMAL(10,2)
);

-- 庫存
CREATE TABLE 庫存 (
    產品編號 INT NOT NULL,
    倉庫編號 INT NOT NULL,
    最後更新時間 DATETIME DEFAULT GETDATE(),
    數量 INT DEFAULT 0,
    PRIMARY KEY (產品編號, 倉庫編號),
    FOREIGN KEY (產品編號) REFERENCES 產品(產品編號),
    FOREIGN KEY (倉庫編號) REFERENCES 倉庫(倉庫編號)
);

-- 入庫訂單
CREATE TABLE 入庫訂單 (
    入庫訂單編號 INT PRIMARY KEY IDENTITY(1,1),
    倉庫編號 INT NOT NULL,
    供應商名稱 NVARCHAR(100),
    收貨日期 DATE,
    FOREIGN KEY (倉庫編號) REFERENCES 倉庫(倉庫編號)
);

-- 入庫明細
CREATE TABLE 入庫明細 (
    入庫明細編號 INT PRIMARY KEY IDENTITY(1,1),
    入庫訂單編號 INT NOT NULL,
    產品編號 INT NOT NULL,
    數量 INT NOT NULL,
    FOREIGN KEY (入庫訂單編號) REFERENCES 入庫訂單(入庫訂單編號),
    FOREIGN KEY (產品編號) REFERENCES 產品(產品編號)
);

-- 出庫訂單
CREATE TABLE 出庫訂單 (
    出庫訂單編號 INT PRIMARY KEY IDENTITY(1,1),
    倉庫編號 INT NOT NULL,
    出貨日期 DATE,
    送達地址 NVARCHAR(200),
    FOREIGN KEY (倉庫編號) REFERENCES 倉庫(倉庫編號)
);

-- 出庫明細
CREATE TABLE 出庫明細 (
    出庫明細編號 INT PRIMARY KEY IDENTITY(1,1),
    出庫訂單編號 INT NOT NULL,
    產品編號 INT NOT NULL,
    數量 INT NOT NULL,
    FOREIGN KEY (出庫訂單編號) REFERENCES 出庫訂單(出庫訂單編號),
    FOREIGN KEY (產品編號) REFERENCES 產品(產品編號)
);
