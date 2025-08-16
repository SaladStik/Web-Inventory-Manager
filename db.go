package main

import (
	"fmt"
	"sync"
	"time"

	"golang.org/x/crypto/bcrypt"
)

type User struct {
	PasswordHash []byte
	IsAdmin      bool
}

type DB struct {
	mu            sync.Mutex
	users         map[string]User
	products      []Product
	serialNumbers map[int][]SerialNumber
	nextProductID int
	nextSerialID  int
}

func NewDB() (*DB, error) {
	hash, err := bcrypt.GenerateFromPassword([]byte("admin"), bcrypt.DefaultCost)
	if err != nil {
		return nil, err
	}
	return &DB{
		users:         map[string]User{"admin": {PasswordHash: hash, IsAdmin: true}},
		products:      []Product{},
		serialNumbers: make(map[int][]SerialNumber),
		nextProductID: 1,
		nextSerialID:  1,
	}, nil
}

func (db *DB) ValidateUser(username, password string) (bool, error) {
	db.mu.Lock()
	defer db.mu.Unlock()
	u, ok := db.users[username]
	if !ok {
		return false, nil
	}
	if err := bcrypt.CompareHashAndPassword(u.PasswordHash, []byte(password)); err != nil {
		return false, nil
	}
	return true, nil
}

func (db *DB) AddUser(username, password string, isAdmin bool) error {
	db.mu.Lock()
	defer db.mu.Unlock()
	if _, exists := db.users[username]; exists {
		return fmt.Errorf("user already exists")
	}
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return err
	}
	db.users[username] = User{PasswordHash: hash, IsAdmin: isAdmin}
	return nil
}

func (db *DB) DeleteUser(username string) error {
	db.mu.Lock()
	defer db.mu.Unlock()
	if _, exists := db.users[username]; !exists {
		return fmt.Errorf("user not found")
	}
	delete(db.users, username)
	return nil
}

func (db *DB) IsAdmin(username string) (bool, error) {
	db.mu.Lock()
	defer db.mu.Unlock()
	u, ok := db.users[username]
	if !ok {
		return false, fmt.Errorf("user not found")
	}
	return u.IsAdmin, nil
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
