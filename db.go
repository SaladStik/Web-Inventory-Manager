package main

import (
    "sync"
    "time"
)

type DB struct {
    mu            sync.Mutex
    users         map[string]string
    products      []Product
    serialNumbers map[int][]SerialNumber
    nextProductID int
    nextSerialID  int
}

func NewDB() (*DB, error) {
    return &DB{
        users:         map[string]string{"admin": "admin"},
        products:      []Product{},
        serialNumbers: make(map[int][]SerialNumber),
        nextProductID: 1,
        nextSerialID:  1,
    }, nil
}

func (db *DB) ValidateUser(username, password string) (bool, error) {
    db.mu.Lock()
    defer db.mu.Unlock()
    stored, ok := db.users[username]
    return ok && stored == password, nil
}

type Product struct {
    ID                  int    `json:"id"`
    ModelNumber         string `json:"model_number"`
    Alias               string `json:"alias"`
    Type                string `json:"type"`
    Quantity            int    `json:"quantity"`
    Barcode             string `json:"barcode"`
    RequireSerialNumber bool   `json:"require_serial_number"`
    ImageURL            string `json:"image_url,omitempty"`
    Supplier            string `json:"supplier,omitempty"`
    SupplierLink        string `json:"supplier_link,omitempty"`
    MinStock            int    `json:"min_stock,omitempty"`
    Bin                 string `json:"bin,omitempty"`
}

func (db *DB) GetProductData() ([]Product, error) {
    db.mu.Lock()
    defer db.mu.Unlock()
    products := make([]Product, len(db.products))
    copy(products, db.products)
    return products, nil
}

type SerialNumber struct {
    ID           int        `json:"id"`
    ProductID    int        `json:"id_product"`
    LocationName string     `json:"location_name"`
    SerialNumber string     `json:"serial_number"`
    Date         *time.Time `json:"date,omitempty"`
    Note         string     `json:"note,omitempty"`
    TicketNum    string     `json:"ticket_num,omitempty"`
}

func (db *DB) GetSerialNumbers(productID int) ([]SerialNumber, error) {
    db.mu.Lock()
    defer db.mu.Unlock()
    list := db.serialNumbers[productID]
    res := make([]SerialNumber, len(list))
    copy(res, list)
    return res, nil
}

type ProductInput struct {
    ModelNumber         string `json:"modelNumber"`
    Alias               string `json:"alias"`
    Type                string `json:"type"`
    Quantity            int    `json:"quantity"`
    Barcode             string `json:"barcode"`
    RequireSerialNumber bool   `json:"requireSerialNumber"`
    ImageURL            string `json:"imageUrl"`
    Supplier            string `json:"supplier"`
    SupplierLink        string `json:"supplierLink"`
    MinStock            int    `json:"minStock"`
    Bin                 string `json:"bin"`
}

func (db *DB) AddProduct(p ProductInput) (int, error) {
    db.mu.Lock()
    defer db.mu.Unlock()
    id := db.nextProductID
    db.nextProductID++
    product := Product{
        ID:                  id,
        ModelNumber:         p.ModelNumber,
        Alias:               p.Alias,
        Type:                p.Type,
        Quantity:            p.Quantity,
        Barcode:             p.Barcode,
        RequireSerialNumber: p.RequireSerialNumber,
        ImageURL:            p.ImageURL,
        Supplier:            p.Supplier,
        SupplierLink:        p.SupplierLink,
        MinStock:            p.MinStock,
        Bin:                 p.Bin,
    }
    db.products = append(db.products, product)
    return id, nil
}
